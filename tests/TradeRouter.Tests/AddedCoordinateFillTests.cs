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

        rows.Should().HaveCount(1_484); // 1,485 generated; a reviewed row replaced the Port of Palm Beach (USPBC).
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

    // Reviewed on 18 November 2026: codes UNECE publishes without a position that no generated row could place.
    [Theory]
    [InlineData("USKCK", "GeoNames, Kansas City, geonameId 4273837", -94.63, 39.11)]          // Kansas, not the Missouri airport.
    [InlineData("IDAJN", "ShipNext, Ardjuna Marine Terminal", 107.74, -5.90)]                // Offshore, not the inland village.
    [InlineData("TRKCO", "OurAirports LTBQ Cengiz Topel Airport", 30.08, 40.74)]
    [InlineData("CNQSE", "GeoNames, Qingshuihe, geonameId 13293570", 114.09, 22.57)]
    [InlineData("CNFRT", "GeoNames, Foshan New Port, geonameId 11524246", 113.13, 22.98)]
    [InlineData("GBLUT", "GeoNames, Luton, geonameId 2643339", -0.42, 51.88)]
    [InlineData("PHCVE", "GeoNames, Cavite City, geonameId 1717641", 120.90, 14.48)]        // On Manila Bay.
    [InlineData("GBPFT", "GeoNames, Purfleet-on-Thames, geonameId 2639845", 0.24, 51.48)]
    [InlineData("CAYTO", "GeoNames, Metropolitan Area Apt, geonameId 11549704", -79.40, 43.72)]
    public void Locate_PlacesCodesReviewedOn18November(string code, string source, double lon, double lat)
    {
        TradeRouterEngine.Default.UnLocodes.GetByCode(code)!.CoordinateSource.Should().StartWith(source);
        Haversine.DistanceKm(TradeRoutes.Locate(code).Coordinate, new Coordinate(lon, lat)).Should().BeLessThan(2.0);
    }
}
