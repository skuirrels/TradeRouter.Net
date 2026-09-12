namespace TradeRouter.Graph;

/// <summary>
/// Represents a directed edge in the maritime network graph.
/// </summary>
/// <param name="TargetNodeId">Index of the adjacent target node.</param>
/// <param name="Weight">Edge length/weight in kilometers.</param>
/// <param name="Passage">Optional passage name (e.g. "suez", "panama").</param>
public readonly record struct GraphEdge(int TargetNodeId, double Weight, string? Passage);
