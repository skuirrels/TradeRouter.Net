using System.Diagnostics;
using System.Net;
using System.Text;
using AwesomeAssertions;
using TradeRouter.Common;
using TradeRouter.Movements;
using Xunit;

namespace TradeRouter.Tests;

public class OsrmRoadRouteProviderTests
{
    private static readonly Uri BaseUri = new("http://127.0.0.1:5000");

    [Fact]
    public async Task RouteAsync_ConnectionFailureIsUnavailable()
    {
        var provider = CreateProvider((_, _) => throw new HttpRequestException("connection refused"));

        var result = await provider.RouteAsync(CreateRequest());

        result.Status.Should().Be(RoadRouteStatus.Unavailable);
        result.Source.Should().Be("osrm");
        result.Message.Should().Be("OSRM was unavailable: connection refused");
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
        var provider = new OsrmRoadRouteProvider(client, BaseUri);

        var result = await provider.RouteAsync(CreateRequest());

        result.Status.Should().Be(RoadRouteStatus.Unavailable);
        result.Message.Should().Be("The OSRM request timed out.");
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
        result.Message.Should().StartWith("OSRM returned invalid JSON:");
    }

    [Fact]
    public async Task RouteAsync_NullJsonBodyIsUnavailable()
    {
        var provider = CreateProvider("null");

        var result = await provider.RouteAsync(CreateRequest());

        result.Status.Should().Be(RoadRouteStatus.Unavailable);
        result.Message.Should().Be("OSRM returned an empty response.");
    }

    [Theory]
    [InlineData("""{"code":"NoRoute","message":"Impossible route between points"}""", "Impossible route between points")]
    [InlineData("""{"code":"NoRoute"}""", "OSRM found no traversable route between the endpoints.")]
    public async Task RouteAsync_NoRouteIsReportedAsNoRoute(string json, string expectedMessage)
    {
        var provider = CreateProvider(json, HttpStatusCode.BadRequest);

        var result = await provider.RouteAsync(CreateRequest());

        result.Status.Should().Be(RoadRouteStatus.NoRoute);
        result.Message.Should().Be(expectedMessage);
    }

    [Fact]
    public async Task RouteAsync_NoSegmentWithoutMessageUsesDefaultReason()
    {
        var provider = CreateProvider("""{"code":"NoSegment"}""", HttpStatusCode.BadRequest);

        var result = await provider.RouteAsync(CreateRequest());

        result.Status.Should().Be(RoadRouteStatus.OutsideCoverage);
        result.Message.Should().Be("OSRM could not match an endpoint to its loaded road network.");
    }

    [Theory]
    [InlineData("""{"code":"Error"}""", HttpStatusCode.InternalServerError, "OSRM returned HTTP 500 with code 'Error'.")]
    [InlineData("{}", HttpStatusCode.ServiceUnavailable, "OSRM returned HTTP 503 with code 'unknown'.")]
    [InlineData("""{"code":"InvalidQuery","message":"Query string malformed"}""", HttpStatusCode.OK, "Query string malformed")]
    [InlineData("""{"code":"Ok","routes":[]}""", HttpStatusCode.InternalServerError, "OSRM returned HTTP 500 with code 'Ok'.")]
    public async Task RouteAsync_ErrorStatusOrCodeIsUnavailable(string json, HttpStatusCode status, string expectedMessage)
    {
        var provider = CreateProvider(json, status);

        var result = await provider.RouteAsync(CreateRequest());

        result.Status.Should().Be(RoadRouteStatus.Unavailable);
        result.Message.Should().Be(expectedMessage);
    }

    [Theory]
    [InlineData("""{"code":"Ok","routes":[]}""")]
    [InlineData("""{"code":"Ok"}""")]
    [InlineData("""{"code":"Ok","routes":[{"distance":-1,"duration":60}]}""")]
    [InlineData("""{"code":"Ok","routes":[{"distance":1000,"duration":-1}]}""")]
    [InlineData("""{"code":"Ok","routes":[{"distance":1000,"duration":0}]}""")]
    public async Task RouteAsync_UnusableRouteSummaryIsUnavailable(string json)
    {
        var provider = CreateProvider(json);

        var result = await provider.RouteAsync(CreateRequest());

        result.Status.Should().Be(RoadRouteStatus.Unavailable);
        result.Message.Should().Be("OSRM returned no usable route summary.");
    }

    [Fact]
    public async Task RouteAsync_ZeroLengthRouteSucceeds()
    {
        var provider = CreateProvider("""{"code":"Ok","routes":[{"distance":0,"duration":0}]}""");

        var result = await provider.RouteAsync(CreateRequest());

        result.Status.Should().Be(RoadRouteStatus.Success);
        result.DistanceKm.Should().Be(0.0);
        result.DurationHours.Should().Be(0.0);
        result.Geometry.Should().BeNull();
    }

    [Theory]
    [InlineData("[[0,0]]")]
    [InlineData("[[0,0],[1]]")]
    [InlineData("[[0,0],[1,95]]")]
    [InlineData("[[0,0],[181,1]]")]
    public async Task RouteAsync_UnusableGeometryIsDroppedButRouteSucceeds(string coordinates)
    {
        var provider = CreateProvider(
            $$$"""{"code":"Ok","routes":[{"distance":1000,"duration":60,"geometry":{"type":"LineString","coordinates":{{{coordinates}}}}}]}""");

        var result = await provider.RouteAsync(CreateRequest());

        result.Status.Should().Be(RoadRouteStatus.Success);
        result.DistanceKm.Should().Be(1.0);
        result.Geometry.Should().BeNull();
    }

    [Fact]
    public async Task RouteAsync_FailureResultsCarryProfileAndDataVersion()
    {
        var provider = CreateProvider("""{"code":"NoSegment"}""", HttpStatusCode.BadRequest, profile: "truck", dataVersion: "europe-2026-09");

        var result = await provider.RouteAsync(CreateRequest());

        result.Profile.Should().Be("truck");
        result.DataVersion.Should().Be("europe-2026-09");
    }

    [Fact]
    public async Task RouteAsync_BuildsUriFromBasePathTrimmedProfileAndSnapRadius()
    {
        Uri? requested = null;
        using var client = new HttpClient(new StubHandler((request, _) =>
        {
            requested = request.RequestUri;
            return Task.FromResult(JsonResponse("""{"code":"NoSegment"}""", HttpStatusCode.BadRequest));
        }));
        var provider = new OsrmRoadRouteProvider(client, new Uri("http://127.0.0.1:5000/osrm"), profile: " truck ", maximumSnapDistanceMeters: 250);

        await provider.RouteAsync(CreateRequest());

        requested!.AbsolutePath.Should().Be("/osrm/route/v1/truck/0,0;1,1");
        requested.Query.Should().EndWith("&radiuses=250;250");
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
    public async Task RouteAsync_RejectsInvalidEndpointBeforeCallingOsrm()
    {
        bool called = false;
        var provider = CreateProvider((_, _) =>
        {
            called = true;
            return Task.FromResult(JsonResponse("{}"));
        });
        var request = new RoadRouteRequest(
            1,
            new ResolvedLocation(null, null, new Coordinate(0, 91), null, "test"),
            new ResolvedLocation(null, null, new Coordinate(1, 1), null, "test"));

        var act = async () => await provider.RouteAsync(request);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Latitude*");
        called.Should().BeFalse();
    }

    [Theory]
    [InlineData("osrm/v1", "*must be absolute*")]
    [InlineData("http://127.0.0.1:5000/?profile=car", "*query string or fragment*")]
    [InlineData("http://127.0.0.1:5000/#section", "*query string or fragment*")]
    public void Constructor_RejectsUnusableBaseUri(string uri, string expectedMessage)
    {
        using var client = new HttpClient();

        var act = () => new OsrmRoadRouteProvider(client, new Uri(uri, UriKind.RelativeOrAbsolute));

        act.Should().Throw<ArgumentException>().WithMessage(expectedMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankProfile(string profile)
    {
        using var client = new HttpClient();

        var act = () => new OsrmRoadRouteProvider(client, BaseUri, profile);

        act.Should().Throw<ArgumentException>().WithMessage("*profile cannot be blank*");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Constructor_RejectsInvalidSnapDistance(double snapDistance)
    {
        using var client = new HttpClient();

        var act = () => new OsrmRoadRouteProvider(client, BaseUri, maximumSnapDistanceMeters: snapDistance);

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*snap distance*");
    }

    [Fact]
    public void Constructor_RejectsNullArguments()
    {
        using var client = new HttpClient();

        FluentActions.Invoking(() => new OsrmRoadRouteProvider(null!, BaseUri)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new OsrmRoadRouteProvider(client, null!)).Should().Throw<ArgumentNullException>();
    }

    private static RoadRouteRequest CreateRequest() => new(
        1,
        new ResolvedLocation(null, null, new Coordinate(0, 0), null, "test"),
        new ResolvedLocation(null, null, new Coordinate(1, 1), null, "test"));

    private static OsrmRoadRouteProvider CreateProvider(
        string json,
        HttpStatusCode status = HttpStatusCode.OK,
        string profile = "driving",
        string? dataVersion = null) =>
        new(new HttpClient(new StubHandler((_, _) => Task.FromResult(JsonResponse(json, status)))), BaseUri, profile, dataVersion);

    private static OsrmRoadRouteProvider CreateProvider(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new HttpClient(new StubHandler(send)), BaseUri);

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
