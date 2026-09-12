using FluentAssertions;
using TradeRouter.Common;
using TradeRouter.Data;
using TradeRouter.Ports;
using Xunit;

namespace TradeRouter.Tests;

public class PortSelectionTests
{
    private static readonly Coordinate[] BelgiumPolygon =
    [
        new(2.5390082105781664, 51.12935353101912),
        new(2.658422, 50.796848),
        new(3.123252, 50.780363),
        new(4.1797570873041145, 50.02875404340671),
        new(4.88490130124042, 50.15316867323778),
        new(4.84369916664572, 49.816903537989774),
        new(5.367505366280012, 49.660158663860244),
        new(5.462583288120824, 49.501950558185754),
        new(5.838619986971207, 49.60595170762344),
        new(5.738394125385502, 49.96328692553026),
        new(6.412540492173664, 50.38040683350353),
        new(5.740262760313543, 50.81332955689459),
        new(5.823088307078621, 51.12406851908984),
        new(4.705997, 51.474099),
        new(3.830289, 51.620545),
        new(3.314971, 51.345781),
        new(2.5390082105781664, 51.12935353101912)
    ];

    [Fact]
    public void PortDatabase_LoadEmbedded_ShouldContainPorts()
    {
        var db = TradeRouterEngine.Default.Ports;
        db.Count.Should().BeGreaterThan(3000);

        var leHavre = db.GetByCode("FRLEH");
        leHavre.Should().NotBeNull();
        leHavre!.Name.Should().Be("Le Havre");

        var singapore = db.GetByCode("SGSIN");
        singapore.Should().NotBeNull();
        singapore!.Name.Should().Be("Singapore");
    }

    [Fact]
    public void QueryClosestPort_WithTerminalFilter_ShouldRespectTerminalFlag()
    {
        var db = TradeRouterEngine.Default.Ports;
        var paris = new Coordinate(2.333333, 48.866667);

        var anyPort = db.QueryClosestPort(paris, onlyTerminals: false);
        anyPort.Should().NotBeNull();

        var terminalPort = db.QueryClosestPort(paris, onlyTerminals: true);
        terminalPort.Should().NotBeNull();
        terminalPort!.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void AreaFeature_ContainsBrusselsPoint_ShouldBeTrue()
    {
        var brussels = new Coordinate(4.352066635732303, 50.85097556499499);
        var areaBE = new AreaFeature(BelgiumPolygon, "BE");

        areaBE.Contains(brussels).Should().BeTrue();

        // Paris is outside Belgium
        var paris = new Coordinate(2.333333, 48.866667);
        areaBE.Contains(paris).Should().BeFalse();
    }

    [Fact]
    public void AreaFeature_MultiRouteGeneration_ReturnsRoutesForBothPreferredPorts()
    {
        var firstPort = new PortProps("FRLEH", 200);
        var secondPort = new PortProps("BEANR", 250);

        var areaBE = new AreaFeature(BelgiumPolygon, "BE", [firstPort, secondPort]);

        var portParam = new PortParameters
        {
            PortsInAreasFrom = [areaBE]
        };

        var brussels = new Coordinate(4.352066635732303, 50.85097556499499);
        var tokyo = new Coordinate(139.67917395748216, 35.77846652689662);

        var options = new TradeRouterOptions
        {
            AppendOriginDestination = false,
            IncludePorts = true,
            PortParameters = portParam
        };

        var routes = TradeRoutes.CalculateRoutes(brussels, tokyo, options);
        routes.Count.Should().Be(2);

        var originPortCodes = routes.Select(r => r.Properties.PortOrigin?.PortCode).ToList();
        originPortCodes.Should().Contain(["FRLEH", "BEANR"]);
    }

    [Fact]
    public void CalculateRoute_ByPortCodes_PopulatesPortMetadata()
    {
        var route = TradeRoutes.Calculate("FRLEH", "SGSIN");

        route.Should().NotBeNull();
        route.Properties.PortOrigin.Should().NotBeNull();
        route.Properties.PortOrigin!.PortCode.Should().Be("FRLEH");
        route.Properties.PortOrigin.Name.Should().Be("Le Havre");

        route.Properties.PortDest.Should().NotBeNull();
        route.Properties.PortDest!.PortCode.Should().Be("SGSIN");
        route.Properties.PortDest.Name.Should().Be("Singapore");

        route.Properties.Length.Should().BeGreaterThan(10000.0);
    }
}
