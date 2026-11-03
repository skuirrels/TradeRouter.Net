# TradeRouter.Net.Caching

Caching for [TradeRouter.Net](https://www.nuget.org/packages/TradeRouter.Net) road-route providers. `CachingRoadRouteProvider` wraps any `IRoadRouteProvider` and serves repeated road legs from a store, so the same pickup or delivery leg is routed once per time to live instead of once per movement.

`DistributedRoadRouteCache` keeps results in any `IDistributedCache`: Redis, SQL Server, PostgreSQL, or the in-memory cache for tests. Implement `IRoadRouteCache` to keep them somewhere else, such as a database table you want to query.

```csharp
// The application registers the IDistributedCache it uses, for example
// services.AddStackExchangeRedisCache(...) or services.AddDistributedSqlServerCache(...).
var request = new MovementRequest
{
    Legs = plan.Legs.ToList(),
    RoadRouteProvider = new CachingRoadRouteProvider(
        new OsrmRoadRouteProvider(httpClient, osrmUri),
        new DistributedRoadRouteCache(distributedCache),
        new RoadRouteCacheOptions
        {
            Partition = configuration["RoadRouting:CachePartition"]!,
            TimeToLive = TimeSpan.FromDays(configuration.GetValue<int>("RoadRouting:CacheDays"))
        }),
    RoadRoutingMode = RoadRoutingMode.PreferNetworkThenEstimate
};
```

Successful routes are kept for `TimeToLive` from the time they were fetched. `NoRoute` and `OutsideCoverage` results are kept only when `NegativeTimeToLive` is set, and `Unavailable` results never. If the store fails, the leg is routed without it and `OnCacheError` is called.

Google Maps Platform terms do not allow Routes API results to be cached, so the provider refuses to wrap `GoogleRoutesRoadRouteProvider` and never stores a result whose source is `google-routes`. Check the terms of any other provider before choosing a time to live.

See the [TradeRouter.Net README](https://github.com/skuirrels/TradeRouter.Net#caching-road-routes) for the full description.
