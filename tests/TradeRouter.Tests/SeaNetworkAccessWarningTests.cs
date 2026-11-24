using System.Globalization;
using System.Text.Json;
using AwesomeAssertions;
using TradeRouter.Common;
using TradeRouter.Movements;
using Xunit;

namespace TradeRouter.Tests;

public class SeaNetworkAccessWarningTests
{
    [Fact]
    public void Movement_WarnsWhenAPortLiesFarFromTheMaritimeNetwork()
    {
        // Manaus, 1,500 km up the Amazon, which the network does not follow.
        var leg = TradeRoutes.CalculateMovement("Port NLRTM to Port BRMAO Sea").Legs.Single();

        leg.Feature.Properties.DistanceBasis.Should().Be("maritime_network");
        leg.Feature.Properties.DistanceWarning.Should().Match("BRMAO is * km from the nearest node of the maritime network, beyond the 50 km NetworkAccessWarningKm. The route joins the network there in a straight line, counted in the length*");
        leg.Feature.Properties.DistanceWarning.Should().NotContain("NLRTM");
    }

    [Fact]
    public void Movement_NamesBothEndsWhenBothLieFarFromTheNetwork()
    {
        var leg = TradeRoutes.CalculateMovement("Port BRMAO to Port CUMOA Sea").Legs.Single();

        leg.Feature.Properties.DistanceWarning.Should().StartWith("BRMAO is ").And.Contain(" and CUMOA is ");
    }

    [Fact]
    public void Movement_DoesNotWarnBetweenWellConnectedPorts()
    {
        var leg = TradeRoutes.CalculateMovement("Port NLRTM to Port SGSIN Sea").Legs.Single();

        leg.Feature.Properties.DistanceWarning.Should().BeNull();
    }

    [Fact]
    public void Movement_ThresholdFollowsTheSeaOptions()
    {
        // Moa, Cuba, lies about 73 km from the nearest node.
        TradeRoutes.CalculateMovement("Port NLRTM to Port CUMOA Sea").Legs.Single()
            .Feature.Properties.DistanceWarning.Should().Contain("CUMOA is 7");

        var request = new MovementRequest
        {
            Legs = MovementParser.Parse("Port NLRTM to Port CUMOA Sea"),
            SeaOptions = new TradeRouterOptions { NetworkAccessWarningKm = 100.0 }
        };
        TradeRouterEngine.Default.CalculateMovement(request).Legs.Single()
            .Feature.Properties.DistanceWarning.Should().BeNull();
    }

    [Fact]
    public void Route_InfiniteThresholdTurnsTheWarningOff()
    {
        var manaus = new Coordinate(-60.0086, -3.144);
        var rotterdam = new Coordinate(4.0, 51.95);

        TradeRoutes.Calculate(manaus, rotterdam, new TradeRouterOptions { NetworkAccessWarningKm = double.PositiveInfinity })
            .Properties.DistanceWarning.Should().BeNull();
    }

    [Fact]
    public void Route_SaysTheLengthLeavesTheGapOutWithoutAppendedEnds()
    {
        var manaus = new Coordinate(-60.0086, -3.144);
        var rotterdam = new Coordinate(4.0, 51.95);

        var feature = TradeRoutes.Calculate(manaus, rotterdam);

        feature.Properties.DistanceWarning.Should().StartWith("The origin is ")
            .And.EndWith("The route does not run to the requested point, so its length leaves part or all of the gap out.");
    }

    [Fact]
    public void Route_CapitalisesTheDestinationWhenOnlyItLiesFar()
    {
        var rotterdam = new Coordinate(4.0, 51.95);
        var manaus = new Coordinate(-60.0086, -3.144);

        TradeRoutes.Calculate(rotterdam, manaus).Properties.DistanceWarning.Should().StartWith("The destination is ");
    }

    [Fact]
    public void Route_MeasuresFromTheRequestedPointWhenPortsAreIncluded()
    {
        var manaus = new Coordinate(-60.0086, -3.144);
        var rotterdam = new Coordinate(4.0, 51.95);

        var feature = TradeRoutes.Calculate(manaus, rotterdam, new TradeRouterOptions { IncludePorts = true });

        feature.Properties.DistanceWarning.Should().StartWith("The origin is ").And.NotContain("the destination");
    }

    [Fact]
    public void Route_SaysAStraightLineWhenBothEndsSnapToOneNode()
    {
        // Two points a few kilometres apart in central Australia share the same nearest node, far inland.
        var first = new Coordinate(133.88, -23.70);
        var second = new Coordinate(133.92, -23.70);

        var feature = TradeRoutes.Calculate(first, second);

        feature.Properties.DistanceWarning.Should().StartWith("The origin is ").And.Contain(" and the destination is ")
            .And.EndWith("Both ends snap to the same node, so the route is a straight line between them, which may cross land and is not a sailed track.");
    }

    [Fact]
    public void Route_WarningReadsTheSameInEveryCulture()
    {
        var manaus = new Coordinate(-60.0086, -3.144);
        var rotterdam = new Coordinate(4.0, 51.95);
        string? invariant = TradeRoutes.Calculate(manaus, rotterdam).Properties.DistanceWarning;

        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            TradeRoutes.Calculate(manaus, rotterdam).Properties.DistanceWarning.Should().Be(invariant).And.Contain(",");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Route_WarningIsSerialisedAndTheMeasurementsAreNot()
    {
        var leg = TradeRoutes.CalculateMovement("Port NLRTM to Port BRMAO Sea").Legs.Single();

        string json = JsonSerializer.Serialize(leg.Feature.Properties);

        json.Should().Contain("\"distance_warning\"").And.NotContain("NetworkAccessKm").And.NotContain("network_access");
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void Options_RejectANegativeOrUndefinedThreshold(double value)
    {
        var act = () => new TradeRouterOptions { NetworkAccessWarningKm = value }.Validate();

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(TradeRouterOptions.NetworkAccessWarningKm));
    }

    [Fact]
    public void Options_CloneKeepsTheThreshold()
    {
        new TradeRouterOptions { NetworkAccessWarningKm = 80.0 }.Clone().NetworkAccessWarningKm.Should().Be(80.0);
    }
}
