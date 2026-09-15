using TradeRouter.Common;

namespace TradeRouter.Movements;

/// <summary>Controls how road legs choose between a configured network provider and the built-in estimate.</summary>
public enum RoadRoutingMode
{
    /// <summary>Use a configured provider when possible, otherwise return a labelled built-in estimate.</summary>
    PreferNetworkThenEstimate,

    /// <summary>Require every road leg to be resolved by a configured network provider or imported upstream result.</summary>
    RequireNetwork,

    /// <summary>Do not call a configured provider; use the built-in estimate or an imported upstream result.</summary>
    EstimateOnly
}

/// <summary>Controls how travelling time is calculated for road legs.</summary>
public enum RoadDurationMode
{
    /// <summary>Calculate duration from routed or estimated distance and the configured road speed.</summary>
    ConfiguredSpeed,

    /// <summary>
    /// Use a duration returned by a road provider or authoritative imported route when available;
    /// otherwise calculate it from distance and the configured road speed.
    /// </summary>
    RouteDurationWhenAvailable
}

/// <summary>Outcome of asking an external provider to route one road leg.</summary>
public enum RoadRouteStatus
{
    /// <summary>A road-network route was found.</summary>
    Success,

    /// <summary>One or both endpoints lie outside the provider's loaded road-network coverage.</summary>
    OutsideCoverage,

    /// <summary>The endpoints are covered, but the provider found no traversable route.</summary>
    NoRoute,

    /// <summary>The provider could not answer because it was unavailable or returned an unusable response.</summary>
    Unavailable
}

/// <summary>Resolved input passed to a road-network provider.</summary>
/// <param name="Sequence">One-based movement-leg sequence.</param>
/// <param name="From">Resolved road origin.</param>
/// <param name="To">Resolved road destination.</param>
public sealed record RoadRouteRequest(int Sequence, ResolvedLocation From, ResolvedLocation To);

/// <summary>A road-network provider's typed result.</summary>
public sealed record RoadRouteResult
{
    /// <summary>Provider outcome.</summary>
    public required RoadRouteStatus Status { get; init; }

    /// <summary>Routed distance in kilometres. Required for a successful result.</summary>
    public double? DistanceKm { get; init; }

    /// <summary>Provider-modelled travelling time in hours, when available.</summary>
    public double? DurationHours { get; init; }

    /// <summary>Road geometry in traversal order, when available.</summary>
    public IReadOnlyList<Coordinate>? Geometry { get; init; }

    /// <summary>Stable provider identifier, for example "osrm".</summary>
    public required string Source { get; init; }

    /// <summary>Routing profile used by the provider, for example "driving".</summary>
    public string? Profile { get; init; }

    /// <summary>Optional caller-defined provenance label for the provider's road dataset.</summary>
    public string? DataVersion { get; init; }

    /// <summary>Human-readable reason for a non-success outcome.</summary>
    public string? Message { get; init; }
}

/// <summary>Supplies road-network distance, duration and geometry for resolved movement legs.</summary>
public interface IRoadRouteProvider
{
    /// <summary>Routes one road leg without blocking the calling thread.</summary>
    ValueTask<RoadRouteResult> RouteAsync(RoadRouteRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Input to the deterministic fallback estimator.</summary>
/// <param name="Sequence">One-based movement-leg sequence.</param>
/// <param name="From">Resolved road origin.</param>
/// <param name="To">Resolved road destination.</param>
/// <param name="StraightLineDistanceKm">Great-circle lower bound in kilometres.</param>
public sealed record RoadDistanceEstimateRequest(
    int Sequence,
    ResolvedLocation From,
    ResolvedLocation To,
    double StraightLineDistanceKm);

/// <summary>A deterministic road-distance estimate and the model that produced it.</summary>
/// <param name="DistanceKm">Estimated road distance in kilometres.</param>
/// <param name="Model">Stable estimator identifier.</param>
/// <param name="Warning">Explanation of the estimate's limitations.</param>
public sealed record RoadDistanceEstimate(double DistanceKm, string Model, string Warning);

/// <summary>Estimates road distance when no road network is available.</summary>
public interface IRoadDistanceEstimator
{
    /// <summary>Estimates a road distance from resolved endpoints and their great-circle separation.</summary>
    RoadDistanceEstimate Estimate(RoadDistanceEstimateRequest request);
}

/// <summary>
/// Estimates road distance with a distance-decay circuity model. The road-to-straight-line ratio is higher
/// for short journeys and gradually declines with distance, reflecting the proportionally larger effect of
/// local access and road-network topology near each endpoint.
/// </summary>
public sealed class DistanceDecayRoadDistanceEstimator : IRoadDistanceEstimator
{
    /// <summary>Coefficient fitted to the documented OSRM calibration set, with distance expressed in kilometres.</summary>
    public const double DefaultCoefficient = 1.6173976178145946;

    /// <summary>Exponent fitted to the documented OSRM calibration set.</summary>
    public const double DefaultExponent = 0.9579701056959417;

    /// <summary>Shared default estimator.</summary>
    public static DistanceDecayRoadDistanceEstimator Default { get; } = new();

    /// <summary>Multiplier in the power function, where input and output are kilometres.</summary>
    public double Coefficient { get; }

    /// <summary>Exponent in the power function. Values below one make circuity decline with distance.</summary>
    public double Exponent { get; }

    /// <summary>
    /// Creates an estimator for <c>coefficient × straightLineDistance^exponent</c>. The coefficient must be
    /// finite and at least one; the exponent must be finite, greater than zero and no greater than one.
    /// </summary>
    public DistanceDecayRoadDistanceEstimator(
        double coefficient = DefaultCoefficient,
        double exponent = DefaultExponent)
    {
        if (!double.IsFinite(coefficient) || coefficient < 1.0)
            throw new ArgumentOutOfRangeException(nameof(coefficient), coefficient, "The coefficient must be finite and at least 1.");
        if (!double.IsFinite(exponent) || exponent <= 0.0 || exponent > 1.0)
            throw new ArgumentOutOfRangeException(nameof(exponent), exponent, "The exponent must be finite, greater than 0 and no greater than 1.");

        Coefficient = coefficient;
        Exponent = exponent;
    }

    /// <inheritdoc />
    public RoadDistanceEstimate Estimate(RoadDistanceEstimateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!double.IsFinite(request.StraightLineDistanceKm) || request.StraightLineDistanceKm < 0.0)
            throw new ArgumentOutOfRangeException(nameof(request), "Straight-line distance must be finite and non-negative.");

        double straightLineDistanceKm = request.StraightLineDistanceKm;
        double modelledDistanceKm = straightLineDistanceKm == 0.0
            ? 0.0
            : Coefficient * Math.Pow(straightLineDistanceKm, Exponent);

        return new RoadDistanceEstimate(
            Math.Max(straightLineDistanceKm, modelledDistanceKm),
            FormattableString.Invariant($"distance-decay-{Coefficient:0.#####}x^{Exponent:0.#####}"),
            "Estimated with a distance-decay circuity model because no road-network route was available.");
    }
}

/// <summary>
/// Applies a configurable road-circuity factor to the great-circle lower bound. This is a coarse planning
/// estimate, not a road-network route; callers needing route-specific accuracy should configure a provider.
/// </summary>
public sealed class CircuityRoadDistanceEstimator : IRoadDistanceEstimator
{
    /// <summary>Default road-to-great-circle multiplier used by the built-in fallback.</summary>
    public const double DefaultFactor = 1.3;

    /// <summary>Shared default estimator.</summary>
    public static CircuityRoadDistanceEstimator Default { get; } = new();

    /// <summary>Road-to-great-circle multiplier.</summary>
    public double Factor { get; }

    /// <summary>Creates an estimator with the supplied multiplier, which must be finite and at least one.</summary>
    public CircuityRoadDistanceEstimator(double factor = DefaultFactor)
    {
        if (!double.IsFinite(factor) || factor < 1.0)
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "The road circuity factor must be finite and at least 1.");
        Factor = factor;
    }

    /// <inheritdoc />
    public RoadDistanceEstimate Estimate(RoadDistanceEstimateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!double.IsFinite(request.StraightLineDistanceKm) || request.StraightLineDistanceKm < 0.0)
            throw new ArgumentOutOfRangeException(nameof(request), "Straight-line distance must be finite and non-negative.");

        return new RoadDistanceEstimate(
            request.StraightLineDistanceKm * Factor,
            FormattableString.Invariant($"circuity-{Factor:0.###}"),
            "Estimated from great-circle distance because no road-network route was available.");
    }
}

/// <summary>
/// A complete road result imported from an authoritative upstream routing system. Application and sample code should
/// use <see cref="IRoadRouteProvider"/> or <see cref="IRoadDistanceEstimator"/> rather than constructing fixed values.
/// </summary>
public sealed record SuppliedRoadRoute
{
    /// <summary>Road distance in kilometres.</summary>
    public required double DistanceKm { get; init; }

    /// <summary>Travelling time in hours, when known.</summary>
    public double? DurationHours { get; init; }

    /// <summary>Road geometry in traversal order, when known.</summary>
    public IReadOnlyList<Coordinate>? Geometry { get; init; }

    /// <summary>Caller-defined provenance label.</summary>
    public string Source { get; init; } = "caller";
}
