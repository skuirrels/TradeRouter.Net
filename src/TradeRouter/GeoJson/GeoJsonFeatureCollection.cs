using System.Text.Json.Serialization;

namespace TradeRouter.GeoJson;

/// <summary>
/// GeoJSON FeatureCollection, used for multi-leg movements.
/// </summary>
public sealed class GeoJsonFeatureCollection
{
    /// <summary>GeoJSON type: "FeatureCollection".</summary>
    [JsonPropertyName("type")]
    public string Type => "FeatureCollection";

    /// <summary>Member features, one per leg.</summary>
    [JsonPropertyName("features")]
    public List<GeoJsonFeature> Features { get; init; } = [];

    /// <summary>Movement totals, written as a foreign member permitted by RFC 7946 section 6.1.</summary>
    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MovementProperties? Properties { get; init; }

    /// <summary>
    /// Serialises this collection to a GeoJSON string.
    /// </summary>
    public string ToJson(bool writeIndented = false) => GeoJsonSerializer.Serialize(this, writeIndented);

    /// <inheritdoc />
    public override string ToString() => ToJson(false);
}

/// <summary>
/// Totals for a multi-leg movement.
/// </summary>
public sealed class MovementProperties
{
    /// <summary>Sum of leg lengths in <see cref="Units"/>.</summary>
    [JsonPropertyName("total_length")]
    public double TotalLength { get; set; }

    /// <summary>Unit identifier.</summary>
    [JsonPropertyName("units")]
    public string Units { get; set; } = "km";

    /// <summary>Sum of leg durations in hours.</summary>
    [JsonPropertyName("total_duration_hours")]
    public double TotalDurationHours { get; set; }

    /// <summary>Sum of port hours across sea legs.</summary>
    [JsonPropertyName("total_port_hours")]
    public double TotalPortHours { get; set; }

    /// <summary>Sum of sea-service operational allowances.</summary>
    [JsonPropertyName("total_operational_allowance_hours")]
    public double TotalOperationalAllowanceHours { get; set; }

    /// <summary>Sum of transshipment connection hours.</summary>
    [JsonPropertyName("total_connection_hours")]
    public double TotalConnectionHours { get; set; }

    /// <summary>Modelled minimum hours across all legs.</summary>
    [JsonPropertyName("total_transit_hours")]
    public double TotalTransitHours { get; set; }

    /// <summary>Number of legs.</summary>
    [JsonPropertyName("legs")]
    public int LegCount { get; set; }

    /// <summary>Sum of leg CO2e per tonne of cargo, in kilograms.</summary>
    [JsonPropertyName("total_co2e_kg_per_tonne")]
    public double TotalCo2eKgPerTonne { get; set; }

    /// <summary>Cargo weight in tonnes, when stated.</summary>
    [JsonPropertyName("cargo_tonnes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? CargoTonnes { get; set; }

    /// <summary>Container count in TEU, when stated.</summary>
    [JsonPropertyName("cargo_teu")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? CargoTeu { get; set; }

    /// <summary>Sum of leg CO2e in kilograms, when a cargo weight is stated.</summary>
    [JsonPropertyName("total_co2e_kg")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TotalCo2eKg { get; set; }
}
