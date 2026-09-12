using System.Collections.Frozen;

namespace TradeRouter.Passages;

/// <summary>
/// Known maritime passages, straits, and canals that can be dynamically restricted/avoided.
/// </summary>
public static class Passage
{
    /// <summary>Bab-el-Mandeb strait between Yemen and Djibouti.</summary>
    public const string Babalmandab = "babalmandab";

    /// <summary>Bosporus strait in Turkey.</summary>
    public const string Bosporus = "bosporus";

    /// <summary>Strait of Gibraltar between Spain and Morocco.</summary>
    public const string Gibraltar = "gibraltar";

    /// <summary>Suez Canal in Egypt.</summary>
    public const string Suez = "suez";

    /// <summary>Panama Canal in Panama.</summary>
    public const string Panama = "panama";

    /// <summary>Strait of Hormuz between Oman/UAE and Iran.</summary>
    public const string Ormuz = "ormuz";

    /// <summary>Northwest Passage through the Arctic Ocean.</summary>
    public const string Northwest = "northwest";

    /// <summary>Strait of Malacca between Malaysia/Singapore and Indonesia.</summary>
    public const string Malacca = "malacca";

    /// <summary>Sunda Strait in Indonesia.</summary>
    public const string Sunda = "sunda";

    /// <summary>Chilean channels and Magellan strait.</summary>
    public const string Chili = "chili";

    /// <summary>Cape of Good Hope / South Africa route.</summary>
    public const string SouthAfrica = "south_africa";

    /// <summary>Bering Strait between Russia and Alaska.</summary>
    public const string Bering = "bering";

    /// <summary>Dardanelles strait connecting the Aegean Sea to the Sea of Marmara.</summary>
    public const string Dardanelles = "dardanelles";

    private static readonly FrozenSet<string> AllPassages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Babalmandab,
        Bosporus,
        Gibraltar,
        Suez,
        Panama,
        Ormuz,
        Northwest,
        Malacca,
        Sunda,
        Chili,
        SouthAfrica,
        Bering,
        Dardanelles
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets all recognized passage identifiers.
    /// </summary>
    public static IReadOnlySet<string> ValidPassages => AllPassages;

    /// <summary>Returns a human-readable name for a recognized passage identifier.</summary>
    public static string GetDisplayName(string passage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passage);
        return passage.Trim().ToLowerInvariant() switch
        {
            Babalmandab => "Bab-el-Mandeb",
            Bosporus => "Bosporus",
            Gibraltar => "Gibraltar",
            Suez => "Suez",
            Panama => "Panama",
            Ormuz => "Hormuz",
            Northwest => "Northwest Passage",
            Malacca => "Malacca",
            Sunda => "Sunda",
            Chili => "Magellan Strait",
            SouthAfrica => "Cape of Good Hope",
            Bering => "Bering Strait",
            Dardanelles => "Dardanelles",
            _ => throw new ArgumentException($"Unknown passage identifier '{passage}'.", nameof(passage))
        };
    }

    /// <summary>
    /// Filters and returns only recognized passage names from the candidate list.
    /// </summary>
    public static List<string> FilterValidPassages(IEnumerable<string>? candidates)
    {
        if (candidates == null)
            return [];

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            string normalized = candidate.Trim().ToLowerInvariant();
            if (AllPassages.Contains(normalized))
            {
                result.Add(normalized);
            }
        }

        return result.ToList();
    }
}
