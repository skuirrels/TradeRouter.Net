using System.Text.Json;
using System.Text.Json.Serialization;

namespace TradeRouter.GeoJson;

/// <summary>
/// Shared serializer settings so every GeoJSON type in the library writes the same shape.
/// </summary>
internal static class GeoJsonSerializer
{
    private static readonly JsonSerializerOptions Compact = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize<T>(T value, bool writeIndented)
    {
        return JsonSerializer.Serialize(value, writeIndented ? Indented : Compact);
    }
}
