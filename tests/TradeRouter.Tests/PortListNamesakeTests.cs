using System.IO.Compression;
using System.Text.Json;
using AwesomeAssertions;
using TradeRouter.Common;
using TradeRouter.Locations;
using TradeRouter.Movements;
using Xunit;

namespace TradeRouter.Tests;

public class PortListNamesakeTests
{
    [Theory]
    [InlineData("USPIT", -80.00, 40.44)]  // The port list's USPIT is Pilottown, Louisiana.
    [InlineData("USPHX", -112.07, 33.45)] // Phoenix, Louisiana, on the Mississippi.
    [InlineData("USBNA", -86.78, 36.17)]  // Nashville, Michigan.
    [InlineData("USCMH", -83.00, 39.96)]  // Columbus, Georgia.
    [InlineData("MXMTY", -100.32, 25.68)] // A Monterrey in Chiapas.
    [InlineData("CRSJO", -84.08, 9.93)]   // San Jose with its longitude sign flipped.
    [InlineData("PHOLO", 120.28, 14.83)]  // Olongapo, placed 116 km south in Batangas.
    public void Locate_PrefersUnLocodeOverAFarAwayNamesakeInThePortList(string code, double lon, double lat)
    {
        var resolved = TradeRoutes.Locate(code);

        resolved.Source.Should().Be("unlocode");
        resolved.Port.Should().BeNull();
        Haversine.DistanceKm(resolved.Coordinate, new Coordinate(lon, lat)).Should().BeLessThan(25.0);
    }

    [Fact]
    public void Locate_KeepsThePortListWithin50KmAndDropsItBeyond()
    {
        // Cabo Negro's harbour is 49 km from UN/LOCODE's position; Winnipeg's port-list entry is 50.2 km from the city.
        var caboNegro = TradeRoutes.Locate("CLCNX");
        caboNegro.Source.Should().Be("ports");
        Haversine.DistanceKm(caboNegro.Coordinate, TradeRouterEngine.Default.UnLocodes.GetByCode("CLCNX")!.Coordinate!.Value)
            .Should().BeInRange(45.0, 50.0);

        TradeRoutes.Locate("CAWNP").Source.Should().Be("unlocode");
    }

    [Theory]
    [InlineData("AEJEA")] // In UN/LOCODE without a coordinate.
    [InlineData("ALVLO")] // Not in UN/LOCODE at all.
    public void Locate_StillUsesThePortListWhenUnLocodeHasNoPosition(string code)
    {
        var resolved = TradeRoutes.Locate(code);

        resolved.Source.Should().Be("ports");
        resolved.Port.Should().NotBeNull();
    }

    [Fact]
    public void Locate_NeverTakesAPortListPositionMoreThan50KmFromUnLocode()
    {
        // Guards the rule across every port-list code, including any added by a later data update.
        var engine = TradeRouterEngine.Default;
        using var raw = typeof(TradeRouterEngine).Assembly.GetManifestResourceStream("TradeRouter.Data.ports.json.gz")!;
        using var gzip = new GZipStream(raw, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gzip);
        var codes = doc.RootElement.EnumerateArray().Select(port => port.GetProperty("port").GetString()!).Distinct().ToList();

        codes.Should().HaveCountGreaterThan(3000);
        foreach (var code in codes)
        {
            if (engine.UnLocodes.GetByCode(code)?.Coordinate is not { } listed)
                continue;
            var resolved = engine.Locate(code);
            if (resolved.Source == "ports")
                Haversine.DistanceKm(resolved.Coordinate, listed).Should().BeLessThanOrEqualTo(50.0, $"{code} must not resolve to a namesake");
        }
    }

    [Theory]
    [InlineData("ASPPG", -170.67, -14.28)] // UNECE puts Pago Pago in the northern hemisphere.
    [InlineData("CDBNW", 12.38, -5.98)]    // And Banana, Congo.
    [InlineData("ESSCI", -7.43, 43.70)]    // GeoNames linked San Ciprian to Aldan, 194 km south.
    [InlineData("USHPZ", -80.01, 35.96)]   // UNECE puts High Point, North Carolina, in West Virginia.
    [InlineData("GRRAF", 24.01, 38.02)]    // GeoNames linked a hamlet of Rafina in Thessaly, not the Attica ferry port.
    [InlineData("VELEC", -71.52, 10.70)]   // GeoNames linked a La Estacada in Apure, not the one on Lake Maracaibo.
    public void Corrections_PlaceCodesWhereTheirOwnRecordsPutThem(string code, double lon, double lat)
    {
        var record = TradeRouterEngine.Default.UnLocodes.GetByCode(code)!;

        Haversine.DistanceKm(record.Coordinate!.Value, new Coordinate(lon, lat)).Should().BeLessThan(2.0);
        record.CoordinateSource.Should().MatchRegex(@"^(World Port Index \d+ .+, linked by UN/LOCODE|GeoNames, .+, geonameId \d+.*); replaces ");
        TradeRoutes.Locate(code).Source.Should().Be("ports", "the corrected position agrees with the port list");
    }

    [Fact]
    public void Movement_SeaLegFromRafinaLeavesFromAttica()
    {
        var plan = MovementPlan
            .From(Waypoint.Port("GRRAF"))
            .ThenTo(Waypoint.Port("GRPIR"), TransportMode.Sea);

        var leg = TradeRoutes.CalculateMovement(plan).Legs.Single();

        leg.Length.Should().BeLessThan(200.0, "Rafina and Piraeus are both on the Attica coast");
    }

    [Fact]
    public void Movement_SeaLegFromOlongapoLeavesFromSubicBay()
    {
        var plan = MovementPlan
            .From(Waypoint.Port("PHOLO"))
            .ThenTo(Waypoint.Port("PHMNL"), TransportMode.Sea);

        var leg = TradeRoutes.CalculateMovement(plan).Legs.Single();

        leg.From.Source.Should().Be("unlocode");
        leg.Length.Should().BeInRange(80.0, 200.0);
    }

    [Fact]
    public void Movement_ChicagoToPhoenixRoutesToArizona()
    {
        var request = new MovementRequest
        {
            Legs = MovementPlan.From(Waypoint.Place("USCHI")).ThenTo(Waypoint.Place("USPHX"), TransportMode.Rail).Legs.ToList()
        };
        request.AdditionalLocationFunctions["USPHX"] = LocationFunctions.RailTerminal;

        var leg = TradeRoutes.CalculateMovement(request).Legs.Single();

        leg.Feature.Properties.DistanceBasis.Should().Be("rail_network");
        leg.Length.Should().BeInRange(2_700.0, 3_200.0);
    }

    [Fact]
    public void Movement_MonterreyToLaredoRoutesOnTheRailNetwork()
    {
        var plan = MovementPlan
            .From(Waypoint.Station("MXMTY"))
            .ThenTo(Waypoint.Station("USLRD"), TransportMode.Rail);

        var leg = TradeRoutes.CalculateMovement(plan).Legs.Single();

        leg.Feature.Properties.DistanceBasis.Should().Be("rail_network");
        leg.Feature.Properties.Railroads.Should().Contain("CPKC");
        leg.Length.Should().BeInRange(250.0, 400.0);
    }
}
