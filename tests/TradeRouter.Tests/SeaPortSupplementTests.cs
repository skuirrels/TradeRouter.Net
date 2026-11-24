using System.IO.Compression;
using System.Text.Json;
using AwesomeAssertions;
using TradeRouter.Locations;
using Xunit;

namespace TradeRouter.Tests;

public class SeaPortSupplementTests
{
    private sealed record Entry(string Code, string Name, string Source);

    private static List<Entry> LoadEntries()
    {
        using var stream = typeof(TradeRouterEngine).Assembly.GetManifestResourceStream("TradeRouter.Data.unlocode-seaport-supplement.json")
            ?? throw new InvalidOperationException("Sea-port supplement is not embedded.");
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

        entries.Should().HaveCount(14);
        entries.Select(entry => entry.Code).Should().OnlyHaveUniqueItems();
        foreach (var entry in entries)
        {
            var record = TradeRouterEngine.Default.UnLocodes.GetByCode(entry.Code);
            record.Should().NotBeNull($"{entry.Code} must be a UN/LOCODE");
            record!.Name.Should().Be(entry.Name, $"{entry.Code} must name its UN/LOCODE record");
            entry.Source.Should().NotBeNullOrWhiteSpace($"{entry.Code} must cite a source");
        }
    }

    [Fact]
    public void Entries_AddOnlyTheSeaPortFunctionWhereUnecePublishesNone()
    {
        var published = LoadPublishedFunctions();

        foreach (var entry in LoadEntries())
        {
            var original = published[entry.Code];
            (original & LocationFunctions.SeaPort)
                .Should().Be(LocationFunctions.None, $"{entry.Code} is supplemented only because UNECE records no sea-port function");
            TradeRouterEngine.Default.UnLocodes.GetByCode(entry.Code)!.Functions
                .Should().Be(original | LocationFunctions.SeaPort, $"{entry.Code} must keep its published functions");
        }
    }
}
