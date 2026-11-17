using System.Text.Json;
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
            record.Functions.Should().HaveFlag(LocationFunctions.Airport);
            entry.Source.Should().Contain("https://", $"{entry.Code} must cite a source");
        }
    }

    [Fact]
    public void Ningbo_TakesTheFunctionsUnece20251Lists()
    {
        var ningbo = TradeRouterEngine.Default.UnLocodes.GetByCode("CNNBO")!;

        ningbo.Functions.Should().Be(LocationFunctions.SeaPort | LocationFunctions.RailTerminal | LocationFunctions.Airport);
        ningbo.Coordinate.Should().NotBeNull();
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
        TradeRoutes.CalculateMovement("CNNBO to GBLHR Air").Legs.Single().Leg.Mode.Should().Be(TransportMode.Air);
    }
}
