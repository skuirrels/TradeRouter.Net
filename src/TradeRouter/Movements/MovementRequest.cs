using TradeRouter.Common;

namespace TradeRouter.Movements;

/// <summary>
/// A multi-leg movement to be routed: an ordered list of legs plus the options and lookups needed to route them.
/// </summary>
public sealed class MovementRequest
{
    /// <summary>Ordered legs of the movement.</summary>
    public List<MovementLeg> Legs { get; init; } = [];

    /// <summary>
    /// Coordinates for location codes, keyed case-insensitively by code. Checked before the embedded port
    /// database, so an entry here overrides a port's stored position; the port record is then not attached
    /// to the leg. Use this when the embedded list disagrees with your code conventions, for example CNSHG,
    /// which the list holds as Sanshan rather than the Port of Shanghai.
    /// </summary>
    public Dictionary<string, Coordinate> Coordinates { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Optional resolver consulted last, for codes that are neither in <see cref="Coordinates"/> nor in the
    /// embedded port database.
    /// </summary>
    public ILocationResolver? Resolver { get; set; }

    /// <summary>
    /// Options applied to every sea leg. Units and vessel speed here also govern the totals.
    /// <see cref="TradeRouterOptions.AppendOriginDestination"/> is always treated as true for movement legs so that
    /// consecutive legs join end to end.
    /// </summary>
    public TradeRouterOptions? SeaOptions { get; set; }

    /// <summary>
    /// CO2e intensity per mode used for the emission figures on each leg. Defaults to the GLEC values.
    /// </summary>
    public EmissionFactors Emissions { get; set; } = EmissionFactors.GlecDefaults;

    /// <summary>
    /// Cargo weight in tonnes, gross physical weight. When set, each leg and the totals also report absolute
    /// CO2e in kilograms; otherwise only the per-tonne figures are reported.
    /// </summary>
    public double? CargoTonnes { get; set; }

    /// <summary>
    /// Container count in TEU (a 40-foot box is 2, a 40-foot high cube 2.25). When set, sea legs use the
    /// per-TEU rate instead of the per-tonne rate. Road, rail and air legs still use tonnes; if
    /// <see cref="CargoTonnes"/> is not given they assume the GLEC average of 10 t per TEU.
    /// </summary>
    public double? CargoTeu { get; set; }

    /// <summary>
    /// Hours spent handling cargo at each end of every sea leg. The default is 24 hours for loading or
    /// discharge. A consecutive sea leg also receives <see cref="TransshipmentConnectionHours"/>.
    /// </summary>
    public double PortDwellHours { get; set; } = 24.0;

    /// <summary>
    /// Fraction of sea travelling time added for normal service operations that shortest-path geometry cannot
    /// represent: intermediate port calls, restricted-water slowdowns, pilotage and berth approaches.
    /// Leave null, the default, and each sea leg takes the fraction for its trade corridor from
    /// <see cref="SeaServiceAllowance"/>, which was fitted to observed port-to-port sailings. Set a value to
    /// apply one fraction to every sea leg instead; 0 gives pure distance-divided-by-speed time.
    /// </summary>
    public double? SeaOperationalAllowance { get; set; }

    /// <summary>
    /// Connection time added before each sea leg that immediately follows another sea leg. The default is
    /// 48 hours. Set to zero when consecutive sea legs represent a through service rather than a transshipment.
    /// </summary>
    public double TransshipmentConnectionHours { get; set; } = 48.0;

    /// <summary>
    /// Assumed average speed in kilometres per hour for each non-sea mode, used to estimate leg duration.
    /// Defaults: Road 60, Rail 80, Air 800. Sea speed comes from <see cref="SeaOptions"/>.
    /// </summary>
    public Dictionary<TransportMode, double> SpeedsKmh { get; } = new()
    {
        [TransportMode.Road] = 60.0,
        [TransportMode.Rail] = 80.0,
        [TransportMode.Air] = 800.0
    };
}
