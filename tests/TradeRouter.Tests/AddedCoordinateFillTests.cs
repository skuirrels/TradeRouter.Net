using System.IO.Compression;
using System.Text.Json;
using AwesomeAssertions;
using TradeRouter.Common;
using Xunit;

namespace TradeRouter.Tests;

/// <summary>
/// Supplement rows written by tools/fill-added-unlocode-coordinates.py, and the hand-reviewed rows added with them.
/// </summary>
public class AddedCoordinateFillTests
{
    private const string Marker = "Added fill, ";

    private static List<JsonElement> LoadSupplement()
    {
        using var raw = typeof(TradeRouterEngine).Assembly.GetManifestResourceStream("TradeRouter.Data.unlocode-supplement.json.gz")!;
        using var gzip = new GZipStream(raw, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gzip);
        return doc.RootElement.EnumerateArray().Select(item => item.Clone()).ToList();
    }

    private static Dictionary<string, JsonElement> LoadPublishedRows()
    {
        using var raw = typeof(TradeRouterEngine).Assembly.GetManifestResourceStream("TradeRouter.Data.unlocode.json.gz")!;
        using var gzip = new GZipStream(raw, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gzip);
        return doc.RootElement.EnumerateArray().ToDictionary(item => item[0].GetString()!, item => item.Clone());
    }

    [Fact]
    public void Rows_FillOnlyCodesNoOtherSourcePlacesAndLeavePortListCodesAlone()
    {
        var supplement = LoadSupplement();
        var rows = supplement.Where(item => item.GetProperty("source").GetString()!.StartsWith(Marker, StringComparison.Ordinal)).ToList();
        var published = LoadPublishedRows();
        var aliases = Data.EmbeddedResources.LoadPortCodeAliases();

        rows.Should().HaveCount(1_485);
        supplement.Select(item => item.GetProperty("code").GetString()).Should().OnlyHaveUniqueItems();
        foreach (var row in rows)
        {
            string code = row.GetProperty("code").GetString()!;
            published[code][2].ValueKind.Should().Be(JsonValueKind.Null, $"{code} is filled only because UNECE publishes no coordinate");
            row.GetProperty("name").GetString().Should().Be(published[code][1].GetString(), $"{code} must name its UN/LOCODE record");
            TradeRouterEngine.Default.Ports.GetByCodeCandidates(code).Should().BeEmpty($"the fill leaves the port list's {code} alone");
            aliases.Keys.Should().NotContain(code);
            aliases.Values.Should().NotContain(code);
            row.GetProperty("source").GetString().Should().MatchRegex(@"^Added fill, (GeoNames .+, geonameId \d+|Wikidata Q\d+ .+; GeoNames .+, geonameId \d+, [\d.]+ km away|OurAirports \S+ .+, IATA [A-Z0-9]{3})");
        }
    }

    [Theory]
    [InlineData("USSXU", -148.89, 61.71)] // Sutton, Alaska: linked by GeoNames, confirmed in its subdivision.
    [InlineData("USWCA", -84.25, 39.67)]  // West Carrollton, Ohio.
    [InlineData("ITSBM", 11.09, 44.77)]   // Solara di Bomporto, Modena.
    [InlineData("USLCG", -92.40, 31.09)]  // Le Compte, Louisiana: the only Lecompte in the country, confirmed in its subdivision.
    [InlineData("GBSYN", 0.78, 51.53)]    // Shoeburyness, Essex.
    public void Locate_PlacesFilledCodes(string code, double lon, double lat)
    {
        var resolved = TradeRoutes.Locate(code);
        resolved.Source.Should().Be("unlocode");
        TradeRouterEngine.Default.UnLocodes.GetByCode(code)!.CoordinateSource.Should().StartWith(Marker);
        Haversine.DistanceKm(resolved.Coordinate, new Coordinate(lon, lat)).Should().BeLessThan(2.0);
    }

    [Theory]
    [InlineData("ILTAB", "Wikipedia, Taba Border Crossing", 34.90, 29.49)]
    [InlineData("GBDNG", "GeoNames, Donnington, geonameId 13269858", -2.44, 52.72)]
    [InlineData("CNYCG", "GeoNames, Yichang, geonameId 1786764", 111.28, 30.71)]
    [InlineData("CZPRB", "GeoNames, Pardubice, geonameId 3068582", 15.78, 50.04)]
    [InlineData("USILC", "GeoNames, Charleston, geonameId 4235668", -88.18, 39.50)] // Replaces a generated row at St. Charles, Illinois.
    public void Locate_PlacesHandReviewedCodes(string code, string source, double lon, double lat)
    {
        var record = TradeRouterEngine.Default.UnLocodes.GetByCode(code)!;
        record.CoordinateSource.Should().StartWith(source);
        Haversine.DistanceKm(TradeRoutes.Locate(code).Coordinate, new Coordinate(lon, lat)).Should().BeLessThan(2.0);
    }
}
