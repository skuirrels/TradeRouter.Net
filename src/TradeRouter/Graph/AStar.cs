using System.Runtime.InteropServices;
using TradeRouter.Common;

namespace TradeRouter.Graph;

/// <summary>
/// A* shortest path solver using Haversine great-circle distance as heuristic.
/// Uses node-indexed scratch arrays and generation stamps instead of per-query dictionaries.
/// </summary>
public static class AStar
{
    /// <summary>
    /// Computes the shortest path using A* search.
    /// </summary>
    public static (double LengthKm, List<Coordinate> Path) FindPath(
        MaritimeGraph graph,
        int source,
        int target,
        IReadOnlySet<string>? restrictions)
    {
        if (source == target)
        {
            return (0.0, [graph.GetCoordinate(source)]);
        }

        Coordinate targetCoord = graph.GetCoordinate(target);

        var buffers = SearchBuffers.Acquire(graph.NodeCount);
        int gen = buffers.Generation;
        double[] gScore = buffers.DistF;
        int[] parent = buffers.ParentF;
        int[] stamp = buffers.StampF;
        int[] closed = buffers.Closed;
        var openSet = buffers.QueueF;

        gScore[source] = 0.0;
        stamp[source] = gen;
        double h0 = graph.HaversineHeuristicScale * Haversine.DistanceKmUnchecked(graph.GetCoordinate(source), targetCoord);
        openSet.Enqueue(source, h0);

        while (openSet.Count > 0)
        {
            int current = openSet.Dequeue();

            if (current == target)
            {
                // Reconstruct path
                int length = 1;
                for (int curr = target; curr != source; curr = parent[curr])
                    length++;

                var path = new List<Coordinate>(length);
                CollectionsMarshal.SetCount(path, length);

                int index = length - 1;
                for (int curr = target; ; curr = parent[curr])
                {
                    path[index--] = graph.GetCoordinate(curr);
                    if (curr == source)
                        break;
                }

                return (gScore[target], path);
            }

            if (closed[current] == gen)
                continue;
            closed[current] = gen;

            double currentG = gScore[current];

            foreach (var edge in graph.GetEdges(current))
            {
                if (edge.Passage != null && restrictions != null && restrictions.Contains(edge.Passage))
                    continue;

                int neighbor = edge.TargetNodeId;
                if (closed[neighbor] == gen)
                    continue;

                double tentativeG = currentG + edge.Weight;

                if (stamp[neighbor] != gen || tentativeG < gScore[neighbor])
                {
                    gScore[neighbor] = tentativeG;
                    parent[neighbor] = current;
                    stamp[neighbor] = gen;
                    double h = graph.HaversineHeuristicScale * Haversine.DistanceKmUnchecked(graph.GetCoordinate(neighbor), targetCoord);
                    openSet.Enqueue(neighbor, tentativeG + h);
                }
            }
        }

        return (double.PositiveInfinity, []);
    }
}
