using System.Text.Json.Serialization;
using TradeRouter.Common;

namespace TradeRouter.Ports;

/// <summary>
/// Represents a maritime port from the World Ports dataset.
/// </summary>
public sealed class Port
{
    /// <summary>UN/LOCODE or internal port code (e.g., "FRLEH", "CNTSN").</summary>
    [JsonPropertyName("port")]
    public string PortCode { get; init; } = string.Empty;

    /// <summary>Human-readable name of the port (e.g., "Le Havre", "Singapore").</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Country name or ISO code of the port.</summary>
    [JsonPropertyName("cty")]
    public string Country { get; init; } = string.Empty;

    /// <summary>Terminal flag (1.0 = terminal port, 0.0 = non-terminal).</summary>
    [JsonPropertyName("t")]
    public double TerminalFlag { get; init; }

    /// <summary>Indicates if this port is a major container / cargo terminal.</summary>
    [JsonIgnore]
    public bool IsTerminal => TerminalFlag >= 1.0;

    /// <summary>List of destination country codes permitted from this port.</summary>
    [JsonPropertyName("to_cty")]
    public IReadOnlyList<string> ToCountries { get; init; } = [];

    /// <summary>Geographical coordinates (Longitude, Latitude).</summary>
    [JsonIgnore]
    public Coordinate Coordinate { get; init; }

    /// <summary>Longitude coordinate.</summary>
    [JsonPropertyName("x")]
    public double Longitude => Coordinate.Longitude;

    /// <summary>Latitude coordinate.</summary>
    [JsonPropertyName("y")]
    public double Latitude => Coordinate.Latitude;

    /// <summary>Relative share weight when used in preferred port calculations.</summary>
    [JsonPropertyName("share")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Share { get; set; }

    /// <summary>
    /// Clones this port with an updated share.
    /// </summary>
    public Port WithShare(double share)
    {
        return new Port
        {
            PortCode = PortCode,
            Name = Name,
            Country = Country,
            TerminalFlag = TerminalFlag,
            ToCountries = ToCountries,
            Coordinate = Coordinate,
            Share = share
        };
    }
}
