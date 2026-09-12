using TradeRouter.Common;

namespace TradeRouter.Spatial;

/// <summary>
/// A balanced three-dimensional KD-Tree over unit-sphere coordinates. This preserves exact
/// great-circle nearest-neighbour ordering across the antimeridian and near the poles.
/// Thread-safe for read queries once constructed.
/// </summary>
/// <typeparam name="T">Payload type associated with coordinates.</typeparam>
public sealed class KdTree<T>
{
    private readonly KdNode<T>? _root;

    /// <summary>Number of nodes in the tree.</summary>
    public int Count { get; }

    /// <summary>Builds a balanced spherical KD-Tree from point-value pairs.</summary>
    public KdTree(IEnumerable<(Coordinate Point, T Value)> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var nodes = items.Select(item => new KdNode<T>(item.Point, item.Value)).ToList();
        Count = nodes.Count;
        _root = BuildTree(nodes, 0, nodes.Count, 0);
    }

    /// <summary>Builds a tree whose payload is the coordinate itself.</summary>
    public static KdTree<Coordinate> FromCoordinates(IEnumerable<Coordinate> coordinates) =>
        new(coordinates.Select(c => (c, c)));

    /// <summary>Finds the geographically nearest neighbour to <paramref name="target"/>.</summary>
    public (Coordinate Point, T Value)? Query(Coordinate target)
    {
        if (_root is null)
            return null;

        var vector = Haversine.ToUnitVector(target);
        KdNode<T>? best = null;
        double bestDistanceSquared = double.PositiveInfinity;
        QueryRecursive(_root, vector, 0, ref best, ref bestDistanceSquared);
        return best is null ? null : (best.Point, best.Value);
    }

    private static KdNode<T>? BuildTree(List<KdNode<T>> nodes, int start, int length, int depth)
    {
        if (length <= 0)
            return null;

        int axis = depth % 3;
        nodes.Sort(start, length, Comparer<KdNode<T>>.Create((a, b) => Axis(a, axis).CompareTo(Axis(b, axis))));
        int medianOffset = length / 2;
        int medianIndex = start + medianOffset;
        var node = nodes[medianIndex];
        node.Left = BuildTree(nodes, start, medianOffset, depth + 1);
        node.Right = BuildTree(nodes, medianIndex + 1, length - medianOffset - 1, depth + 1);
        return node;
    }

    private static void QueryRecursive(
        KdNode<T> current,
        (double X, double Y, double Z) target,
        int depth,
        ref KdNode<T>? best,
        ref double bestDistanceSquared)
    {
        double dx = target.X - current.X;
        double dy = target.Y - current.Y;
        double dz = target.Z - current.Z;
        double distanceSquared = (dx * dx) + (dy * dy) + (dz * dz);
        if (distanceSquared < bestDistanceSquared)
        {
            bestDistanceSquared = distanceSquared;
            best = current;
        }

        int axis = depth % 3;
        double delta = Axis(target, axis) - Axis(current, axis);
        KdNode<T>? near = delta < 0 ? current.Left : current.Right;
        KdNode<T>? far = delta < 0 ? current.Right : current.Left;

        if (near is not null)
            QueryRecursive(near, target, depth + 1, ref best, ref bestDistanceSquared);
        if (far is not null && (delta * delta) < bestDistanceSquared)
            QueryRecursive(far, target, depth + 1, ref best, ref bestDistanceSquared);
    }

    private static double Axis(KdNode<T> node, int axis) => axis switch
    {
        0 => node.X,
        1 => node.Y,
        _ => node.Z
    };

    private static double Axis((double X, double Y, double Z) vector, int axis) => axis switch
    {
        0 => vector.X,
        1 => vector.Y,
        _ => vector.Z
    };
}
