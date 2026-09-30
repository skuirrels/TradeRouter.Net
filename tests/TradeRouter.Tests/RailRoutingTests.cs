using AwesomeAssertions;
using TradeRouter.Common;
using TradeRouter.Movements;
using Xunit;

namespace TradeRouter.Tests;

public class RailRoutingTests
{
    private static readonly Coordinate LosAngeles = new(-118.2437, 34.0522);
    private static readonly Coordinate Chicago = new(-87.6298, 41.8781);
    private static readonly Coordinate London = new(-0.1278, 51.5074);
    private static readonly Coordinate Felixstowe = new(1.3108, 51.9630);

    private static MovementRequest RailLeg(Coordinate from, string fromName, Coordinate to, string toName) => new()
    {
        Legs =
        [
            new MovementLeg(
                Location.FromCoordinate(from, fromName),
                Location.FromCoordinate(to, toName),
                TransportMode.Rail,
                LegKind.Main)
        ]
    };

    [Fact]
    public void Rail_LegInNorthAmerica_IsRoutedOnTheNarnMainLine()
    {
        var leg = TradeRouterEngine.Default.CalculateMovement(RailLeg(LosAngeles, "Los Angeles", Chicago, "Chicago")).Legs.Single();
        var properties = leg.Feature.Properties;

        properties.DistanceBasis.Should().Be("rail_network");
        properties.DistanceSource.Should().Be("narn");
        properties.GeometryBasis.Should().Be("rail_network");
        properties.DistanceWarning.Should().BeNull();
        // Los Angeles to Chicago is about 2,800 km in a straight line and about 3,500 km by BNSF's Southern Transcon.
        properties.StraightLineLength.Should().BeInRange(2_780.0, 2_820.0);
        leg.Length.Should().BeInRange(3_300.0, 3_800.0);
        properties.Railroads.Should().NotBeEmpty();
        properties.Railroads!.Should().Contain(mark => mark == "BNSF" || mark == "UP");
        leg.Feature.Geometry!.Positions.Count.Should().BeGreaterThan(100);
        leg.Feature.Geometry.Positions[0][0].Should().BeApproximately(LosAngeles.Longitude, 1e-9);
        leg.Feature.Geometry.Positions[^1][1].Should().BeApproximately(Chicago.Latitude, 1e-9);
        leg.DurationHours.Should().BeApproximately(leg.Length / 80.0, 1e-6);
    }

    [Fact]
    public void Rail_LegOutsideNorthAmerica_KeepsGreatCircleDistanceWithAWarning()
    {
        var leg = TradeRouterEngine.Default.CalculateMovement(RailLeg(London, "London", Felixstowe, "Felixstowe")).Legs.Single();

        leg.Feature.Properties.DistanceBasis.Should().Be("great_circle");
        leg.Feature.Properties.Railroads.Should().BeNull();
        // The warning names the end further from the network.
        leg.Feature.Properties.DistanceWarning.Should().Contain("Felixstowe").And.Contain("North American rail network");
        leg.Length.Should().BeInRange(100.0, 130.0);
    }

    [Fact]
    public void Rail_EndBeyondTheSnapDistance_KeepsGreatCircleDistance()
    {
        var request = RailLeg(LosAngeles, "Los Angeles", Chicago, "Chicago");
        request.RailNetworkSnapKm = 0.0;

        var leg = TradeRouterEngine.Default.CalculateMovement(request).Legs.Single();

        leg.Feature.Properties.DistanceBasis.Should().Be("great_circle");
        leg.Feature.Properties.DistanceWarning.Should().Contain("beyond the 0 km limit");
    }

    [Fact]
    public void Rail_GreatCircleOnly_DoesNotUseTheNetwork()
    {
        var request = RailLeg(LosAngeles, "Los Angeles", Chicago, "Chicago");
        request.RailRoutingMode = RailRoutingMode.GreatCircleOnly;

        var leg = TradeRouterEngine.Default.CalculateMovement(request).Legs.Single();

        leg.Feature.Properties.DistanceBasis.Should().Be("great_circle");
        leg.Feature.Properties.DistanceWarning.Should().BeNull();
        leg.Length.Should().BeInRange(2_780.0, 2_820.0);
    }

    [Fact]
    public void Rail_NegativeSnapDistance_IsRejected()
    {
        var request = RailLeg(LosAngeles, "Los Angeles", Chicago, "Chicago");
        request.RailNetworkSnapKm = -1.0;

        var act = () => TradeRouterEngine.Default.CalculateMovement(request);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(MovementRequest.RailNetworkSnapKm));
    }

    [Fact]
    public void Rail_SuppliedRouteOverridesGreatCircleAndDrivesEmissions()
    {
        var request = new MovementRequest
        {
            Legs = MovementParser.Parse("DEHAM to CZPRG Rail"),
            CargoTonnes = 20.0
        };
        request.RailRouteOverrides[1] = new SuppliedRoute { DistanceKm = 640.0, Source = "rail-planner" };

        var rail = TradeRouterEngine.Default.CalculateMovement(request).Legs.Single();

        rail.Length.Should().Be(640.0);
        rail.DurationHours.Should().BeApproximately(640.0 / 80.0, 1e-9);
        rail.Co2eKgPerTonne.Should().BeApproximately(17.92, 1e-9);
        rail.Co2eKg.Should().BeApproximately(358.4, 1e-9);
        rail.Feature.Properties.DistanceBasis.Should().Be("supplied");
        rail.Feature.Properties.DistanceSource.Should().Be("rail-planner");
        rail.Feature.Properties.DistanceWarning.Should().BeNull();
        rail.Feature.Properties.DurationBasis.Should().Be("assumed_speed");
        rail.Feature.Properties.GeometryBasis.Should().Be("great_circle");
        rail.Feature.Properties.StraightLineLength.Should().BeInRange(480.0, 500.0);
    }

    [Fact]
    public void Rail_SuppliedDurationAndGeometryAreUsedAsGiven()
    {
        var berlin = new Coordinate(13.4, 52.5);
        var request = new MovementRequest { Legs = MovementParser.Parse("DEHAM to CZPRG Rail") };
        request.RailRouteOverrides[1] = new SuppliedRoute
        {
            DistanceKm = 660.0,
            DurationHours = 14.0,
            Geometry = [new Coordinate(10.0, 53.55), berlin],
            Source = "rail-planner"
        };

        var rail = TradeRouterEngine.Default.CalculateMovement(request).Legs.Single();

        rail.DurationHours.Should().Be(14.0);
        rail.Feature.Properties.DurationBasis.Should().Be("supplied");
        rail.Feature.Properties.GeometryBasis.Should().Be("supplied");
        var positions = rail.Feature.Geometry!.Positions;
        positions.Should().HaveCount(4);
        positions[0][0].Should().BeApproximately(rail.From.Coordinate.Longitude, 1e-9);
        positions[2][0].Should().BeApproximately(berlin.Longitude, 1e-9);
        positions[2][1].Should().BeApproximately(berlin.Latitude, 1e-9);
        positions[^1][1].Should().BeApproximately(rail.To.Coordinate.Latitude, 1e-9);
    }

    [Fact]
    public void Rail_SuppliedRouteTakesPrecedenceOverTheNetwork()
    {
        var request = RailLeg(LosAngeles, "Los Angeles", Chicago, "Chicago");
        request.RailRouteOverrides[1] = new SuppliedRoute { DistanceKm = 3_600.0, Source = "rail-planner" };

        var leg = TradeRouterEngine.Default.CalculateMovement(request).Legs.Single();

        leg.Length.Should().Be(3_600.0);
        leg.Feature.Properties.DistanceBasis.Should().Be("supplied");
        leg.Feature.Properties.Railroads.Should().BeNull();
    }

    [Fact]
    public void Rail_OverrideAppliesOnlyToItsSequence()
    {
        var request = new MovementRequest { Legs = MovementParser.Parse("DEHAM to CZPRG Rail\nCZPRG to DEDUI Rail") };
        request.RailRouteOverrides[2] = new SuppliedRoute { DistanceKm = 700.0, Source = "rail-planner" };

        var legs = TradeRouterEngine.Default.CalculateMovement(request).Legs;

        legs[0].Feature.Properties.DistanceBasis.Should().Be("great_circle");
        legs[1].Feature.Properties.DistanceBasis.Should().Be("supplied");
        legs[1].Length.Should().Be(700.0);
    }

    [Fact]
    public void RailGraph_HoldsTheConnectedMainLine()
    {
        var graph = TradeRouterEngine.Default.RailGraph;

        graph.IsReadOnly.Should().BeTrue();
        graph.NodeCount.Should().BeGreaterThan(10_000);
        int losAngeles = graph.FindNearestNode(LosAngeles);
        Haversine.DistanceKm(LosAngeles, graph.GetCoordinate(losAngeles)).Should().BeLessThan(10.0);
    }
}
