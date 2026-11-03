using TradeRouter.Movements;

namespace TradeRouter.Caching;

/// <summary>
/// Stores road-route results for <see cref="CachingRoadRouteProvider"/>. <see cref="DistributedRoadRouteCache"/> stores
/// them in any <c>IDistributedCache</c>; implement this interface to keep them somewhere else, such as a database table
/// you want to query.
/// </summary>
/// <remarks>
/// One instance may be shared by concurrent movement calculations, so implementations must be thread-safe. Throw for
/// store failures rather than hiding them: the caching provider reports them and routes the leg without the cache.
/// </remarks>
public interface IRoadRouteCache
{
    /// <summary>Returns the stored result for the key, or null when there is none or it has expired.</summary>
    ValueTask<RoadRouteResult?> GetAsync(RoadRouteCacheKey key, CancellationToken cancellationToken = default);

    /// <summary>Stores a result for the key, replacing any earlier one, and removes it once the time to live has passed.</summary>
    ValueTask SetAsync(
        RoadRouteCacheKey key,
        RoadRouteResult result,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default);
}
