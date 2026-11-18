using System.IO.Compression;
using System.Text.Json;
using TradeRouter.Common;
using AwesomeAssertions;
using TradeRouter.Locations;
using TradeRouter.Movements;
using Xunit;

namespace TradeRouter.Tests;

public class AirportSupplementTests
{
    internal sealed record Entry(string Code, string Name, string Source);

    internal static List<Entry> LoadEntries()
    {
        using var stream = typeof(TradeRouterEngine).Assembly.GetManifestResourceStream("TradeRouter.Data.unlocode-airport-supplement.json")
            ?? throw new InvalidOperationException("Airport supplement is not embedded.");
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.EnumerateArray()
            .Select(item => new Entry(
                item.GetProperty("code").GetString()!,
                item.GetProperty("name").GetString()!,
                item.GetProperty("source").GetString()!))
            .ToList();
    }

    [Fact]
    public void Entries_AreUniqueSourcedAndNameTheirCode()
    {
        var entries = LoadEntries();

        entries.Should().NotBeEmpty();
        entries.Select(entry => entry.Code).Should().OnlyHaveUniqueItems();
        foreach (var entry in entries)
        {
            var record = TradeRouterEngine.Default.UnLocodes.GetByCode(entry.Code);
            record.Should().NotBeNull($"{entry.Code} must be a UN/LOCODE");
            record!.Name.Should().Be(entry.Name, $"{entry.Code} must name its UN/LOCODE record");
            entry.Source.Should().Contain("https://", $"{entry.Code} must cite a source");
        }
    }

    [Fact]
    public void Entries_AddOnlyTheAirportFunctionWherePublishedRowsLackIt()
    {
        using var raw = typeof(TradeRouterEngine).Assembly.GetManifestResourceStream("TradeRouter.Data.unlocode.json.gz")!;
        using var gzip = new GZipStream(raw, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gzip);
        var published = doc.RootElement.EnumerateArray()
            .ToDictionary(item => item[0].GetString()!, item => (LocationFunctions)item[4].GetInt32(), StringComparer.OrdinalIgnoreCase);
        var railTerminals = RailSupplementTests.LoadEntries().Select(entry => entry.Code).ToHashSet();

        foreach (var entry in LoadEntries())
        {
            var original = published[entry.Code];
            (original & LocationFunctions.Airport).Should().Be(LocationFunctions.None, $"{entry.Code} is supplemented only because its row records no airport");
            var rail = railTerminals.Contains(entry.Code) ? LocationFunctions.RailTerminal : LocationFunctions.None;
            TradeRouterEngine.Default.UnLocodes.GetByCode(entry.Code)!.Functions
                .Should().Be(original | LocationFunctions.Airport | rail, $"{entry.Code} must keep its published functions");
        }
    }

    [Fact]
    public void Ningbo_TakesTheFunctionsUnece20251Lists()
    {
        var ningbo = TradeRouterEngine.Default.UnLocodes.GetByCode("CNNBO")!;

        ningbo.Functions.Should().Be(LocationFunctions.SeaPort | LocationFunctions.RailTerminal | LocationFunctions.Airport);
        ningbo.Coordinate.Should().NotBeNull();
    }

    [Theory]
    [InlineData("FIHEL", "ports")]    // UNECE 2025-1 lists Helsinki as an airport; the port list keeps its position.
    [InlineData("TWKHH", "ports")]    // And Kaohsiung.
    [InlineData("ITMIL", "ports")]    // MIL is the IATA code for all Milan airports; the code stays in the city.
    public void Movement_CityCodeWithASupplementedAirportCanEndAnAirLeg(string code, string source)
    {
        TradeRouterEngine.Default.UnLocodes.GetByCode(code)!.IsAirport.Should().BeTrue();

        var leg = TradeRoutes.CalculateMovement($"{code} to DEFRA Air").Legs.Single();

        leg.From.Source.Should().Be(source);
        leg.Feature.Properties.DistanceBasis.Should().Be("great_circle");
        leg.Length.Should().BeGreaterThan(400.0);
    }

    [Fact]
    public void Movement_HelsinkiAndKaohsiungStillEndSeaLegsAtTheirPorts()
    {
        TradeRoutes.CalculateMovement("FIHEL to TWKHH Sea").Legs.Single()
            .Feature.Properties.DistanceBasis.Should().Be("maritime_network");
    }

    [Fact]
    public void Movement_NingboCanBeASeaPortStationAndAirport()
    {
        var plan = MovementPlan
            .From(Waypoint.Station("CNXIA"))
            .ThenTo(Waypoint.Station("CNNBO"), TransportMode.Rail)
            .ThenTo(Waypoint.Port("NLRTM"), TransportMode.Sea);

        var movement = TradeRoutes.CalculateMovement(plan);

        movement.Legs[0].Feature.Properties.DistanceBasis.Should().Be("great_circle");
        movement.Legs[1].Feature.Properties.DistanceBasis.Should().Be("maritime_network");
        var air = TradeRoutes.CalculateMovement("CNNBO to GBLHR Air").Legs.Single();
        air.From.Code.Should().Be("CNNBO");
        Haversine.DistanceKm(air.From.Coordinate, new Coordinate(121.55, 29.86667)).Should().BeLessThan(0.1, "Ningbo keeps its UNECE position");
        air.Feature.Properties.DistanceBasis.Should().Be("great_circle");
    }
}
