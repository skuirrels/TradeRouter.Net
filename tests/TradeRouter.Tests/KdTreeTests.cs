using FluentAssertions;
using TradeRouter.Common;
using TradeRouter.Spatial;
using Xunit;

namespace TradeRouter.Tests;

public class KdTreeTests
{
    [Fact]
    public void KdTree_Empty_ReturnsNull()
    {
        var tree = new KdTree<string>([]);
        tree.Count.Should().Be(0);
        tree.Query(new Coordinate(0, 0)).Should().BeNull();
    }

    [Fact]
    public void KdTree_SingleElement_ReturnsThatElement()
    {
        var pt = new Coordinate(10.0, 20.0);
        var tree = new KdTree<string>([(pt, "Item1")]);
        tree.Count.Should().Be(1);

        var query = tree.Query(new Coordinate(100.0, 20.0));
        query.Should().NotBeNull();
        query!.Value.Value.Should().Be("Item1");
        query.Value.Point.Should().Be(pt);
    }

    [Fact]
    public void KdTree_MultiplePoints_FindsExactNearestNeighbor()
    {
        var points = new (Coordinate, int)[]
        {
            (new Coordinate(0, 0), 0),
            (new Coordinate(10, 10), 1),
            (new Coordinate(-10, -10), 2),
            (new Coordinate(5, 5), 3),
            (new Coordinate(-5, 5), 4),
            (new Coordinate(20, -20), 5)
        };

        var tree = new KdTree<int>(points);
        tree.Count.Should().Be(points.Length);

        // Closest to (4.9, 5.1) is (5, 5) => id 3
        var res = tree.Query(new Coordinate(4.9, 5.1));
        res.Should().NotBeNull();
        res!.Value.Value.Should().Be(3);

        // Closest to (-9.0, -9.5) is (-10, -10) => id 2
        var res2 = tree.Query(new Coordinate(-9.0, -9.5));
        res2.Should().NotBeNull();
        res2!.Value.Value.Should().Be(2);
    }

    [Fact]
    public void KdTree_RandomCoordinates_MatchesLinearBruteForceSearch()
    {
        var rand = new Random(42);
        var items = new List<(Coordinate Point, int Value)>();
        for (int i = 0; i < 500; i++)
        {
            double lon = (rand.NextDouble() * 360.0) - 180.0;
            double lat = (rand.NextDouble() * 180.0) - 90.0;
            items.Add((new Coordinate(lon, lat), i));
        }

        var tree = new KdTree<int>(items);

        for (int q = 0; q < 50; q++)
        {
            double qLon = (rand.NextDouble() * 360.0) - 180.0;
            double qLat = (rand.NextDouble() * 180.0) - 90.0;
            var qCoord = new Coordinate(qLon, qLat);

            // Brute force
            int bestIdx = -1;
            double bestDistSq = double.PositiveInfinity;
            for (int i = 0; i < items.Count; i++)
            {
                double dSq = Haversine.UnitSphereDistanceSquared(qCoord, items[i].Point);
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    bestIdx = i;
                }
            }

            var treeResult = tree.Query(qCoord);
            treeResult.Should().NotBeNull();

            // The distance to tree result point should be identical to best distance
            double treeDistSq = Haversine.UnitSphereDistanceSquared(qCoord, treeResult!.Value.Point);
            treeDistSq.Should().BeApproximately(bestDistSq, 1e-9);
        }
    }

    [Fact]
    public void KdTree_Antimeridian_SelectsAcrossDateLineNeighbor()
    {
        var acrossDateLine = new Coordinate(179.8, 0);
        var geographicallyFarther = new Coordinate(-160, 0);
        var tree = new KdTree<string>([(acrossDateLine, "across"), (geographicallyFarther, "farther")]);

        tree.Query(new Coordinate(-179.9, 0))!.Value.Value.Should().Be("across");
    }

    [Fact]
    public void KdTree_NearPole_MatchesSphericalNearestNeighbor()
    {
        var closeAcrossLongitude = new Coordinate(90, 86);
        var fartherDownMeridian = new Coordinate(0, 70);
        var tree = new KdTree<string>([(closeAcrossLongitude, "polar"), (fartherDownMeridian, "southern")]);

        tree.Query(new Coordinate(0, 85))!.Value.Value.Should().Be("polar");
    }
}
