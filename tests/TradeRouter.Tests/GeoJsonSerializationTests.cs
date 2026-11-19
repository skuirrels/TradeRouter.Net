using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using AwesomeAssertions;
using TradeRouter.Common;
using TradeRouter.GeoJson;
using Xunit;

namespace TradeRouter.Tests;

/// <summary>
/// GeoJSON is written with source-generated metadata. These tests pin its output to what reflection-based
/// serialization with the same settings writes, so the switch cannot change the JSON shape.
/// </summary>
public class GeoJsonSerializationTests
{
    // Pickup, two sea legs (the second crosses the antimeridian, so its geometry is a MultiLineString) and delivery.
    private const string TranspacificMovement = """
        Pickup GBLGW to Port GBFXT Road
        Port GBFXT to Port CNSHG Sea
        Port CNSHG to Port USLAX Sea
        Delivery from port USLAX to place USPHX Road
        """;

    private static readonly TradeRouterOptions SeaDetail = new() { ReturnPassages = true, ReturnSeaAreas = true };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovementJson_MatchesReflectionSerialization(bool writeIndented)
    {
        var result = TradeRoutes.CalculateMovement(TranspacificMovement, seaOptions: SeaDetail, cargoTonnes: 12.0, cargoTeu: 2.0);
        var collection = result.ToFeatureCollection();

        result.Legs[2].Feature.Geometry.Should().BeOfType<GeoJsonMultiLineString>("the fixture must cover both geometry types");
        result.Legs[1].Feature.Properties.TraversedSeaAreas.Should().NotBeNullOrEmpty();
        result.Legs[1].Feature.Properties.PortOrigin.Should().NotBeNull();

        result.ToJson(writeIndented).Should().Be(ReflectionJson(collection, writeIndented));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortToPortFeatureJson_MatchesReflectionSerialization(bool writeIndented)
    {
        var feature = TradeRoutes.Calculate(
            new Coordinate(121.8, 31.2),
            new Coordinate(-118.3, 33.7),
            new TradeRouterOptions { IncludePorts = true, ReturnPassages = true, ReturnSeaAreas = true });

        feature.ToJson(writeIndented).Should().Be(ReflectionJson(feature, writeIndented));
        feature.ToString().Should().Be(ReflectionJson(feature, false));
    }

    [Fact]
    public void GeometryRoundTripsThroughItsConverter()
    {
        var feature = TradeRoutes.Calculate(new Coordinate(121.8, 31.2), new Coordinate(-118.3, 33.7));
        string json = feature.ToJson();

        using var document = JsonDocument.Parse(json);
        var geometry = JsonSerializer.Deserialize(document.RootElement.GetProperty("geometry").GetRawText(), ReflectionGeometry);

        geometry!.Positions.Should().BeEquivalentTo(feature.Geometry!.Positions, options => options.WithStrictOrdering());
    }

    // Literal expectations, independent of the converter, so a change to how coordinates are written shows up here.
    [Fact]
    public void LineStringCoordinates_AreWrittenAsNestedNumberArrays()
    {
        var feature = new GeoJsonFeature { Geometry = new GeoJsonLineString { Coordinates = [[1.5, 2], [3, -4.25]] } };

        feature.ToJson().Should().Contain("""
            "geometry":{"type":"LineString","coordinates":[[1.5,2],[3,-4.25]]}
            """);
    }

    [Fact]
    public void MultiLineStringCoordinates_AreWrittenAsNestedNumberArrays()
    {
        var feature = new GeoJsonFeature
        {
            Geometry = new GeoJsonMultiLineString { Coordinates = [[[179, 1], [180, 1.5]], [[-180, 1.5], [-179, 2]]] }
        };

        feature.ToJson().Should().Contain("""
            "geometry":{"type":"MultiLineString","coordinates":[[[179,1],[180,1.5]],[[-180,1.5],[-179,2]]]}
            """);
    }

    [Fact]
    public void NullCoordinateListsAndPositions_AreWrittenAsNull()
    {
        new GeoJsonFeature { Geometry = new GeoJsonLineString { Coordinates = null! } }.ToJson()
            .Should().Contain("""{"type":"LineString","coordinates":null}""");
        new GeoJsonFeature { Geometry = new GeoJsonLineString { Coordinates = [[1, 2], null!] } }.ToJson()
            .Should().Contain("""{"type":"LineString","coordinates":[[1,2],null]}""");
        new GeoJsonFeature { Geometry = new GeoJsonMultiLineString { Coordinates = [[[1, 2], [3, 4]], null!] } }.ToJson()
            .Should().Contain("""{"type":"MultiLineString","coordinates":[[[1,2],[3,4]],null]}""");
    }

    [Fact]
    public void CallerOptions_StillApplyToCoordinates()
    {
        var feature = new GeoJsonFeature { Geometry = new GeoJsonLineString { Coordinates = [[double.NaN, 2], [3, 4]] } };
        var options = ReflectionOptions(false);
        options.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;

        JsonSerializer.Serialize(feature, options).Should().Contain("""
            "coordinates":[["NaN",2],[3,4]]
            """);
    }

    private static readonly JsonTypeInfo<GeoJsonGeometry> ReflectionGeometry =
        (JsonTypeInfo<GeoJsonGeometry>)ReflectionOptions(false).GetTypeInfo(typeof(GeoJsonGeometry));

    private static string ReflectionJson<T>(T value, bool writeIndented) =>
        JsonSerializer.Serialize(value, ReflectionOptions(writeIndented));

    private static JsonSerializerOptions ReflectionOptions(bool writeIndented) => new()
    {
        WriteIndented = writeIndented,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };
}
