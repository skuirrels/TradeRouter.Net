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
    /// Passages to avoid (e.g. Suez, Panama, Gibraltar).
    /// Default is [Passage.Northwest].
    /// </summary>
    public HashSet<string> Restrictions { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        Passage.Northwest
    };

    /// <summary>Whether to route through nearest ports close to origin and destination. Default is false.</summary>
    public bool IncludePorts { get; set; }

    /// <summary>Optional port selection and filtering parameters.</summary>
    public PortParameters? PortParameters { get; set; }

    /// <summary>Whether to return the list of traversed passages in the route properties. Default is false.</summary>
    public bool ReturnPassages { get; set; }

    /// <summary>Pathfinding algorithm: "dijkstra" (default) or "astar".</summary>
    public string Algorithm { get; set; } = "dijkstra";

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
        IncludePorts = IncludePorts,
        PortParameters = PortParameters?.Clone(),
        ReturnPassages = ReturnPassages,
        Algorithm = Algorithm
    };

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
        PortParameters?.Validate();
    }
}
