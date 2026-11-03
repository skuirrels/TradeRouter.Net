using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using TradeRouter.Common;
using TradeRouter.Movements;

namespace TradeRouter.Caching;

/// <summary>
/// Stores road-route results in any <see cref="IDistributedCache"/>, such as Redis
/// (<c>Microsoft.Extensions.Caching.StackExchangeRedis</c>), SQL Server (<c>Microsoft.Extensions.Caching.SqlServer</c>),
/// PostgreSQL or the in-memory <c>MemoryDistributedCache</c>. Each result is one JSON entry with an absolute expiry,
/// so the store removes it when its time to live has passed.
/// </summary>
public sealed class DistributedRoadRouteCache : IRoadRouteCache
{
    /// <summary>Prefix added to every key unless another is given.</summary>
    public const string DefaultKeyPrefix = "traderouter:road-route:";

    private const int PayloadVersion = 1;

    private readonly IDistributedCache _cache;
    private readonly string _keyPrefix;

    /// <summary>Creates a store over a distributed cache.</summary>
    /// <param name="cache">The distributed cache, normally registered by the application.</param>
    /// <param name="keyPrefix">
    /// Prefix added to every key, so road routes do not collide with other entries in a shared cache. It may be empty.
    /// </param>
    public DistributedRoadRouteCache(IDistributedCache cache, string keyPrefix = DefaultKeyPrefix)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(keyPrefix);
        _cache = cache;
        _keyPrefix = keyPrefix;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidDataException">The stored entry is not a road-route result.</exception>
    public async ValueTask<RoadRouteResult?> GetAsync(RoadRouteCacheKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        byte[]? payload = await _cache.GetAsync(_keyPrefix + key.Value, cancellationToken).ConfigureAwait(false);
        if (payload is null)
            return null;

        CachedRoadRoute? entry;
        try
        {
            entry = JsonSerializer.Deserialize(payload, CachingJsonContext.Default.CachedRoadRoute);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The cached road route for '{key.Value}' is not valid JSON.", ex);
        }

        // An entry written by a later payload version is treated as absent and overwritten.
        if (entry is { Version: > PayloadVersion })
            return null;
        if (entry is not { Version: PayloadVersion })
            throw new InvalidDataException($"The cached road route for '{key.Value}' has no recognised payload version.");

        return ToResult(entry, key);
    }

    /// <inheritdoc />
    public async ValueTask SetAsync(
        RoadRouteCacheKey key,
        RoadRouteResult result,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(result);
        if (timeToLive <= TimeSpan.Zero || timeToLive > RoadRouteCacheOptions.MaximumTimeToLive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeToLive),
                timeToLive,
                $"A cache time to live must be positive and at most {RoadRouteCacheOptions.MaximumTimeToLive.TotalDays:N0} days.");
        }

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(FromResult(result), CachingJsonContext.Default.CachedRoadRoute);
        await _cache.SetAsync(
            _keyPrefix + key.Value,
            payload,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = timeToLive },
            cancellationToken).ConfigureAwait(false);
    }

    private static CachedRoadRoute FromResult(RoadRouteResult result)
    {
        double[]? geometry = null;
        if (result.Geometry is { } points)
        {
            geometry = new double[points.Count * 2];
            for (int i = 0; i < points.Count; i++)
            {
                geometry[2 * i] = points[i].Longitude;
                geometry[2 * i + 1] = points[i].Latitude;
            }
        }

        return new CachedRoadRoute
        {
            Version = PayloadVersion,
            Status = result.Status,
            DistanceKm = result.DistanceKm,
            DurationHours = result.DurationHours,
            Geometry = geometry,
            Source = result.Source,
            Profile = result.Profile,
            DataVersion = result.DataVersion,
            Message = result.Message
        };
    }

    private static RoadRouteResult ToResult(CachedRoadRoute entry, RoadRouteCacheKey key)
    {
        if (!Enum.IsDefined(entry.Status) || string.IsNullOrEmpty(entry.Source) || entry.Geometry is { Length: var length } && length % 2 != 0)
            throw new InvalidDataException($"The cached road route for '{key.Value}' is incomplete.");

        Coordinate[]? geometry = null;
        if (entry.Geometry is { } flat)
        {
            geometry = new Coordinate[flat.Length / 2];
            for (int i = 0; i < geometry.Length; i++)
                geometry[i] = new Coordinate(flat[2 * i], flat[2 * i + 1]);
        }

        return new RoadRouteResult
        {
            Status = entry.Status,
            DistanceKm = entry.DistanceKm,
            DurationHours = entry.DurationHours,
            Geometry = geometry,
            Source = entry.Source,
            Profile = entry.Profile,
            DataVersion = entry.DataVersion,
            Message = entry.Message
        };
    }
}

/// <summary>Stored form of a road-route result. Geometry is flattened to longitude, latitude pairs.</summary>
internal sealed class CachedRoadRoute
{
    [JsonPropertyName("v")]
    public int Version { get; init; }

    [JsonPropertyName("status")]
    public RoadRouteStatus Status { get; init; }

    [JsonPropertyName("distanceKm")]
    public double? DistanceKm { get; init; }

    [JsonPropertyName("durationHours")]
    public double? DurationHours { get; init; }

    [JsonPropertyName("geometry")]
    public double[]? Geometry { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("profile")]
    public string? Profile { get; init; }

    [JsonPropertyName("dataVersion")]
    public string? DataVersion { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(CachedRoadRoute))]
internal sealed partial class CachingJsonContext : JsonSerializerContext;
