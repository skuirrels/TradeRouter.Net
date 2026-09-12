namespace TradeRouter.Common;

/// <summary>
/// Supported distance units for route length calculations.
/// </summary>
public enum DistanceUnit
{
    /// <summary>Kilometers (km) - default.</summary>
    Km,
    /// <summary>Meters (m).</summary>
    Meters,
    /// <summary>Statute Miles (mi).</summary>
    Miles,
    /// <summary>Feet (ft).</summary>
    Feet,
    /// <summary>Inches (in).</summary>
    Inches,
    /// <summary>Degrees (deg).</summary>
    Degrees,
    /// <summary>Centimeters (cen).</summary>
    Centimeters,
    /// <summary>Radians (rad).</summary>
    Radians,
    /// <summary>Nautical Miles (naut / nm).</summary>
    NauticalMiles,
    /// <summary>Yards (yd).</summary>
    Yards
}

/// <summary>
/// Extension methods and helpers for <see cref="DistanceUnit"/>.
/// </summary>
public static class DistanceUnitExtensions
{
    private const double AvgEarthRadiusMeters = 6371008.8;

    /// <summary>
    /// Converts a unit string (e.g., "km", "mi", "naut") to the corresponding <see cref="DistanceUnit"/>.
    /// </summary>
    public static DistanceUnit Parse(string? unitStr)
    {
        if (string.IsNullOrWhiteSpace(unitStr))
            return DistanceUnit.Km;

        return unitStr.Trim().ToLowerInvariant() switch
        {
            "km" or "kilometer" or "kilometers" => DistanceUnit.Km,
            "m" or "meter" or "meters" => DistanceUnit.Meters,
            "mi" or "mile" or "miles" => DistanceUnit.Miles,
            "ft" or "foot" or "feet" => DistanceUnit.Feet,
            "in" or "inch" or "inches" => DistanceUnit.Inches,
            "deg" or "degree" or "degrees" => DistanceUnit.Degrees,
            "cen" or "cm" or "centimeter" or "centimeters" => DistanceUnit.Centimeters,
            "rad" or "radian" or "radians" => DistanceUnit.Radians,
            "naut" or "nm" or "nautical" or "nauticalmile" or "nauticalmiles" => DistanceUnit.NauticalMiles,
            "yd" or "yard" or "yards" => DistanceUnit.Yards,
            _ => throw new ArgumentException($"Unsupported distance unit: '{unitStr}'. Valid units: km, m, mi, ft, in, deg, cen, rad, naut, yd.")
        };
    }

    /// <summary>
    /// Returns the canonical string representation for the unit.
    /// </summary>
    public static string ToUnitString(this DistanceUnit unit) => unit switch
    {
        DistanceUnit.Km => "km",
        DistanceUnit.Meters => "m",
        DistanceUnit.Miles => "mi",
        DistanceUnit.Feet => "ft",
        DistanceUnit.Inches => "in",
        DistanceUnit.Degrees => "deg",
        DistanceUnit.Centimeters => "cen",
        DistanceUnit.Radians => "rad",
        DistanceUnit.NauticalMiles => "naut",
        DistanceUnit.Yards => "yd",
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown distance unit.")
    };

    /// <summary>
    /// Gets the conversion factor from meters to the target unit (valueInTargetUnit = meters * factor).
    /// </summary>
    public static double GetConversionFactorFromMeters(this DistanceUnit unit) => unit switch
    {
        DistanceUnit.Km => 0.001,
        DistanceUnit.Meters => 1.0,
        DistanceUnit.Miles => 0.000621371192,
        DistanceUnit.Feet => 3.28084,
        DistanceUnit.Inches => 39.370,
        DistanceUnit.Degrees => 1.0 / 111325.0,
        DistanceUnit.Centimeters => 100.0,
        DistanceUnit.Radians => 1.0 / AvgEarthRadiusMeters,
        DistanceUnit.NauticalMiles => 0.000539956803,
        DistanceUnit.Yards => 1.0936132983377078,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown distance unit.")
    };

    /// <summary>
    /// Gets the speed coefficient used to convert speed in knots to the target distance unit per hour.
    /// speedInUnitPerHour = speedInKnots * GetSpeedCoefficient(unit).
    /// </summary>
    public static double GetSpeedCoefficient(this DistanceUnit unit) => unit switch
    {
        DistanceUnit.Km => 1.852,
        DistanceUnit.Meters => 1852.0,
        DistanceUnit.Miles => 1.15078,
        DistanceUnit.Feet => 6076.12,
        DistanceUnit.Inches => 72913.4,
        DistanceUnit.Degrees => 1852.0 / 111325.0,
        DistanceUnit.Centimeters => 185200.0,
        DistanceUnit.Radians => 1852.0 / AvgEarthRadiusMeters,
        DistanceUnit.NauticalMiles => 1.0,
        DistanceUnit.Yards => 2025.37,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown distance unit.")
    };
}
