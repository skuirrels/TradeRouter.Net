namespace TradeRouter.Movements;

/// <summary>Controls whether rail legs are routed on the embedded rail network or measured as a straight line.</summary>
public enum RailRoutingMode
{
    /// <summary>
    /// Route on the embedded North American rail network when both ends lie within
    /// <see cref="MovementRequest.RailNetworkSnapKm"/> of a connected line; otherwise use great-circle distance
    /// with a warning.
    /// </summary>
    PreferNetworkThenGreatCircle,

    /// <summary>Always use great-circle distance, as before the rail network was embedded.</summary>
    GreatCircleOnly
}
