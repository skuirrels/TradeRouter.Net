using System.Collections.Concurrent;
using AwesomeAssertions;
using TradeRouter.Common;
using TradeRouter.Graph;
using TradeRouter.Movements;
using TradeRouter.Passages;
using TradeRouter.Ports;
using Xunit;

namespace TradeRouter.Tests;

public class ConcurrencyTests
{
    [Fact]
    public void ConcurrentRouting_ParallelQueries_ShouldAllSucceedConsistently()
    {
        var testPairs = new (Coordinate Origin, Coordinate Dest)[]
        {
            (new Coordinate(5.333333, 43.333333), new Coordinate(18.366667, -33.916667)),
            (new Coordinate(121.47, 31.23), new Coordinate(4.48, 51.92)),
            (new Coordinate(139.64, 35.44), new Coordinate(-118.24, 33.74)),
            (new Coordinate(52.99, 25.01), new Coordinate(-61.87, 17.15)),
            (new Coordinate(-74.0, 40.7), new Coordinate(0.0, 51.5))
        };

        var errors = new ConcurrentBag<Exception>();
        var lengths = new ConcurrentBag<double>();

        Parallel.For(0, 100, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
        {
            try
            {
                var pair = testPairs[i % testPairs.Length];
                var route = TradeRoutes.Calculate(pair.Origin, pair.Dest, appendOrigDest: true);
                lengths.Add(route.Properties.Length);
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        });

        errors.Should().BeEmpty();
        lengths.Count.Should().Be(100);
        foreach (var len in lengths)
        {
            len.Should().BeGreaterThan(1000.0);
        }
    }

    private static readonly (Coordinate Origin, Coordinate Dest)[] RoutePairs =
    [
        (new Coordinate(5.333333, 43.333333), new Coordinate(18.366667, -33.916667)),
        (new Coordinate(121.47, 31.23), new Coordinate(4.48, 51.92)),
        (new Coordinate(139.64, 35.44), new Coordinate(-118.24, 33.74)),
        (new Coordinate(52.99, 25.01), new Coordinate(-61.87, 17.15)),
        (new Coordinate(-74.0, 40.7), new Coordinate(0.0, 51.5))
    ];

    [Theory]
    [InlineData("dijkstra")]
    [InlineData("astar")]
    public void ConcurrentRouting_MatchesSequentialResultsExactly(string algorithm)
    {
        var options = new TradeRouterOptions
        {
            Algorithm = algorithm,
            Restrictions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Passage.Suez },
            ReturnPassages = true
        };
        var expected = RoutePairs.Select(p => Fingerprint(TradeRoutes.Calculate(p.Origin, p.Dest, options))).ToArray();
        var actual = new string[200];

        Parallel.For(0, actual.Length, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
        {
            var pair = RoutePairs[i % RoutePairs.Length];
            actual[i] = Fingerprint(TradeRoutes.Calculate(pair.Origin, pair.Dest, options));
        });

        for (int i = 0; i < actual.Length; i++)
            actual[i].Should().Be(expected[i % RoutePairs.Length], $"parallel query {i} must match its sequential result");
    }

    [Fact]
    public void ConcurrentRouting_InterleavedGraphSizesOnSharedThreadsStayCorrect()
    {
        // Per-thread search buffers are sized for the largest graph seen; a small graph must not read stale entries.
        var smallGraph = new MaritimeGraph();
        for (int lon = 0; lon < 4; lon++)
            smallGraph.AddEdge(new Coordinate(lon, 0), new Coordinate(lon + 1, 0));
        smallGraph.BuildIndex();
        var smallEngine = new TradeRouterEngine(smallGraph, new PortDatabase([]));
        double expectedSmall = smallEngine.CalculateRoute(new Coordinate(0, 0), new Coordinate(4, 0)).Properties.Length;
        double expectedLarge = TradeRoutes.Calculate(RoutePairs[0].Origin, RoutePairs[0].Dest).Properties.Length;
        var errors = new ConcurrentBag<string>();

        Parallel.For(0, 200, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
        {
            if (i % 2 == 0)
            {
                double length = smallEngine.CalculateRoute(new Coordinate(0, 0), new Coordinate(4, 0)).Properties.Length;
                if (length != expectedSmall)
                    errors.Add($"small graph query {i} returned {length}");
            }
            else
            {
                double length = TradeRoutes.Calculate(RoutePairs[0].Origin, RoutePairs[0].Dest).Properties.Length;
                if (length != expectedLarge)
                    errors.Add($"large graph query {i} returned {length}");
            }
        });

        errors.Should().BeEmpty();
        expectedSmall.Should().BeApproximately(Haversine.DistanceKm(new Coordinate(0, 0), new Coordinate(4, 0)), 1e-6);
    }

    [Fact]
    public void ConcurrentFirstUse_BuildsLazyGraphAndPortsOnce()
    {
        int graphBuilds = 0;
        int portBuilds = 0;
        var engine = new TradeRouterEngine(
            () =>
            {
                Interlocked.Increment(ref graphBuilds);
                var graph = new MaritimeGraph();
                graph.AddEdge(new Coordinate(0, 0), new Coordinate(1, 0));
                graph.BuildIndex();
                return graph;
            },
            () =>
            {
                Interlocked.Increment(ref portBuilds);
                return new PortDatabase([]);
            });
        using var start = new Barrier(Math.Min(Environment.ProcessorCount, 8));

        Parallel.For(0, start.ParticipantCount, new ParallelOptions { MaxDegreeOfParallelism = start.ParticipantCount }, _ =>
        {
            start.SignalAndWait();
            engine.CalculateRoute(new Coordinate(0, 0), new Coordinate(1, 0), new TradeRouterOptions { IncludePorts = true });
        });

        graphBuilds.Should().Be(1);
        portBuilds.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentMovements_MatchSequentialResults()
    {
        const string legs = """
            Pickup GBLGW to Port GBFXT Road
            Port GBFXT to Port SGSIN Sea
            """;
        var expected = TradeRoutes.CalculateMovement(legs);
        var provider = new FixedRoadProvider();

        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(i => Task.Run(async () =>
        {
            var request = new MovementRequest { Legs = MovementParser.Parse(legs) };
            if (i % 2 == 1)
                request.RoadRouteProvider = provider;
            return (UsesProvider: i % 2 == 1, Result: await TradeRouterEngine.Default.CalculateMovementAsync(request));
        })));

        foreach (var (usesProvider, result) in results)
        {
            result.Legs[1].Length.Should().Be(expected.Legs[1].Length);
            result.Legs[1].DurationHours.Should().Be(expected.Legs[1].DurationHours);
            if (usesProvider)
                result.Legs[0].Length.Should().Be(FixedRoadProvider.DistanceKm);
            else
                result.Legs[0].Length.Should().Be(expected.Legs[0].Length);
        }
        provider.Calls.Should().Be(20, "half of the 40 movements use the provider for their single road leg");
    }

    private static string Fingerprint(TradeRouter.GeoJson.GeoJsonFeature route) =>
        FormattableString.Invariant(
            $"{route.Properties.Length:R}|{route.Properties.DurationHours:R}|{route.Geometry!.Coordinates.Count}|{string.Join(",", route.Properties.TraversedPassages ?? [])}");

    private sealed class FixedRoadProvider : IRoadRouteProvider
    {
        public const double DistanceKm = 42.0;
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public async ValueTask<RoadRouteResult> RouteAsync(RoadRouteRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            await Task.Yield();
            return new RoadRouteResult { Status = RoadRouteStatus.Success, DistanceKm = DistanceKm, DurationHours = 0.7, Source = "fixed" };
        }
    }
}
