using System.Text.Json.Serialization;

namespace TradeRouter.SeaAreas;

/// <summary>
/// One passage of a route through a named sea area.
/// </summary>
public sealed class SeaAreaTraversal
{
    /// <summary>Sea-area identifier, one of the <see cref="SeaArea"/> constants.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>English display name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Wikidata item, as a cross-reference only.</summary>
    [JsonPropertyName("wikidata")]
    public string Wikidata { get; set; } = "";

    /// <summary>Kilometres from the start of the route where it enters the area, whatever the route's units.</summary>
    [JsonPropertyName("enter_km")]
    public double EnterKm { get; set; }

    /// <summary>Kilometres from the start of the route where it leaves the area, whatever the route's units.</summary>
    [JsonPropertyName("exit_km")]
    public double ExitKm { get; set; }
}
