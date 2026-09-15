using System.Text.Json;
using FluentAssertions;
using TradeRouter.Common;
using TradeRouter.GeoJson;
using Xunit;

namespace TradeRouter.Tests;

public class RoutingTests
{
    [Fact]
    public void Readme_PortCodeRoute_AcceptsOptionsWithoutCoordinateFallback()
    {
        var route = TradeRoutes.Calculate(
            "AEJEA",
            "AGSJO",
            new TradeRouterOptions
            {
                Restrictions = [Passages.Passage.Suez],
                ReturnPassages = true
            });

        route.Properties.PortOrigin!.PortCode.Should().Be("AEJEA");
        route.Properties.PortDest!.PortCode.Should().Be("AGSJS");
        route.Properties.TraversedPassages.Should().Contain(Passages.Passage.Ormuz);
        route.Properties.TraversedPassages.Should().Contain(Passages.Passage.SouthAfrica);
        route.Properties.TraversedPassages.Should().NotContain(Passages.Passage.Suez);
    }

    [Theory]
    [InlineData("AGSJO", "AGSJS")] // Saint John's, Antigua
    [InlineData("JOAQJ", "JOAQB")] // Aqaba
    public void PortCodeRoute_OfficialUnLocodeResolvesThroughItsAlias(string officialCode, string portListCode)
    {
        TradeRouterEngine.Default.Ports.GetByCode(officialCode).Should().BeNull("the port list holds this port under another code");

        var viaAlias = TradeRoutes.Calculate("AEJEA", officialCode);
        var direct = TradeRoutes.Calculate("AEJEA", portListCode);

        viaAlias.Properties.PortDest!.PortCode.Should().Be(portListCode);
        viaAlias.Properties.Length.Should().Be(direct.Properties.Length);
    }

    [Fact]
    public void PortCodeAliases_EachNameAnOfficialSeaPortAndOneUnambiguousPortRecord()
    {
        var aliases = Data.EmbeddedResources.LoadPortCodeAliases();
        aliases.Should().HaveCountGreaterThan(50);

        foreach (var (code, portCode) in aliases)
        {
            TradeRouterEngine.Default.Ports.GetByCodeCandidates(code).Should().BeEmpty($"{code} must not shadow a port-list record");
            TradeRouterEngine.Default.UnLocodes.GetByCode(code)!.IsSeaPort.Should().BeTrue($"{code} must be a UN/LOCODE sea port");
            TradeRouterEngine.Default.UnLocodes.GetByCode(portCode).Should().BeNull($"{portCode} is aliased only because UN/LOCODE lacks it");
            TradeRouterEngine.Default.Ports.GetByCodeCandidates(portCode).Should().ContainSingle($"{code} must map to one {portCode} record");
        }
    }

    [Fact]
    public void PortCodeRoute_UnknownCodeStillThrows()
    {
        var act = () => TradeRoutes.Calculate("AEJEA", "XXZZZ");

        act.Should().Throw<ArgumentException>().WithMessage("*XXZZZ*not found*");
    }

    [Fact]
    public void MarseilleToCapeTown_NoAppend_ShouldMatchExpectedLength()
    {
        var origin = new Coordinate(5.333333, 43.333333);
        var dest = new Coordinate(18.366667, -33.916667);

        var route = TradeRoutes.Calculate(origin, dest, appendOrigDest: false);

        route.Properties.Units.Should().Be("km");
        route.Properties.Length.Should().BeApproximately(10986.505, 1.0);
        route.Geometry!.Coordinates.Count.Should().Be(59);

        // A* algorithm should yield identical route length
        var routeAStar = TradeRoutes.Calculate(origin, dest, appendOrigDest: false, algorithm: "astar");
        routeAStar.Properties.Length.Should().BeApproximately(route.Properties.Length, 1e-3);
    }

    [Fact]
    public void MarseilleToCapeTown_AppendOrigDest_ShouldMatchExpectedLength()
    {
        var origin = new Coordinate(5.333333, 43.333333);
        var dest = new Coordinate(18.366667, -33.916667);

        var route = TradeRoutes.Calculate(origin, dest, appendOrigDest: true);

        route.Properties.Length.Should().BeApproximately(10996.763, 1.0);
        route.Geometry!.Coordinates.Count.Should().Be(61);

        // First coord must be origin and last must be dest
        route.Geometry.Coordinates[0][0].Should().BeApproximately(origin.Longitude, 1e-5);
        route.Geometry.Coordinates[0][1].Should().BeApproximately(origin.Latitude, 1e-5);
        route.Geometry.Coordinates[^1][0].Should().BeApproximately(dest.Longitude, 1e-5);
        route.Geometry.Coordinates[^1][1].Should().BeApproximately(dest.Latitude, 1e-5);
    }

    [Fact]
    public void MarseilleToCapeTown_UnitConversions_ShouldMatchExpectedOutputs()
    {
        var origin = new Coordinate(5.333333, 43.333333);
        var dest = new Coordinate(18.366667, -33.916667);

        var routeMiles = TradeRoutes.Calculate(origin, dest, units: DistanceUnit.Miles, appendOrigDest: false);
        routeMiles.Properties.Units.Should().Be("mi");
        routeMiles.Properties.Length.Should().BeApproximately(6826.70, 1.0);
        routeMiles.Properties.DurationHours.Should().BeApproximately(370.77, 0.5);

        var routeNaut = TradeRoutes.Calculate(origin, dest, units: DistanceUnit.NauticalMiles, appendOrigDest: false);
        routeNaut.Properties.Units.Should().Be("naut");
        routeNaut.Properties.Length.Should().BeApproximately(5932.24, 1.0);
        routeNaut.Properties.DurationHours.Should().BeApproximately(370.77, 0.5);
    }

    [Fact]
    public void ShanghaiToRotterdam_MajorCommercialRoute_ShouldMatchExpectedLength()
    {
        var shanghai = new Coordinate(121.47, 31.23);
        var rotterdam = new Coordinate(4.48, 51.92);

        var route = TradeRoutes.Calculate(shanghai, rotterdam, appendOrigDest: true);

        route.Properties.Length.Should().BeApproximately(19646.929, 2.0);
        route.Geometry!.Coordinates.Count.Should().Be(159);
    }

    [Fact]
    public void TransPacific_YokohamaToLosAngeles_CrossesAntimeridianCorrectly()
    {
        var yokohama = new Coordinate(139.64, 35.44);
        var losAngeles = new Coordinate(-118.24, 33.74);

        var route = TradeRoutes.Calculate(yokohama, losAngeles, appendOrigDest: true);

        route.Properties.Length.Should().BeApproximately(9126.579, 2.0);
        route.Geometry.Should().BeOfType<GeoJsonMultiLineString>();
        var multiLine = (GeoJsonMultiLineString)route.Geometry!;
        multiLine.Coordinates.Should().HaveCount(2);
        multiLine.Coordinates.Should().OnlyContain(segment => segment.Count >= 2);

        foreach (var segment in multiLine.Coordinates)
        {
            segment.Should().HaveCountGreaterThanOrEqualTo(2);
            segment.Should().OnlyContain(position => position[0] >= -180.0 && position[0] <= 180.0);
            for (int i = 0; i < segment.Count - 1; i++)
            {
                double diff = Math.Abs(segment[i + 1][0] - segment[i][0]);
                diff.Should().BeLessThanOrEqualTo(180.0, "each RFC 7946 line segment stays on one side of the antimeridian");
            }
        }

        using var document = JsonDocument.Parse(route.ToJson());
        document.RootElement.GetProperty("geometry").GetProperty("type").GetString().Should().Be("MultiLineString");
    }

    [Fact]
    public void GeoJsonFeature_SerializationAndDeserialization_IsValidGeoJson()
    {
        var origin = new Coordinate(5.333333, 43.333333);
        var dest = new Coordinate(18.366667, -33.916667);

        var route = TradeRoutes.Calculate(origin, dest, appendOrigDest: true);
        string json = route.ToJson();

        json.Should().NotBeNullOrWhiteSpace();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("type").GetString().Should().Be("Feature");
        root.GetProperty("geometry").GetProperty("type").GetString().Should().Be("LineString");
        root.GetProperty("geometry").GetProperty("coordinates").GetArrayLength().Should().Be(61);
        root.GetProperty("properties").GetProperty("units").GetString().Should().Be("km");
        root.GetProperty("properties").GetProperty("length").GetDouble().Should().BeApproximately(10996.763, 1.0);
    }
}
