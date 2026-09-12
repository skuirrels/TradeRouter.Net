using System.Text.Json.Serialization;

namespace TradeRouter.GeoJson;

/// <summary>
/// Represents a GeoJSON Feature containing route geometry and properties.
/// </summary>
public sealed class GeoJsonFeature
{
    /// <summary>GeoJSON type: "Feature".</summary>
    [JsonPropertyName("type")]
    public string Type => "Feature";

    /// <summary>The route geometry, or null when no route exists under the supplied restrictions.</summary>
    [JsonPropertyName("geometry")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public GeoJsonGeometry? Geometry { get; init; }

    /// <summary>Calculated properties including length, duration, ports, and passages.</summary>
    [JsonPropertyName("properties")]
    public TradeRouterProperties Properties { get; init; } = new();

    /// <summary>
    /// Serializes this feature to a GeoJSON string.
    /// </summary>
    public string ToJson(bool writeIndented = false) => GeoJsonSerializer.Serialize(this, writeIndented);

    /// <inheritdoc />
    public override string ToString() => ToJson(false);
}
