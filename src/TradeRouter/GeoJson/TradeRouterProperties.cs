using System.Text.Json.Serialization;
using TradeRouter.Ports;

namespace TradeRouter.GeoJson;

/// <summary>
/// Properties associated with a calculated sea route GeoJSON feature.
/// </summary>
public sealed class TradeRouterProperties
{
    /// <summary>Total route length in the requested units.</summary>
    [JsonPropertyName("length")]
    public double Length { get; set; }

    /// <summary>Unit used for the length measurement (e.g. "km", "naut", "mi").</summary>
    [JsonPropertyName("units")]
    public string Units { get; set; } = "km";

    /// <summary>Travelling duration in hours, derived according to the request's duration policy.</summary>
    [JsonPropertyName("duration_hours")]
    public double DurationHours { get; set; }

    /// <summary>How the distance was obtained, for example "road_network", "supplied", "circuity_estimate" or "great_circle".</summary>
    [JsonPropertyName("distance_basis")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DistanceBasis { get; set; }

    /// <summary>Great-circle lower bound in the requested units when a road distance was supplied, routed or estimated.</summary>
    [JsonPropertyName("straight_line_length")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? StraightLineLength { get; set; }

    /// <summary>Provider, estimator or caller provenance for the distance.</summary>
    [JsonPropertyName("distance_source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DistanceSource { get; set; }

    /// <summary>Routing profile used for a road-network result.</summary>
    [JsonPropertyName("routing_profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RoutingProfile { get; set; }

    /// <summary>Version identifier configured for the road-network dataset.</summary>
    [JsonPropertyName("routing_data_version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RoutingDataVersion { get; set; }

    /// <summary>How travelling duration was derived, such as "provider" or "assumed_speed".</summary>
    [JsonPropertyName("duration_basis")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DurationBasis { get; set; }

    /// <summary>How the emitted geometry was obtained, such as "road_network" or "great_circle".</summary>
    [JsonPropertyName("geometry_basis")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GeometryBasis { get; set; }

    /// <summary>Warning attached to an estimated road distance or provider fallback.</summary>
    [JsonPropertyName("distance_warning")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DistanceWarning { get; set; }

    /// <summary>Origin port details if ports are included.</summary>
    [JsonPropertyName("port_origin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Port? PortOrigin { get; set; }

    /// <summary>Destination port details if ports are included.</summary>
    [JsonPropertyName("port_dest")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Port? PortDest { get; set; }

    /// <summary>List of passages, straits, or canals traversed along the route.</summary>
    [JsonPropertyName("traversed_passages")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? TraversedPassages { get; set; }

    /// <summary>1-based leg number when the feature is part of a multi-leg movement.</summary>
    [JsonPropertyName("leg")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Leg { get; set; }

    /// <summary>Transport mode of the leg: "sea", "road", "rail" or "air".</summary>
    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mode { get; set; }

    /// <summary>Role of the leg: "main", "pickup" or "delivery".</summary>
    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; set; }

    /// <summary>Code or name of the leg's start location.</summary>
    [JsonPropertyName("from")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? From { get; set; }

    /// <summary>Code or name of the leg's end location.</summary>
    [JsonPropertyName("to")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? To { get; set; }

    /// <summary>Port time for the leg in hours: dwell at each end of a sea leg, zero for other modes. Movement legs only.</summary>
    [JsonPropertyName("port_hours")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? PortHours { get; set; }

    /// <summary>Sea-service operational allowance in hours. Zero for non-sea movement legs.</summary>
    [JsonPropertyName("operational_allowance_hours")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? OperationalAllowanceHours { get; set; }

    /// <summary>
    /// Fraction of sea travelling time added as the operational allowance for this leg. Chosen from the
    /// corridor table unless the request sets <c>SeaOperationalAllowance</c>. Sea movement legs only.
    /// </summary>
    [JsonPropertyName("operational_allowance_fraction")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? OperationalAllowanceFraction { get; set; }

    /// <summary>
    /// Trade corridor the operational allowance was taken from, for example "asia-north-europe" or
    /// "transpacific"; "override" when the request set its own fraction. Sea movement legs only.
    /// </summary>
    [JsonPropertyName("operational_allowance_corridor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OperationalAllowanceCorridor { get; set; }

    /// <summary>Transshipment connection time before the leg in hours. Movement legs only.</summary>
    [JsonPropertyName("connection_hours")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ConnectionHours { get; set; }

    /// <summary>
    /// Modelled minimum time for the leg in hours: travel, operational allowance, port handling and connection
    /// time. This is not a carrier-scheduled or guaranteed transit time. Movement legs only.
    /// </summary>
    [JsonPropertyName("transit_hours")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TransitHours { get; set; }

    /// <summary>Well-to-wheel CO2e intensity applied to the leg, in grams per tonne-kilometre.</summary>
    [JsonPropertyName("co2e_g_per_tonne_km")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Co2eGramsPerTonneKm { get; set; }

    /// <summary>CO2e for the leg per tonne of cargo, in kilograms: intensity times leg length in kilometres.</summary>
    [JsonPropertyName("co2e_kg_per_tonne")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Co2eKgPerTonne { get; set; }

    /// <summary>CO2e rate per container for sea legs, in grams per TEU-kilometre. Present when the movement states a TEU count.</summary>
    [JsonPropertyName("co2e_g_per_teu_km")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Co2eGramsPerTeuKm { get; set; }

    /// <summary>CO2e for the leg in kilograms, present when the movement states a cargo weight or TEU count.</summary>
    [JsonPropertyName("co2e_kg")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Co2eKg { get; set; }

    /// <summary>How <see cref="Co2eKg"/> was derived: "tonnes", "teu", or "teu_average_weight" when tonnes were inferred from TEU.</summary>
    [JsonPropertyName("co2e_basis")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Co2eBasis { get; set; }
}
