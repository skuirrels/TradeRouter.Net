using TradeRouter.Common;
using TradeRouter.GeoJson;
using TradeRouter.Graph;
using TradeRouter.Movements;
using TradeRouter.Ports;

namespace TradeRouter;

/// <summary>
/// Service interface for maritime sea route calculation and network querying.
/// </summary>
public interface ITradeRouterEngine
{
    /// <summary>
    /// Gets the underlying maritime navigation graph.
    /// </summary>
    MaritimeGraph Graph { get; }

    /// <summary>
    /// Gets the port database indexed spatially.
    /// </summary>
    PortDatabase Ports { get; }

    /// <summary>
    /// Calculates the shortest maritime route between origin and destination coordinates.
    /// </summary>
    GeoJsonFeature CalculateRoute(Coordinate origin, Coordinate destination, TradeRouterOptions? options = null);

    /// <summary>
    /// Calculates maritime route(s) between origin and destination coordinates.
    /// When area-based port matrices match multiple ports, multiple features are returned.
    /// </summary>
    IReadOnlyList<GeoJsonFeature> CalculateRoutes(Coordinate origin, Coordinate destination, TradeRouterOptions? options = null);

    /// <summary>
    /// Calculates the shortest maritime route between origin and destination coordinates specified as lon/lat.
    /// </summary>
    GeoJsonFeature CalculateRoute(double originLon, double originLat, double destLon, double destLat, TradeRouterOptions? options = null);

    /// <summary>
    /// Calculates the shortest maritime route between two port codes (e.g. UN/LOCODE like "FRLEH", "CNTSN").
    /// </summary>
    GeoJsonFeature CalculateRoute(string originPortCode, string destPortCode, TradeRouterOptions? options = null);

    /// <summary>
    /// Routes a multi-leg movement synchronously. Sea legs use the maritime network; road legs use supplied routes or
    /// the configured estimator; rail and air legs are straight great-circle lines.
    /// Implementations that predate movements keep compiling and throw <see cref="NotSupportedException"/>.
    /// </summary>
    MovementResult CalculateMovement(MovementRequest request)
        => throw new NotSupportedException($"{GetType().Name} does not support multi-leg movements.");

    /// <summary>
    /// Routes a multi-leg movement asynchronously, allowing a configured road-network provider to be called.
    /// Implementations that predate asynchronous movements keep compiling and throw <see cref="NotSupportedException"/>.
    /// </summary>
    ValueTask<MovementResult> CalculateMovementAsync(MovementRequest request, CancellationToken cancellationToken = default)
        => ValueTask.FromException<MovementResult>(new NotSupportedException($"{GetType().Name} does not support asynchronous multi-leg movements."));

    /// <summary>
    /// Resolves a UN/LOCODE to a position using the embedded port list and UN/LOCODE list, the same way movement
    /// legs are resolved. Throws <see cref="ArgumentException"/> when neither list can place the code.
    /// </summary>
    ResolvedLocation Locate(string code)
        => throw new NotSupportedException($"{GetType().Name} does not support code lookup.");
}
