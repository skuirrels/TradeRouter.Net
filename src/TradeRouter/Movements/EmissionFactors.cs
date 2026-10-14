namespace TradeRouter.Movements;

/// <summary>
/// Well-to-wheel CO2e emission intensity per transport mode, in grams of CO2e per tonne-kilometre.
/// Defaults are the GLEC Framework v3.2 default values (Smart Freight Centre, October 2025, Module 2), which ISO 14083
/// builds on. Override any property to use carrier-specific or newer factors.
/// </summary>
public sealed class EmissionFactors
{
    /// <summary>A fresh instance carrying the GLEC default values.</summary>
    public static EmissionFactors GlecDefaults => new();

    /// <summary>
    /// Deep-sea container shipping per container. GLEC v3.2 Table 18, industry average dry container, end-user
    /// intensity, 71.7 g CO2e per TEU-km WTW. Used for sea legs when the request states a TEU count.
    /// </summary>
    public double SeaGramsPerTeuKm { get; set; } = 71.7;

    /// <summary>GLEC average cargo weight per TEU, 10 tonnes, used to convert between the two sea bases.</summary>
    public double AverageTonnesPerTeu { get; set; } = 10.0;

    /// <summary>
    /// Deep-sea container shipping per tonne: <see cref="SeaGramsPerTeuKm"/> divided by <see cref="AverageTonnesPerTeu"/>.
    /// Used for sea legs when only a cargo weight is known.
    /// </summary>
    public double SeaGramsPerTonneKm { get; set; } = 7.17;

    /// <summary>
    /// Road haulage. GLEC v3.2 Europe starting value for an HGV over 20 t GVW, 125 g CO2e per tonne-km WTW, including
    /// GLEC's +5% distance adjustment.
    /// </summary>
    public double RoadGramsPerTonneKm { get; set; } = 125.0;

    /// <summary>
    /// Rail freight outside North America. GLEC v3.2 Table 4, European diesel traction, average/mixed load,
    /// 30.7 g CO2e per tonne-km WTW.
    /// </summary>
    public double RailGramsPerTonneKm { get; set; } = 30.7;

    /// <summary>
    /// Rail freight in North America: a rail leg whose ends both lie within
    /// <see cref="MovementRequest.RailNetworkSnapKm"/> of the embedded North American rail network. GLEC v3.2 Module 2,
    /// North America, US average diesel, 16.1 g CO2e per tonne-km WTW, derived from Class I railroad fuel reporting.
    /// </summary>
    public double RailNorthAmericaGramsPerTonneKm { get; set; } = 16.1;

    /// <summary>
    /// Great-circle length below which an air leg is short-haul, 1,500 km as in GLEC v3.2 Table 1 (SBTi and ISO 14083
    /// definitions). The air factors already include GLEC's +95 km distance adjustment, so they apply to the
    /// great-circle distance of the leg as it stands.
    /// </summary>
    public double AirShortHaulMaxKm { get; set; } = 1500.0;

    /// <summary>Freighter, short-haul. GLEC v3.2 Table 1, 1,516 g CO2e per tonne-km WTW.</summary>
    public double AirFreighterShortHaulGramsPerTonneKm { get; set; } = 1516.0;

    /// <summary>Freighter, long-haul. GLEC v3.2 Table 1, 608 g CO2e per tonne-km WTW.</summary>
    public double AirFreighterLongHaulGramsPerTonneKm { get; set; } = 608.0;

    /// <summary>Belly freight on a passenger aircraft, short-haul. GLEC v3.2 Table 1, 1,239 g CO2e per tonne-km WTW.</summary>
    public double AirBellyShortHaulGramsPerTonneKm { get; set; } = 1239.0;

    /// <summary>Belly freight on a passenger aircraft, long-haul. GLEC v3.2 Table 1, 936 g CO2e per tonne-km WTW.</summary>
    public double AirBellyLongHaulGramsPerTonneKm { get; set; } = 936.0;

    /// <summary>Air freight on an unknown service, short-haul. GLEC v3.2 Table 1, 1,363 g CO2e per tonne-km WTW.</summary>
    public double AirUnknownShortHaulGramsPerTonneKm { get; set; } = 1363.0;

    /// <summary>Air freight on an unknown service, long-haul. GLEC v3.2 Table 1, 788 g CO2e per tonne-km WTW.</summary>
    public double AirUnknownLongHaulGramsPerTonneKm { get; set; } = 788.0;

    /// <summary>
    /// Returns the factor for a leg of the given mode and length. Air uses the GLEC short- and long-haul bands for
    /// the service, unknown unless given. Rail returns <see cref="RailGramsPerTonneKm"/>; a movement charges North
    /// American rail legs at <see cref="RailNorthAmericaGramsPerTonneKm"/> instead.
    /// </summary>
    public double GramsPerTonneKm(TransportMode mode, double distanceKm, AirCargoService airService = AirCargoService.Unknown) => mode switch
    {
        TransportMode.Sea => SeaGramsPerTonneKm,
        TransportMode.Road => RoadGramsPerTonneKm,
        TransportMode.Rail => RailGramsPerTonneKm,
        TransportMode.Air => (airService, distanceKm < AirShortHaulMaxKm) switch
        {
            (AirCargoService.Freighter, true) => AirFreighterShortHaulGramsPerTonneKm,
            (AirCargoService.Freighter, false) => AirFreighterLongHaulGramsPerTonneKm,
            (AirCargoService.Belly, true) => AirBellyShortHaulGramsPerTonneKm,
            (AirCargoService.Belly, false) => AirBellyLongHaulGramsPerTonneKm,
            (_, true) => AirUnknownShortHaulGramsPerTonneKm,
            _ => AirUnknownLongHaulGramsPerTonneKm
        },
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
        ValidateNonNegative(RailNorthAmericaGramsPerTonneKm, nameof(RailNorthAmericaGramsPerTonneKm));
        ValidateNonNegative(AirShortHaulMaxKm, nameof(AirShortHaulMaxKm));
        ValidateNonNegative(AirFreighterShortHaulGramsPerTonneKm, nameof(AirFreighterShortHaulGramsPerTonneKm));
        ValidateNonNegative(AirFreighterLongHaulGramsPerTonneKm, nameof(AirFreighterLongHaulGramsPerTonneKm));
        ValidateNonNegative(AirBellyShortHaulGramsPerTonneKm, nameof(AirBellyShortHaulGramsPerTonneKm));
        ValidateNonNegative(AirBellyLongHaulGramsPerTonneKm, nameof(AirBellyLongHaulGramsPerTonneKm));
        ValidateNonNegative(AirUnknownShortHaulGramsPerTonneKm, nameof(AirUnknownShortHaulGramsPerTonneKm));
        ValidateNonNegative(AirUnknownLongHaulGramsPerTonneKm, nameof(AirUnknownLongHaulGramsPerTonneKm));
    }

    private static void ValidateNonNegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name, value, "Emission factors must be finite and non-negative.");
    }
}
