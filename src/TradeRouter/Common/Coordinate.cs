namespace TradeRouter.Common;

/// <summary>
/// Represents a geographic coordinate with Longitude (X) and Latitude (Y).
/// </summary>
/// <param name="Longitude">Longitude in degrees (between -180 and 180 for standard coordinates, or beyond for unwrapped lines).</param>
/// <param name="Latitude">Latitude in degrees (between -90 and 90).</param>
public readonly record struct Coordinate(double Longitude, double Latitude)
{
    /// <summary>
    /// Validates that the coordinate has valid latitude and finite numbers.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when coordinates are invalid.</exception>
    public void Validate()
    {
        if (double.IsNaN(Latitude) || double.IsInfinity(Latitude) ||
            double.IsNaN(Longitude) || double.IsInfinity(Longitude))
        {
            throw new ArgumentException($"Invalid coordinate values: ({Longitude}, {Latitude})");
        }

        if (Latitude < -90.0 || Latitude > 90.0)
        {
            throw new ArgumentException($"Latitude must be between -90 and 90 degrees. Received: {Latitude}");
        }
    }

    /// <summary>
    /// Normalizes the longitude to the [-180, 180] range.
    /// </summary>
    public Coordinate WithNormalizedLongitude()
    {
        double normalizedLon = ((Longitude % 360.0) + 540.0) % 360.0 - 180.0;
        return new Coordinate(normalizedLon, Latitude);
    }

    /// <summary>
    /// Creates a Coordinate from a [longitude, latitude] array or tuple.
    /// </summary>
    public static Coordinate FromArray(double[] coords)
    {
        if (coords == null || coords.Length < 2)
            throw new ArgumentException("Coordinate array must contain at least 2 elements [lon, lat].");
        return new Coordinate(coords[0], coords[1]);
    }

    /// <summary>
    /// Creates a Coordinate from a (longitude, latitude) tuple.
    /// </summary>
    public static Coordinate FromTuple((double lon, double lat) tuple) => new(tuple.lon, tuple.lat);

    /// <inheritdoc />
    public override string ToString() =>
        $"[{Longitude.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, {Latitude.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}]";
}
