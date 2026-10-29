using System.IO.Compression;
using System.Text.Json;
using AwesomeAssertions;
using TradeRouter.Common;
using TradeRouter.Locations;
using Xunit;

namespace TradeRouter.Tests;

public class PortCoordinateFillTests
{
    private const string Marker = "Port fill, ";

    private sealed record Row(string Code, string Name, double Lon, double Lat, string Source);

    private static List<Row> LoadFillRows()
    {
        using var raw = typeof(TradeRouterEngine).Assembly.GetManifestResourceStream("TradeRouter.Data.unlocode-supplement.json.gz")!;
        using var gzip = new GZipStream(raw, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gzip);
        return doc.RootElement.EnumerateArray()
            .Where(item => item.GetProperty("source").GetString()!.StartsWith(Marker, StringComparison.Ordinal))
            .Select(item => new Row(
                item.GetProperty("code").GetString()!,
                item.GetProperty("name").GetString()!,
                item.GetProperty("lon").GetDouble(),
                item.GetProperty("lat").GetDouble(),
                item.GetProperty("source").GetString()!))
            .ToList();
    }

    private static Dictionary<string, JsonElement> LoadPublishedRows()
    {
        using var raw = typeof(TradeRouterEngine).Assembly.GetManifestResourceStream("TradeRouter.Data.unlocode.json.gz")!;
        using var gzip = new GZipStream(raw, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gzip);
        return doc.RootElement.EnumerateArray().ToDictionary(item => item[0].GetString()!, item => item.Clone());
    }

    [Fact]
    public void Rows_FillOnlySeaPortsThatNoOtherSourcePlaces()
    {
        var rows = LoadFillRows();
        var published = LoadPublishedRows();

        rows.Should().HaveCount(414);
        rows.Select(row => row.Code).Should().OnlyHaveUniqueItems();
        foreach (var row in rows)
        {
            var original = published[row.Code];
            original[2].ValueKind.Should().Be(JsonValueKind.Null, $"{row.Code} is filled only because UNECE publishes no coordinate");
            ((LocationFunctions)original[4].GetInt32() & LocationFunctions.SeaPort).Should().Be(LocationFunctions.SeaPort, $"{row.Code} must be a sea port");
            row.Name.Should().Be(original[1].GetString(), $"{row.Code} must name its UN/LOCODE record");
            TradeRouterEngine.Default.Ports.GetByCodeCandidates(row.Code).Should().BeEmpty($"the port list does not place {row.Code}");
            row.Source.Should().MatchRegex(@"^Port fill, (World Port Index \d+ |Wikidata Q\d+ |GeoNames .+ geonameId \d+,)", $"{row.Code} must cite its source record");
        }
    }

    [Fact]
    public void Rows_ResolveByCodeWithTheirSource()
    {
        var huelva = TradeRouterEngine.Default.UnLocodes.GetByCode("ESHUV")!;
        huelva.Coordinate.Should().Be(new Coordinate(-6.95, 37.25));
        huelva.CoordinateSource.Should().StartWith("Port fill, World Port Index 38220 Huelva, linked by UN/LOCODE");

        var resolved = TradeRoutes.Locate("ESHUV");
        resolved.Source.Should().Be("unlocode");
        resolved.Coordinate.Should().Be(huelva.Coordinate.Value);

        // A river port far from the coast is placed when its own record is confirmed in the code's province.
        var nanjing = TradeRouterEngine.Default.UnLocodes.GetByCode("CNNJG")!;
        nanjing.CoordinateSource.Should().StartWith("Port fill, Wikidata Q2092124 Port of Nanjing").And.Contain("in its UN/LOCODE subdivision");
    }

    [Theory]
    [InlineData("JPSAC")] // Sai, Shimane: GeoNames' only Sai is in Aomori.
    [InlineData("JPFAE")] // Fukae, Nagasaki: its GeoNames link is Fukue, already placed as JPFUJ.
    [InlineData("CNBLI")] // Bali: Wikidata's and GeoNames' Bali lie outside the code's province.
    [InlineData("PHTGL")] // Taguilon/Ozamis: only Ozamiz, the city it is filed under, matches a source.
    [InlineData("IDPAG")] // Pagatan, Kalimantan: GeoNames links Central Kalimantan, but South Kalimantan has a coastal Pagatan too.
    public void Namesakes_AreLeftUnplaced(string code)
    {
        TradeRouterEngine.Default.UnLocodes.GetByCode(code)!.Coordinate
            .Should().BeNull($"{code} has no position its own record supports");
    }
}
