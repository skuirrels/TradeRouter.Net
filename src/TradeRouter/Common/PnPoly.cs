namespace TradeRouter.Common;

/// <summary>
/// Point-in-polygon algorithm based on W. Randolph Franklin's PNPOLY.
/// </summary>
public static class PnPoly
{
    /// <summary>
    /// Determines whether a given coordinate point is located inside a polygon defined by its vertices.
    /// </summary>
    /// <param name="vertices">List of polygon vertex coordinates (outer ring).</param>
    /// <param name="testPoint">The point to test.</param>
    /// <returns>True if the point is strictly inside or on edge; false otherwise.</returns>
    public static bool ContainsPoint(IReadOnlyList<Coordinate> vertices, Coordinate testPoint)
    {
        if (vertices == null || vertices.Count < 3)
            return false;

        int nvert = vertices.Count;
        testPoint.Validate();
        var longitudes = UnwrapLongitudes(vertices);
        double centre = (longitudes.Min() + longitudes.Max()) / 2.0;
        double testx = testPoint.Longitude + (360.0 * Math.Round((centre - testPoint.Longitude) / 360.0));
        double testy = testPoint.Latitude;
        bool inside = false;

        for (int i = 0, j = nvert - 1; i < nvert; j = i++)
        {
            double viY = vertices[i].Latitude;
            double vjY = vertices[j].Latitude;
            double viX = longitudes[i];
            double vjX = longitudes[j];

            if (((viY > testy) != (vjY > testy)) &&
                (testx < (vjX - viX) * (testy - viY) / (vjY - viY) + viX))
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>
    /// Calculates the planar area of a polygon using the Shoelace formula (used for ordering smallest AreaFeature).
    /// </summary>
    public static double CalculatePlanarArea(IReadOnlyList<Coordinate> vertices)
    {
        if (vertices == null || vertices.Count < 3)
            return 0.0;

        double area = 0.0;
        int n = vertices.Count;
        var longitudes = UnwrapLongitudes(vertices);
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            double nextLongitude = j == 0
                ? longitudes[i] + NormalizeLongitudeDelta(longitudes[0] - longitudes[i])
                : longitudes[j];
            area += (longitudes[i] * vertices[j].Latitude) - (nextLongitude * vertices[i].Latitude);
        }

        return Math.Abs(area / 2.0);
    }

    internal static double NormalizeLongitudeDelta(double delta) => ((delta % 360.0) + 540.0) % 360.0 - 180.0;

    private static double[] UnwrapLongitudes(IReadOnlyList<Coordinate> vertices)
    {
        var result = new double[vertices.Count];
        result[0] = vertices[0].Longitude;
        for (int i = 1; i < vertices.Count; i++)
            result[i] = result[i - 1] + NormalizeLongitudeDelta(vertices[i].Longitude - result[i - 1]);
        return result;
    }
}
