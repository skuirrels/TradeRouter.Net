namespace TradeRouter.Graph;

/// <summary>
/// Per-thread reusable scratch state for graph searches. Node-indexed arrays replace per-query
/// dictionaries, and a generation stamp marks which entries belong to the current query so no
/// per-query clearing is required.
/// </summary>
internal sealed class SearchBuffers
{
    [ThreadStatic]
    private static SearchBuffers? t_instance;

    private int _generation;

    /// <summary>Forward (or single-direction) tentative distances, valid when <see cref="StampF"/> equals <see cref="Generation"/>.</summary>
    public double[] DistF = [];

    /// <summary>Backward tentative distances, valid when <see cref="StampB"/> equals <see cref="Generation"/>.</summary>
    public double[] DistB = [];

    /// <summary>Forward parent pointers.</summary>
    public int[] ParentF = [];

    /// <summary>Backward parent pointers.</summary>
    public int[] ParentB = [];

    /// <summary>Generation stamp per node for the forward arrays.</summary>
    public int[] StampF = [];

    /// <summary>Generation stamp per node for the backward arrays.</summary>
    public int[] StampB = [];

    /// <summary>Generation stamp per node marking settled (closed) nodes.</summary>
    public int[] Closed = [];

    /// <summary>Forward priority queue, cleared between queries but retaining capacity.</summary>
    public readonly PriorityQueue<int, double> QueueF = new(256);

    /// <summary>Backward priority queue, cleared between queries but retaining capacity.</summary>
    public readonly PriorityQueue<int, double> QueueB = new(256);

    /// <summary>The stamp value identifying entries written by the current query.</summary>
    public int Generation => _generation;

    /// <summary>
    /// Returns the calling thread's buffers, sized for <paramref name="nodeCount"/> nodes and advanced to a fresh generation.
    /// </summary>
    public static SearchBuffers Acquire(int nodeCount)
    {
        var buffers = t_instance ??= new SearchBuffers();
        buffers.Prepare(nodeCount);
        return buffers;
    }

    private void Prepare(int nodeCount)
    {
        if (DistF.Length < nodeCount)
        {
            DistF = new double[nodeCount];
            DistB = new double[nodeCount];
            ParentF = new int[nodeCount];
            ParentB = new int[nodeCount];
            StampF = new int[nodeCount];
            StampB = new int[nodeCount];
            Closed = new int[nodeCount];
            _generation = 0;
        }

        if (_generation == int.MaxValue)
        {
            Array.Clear(StampF);
            Array.Clear(StampB);
            Array.Clear(Closed);
            _generation = 0;
        }

        _generation++;
        QueueF.Clear();
        QueueB.Clear();
    }
}
