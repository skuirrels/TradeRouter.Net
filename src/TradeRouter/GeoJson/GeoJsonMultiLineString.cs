using System.Text.Json.Serialization;

namespace TradeRouter.GeoJson;

/// <summary>GeoJSON MultiLineString used when a route is split at the antimeridian.</summary>
public sealed class GeoJsonMultiLineString : GeoJsonGeometry
{
    /// <inheritdoc />
    [JsonPropertyName("type")]
    public override string Type => "MultiLineString";

    /// <summary>Line segments as arrays of [longitude, latitude] positions.</summary>
    [JsonPropertyName("coordinates")]
    public new List<List<double[]>> Coordinates { get; init; } = [];

    /// <inheritdoc />
    public override IReadOnlyList<double[]> Positions => Coordinates.SelectMany(segment => segment).ToList();
}
