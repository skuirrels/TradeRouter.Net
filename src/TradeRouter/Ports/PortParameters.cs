namespace TradeRouter.Ports;

/// <summary>
/// Parameters for filtering, selection, and polygon-weighting of maritime ports.
/// </summary>
public sealed class PortParameters
{
    /// <summary>Whether to consider only major terminals (default: false).</summary>
    public bool OnlyTerminals { get; set; }

    /// <summary>Country code/name filter for Port of Loading (origin).</summary>
    public string? CountryPol { get; set; }

    /// <summary>Country code/name filter for Port of Discharge (destination).</summary>
    public string? CountryPod { get; set; }

    /// <summary>Whether destination country restriction is enforced against allowed countries.</summary>
    public bool CountryRestricted { get; set; }

    /// <summary>
    /// When true, the default, a terminal or country filter that matches no port yields no port, and the route
    /// falls back to the raw coordinates. When false the filter is dropped and the nearest port of any kind is used.
    /// </summary>
    public bool Strict { get; set; } = true;

    /// <summary>Whether point must be strictly inside an area polygon (default: true).</summary>
    public bool StrictArea { get; set; } = true;

    /// <summary>Preferred port areas for the origin point.</summary>
    public IReadOnlyList<AreaFeature>? PortsInAreasFrom { get; set; }

    /// <summary>Preferred port areas for the destination point.</summary>
    public IReadOnlyList<AreaFeature>? PortsInAreasTo { get; set; }

    /// <summary>Preferred port areas applied to both origin and destination if not specified individually.</summary>
    public IReadOnlyList<AreaFeature>? PortsInAreas { get; set; }

    /// <summary>Returns a snapshot whose area lists cannot be changed through the source options.</summary>
    public PortParameters Clone() => new()
    {
        OnlyTerminals = OnlyTerminals,
        CountryPol = CountryPol,
        CountryPod = CountryPod,
        CountryRestricted = CountryRestricted,
        Strict = Strict,
        StrictArea = StrictArea,
        PortsInAreasFrom = PortsInAreasFrom?.ToArray(),
        PortsInAreasTo = PortsInAreasTo?.ToArray(),
        PortsInAreas = PortsInAreas?.ToArray()
    };

    internal void Validate()
    {
        ValidateAreas(PortsInAreasFrom, nameof(PortsInAreasFrom));
        ValidateAreas(PortsInAreasTo, nameof(PortsInAreasTo));
        ValidateAreas(PortsInAreas, nameof(PortsInAreas));
    }

    private static void ValidateAreas(IReadOnlyList<AreaFeature>? areas, string name)
    {
        if (areas is not null && areas.Any(area => area is null))
            throw new ArgumentException($"{name} cannot contain null areas.", name);
    }
}
