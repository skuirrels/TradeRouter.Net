using TradeRouter.Graph;

namespace TradeRouter.Common;

/// <summary>
/// Handles coordinate normalization across the antimeridian (±180° longitude)
/// to avoid wrapping artifacts across world maps.
/// </summary>
public static class RouteNormalizer
{
    /// <summary>
    /// Normalizes the current coordinate relative to the previous coordinate in a LineString.
    /// </summary>
    public static Coordinate NormalizeLineString(Coordinate? prev, Coordinate now)
    {
        if (prev is null)
            return now;

        double nowX = now.Longitude;
        double nowY = now.Latitude;
        double prevX = prev.Value.Longitude;

        double px = prevX - nowX;
        if (px < -180.0)
        {
            nowX -= 360.0;
        }
        else if (px > 180.0)
        {
            nowX += 360.0;
        }

        return new Coordinate(nowX, nowY);
    }

    /// <summary>
    /// Normalizes a sequence of coordinates along a path.
    /// </summary>
    public static List<Coordinate> NormalizeRoute(IReadOnlyList<Coordinate> route)
    {
        if (route == null || route.Count == 0)
            return [];

        var result = new List<Coordinate>(route.Count);
        Coordinate? previous = null;

        foreach (var coord in route)
        {
            var normalized = NormalizeLineString(previous, coord);
            result.Add(normalized);
            previous = normalized;
        }

        return result;
    }

    /// <summary>
    /// Normalizes coordinates and extracts traversed passages from the maritime graph along the route.
    /// </summary>
    public static (List<Coordinate> LineString, List<string> TraversedPassages) ProcessRoute(
        IReadOnlyList<Coordinate> route,
        MaritimeGraph graph,
        bool returnPassages = false)
    {
        if (route == null || route.Count == 0)
            return ([], []);

        var ls = new List<Coordinate>(route.Count);
        Coordinate? previous = null;
        Coordinate? previousTraversed = null;
        var traversedPassages = new List<string>();

        if (returnPassages)
        {
            foreach (var now in route)
            {
                if (previousTraversed.HasValue)
                {
                    string? passage = graph.GetPassage(previousTraversed.Value, now);
                    if (!string.IsNullOrEmpty(passage))
                    {
                        traversedPassages.Add(passage);
                    }
                }

                var fixedCoords = NormalizeLineString(previous, now);
                ls.Add(fixedCoords);
                previous = fixedCoords;
                previousTraversed = now;
            }

            return (ls, traversedPassages);
        }
        else
        {
            foreach (var now in route)
            {
                var fixedCoords = NormalizeLineString(previous, now);
                ls.Add(fixedCoords);
                previous = fixedCoords;
            }

            return (ls, traversedPassages);
        }
    }
}
