using System.Text.Json;
using System.Text.Json.Serialization;

namespace TradeRouter.GeoJson;

/// <summary>
/// Shared serializer settings so every GeoJSON type in the library writes the same shape. Serialization uses
/// source-generated metadata, so it works in Native AOT and trimmed apps and wherever reflection-based JSON is
/// switched off.
/// </summary>
internal static class GeoJsonSerializer
{
    private static readonly GeoJsonJsonContext Compact = new(new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    });

    private static readonly GeoJsonJsonContext Indented = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    });

    public static string Serialize(GeoJsonFeature value, bool writeIndented) =>
        JsonSerializer.Serialize(value, (writeIndented ? Indented : Compact).GeoJsonFeature);

    public static string Serialize(GeoJsonFeatureCollection value, bool writeIndented) =>
        JsonSerializer.Serialize(value, (writeIndented ? Indented : Compact).GeoJsonFeatureCollection);
}

// The coordinate list types are listed because GeoJsonGeometryConverter serializes them through the options it is
// given; the generator does not reach them on its own past the converter.
[JsonSerializable(typeof(GeoJsonFeatureCollection))]
[JsonSerializable(typeof(GeoJsonFeature))]
[JsonSerializable(typeof(List<double[]>))]
[JsonSerializable(typeof(List<List<double[]>>))]
internal sealed partial class GeoJsonJsonContext : JsonSerializerContext;
