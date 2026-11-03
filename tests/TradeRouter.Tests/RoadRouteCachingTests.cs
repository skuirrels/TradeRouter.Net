using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using TradeRouter.Caching;
using TradeRouter.Common;
using TradeRouter.Movements;
using Xunit;

namespace TradeRouter.Tests;

public class RoadRouteCachingTests
{
    private static readonly TimeSpan ThirtyDays = TimeSpan.FromDays(30);

    [Fact]
    public async Task RouteAsync_ServesARepeatedLegFromTheCache()
    {
        var inner = new CountingProvider(Success());
        var provider = CreateProvider(inner, new DistributedRoadRouteCache(MemoryCache()));

        var first = await provider.RouteAsync(CreateRequest(sequence: 1));
        var second = await provider.RouteAsync(CreateRequest(sequence: 3));

        inner.Calls.Should().Be(1, "the leg sequence is not part of the key");
        second.Should().BeEquivalentTo(first);
    }

    [Fact]
    public async Task RouteAsync_KeepsDifferentEndpointsAndPartitionsApart()
    {
        var inner = new CountingProvider(Success());
        var store = new DistributedRoadRouteCache(MemoryCache());
        var provider = CreateProvider(inner, store);
        var otherPartition = CreateProvider(inner, store, options => options with { Partition = "osrm-driving-other" });

        await provider.RouteAsync(CreateRequest());
        await provider.RouteAsync(CreateRequest(to: new Coordinate(1, 1.001)));
        await provider.RouteAsync(CreateRequest(from: new Coordinate(1, 1), to: new Coordinate(0, 0)));
        await otherPartition.RouteAsync(CreateRequest());

        inner.Calls.Should().Be(4);
    }

    [Fact]
    public async Task RouteAsync_SharesAnEntryBetweenCoordinatesThatRoundTogether()
    {
        var inner = new CountingProvider(Success());
        var provider = CreateProvider(inner, new DistributedRoadRouteCache(MemoryCache()), options => options with { CoordinateDecimals = 3 });

        await provider.RouteAsync(CreateRequest(from: new Coordinate(-0.0001, 0.0002)));
        await provider.RouteAsync(CreateRequest(from: new Coordinate(0.0001, -0.0002)));

        inner.Calls.Should().Be(1, "both origins round to 0,0 at three decimal places");
    }

    [Fact]
    public async Task RouteAsync_StoresSuccessForTheTimeToLive()
    {
        var store = new RecordingStore();
        var provider = CreateProvider(new CountingProvider(Success()), store);

        await provider.RouteAsync(CreateRequest());

        store.Writes.Should().ContainSingle().Which.TimeToLive.Should().Be(ThirtyDays);
    }

    [Theory]
    [InlineData(RoadRouteStatus.NoRoute)]
    [InlineData(RoadRouteStatus.OutsideCoverage)]
    public async Task RouteAsync_StoresNegativeResultsOnlyWhenANegativeTimeToLiveIsSet(RoadRouteStatus status)
    {
        var result = new RoadRouteResult { Status = status, Source = "osrm", Message = "No route." };
        var withoutStore = new RecordingStore();
        var withStore = new RecordingStore();

        await CreateProvider(new CountingProvider(result), withoutStore).RouteAsync(CreateRequest());
        await CreateProvider(new CountingProvider(result), withStore, options => options with { NegativeTimeToLive = TimeSpan.FromDays(1) })
            .RouteAsync(CreateRequest());

        withoutStore.Writes.Should().BeEmpty();
        withStore.Writes.Should().ContainSingle().Which.TimeToLive.Should().Be(TimeSpan.FromDays(1));
    }

    [Fact]
    public async Task RouteAsync_NeverStoresAnUnavailableResult()
    {
        var store = new RecordingStore();
        var inner = new CountingProvider(new RoadRouteResult { Status = RoadRouteStatus.Unavailable, Source = "osrm", Message = "Timed out." });
        var provider = CreateProvider(inner, store, options => options with { NegativeTimeToLive = TimeSpan.FromDays(1) });

        await provider.RouteAsync(CreateRequest());
        await provider.RouteAsync(CreateRequest());

        store.Writes.Should().BeEmpty();
        inner.Calls.Should().Be(2);
    }

    [Fact]
    public async Task RouteAsync_LeavesGeometryOutOfTheStoreWhenAsked()
    {
        var store = new RecordingStore();
        var provider = CreateProvider(new CountingProvider(Success()), store, options => options with { CacheGeometry = false });

        var result = await provider.RouteAsync(CreateRequest());

        result.Geometry.Should().NotBeNull("the provider's own result is returned unchanged");
        store.Writes.Should().ContainSingle().Which.Result.Geometry.Should().BeNull();
    }

    [Fact]
    public void Constructor_RefusesToWrapGoogleRoutes()
    {
        var google = new GoogleRoutesRoadRouteProvider(new HttpClient(), "test-key-not-real");

        var act = () => new CachingRoadRouteProvider(google, new RecordingStore(), Options());

        act.Should().Throw<ArgumentException>().WithMessage("*Google Maps Platform terms*");
    }

    [Fact]
    public async Task RouteAsync_NeverStoresGoogleRoutesResultsReachedThroughAnotherProvider()
    {
        const string json = """{"routes":[{"distanceMeters":157000,"duration":"7200s","polyline":{"geoJsonLinestring":{"type":"LineString","coordinates":[[0,0],[1,1]]}}}]}""";
        int calls = 0;
        using var client = new HttpClient(new StubHandler(() =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }));
        var google = new GoogleRoutesRoadRouteProvider(client, "test-key-not-real");
        var store = new RecordingStore();
        var provider = CreateProvider(new PassThroughProvider(google), store);

        var result = await provider.RouteAsync(CreateRequest());
        await provider.RouteAsync(CreateRequest());

        result.Source.Should().Be(GoogleRoutesRoadRouteProvider.SourceName);
        store.Writes.Should().BeEmpty();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task RouteAsync_RoutesWithoutTheCacheWhenTheStoreFails()
    {
        var errors = new List<Exception>();
        var inner = new CountingProvider(Success());
        var provider = CreateProvider(inner, new FailingStore(), options => options with { OnCacheError = errors.Add });

        var result = await provider.RouteAsync(CreateRequest());

        result.Status.Should().Be(RoadRouteStatus.Success);
        inner.Calls.Should().Be(1);
        errors.Should().HaveCount(2, "both the read and the write failed").And.AllBeOfType<IOException>();
    }

    [Fact]
    public async Task RouteAsync_RoutesAgainWhenTheStoreReturnsAnUnusableResult()
    {
        var errors = new List<Exception>();
        var store = new RecordingStore { Stored = new RoadRouteResult { Status = RoadRouteStatus.Unavailable, Source = "osrm" } };
        var inner = new CountingProvider(Success());
        var provider = CreateProvider(inner, store, options => options with { OnCacheError = errors.Add });

        var result = await provider.RouteAsync(CreateRequest());

        result.Status.Should().Be(RoadRouteStatus.Success);
        inner.Calls.Should().Be(1);
        errors.Should().ContainSingle().Which.Should().BeOfType<InvalidDataException>();
    }

    public static TheoryData<RoadRouteResult> UnusableSuccesses() =>
    [
        new RoadRouteResult { Status = RoadRouteStatus.Success, DistanceKm = -1.0, Source = "osrm" },
        new RoadRouteResult { Status = RoadRouteStatus.Success, DistanceKm = double.NaN, Source = "osrm" },
        new RoadRouteResult { Status = RoadRouteStatus.Success, DistanceKm = 10.0, DurationHours = double.PositiveInfinity, Source = "osrm" },
        new RoadRouteResult { Status = RoadRouteStatus.Success, DistanceKm = 10.0, Geometry = [new Coordinate(0, 0)], Source = "osrm" },
        new RoadRouteResult { Status = RoadRouteStatus.Success, DistanceKm = 10.0, Source = " " }
    ];

    [Theory]
    [MemberData(nameof(UnusableSuccesses))]
    public async Task RouteAsync_RoutesAgainWhenAStoredSuccessFailsTheEnginesChecks(RoadRouteResult stored)
    {
        var errors = new List<Exception>();
        var inner = new CountingProvider(Success());
        var provider = CreateProvider(inner, new RecordingStore { Stored = stored }, options => options with { OnCacheError = errors.Add });

        var result = await provider.RouteAsync(CreateRequest());

        result.DistanceKm.Should().Be(157.0);
        inner.Calls.Should().Be(1);
        errors.Should().ContainSingle().Which.Should().BeOfType<InvalidDataException>();
    }

    [Fact]
    public async Task RouteAsync_IgnoresExceptionsFromTheErrorCallback()
    {
        var inner = new CountingProvider(Success());
        var provider = CreateProvider(
            inner,
            new FailingStore(),
            options => options with { OnCacheError = _ => throw new InvalidOperationException("Logger is down.") });

        var result = await provider.RouteAsync(CreateRequest());

        result.Status.Should().Be(RoadRouteStatus.Success);
        inner.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(181.0, -179.0)]
    [InlineData(180.0, -180.0)]
    [InlineData(179.999999, -180.0)]
    public async Task RouteAsync_SharesAnEntryBetweenEquivalentLongitudes(double first, double second)
    {
        var inner = new CountingProvider(Success());
        var provider = CreateProvider(inner, new DistributedRoadRouteCache(MemoryCache()));

        await provider.RouteAsync(CreateRequest(to: new Coordinate(first, 10)));
        await provider.RouteAsync(CreateRequest(to: new Coordinate(second, 10)));

        inner.Calls.Should().Be(1);
    }

    [Fact]
    public async Task RouteAsync_LetsTheCallersCancellationPropagate()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var provider = CreateProvider(new CountingProvider(Success()), new DistributedRoadRouteCache(MemoryCache()));

        var act = async () => await provider.RouteAsync(CreateRequest(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("", 30, 5)]
    [InlineData("osrm|driving", 30, 5)]
    [InlineData("osrm", 0, 5)]
    [InlineData("osrm", 30, 11)]
    [InlineData("osrm", 30, -1)]
    public void Constructor_RejectsInvalidOptions(string partition, int timeToLiveDays, int coordinateDecimals)
    {
        var options = new RoadRouteCacheOptions
        {
            Partition = partition,
            TimeToLive = TimeSpan.FromDays(timeToLiveDays),
            CoordinateDecimals = coordinateDecimals
        };

        var act = () => new CachingRoadRouteProvider(new CountingProvider(Success()), new RecordingStore(), options);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_RejectsANonPositiveNegativeTimeToLive()
    {
        var act = () => new CachingRoadRouteProvider(
            new CountingProvider(Success()),
            new RecordingStore(),
            Options() with { NegativeTimeToLive = TimeSpan.Zero });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_RejectsATimeToLiveBeyondTheMaximum()
    {
        var timeToLive = () => new CachingRoadRouteProvider(
            new CountingProvider(Success()),
            new RecordingStore(),
            Options() with { TimeToLive = TimeSpan.MaxValue });
        var negative = () => new CachingRoadRouteProvider(
            new CountingProvider(Success()),
            new RecordingStore(),
            Options() with { NegativeTimeToLive = RoadRouteCacheOptions.MaximumTimeToLive + TimeSpan.FromDays(1) });

        timeToLive.Should().Throw<ArgumentOutOfRangeException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task DistributedCache_StoresAtTheMaximumTimeToLive()
    {
        var store = new DistributedRoadRouteCache(MemoryCache());

        await store.SetAsync(Key(), Success(), RoadRouteCacheOptions.MaximumTimeToLive);

        (await store.GetAsync(Key())).Should().NotBeNull();
    }

    [Fact]
    public void CacheKey_IsCultureInvariantAndRounded()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            var key = RoadRouteCacheKey.Create(" osrm-driving ", CreateRequest(from: new Coordinate(-0.190284, 51.148065), to: new Coordinate(0.1, -0.000001)), 5);

            key.Value.Should().Be("osrm-driving|-0.19028,51.14807|0.1,0");
            key.Partition.Should().Be("osrm-driving");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task DistributedCache_RoundTripsEveryField()
    {
        var store = new DistributedRoadRouteCache(MemoryCache());
        var key = Key();
        var result = new RoadRouteResult
        {
            Status = RoadRouteStatus.Success,
            DistanceKm = 64.321,
            DurationHours = 1.05,
            Geometry = [new Coordinate(-0.19028, 51.14806), new Coordinate(-0.3, 51.3), new Coordinate(-0.45, 51.46667)],
            Source = "osrm",
            Profile = "driving",
            DataVersion = "england-260914",
            Message = "note"
        };

        await store.SetAsync(key, result, ThirtyDays);
        var read = await store.GetAsync(key);

        read.Should().BeEquivalentTo(result);
    }

    [Fact]
    public async Task DistributedCache_WritesAnAbsoluteExpiryUnderThePrefixedKey()
    {
        var cache = new RecordingDistributedCache();
        var store = new DistributedRoadRouteCache(cache, "eDC:");

        await store.SetAsync(Key(), Success(), ThirtyDays);

        cache.LastKey.Should().Be("eDC:" + Key().Value);
        cache.LastOptions!.AbsoluteExpirationRelativeToNow.Should().Be(ThirtyDays);
        cache.LastOptions.SlidingExpiration.Should().BeNull();
    }

    [Fact]
    public async Task DistributedCache_ReturnsNullForAMissingEntryOrALaterPayloadVersion()
    {
        var cache = MemoryCache();
        var store = new DistributedRoadRouteCache(cache);
        await cache.SetAsync(DistributedRoadRouteCache.DefaultKeyPrefix + Key().Value, Encoding.UTF8.GetBytes("""{"v":2,"status":"Success","source":"osrm"}"""));

        (await store.GetAsync(Key())).Should().BeNull();
        (await store.GetAsync(RoadRouteCacheKey.Create("other", CreateRequest(), 5))).Should().BeNull();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("""{"status":"Success","source":"osrm","distanceKm":1}""")]
    [InlineData("""{"v":1,"status":"Success"}""")]
    [InlineData("""{"v":1,"status":"Success","source":"osrm","geometry":[0,0,1]}""")]
    [InlineData("""{"v":1,"status":"Sideways","source":"osrm"}""")]
    public async Task DistributedCache_ThrowsForAnUnreadableEntry(string payload)
    {
        var cache = MemoryCache();
        var store = new DistributedRoadRouteCache(cache);
        await cache.SetAsync(DistributedRoadRouteCache.DefaultKeyPrefix + Key().Value, Encoding.UTF8.GetBytes(payload));

        var act = async () => await store.GetAsync(Key());

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task MovementAsync_UsesTheCacheForRoadLegs()
    {
        var inner = new CountingProvider(Success());
        var provider = CreateProvider(inner, new DistributedRoadRouteCache(MemoryCache()));
        MovementRequest Request() => new()
        {
            Legs = MovementParser.Parse("Pickup GBLGW to Airport GBLHR Road"),
            RoadRouteProvider = provider,
            RoadRoutingMode = RoadRoutingMode.RequireNetwork
        };

        var first = (await TradeRouterEngine.Default.CalculateMovementAsync(Request())).Legs.Single();
        var second = (await TradeRouterEngine.Default.CalculateMovementAsync(Request())).Legs.Single();

        inner.Calls.Should().Be(1);
        second.Length.Should().Be(first.Length);
        second.Feature.Properties.DistanceSource.Should().Be("osrm");
        second.Feature.Properties.DistanceBasis.Should().Be("road_network");
    }

    private static RoadRouteResult Success() => new()
    {
        Status = RoadRouteStatus.Success,
        DistanceKm = 157.0,
        DurationHours = 2.0,
        Geometry = [new Coordinate(0, 0), new Coordinate(0.5, 0.5), new Coordinate(1, 1)],
        Source = "osrm",
        Profile = "driving"
    };

    private static RoadRouteCacheOptions Options() => new() { Partition = "osrm-driving-test", TimeToLive = ThirtyDays };

    private static CachingRoadRouteProvider CreateProvider(
        IRoadRouteProvider inner,
        IRoadRouteCache store,
        Func<RoadRouteCacheOptions, RoadRouteCacheOptions>? configure = null) =>
        new(inner, store, configure is null ? Options() : configure(Options()));

    private static RoadRouteRequest CreateRequest(Coordinate? from = null, Coordinate? to = null, int sequence = 1) => new(
        sequence,
        new ResolvedLocation("FROM", "From", from ?? new Coordinate(0, 0), null, "test"),
        new ResolvedLocation("TO", "To", to ?? new Coordinate(1, 1), null, "test"));

    private static RoadRouteCacheKey Key() => RoadRouteCacheKey.Create("osrm-driving-test", CreateRequest(), 5);

    private static MemoryDistributedCache MemoryCache() => new(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions()));

    private sealed class CountingProvider(RoadRouteResult result) : IRoadRouteProvider
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask<RoadRouteResult> RouteAsync(RoadRouteRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _calls);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class PassThroughProvider(IRoadRouteProvider inner) : IRoadRouteProvider
    {
        public ValueTask<RoadRouteResult> RouteAsync(RoadRouteRequest request, CancellationToken cancellationToken = default) =>
            inner.RouteAsync(request, cancellationToken);
    }

    private sealed class RecordingStore : IRoadRouteCache
    {
        public RoadRouteResult? Stored { get; init; }

        public List<(RoadRouteCacheKey Key, RoadRouteResult Result, TimeSpan TimeToLive)> Writes { get; } = [];

        public ValueTask<RoadRouteResult?> GetAsync(RoadRouteCacheKey key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Stored);

        public ValueTask SetAsync(RoadRouteCacheKey key, RoadRouteResult result, TimeSpan timeToLive, CancellationToken cancellationToken = default)
        {
            Writes.Add((key, result, timeToLive));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingStore : IRoadRouteCache
    {
        public ValueTask<RoadRouteResult?> GetAsync(RoadRouteCacheKey key, CancellationToken cancellationToken = default) =>
            throw new IOException("Store is down.");

        public ValueTask SetAsync(RoadRouteCacheKey key, RoadRouteResult result, TimeSpan timeToLive, CancellationToken cancellationToken = default) =>
            throw new IOException("Store is down.");
    }

    private sealed class RecordingDistributedCache : IDistributedCache
    {
        private readonly MemoryDistributedCache _inner = MemoryCache();

        public string? LastKey { get; private set; }

        public DistributedCacheEntryOptions? LastOptions { get; private set; }

        public byte[]? Get(string key) => _inner.Get(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => _inner.GetAsync(key, token);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _inner.Set(key, value, options);

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            LastKey = key;
            LastOptions = options;
            return _inner.SetAsync(key, value, options, token);
        }

        public void Refresh(string key) => _inner.Refresh(key);

        public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(key, token);

        public void Remove(string key) => _inner.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default) => _inner.RemoveAsync(key, token);
    }

    private sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond());
    }
}
