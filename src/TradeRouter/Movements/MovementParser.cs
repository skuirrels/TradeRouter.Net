using System.Diagnostics;
using System.Text.RegularExpressions;

namespace TradeRouter.Movements;

/// <summary>
/// Parses leg lines in the form <c>[Pickup|Delivery] [from] [port|place|airport|station|terminal|depot] CODE to [...] CODE MODE</c>.
/// Mode and kind words are validated against <see cref="TransportModeExtensions"/>.
/// </summary>
public static partial class MovementParser
{
    [GeneratedRegex(
        @"^\s*(?:(?<kind>[a-z]+)\s+)??(?:from\s+)?(?:(?<from_kind>port|place|airport|station|terminal|depot)\s+)?(?<from>[A-Z]{2}[A-Z0-9]{3})\s+to\s+(?:(?<to_kind>port|place|airport|station|terminal|depot)\s+)?(?<to>[A-Z]{2}[A-Z0-9]{3})\s+(?<mode>[a-z]+)\s*\.?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegLine();

    private const string ExpectedForm = "[Pickup|Delivery] [port|place|airport|station|terminal|depot] CODE to [port|place|airport|...] CODE Sea|Road|Rail|Air";

    /// <summary>
    /// Parses one leg per non-blank line. Throws <see cref="FormatException"/> naming the first line, by its
    /// position in the input text, that does not parse.
    /// </summary>
    public static List<MovementLeg> Parse(string legsText)
    {
        ArgumentNullException.ThrowIfNull(legsText);

        var legs = new List<MovementLeg>();
        var lines = legsText.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r').Trim();
            if (line.Length == 0)
                continue;

            var leg = ParseLine(line, i + 1);
            legs.Add(leg);
        }

        if (legs.Count == 0)
            throw new FormatException("No legs found.");

        return legs;
    }

    /// <summary>
    /// Parses a single leg line, returning null when it is not a recognised leg.
    /// </summary>
    public static MovementLeg? TryParseLine(string line)
    {
        try
        {
            return string.IsNullOrWhiteSpace(line) ? null : ParseLine(line.Trim(), lineNumber: 1);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static MovementLeg ParseLine(string line, int lineNumber)
    {
        var match = LegLine().Match(line);
        if (!match.Success)
            throw new FormatException($"Line {lineNumber} is not a recognised leg: \"{line}\". Expected \"{ExpectedForm}\".");

        var kind = LegKind.Main;
        var kindGroup = match.Groups["kind"];
        if (kindGroup.Success && !TransportModeExtensions.TryParseKind(kindGroup.Value, out kind))
            throw new FormatException($"Line {lineNumber}: unknown leg kind \"{kindGroup.Value}\". Expected Pickup, Delivery or Main.");

        var modeText = match.Groups["mode"].Value;
        if (!TransportModeExtensions.TryParseMode(modeText, out var mode))
            throw new FormatException($"Line {lineNumber}: unknown transport mode \"{modeText}\". Expected Sea, Road, Rail or Air.");

        return new MovementLeg(
            match.Groups["from"].Value,
            match.Groups["to"].Value,
            mode,
            kind,
            ParseWaypointKind(match.Groups["from_kind"].Value),
            ParseWaypointKind(match.Groups["to_kind"].Value));
    }

    private static WaypointKind ParseWaypointKind(string value) => value.ToLowerInvariant() switch
    {
        "" => WaypointKind.Unspecified,
        "place" => WaypointKind.Place,
        "port" => WaypointKind.Port,
        "airport" => WaypointKind.Airport,
        "station" => WaypointKind.Station,
        "terminal" => WaypointKind.Terminal,
        "depot" => WaypointKind.Depot,
        _ => throw new UnreachableException()
    };
}
