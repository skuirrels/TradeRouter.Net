namespace TradeRouter.Movements;

/// <summary>
/// Transport mode of a movement leg. Only <see cref="Sea"/> is routed on the maritime network;
/// road uses provider-routed or estimated distance, while rail and air use great-circle distance.
/// </summary>
public enum TransportMode
{
    /// <summary>Ocean or coastal shipping, routed on the Marnet graph.</summary>
    Sea,
    /// <summary>Road haulage, resolved by a road-network provider or fallback estimator.</summary>
    Road,
    /// <summary>Rail, straight-line leg.</summary>
    Rail,
    /// <summary>Air freight, straight-line leg.</summary>
    Air
}

/// <summary>
/// Role of a leg within a movement.
/// </summary>
public enum LegKind
{
    /// <summary>Main carriage, typically port to port.</summary>
    Main,
    /// <summary>Pre-carriage from the pickup place to the port of loading.</summary>
    Pickup,
    /// <summary>On-carriage from the port of discharge to the delivery place.</summary>
    Delivery
}

/// <summary>Semantic type declared for a movement endpoint.</summary>
public enum WaypointKind
{
    /// <summary>No endpoint type was declared.</summary>
    Unspecified,
    /// <summary>A general place with no required transport function.</summary>
    Place,
    /// <summary>A maritime port.</summary>
    Port,
    /// <summary>An airport.</summary>
    Airport,
    /// <summary>A rail station or terminal.</summary>
    Station,
    /// <summary>A transport terminal of any recorded terminal type.</summary>
    Terminal,
    /// <summary>A depot or multimodal facility.</summary>
    Depot
}

/// <summary>
/// The single source of truth for how modes and kinds are written in leg lines and in GeoJSON.
/// </summary>
public static class TransportModeExtensions
{
    /// <summary>Canonical lower-case identifier written to GeoJSON.</summary>
    public static string ToWireString(this TransportMode mode) => mode switch
    {
        TransportMode.Sea => "sea",
        TransportMode.Road => "road",
        TransportMode.Rail => "rail",
        TransportMode.Air => "air",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown transport mode.")
    };

    /// <summary>Canonical lower-case identifier written to GeoJSON.</summary>
    public static string ToWireString(this LegKind kind) => kind switch
    {
        LegKind.Main => "main",
        LegKind.Pickup => "pickup",
        LegKind.Delivery => "delivery",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown leg kind.")
    };

    /// <summary>Accepted spellings of each mode in leg lines, canonical form first.</summary>
    public static IReadOnlyDictionary<TransportMode, string[]> ModeSpellings { get; } = new Dictionary<TransportMode, string[]>
    {
        [TransportMode.Sea] = ["sea", "ocean", "vessel", "ship"],
        [TransportMode.Road] = ["road", "truck", "lorry"],
        [TransportMode.Rail] = ["rail", "train"],
        [TransportMode.Air] = ["air", "flight", "plane"]
    };

    /// <summary>Accepted spellings of each kind in leg lines, canonical form first.</summary>
    public static IReadOnlyDictionary<LegKind, string[]> KindSpellings { get; } = new Dictionary<LegKind, string[]>
    {
        [LegKind.Main] = ["main"],
        [LegKind.Pickup] = ["pickup", "collection"],
        [LegKind.Delivery] = ["delivery"]
    };

    /// <summary>Parses a mode word, case-insensitively, accepting the spellings in <see cref="ModeSpellings"/>.</summary>
    public static bool TryParseMode(string? text, out TransportMode mode)
    {
        foreach (var (candidate, spellings) in ModeSpellings)
        {
            if (Matches(text, spellings))
            {
                mode = candidate;
                return true;
            }
        }
        mode = default;
        return false;
    }

    /// <summary>Parses a kind word, case-insensitively, accepting the spellings in <see cref="KindSpellings"/>.</summary>
    public static bool TryParseKind(string? text, out LegKind kind)
    {
        foreach (var (candidate, spellings) in KindSpellings)
        {
            if (Matches(text, spellings))
            {
                kind = candidate;
                return true;
            }
        }
        kind = default;
        return false;
    }

    private static bool Matches(string? text, string[] spellings)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var trimmed = text.AsSpan().Trim();
        foreach (var spelling in spellings)
        {
            if (trimmed.Equals(spelling, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
