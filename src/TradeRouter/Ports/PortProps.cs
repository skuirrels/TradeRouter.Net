using TradeRouter.Common;

namespace TradeRouter.Ports;

/// <summary>
/// Specification for a preferred port with a selection share weight and optional custom properties.
/// </summary>
public sealed class PortProps
{
    /// <summary>Identifier or UN/LOCODE of the port.</summary>
    public string PortId { get; }

    /// <summary>Selection share weight (positive number).</summary>
    public double Share { get; }

    /// <summary>Optional custom properties or coordinates.</summary>
    public IReadOnlyDictionary<string, object?>? Props { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="PortProps"/>.
    /// </summary>
    public PortProps(string portId, double share = 1.0, IReadOnlyDictionary<string, object?>? props = null)
    {
        if (string.IsNullOrWhiteSpace(portId))
            throw new ArgumentException("PortId cannot be null or whitespace.", nameof(portId));
        if (!double.IsFinite(share) || share <= 0)
            throw new ArgumentOutOfRangeException(nameof(share), "Share must be finite and greater than zero.");

        PortId = portId;
        Share = share;
        Props = props;
    }

    /// <summary>
    /// Gets coordinate if specified in props ('x' and 'y').
    /// </summary>
    public Coordinate? TryGetCoordinate()
    {
        if (Props == null)
            return null;

        if (Props.TryGetValue("x", out var xObj) && Props.TryGetValue("y", out var yObj))
        {
            if (TryConvertToDouble(xObj, out double lon) && TryConvertToDouble(yObj, out double lat))
            {
                var coordinate = new Coordinate(lon, lat);
                coordinate.Validate();
                return coordinate;
            }
        }

        return null;
    }

    private static bool TryConvertToDouble(object? obj, out double val)
    {
        if (obj is double d) { val = d; return true; }
        if (obj is float f) { val = f; return true; }
        if (obj is int i) { val = i; return true; }
        if (obj is long l) { val = l; return true; }
        if (obj is string s && double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
        {
            val = parsed;
            return true;
        }
        val = 0;
        return false;
    }
}
