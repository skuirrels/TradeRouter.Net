namespace TradeRouter.Movements;

/// <summary>
/// Well-to-wheel CO2e emission intensity per transport mode, in grams of CO2e per tonne-kilometre.
/// Defaults are the GLEC Framework default values (Smart Freight Centre, GLEC Framework v2, July 2022 edition,
/// Module 2), which ISO 14083 builds on. Override any property to use carrier-specific or newer factors.
/// </summary>
public sealed class EmissionFactors
{
    /// <summary>A fresh instance carrying the GLEC default values.</summary>
    public static EmissionFactors GlecDefaults => new();

    /// <summary>
    /// Deep-sea container shipping per container. GLEC Table 46, industry average dry container,
    /// 76 g CO2e per TEU-km WTW. Used for sea legs when the request states a TEU count.
    /// </summary>
    public double SeaGramsPerTeuKm { get; set; } = 76.0;

    /// <summary>GLEC average cargo weight per TEU, 10 tonnes, used to convert between the two sea bases.</summary>
    public double AverageTonnesPerTeu { get; set; } = 10.0;

    /// <summary>
    /// Deep-sea container shipping per tonne: <see cref="SeaGramsPerTeuKm"/> divided by <see cref="AverageTonnesPerTeu"/>.
    /// Used for sea legs when only a cargo weight is known.
    /// </summary>
    public double SeaGramsPerTonneKm { get; set; } = 7.6;

    /// <summary>Road haulage. GLEC Europe starting value for an HGV over 20 t GVW, 92 g CO2e per tonne-km WTW.</summary>
    public double RoadGramsPerTonneKm { get; set; } = 92.0;

    /// <summary>Rail freight. GLEC Table 38, European diesel traction, average/mixed load, 28 g CO2e per tonne-km WTW.</summary>
    public double RailGramsPerTonneKm { get; set; } = 28.0;

    /// <summary>Air freight under 1,000 km. GLEC Table 35, ICAO/IATA RP1678 basis, aircraft type unknown, 1,130 g CO2e per tonne-km WTW.</summary>
    public double AirShortHaulGramsPerTonneKm { get; set; } = 1130.0;

    /// <summary>Air freight from 1,000 to 3,700 km. GLEC Table 35, aircraft type unknown, 700 g CO2e per tonne-km WTW.</summary>
    public double AirMediumHaulGramsPerTonneKm { get; set; } = 700.0;

    /// <summary>Air freight over 3,700 km. GLEC Table 35, aircraft type unknown, 630 g CO2e per tonne-km WTW.</summary>
    public double AirLongHaulGramsPerTonneKm { get; set; } = 630.0;

    /// <summary>
    /// Returns the factor for a leg of the given mode and length. Air uses the GLEC distance bands.
    /// </summary>
    public double GramsPerTonneKm(TransportMode mode, double distanceKm) => mode switch
    {
        TransportMode.Sea => SeaGramsPerTonneKm,
        TransportMode.Road => RoadGramsPerTonneKm,
        TransportMode.Rail => RailGramsPerTonneKm,
        TransportMode.Air when distanceKm < 1000.0 => AirShortHaulGramsPerTonneKm,
        TransportMode.Air when distanceKm <= 3700.0 => AirMediumHaulGramsPerTonneKm,
        TransportMode.Air => AirLongHaulGramsPerTonneKm,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown transport mode.")
    };

    internal void Validate()
    {
        ValidateNonNegative(SeaGramsPerTeuKm, nameof(SeaGramsPerTeuKm));
        if (!double.IsFinite(AverageTonnesPerTeu) || AverageTonnesPerTeu <= 0)
            throw new ArgumentOutOfRangeException(nameof(AverageTonnesPerTeu), AverageTonnesPerTeu, "Average tonnes per TEU must be finite and positive.");
        ValidateNonNegative(SeaGramsPerTonneKm, nameof(SeaGramsPerTonneKm));
        ValidateNonNegative(RoadGramsPerTonneKm, nameof(RoadGramsPerTonneKm));
        ValidateNonNegative(RailGramsPerTonneKm, nameof(RailGramsPerTonneKm));
        ValidateNonNegative(AirShortHaulGramsPerTonneKm, nameof(AirShortHaulGramsPerTonneKm));
        ValidateNonNegative(AirMediumHaulGramsPerTonneKm, nameof(AirMediumHaulGramsPerTonneKm));
        ValidateNonNegative(AirLongHaulGramsPerTonneKm, nameof(AirLongHaulGramsPerTonneKm));
    }

    private static void ValidateNonNegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name, value, "Emission factors must be finite and non-negative.");
    }
}
