using TradeRouter.Common;

namespace TradeRouter.Locations;

/// <summary>
/// Functions a UN/LOCODE location performs, as flagged in the UNECE code list.
/// </summary>
[Flags]
public enum LocationFunctions
{
    /// <summary>No function recorded.</summary>
    None = 0,
    /// <summary>Sea port.</summary>
    SeaPort = 1,
    /// <summary>Rail terminal.</summary>
    RailTerminal = 2,
    /// <summary>Road terminal.</summary>
    RoadTerminal = 4,
    /// <summary>Airport.</summary>
    Airport = 8,
    /// <summary>Postal exchange office.</summary>
    PostalExchange = 16,
    /// <summary>Multimodal facility or inland container depot.</summary>
    Multimodal = 32,
    /// <summary>Fixed transport such as a pipeline or oil platform.</summary>
    FixedTransport = 64,
    /// <summary>Border crossing.</summary>
    BorderCrossing = 128
}

/// <summary>
/// One entry of the UNECE UN/LOCODE list. About a fifth of entries carry no coordinates.
/// </summary>
/// <param name="Code">Five-character UN/LOCODE.</param>
/// <param name="Name">Location name without diacritics.</param>
/// <param name="Coordinate">Position to one minute of arc, or null when UNECE publishes none.</param>
/// <param name="Functions">Recorded functions, with the sea-port function added for codes listed in the sea-port supplement file.</param>
/// <param name="CoordinateSource">"UNECE" when the position comes from the code list, otherwise the researched source named in the supplement file; empty when there is no position.</param>
public sealed record UnLocode(string Code, string Name, Coordinate? Coordinate, LocationFunctions Functions, string CoordinateSource = "")
{
    /// <summary>Two-letter ISO country code, the first two characters of the code.</summary>
    public string Country => Code[..2];

    /// <summary>Whether the entry is flagged as a sea port.</summary>
    public bool IsSeaPort => (Functions & LocationFunctions.SeaPort) != 0;

    /// <summary>Whether the entry is flagged as an airport.</summary>
    public bool IsAirport => (Functions & LocationFunctions.Airport) != 0;
}

/// <summary>
/// Lookup over the embedded UN/LOCODE list.
/// </summary>
public sealed class UnLocodeDatabase
{
    private readonly Dictionary<string, UnLocode> _byCode;

    /// <summary>Number of entries.</summary>
    public int Count => _byCode.Count;

    /// <summary>Number of entries that carry coordinates.</summary>
    public int CountWithCoordinates { get; }

    /// <summary>
    /// Initializes a new instance over the given entries. Later duplicates replace earlier ones.
    /// </summary>
    public UnLocodeDatabase(IEnumerable<UnLocode> entries)
    {
        _byCode = new Dictionary<string, UnLocode>(StringComparer.OrdinalIgnoreCase);
        int withCoordinates = 0;
        foreach (var entry in entries)
        {
            _byCode[entry.Code] = entry;
            if (entry.Coordinate.HasValue)
                withCoordinates++;
        }
        CountWithCoordinates = withCoordinates;
    }

    /// <summary>
    /// Looks up a code, case-insensitively.
    /// </summary>
    public UnLocode? GetByCode(string code)
    {
        return _byCode.TryGetValue(code.Trim(), out var entry) ? entry : null;
    }
}
