using System.Collections.Concurrent;
using FluentAssertions;
using TradeRouter.Common;
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
}
