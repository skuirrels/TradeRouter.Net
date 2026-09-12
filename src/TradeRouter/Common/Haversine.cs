namespace TradeRouter.Common;

/// <summary>
/// Geodesic and Great-Circle distance calculations.
/// </summary>
public static class Haversine
{
    private const double AvgEarthRadiusMeters = 6371008.8;
    private const double EarthRadiusKm = AvgEarthRadiusMeters / 1000.0;

    /// <summary>
    /// Calculates the great circle distance between two coordinates in the specified distance unit.
    /// </summary>
    public static double Distance(Coordinate c1, Coordinate c2, DistanceUnit unit = DistanceUnit.Km)
    {
        c1.Validate();
        c2.Validate();
        double dLat = ToRadians(c2.Latitude - c1.Latitude);
        double dLon = ToRadians(c2.Longitude - c1.Longitude);

        double lat1 = ToRadians(c1.Latitude);
        double lat2 = ToRadians(c2.Latitude);

        double sinHalfDLat = Math.Sin(dLat / 2.0);
        double sinHalfDLon = Math.Sin(dLon / 2.0);

        double a = (sinHalfDLat * sinHalfDLat) + (sinHalfDLon * sinHalfDLon * Math.Cos(lat1) * Math.Cos(lat2));
        a = Math.Clamp(a, 0.0, 1.0);
        double b = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));

        return b * AvgEarthRadiusMeters * unit.GetConversionFactorFromMeters();
    }

    /// <summary>
    /// Calculates the Haversine distance in kilometers directly.
    /// Used as the A* heuristic.
    /// </summary>
    public static double DistanceKm(Coordinate c1, Coordinate c2)
    {
        c1.Validate();
        c2.Validate();
        return DistanceKmUnchecked(c1, c2);
    }

    internal static double DistanceKmUnchecked(Coordinate c1, Coordinate c2)
    {
        double lat1 = ToRadians(c1.Latitude);
        double lat2 = ToRadians(c2.Latitude);
        double dLat = lat2 - lat1;
        double dLon = ToRadians(c2.Longitude - c1.Longitude);

        double sinHalfDLat = Math.Sin(dLat / 2.0);
        double sinHalfDLon = Math.Sin(dLon / 2.0);

        double a = (sinHalfDLat * sinHalfDLat) + (Math.Cos(lat1) * Math.Cos(lat2) * sinHalfDLon * sinHalfDLon);
        a = Math.Clamp(a, 0.0, 1.0);
        return EarthRadiusKm * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
    }

    /// <summary>
    /// Calculates the total path length of a sequence of coordinates in the specified unit.
    /// </summary>
    public static double CalculatePathLength(IReadOnlyList<Coordinate> coordinates, DistanceUnit unit = DistanceUnit.Km)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        if (!Enum.IsDefined(unit))
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown distance unit.");
        foreach (var coordinate in coordinates)
            coordinate.Validate();
        if (coordinates.Count < 2)
            return 0.0;

        double total = 0.0;
        for (int i = 0; i < coordinates.Count - 1; i++)
        {
            total += Distance(coordinates[i], coordinates[i + 1], unit);
        }
        return total;
    }

    /// <summary>
    /// Calculates route duration in hours given vessel speed in knots and total path length in unit.
    /// </summary>
    public static double CalculateDurationHours(double speedKnots, double length, DistanceUnit unit)
    {
        if (!double.IsFinite(speedKnots) || speedKnots <= 0)
            throw new ArgumentOutOfRangeException(nameof(speedKnots), speedKnots, "Speed must be a finite positive value.");
        if (!double.IsFinite(length) || length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), length, "Length must be a finite non-negative value.");
        if (!Enum.IsDefined(unit))
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown distance unit.");
        if (length == 0)
            return 0.0;

        double speedInUnit = speedKnots * unit.GetSpeedCoefficient();
        return length / speedInUnit;
    }

    /// <summary>
    /// Calculates squared chord distance on a unit sphere. Its ordering is identical to great-circle distance.
    /// </summary>
    public static double UnitSphereDistanceSquared(Coordinate c1, Coordinate c2)
    {
        var a = ToUnitVector(c1);
        var b = ToUnitVector(c2);
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        double dz = a.Z - b.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    internal static (double X, double Y, double Z) ToUnitVector(Coordinate coordinate)
    {
        coordinate.Validate();
        double lat = ToRadians(coordinate.Latitude);
        double lon = ToRadians(coordinate.Longitude);
        double cosLat = Math.Cos(lat);
        return (cosLat * Math.Cos(lon), cosLat * Math.Sin(lon), Math.Sin(lat));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static double ToRadians(double degrees) => degrees * (Math.PI / 180.0);
}
