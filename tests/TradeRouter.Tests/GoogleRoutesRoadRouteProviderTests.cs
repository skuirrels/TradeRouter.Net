using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using TradeRouter.Common;
using TradeRouter.Movements;
using Xunit;

namespace TradeRouter.Tests;

public class GoogleRoutesRoadRouteProviderTests
{
    private const string ApiKey = "test-key-not-real";
    private const string Line = """{"type":"LineString","coordinates":[[0,0],[0.5,0.5],[1,1]]}""";

    [Fact]
    public async Task RouteAsync_SuccessMapsDistanceDurationAndGeometry()
    {
        var provider = CreateProvider($$$"""{"routes":[{"distanceMeters":157000,"duration":"7200s","polyline":{"geoJsonLinestring":{{{Line}}}}}]}""");

        var result = await provider.RouteAsync(CreateRequest());

        result.Status.Should().Be(RoadRouteStatus.Success);
        result.DistanceKm.Should().Be(157.0);
        result.DurationHours.Should().Be(2.0);
        result.Geometry.Should().Equal(new Coordinate(0, 0), new Coordinate(0.5, 0.5), new Coordinate(1, 1));
        result.Source.Should().Be("google-routes");
        result.Profile.Should().Be("DRIVE");
        result.DataVersion.Should().BeNull();
    }

    [Fact]
    public async Task RouteAsync_SendsKeyFieldMaskAndRequestBody()
    {
        HttpRequestMessage? sent = null;
        string? body = null;
        using var client = new HttpClient(new StubHandler(async (request, token) =>
        {
            sent = request;
            body = await request.Content!.ReadAsStringAsync(token);
            return JsonResponse("{}");
        }));
        var provider = new GoogleRoutesRoadRouteProvider(client, $" {ApiKey} ", travelMode: " drive ");

        await provider.RouteAsync(CreateRequest(from: new Coordinate(181, 10)));

        sent!.Method.Should().Be(HttpMethod.Post);
        sent.RequestUri.Should().Be(new Uri("https://routes.googleapis.com/directions/v2:computeRoutes"));
        sent.Headers.GetValues("X-Goog-Api-Key").Should().Equal(ApiKey);
        sent.Headers.GetValues("X-Goog-FieldMask").Should().Equal("routes.distanceMeters,routes.duration,routes.polyline.geoJsonLinestring");

        using var json = JsonDocument.Parse(body!);
        var root = json.RootElement;
        root.GetProperty("travelMode").GetString().Should().Be("DRIVE");
        root.GetProperty("polylineEncoding").GetString().Should().Be("GEO_JSON_LINESTRING");
        root.GetProperty("polylineQuality").GetString().Should().Be("HIGH_QUALITY");
        root.TryGetProperty("routingPreference", out _).Should().BeFalse("traffic-aware routing is billed at the Pro SKU");
        var origin = root.GetProperty("origin").GetProperty("location").GetProperty("latLng");
        origin.GetProperty("latitude").GetDouble().Should().Be(10);
        origin.GetProperty("longitude").GetDouble().Should().Be(-179);
        root.GetProperty("destination").GetProperty("location").GetProperty("latLng").GetProperty("longitude").GetDouble().Should().Be(1);
    }

    [Fact]
    public async Task RouteAsync_BuildsUriFromCustomBasePath()
    {
        Uri? requested = null;
        using var client = new HttpClient(new StubHandler((request, _) =>
        {
            requested = request.RequestUri;
            return Task.FromResult(JsonResponse("{}"));
        }));
        var provider = new GoogleRoutesRoadRouteProvider(client, ApiKey, baseUri: new Uri("https://proxy.example/google"));

        await provider.RouteAsync(CreateRequest());

        requested.Should().Be(new Uri("https://proxy.example/google/directions/v2:computeRoutes"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"routes":[]}""")]
    public async Task RouteAsync_NoRoutesIsNoRoute(string json)
    {
        var provider = CreateProvider(json);

        var result = await provider.RouteAsync(CreateRequest());

        result.Status.Should().Be(RoadRouteStatus.NoRoute);
        result.Message.Should().Be("Google Routes found no route between the endpoints.");
    }

    [Fact]
    public async Task RouteAsync_ZeroLengthRouteWithOmittedFieldsSucceeds()
    {
        var provider = CreateProvider(
            """{"routes":[{"polyline":{"geoJsonLinestring":{"type":"LineString","coordinates":[[0,0],[0,0]]}}}]}""");

        var result = await provider.RouteAsync(CreateRequest(to: new Coordinate(0, 0)));

        result.Status.Should().Be(RoadRouteStatus.Success);
        result.DistanceKm.Should().Be(0.0);
        result.DurationHours.Should().Be(0.0);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, """{"error":{"code":403,"message":"Routes API has not been used in project 1.","status":"PERMISSION_DENIED"}}""", "Google Routes returned HTTP 403 PERMISSION_DENIED: Routes API has not been used in project 1.")]
    [InlineData(HttpStatusCode.TooManyRequests, """{"error":{"code":429,"status":"RESOURCE_EXHAUSTED"}}""", "Google Routes returned HTTP 429 RESOURCE_EXHAUSTED")]
    [InlineData(HttpStatusCode.BadGateway, "<html>bad gateway</html>", "Google Routes returned HTTP 502.")]
    [InlineData(HttpStatusCode.InternalServerError, "{}", "Google Routes returned HTTP 500.")]
    public async Task RouteAsync_HttpErrorIsUnavailableWithoutLeakingKey(HttpStatusCode status, string body, string expectedMessage)
    {
        var provider = CreateProvider(body, status);

        var result = await provider.RouteAsync(CreateRequest());

        result.Status.Should().Be(RoadRouteStatus.Unavailable);
        result.Message.Should().Be(expectedMessage);
        result.Message.Should().NotContain(ApiKey);
    }

    [Fact]
    public async Task RouteAsync_ConnectionFailureIsUnavailable()
    {
        var provider = CreateProvider((_, _) => throw new HttpRequestException("connection refused"));

        var result = await provider.RouteAsync(CreateRequest());

        result.Status.Should().Be(RoadRouteStatus.Unavailable);
        result.Message.Should().Be("Google Routes was unavailable: connection refused");
    }

    [Fact]
    public async Task RouteAsync_HttpClientTimeoutIsUnavailable()
    {
        using var client = new HttpClient(new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new UnreachableException();
        }))
        {
            Timeout = TimeSpan.FromMilliseconds(100)
        };
        var provider = new GoogleRoutesRoadRouteProvider(client, ApiKey);

        var result = await provider.RouteAsync(CreateRequest());

        result.Status.Should().Be(RoadRouteStatus.Unavailable);
        result.Message.Should().Be("The Google Routes request timed out.");
    }

    [Fact]
    public async Task RouteAsync_CallerCancellationPropagates()
    {
        var provider = CreateProvider(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new UnreachableException();
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var act = async () => await provider.RouteAsync(CreateRequest(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RouteAsync_InvalidJsonIsUnavailable()
    {
        var provider = CreateProvider("this is not json");

        var result = await provider.RouteAsync(CreateRequest());

        result.Status.Should().Be(RoadRouteStatus.Unavailable);
        result.Message.Should().StartWith("Google Routes returned invalid JSON:");
    }

    [Fact]
    public async Task RouteAsync_NullJsonBodyIsUnavailable()
    {
        var provider = CreateProvider("null");

        var result = await provider.RouteAsync(CreateRequest());

        result.Status.Should().Be(RoadRouteStatus.Unavailable);
        result.Message.Should().Be("Google Routes returned an empty response.");
    }

    [Theory]
    [InlineData("\"distanceMeters\":-1,\"duration\":\"60s\"")]
    [InlineData("\"distanceMeters\":1000,\"duration\":\"-1s\"")]
    [InlineData("\"distanceMeters\":1000,\"duration\":\"60\"")]
    [InlineData("\"distanceMeters\":1000,\"duration\":\"0s\"")]
    [InlineData("\"distanceMeters\":1000")]
    public async Task RouteAsync_UnusableRouteSummaryIsUnavailable(string summary)
    {
        var provider = CreateProvider($$$"""{"routes":[{{{{summary}}},"polyline":{"geoJsonLinestring":{{{Line}}}}}]}""");

        var result = await provider.RouteAsync(CreateRequest());

        result.Status.Should().Be(RoadRouteStatus.Unavailable);
        result.Message.Should().Be("Google Routes returned no usable route summary.");
    }

    [Theory]
    [InlineData("""{"distanceMeters":1000,"duration":"60s"}""")]
    [InlineData("""{"distanceMeters":1000,"duration":"60s","polyline":{"geoJsonLinestring":{"type":"LineString","coordinates":[[0,0]]}}}""")]
    [InlineData("""{"distanceMeters":1000,"duration":"60s","polyline":{"geoJsonLinestring":{"type":"LineString","coordinates":[[0,0],[1,95]]}}}""")]
    public async Task RouteAsync_MissingOrUnusableGeometryIsUnavailable(string route)
    {
        var provider = CreateProvider($$"""{"routes":[{{route}}]}""");

        var result = await provider.RouteAsync(CreateRequest());

        result.Status.Should().Be(RoadRouteStatus.Unavailable);
        result.Message.Should().Be("Google Routes returned no usable route geometry.");
    }

    [Fact]
    public async Task RouteAsync_EndpointSnappedBeyondLimitIsOutsideCoverage()
    {
        var provider = CreateProvider(
            """{"routes":[{"distanceMeters":1000,"duration":"60s","polyline":{"geoJsonLinestring":{"type":"LineString","coordinates":[[0,0],[1,1.1]]}}}]}""",
            maximumSnapDistanceMeters: 5_000);

        var result = await provider.RouteAsync(CreateRequest());

        result.Status.Should().Be(RoadRouteStatus.OutsideCoverage);
        result.Message.Should().StartWith("Google Routes snapped an endpoint 11").And.EndWith("beyond the 5000 m limit.");
    }

    [Fact]
    public async Task RouteAsync_RejectsSequenceBelowOne()
    {
        var provider = CreateProvider("{}");
        var request = CreateRequest() with { Sequence = 0 };

        var act = async () => await provider.RouteAsync(request);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task RouteAsync_RejectsInvalidEndpointBeforeCallingGoogle()
    {
        bool called = false;
        var provider = CreateProvider((_, _) =>
        {
            called = true;
            return Task.FromResult(JsonResponse("{}"));
        });

        var act = async () => await provider.RouteAsync(CreateRequest(from: new Coordinate(0, 91)));

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Latitude*");
        called.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankApiKeyOrTravelMode(string value)
    {
        using var client = new HttpClient();

        FluentActions.Invoking(() => new GoogleRoutesRoadRouteProvider(client, value))
            .Should().Throw<ArgumentException>().WithMessage("*API key cannot be blank*");
        FluentActions.Invoking(() => new GoogleRoutesRoadRouteProvider(client, ApiKey, travelMode: value))
            .Should().Throw<ArgumentException>().WithMessage("*travel mode cannot be blank*");
    }

    [Theory]
    [InlineData("routes/v2", "*must be absolute*")]
    [InlineData("ftp://routes.example/", "*HTTP or HTTPS*")]
    [InlineData("https://routes.example/?key=1", "*query string or fragment*")]
    public void Constructor_RejectsUnusableBaseUri(string uri, string expectedMessage)
    {
        using var client = new HttpClient();

        var act = () => new GoogleRoutesRoadRouteProvider(client, ApiKey, baseUri: new Uri(uri, UriKind.RelativeOrAbsolute));

        act.Should().Throw<ArgumentException>().WithMessage(expectedMessage);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Constructor_RejectsInvalidSnapDistance(double snapDistance)
    {
        using var client = new HttpClient();

        var act = () => new GoogleRoutesRoadRouteProvider(client, ApiKey, maximumSnapDistanceMeters: snapDistance);

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*snap distance*");
    }

    [Fact]
    public void Constructor_RejectsNullArguments()
    {
        using var client = new HttpClient();

        FluentActions.Invoking(() => new GoogleRoutesRoadRouteProvider(null!, ApiKey)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new GoogleRoutesRoadRouteProvider(client, null!)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task CalculateMovementAsync_UsesGoogleRouteForRoadLeg()
    {
        var provider = CreateProvider($$$"""{"routes":[{"distanceMeters":157000,"duration":"7200s","polyline":{"geoJsonLinestring":{{{Line}}}}}]}""");
        var request = new MovementRequest
        {
            Legs =
            [
                new MovementLeg(Location.FromCoordinate(new Coordinate(0, 0)), Location.FromCoordinate(new Coordinate(1, 1)), TransportMode.Road)
            ],
            RoadRouteProvider = provider,
            RoadRoutingMode = RoadRoutingMode.RequireNetwork
        };

        var movement = await TradeRouterEngine.Default.CalculateMovementAsync(request);

        movement.Legs[0].Length.Should().Be(157.0);
        movement.Legs[0].Feature.Properties.DistanceSource.Should().Be("google-routes");
        movement.Legs[0].Feature.Properties.DistanceBasis.Should().Be("road_network");
    }

    private static RoadRouteRequest CreateRequest(Coordinate? from = null, Coordinate? to = null) => new(
        1,
        new ResolvedLocation(null, null, from ?? new Coordinate(0, 0), null, "test"),
        new ResolvedLocation(null, null, to ?? new Coordinate(1, 1), null, "test"));

    private static GoogleRoutesRoadRouteProvider CreateProvider(
        string json,
        HttpStatusCode status = HttpStatusCode.OK,
        double maximumSnapDistanceMeters = 10_000.0) =>
        new(
            new HttpClient(new StubHandler((_, _) => Task.FromResult(JsonResponse(json, status)))),
            ApiKey,
            maximumSnapDistanceMeters: maximumSnapDistanceMeters);

    private static GoogleRoutesRoadRouteProvider CreateProvider(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new HttpClient(new StubHandler(send)), ApiKey);

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
