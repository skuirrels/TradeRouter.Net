using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using TradeRouter.Common;

namespace TradeRouter.Movements;

/// <summary>
/// Routes road legs through the Google Maps Platform Routes API <c>computeRoutes</c> method.
/// Google's terms restrict caching of most Routes API content and require Routes results drawn on a map to be
/// shown on a Google Map; callers are responsible for complying with them when they store or display results.
/// </summary>
public sealed class GoogleRoutesRoadRouteProvider : IRoadRouteProvider
{
    /// <summary>Base URI of the public Google Routes API.</summary>
    public static Uri DefaultBaseUri { get; } = new("https://routes.googleapis.com/");

    private const string SourceName = "google-routes";
    private const string FieldMask = "routes.distanceMeters,routes.duration,routes.polyline.geoJsonLinestring";

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly Uri _requestUri;
    private readonly string _travelMode;
    private readonly double _maximumSnapDistanceMeters;

    /// <summary>
    /// Creates a Google Routes provider. The client is owned by the caller and should normally come from an
    /// IHttpClientFactory or another long-lived client factory. The API key is sent in the
    /// <c>X-Goog-Api-Key</c> header of each request and never appears in result messages.
    /// </summary>
    /// <param name="httpClient">Caller-owned HTTP client used to call the Routes API.</param>
    /// <param name="apiKey">Google Maps Platform API key with the Routes API enabled.</param>
    /// <param name="travelMode">
    /// Routes API travel mode, normally <c>DRIVE</c>. Other modes such as <c>TWO_WHEELER</c> or <c>TRUCK</c> may
    /// be billed at a higher SKU or need request attributes this adapter does not send.
    /// </param>
    /// <param name="maximumSnapDistanceMeters">
    /// Maximum distance from each endpoint to the start or end of the returned route. Google snaps endpoints to
    /// roads without a limit, so a larger gap is reported as <see cref="RoadRouteStatus.OutsideCoverage"/>.
    /// </param>
    /// <param name="baseUri">Base URI of the Routes API, or a proxy in front of it. Defaults to <see cref="DefaultBaseUri"/>.</param>
    public GoogleRoutesRoadRouteProvider(
        HttpClient httpClient,
        string apiKey,
        string travelMode = "DRIVE",
        double maximumSnapDistanceMeters = 10_000.0,
        Uri? baseUri = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("The Google Maps API key cannot be blank.", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(travelMode))
            throw new ArgumentException("The Google Routes travel mode cannot be blank.", nameof(travelMode));
        if (!double.IsFinite(maximumSnapDistanceMeters) || maximumSnapDistanceMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumSnapDistanceMeters),
                maximumSnapDistanceMeters,
                "The maximum Google Routes snap distance must be finite and positive.");
        }

        baseUri ??= DefaultBaseUri;
        if (!baseUri.IsAbsoluteUri)
            throw new ArgumentException("The Google Routes base URI must be absolute.", nameof(baseUri));
        if (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("The Google Routes base URI must use HTTP or HTTPS.", nameof(baseUri));
        if (!string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
            throw new ArgumentException("The Google Routes base URI cannot contain a query string or fragment.", nameof(baseUri));

        var normalizedBase = baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? baseUri
            : new Uri(baseUri.AbsoluteUri + '/', UriKind.Absolute);

        _httpClient = httpClient;
        _apiKey = apiKey.Trim();
        _requestUri = new Uri(normalizedBase, "directions/v2:computeRoutes");
        _travelMode = travelMode.Trim().ToUpperInvariant();
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
        var from = NormalizeLongitude(request.From.Coordinate);
        var to = NormalizeLongitude(request.To.Coordinate);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _requestUri)
        {
            Content = JsonContent.Create(new ComputeRoutesRequest
            {
                Origin = Waypoint(from),
                Destination = Waypoint(to),
                TravelMode = _travelMode,
                PolylineQuality = "HIGH_QUALITY",
                PolylineEncoding = "GEO_JSON_LINESTRING"
            })
        };
        httpRequest.Headers.Add("X-Goog-Api-Key", _apiKey);
        httpRequest.Headers.Add("X-Goog-FieldMask", FieldMask);

        try
        {
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Unavailable(await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false));

            var payload = await response.Content.ReadFromJsonAsync<ComputeRoutesResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            if (payload is null)
                return Unavailable("Google Routes returned an empty response.");

            var route = payload.Routes?.FirstOrDefault();
            if (route is null)
                return Failure(RoadRouteStatus.NoRoute, "Google Routes found no route between the endpoints.");

            // Protobuf JSON omits zero values, so a missing distance or duration on a returned route means zero.
            double distanceMeters = route.DistanceMeters ?? 0.0;
            double? durationSeconds = route.Duration is null ? 0.0 : ParseDuration(route.Duration);
            if (!double.IsFinite(distanceMeters) || distanceMeters < 0.0 ||
                durationSeconds is not { } seconds || seconds < 0.0 ||
                (distanceMeters > 0.0 && seconds <= 0.0))
            {
                return Unavailable("Google Routes returned no usable route summary.");
            }

            IReadOnlyList<Coordinate>? geometry = ReadGeometry(route.Polyline?.GeoJsonLinestring);
            if (geometry is null)
                return Unavailable("Google Routes returned no usable route geometry.");

            double startGapMeters = Haversine.DistanceKm(from, geometry[0]) * 1000.0;
            double endGapMeters = Haversine.DistanceKm(to, geometry[^1]) * 1000.0;
            if (startGapMeters > _maximumSnapDistanceMeters || endGapMeters > _maximumSnapDistanceMeters)
            {
                return Failure(
                    RoadRouteStatus.OutsideCoverage,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Google Routes snapped an endpoint {Math.Max(startGapMeters, endGapMeters):F0} m away, beyond the {_maximumSnapDistanceMeters:R} m limit."));
            }

            return new RoadRouteResult
            {
                Status = RoadRouteStatus.Success,
                DistanceKm = distanceMeters / 1000.0,
                DurationHours = seconds / 3600.0,
                Geometry = geometry,
                Source = SourceName,
                Profile = _travelMode
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable("The Google Routes request timed out.");
        }
        catch (HttpRequestException ex)
        {
            return Unavailable($"Google Routes was unavailable: {ex.Message}");
        }
        catch (System.Text.Json.JsonException ex)
        {
            return Unavailable($"Google Routes returned invalid JSON: {ex.Message}");
        }
    }

    private RoadRouteResult Failure(RoadRouteStatus status, string message) => new()
    {
        Status = status,
        Source = SourceName,
        Profile = _travelMode,
        Message = message
    };

    private RoadRouteResult Unavailable(string message) => Failure(RoadRouteStatus.Unavailable, message);

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string prefix = $"Google Routes returned HTTP {(int)response.StatusCode}";
        try
        {
            var error = (await response.Content.ReadFromJsonAsync<GoogleErrorResponse>(cancellationToken: cancellationToken).ConfigureAwait(false))?.Error;
            if (error is null)
                return prefix + ".";
            string status = string.IsNullOrWhiteSpace(error.Status) ? "" : " " + error.Status;
            string detail = string.IsNullOrWhiteSpace(error.Message) ? "" : ": " + error.Message;
            return prefix + status + detail;
        }
        catch (System.Text.Json.JsonException)
        {
            return prefix + ".";
        }
    }

    private static double? ParseDuration(string value)
    {
        if (!value.EndsWith('s'))
            return null;
        return double.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double seconds)
            ? seconds
            : null;
    }

    private static Coordinate NormalizeLongitude(Coordinate coordinate) =>
        coordinate.Longitude is >= -180.0 and <= 180.0
            ? coordinate
            : coordinate.WithNormalizedLongitude();

    private static GoogleWaypoint Waypoint(Coordinate coordinate) => new()
    {
        Location = new GoogleLocation
        {
            LatLng = new GoogleLatLng { Latitude = coordinate.Latitude, Longitude = coordinate.Longitude }
        }
    };

    private static IReadOnlyList<Coordinate>? ReadGeometry(GoogleLineString? lineString)
    {
        if (lineString?.Coordinates is not { Count: >= 2 })
            return null;

        var coordinates = new List<Coordinate>(lineString.Coordinates.Count);
        foreach (var position in lineString.Coordinates)
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

    private sealed class ComputeRoutesRequest
    {
        [JsonPropertyName("origin")]
        public required GoogleWaypoint Origin { get; init; }

        [JsonPropertyName("destination")]
        public required GoogleWaypoint Destination { get; init; }

        [JsonPropertyName("travelMode")]
        public required string TravelMode { get; init; }

        [JsonPropertyName("polylineQuality")]
        public required string PolylineQuality { get; init; }

        [JsonPropertyName("polylineEncoding")]
        public required string PolylineEncoding { get; init; }
    }

    private sealed class GoogleWaypoint
    {
        [JsonPropertyName("location")]
        public required GoogleLocation Location { get; init; }
    }

    private sealed class GoogleLocation
    {
        [JsonPropertyName("latLng")]
        public required GoogleLatLng LatLng { get; init; }
    }

    private sealed class GoogleLatLng
    {
        [JsonPropertyName("latitude")]
        public double Latitude { get; init; }

        [JsonPropertyName("longitude")]
        public double Longitude { get; init; }
    }

    private sealed class ComputeRoutesResponse
    {
        [JsonPropertyName("routes")]
        public List<GoogleRoute>? Routes { get; init; }
    }

    private sealed class GoogleRoute
    {
        [JsonPropertyName("distanceMeters")]
        public double? DistanceMeters { get; init; }

        [JsonPropertyName("duration")]
        public string? Duration { get; init; }

        [JsonPropertyName("polyline")]
        public GooglePolyline? Polyline { get; init; }
    }

    private sealed class GooglePolyline
    {
        [JsonPropertyName("geoJsonLinestring")]
        public GoogleLineString? GeoJsonLinestring { get; init; }
    }

    private sealed class GoogleLineString
    {
        [JsonPropertyName("coordinates")]
        public List<double[]>? Coordinates { get; init; }
    }

    private sealed class GoogleErrorResponse
    {
        [JsonPropertyName("error")]
        public GoogleError? Error { get; init; }
    }

    private sealed class GoogleError
    {
        [JsonPropertyName("message")]
        public string? Message { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }
    }
}
