using TradeRouter.Common;
using TradeRouter.Locations;
using TradeRouter.Ports;

namespace TradeRouter.Movements;

/// <summary>
/// A place referenced by a movement leg: a UN/LOCODE, a coordinate, or both.
/// </summary>
public sealed class Location
{
    /// <summary>UN/LOCODE or other location code, upper-cased. Null when only a coordinate was given.</summary>
    public string? Code { get; }

    /// <summary>Explicit coordinate. When set it takes precedence over any lookup of <see cref="Code"/>.</summary>
    public Coordinate? Coordinate { get; }

    /// <summary>Optional display name.</summary>
    public string? Name { get; }

    private Location(string? code, Coordinate? coordinate, string? name)
    {
        Code = code;
        Coordinate = coordinate;
        Name = name;
    }

    /// <summary>Creates a location from a code that will be resolved against the port database or a resolver.</summary>
    public static Location FromCode(string code) => new(NormalizeCode(code), null, null);

    /// <summary>Creates a location from a code with a known coordinate, bypassing lookup.</summary>
    public static Location FromCode(string code, Coordinate coordinate, string? name = null) => new(NormalizeCode(code), coordinate, name);

    /// <summary>Creates a location from a coordinate alone.</summary>
    public static Location FromCoordinate(Coordinate coordinate, string? name = null) => new(null, coordinate, name);

    private static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Location code cannot be null or whitespace.", nameof(code));
        return code.Trim().ToUpperInvariant();
    }

    /// <inheritdoc />
    public override string ToString() => Code ?? Name ?? Coordinate?.ToString() ?? "?";
}

/// <summary>
/// A location after its coordinate has been determined.
/// </summary>
/// <param name="Code">Location code, if any.</param>
/// <param name="Name">Display name, if any.</param>
/// <param name="Coordinate">Resolved coordinate.</param>
/// <param name="Port">Matching port from the embedded database, when the position came from it.</param>
/// <param name="Source">Where the position came from: "coordinates", "ports", "unlocode" or "resolver".</param>
/// <param name="Functions">Known UN/LOCODE functions, or null for an unclassified custom coordinate.</param>
public readonly record struct ResolvedLocation(
    string? Code,
    string? Name,
    Coordinate Coordinate,
    Port? Port,
    string Source,
    LocationFunctions? Functions = null)
{
    /// <summary>Best available label: code, then name, then coordinate.</summary>
    public string Label => Code ?? Name ?? Coordinate.ToString();
}

/// <summary>
/// Supplies coordinates for location codes that are neither in <see cref="MovementRequest.Coordinates"/>, the embedded
/// port list nor the embedded UN/LOCODE list with coordinates. It is consulted last.
/// </summary>
public interface ILocationResolver
{
    /// <summary>
    /// Attempts to resolve a location code to a coordinate.
    /// </summary>
    bool TryResolve(string code, out Coordinate coordinate, out string? name);
}
