using TradeRouter.Common;

namespace TradeRouter.Ports;

/// <summary>
/// Represents a geographic area (polygon) associated with preferred maritime ports and share weights.
/// </summary>
public sealed class AreaFeature
{
    /// <summary>Name or identifier of the area (e.g. "BE", "EUR").</summary>
    public string Name { get; }

    /// <summary>Polygon boundary vertices.</summary>
    public IReadOnlyList<Coordinate> Coordinates { get; }

    /// <summary>Preferred ports configured for this area.</summary>
    public IReadOnlyList<PortProps> PreferredPorts { get; }

    /// <summary>Planar area of the polygon.</summary>
    public double Area { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="AreaFeature"/>.
    /// </summary>
    public AreaFeature(IEnumerable<Coordinate> coordinates, string name, IEnumerable<PortProps>? preferredPorts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(coordinates);
        Name = name;
        var coordinateList = coordinates.ToList();
        if (coordinateList.Count < 3)
            throw new ArgumentException("An area polygon requires at least three vertices.", nameof(coordinates));
        foreach (var coordinate in coordinateList)
            coordinate.Validate();
        Coordinates = coordinateList.AsReadOnly();
        var preferredPortList = (preferredPorts ?? []).ToList();
        if (preferredPortList.Any(port => port is null))
            throw new ArgumentException("Preferred ports cannot contain null entries.", nameof(preferredPorts));
        PreferredPorts = preferredPortList.AsReadOnly();
        Area = PnPoly.CalculatePlanarArea(Coordinates);
    }

    /// <summary>
    /// Checks if a geographic coordinate is inside this area polygon.
    /// </summary>
    public bool Contains(Coordinate point)
    {
        return PnPoly.ContainsPoint(Coordinates, point);
    }

    /// <summary>
    /// Calculates approximate distance in km from a point to the nearest polygon edge.
    /// </summary>
    public double DistanceToPoint(Coordinate point)
    {
        if (Coordinates.Count == 0)
            return double.PositiveInfinity;

        point.Validate();
        if (Contains(point))
            return 0.0;

        const double earthRadiusKm = 6371.0088;
        double cosLatitude = Math.Cos(point.Latitude * Math.PI / 180.0);
        double minDistanceSquared = double.PositiveInfinity;
        for (int i = 0; i < Coordinates.Count; i++)
        {
            var a = Coordinates[i];
            var b = Coordinates[(i + 1) % Coordinates.Count];
            double unwrappedBx = a.Longitude + PnPoly.NormalizeLongitudeDelta(b.Longitude - a.Longitude);
            double segmentCentre = (a.Longitude + unwrappedBx) / 2.0;
            double mappedPointLongitude = point.Longitude + (360.0 * Math.Round((segmentCentre - point.Longitude) / 360.0));
            double ax = (a.Longitude - mappedPointLongitude) * Math.PI / 180.0 * cosLatitude;
            double ay = (a.Latitude - point.Latitude) * Math.PI / 180.0;
            double bx = (unwrappedBx - mappedPointLongitude) * Math.PI / 180.0 * cosLatitude;
            double by = (b.Latitude - point.Latitude) * Math.PI / 180.0;

            double dx = bx - ax;
            double dy = by - ay;
            double segmentLengthSquared = (dx * dx) + (dy * dy);
            double t = segmentLengthSquared == 0 ? 0 : Math.Clamp(((-ax * dx) + (-ay * dy)) / segmentLengthSquared, 0.0, 1.0);
            double closestX = ax + (t * dx);
            double closestY = ay + (t * dy);
            minDistanceSquared = Math.Min(minDistanceSquared, (closestX * closestX) + (closestY * closestY));
        }

        return Math.Sqrt(minDistanceSquared) * earthRadiusKm;
    }
}
