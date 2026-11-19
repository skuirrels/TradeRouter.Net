using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

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

    public static string Serialize<T>(T value, bool writeIndented)
    {
        var context = writeIndented ? Indented : Compact;
        var typeInfo = (JsonTypeInfo<T>)context.GetTypeInfo(typeof(T))!;
        return JsonSerializer.Serialize(value, typeInfo);
    }
}

[JsonSerializable(typeof(GeoJsonFeatureCollection))]
[JsonSerializable(typeof(GeoJsonFeature))]
internal sealed partial class GeoJsonJsonContext : JsonSerializerContext;
