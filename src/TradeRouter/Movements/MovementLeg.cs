namespace TradeRouter.Movements;

/// <summary>
/// One leg of a movement: from one location to another by one transport mode.
/// </summary>
public sealed class MovementLeg
{
    /// <summary>Start of the leg.</summary>
    public Location From { get; }

    /// <summary>End of the leg.</summary>
    public Location To { get; }

    /// <summary>Transport mode used for the leg.</summary>
    public TransportMode Mode { get; }

    /// <summary>Role of the leg within the movement.</summary>
    public LegKind Kind { get; }

    /// <summary>Declared semantic type of the start point.</summary>
    public WaypointKind FromKind { get; }

    /// <summary>Declared semantic type of the end point.</summary>
    public WaypointKind ToKind { get; }

    /// <summary>For an air leg, whether the cargo flies on a freighter or as belly freight; other modes ignore it.</summary>
    public AirCargoService AirService { get; }

    /// <summary>
    /// Initializes a new leg.
    /// </summary>
    public MovementLeg(
        Location from,
        Location to,
        TransportMode mode,
        LegKind kind = LegKind.Main,
        WaypointKind fromKind = WaypointKind.Unspecified,
        WaypointKind toKind = WaypointKind.Unspecified,
        AirCargoService airService = AirCargoService.Unknown)
    {
        From = from ?? throw new ArgumentNullException(nameof(from));
        To = to ?? throw new ArgumentNullException(nameof(to));
        Mode = mode;
        Kind = kind;
        FromKind = fromKind;
        ToKind = toKind;
        AirService = airService;
    }

    /// <summary>Convenience constructor from two location codes.</summary>
    public MovementLeg(
        string fromCode,
        string toCode,
        TransportMode mode,
        LegKind kind = LegKind.Main,
        WaypointKind fromKind = WaypointKind.Unspecified,
        WaypointKind toKind = WaypointKind.Unspecified,
        AirCargoService airService = AirCargoService.Unknown)
        : this(Location.FromCode(fromCode), Location.FromCode(toCode), mode, kind, fromKind, toKind, airService)
    {
    }

    /// <inheritdoc />
    public override string ToString() => $"{Kind} {From} to {To} {Mode}";
}
