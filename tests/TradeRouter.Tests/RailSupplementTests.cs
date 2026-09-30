using System.IO.Compression;
using System.Text.Json;
using AwesomeAssertions;
using TradeRouter.Locations;
using TradeRouter.Movements;
using Xunit;

namespace TradeRouter.Tests;

public class RailSupplementTests
{
    private sealed record Entry(string Code, string Name, string Source);

    private static List<Entry> LoadEntries()
    {
        using var stream = typeof(TradeRouterEngine).Assembly.GetManifestResourceStream("TradeRouter.Data.unlocode-rail-supplement.json")
            ?? throw new InvalidOperationException("Rail supplement is not embedded.");
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.EnumerateArray()
            .Select(item => new Entry(
                item.GetProperty("code").GetString()!,
                item.GetProperty("name").GetString()!,
                item.GetProperty("source").GetString()!))
            .ToList();
    }

    private static Dictionary<string, LocationFunctions> LoadPublishedFunctions()
    {
        using var raw = typeof(TradeRouterEngine).Assembly.GetManifestResourceStream("TradeRouter.Data.unlocode.json.gz")!;
        using var gzip = new GZipStream(raw, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gzip);
        var result = new Dictionary<string, LocationFunctions>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in doc.RootElement.EnumerateArray())
            result[item[0].GetString()!] = (LocationFunctions)item[4].GetInt32();
        return result;
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
            record.Coordinate.Should().NotBeNull($"{entry.Code} must resolve to a position for rail legs");
            entry.Source.Should().Contain("https://", $"{entry.Code} must cite a source");
        }
    }

    [Fact]
    public void Entries_AddOnlyTheRailFunctionWhereUnecePublishesNone()
    {
        var published = LoadPublishedFunctions();

        foreach (var entry in LoadEntries())
        {
            var original = published[entry.Code];
            (original & (LocationFunctions.RailTerminal | LocationFunctions.Multimodal))
                .Should().Be(LocationFunctions.None, $"{entry.Code} is supplemented only because UNECE records no rail function");
            TradeRouterEngine.Default.UnLocodes.GetByCode(entry.Code)!.Functions
                .Should().Be(original | LocationFunctions.RailTerminal, $"{entry.Code} must keep its published functions");
        }
    }

    [Fact]
    public void Movement_LosAngelesToChicagoByCodeRoutesOnTheRailNetwork()
    {
        var plan = MovementPlan
            .From(Waypoint.Station("USLAX"))
            .ThenTo(Waypoint.Station("USCHI"), TransportMode.Rail);

        var leg = TradeRoutes.CalculateMovement(plan).Legs.Single();

        leg.Feature.Properties.DistanceBasis.Should().Be("rail_network");
        leg.Length.Should().BeInRange(3_300.0, 3_800.0);
    }
}
