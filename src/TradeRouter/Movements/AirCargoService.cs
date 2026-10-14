namespace TradeRouter.Movements;

/// <summary>How an air leg's cargo flies, which sets its GLEC emission intensity.</summary>
public enum AirCargoService
{
    /// <summary>Not known: GLEC's weighted average of 55% belly freight and 45% freighter.</summary>
    Unknown,

    /// <summary>On an all-cargo aircraft.</summary>
    Freighter,

    /// <summary>In the hold of a passenger aircraft.</summary>
    Belly
}
