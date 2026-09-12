using System.Collections.ObjectModel;
using TradeRouter.Common;

namespace TradeRouter.Movements;

/// <summary>
/// A typed endpoint in a movement plan. The factory method records both the location code and its transport role.
/// </summary>
public sealed class Waypoint
{
    internal Location Location { get; }

    /// <summary>The endpoint's declared transport role.</summary>
    public WaypointKind Kind { get; }

    /// <summary>The endpoint's UN/LOCODE, or null for a coordinate-only waypoint.</summary>
    public string? Code => Location.Code;

    private Waypoint(Location location, WaypointKind kind)
    {
        Location = location;
        Kind = kind;
    }

    /// <summary>Creates a general place identified by UN/LOCODE.</summary>
    public static Waypoint Place(string unLocode) => FromCode(unLocode, WaypointKind.Place);

    /// <summary>Creates a maritime port identified by UN/LOCODE.</summary>
    public static Waypoint Port(string unLocode) => FromCode(unLocode, WaypointKind.Port);

    /// <summary>Creates an airport identified by UN/LOCODE.</summary>
    public static Waypoint Airport(string unLocode) => FromCode(unLocode, WaypointKind.Airport);

    /// <summary>Creates a rail station identified by UN/LOCODE.</summary>
    public static Waypoint Station(string unLocode) => FromCode(unLocode, WaypointKind.Station);

    /// <summary>Creates a transport terminal identified by UN/LOCODE.</summary>
    public static Waypoint Terminal(string unLocode) => FromCode(unLocode, WaypointKind.Terminal);

    /// <summary>Creates a depot identified by UN/LOCODE.</summary>
    public static Waypoint Depot(string unLocode) => FromCode(unLocode, WaypointKind.Depot);

    /// <summary>Creates an unclassified waypoint at an exact coordinate.</summary>
    public static Waypoint At(Coordinate coordinate, string? name = null) =>
        new(Location.FromCoordinate(coordinate, name), WaypointKind.Unspecified);

    /// <summary>Creates a typed waypoint from a location supplied by the caller.</summary>
    public static Waypoint From(Location location, WaypointKind kind = WaypointKind.Unspecified)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown waypoint kind.");
        return new Waypoint(location, kind);
    }

    private static Waypoint FromCode(string unLocode, WaypointKind kind) =>
        new(Location.FromCode(unLocode), kind);
}

/// <summary>
/// An immutable, fluent description of a continuous multi-leg movement.
/// </summary>
public sealed class MovementPlan
{
    private readonly ReadOnlyCollection<MovementLeg> _legs;
    private readonly Waypoint _current;
    private readonly bool _isComplete;

    /// <summary>The legs added to the plan so far.</summary>
    public IReadOnlyList<MovementLeg> Legs => _legs;

    private MovementPlan(Waypoint current, IEnumerable<MovementLeg> legs, bool isComplete)
    {
        _current = current;
        _legs = new List<MovementLeg>(legs).AsReadOnly();
        _isComplete = isComplete;
    }

    /// <summary>Starts a movement plan at the given waypoint.</summary>
    public static MovementPlan From(Waypoint origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        return new MovementPlan(origin, [], isComplete: false);
    }

    /// <summary>Adds the first-mile pickup leg. A pickup can only be the first leg.</summary>
    public MovementPlan PickupTo(Waypoint destination, TransportMode by)
    {
        if (_legs.Count != 0)
            throw new InvalidOperationException("The pickup leg must be the first leg in a movement plan.");
        return Append(destination, by, LegKind.Pickup, completesPlan: false);
    }

    /// <summary>Adds a main-carriage leg from the previous destination.</summary>
    public MovementPlan ThenTo(Waypoint destination, TransportMode by) =>
        Append(destination, by, LegKind.Main, completesPlan: false);

    /// <summary>Adds the final delivery leg and completes the plan.</summary>
    public MovementPlan DeliverTo(Waypoint destination, TransportMode by) =>
        Append(destination, by, LegKind.Delivery, completesPlan: true);

    private MovementPlan Append(Waypoint destination, TransportMode mode, LegKind kind, bool completesPlan)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (_isComplete)
            throw new InvalidOperationException("No leg can be added after the delivery leg.");
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown transport mode.");

        var leg = new MovementLeg(
            _current.Location,
            destination.Location,
            mode,
            kind,
            _current.Kind,
            destination.Kind);
        var legs = new List<MovementLeg>(_legs) { leg };
        return new MovementPlan(destination, legs, completesPlan);
    }
}
