using TradeRouter.Common;
using TradeRouter.Passages;
using TradeRouter.Ports;

namespace TradeRouter;

/// <summary>
/// Options configuring sea route calculations.
/// </summary>
public sealed class TradeRouterOptions
{
    /// <summary>Unit for measuring route distance. Defaults to <see cref="DistanceUnit.Km"/>.</summary>
    public DistanceUnit Units { get; set; } = DistanceUnit.Km;

    /// <summary>Unit as string (e.g., "km", "mi", "naut"). Setting this updates <see cref="Units"/>.</summary>
    public string UnitString
    {
        get => Units.ToUnitString();
        set => Units = DistanceUnitExtensions.Parse(value);
    }

    /// <summary>
    /// Vessel speed in knots (nautical miles per hour). Default is 16 knots, a typical slow-steaming service speed;
    /// the container fleet averaged under 14 knots in 2023 and design speeds of 22 to 25 knots are rarely used.
    /// </summary>
    public double SpeedKnots { get; set; } = 16.0;

    /// <summary>
    /// Whether to explicitly prepend the origin and append the destination coordinates to the route geometry.
    /// Default is false.
    /// </summary>
    public bool AppendOriginDestination { get; set; }

    /// <summary>
    /// Passages to avoid (e.g. Suez, Panama, Gibraltar). Default is empty.
    /// The Northwest Passage is closed separately unless <see cref="AllowNorthwest"/> is true.
    /// </summary>
    public HashSet<string> Restrictions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether routes may use the seasonal, ice-dependent Arctic lanes tagged <see cref="Passage.Northwest"/>.
    /// Default is false, so replacing <see cref="Restrictions"/> never reopens the Arctic.
    /// Listing <see cref="Passage.Northwest"/> in <see cref="Restrictions"/> keeps it closed even when this is true.
    /// </summary>
    public bool AllowNorthwest { get; set; }

    /// <summary>Whether to route through nearest ports close to origin and destination. Default is false.</summary>
    public bool IncludePorts { get; set; }

    /// <summary>Optional port selection and filtering parameters.</summary>
    public PortParameters? PortParameters { get; set; }

    /// <summary>Whether to return the list of traversed passages in the route properties. Default is false.</summary>
    public bool ReturnPassages { get; set; }

    /// <summary>
    /// Whether to return the named sea areas the route passes through, with where it enters and leaves each, in the
    /// route properties. Default is false.
    /// </summary>
    public bool ReturnSeaAreas { get; set; }

    /// <summary>Pathfinding algorithm: "dijkstra" (default) or "astar".</summary>
    public string Algorithm { get; set; } = "dijkstra";

    /// <summary>
    /// How far, in kilometres, a route's origin or destination may lie from the nearest maritime network node before
    /// the route carries a <c>distance_warning</c>. The route joins the network at that node in a straight line, which
    /// may cross land and is not a sailed track. Default is <see cref="DefaultNetworkAccessWarningKm"/>; set
    /// <see cref="double.PositiveInfinity"/> to turn the warning off.
    /// </summary>
    public double NetworkAccessWarningKm { get; set; } = DefaultNetworkAccessWarningKm;

    /// <summary>
    /// The default <see cref="NetworkAccessWarningKm"/>, 50 km: the distance within which the engine also takes a
    /// port-list record to reach the sea network (the engine's private PortListSeaAccessKm). Half of the port list's main terminal ports lie within 15 km of a
    /// node and nine in ten within about 80 km.
    /// </summary>
    public static readonly double DefaultNetworkAccessWarningKm = 50.0;

    /// <summary>
    /// Returns a copy of these options with an independent restriction set.
    /// </summary>
    public TradeRouterOptions Clone() => new()
    {
        Units = Units,
        SpeedKnots = SpeedKnots,
        AppendOriginDestination = AppendOriginDestination,
        Restrictions = Restrictions is null
            ? throw new ArgumentException("Restrictions cannot be null.", nameof(Restrictions))
            : new HashSet<string>(Restrictions, StringComparer.OrdinalIgnoreCase),
        AllowNorthwest = AllowNorthwest,
        IncludePorts = IncludePorts,
        PortParameters = PortParameters?.Clone(),
        ReturnPassages = ReturnPassages,
        ReturnSeaAreas = ReturnSeaAreas,
        Algorithm = Algorithm,
        NetworkAccessWarningKm = NetworkAccessWarningKm
    };

    /// <summary>
    /// Returns every passage closed to the search: <see cref="Restrictions"/>, plus
    /// <see cref="Passage.Northwest"/> unless <see cref="AllowNorthwest"/> is true.
    /// </summary>
    public IReadOnlySet<string> GetClosedPassages()
    {
        if (Restrictions is null)
            throw new ArgumentException("Restrictions cannot be null.", nameof(Restrictions));
        var closed = new HashSet<string>(Restrictions, StringComparer.OrdinalIgnoreCase);
        if (!AllowNorthwest)
            closed.Add(Passage.Northwest);
        return closed;
    }

    /// <summary>Validates values that affect routing, timing, and passage selection.</summary>
    public void Validate()
    {
        if (!Enum.IsDefined(Units))
            throw new ArgumentOutOfRangeException(nameof(Units), Units, "Unknown distance unit.");
        if (!double.IsFinite(SpeedKnots) || SpeedKnots <= 0)
            throw new ArgumentOutOfRangeException(nameof(SpeedKnots), SpeedKnots, "Vessel speed must be finite and positive.");
        if (Restrictions is null)
            throw new ArgumentException("Restrictions cannot be null.", nameof(Restrictions));
        foreach (string restriction in Restrictions)
        {
            if (string.IsNullOrWhiteSpace(restriction) || !Passage.ValidPassages.Contains(restriction.Trim()))
                throw new ArgumentException($"Unknown passage restriction '{restriction}'. Use a value from Passage.ValidPassages.", nameof(Restrictions));
        }
        if (!string.Equals(Algorithm, "dijkstra", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Algorithm, "astar", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unknown pathfinding algorithm '{Algorithm}'. Expected 'dijkstra' or 'astar'.", nameof(Algorithm));
        }
        if (double.IsNaN(NetworkAccessWarningKm) || NetworkAccessWarningKm < 0)
            throw new ArgumentOutOfRangeException(nameof(NetworkAccessWarningKm), NetworkAccessWarningKm, "Network access warning distance must be non-negative.");
        PortParameters?.Validate();
    }
}
