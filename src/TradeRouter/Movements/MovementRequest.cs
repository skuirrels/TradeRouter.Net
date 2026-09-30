using TradeRouter.Common;
using TradeRouter.Locations;

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
    /// Functions to add to a location's recorded UN/LOCODE functions, keyed case-insensitively by code. The UNECE
    /// list under-records many terminals, for example Chongqing (<c>CNCKG</c>) appears only as an airport, so a rail
    /// leg to it is rejected. Declaring <see cref="LocationFunctions.RailTerminal"/> here lets the waypoint and mode
    /// checks accept it. The functions are added only for validation; they do not change how the code resolves.
    /// </summary>
    public Dictionary<string, LocationFunctions> AdditionalLocationFunctions { get; } = new(StringComparer.OrdinalIgnoreCase);

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
    /// per-TEU rate instead of the per-tonne rate, and road and rail legs are charged on the greater of
    /// <see cref="CargoTonnes"/> and the GLEC average of 10 t per TEU, since a light container still needs a whole
    /// truck or wagon slot. Air legs use <see cref="CargoTonnes"/>, or the TEU average when no weight is given.
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

    /// <summary>
    /// Optional road-network provider, such as <see cref="OsrmRoadRouteProvider"/>. It is used only by
    /// <c>CalculateMovementAsync</c>; synchronous movement calculation rejects a request that expects to call it.
    /// </summary>
    public IRoadRouteProvider? RoadRouteProvider { get; set; }

    /// <summary>
    /// Controls rail legs. By default a leg whose ends both lie near the embedded North American rail network is
    /// routed on it; any other rail leg keeps great-circle distance and carries a warning.
    /// </summary>
    public RailRoutingMode RailRoutingMode { get; set; } = RailRoutingMode.PreferNetworkThenGreatCircle;

    /// <summary>
    /// Furthest a rail leg's end may lie from the nearest point of the rail network, in kilometres, for the leg to
    /// be routed on it. The default is 25 km, enough for a UN/LOCODE city position to reach a line through its
    /// city. The gap at each end is added to the leg as straight-line distance.
    /// </summary>
    public double RailNetworkSnapKm { get; set; } = 25.0;

    /// <summary>Controls provider use and fallback behavior for road legs.</summary>
    public RoadRoutingMode RoadRoutingMode { get; set; } = RoadRoutingMode.PreferNetworkThenEstimate;

    /// <summary>
    /// Controls road travelling-time calculation. The default uses routed or estimated distance divided by
    /// <see cref="SpeedsKmh"/> for road. Set this to <see cref="Movements.RoadDurationMode.RouteDurationWhenAvailable"/>
    /// to prefer a duration returned by a route provider or authoritative imported route.
    /// </summary>
    public RoadDurationMode RoadDurationMode { get; set; } = Movements.RoadDurationMode.ConfiguredSpeed;

    /// <summary>
    /// Deterministic fallback used when a road-network route is not configured or lies outside its coverage.
    /// Defaults to a calibrated distance-decay circuity model. Set a different implementation to override it.
    /// </summary>
    public IRoadDistanceEstimator RoadDistanceEstimator { get; set; } = DistanceDecayRoadDistanceEstimator.Default;

    /// <summary>
    /// Complete road results imported from an authoritative upstream routing system, keyed by one-based leg sequence.
    /// These take precedence over both a configured provider and the fallback estimator. Do not use fixed literals here
    /// to force an expected result; application and sample code should use the estimator or a configured provider.
    /// </summary>
    public Dictionary<int, SuppliedRoute> RoadRouteOverrides { get; } = [];

    /// <summary>
    /// Complete rail results imported from an authoritative upstream routing system, keyed by one-based leg sequence.
    /// An entry takes precedence over the embedded rail network and great-circle distance, so use it where the
    /// network has no coverage or a rail planner knows the actual route.
    /// A supplied duration is used as given; otherwise duration is the supplied distance divided by
    /// <see cref="SpeedsKmh"/> for rail.
    /// </summary>
    public Dictionary<int, SuppliedRoute> RailRouteOverrides { get; } = [];
}
