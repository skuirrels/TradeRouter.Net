using System.Net;
using System.Text;
using FluentAssertions;
using TradeRouter.Common;
using TradeRouter.Graph;
using TradeRouter.Movements;
using TradeRouter.Ports;
using Xunit;

namespace TradeRouter.Tests;

public class RoadRoutingTests
{
    [Fact]
    public void Movement_RoadFallbackUsesLabelledCircuityEstimate()
    {
        var result = TradeRoutes.CalculateMovement("Pickup GBLGW to Airport GBLHR Road");

        var road = result.Legs.Single();
        road.Feature.Properties.DistanceBasis.Should().Be("circuity_estimate");
        road.Feature.Properties.DistanceSource.Should().Be("circuity-1.3");
        road.Feature.Properties.GeometryBasis.Should().Be("great_circle");
        road.Feature.Properties.DistanceWarning.Should().Contain("no road-network route");
        road.Feature.Properties.StraightLineLength.Should().BeInRange(39.0, 41.0);
        road.Length.Should().BeApproximately(
            road.Feature.Properties.StraightLineLength!.Value * CircuityRoadDistanceEstimator.DefaultFactor,
            1e-6);
        road.DurationHours.Should().BeApproximately(road.Length / 60.0, 1e-6);
    }

    [Fact]
    public void Movement_SuppliedRoadRouteOverridesEstimateAndDrivesEmissions()
    {
        var request = new MovementRequest
        {
            Legs = MovementParser.Parse("Pickup GBLGW to Airport GBLHR Road"),
            CargoTonnes = 20.0
        };
        request.RoadRouteOverrides[1] = new SuppliedRoadRoute
        {
            DistanceKm = 64.0,
            Source = "known-route"
        };

        var road = TradeRouterEngine.Default.CalculateMovement(request).Legs.Single();

        road.Length.Should().Be(64.0);
        road.DurationHours.Should().BeApproximately(64.0 / 60.0, 1e-9);
        road.Co2eKgPerTonne.Should().BeApproximately(5.888, 1e-9);
        road.Co2eKg.Should().BeApproximately(117.76, 1e-9);
        road.Feature.Properties.DistanceBasis.Should().Be("supplied");
        road.Feature.Properties.DistanceSource.Should().Be("known-route");
        road.Feature.Properties.DistanceWarning.Should().BeNull();
    }

    [Fact]
    public async Task MovementAsync_UsesConfiguredRoadNetworkProvider()
    {
        var provider = new StubRoadProvider(new RoadRouteResult
        {
            Status = RoadRouteStatus.Success,
            DistanceKm = 64.0,
            DurationHours = 0.9,
            Geometry =
            [
                new Coordinate(-0.19028, 51.14806),
                new Coordinate(-0.25, 51.25),
                new Coordinate(-0.45, 51.46667)
            ],
            Source = "osrm",
            Profile = "driving",
            DataVersion = "planet-2026-09"
        });
        var request = CreateProviderRequest(provider, RoadRoutingMode.RequireNetwork);

        var road = (await TradeRouterEngine.Default.CalculateMovementAsync(request)).Legs.Single();

        provider.Requests.Should().ContainSingle();
        road.Length.Should().Be(64.0);
        road.DurationHours.Should().Be(0.9);
        road.Feature.Geometry!.Coordinates.Should().HaveCount(3);
        road.Feature.Properties.DistanceBasis.Should().Be("road_network");
        road.Feature.Properties.DistanceSource.Should().Be("osrm");
        road.Feature.Properties.RoutingProfile.Should().Be("driving");
        road.Feature.Properties.RoutingDataVersion.Should().Be("planet-2026-09");
        road.Feature.Properties.DurationBasis.Should().Be("provider");
        road.Feature.Properties.GeometryBasis.Should().Be("road_network");
    }

    [Fact]
    public async Task MovementAsync_ProviderDurationDoesNotRequireFallbackRoadSpeed()
    {
        var provider = new StubRoadProvider(new RoadRouteResult
        {
            Status = RoadRouteStatus.Success,
            DistanceKm = 64.0,
            DurationHours = 0.9,
            Source = "stub"
        });
        var request = CreateProviderRequest(provider, RoadRoutingMode.RequireNetwork);
        request.SpeedsKmh.Remove(TransportMode.Road);

        var road = (await TradeRouterEngine.Default.CalculateMovementAsync(request)).Legs.Single();

        road.DurationHours.Should().Be(0.9);
        road.Feature.Properties.DurationBasis.Should().Be("provider");
    }

    [Theory]
    [InlineData(RoadRouteStatus.OutsideCoverage)]
    [InlineData(RoadRouteStatus.Unavailable)]
    public async Task MovementAsync_ProviderCoverageOrAvailabilityFailureFallsBackWithWarning(RoadRouteStatus status)
    {
        var provider = new StubRoadProvider(new RoadRouteResult
        {
            Status = status,
            Source = "osrm",
            Message = "provider detail"
        });
        var request = CreateProviderRequest(provider, RoadRoutingMode.PreferNetworkThenEstimate);

        var road = (await TradeRouterEngine.Default.CalculateMovementAsync(request)).Legs.Single();

        road.Feature.Properties.DistanceBasis.Should().Be("circuity_estimate");
        road.Feature.Properties.DistanceWarning.Should().StartWith($"osrm returned {status}: provider detail.");
    }

    [Fact]
    public async Task MovementAsync_NoRouteDoesNotSilentlyEstimate()
    {
        var provider = new StubRoadProvider(new RoadRouteResult
        {
            Status = RoadRouteStatus.NoRoute,
            Source = "osrm",
            Message = "disconnected network"
        });
        var request = CreateProviderRequest(provider, RoadRoutingMode.PreferNetworkThenEstimate);

        var act = async () => await TradeRouterEngine.Default.CalculateMovementAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*has no route*disconnected network*");
    }

    [Fact]
    public void MovementSync_RejectsProviderIo()
    {
        var request = CreateProviderRequest(
            new StubRoadProvider(new RoadRouteResult { Status = RoadRouteStatus.Unavailable, Source = "stub" }),
            RoadRoutingMode.PreferNetworkThenEstimate);

        var act = () => TradeRouterEngine.Default.CalculateMovement(request);

        act.Should().Throw<InvalidOperationException>().WithMessage("*CalculateMovementAsync*");
    }

    [Fact]
    public void MovementSync_SuppliedRouteTakesPrecedenceWithoutCallingConfiguredProvider()
    {
        var provider = new StubRoadProvider(new RoadRouteResult
        {
            Status = RoadRouteStatus.Success,
            DistanceKm = 70.0,
            Source = "stub"
        });
        var request = CreateProviderRequest(provider, RoadRoutingMode.PreferNetworkThenEstimate);
        request.RoadRouteOverrides[1] = new SuppliedRoadRoute { DistanceKm = 64.0, Source = "known-route" };

        var road = TradeRouterEngine.Default.CalculateMovement(request).Legs.Single();

        provider.Requests.Should().BeEmpty();
        road.Length.Should().Be(64.0);
        road.Feature.Properties.DistanceBasis.Should().Be("supplied");
    }

    [Fact]
    public async Task MovementAsync_RequireNetworkRejectsMissingProvider()
    {
        var request = new MovementRequest
        {
            Legs = MovementParser.Parse("Pickup GBLGW to Airport GBLHR Road"),
            RoadRoutingMode = RoadRoutingMode.RequireNetwork
        };

        var act = async () => await TradeRouterEngine.Default.CalculateMovementAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*requires a road-network provider*");
    }

    [Fact]
    public void MovementSync_EstimateOnlyDoesNotCallConfiguredProvider()
    {
        var provider = new StubRoadProvider(new RoadRouteResult
        {
            Status = RoadRouteStatus.Success,
            DistanceKm = 64.0,
            Source = "stub"
        });
        var request = CreateProviderRequest(provider, RoadRoutingMode.EstimateOnly);

        var road = TradeRouterEngine.Default.CalculateMovement(request).Legs.Single();

        provider.Requests.Should().BeEmpty();
        road.Feature.Properties.DistanceBasis.Should().Be("circuity_estimate");
    }

    [Fact]
    public async Task MovementAsync_ObservesCancellationWithoutCallingAProvider()
    {
        var request = new MovementRequest
        {
            Legs = MovementParser.Parse("Pickup GBLGW to Airport GBLHR Road")
        };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = async () => await TradeRouterEngine.Default.CalculateMovementAsync(request, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task MovementAsync_ProviderGeometryAlwaysRetainsExactMovementEndpoints()
    {
        var provider = new StubRoadProvider(new RoadRouteResult
        {
            Status = RoadRouteStatus.Success,
            DistanceKm = 64.0,
            DurationHours = 1.0,
            Geometry =
            [
                new Coordinate(-0.190279, 51.14806),
                new Coordinate(-0.25, 51.25),
                new Coordinate(-0.450001, 51.46667)
            ],
            Source = "stub"
        });
        var request = CreateProviderRequest(provider, RoadRoutingMode.RequireNetwork);

        var road = (await TradeRouterEngine.Default.CalculateMovementAsync(request)).Legs.Single();

        road.Feature.Geometry!.Coordinates.Should().HaveCount(5);
        road.Feature.Geometry.Coordinates[0][0].Should().BeApproximately(-0.19028, 1e-12);
        road.Feature.Geometry.Coordinates[0][1].Should().Be(51.14806);
        road.Feature.Geometry.Coordinates[^1][0].Should().BeApproximately(-0.45, 1e-12);
        road.Feature.Geometry.Coordinates[^1][1].Should().Be(51.46667);
    }

    [Fact]
    public async Task MovementAsync_ZeroLengthProviderGeometryRemainsAValidLineString()
    {
        var coordinate = new Coordinate(-0.45, 51.46667);
        var provider = new StubRoadProvider(new RoadRouteResult
        {
            Status = RoadRouteStatus.Success,
            DistanceKm = 0.0,
            DurationHours = 0.0,
            Geometry = [coordinate, coordinate, coordinate],
            Source = "stub"
        });
        var request = new MovementRequest
        {
            Legs = [new MovementLeg(Location.FromCoordinate(coordinate), Location.FromCoordinate(coordinate), TransportMode.Road)],
            RoadRouteProvider = provider,
            RoadRoutingMode = RoadRoutingMode.RequireNetwork
        };

        var road = (await TradeRouterEngine.Default.CalculateMovementAsync(request)).Legs.Single();

        road.Feature.Geometry!.Type.Should().Be("LineString");
        road.Feature.Geometry.Coordinates.Should().HaveCount(2);
        road.Feature.Geometry.Coordinates[0].Should().Equal(road.Feature.Geometry.Coordinates[1]);
    }

    [Fact]
    public async Task MovementAsync_RejectsBlankProviderSourceForFailureResult()
    {
        var request = CreateProviderRequest(
            new StubRoadProvider(new RoadRouteResult
            {
                Status = RoadRouteStatus.Unavailable,
                Source = " "
            }),
            RoadRoutingMode.PreferNetworkThenEstimate);

        var act = async () => await TradeRouterEngine.Default.CalculateMovementAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*blank source*");
    }

    [Fact]
    public async Task MovementAsync_RejectsZeroProviderDurationForNonZeroDistance()
    {
        var request = CreateProviderRequest(
            new StubRoadProvider(new RoadRouteResult
            {
                Status = RoadRouteStatus.Success,
                DistanceKm = 64.0,
                DurationHours = 0.0,
                Source = "stub"
            }),
            RoadRoutingMode.RequireNetwork);

        var act = async () => await TradeRouterEngine.Default.CalculateMovementAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*non-positive duration*");
    }

    [Fact]
    public async Task OsrmProvider_ParsesDistanceDurationGeometryAndBuildsInvariantRequest()
    {
        const string json = """
            {
              "code": "Ok",
              "routes": [{
                "distance": 64321.0,
                "duration": 3600.0,
                "geometry": {
                  "type": "LineString",
                  "coordinates": [[-0.19028, 51.14806], [-0.45, 51.46667]]
                }
              }]
            }
            """;
        var handler = new StubHttpHandler(json);
        using var client = new HttpClient(handler);
        var provider = new OsrmRoadRouteProvider(
            client,
            new Uri("http://127.0.0.1:5000"),
            dataVersion: "planet-test");
        var request = new RoadRouteRequest(
            1,
            new ResolvedLocation("GBLGW", "Gatwick", new Coordinate(-0.19028, 51.14806), null, "test"),
            new ResolvedLocation("GBLHR", "Heathrow", new Coordinate(-0.45, 51.46667), null, "test"));

        var result = await provider.RouteAsync(request);

        result.Status.Should().Be(RoadRouteStatus.Success);
        result.DistanceKm.Should().Be(64.321);
        result.DurationHours.Should().Be(1.0);
        result.Geometry.Should().HaveCount(2);
        result.DataVersion.Should().Be("planet-test");
        handler.RequestUri!.AbsoluteUri.Should().Contain(
            "/route/v1/driving/-0.19028,51.14806;-0.45,51.46667?overview=full&geometries=geojson&steps=false&radiuses=10000;10000");
    }

    [Fact]
    public async Task OsrmProvider_MapsNoSegmentToOutsideCoverage()
    {
        var handler = new StubHttpHandler("""{"code":"NoSegment","message":"Could not find a matching segment"}""", HttpStatusCode.BadRequest);
        using var client = new HttpClient(handler);
        var provider = new OsrmRoadRouteProvider(client, new Uri("http://127.0.0.1:5000"));
        var request = new RoadRouteRequest(
            1,
            new ResolvedLocation(null, null, new Coordinate(0, 0), null, "test"),
            new ResolvedLocation(null, null, new Coordinate(1, 1), null, "test"));

        var result = await provider.RouteAsync(request);

        result.Status.Should().Be(RoadRouteStatus.OutsideCoverage);
        result.Message.Should().Contain("matching segment");
    }

    [Theory]
    [InlineData("{\"code\":\"Ok\",\"routes\":[{\"duration\":120}]}")]
    [InlineData("{\"code\":\"Ok\",\"routes\":[{\"distance\":1000}]}")]
    public async Task OsrmProvider_MissingRouteSummaryValueIsUnavailable(string json)
    {
        var handler = new StubHttpHandler(json);
        using var client = new HttpClient(handler);
        var provider = new OsrmRoadRouteProvider(client, new Uri("http://127.0.0.1:5000"));

        var result = await provider.RouteAsync(CreateDirectProviderRequest());

        result.Status.Should().Be(RoadRouteStatus.Unavailable);
        result.Message.Should().Contain("no usable route summary");
    }

    [Fact]
    public async Task OsrmProvider_NormalizesLongitudesForTheHttpApi()
    {
        var handler = new StubHttpHandler("{\"code\":\"NoSegment\"}", HttpStatusCode.BadRequest);
        using var client = new HttpClient(handler);
        var provider = new OsrmRoadRouteProvider(client, new Uri("http://127.0.0.1:5000"));
        var request = new RoadRouteRequest(
            1,
            new ResolvedLocation(null, null, new Coordinate(190, 1), null, "test"),
            new ResolvedLocation(null, null, new Coordinate(-190, 2), null, "test"));

        await provider.RouteAsync(request);

        handler.RequestUri!.AbsolutePath.Should().Contain("/-170,1;170,2");
    }

    [Fact]
    public void OsrmProvider_RejectsNonHttpBaseUri()
    {
        using var client = new HttpClient();

        var act = () => new OsrmRoadRouteProvider(client, new Uri("file:///tmp/osrm"));

        act.Should().Throw<ArgumentException>().WithMessage("*HTTP or HTTPS*");
    }

    [Fact]
    public void SeaRoute_ReportsNetworkDistanceProvenance()
    {
        var route = TradeRoutes.Calculate("FRMRS", "ZACPT");

        route.Properties.DistanceBasis.Should().Be("maritime_network");
        route.Properties.DistanceSource.Should().Be("marnet");
        route.Properties.DurationBasis.Should().Be("assumed_speed");
        route.Properties.GeometryBasis.Should().Be("maritime_network");
    }

    [Fact]
    public void SeaRoute_CustomGraphDoesNotClaimMarnetProvenance()
    {
        var graph = new MaritimeGraph();
        graph.AddEdge(new Coordinate(0, 0), new Coordinate(1, 0));
        graph.BuildIndex();
        var engine = new TradeRouterEngine(graph, new PortDatabase([]));

        var route = engine.CalculateRoute(new Coordinate(0, 0), new Coordinate(1, 0));

        route.Properties.DistanceBasis.Should().Be("maritime_network");
        route.Properties.DistanceSource.Should().Be("custom_graph");
    }

    private static RoadRouteRequest CreateDirectProviderRequest() => new(
        1,
        new ResolvedLocation(null, null, new Coordinate(0, 0), null, "test"),
        new ResolvedLocation(null, null, new Coordinate(1, 1), null, "test"));

    private static MovementRequest CreateProviderRequest(IRoadRouteProvider provider, RoadRoutingMode mode) => new()
    {
        Legs = MovementParser.Parse("Pickup GBLGW to Airport GBLHR Road"),
        RoadRouteProvider = provider,
        RoadRoutingMode = mode
    };

    private sealed class StubRoadProvider(RoadRouteResult result) : IRoadRouteProvider
    {
        public List<RoadRouteRequest> Requests { get; } = [];

        public ValueTask<RoadRouteResult> RouteAsync(RoadRouteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class StubHttpHandler(
        string response,
        HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            });
        }
    }
}
