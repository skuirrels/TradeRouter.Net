using AwesomeAssertions;
using TradeRouter.Common;
using TradeRouter.Movements;
using Xunit;

namespace TradeRouter.Tests;

public class MovementValidationTests
{
    private static readonly Coordinate Inland = new(1.0, 52.0);
    private static readonly Coordinate Quay = new(1.35, 51.96);
    private static readonly Coordinate FarQuay = new(4.5, 51.9);
    private static readonly Coordinate Warehouse = new(4.8, 52.1);

    private static readonly Dictionary<string, Action<MovementRequest>> InvalidRequests = new()
    {
        ["no legs"] = r => r.Legs.Clear(),
        ["null emissions"] = r => r.Emissions = null!,
        ["null estimator"] = r => r.RoadDistanceEstimator = null!,
        ["unknown routing mode"] = r => r.RoadRoutingMode = (RoadRoutingMode)99,
        ["negative dwell"] = r => r.PortDwellHours = -1.0,
        ["NaN dwell"] = r => r.PortDwellHours = double.NaN,
        ["unsupported speed mode"] = r => r.SpeedsKmh[(TransportMode)99] = 10.0,
        ["zero road speed"] = r => r.SpeedsKmh[TransportMode.Road] = 0.0,
        ["infinite air speed"] = r => r.SpeedsKmh[TransportMode.Air] = double.PositiveInfinity,
        ["unknown leg mode"] = r => r.Legs[1] = new MovementLeg(Location.FromCoordinate(Quay), Location.FromCoordinate(FarQuay), (TransportMode)99),
        ["unknown leg kind"] = r => r.Legs[1] = new MovementLeg(Location.FromCoordinate(Quay), Location.FromCoordinate(FarQuay), TransportMode.Sea, (LegKind)99),
        ["unknown waypoint kind"] = r => r.Legs[1] = new MovementLeg(Location.FromCoordinate(Quay), Location.FromCoordinate(FarQuay), TransportMode.Sea, fromKind: (WaypointKind)99),
        ["pickup not first"] = r => r.Legs[1] = new MovementLeg(Location.FromCoordinate(Quay), Location.FromCoordinate(FarQuay), TransportMode.Sea, LegKind.Pickup),
        ["delivery not last"] = r => r.Legs[1] = new MovementLeg(Location.FromCoordinate(Quay), Location.FromCoordinate(FarQuay), TransportMode.Sea, LegKind.Delivery),
        ["blank coordinate key"] = r => r.Coordinates[" "] = Inland,
        ["invalid coordinate value"] = r => r.Coordinates["XXAAA"] = new Coordinate(0, 95),
        ["override sequence zero"] = r => r.RoadRouteOverrides[0] = new SuppliedRoute { DistanceKm = 10 },
        ["override sequence past end"] = r => r.RoadRouteOverrides[4] = new SuppliedRoute { DistanceKm = 10 },
        ["override on sea leg"] = r => r.RoadRouteOverrides[2] = new SuppliedRoute { DistanceKm = 10 },
        ["null override"] = r => r.RoadRouteOverrides[1] = null!,
        ["NaN override distance"] = r => r.RoadRouteOverrides[1] = new SuppliedRoute { DistanceKm = double.NaN },
        ["negative override distance"] = r => r.RoadRouteOverrides[1] = new SuppliedRoute { DistanceKm = -1 },
        ["negative override duration"] = r => r.RoadRouteOverrides[1] = new SuppliedRoute { DistanceKm = 10, DurationHours = -1 },
        ["zero override duration"] = r => r.RoadRouteOverrides[1] = new SuppliedRoute { DistanceKm = 10, DurationHours = 0 },
        ["blank override source"] = r => r.RoadRouteOverrides[1] = new SuppliedRoute { DistanceKm = 10, Source = " " },
        ["single-point override geometry"] = r => r.RoadRouteOverrides[1] = new SuppliedRoute { DistanceKm = 10, Geometry = [Inland] },
        ["invalid override geometry"] = r => r.RoadRouteOverrides[1] = new SuppliedRoute { DistanceKm = 10, Geometry = [Inland, new Coordinate(1, 95)] }
    };

    [Theory]
    [InlineData("no legs", "A movement needs at least one leg.*")]
    [InlineData("null emissions", "Movement emissions cannot be null.*")]
    [InlineData("null estimator", "Movement road-distance estimator cannot be null.*")]
    [InlineData("unknown routing mode", "Movement road-routing mode is unknown.*")]
    [InlineData("negative dwell", "Port dwell must be finite and non-negative.*")]
    [InlineData("NaN dwell", "Port dwell must be finite and non-negative.*")]
    [InlineData("unsupported speed mode", "SpeedsKmh contains unsupported mode '99'.*")]
    [InlineData("zero road speed", "Speed for Road must be finite and positive.*")]
    [InlineData("infinite air speed", "Speed for Air must be finite and positive.*")]
    [InlineData("unknown leg mode", "Movement leg 2 has an unknown transport mode.*")]
    [InlineData("unknown leg kind", "Movement leg 2 has an unknown leg or waypoint kind.*")]
    [InlineData("unknown waypoint kind", "Movement leg 2 has an unknown leg or waypoint kind.*")]
    [InlineData("pickup not first", "Pickup leg 2 must be the first leg.*")]
    [InlineData("delivery not last", "Delivery leg 2 must be the last leg.*")]
    [InlineData("blank coordinate key", "Movement coordinate keys cannot be blank.*")]
    [InlineData("invalid coordinate value", "Latitude must be between -90 and 90 degrees.*")]
    [InlineData("override sequence zero", "Road route override sequence 0 is outside the movement's leg range.*")]
    [InlineData("override sequence past end", "Road route override sequence 4 is outside the movement's leg range.*")]
    [InlineData("override on sea leg", "Road route override sequence 2 does not refer to a road leg.*")]
    [InlineData("null override", "Road route override sequence 1 is null.*")]
    [InlineData("NaN override distance", "Road route override sequence 1 has an invalid distance.*")]
    [InlineData("negative override distance", "Road route override sequence 1 has an invalid distance.*")]
    [InlineData("negative override duration", "Road route override sequence 1 has an invalid duration.*")]
    [InlineData("zero override duration", "Road route override sequence 1 has a non-positive duration for a non-zero distance.*")]
    [InlineData("blank override source", "Road route override sequence 1 has a blank source.*")]
    [InlineData("single-point override geometry", "Road route override sequence 1 geometry needs at least two positions.*")]
    [InlineData("invalid override geometry", "Latitude must be between -90 and 90 degrees.*")]
    public void CalculateMovement_RejectsInvalidRequest(string invalidRequest, string expectedMessage)
    {
        var request = CreateValidRequest();
        InvalidRequests[invalidRequest](request);

        var act = () => TradeRouterEngine.Default.CalculateMovement(request);

        act.Should().Throw<ArgumentException>().WithMessage(expectedMessage);
    }

    [Fact]
    public void CalculateMovement_ValidBaseRequestSucceeds()
    {
        var result = TradeRouterEngine.Default.CalculateMovement(CreateValidRequest());

        result.Legs.Should().HaveCount(3);
    }

    [Fact]
    public void CalculateMovement_AcceptsZeroDistanceOverrideWithZeroDuration()
    {
        var request = CreateValidRequest();
        request.RoadRouteOverrides[1] = new SuppliedRoute { DistanceKm = 0.0, DurationHours = 0.0 };

        var road = TradeRouterEngine.Default.CalculateMovement(request).Legs[0];

        road.Length.Should().Be(0.0);
        road.DurationHours.Should().Be(0.0);
    }

    [Theory]
    [InlineData(RoadRouteStatus.Unavailable, "provider detail", "Road leg 1 (GBLGW to GBLHR) could not be routed by stub: provider detail")]
    [InlineData(RoadRouteStatus.OutsideCoverage, null, "Road leg 1 (GBLGW to GBLHR) could not be routed by stub: OutsideCoverage")]
    public async Task CalculateMovementAsync_RequireNetworkRejectsProviderFailure(RoadRouteStatus status, string? message, string expected)
    {
        var request = CreateProviderRequest(new RoadRouteResult { Status = status, Source = "stub", Message = message }, RoadRoutingMode.RequireNetwork);

        var act = async () => await TradeRouterEngine.Default.CalculateMovementAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(expected);
    }

    [Fact]
    public async Task CalculateMovementAsync_RejectsUnknownProviderStatus()
    {
        var request = CreateProviderRequest(new RoadRouteResult { Status = (RoadRouteStatus)99, Source = "stub" }, RoadRoutingMode.PreferNetworkThenEstimate);

        var act = async () => await TradeRouterEngine.Default.CalculateMovementAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Road-route provider returned unknown status '99'.");
    }

    [Fact]
    public async Task CalculateMovementAsync_RejectsNullProviderResult()
    {
        var request = CreateProviderRequest(null!, RoadRoutingMode.PreferNetworkThenEstimate);

        var act = async () => await TradeRouterEngine.Default.CalculateMovementAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Road-route provider returned null for leg 1.");
    }

    public static TheoryData<RoadRouteResult, string> InvalidProviderSuccesses => new()
    {
        { new RoadRouteResult { Status = RoadRouteStatus.Success, Source = "stub" }, "*invalid distance for leg 1." },
        { new RoadRouteResult { Status = RoadRouteStatus.Success, Source = "stub", DistanceKm = double.NaN }, "*invalid distance for leg 1." },
        { new RoadRouteResult { Status = RoadRouteStatus.Success, Source = "stub", DistanceKm = -1 }, "*invalid distance for leg 1." },
        { new RoadRouteResult { Status = RoadRouteStatus.Success, Source = "stub", DistanceKm = 50, DurationHours = double.NaN }, "*invalid duration for leg 1." },
        { new RoadRouteResult { Status = RoadRouteStatus.Success, Source = "stub", DistanceKm = 50, DurationHours = -1 }, "*invalid duration for leg 1." },
        { new RoadRouteResult { Status = RoadRouteStatus.Success, Source = "stub", DistanceKm = 50, DurationHours = 1, Geometry = [new Coordinate(-0.19, 51.15)] }, "*fewer than two geometry positions for leg 1." }
    };

    [Theory]
    [MemberData(nameof(InvalidProviderSuccesses))]
    public async Task CalculateMovementAsync_RejectsInvalidProviderSuccess(RoadRouteResult result, string expected)
    {
        var request = CreateProviderRequest(result, RoadRoutingMode.RequireNetwork);

        var act = async () => await TradeRouterEngine.Default.CalculateMovementAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(expected);
    }

    public static TheoryData<string, string> InvalidEstimates => new()
    {
        { "null", "Road-distance estimator returned null for leg 1." },
        { "blank model", "Road-distance estimator returned a blank model for leg 1." },
        { "blank warning", "Road-distance estimator returned a blank warning for leg 1." },
        { "NaN distance", "Road-distance estimator 'stub' returned NaN km for a * km great-circle lower bound." },
        { "below straight line", "Road-distance estimator 'stub' returned 1 km for a * km great-circle lower bound." }
    };

    [Theory]
    [MemberData(nameof(InvalidEstimates))]
    public void CalculateMovement_RejectsInvalidEstimatorOutput(string estimate, string expected)
    {
        RoadDistanceEstimate? output = estimate switch
        {
            "null" => null,
            "blank model" => new RoadDistanceEstimate(100, " ", "warning"),
            "blank warning" => new RoadDistanceEstimate(100, "stub", ""),
            "NaN distance" => new RoadDistanceEstimate(double.NaN, "stub", "warning"),
            _ => new RoadDistanceEstimate(1, "stub", "warning")
        };
        var request = new MovementRequest
        {
            Legs = MovementParser.Parse("Pickup GBLGW to Airport GBLHR Road"),
            RoadDistanceEstimator = new StubEstimator(output)
        };

        var act = () => TradeRouterEngine.Default.CalculateMovement(request);

        act.Should().Throw<InvalidOperationException>().WithMessage(expected);
    }

    [Fact]
    public async Task CalculateMovementAsync_ProviderFallbackWarningPrefixesEstimatorWarning()
    {
        var request = CreateProviderRequest(
            new RoadRouteResult { Status = RoadRouteStatus.Unavailable, Source = "stub" },
            RoadRoutingMode.PreferNetworkThenEstimate);
        request.RoadDistanceEstimator = new StubEstimator(new RoadDistanceEstimate(100, "stub-model", "Stub warning."));

        var road = (await TradeRouterEngine.Default.CalculateMovementAsync(request)).Legs.Single();

        road.Feature.Properties.DistanceWarning.Should().Be("stub returned Unavailable: no reason supplied. Stub warning.");
        road.Feature.Properties.DistanceSource.Should().Be("stub-model");
    }

    [Theory]
    [InlineData("GBLHR to GBFXT Sea", "Leg 1 uses Sea, but its from location GBLHR is recorded as airport rather than a compatible sea terminal. If the UN/LOCODE record is incomplete*")]
    [InlineData("GBFXT to GBLHR Air", "Leg 1 uses Air, but its from location GBFXT is recorded as sea port rather than a compatible air terminal. If the UN/LOCODE record is incomplete*")]
    [InlineData("GBFXT to DEDUI Rail", "Leg 1 uses Rail, but its from location GBFXT is recorded as sea port rather than a compatible rail terminal. If the UN/LOCODE record is incomplete*")]
    [InlineData("DEDUI to GBLHR Rail", "Leg 1 uses Rail, but its to location GBLHR is recorded as airport rather than a compatible rail terminal. If the UN/LOCODE record is incomplete*")]
    public void CalculateMovement_RejectsLocationWithoutFunctionForMode(string legs, string expected)
    {
        var act = () => TradeRoutes.CalculateMovement(legs);

        act.Should().Throw<ArgumentException>().WithMessage(expected);
    }

    [Theory]
    [InlineData("Station GBFXT to Station DEDUI Rail", "*GBFXT is declared as Station, but UN/LOCODE records sea port. If the UN/LOCODE record is incomplete*")]
    [InlineData("Terminal GBLHR to Terminal DEDUI Road", "*GBLHR is declared as Terminal, but UN/LOCODE records airport. If the UN/LOCODE record is incomplete*")]
    [InlineData("Depot GBLGW to Depot DEDUI Road", "*GBLGW is declared as Depot, but UN/LOCODE records airport. If the UN/LOCODE record is incomplete*")]
    [InlineData("Depot DEDUI to Depot GBFXT Road", "Leg 1 to location GBFXT is declared as Depot, but UN/LOCODE records sea port. If the UN/LOCODE record is incomplete*")]
    public void CalculateMovement_RejectsWaypointKindTheLocationDoesNotServe(string legs, string expected)
    {
        var act = () => TradeRoutes.CalculateMovement(legs);

        act.Should().Throw<ArgumentException>().WithMessage(expected);
    }

    [Theory]
    [InlineData("Station DEHAM to Station DEDUI Rail")]
    [InlineData("Terminal GBFXT to Terminal DEDUI Road")]
    [InlineData("Depot DEHAM to Depot DEDUI Road")]
    [InlineData("Terminal NLRTM to Depot DEDUI Rail")]
    public void CalculateMovement_AcceptsWaypointKindTheLocationServes(string legs)
    {
        var result = TradeRoutes.CalculateMovement(legs);

        result.Legs.Should().ContainSingle().Which.Length.Should().BeGreaterThan(0.0);
    }

    [Fact]
    public void CalculateMovement_TypedPlanWaypointsAreValidatedLikeParsedKinds()
    {
        var accepted = MovementPlan
            .From(Waypoint.Station("DEHAM"))
            .ThenTo(Waypoint.Depot("DEDUI"), TransportMode.Rail)
            .ThenTo(Waypoint.Terminal("NLRTM"), TransportMode.Road);
        var rejected = MovementPlan
            .From(Waypoint.Station("GBFXT"))
            .ThenTo(Waypoint.Station("DEDUI"), TransportMode.Rail);

        TradeRoutes.CalculateMovement(accepted).Legs.Should().HaveCount(2);
        FluentActions.Invoking(() => TradeRoutes.CalculateMovement(rejected))
            .Should().Throw<ArgumentException>().WithMessage("*GBFXT is declared as Station*");
    }

    [Fact]
    public void CalculateMovement_CoordinateOnlyLocationsHaveNoFunctionToCheck()
    {
        var request = new MovementRequest
        {
            Legs = [new MovementLeg(Location.FromCoordinate(Inland), Location.FromCoordinate(Warehouse), TransportMode.Rail)]
        };

        TradeRouterEngine.Default.CalculateMovement(request).Legs.Should().ContainSingle();
    }

    private static MovementRequest CreateValidRequest() => new()
    {
        Legs =
        [
            new MovementLeg(Location.FromCoordinate(Inland), Location.FromCoordinate(Quay), TransportMode.Road, LegKind.Pickup),
            new MovementLeg(Location.FromCoordinate(Quay), Location.FromCoordinate(FarQuay), TransportMode.Sea),
            new MovementLeg(Location.FromCoordinate(FarQuay), Location.FromCoordinate(Warehouse), TransportMode.Road, LegKind.Delivery)
        ]
    };

    private static MovementRequest CreateProviderRequest(RoadRouteResult result, RoadRoutingMode mode) => new()
    {
        Legs = MovementParser.Parse("Pickup GBLGW to Airport GBLHR Road"),
        RoadRouteProvider = new StubRoadProvider(result),
        RoadRoutingMode = mode
    };

    private sealed class StubRoadProvider(RoadRouteResult result) : IRoadRouteProvider
    {
        public ValueTask<RoadRouteResult> RouteAsync(RoadRouteRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(result);
    }

    private sealed class StubEstimator(RoadDistanceEstimate? estimate) : IRoadDistanceEstimator
    {
        public RoadDistanceEstimate Estimate(RoadDistanceEstimateRequest request) => estimate!;
    }
}
