namespace TradeRouter.Movements;

/// <summary>
/// Counts the events on a rail leg that the optional rail time allowances on <see cref="MovementRequest"/> are
/// charged for: changes between Class I railroads along the routed path and crossings of the US–Canada or
/// US–Mexico border.
/// </summary>
/// <remarks>
/// A rail leg's travelling time is distance divided by an average speed. Real carload and intermodal moves also
/// wait at the origin and destination terminals, are handed between railroads and are held for customs at the
/// border. Those allowances default to zero, so the travelling time is unchanged unless the request sets them.
/// Sources and suggested values are recorded in DATA_PROVENANCE.md.
/// </remarks>
public static class RailServiceAllowance
{
    /// <summary>
    /// Reporting marks of the Class I railroads in the embedded rail network: the six the Association of American
    /// Railroads lists since Canadian Pacific and Kansas City Southern merged as CPKC in 2023.
    /// </summary>
    /// <remarks>Ferromex (FXE) is not a Class I railroad and is not counted.</remarks>
    public static IReadOnlySet<string> ClassIRailroads { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "BNSF", "CN", "CPKC", "CSXT", "NS", "UP"
    };

    /// <summary>Countries whose shared borders are counted as rail border crossings, by ISO 3166-1 code.</summary>
    public static IReadOnlySet<string> BorderCountries { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "CA", "MX", "US"
    };

    /// <summary>
    /// Track a Class I railroad must own along a routed leg, in kilometres, for the leg to be counted as running on it.
    /// </summary>
    public const double MinimumClassIDistanceKm = 1_000.0;

    /// <summary>
    /// Estimates the interchanges on a routed rail leg: the number of Class I railroads owning at least
    /// <see cref="MinimumClassIDistanceKm"/> of its track, less one.
    /// </summary>
    /// <param name="segments">Track owner and length of each segment of the routed path. Order does not matter.</param>
    /// <returns>The estimated number of interchanges, never negative.</returns>
    /// <remarks>
    /// The rail network records who owns the track, not who runs the train. The shortest path switches between
    /// parallel lines, such as BNSF's and Union Pacific's between Los Angeles and Chicago, and crosses short pieces of
    /// terminal, switching and short-line track, though one railroad runs the whole way. Counting every change of
    /// owner would charge Los Angeles to Chicago with five interchanges where a shipper sees none. Counting only
    /// railroads that own a long stretch of the route keeps the hand-offs that matter most, the long hauls that pass
    /// from a western to an eastern railroad at Chicago, St Louis, Memphis or New Orleans. It misses interchanges on
    /// shorter legs and counts one where a railroad runs over another's long line by trackage rights. Set
    /// <see cref="MovementRequest.RailInterchangeCounts"/> when the actual count is known.
    /// </remarks>
    public static int CountClassIInterchanges(IEnumerable<(string? Owner, double Km)> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var kmByRailroad = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (owner, km) in segments)
        {
            if (owner is not null && ClassIRailroads.Contains(owner))
                kmByRailroad[owner] = kmByRailroad.GetValueOrDefault(owner) + km;
        }
        int railroads = kmByRailroad.Values.Count(km => km >= MinimumClassIDistanceKm);
        return Math.Max(0, railroads - 1);
    }

    /// <summary>
    /// Counts the border crossings of a rail leg from the countries of its ends: one between the United States and
    /// Canada or Mexico, two between Canada and Mexico, which no rail line joins except through the United States.
    /// </summary>
    /// <param name="fromCountry">ISO 3166-1 code of the start of the leg, or null when unknown.</param>
    /// <param name="toCountry">ISO 3166-1 code of the end of the leg, or null when unknown.</param>
    /// <returns>The number of crossings, zero when either country is unknown or outside North America.</returns>
    /// <remarks>
    /// The route between the ends is not examined, so a leg between two places in one country that runs through
    /// another, such as Detroit to Buffalo through southern Ontario, is counted as no crossing.
    /// </remarks>
    public static int CountBorderCrossings(string? fromCountry, string? toCountry)
    {
        if (fromCountry is null || toCountry is null
            || !BorderCountries.Contains(fromCountry) || !BorderCountries.Contains(toCountry)
            || fromCountry == toCountry)
        {
            return 0;
        }
        return fromCountry == "US" || toCountry == "US" ? 1 : 2;
    }

    /// <summary>
    /// The country of a location taken from its UN/LOCODE, the first two letters of a five-character code, or
    /// null when the code is missing or not UN/LOCODE-shaped.
    /// </summary>
    internal static string? CountryOf(ResolvedLocation location)
    {
        var code = location.Code;
        if (code is not { Length: 5 } || !char.IsAsciiLetter(code[0]) || !char.IsAsciiLetter(code[1]))
            return null;
        return code[..2].ToUpperInvariant();
    }
}
