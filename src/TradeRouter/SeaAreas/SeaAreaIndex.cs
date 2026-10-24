using TradeRouter.Common;
using TradeRouter.Data;

namespace TradeRouter.SeaAreas;

/// <summary>
/// Finds where a route enters and leaves each named sea area. The route is followed along great circles, as its
/// length is measured, and each polygon is read as drawn in longitude and latitude, as Natural Earth draws it.
/// </summary>
internal sealed class SeaAreaIndex
{
    /// <summary>
    /// Longest stretch of great circle followed as one straight line in longitude and latitude. A 10 km chord
    /// strays from the great circle by metres, far below the accuracy of the 1:50m polygons.
    /// </summary>
    internal const double MaxChordKm = 10.0;

    /// <summary>Side of the square grid cells, in degrees, that index each polygon's boundary segments.</summary>
    private const double CellDegrees = 1.0;

    private static readonly Lazy<SeaAreaIndex> LazyDefault =
        new(EmbeddedResources.LoadSeaAreaIndex, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Index over the embedded Natural Earth polygons, loaded on first use.</summary>
    internal static SeaAreaIndex Default => LazyDefault.Value;

    private readonly SeaAreaInfo[] _areas;
    private readonly AreaShape[] _shapes;

    /// <summary>
    /// Creates an index. Each area's rings are flat longitude, latitude pairs; outer rings and holes are told apart
    /// by the even-odd rule, so a point is inside when a ray from it crosses the area's rings an odd number of times.
    /// </summary>
    internal SeaAreaIndex(IReadOnlyList<(SeaAreaInfo Info, IReadOnlyList<double[]> Rings)> areas)
    {
        ArgumentNullException.ThrowIfNull(areas);
        _areas = areas.Select(area => area.Info).ToArray();
        _shapes = areas.Select(area => new AreaShape(area.Rings)).ToArray();
    }

    /// <summary>
    /// Returns the sea areas along a path, in the order it enters them, with distances in kilometres from its start.
    /// The path must be continuous: longitudes may run past ±180 as the route normaliser leaves them.
    /// </summary>
    internal List<SeaAreaTraversal> Traverse(IReadOnlyList<Coordinate> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var walk = new Walk(_shapes);
        double startKm = 0.0;
        for (int i = 1; i < path.Count; i++)
        {
            var from = path[i - 1];
            var to = path[i];
            double segmentKm = Haversine.Distance(from, to, DistanceUnit.Km);
            if (segmentKm <= 0.0)
                continue;

            // Each lane point starts afresh with a point-in-polygon test, so no crossing error carries past it.
            walk.ForgetState();
            int chords = Math.Max(1, (int)Math.Ceiling(segmentKm / MaxChordKm));
            var arc = new GreatCircleArc(from, to);
            var (chordStartLon, chordStartLat) = (from.Longitude, from.Latitude);
            for (int k = 1; k <= chords; k++)
            {
                var (chordEndLon, chordEndLat) = k == chords ? (to.Longitude, to.Latitude) : arc.PointAt((double)k / chords);
                walk.Chord(
                    chordStartLon, chordStartLat, chordEndLon, chordEndLat,
                    startKm + (segmentKm * (k - 1) / chords),
                    startKm + (segmentKm * k / chords));
                (chordStartLon, chordStartLat) = (chordEndLon, chordEndLat);
            }

            startKm += segmentKm;
        }

        walk.Finish(startKm);
        return Merge(walk.Intervals)
            .Select(interval => new SeaAreaTraversal
            {
                Id = _areas[interval.Area].Id,
                Name = _areas[interval.Area].Name,
                Wikidata = _areas[interval.Area].Wikidata,
                EnterKm = interval.EnterKm,
                ExitKm = interval.ExitKm
            })
            .ToList();
    }

    /// <summary>
    /// Joins a return to an area with its previous visit when no other listed area is crossed in between, as when
    /// a route passes through the North Atlantic's unlisted Sargasso Sea hole, then orders visits by entry.
    /// </summary>
    internal static List<AreaInterval> Merge(IReadOnlyList<AreaInterval> intervals)
    {
        var merged = new List<AreaInterval>(intervals.Count);
        foreach (var visits in intervals.GroupBy(interval => interval.Area))
        {
            AreaInterval? current = null;
            foreach (var next in visits.OrderBy(interval => interval.EnterKm))
            {
                if (current is { } previous &&
                    !intervals.Any(other => other.Area != next.Area && other.EnterKm < next.EnterKm && other.ExitKm > previous.ExitKm))
                {
                    current = previous with { ExitKm = Math.Max(previous.ExitKm, next.ExitKm) };
                    continue;
                }

                if (current is { } finished)
                    merged.Add(finished);
                current = next;
            }

            if (current is { } last)
                merged.Add(last);
        }

        return merged
            .OrderBy(interval => interval.EnterKm)
            .ThenBy(interval => interval.ExitKm)
            .ThenBy(interval => interval.Area)
            .ToList();
    }

    /// <summary>One continuous stay inside one area, in kilometres from the start of the path.</summary>
    internal readonly record struct AreaInterval(int Area, double EnterKm, double ExitKm);

    /// <summary>Tracks whether the path is inside each area as it advances chord by chord.</summary>
    private sealed class Walk(AreaShape[] shapes)
    {
        private readonly bool[] _inside = new bool[shapes.Length];
        private readonly bool[] _known = new bool[shapes.Length];
        private readonly double[] _enteredKm = new double[shapes.Length];
        private readonly List<double> _crossings = [];
        private double _lastX = double.NaN;

        public List<AreaInterval> Intervals { get; } = [];

        public void ForgetState() => Array.Clear(_known);

        /// <summary>Advances along one chord, splitting it where it crosses the antimeridian.</summary>
        public void Chord(double lon0, double lat0, double lon1, double lat1, double km0, double km1)
        {
            double x0 = new Coordinate(lon0, lat0).WithNormalizedLongitude().Longitude;
            double x1 = x0 + PnPoly.NormalizeLongitudeDelta(lon1 - lon0);
            if (x1 is <= 180.0 and >= -180.0)
            {
                Piece(x0, lat0, x1, lat1, km0, km1);
                return;
            }

            double boundary = x1 > 180.0 ? 180.0 : -180.0;
            double fraction = (boundary - x0) / (x1 - x0);
            double latAtBoundary = lat0 + ((lat1 - lat0) * fraction);
            double kmAtBoundary = km0 + ((km1 - km0) * fraction);
            Piece(x0, lat0, boundary, latAtBoundary, km0, kmAtBoundary);
            double shift = boundary > 0 ? -360.0 : 360.0;
            Piece(boundary + shift, latAtBoundary, x1 + shift, lat1, kmAtBoundary, km1);
        }

        public void Finish(double km)
        {
            for (int area = 0; area < shapes.Length; area++)
                SetInside(area, false, km);
        }

        private void Piece(double x0, double y0, double x1, double y1, double km0, double km1)
        {
            if (km1 <= km0)
                return;

            // A piece that does not start where the last one ended has wrapped across the antimeridian, where
            // Natural Earth splits polygons into separate rings, so the carried state no longer applies.
            if (!(Math.Abs(x0 - _lastX) <= 1e-9))
                ForgetState();
            _lastX = x1;

            for (int area = 0; area < shapes.Length; area++)
            {
                var shape = shapes[area];
                if (!shape.BoundsOverlap(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1)))
                {
                    SetInside(area, false, km0);
                    _known[area] = true;
                    continue;
                }

                _crossings.Clear();
                shape.AddCrossings(x0, y0, x1, y1, _crossings);
                if (_crossings.Count == 0 && _known[area])
                    continue;

                // Between boundary crossings the state cannot change, so the midpoint of each stretch decides it.
                _crossings.Add(1.0);
                _crossings.Sort();
                double start = 0.0;
                foreach (double end in _crossings)
                {
                    if (end <= start)
                        continue;
                    double middle = (start + end) / 2.0;
                    bool inside = shape.Contains(x0 + ((x1 - x0) * middle), y0 + ((y1 - y0) * middle));
                    SetInside(area, inside, km0 + ((km1 - km0) * start));
                    start = end;
                }

                _known[area] = true;
            }
        }

        private void SetInside(int area, bool inside, double km)
        {
            if (_inside[area] == inside)
                return;
            if (inside)
                _enteredKm[area] = km;
            else if (km > _enteredKm[area])
                Intervals.Add(new AreaInterval(area, _enteredKm[area], km));
            _inside[area] = inside;
        }
    }

    /// <summary>Points along the great circle between two coordinates, by fraction of its length.</summary>
    private readonly struct GreatCircleArc
    {
        private readonly (double X, double Y, double Z) _from;
        private readonly (double X, double Y, double Z) _to;
        private readonly double _angle;

        public GreatCircleArc(Coordinate from, Coordinate to)
        {
            _from = ToVector(from);
            _to = ToVector(to);
            double dot = Math.Clamp((_from.X * _to.X) + (_from.Y * _to.Y) + (_from.Z * _to.Z), -1.0, 1.0);
            _angle = Math.Acos(dot);
        }

        public (double Longitude, double Latitude) PointAt(double fraction)
        {
            double sinAngle = Math.Sin(_angle);
            double a = sinAngle > 0 ? Math.Sin((1.0 - fraction) * _angle) / sinAngle : 1.0 - fraction;
            double b = sinAngle > 0 ? Math.Sin(fraction * _angle) / sinAngle : fraction;
            double x = (a * _from.X) + (b * _to.X);
            double y = (a * _from.Y) + (b * _to.Y);
            double z = (a * _from.Z) + (b * _to.Z);
            return (Math.Atan2(y, x) * 180.0 / Math.PI, Math.Atan2(z, Math.Sqrt((x * x) + (y * y))) * 180.0 / Math.PI);
        }

        private static (double X, double Y, double Z) ToVector(Coordinate coordinate)
        {
            double lon = coordinate.Longitude * Math.PI / 180.0;
            double lat = coordinate.Latitude * Math.PI / 180.0;
            return (Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
        }
    }

    /// <summary>One area's boundary segments, indexed by a grid of cells covering its bounding box.</summary>
    private sealed class AreaShape
    {
        private readonly double[] _x0;
        private readonly double[] _y0;
        private readonly double[] _x1;
        private readonly double[] _y1;
        private readonly double _minX;
        private readonly double _minY;
        private readonly double _maxX;
        private readonly double _maxY;
        private readonly double _originX;
        private readonly double _originY;
        private readonly int _columns;
        private readonly int _rows;
        private readonly int[] _cellOffsets;
        private readonly int[] _cellSegments;

        public AreaShape(IReadOnlyList<double[]> rings)
        {
            var x0 = new List<double>();
            var y0 = new List<double>();
            var x1 = new List<double>();
            var y1 = new List<double>();
            foreach (var ring in rings)
            {
                if (ring.Length < 8 || ring.Length % 2 != 0)
                    throw new ArgumentException("A sea-area ring needs at least four longitude, latitude pairs.", nameof(rings));
                int count = ring.Length / 2;
                for (int i = 0; i < count; i++)
                {
                    // Closing the ring explicitly makes open and GeoJSON-closed rings behave the same.
                    int j = (i + 1) % count;
                    if (ring[2 * i] == ring[2 * j] && ring[(2 * i) + 1] == ring[(2 * j) + 1])
                        continue;
                    x0.Add(ring[2 * i]);
                    y0.Add(ring[(2 * i) + 1]);
                    x1.Add(ring[2 * j]);
                    y1.Add(ring[(2 * j) + 1]);
                }
            }

            if (x0.Count == 0)
                throw new ArgumentException("A sea area needs at least one ring.", nameof(rings));

            _x0 = [.. x0];
            _y0 = [.. y0];
            _x1 = [.. x1];
            _y1 = [.. y1];
            _minX = Math.Min(_x0.Min(), _x1.Min());
            _minY = Math.Min(_y0.Min(), _y1.Min());
            _maxX = Math.Max(_x0.Max(), _x1.Max());
            _maxY = Math.Max(_y0.Max(), _y1.Max());
            _originX = Math.Floor(_minX / CellDegrees) * CellDegrees;
            _originY = Math.Floor(_minY / CellDegrees) * CellDegrees;
            _columns = (int)Math.Floor((_maxX - _originX) / CellDegrees) + 1;
            _rows = (int)Math.Floor((_maxY - _originY) / CellDegrees) + 1;

            var counts = new int[(_columns * _rows) + 1];
            ForEachSegmentCell((cell, _) => counts[cell + 1]++);
            for (int i = 1; i < counts.Length; i++)
                counts[i] += counts[i - 1];
            _cellOffsets = counts;
            _cellSegments = new int[counts[^1]];
            var positions = (int[])counts.Clone();
            ForEachSegmentCell((cell, segment) => _cellSegments[positions[cell]++] = segment);
        }

        public bool BoundsOverlap(double minX, double minY, double maxX, double maxY) =>
            maxX >= _minX && minX <= _maxX && maxY >= _minY && minY <= _maxY;

        /// <summary>Casts a ray east from the point and counts the boundary segments it crosses.</summary>
        public bool Contains(double x, double y)
        {
            if (x < _minX || x > _maxX || y < _minY || y > _maxY)
                return false;

            int row = Row(y);
            bool inside = false;
            for (int column = Column(x); column < _columns; column++)
            {
                int cell = (row * _columns) + column;
                for (int i = _cellOffsets[cell]; i < _cellOffsets[cell + 1]; i++)
                {
                    int s = _cellSegments[i];
                    double sy0 = _y0[s];
                    double sy1 = _y1[s];
                    if ((sy0 > y) == (sy1 > y))
                        continue;
                    double crossingX = _x0[s] + ((y - sy0) * (_x1[s] - _x0[s]) / (sy1 - sy0));
                    // A segment is listed in every cell it touches; count it only in the cell where the ray meets it.
                    if (crossingX > x && Column(crossingX) == column)
                        inside = !inside;
                }
            }

            return inside;
        }

        /// <summary>Adds the fractions along the line from (x0, y0) to (x1, y1) at which it meets the boundary.</summary>
        public void AddCrossings(double x0, double y0, double x1, double y1, List<double> crossings)
        {
            double dx = x1 - x0;
            double dy = y1 - y0;
            int firstColumn = Column(Math.Min(x0, x1));
            int lastColumn = Column(Math.Max(x0, x1));
            int firstRow = Row(Math.Min(y0, y1));
            int lastRow = Row(Math.Max(y0, y1));
            for (int row = firstRow; row <= lastRow; row++)
            {
                for (int column = firstColumn; column <= lastColumn; column++)
                {
                    int cell = (row * _columns) + column;
                    for (int i = _cellOffsets[cell]; i < _cellOffsets[cell + 1]; i++)
                    {
                        // Repeats from segments listed in several cells only add empty stretches, which are skipped.
                        int s = _cellSegments[i];
                        double ex = _x1[s] - _x0[s];
                        double ey = _y1[s] - _y0[s];
                        double denominator = (dx * ey) - (dy * ex);
                        if (denominator == 0.0)
                            continue;
                        double qx = _x0[s] - x0;
                        double qy = _y0[s] - y0;
                        double along = ((qx * ey) - (qy * ex)) / denominator;
                        double onSegment = ((qx * dy) - (qy * dx)) / denominator;
                        if (along is >= 0.0 and <= 1.0 && onSegment is >= 0.0 and <= 1.0)
                            crossings.Add(along);
                    }
                }
            }
        }

        private int Column(double x) => Math.Clamp((int)Math.Floor((x - _originX) / CellDegrees), 0, _columns - 1);

        private int Row(double y) => Math.Clamp((int)Math.Floor((y - _originY) / CellDegrees), 0, _rows - 1);

        private void ForEachSegmentCell(Action<int, int> visit)
        {
            for (int s = 0; s < _x0.Length; s++)
            {
                int firstColumn = Column(Math.Min(_x0[s], _x1[s]));
                int lastColumn = Column(Math.Max(_x0[s], _x1[s]));
                int firstRow = Row(Math.Min(_y0[s], _y1[s]));
                int lastRow = Row(Math.Max(_y0[s], _y1[s]));
                for (int row = firstRow; row <= lastRow; row++)
                {
                    for (int column = firstColumn; column <= lastColumn; column++)
                        visit((row * _columns) + column, s);
                }
            }
        }
    }
}
