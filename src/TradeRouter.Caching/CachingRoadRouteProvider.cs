using TradeRouter.Movements;

namespace TradeRouter.Caching;

/// <summary>
/// Wraps a road-route provider and serves repeated legs from an <see cref="IRoadRouteCache"/>. Successful routes are
/// kept for <see cref="RoadRouteCacheOptions.TimeToLive"/> from the time they were fetched, <c>NoRoute</c> and
/// <c>OutsideCoverage</c> results only when <see cref="RoadRouteCacheOptions.NegativeTimeToLive"/> is set, and
/// <c>Unavailable</c> results never.
/// </summary>
/// <remarks>
/// Google Maps Platform terms do not allow Routes API distances, durations or geometry to be cached, so the provider
/// refuses to wrap <see cref="GoogleRoutesRoadRouteProvider"/> and never stores a result whose source is
/// <see cref="GoogleRoutesRoadRouteProvider.SourceName"/>. Concurrent requests for the same uncached leg each call the
/// wrapped provider.
/// </remarks>
public sealed class CachingRoadRouteProvider : IRoadRouteProvider
{
    private readonly IRoadRouteProvider _inner;
    private readonly IRoadRouteCache _cache;
    private readonly RoadRouteCacheOptions _options;
    private readonly string _partition;

    /// <summary>Creates a caching provider.</summary>
    /// <param name="inner">Provider that routes legs not found in the cache.</param>
    /// <param name="cache">Store for the results.</param>
    /// <param name="options">Partition, times to live and key settings.</param>
    public CachingRoadRouteProvider(IRoadRouteProvider inner, IRoadRouteCache cache, RoadRouteCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);
        if (inner is GoogleRoutesRoadRouteProvider)
        {
            throw new ArgumentException(
                "Google Maps Platform terms do not allow Routes API results to be cached.",
                nameof(inner));
        }

        RoadRouteCacheKey.ValidatePartition(options.Partition);
        ValidateTimeToLive(options.TimeToLive, nameof(options.TimeToLive));
        if (options.NegativeTimeToLive is { } negative)
            ValidateTimeToLive(negative, nameof(options.NegativeTimeToLive));
        RoadRouteCacheKey.ValidateCoordinateDecimals(options.CoordinateDecimals);

        _inner = inner;
        _cache = cache;
        _options = options;
        _partition = options.Partition.Trim();
    }

    /// <inheritdoc />
    public async ValueTask<RoadRouteResult> RouteAsync(
        RoadRouteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = RoadRouteCacheKey.CreateValidated(_partition, request, _options.CoordinateDecimals);

        RoadRouteResult? cached = null;
        try
        {
            cached = await _cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
        {
            ReportCacheError(ex);
        }

        if (cached is not null)
        {
            if (IsUsable(cached))
                return cached;
            ReportCacheError(new InvalidDataException(
                $"The cache returned an unusable {cached.Status} result for '{key.Value}'; the leg was routed again."));
        }

        var result = await _inner.RouteAsync(request, cancellationToken).ConfigureAwait(false);
        if (TimeToLiveFor(result) is not { } timeToLive)
            return result;

        try
        {
            var stored = _options.CacheGeometry ? result : result with { Geometry = null };
            await _cache.SetAsync(key, stored, timeToLive, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
        {
            ReportCacheError(ex);
        }

        return result;
    }

    private TimeSpan? TimeToLiveFor(RoadRouteResult result)
    {
        if (string.Equals(result.Source, GoogleRoutesRoadRouteProvider.SourceName, StringComparison.Ordinal))
            return null;

        return result.Status switch
        {
            RoadRouteStatus.Success when result.DistanceKm is not null => _options.TimeToLive,
            RoadRouteStatus.NoRoute or RoadRouteStatus.OutsideCoverage => _options.NegativeTimeToLive,
            _ => null
        };
    }

    // A stored success must pass the checks the engine applies to a provider's result, so a bad entry is routed
    // again rather than failing the movement.
    private static bool IsUsable(RoadRouteResult result) => result.Status switch
    {
        RoadRouteStatus.Success =>
            result.DistanceKm is { } distance && double.IsFinite(distance) && distance >= 0.0
            && (result.DurationHours is not { } duration || (double.IsFinite(duration) && duration >= 0.0))
            && result.Geometry is not { Count: 1 }
            && !string.IsNullOrWhiteSpace(result.Source),
        RoadRouteStatus.NoRoute or RoadRouteStatus.OutsideCoverage => !string.IsNullOrWhiteSpace(result.Source),
        _ => false
    };

    // The callback is for reporting only; an exception from it must not fail the movement the cache was saving.
    private void ReportCacheError(Exception ex)
    {
        try
        {
            _options.OnCacheError?.Invoke(ex);
        }
        catch (Exception)
        {
        }
    }

    private static bool IsCallerCancellation(Exception ex, CancellationToken cancellationToken) =>
        ex is OperationCanceledException && cancellationToken.IsCancellationRequested;

    private static void ValidateTimeToLive(TimeSpan timeToLive, string name)
    {
        if (timeToLive <= TimeSpan.Zero || timeToLive > RoadRouteCacheOptions.MaximumTimeToLive)
        {
            throw new ArgumentOutOfRangeException(
                name,
                timeToLive,
                $"A cache time to live must be positive and at most {RoadRouteCacheOptions.MaximumTimeToLive.TotalDays:N0} days.");
        }
    }
}
