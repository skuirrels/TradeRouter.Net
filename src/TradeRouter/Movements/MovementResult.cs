using TradeRouter.GeoJson;

namespace TradeRouter.Movements;

/// <summary>
/// Routed result for one leg of a movement.
/// </summary>
public sealed class LegResult
{
    /// <summary>1-based position of the leg in the movement.</summary>
    public int Sequence { get; }

    /// <summary>The leg as requested.</summary>
    public MovementLeg Leg { get; }

    /// <summary>Resolved start location.</summary>
    public ResolvedLocation From { get; }

    /// <summary>Resolved end location.</summary>
    public ResolvedLocation To { get; }

    /// <summary>Route geometry and properties for the leg.</summary>
    public GeoJsonFeature Feature { get; }

    /// <summary>Leg length in the movement's units.</summary>
    public double Length => Feature.Properties.Length;

    /// <summary>Travelling time in hours, calculated according to the request's duration policy.</summary>
    public double DurationHours => Feature.Properties.DurationHours;

    /// <summary>Port time in hours: dwell at each end of a sea leg, zero for road, rail and air.</summary>
    public double PortHours => Feature.Properties.PortHours ?? 0.0;

    /// <summary>Normal sea-service operational allowance in hours, zero for non-sea legs.</summary>
    public double OperationalAllowanceHours => Feature.Properties.OperationalAllowanceHours ?? 0.0;

    /// <summary>Transshipment connection time before this leg in hours.</summary>
    public double ConnectionHours => Feature.Properties.ConnectionHours ?? 0.0;

    /// <summary>Modelled minimum time in hours, including all configured allowances.</summary>
    public double TransitHours => Feature.Properties.TransitHours ?? DurationHours;

    /// <summary>Well-to-wheel CO2e intensity applied, in grams per tonne-kilometre.</summary>
    public double Co2eGramsPerTonneKm => Feature.Properties.Co2eGramsPerTonneKm ?? 0.0;

    /// <summary>CO2e per tonne of cargo for this leg, in kilograms.</summary>
    public double Co2eKgPerTonne => Feature.Properties.Co2eKgPerTonne ?? 0.0;

    /// <summary>CO2e for this leg in kilograms, when the movement states a cargo weight or TEU count.</summary>
    public double? Co2eKg => Feature.Properties.Co2eKg;

    /// <summary>Basis of <see cref="Co2eKg"/>: "tonnes", "teu" or "teu_average_weight".</summary>
    public string? Co2eBasis => Feature.Properties.Co2eBasis;

    internal LegResult(int sequence, MovementLeg leg, ResolvedLocation from, ResolvedLocation to, GeoJsonFeature feature)
    {
        Sequence = sequence;
        Leg = leg;
        From = from;
        To = to;
        Feature = feature;
    }
}

/// <summary>
/// Routed result for a whole movement.
/// </summary>
public sealed class MovementResult
{
    /// <summary>Per-leg results in order.</summary>
    public IReadOnlyList<LegResult> Legs { get; }

    /// <summary>Unit identifier shared by every length in the result.</summary>
    public string Units { get; }

    /// <summary>Sum of leg lengths.</summary>
    public double TotalLength { get; }

    /// <summary>Sum of leg travelling hours.</summary>
    public double TotalDurationHours { get; }

    /// <summary>Sum of port hours across sea legs.</summary>
    public double TotalPortHours { get; }

    /// <summary>Sum of sea-service operational allowances.</summary>
    public double TotalOperationalAllowanceHours { get; }

    /// <summary>Sum of transshipment connection hours.</summary>
    public double TotalConnectionHours { get; }

    /// <summary>Modelled minimum hours across all legs.</summary>
    public double TotalTransitHours { get; }

    /// <summary>Length per transport mode.</summary>
    public IReadOnlyDictionary<TransportMode, double> LengthByMode { get; }

    /// <summary>Sum of leg CO2e per tonne of cargo, in kilograms.</summary>
    public double TotalCo2eKgPerTonne { get; }

    /// <summary>Cargo weight in tonnes, when stated on the request.</summary>
    public double? CargoTonnes { get; }

    /// <summary>Container count in TEU, when stated on the request.</summary>
    public double? CargoTeu { get; }

    /// <summary>Sum of leg CO2e in kilograms, when a cargo weight or TEU count is stated.</summary>
    public double? TotalCo2eKg { get; }

    internal MovementResult(IReadOnlyList<LegResult> legs, string units, double? cargoTonnes, double? cargoTeu)
    {
        Legs = legs;
        Units = units;
        CargoTonnes = cargoTonnes;
        CargoTeu = cargoTeu;

        double totalLength = 0.0;
        double totalHours = 0.0;
        double totalPortHours = 0.0;
        double totalOperationalAllowanceHours = 0.0;
        double totalConnectionHours = 0.0;
        double totalTransitHours = 0.0;
        double totalCo2ePerTonne = 0.0;
        double totalCo2eKg = 0.0;
        bool anyAbsolute = false;
        var byMode = new Dictionary<TransportMode, double>(4);
        foreach (var leg in legs)
        {
            totalLength += leg.Length;
            totalHours += leg.DurationHours;
            totalPortHours += leg.PortHours;
            totalOperationalAllowanceHours += leg.OperationalAllowanceHours;
            totalConnectionHours += leg.ConnectionHours;
            totalTransitHours += leg.TransitHours;
            totalCo2ePerTonne += leg.Co2eKgPerTonne;
            if (leg.Co2eKg.HasValue)
            {
                totalCo2eKg += leg.Co2eKg.Value;
                anyAbsolute = true;
            }
            byMode.TryGetValue(leg.Leg.Mode, out double soFar);
            byMode[leg.Leg.Mode] = soFar + leg.Length;
        }

        TotalLength = totalLength;
        TotalDurationHours = totalHours;
        TotalPortHours = totalPortHours;
        TotalOperationalAllowanceHours = totalOperationalAllowanceHours;
        TotalConnectionHours = totalConnectionHours;
        TotalTransitHours = totalTransitHours;
        LengthByMode = byMode;
        TotalCo2eKgPerTonne = totalCo2ePerTonne;
        TotalCo2eKg = anyAbsolute ? totalCo2eKg : null;
    }

    /// <summary>
    /// Builds a GeoJSON FeatureCollection with one feature per leg and the totals as foreign members.
    /// </summary>
    public GeoJsonFeatureCollection ToFeatureCollection()
    {
        var features = new List<GeoJsonFeature>(Legs.Count);
        foreach (var leg in Legs)
            features.Add(leg.Feature);

        return new GeoJsonFeatureCollection
        {
            Features = features,
            Properties = new MovementProperties
            {
                TotalLength = TotalLength,
                Units = Units,
                TotalDurationHours = TotalDurationHours,
                TotalPortHours = TotalPortHours,
                TotalOperationalAllowanceHours = TotalOperationalAllowanceHours,
                TotalConnectionHours = TotalConnectionHours,
                TotalTransitHours = TotalTransitHours,
                LegCount = Legs.Count,
                TotalCo2eKgPerTonne = TotalCo2eKgPerTonne,
                CargoTonnes = CargoTonnes,
                CargoTeu = CargoTeu,
                TotalCo2eKg = TotalCo2eKg
            }
        };
    }

    /// <summary>
    /// Serialises the movement as a GeoJSON FeatureCollection.
    /// </summary>
    public string ToJson(bool writeIndented = false) => ToFeatureCollection().ToJson(writeIndented);

    /// <summary>
    /// Formats the complete movement as a human-readable text table with leg details, totals, transit time,
    /// emissions and traversed choke points.
    /// </summary>
    /// <param name="includeExplanations">Whether to append short explanations of the emissions columns.</param>
    public string ToText(bool includeExplanations = true) =>
        MovementTextFormatter.Format(this, includeExplanations);
}
