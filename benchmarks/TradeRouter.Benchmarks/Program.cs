using BenchmarkDotNet.Running;

namespace TradeRouter.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<RoutingBenchmarks>();
    }
}
