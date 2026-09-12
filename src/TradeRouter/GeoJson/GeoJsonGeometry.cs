using System.Text.Json;
using System.Text.Json.Serialization;
using TradeRouter.Common;

namespace TradeRouter.GeoJson;

/// <summary>Base type for route geometries emitted as RFC 7946 LineString or MultiLineString values.</summary>
[JsonConverter(typeof(GeoJsonGeometryConverter))]
public abstract class GeoJsonGeometry
{
    /// <summary>GeoJSON geometry type.</summary>
    public abstract string Type { get; }

    /// <summary>All positions in traversal order, flattened across antimeridian-split segments.</summary>
    [JsonIgnore]
    public abstract IReadOnlyList<double[]> Positions { get; }

    /// <summary>
    /// All positions in traversal order. For a MultiLineString this is a flattened convenience view;
    /// use <see cref="GeoJsonMultiLineString.Coordinates"/> when segment boundaries are required.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<double[]> Coordinates => Positions;

    /// <summary>Creates an RFC 7946 geometry, splitting a route at the antimeridian when necessary.</summary>
    public static GeoJsonGeometry? FromCoordinates(IReadOnlyList<Coordinate> coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        if (coordinates.Count == 0)
            return null;
        if (coordinates.Count < 2)
            throw new ArgumentException("A route geometry requires at least two positions.", nameof(coordinates));

        var segments = new List<List<double[]>>();
        var current = new List<double[]> { ToPosition(coordinates[0]) };

        for (int i = 1; i < coordinates.Count; i++)
        {
            var previous = coordinates[i - 1];
            var next = coordinates[i];
            previous.Validate();
            next.Validate();

            var previousPosition = ToPosition(previous);
            var nextPosition = ToPosition(next);
            double longitudeDelta = nextPosition[0] - previousPosition[0];
            if (Math.Abs(Math.Abs(longitudeDelta) - 360.0) < 1e-12)
            {
                current.Add([previousPosition[0], nextPosition[1]]);
                continue;
            }

            if (Math.Abs(longitudeDelta) > 180.0)
            {
                bool eastbound = longitudeDelta < 0.0;
                double boundary = eastbound ? 180.0 : -180.0;
                double adjustedNextLongitude = nextPosition[0] + (eastbound ? 360.0 : -360.0);
                double ratio = (boundary - previousPosition[0]) / (adjustedNextLongitude - previousPosition[0]);
                double latitude = previous.Latitude + (ratio * (next.Latitude - previous.Latitude));

                if (!PositionsEqual(current[^1], boundary, latitude))
                    current.Add([boundary, latitude]);
                if (current.Count >= 2)
                    segments.Add(current);

                current = [[-boundary, latitude]];

                if (!PositionsEqual(current[^1], nextPosition[0], nextPosition[1]))
                    current.Add(nextPosition);
            }
            else
            {
                // Preserve the two positions required by GeoJSON for a zero-length two-point route,
                // but do not retain duplicate lane vertices in longer routes.
                if (!PositionsEqual(current[^1], nextPosition[0], nextPosition[1]) ||
                    (coordinates.Count == 2 && i == 1))
                {
                    current.Add(nextPosition);
                }
            }
        }

        if (current.Count >= 2)
            segments.Add(current);

        if (segments.Count == 0)
            throw new ArgumentException("A route geometry requires at least two distinct positions.", nameof(coordinates));

        return segments.Count == 1
            ? new GeoJsonLineString { Coordinates = segments[0] }
            : new GeoJsonMultiLineString { Coordinates = segments };
    }

    private static double[] ToPosition(Coordinate coordinate)
    {
        coordinate.Validate();
        double longitude = coordinate.WithNormalizedLongitude().Longitude;
        if (longitude == -180.0 && coordinate.Longitude > 0.0)
            longitude = 180.0;
        return [longitude, coordinate.Latitude];
    }

    private static bool PositionsEqual(double[] position, double longitude, double latitude) =>
        position[0] == longitude && position[1] == latitude;
}

internal sealed class GeoJsonGeometryConverter : JsonConverter<GeoJsonGeometry>
{
    public override GeoJsonGeometry? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        string type = root.GetProperty("type").GetString()
            ?? throw new JsonException("GeoJSON geometry type is missing.");
        var coordinates = root.GetProperty("coordinates");
        return type switch
        {
            "LineString" => new GeoJsonLineString { Coordinates = ReadLine(coordinates) },
            "MultiLineString" => new GeoJsonMultiLineString
            {
                Coordinates = coordinates.EnumerateArray().Select(ReadLine).ToList()
            },
            _ => throw new JsonException($"Unsupported route geometry type '{type}'.")
        };
    }

    public override void Write(Utf8JsonWriter writer, GeoJsonGeometry value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("type", value.Type);
        writer.WritePropertyName("coordinates");
        switch (value)
        {
            case GeoJsonLineString line:
                JsonSerializer.Serialize(writer, line.Coordinates, options);
                break;
            case GeoJsonMultiLineString multiLine:
                JsonSerializer.Serialize(writer, multiLine.Coordinates, options);
                break;
            default:
                throw new JsonException($"Unsupported route geometry type '{value.GetType().Name}'.");
        }
        writer.WriteEndObject();
    }

    private static List<double[]> ReadLine(JsonElement element) =>
        element.EnumerateArray()
            .Select(position => position.EnumerateArray().Select(value => value.GetDouble()).ToArray())
            .ToList();
}
