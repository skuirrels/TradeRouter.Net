namespace TradeRouter.Caching;

/// <summary>Settings for <see cref="CachingRoadRouteProvider"/>.</summary>
public sealed record RoadRouteCacheOptions
{
    /// <summary>
    /// Longest time to live accepted, 100 years. Distributed caches add the time to live to the current time, so a
    /// value such as <see cref="TimeSpan.MaxValue"/> would overflow on every write.
    /// </summary>
    public static TimeSpan MaximumTimeToLive { get; } = TimeSpan.FromDays(36_525);

    /// <summary>
    /// Name for the provider, profile and road dataset the cached routes come from, for example
    /// <c>osrm-driving-europe-261001</c>. Change it when any of them changes, so routes from the old setup are no
    /// longer served. It cannot be blank or contain <c>|</c>.
    /// </summary>
    public required string Partition { get; init; }

    /// <summary>
    /// How long a successful route is kept from the time it was fetched. Set it from the provider's terms: some allow
    /// caching only for a limited number of days, and some not at all. It must be positive and at most
    /// <see cref="MaximumTimeToLive"/>.
    /// </summary>
    public required TimeSpan TimeToLive { get; init; }

    /// <summary>
    /// How long a <c>NoRoute</c> or <c>OutsideCoverage</c> result is kept, or null to fetch those again every time.
    /// <c>Unavailable</c> results are never cached.
    /// </summary>
    public TimeSpan? NegativeTimeToLive { get; init; }

    /// <summary>
    /// Decimal places the endpoint coordinates are rounded to in the key, from 0 to 10. The default of 5, about a
    /// metre, only absorbs floating-point noise; lower it to share entries between nearby points.
    /// </summary>
    public int CoordinateDecimals { get; init; } = 5;

    /// <summary>
    /// Whether route geometry is stored. Without it an entry holds only distance, duration and provenance, which is
    /// much smaller, but legs served from the cache have no road geometry and are drawn as straight lines.
    /// </summary>
    public bool CacheGeometry { get; init; } = true;

    /// <summary>
    /// Called when the store throws while reading or writing, or returns a result that cannot be used. The leg is then
    /// routed by the wrapped provider as if nothing were cached, so a store outage costs provider calls rather than
    /// failing movements. Exceptions thrown by the callback are ignored for the same reason.
    /// </summary>
    public Action<Exception>? OnCacheError { get; init; }
}
