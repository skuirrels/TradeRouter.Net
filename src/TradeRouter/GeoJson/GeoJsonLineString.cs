using System.Text.Json.Serialization;
using TradeRouter.Common;

namespace TradeRouter.GeoJson;

/// <summary>
/// GeoJSON LineString geometry.
/// </summary>
public sealed class GeoJsonLineString : GeoJsonGeometry
{
    /// <summary>Type of geometry: "LineString".</summary>
    [JsonPropertyName("type")]
    public override string Type => "LineString";

    /// <summary>List of coordinates as [longitude, latitude] arrays.</summary>
    [JsonPropertyName("coordinates")]
    public new List<double[]> Coordinates { get; init; } = [];

    /// <inheritdoc />
    public override IReadOnlyList<double[]> Positions => Coordinates;

    /// <summary>
    /// Creates a LineString from Coordinate objects.
    /// </summary>
    public static GeoJsonLineString FromCoordinates(IEnumerable<Coordinate> coords)
    {
        if (coords is IReadOnlyList<Coordinate> list)
        {
            var result = new List<double[]>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                result.Add([list[i].Longitude, list[i].Latitude]);
            }
            return new GeoJsonLineString { Coordinates = result };
        }

        var res = new List<double[]>();
        foreach (var c in coords)
        {
            res.Add([c.Longitude, c.Latitude]);
        }
        return new GeoJsonLineString { Coordinates = res };
    }
}
