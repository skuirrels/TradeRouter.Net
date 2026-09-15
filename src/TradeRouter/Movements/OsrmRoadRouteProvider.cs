using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using TradeRouter.Common;

namespace TradeRouter.Movements;

/// <summary>Routes road legs through an OSRM HTTP service, including a locally hosted OSRM container.</summary>
public sealed class OsrmRoadRouteProvider : IRoadRouteProvider
{
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;
    private readonly string _profile;
    private readonly string? _dataVersion;
    private readonly double _maximumSnapDistanceMeters;

    /// <summary>
    /// Creates an OSRM provider. The client is owned by the caller and should normally come from an
    /// IHttpClientFactory or another long-lived client factory.
    /// </summary>
    public OsrmRoadRouteProvider(
        HttpClient httpClient,
        Uri baseUri,
        string profile = "driving",
        string? dataVersion = null,
        double maximumSnapDistanceMeters = 10_000.0)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!baseUri.IsAbsoluteUri)
            throw new ArgumentException("The OSRM base URI must be absolute.", nameof(baseUri));
        if (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("The OSRM base URI must use HTTP or HTTPS.", nameof(baseUri));
        if (!string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
            throw new ArgumentException("The OSRM base URI cannot contain a query string or fragment.", nameof(baseUri));
        if (string.IsNullOrWhiteSpace(profile))
            throw new ArgumentException("The OSRM profile cannot be blank.", nameof(profile));
        if (!double.IsFinite(maximumSnapDistanceMeters) || maximumSnapDistanceMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumSnapDistanceMeters),
                maximumSnapDistanceMeters,
                "The maximum OSRM snap distance must be finite and positive.");
        }

        _httpClient = httpClient;
        _baseUri = baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? baseUri
            : new Uri(baseUri.AbsoluteUri + '/', UriKind.Absolute);
        _profile = profile.Trim();
        _dataVersion = string.IsNullOrWhiteSpace(dataVersion) ? null : dataVersion.Trim();
        _maximumSnapDistanceMeters = maximumSnapDistanceMeters;
    }

    /// <inheritdoc />
    public async ValueTask<RoadRouteResult> RouteAsync(
        RoadRouteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Sequence < 1)
            throw new ArgumentOutOfRangeException(nameof(request), request.Sequence, "Road leg sequence must be at least one.");

        request.From.Coordinate.Validate();
        request.To.Coordinate.Validate();
        var from = NormalizeForOsrm(request.From.Coordinate);
        var to = NormalizeForOsrm(request.To.Coordinate);
        string coordinates = string.Create(
            CultureInfo.InvariantCulture,
            $"{from.Longitude:R},{from.Latitude:R};{to.Longitude:R},{to.Latitude:R}");
        var requestUri = new Uri(
            _baseUri,
            string.Create(
                CultureInfo.InvariantCulture,
                $"route/v1/{Uri.EscapeDataString(_profile)}/{coordinates}?overview=full&geometries=geojson&steps=false&radiuses={_maximumSnapDistanceMeters:R};{_maximumSnapDistanceMeters:R}"));

        try
        {
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
            var payload = await response.Content.ReadFromJsonAsync<OsrmResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            if (payload is null)
                return Unavailable("OSRM returned an empty response.");

            if (string.Equals(payload.Code, "NoSegment", StringComparison.OrdinalIgnoreCase))
                return Failure(RoadRouteStatus.OutsideCoverage, payload.Message ?? "OSRM could not match an endpoint to its loaded road network.");
            if (string.Equals(payload.Code, "NoRoute", StringComparison.OrdinalIgnoreCase))
                return Failure(RoadRouteStatus.NoRoute, payload.Message ?? "OSRM found no traversable route between the endpoints.");
            if (!response.IsSuccessStatusCode || !string.Equals(payload.Code, "Ok", StringComparison.OrdinalIgnoreCase))
                return Unavailable(payload.Message ?? $"OSRM returned HTTP {(int)response.StatusCode} with code '{payload.Code ?? "unknown"}'.");

            var route = payload.Routes?.FirstOrDefault();
            if (route is null || !route.Distance.HasValue || !double.IsFinite(route.Distance.Value) || route.Distance.Value < 0.0 ||
                !route.Duration.HasValue || !double.IsFinite(route.Duration.Value) || route.Duration.Value < 0.0 ||
                (route.Distance.Value > 0.0 && route.Duration.Value <= 0.0))
            {
                return Unavailable("OSRM returned no usable route summary.");
            }

            IReadOnlyList<Coordinate>? geometry = ReadGeometry(route.Geometry);
            return new RoadRouteResult
            {
                Status = RoadRouteStatus.Success,
                DistanceKm = route.Distance.Value / 1000.0,
                DurationHours = route.Duration.Value / 3600.0,
                Geometry = geometry,
                Source = "osrm",
                Profile = _profile,
                DataVersion = _dataVersion
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable("The OSRM request timed out.");
        }
        catch (HttpRequestException ex)
        {
            return Unavailable($"OSRM was unavailable: {ex.Message}");
        }
        catch (System.Text.Json.JsonException ex)
        {
            return Unavailable($"OSRM returned invalid JSON: {ex.Message}");
        }
    }

    private RoadRouteResult Failure(RoadRouteStatus status, string message) => new()
    {
        Status = status,
        Source = "osrm",
        Profile = _profile,
        DataVersion = _dataVersion,
        Message = message
    };

    private RoadRouteResult Unavailable(string message) => Failure(RoadRouteStatus.Unavailable, message);

    private static Coordinate NormalizeForOsrm(Coordinate coordinate) =>
        coordinate.Longitude is >= -180.0 and <= 180.0
            ? coordinate
            : coordinate.WithNormalizedLongitude();

    private static IReadOnlyList<Coordinate>? ReadGeometry(OsrmGeometry? geometry)
    {
        if (geometry?.Coordinates is not { Count: >= 2 })
            return null;

        var coordinates = new List<Coordinate>(geometry.Coordinates.Count);
        foreach (var position in geometry.Coordinates)
        {
            if (position is not { Length: >= 2 })
                return null;
            var coordinate = new Coordinate(position[0], position[1]);
            if (!double.IsFinite(coordinate.Longitude) || coordinate.Longitude is < -180.0 or > 180.0 ||
                !double.IsFinite(coordinate.Latitude) || coordinate.Latitude is < -90.0 or > 90.0)
            {
                return null;
            }
            coordinates.Add(coordinate);
        }
        return coordinates;
    }

    private sealed class OsrmResponse
    {
        [JsonPropertyName("code")]
        public string? Code { get; init; }

        [JsonPropertyName("message")]
        public string? Message { get; init; }

        [JsonPropertyName("routes")]
        public List<OsrmRoute>? Routes { get; init; }
    }

    private sealed class OsrmRoute
    {
        [JsonPropertyName("distance")]
        public double? Distance { get; init; }

        [JsonPropertyName("duration")]
        public double? Duration { get; init; }

        [JsonPropertyName("geometry")]
        public OsrmGeometry? Geometry { get; init; }
    }

    private sealed class OsrmGeometry
    {
        [JsonPropertyName("coordinates")]
        public List<double[]>? Coordinates { get; init; }
    }
}
