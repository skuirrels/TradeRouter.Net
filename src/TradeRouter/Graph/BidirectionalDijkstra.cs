using System.Runtime.InteropServices;
using TradeRouter.Common;

namespace TradeRouter.Graph;

/// <summary>
/// High-performance Bidirectional Dijkstra shortest path solver.
/// Uses node-indexed scratch arrays and generation stamps instead of per-query dictionaries.
/// </summary>
public static class BidirectionalDijkstra
{
    /// <summary>
    /// Computes the shortest path and distance in kilometers between source and target nodes,
    /// avoiding any passages present in the restrictions set.
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

        var buffers = SearchBuffers.Acquire(graph.NodeCount);
        int gen = buffers.Generation;
        double[] distF = buffers.DistF;
        double[] distB = buffers.DistB;
        int[] parentF = buffers.ParentF;
        int[] parentB = buffers.ParentB;
        int[] stampF = buffers.StampF;
        int[] stampB = buffers.StampB;
        var queueF = buffers.QueueF;
        var queueB = buffers.QueueB;

        distF[source] = 0.0;
        stampF[source] = gen;
        queueF.Enqueue(source, 0.0);

        distB[target] = 0.0;
        stampB[target] = gen;
        queueB.Enqueue(target, 0.0);

        double bestDistance = double.PositiveInfinity;
        int meetNode = -1;

        while (queueF.Count > 0 && queueB.Count > 0)
        {
            queueF.TryPeek(out _, out double minF);
            queueB.TryPeek(out _, out double minB);

            if (minF + minB >= bestDistance)
            {
                break;
            }

            if (minF <= minB)
            {
                // Expand forward side
                queueF.TryDequeue(out int u, out double prioF);
                if (prioF > distF[u])
                    continue;

                double dU = distF[u];

                foreach (var edge in graph.GetEdges(u))
                {
                    if (edge.Passage != null && restrictions != null && restrictions.Contains(edge.Passage))
                    {
                        continue; // Restricted passage skipped
                    }

                    int v = edge.TargetNodeId;
                    double cost = dU + edge.Weight;

                    if (stampF[v] != gen || cost < distF[v])
                    {
                        distF[v] = cost;
                        parentF[v] = u;
                        stampF[v] = gen;
                        queueF.Enqueue(v, cost);

                        if (stampB[v] == gen)
                        {
                            double total = cost + distB[v];
                            if (total < bestDistance)
                            {
                                bestDistance = total;
                                meetNode = v;
                            }
                        }
                    }
                }
            }
            else
            {
                // Expand backward side
                queueB.TryDequeue(out int u, out double prioB);
                if (prioB > distB[u])
                    continue;

                double dU = distB[u];

                foreach (var edge in graph.GetIncomingEdges(u))
                {
                    if (edge.Passage != null && restrictions != null && restrictions.Contains(edge.Passage))
                    {
                        continue; // Restricted passage skipped
                    }

                    int v = edge.TargetNodeId;
                    double cost = dU + edge.Weight;

                    if (stampB[v] != gen || cost < distB[v])
                    {
                        distB[v] = cost;
                        parentB[v] = u;
                        stampB[v] = gen;
                        queueB.Enqueue(v, cost);

                        if (stampF[v] == gen)
                        {
                            double total = distF[v] + cost;
                            if (total < bestDistance)
                            {
                                bestDistance = total;
                                meetNode = v;
                            }
                        }
                    }
                }
            }
        }

        if (meetNode == -1 || double.IsPositiveInfinity(bestDistance))
        {
            return (double.PositiveInfinity, []);
        }

        // Reconstruct path: source -> meetNode -> target
        int forwardLength = 1;
        for (int curr = meetNode; curr != source; curr = parentF[curr])
            forwardLength++;

        int backwardLength = 0;
        for (int curr = meetNode; curr != target; curr = parentB[curr])
            backwardLength++;

        var coordinates = new List<Coordinate>(forwardLength + backwardLength);
        CollectionsMarshal.SetCount(coordinates, forwardLength);

        int index = forwardLength - 1;
        for (int curr = meetNode; ; curr = parentF[curr])
        {
            coordinates[index--] = graph.GetCoordinate(curr);
            if (curr == source)
                break;
        }

        for (int curr = meetNode; curr != target;)
        {
            curr = parentB[curr];
            coordinates.Add(graph.GetCoordinate(curr));
        }

        return (bestDistance, coordinates);
    }
}
