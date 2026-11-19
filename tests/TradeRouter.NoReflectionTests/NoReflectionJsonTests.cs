using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TradeRouter.Caching;
using TradeRouter.Common;
using TradeRouter.GeoJson;
using TradeRouter.Movements;
using Xunit;

namespace TradeRouter.NoReflectionTests;

/// <summary>
/// Every JSON path in the libraries, exercised in a process where reflection-based System.Text.Json is off.
/// </summary>
public class NoReflectionJsonTests
{
    private static readonly RoadRouteRequest Request = new(
        1,
        new ResolvedLocation(null, null, new Coordinate(0, 0), null, "test"),
        new ResolvedLocation(null, null, new Coordinate(1, 1), null, "test"));

    [Fact]
    public void ReflectionIsOffInThisProcess()
    {
        JsonSerializer.IsReflectionEnabledByDefault.Should().BeFalse();
    }

    [Fact]
    public void MovementToJson_Works()
    {
        var result = TradeRoutes.CalculateMovement(
            """
            Pickup GBLGW to Port GBFXT Road
            Port GBFXT to Port CNSHG Sea
            Port CNSHG to Port USLAX Sea
            """,
            seaOptions: new TradeRouterOptions { ReturnPassages = true, ReturnSeaAreas = true },
            cargoTonnes: 12.0);

        using var document = JsonDocument.Parse(result.ToJson(writeIndented: true));

        document.RootElement.GetProperty("features").GetArrayLength().Should().Be(3);
        document.RootElement.GetProperty("features")[2].GetProperty("geometry").GetProperty("type").GetString()
            .Should().Be("MultiLineString");
        document.RootElement.GetProperty("properties").GetProperty("cargo_tonnes").GetDouble().Should().Be(12.0);
    }

    [Fact]
    public void FeatureToJson_Works()
    {
        var feature = TradeRoutes.Calculate(new Coordinate(121.8, 31.2), new Coordinate(4.0, 51.9),
            new TradeRouterOptions { IncludePorts = true });

        using var document = JsonDocument.Parse(feature.ToJson());

        document.RootElement.GetProperty("type").GetString().Should().Be("Feature");
        document.RootElement.GetProperty("properties").GetProperty("port_origin").GetProperty("port").GetString()
            .Should().NotBeNullOrEmpty();
        feature.ToString().Should().Be(feature.ToJson());
    }

    [Fact]
    public void ConsumerSourceGeneratedContext_WritesTheSameGeoJson()
    {
        // A consumer's own context lists only the GeoJSON types it returns, not the coordinate lists behind the
        // geometry converter; the converter must still write the geometry.
        var feature = TradeRoutes.Calculate(new Coordinate(121.8, 31.2), new Coordinate(-118.3, 33.7));

        string json = JsonSerializer.Serialize(feature, ConsumerJsonContext.Default.GeoJsonFeature);

        feature.Geometry.Should().BeOfType<GeoJsonMultiLineString>();
        json.Should().Be(feature.ToJson());
    }

    [Fact]
    public async Task GoogleRoutesProvider_SendsAndReadsJson()
    {
        string? body = null;
        var provider = new GoogleRoutesRoadRouteProvider(new HttpClient(new StubHandler(async (request, token) =>
        {
            body = await request.Content!.ReadAsStringAsync(token);
            return Json("""{"routes":[{"distanceMeters":157000,"duration":"7200s","polyline":{"geoJsonLinestring":{"type":"LineString","coordinates":[[0,0],[1,1]]}}}]}""");
        })), "test-key-not-real");

        var result = await provider.RouteAsync(Request);

        result.Status.Should().Be(RoadRouteStatus.Success);
        result.DistanceKm.Should().Be(157.0);
        result.Geometry.Should().Equal(new Coordinate(0, 0), new Coordinate(1, 1));
        using var sent = JsonDocument.Parse(body!);
        sent.RootElement.GetProperty("origin").GetProperty("location").GetProperty("latLng").GetProperty("latitude").GetDouble()
            .Should().Be(0);
    }

    [Fact]
    public async Task GoogleRoutesProvider_ReadsErrorJson()
    {
        var provider = new GoogleRoutesRoadRouteProvider(new HttpClient(new StubHandler((_, _) => Task.FromResult(
            Json("""{"error":{"code":403,"message":"Key rejected","status":"PERMISSION_DENIED"}}""", HttpStatusCode.Forbidden)))),
            "test-key-not-real");

        var result = await provider.RouteAsync(Request);

        result.Status.Should().Be(RoadRouteStatus.Unavailable);
        result.Message.Should().Contain("PERMISSION_DENIED: Key rejected");
    }

    [Fact]
    public async Task OsrmProvider_ReadsJson()
    {
        var provider = new OsrmRoadRouteProvider(new HttpClient(new StubHandler((_, _) => Task.FromResult(
            Json("""{"code":"Ok","routes":[{"distance":157000,"duration":7200,"geometry":{"type":"LineString","coordinates":[[0,0],[1,1]]}}]}""")))),
            new Uri("http://127.0.0.1:5000"));

        var result = await provider.RouteAsync(Request);

        result.Status.Should().Be(RoadRouteStatus.Success);
        result.DistanceKm.Should().Be(157.0);
    }

    [Fact]
    public async Task DistributedRoadRouteCache_RoundTrips()
    {
        var cache = new DistributedRoadRouteCache(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        var key = RoadRouteCacheKey.Create("test", Request, 5);
        var stored = new RoadRouteResult
        {
            Status = RoadRouteStatus.Success,
            DistanceKm = 157.0,
            DurationHours = 2.0,
            Geometry = [new Coordinate(0, 0), new Coordinate(1, 1)],
            Source = "osrm"
        };

        await cache.SetAsync(key, stored, TimeSpan.FromMinutes(5));
        var read = await cache.GetAsync(key);

        read!.DistanceKm.Should().Be(157.0);
        read.Geometry.Should().Equal(stored.Geometry);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GeoJsonFeature))]
internal sealed partial class ConsumerJsonContext : JsonSerializerContext;
