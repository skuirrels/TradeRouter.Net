using System.Runtime.InteropServices;
using TradeRouter.Common;
using TradeRouter.Spatial;

namespace TradeRouter.Graph;

/// <summary>
/// In-memory graph representing the global maritime shipping network (Marnet).
/// Backed by forward and reverse compressed sparse row layouts and a spherical KD-Tree.
/// </summary>
public sealed class MaritimeGraph
{
    private readonly List<Coordinate> _nodeCoordinates = [];
    private readonly Dictionary<Coordinate, int> _coordToId = [];
    private readonly List<List<GraphEdge>> _adjacency = [];
    private readonly Dictionary<(int, int), string?> _edgePassages = [];

    // Flattened adjacency built by BuildIndex(): edges for node i live in _edges[_edgeOffsets[i].._edgeOffsets[i+1]).
    private int[]? _edgeOffsets;
    private GraphEdge[]? _edges;
    private int[]? _incomingEdgeOffsets;
    private GraphEdge[]? _incomingEdges;
    private KdTree<int>? _kdTree;
    private bool _isReadOnly;

    /// <summary>Whether the graph has been indexed and frozen for concurrent route queries.</summary>
    public bool IsReadOnly => _isReadOnly;

    /// <summary>Scale applied to the geodesic A* heuristic so it remains admissible for custom edge weights.</summary>
    public double HaversineHeuristicScale { get; private set; }

    /// <summary>Number of nodes in the graph.</summary>
    public int NodeCount => _nodeCoordinates.Count;

    /// <summary>Number of directed edges in the graph.</summary>
    public int EdgeCount => _edges?.Length ?? _adjacency.Sum(a => a.Count);

    /// <summary>
    /// Gets the coordinate of a node by its index.
    /// </summary>
    public Coordinate GetCoordinate(int nodeId)
    {
        EnsureNodeId(nodeId, nameof(nodeId));
        return _nodeCoordinates[nodeId];
    }

    /// <summary>
    /// Gets the outgoing edges for a node as a span over the flattened adjacency array.
    /// </summary>
    public ReadOnlySpan<GraphEdge> GetEdges(int nodeId)
    {
        EnsureNodeId(nodeId, nameof(nodeId));
        if (_edges != null && _edgeOffsets != null)
        {
            int start = _edgeOffsets[nodeId];
            return new ReadOnlySpan<GraphEdge>(_edges, start, _edgeOffsets[nodeId + 1] - start);
        }

        return CollectionsMarshal.AsSpan(_adjacency[nodeId]);
    }

    /// <summary>Gets incoming edges. Each returned target is the predecessor node.</summary>
    public ReadOnlySpan<GraphEdge> GetIncomingEdges(int nodeId)
    {
        EnsureNodeId(nodeId, nameof(nodeId));
        if (_incomingEdges is null || _incomingEdgeOffsets is null)
            throw new InvalidOperationException("Graph index has not been built. Call BuildIndex() first.");

        int start = _incomingEdgeOffsets[nodeId];
        return new ReadOnlySpan<GraphEdge>(_incomingEdges, start, _incomingEdgeOffsets[nodeId + 1] - start);
    }

    /// <summary>
    /// Gets the passage associated with an edge (if any).
    /// </summary>
    public string? GetPassage(int u, int v)
    {
        return _edgePassages.TryGetValue((u, v), out var passage) ? passage : null;
    }

    /// <summary>
    /// Gets the passage associated with an edge between two coordinates (if any).
    /// </summary>
    public string? GetPassage(Coordinate u, Coordinate v)
    {
        if (_coordToId.TryGetValue(u, out int uId) && _coordToId.TryGetValue(v, out int vId))
        {
            return GetPassage(uId, vId);
        }
        return null;
    }

    /// <summary>
    /// Adds a node with its coordinate, returning its assigned integer node ID.
    /// Nodes can only be added before <see cref="BuildIndex"/> freezes the graph.
    /// </summary>
    public int AddNode(Coordinate coord)
    {
        EnsureMutable();
        coord.Validate();
        if (_coordToId.ContainsKey(coord))
            throw new ArgumentException($"A graph node already exists at {coord}.", nameof(coord));
        int id = _nodeCoordinates.Count;
        _nodeCoordinates.Add(coord);
        _coordToId[coord] = id;
        _adjacency.Add([]);
        return id;
    }

    /// <summary>
    /// Adds a directed edge from node uId to node vId with the specified weight and optional passage.
    /// Edges can only be added before <see cref="BuildIndex"/> freezes the graph.
    /// </summary>
    public void AddDirectedEdge(int uId, int vId, double weight, string? passage = null)
    {
        EnsureMutable();
        EnsureNodeId(uId, nameof(uId));
        EnsureNodeId(vId, nameof(vId));
        if (!double.IsFinite(weight) || weight < 0)
            throw new ArgumentOutOfRangeException(nameof(weight), weight, "Edge weight must be finite and non-negative.");
        _adjacency[uId].Add(new GraphEdge(vId, weight, passage));
        _edgePassages[(uId, vId)] = passage;
    }

    /// <summary>
    /// Adds a node if it does not already exist, returning its integer node ID.
    /// </summary>
    public int GetOrAddNode(Coordinate coord)
    {
        EnsureMutable();
        coord.Validate();
        if (_coordToId.TryGetValue(coord, out int id))
            return id;

        return AddNode(coord);
    }

    /// <summary>
    /// Adds an undirected edge between coordinates u and v.
    /// Edges can only be added before <see cref="BuildIndex"/> freezes the graph.
    /// </summary>
    public void AddEdge(Coordinate u, Coordinate v, double? weight = null, string? passage = null)
    {
        EnsureMutable();
        int uId = GetOrAddNode(u);
        int vId = GetOrAddNode(v);

        double w = weight ?? Math.Round(Haversine.Distance(u, v, DistanceUnit.Km), 1);

        AddDirectedEdge(uId, vId, w, passage);
        AddDirectedEdge(vId, uId, w, passage);
    }

    /// <summary>
    /// Finalizes graph construction: flattens the adjacency lists into a compressed sparse row layout
    /// and builds the KD-Tree index. Must be called after all nodes and edges have been added.
    /// </summary>
    public void BuildIndex()
    {
        if (_isReadOnly)
            return;

        int nodeCount = _nodeCoordinates.Count;
        var offsets = new int[nodeCount + 1];
        for (int i = 0; i < nodeCount; i++)
        {
            offsets[i + 1] = offsets[i] + _adjacency[i].Count;
        }

        var edges = new GraphEdge[offsets[nodeCount]];
        var incomingCounts = new int[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            _adjacency[i].CopyTo(edges, offsets[i]);
            foreach (var edge in _adjacency[i])
                incomingCounts[edge.TargetNodeId]++;
        }

        var incomingOffsets = new int[nodeCount + 1];
        for (int i = 0; i < nodeCount; i++)
            incomingOffsets[i + 1] = incomingOffsets[i] + incomingCounts[i];

        var incomingEdges = new GraphEdge[edges.Length];
        var incomingPositions = (int[])incomingOffsets.Clone();
        double heuristicScale = 1.0;
        for (int source = 0; source < nodeCount; source++)
        {
            foreach (var edge in _adjacency[source])
            {
                incomingEdges[incomingPositions[edge.TargetNodeId]++] = new GraphEdge(source, edge.Weight, edge.Passage);
                double directDistance = Haversine.DistanceKmUnchecked(_nodeCoordinates[source], _nodeCoordinates[edge.TargetNodeId]);
                if (directDistance > 0)
                    heuristicScale = Math.Min(heuristicScale, edge.Weight / directDistance);
            }
        }

        var items = new List<(Coordinate Point, int Value)>(nodeCount);
        for (int i = 0; i < nodeCount; i++)
        {
            items.Add((_nodeCoordinates[i], i));
        }

        _edgeOffsets = offsets;
        _edges = edges;
        _incomingEdgeOffsets = incomingOffsets;
        _incomingEdges = incomingEdges;
        _kdTree = new KdTree<int>(items);
        HaversineHeuristicScale = Math.Clamp(heuristicScale, 0.0, 1.0);
        _isReadOnly = true;
    }

    /// <summary>
    /// Finds the nearest graph node index to the given geographic coordinate.
    /// </summary>
    public int FindNearestNode(Coordinate coordinate)
    {
        if (_kdTree == null)
            throw new InvalidOperationException("Graph index has not been built. Call BuildIndex() first.");

        var nearest = _kdTree.Query(coordinate);
        if (nearest == null)
            throw new InvalidOperationException("No nodes in maritime graph.");

        return nearest.Value.Value;
    }

    /// <summary>
    /// Computes the shortest path on the maritime graph between two coordinates, applying passage restrictions.
    /// </summary>
    public (double LengthKm, List<Coordinate> Path) ShortestPath(
        Coordinate origin,
        Coordinate destination,
        IReadOnlySet<string>? restrictions = null,
        string? algorithm = "dijkstra")
    {
        if (!_isReadOnly)
            throw new InvalidOperationException("Graph index has not been built. Call BuildIndex() first.");
        int originNodeId = FindNearestNode(origin);
        int destNodeId = FindNearestNode(destination);

        if (originNodeId == destNodeId)
        {
            return (0.0, [_nodeCoordinates[originNodeId]]);
        }

        if (string.Equals(algorithm, "astar", StringComparison.OrdinalIgnoreCase))
        {
            return AStar.FindPath(this, originNodeId, destNodeId, restrictions);
        }

        if (!string.Equals(algorithm, "dijkstra", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Unknown pathfinding algorithm '{algorithm}'. Expected 'dijkstra' or 'astar'.", nameof(algorithm));

        return BidirectionalDijkstra.FindPath(this, originNodeId, destNodeId, restrictions);
    }

    private void EnsureMutable()
    {
        if (_isReadOnly)
            throw new InvalidOperationException("The graph is read-only after BuildIndex(). Build a new graph to change its nodes or edges.");
    }

    private void EnsureNodeId(int nodeId, string parameterName)
    {
        if ((uint)nodeId >= (uint)_nodeCoordinates.Count)
            throw new ArgumentOutOfRangeException(parameterName, nodeId, $"Node ID must be between 0 and {_nodeCoordinates.Count - 1}.");
    }
}
