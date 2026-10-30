using System.IO.Compression;
using System.Text.Json;
using AwesomeAssertions;
using TradeRouter.Common;
using TradeRouter.Locations;
using TradeRouter.Movements;
using Xunit;

namespace TradeRouter.Tests;

public class AirportCoordinateFillTests
{
    private const string Marker = "Airport fill, ";

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
    public void Rows_FillOnlyAirportsThatUneceLeavesWithoutAPosition()
    {
        var rows = LoadFillRows();
        var published = LoadPublishedRows();

        rows.Should().HaveCount(1_143);
        rows.Select(row => row.Code).Should().OnlyHaveUniqueItems();
        foreach (var row in rows)
        {
            var original = published[row.Code];
            original[2].ValueKind.Should().Be(JsonValueKind.Null, $"{row.Code} is filled only because UNECE publishes no coordinate");
            ((LocationFunctions)original[4].GetInt32() & LocationFunctions.Airport).Should().Be(LocationFunctions.Airport, $"{row.Code} must be an airport");
            row.Name.Should().Be(original[1].GetString(), $"{row.Code} must name its UN/LOCODE record");
            row.Source.Should().MatchRegex($@"^Airport fill, OurAirports \S+ .+, IATA {row.Code[2..]}$", $"{row.Code} must cite the airport carrying its IATA code");
        }
    }

    [Theory]
    [InlineData("JPNRT", 140.39, 35.77)] // Narita.
    [InlineData("USDFW", -97.04, 32.90)] // Dallas-Fort Worth.
    [InlineData("CNPEK", 116.58, 40.08)] // Beijing Capital, filed by UN/LOCODE as Beijing Shoudu International Apt.
    [InlineData("CAYUL", -73.74, 45.47)] // Montreal-Dorval, now Pierre Elliott Trudeau.
    [InlineData("SEARN", 17.92, 59.65)]  // Arlanda.
    public void Locate_PlacesMajorAirportsByCode(string code, double lon, double lat)
    {
        var resolved = TradeRoutes.Locate(code);

        resolved.Source.Should().Be("unlocode");
        TradeRouterEngine.Default.UnLocodes.GetByCode(code)!.CoordinateSource.Should().StartWith(Marker);
        Haversine.DistanceKm(resolved.Coordinate, new Coordinate(lon, lat)).Should().BeLessThan(5.0);
    }

    [Fact]
    public void Locate_PlacesLatrobeInPennsylvaniaNotAtThePortListsLagunaBeach()
    {
        var resolved = TradeRoutes.Locate("USLBE");

        resolved.Source.Should().Be("unlocode");
        resolved.Port.Should().BeNull();
        Haversine.DistanceKm(resolved.Coordinate, new Coordinate(-79.40, 40.28)).Should().BeLessThan(5.0);
    }

    [Fact]
    public void Movement_AirLegBetweenFilledAirportsUsesGreatCircleDistance()
    {
        var plan = MovementPlan
            .From(Waypoint.Airport("JPNRT"))
            .ThenTo(Waypoint.Airport("USDFW"), TransportMode.Air);

        var leg = TradeRoutes.CalculateMovement(plan).Legs.Single();

        leg.Feature.Properties.DistanceBasis.Should().Be("great_circle");
        leg.Length.Should().BeInRange(10_000.0, 10_400.0);
    }
}
