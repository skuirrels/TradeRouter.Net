using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using TradeRouter.Common;
using TradeRouter.GeoJson;
using TradeRouter.Passages;

namespace TradeRouter.Benchmarks;

[ShortRunJob]
[MemoryDiagnoser]
public class RoutingBenchmarks
{
    private Coordinate _marseille;
    private Coordinate _capeTown;
    private Coordinate _shanghai;
    private Coordinate _rotterdam;
    private Coordinate _paris;
    private Coordinate _tokyo;

    [GlobalSetup]
    public void Setup()
    {
        _marseille = new Coordinate(5.333333, 43.333333);
        _capeTown = new Coordinate(18.366667, -33.916667);
        _shanghai = new Coordinate(121.47, 31.23);
        _rotterdam = new Coordinate(4.48, 51.92);
        _paris = new Coordinate(2.333333, 48.866667);
        _tokyo = new Coordinate(139.679174, 35.778467);

        // Warm up graph and spatial indexes
        _ = TradeRoutes.Calculate(_marseille, _capeTown);
    }

    [Benchmark(Baseline = true)]
    public GeoJsonFeature Dijkstra_MarseilleToCapeTown()
    {
        return TradeRoutes.Calculate(_marseille, _capeTown, appendOrigDest: true, algorithm: "dijkstra");
    }

    [Benchmark]
    public GeoJsonFeature AStar_MarseilleToCapeTown()
    {
        return TradeRoutes.Calculate(_marseille, _capeTown, appendOrigDest: true, algorithm: "astar");
    }

    [Benchmark]
    public GeoJsonFeature Dijkstra_ShanghaiToRotterdam()
    {
        return TradeRoutes.Calculate(_shanghai, _rotterdam, appendOrigDest: true, algorithm: "dijkstra");
    }

    [Benchmark]
    public GeoJsonFeature AStar_ShanghaiToRotterdam()
    {
        return TradeRoutes.Calculate(_shanghai, _rotterdam, appendOrigDest: true, algorithm: "astar");
    }

    [Benchmark]
    public GeoJsonFeature Routing_WithPorts_ParisToTokyo()
    {
        return TradeRoutes.Calculate(_paris, _tokyo, appendOrigDest: true, includePorts: true);
    }

    [Benchmark]
    public Coordinate KdTree_QueryNearestPort()
    {
        var port = TradeRouterEngine.Default.Ports.FindNearestPort(_paris);
        return port?.Coordinate ?? default;
    }
}
