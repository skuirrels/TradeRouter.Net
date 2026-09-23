using TradeRouter.Common;
using TradeRouter.GeoJson;
using TradeRouter.Movements;
using TradeRouter.Passages;
using TradeRouter.Ports;

namespace TradeRouter;

/// <summary>
/// Static entrypoint for calculating maritime sea routes between any two points on Earth.
/// </summary>
public static class TradeRoutes
{
    private static ITradeRouterEngine? _customEngine;

    /// <summary>
    /// Gets or sets the active sea route engine. Defaults to <see cref="TradeRouterEngine.Default"/>.
    /// </summary>
    public static ITradeRouterEngine Engine
    {
        get => _customEngine ?? TradeRouterEngine.Default;
        set => _customEngine = value;
    }

    /// <summary>
    /// Resets the engine back to the default singleton instance.
    /// </summary>
    public static void ResetEngine() => _customEngine = null;

    /// <summary>
    /// Calculates the shortest sea route between two coordinates using default options.
    /// </summary>
    public static GeoJsonFeature Calculate(Coordinate origin, Coordinate destination)
    {
        return Engine.CalculateRoute(origin, destination, null);
    }

    /// <summary>
    /// Calculates the shortest sea route between two coordinates using the specified options.
    /// </summary>
    public static GeoJsonFeature Calculate(Coordinate origin, Coordinate destination, TradeRouterOptions options)
    {
        return Engine.CalculateRoute(origin, destination, options);
    }

    /// <summary>
    /// Calculates the shortest sea route between two coordinates specified as (lon, lat) tuples.
    /// </summary>
    public static GeoJsonFeature Calculate(
        (double Longitude, double Latitude) origin,
        (double Longitude, double Latitude) destination,
        TradeRouterOptions? options = null)
    {
        return Engine.CalculateRoute(
            new Coordinate(origin.Longitude, origin.Latitude),
            new Coordinate(destination.Longitude, destination.Latitude),
            options);
    }

    /// <summary>
    /// Calculates the shortest sea route between two coordinates specified as individual longitude/latitude doubles.
    /// </summary>
    public static GeoJsonFeature Calculate(
        double originLon,
        double originLat,
        double destLon,
        double destLat,
        TradeRouterOptions? options = null)
    {
        return Engine.CalculateRoute(new Coordinate(originLon, originLat), new Coordinate(destLon, destLat), options);
    }

    /// <summary>
    /// Calculates the shortest sea route between two ports identified by their UN/LOCODE or port code.
    /// </summary>
    public static GeoJsonFeature Calculate(
        string originPortCode,
        string destPortCode,
        TradeRouterOptions? options = null)
    {
        return Engine.CalculateRoute(originPortCode, destPortCode, options);
    }

    /// <summary>
    /// Calculates the shortest sea route with explicit parameters.
    /// </summary>
    public static GeoJsonFeature Calculate(
        Coordinate origin,
        Coordinate destination,
        DistanceUnit units = DistanceUnit.Km,
        double speedKnots = 16.0,
        bool appendOrigDest = false,
        IEnumerable<string>? restrictions = null,
        bool includePorts = false,
        PortParameters? portParams = null,
        bool returnPassages = false,
        string algorithm = "dijkstra",
        bool allowNorthwest = false)
    {
        var options = new TradeRouterOptions
        {
            Units = units,
            SpeedKnots = speedKnots,
            AppendOriginDestination = appendOrigDest,
            IncludePorts = includePorts,
            PortParameters = portParams,
            ReturnPassages = returnPassages,
            Algorithm = algorithm,
            AllowNorthwest = allowNorthwest
        };

        if (restrictions != null)
        {
            options.Restrictions = new HashSet<string>(restrictions, StringComparer.OrdinalIgnoreCase);
        }

        return Engine.CalculateRoute(origin, destination, options);
    }

    /// <summary>
    /// Routes a multi-leg movement described as one leg per line, for example
    /// <c>Pickup GBLGW to Port GBFXT Road</c>. Codes not in the port database need a coordinate in <paramref name="coordinates"/>.
    /// </summary>
    public static MovementResult CalculateMovement(
        string legsText,
        IReadOnlyDictionary<string, Coordinate>? coordinates = null,
        TradeRouterOptions? seaOptions = null,
        ILocationResolver? resolver = null,
        double? cargoTonnes = null,
        double? cargoTeu = null)
    {
        return CalculateMovement(
            MovementParser.Parse(legsText),
            coordinates,
            seaOptions,
            resolver,
            cargoTonnes,
            cargoTeu);
    }

    /// <summary>
    /// Routes a strongly typed, continuous movement plan.
    /// </summary>
    public static MovementResult CalculateMovement(
        MovementPlan plan,
        IReadOnlyDictionary<string, Coordinate>? coordinates = null,
        TradeRouterOptions? seaOptions = null,
        ILocationResolver? resolver = null,
        double? cargoTonnes = null,
        double? cargoTeu = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return CalculateMovement(plan.Legs, coordinates, seaOptions, resolver, cargoTonnes, cargoTeu);
    }

    /// <summary>
    /// Resolves a UN/LOCODE to a position from the embedded port list and UN/LOCODE list.
    /// </summary>
    public static ResolvedLocation Locate(string code) => Engine.Locate(code);

    /// <summary>
    /// Routes a multi-leg movement.
    /// </summary>
    public static MovementResult CalculateMovement(MovementRequest request)
    {
        return Engine.CalculateMovement(request);
    }

    /// <summary>Routes a multi-leg movement asynchronously, allowing a configured road-network provider to be called.</summary>
    public static ValueTask<MovementResult> CalculateMovementAsync(
        MovementRequest request,
        CancellationToken cancellationToken = default)
    {
        return Engine.CalculateMovementAsync(request, cancellationToken);
    }

    private static MovementResult CalculateMovement(
        IReadOnlyList<MovementLeg> legs,
        IReadOnlyDictionary<string, Coordinate>? coordinates,
        TradeRouterOptions? seaOptions,
        ILocationResolver? resolver,
        double? cargoTonnes,
        double? cargoTeu)
    {
        var request = new MovementRequest
        {
            Legs = [.. legs],
            CargoTonnes = cargoTonnes,
            CargoTeu = cargoTeu,
            SeaOptions = seaOptions,
            Resolver = resolver
        };
        if (coordinates != null)
        {
            foreach (var (code, coordinate) in coordinates)
                request.Coordinates[code] = coordinate;
        }
        return Engine.CalculateMovement(request);
    }

    /// <summary>
    /// Calculates sea routes returning multiple features if area matrices match multiple port combinations.
    /// </summary>
    public static IReadOnlyList<GeoJsonFeature> CalculateRoutes(
        Coordinate origin,
        Coordinate destination,
        TradeRouterOptions? options = null)
    {
        return Engine.CalculateRoutes(origin, destination, options);
    }
}
