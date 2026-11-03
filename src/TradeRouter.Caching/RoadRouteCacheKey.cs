using System.Globalization;
using TradeRouter.Common;
using TradeRouter.Movements;

namespace TradeRouter.Caching;

/// <summary>
/// Identifies one cached road route: a caller-chosen partition and the two endpoint coordinates, with longitude
/// normalised to [-180, 180) and both rounded, so that the same place resolved twice maps to the same entry. The leg
/// sequence and the endpoints' codes and names are not part of the key, because a road-route provider routes between
/// coordinates.
/// </summary>
public sealed record RoadRouteCacheKey
{
    private RoadRouteCacheKey(string partition, Coordinate from, Coordinate to)
    {
        Partition = partition;
        From = from;
        To = to;
        Value = string.Create(
            CultureInfo.InvariantCulture,
            $"{partition}|{from.Longitude:R},{from.Latitude:R}|{to.Longitude:R},{to.Latitude:R}");
    }

    /// <summary>Caller-chosen name for the provider, profile and road dataset the route came from.</summary>
    public string Partition { get; }

    /// <summary>Normalised and rounded road origin.</summary>
    public Coordinate From { get; }

    /// <summary>Normalised and rounded road destination.</summary>
    public Coordinate To { get; }

    /// <summary>
    /// The key as one string, <c>partition|lon,lat|lon,lat</c> in invariant culture, for stores keyed by string.
    /// </summary>
    public string Value { get; }

    /// <summary>Builds the key for a road-route request.</summary>
    /// <param name="partition">
    /// Name for the provider, profile and road dataset, for example <c>osrm-driving-europe-261001</c>. Change it when
    /// any of them changes, so routes from the old setup are no longer served.
    /// </param>
    /// <param name="request">The road-route request.</param>
    /// <param name="coordinateDecimals">Decimal places the endpoint coordinates are rounded to, from 0 to 10.</param>
    public static RoadRouteCacheKey Create(string partition, RoadRouteRequest request, int coordinateDecimals)
    {
        ValidatePartition(partition);
        ValidateCoordinateDecimals(coordinateDecimals);
        return CreateValidated(partition.Trim(), request, coordinateDecimals);
    }

    // For callers that validated and trimmed the partition and decimals once, up front.
    internal static RoadRouteCacheKey CreateValidated(string partition, RoadRouteRequest request, int coordinateDecimals)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.From.Coordinate.Validate();
        request.To.Coordinate.Validate();

        return new RoadRouteCacheKey(
            partition,
            Round(request.From.Coordinate, coordinateDecimals),
            Round(request.To.Coordinate, coordinateDecimals));
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    internal static void ValidatePartition(string partition)
    {
        if (string.IsNullOrWhiteSpace(partition))
            throw new ArgumentException("The cache partition cannot be blank.", nameof(partition));
        if (partition.Contains('|'))
            throw new ArgumentException("The cache partition cannot contain '|', which separates the parts of a key.", nameof(partition));
    }

    internal static void ValidateCoordinateDecimals(int coordinateDecimals)
    {
        if (coordinateDecimals is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coordinateDecimals),
                coordinateDecimals,
                "Coordinates can be rounded to between 0 and 10 decimal places.");
        }
    }

    // Longitude is normalised before rounding, which absorbs the normaliser's floating-point noise, and a rounded
    // 180 becomes -180, so the same meridian always gives the same key. Adding 0.0 turns a rounded -0 into 0, so a
    // coordinate just below zero shares the entry of one just above.
    private static Coordinate Round(Coordinate coordinate, int decimals)
    {
        double longitude = Math.Round(coordinate.WithNormalizedLongitude().Longitude, decimals, MidpointRounding.AwayFromZero);
        if (longitude >= 180.0)
            longitude -= 360.0;

        return new Coordinate(
            longitude + 0.0,
            Math.Round(coordinate.Latitude, decimals, MidpointRounding.AwayFromZero) + 0.0);
    }
}
