using TradeRouter.Common;

namespace TradeRouter.Movements;

/// <summary>
/// A complete road or rail result imported from an authoritative upstream routing system. Application and sample
/// code should use <see cref="IRoadRouteProvider"/> or <see cref="IRoadDistanceEstimator"/> for road legs rather
/// than constructing fixed values.
/// </summary>
public sealed record SuppliedRoute
{
    /// <summary>Route distance in kilometres.</summary>
    public required double DistanceKm { get; init; }

    /// <summary>Travelling time in hours, when known.</summary>
    public double? DurationHours { get; init; }

    /// <summary>Route geometry in traversal order, when known.</summary>
    public IReadOnlyList<Coordinate>? Geometry { get; init; }

    /// <summary>Caller-defined provenance label.</summary>
    public string Source { get; init; } = "caller";
}
