using TradeRouter.Common;
using TradeRouter.Data;
using TradeRouter.GeoJson;
using TradeRouter.Graph;
using TradeRouter.Locations;
using TradeRouter.Movements;
using TradeRouter.Passages;
using TradeRouter.Ports;

namespace TradeRouter;

/// <summary>
/// Default implementation of the <see cref="ITradeRouterEngine"/> maritime navigation engine.
/// Thread-safe and designed for high-concurrency routing.
/// </summary>
public sealed class TradeRouterEngine : ITradeRouterEngine
{
    private static readonly Lazy<TradeRouterEngine> LazyDefault = new(() => new TradeRouterEngine());

    /// <summary>
    /// Default global instance of <see cref="TradeRouterEngine"/> with embedded Marnet and Ports datasets.
    /// </summary>
    public static TradeRouterEngine Default => LazyDefault.Value;

    private readonly Lazy<MaritimeGraph> _lazyGraph;
    private readonly Lazy<PortDatabase> _lazyPorts;
    private readonly Lazy<UnLocodeDatabase> _lazyUnLocodes = new(EmbeddedResources.LoadUnLocodes, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <inheritdoc />
    public MaritimeGraph Graph => _lazyGraph.Value;

    /// <inheritdoc />
    public PortDatabase Ports => _lazyPorts.Value;

    /// <summary>
    /// Gets the embedded UN/LOCODE list, used to resolve movement locations that are not sea ports.
    /// </summary>
    public UnLocodeDatabase UnLocodes => _lazyUnLocodes.Value;

    /// <summary>
    /// Creates a new instance of <see cref="TradeRouterEngine"/> using default embedded datasets.
    /// </summary>
    public TradeRouterEngine()
        : this(EmbeddedResources.LoadMaritimeGraph, EmbeddedResources.LoadPortDatabase)
    {
    }

    /// <summary>
    /// Creates a new instance of <see cref="TradeRouterEngine"/> with custom graph and port instances.
    /// </summary>
    public TradeRouterEngine(MaritimeGraph graph, PortDatabase ports)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(ports);
        if (!graph.IsReadOnly)
            throw new ArgumentException("The custom graph must be finalized with BuildIndex() before it is used by an engine.", nameof(graph));
        _lazyGraph = new Lazy<MaritimeGraph>(() => graph);
        _lazyPorts = new Lazy<PortDatabase>(() => ports);
    }

    /// <summary>
    /// Creates a new instance of <see cref="TradeRouterEngine"/> with custom factory delegates.
    /// </summary>
    public TradeRouterEngine(Func<MaritimeGraph> graphFactory, Func<PortDatabase> portsFactory)
    {
        ArgumentNullException.ThrowIfNull(graphFactory);
        ArgumentNullException.ThrowIfNull(portsFactory);
        _lazyGraph = new Lazy<MaritimeGraph>(() => ValidateGraph(graphFactory()), LazyThreadSafetyMode.ExecutionAndPublication);
        _lazyPorts = new Lazy<PortDatabase>(() => portsFactory() ?? throw new InvalidOperationException("The port factory returned null."), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public GeoJsonFeature CalculateRoute(Coordinate origin, Coordinate destination, TradeRouterOptions? options = null)
    {
        var routes = CalculateRoutes(origin, destination, options);
        return routes[0];
    }

    /// <inheritdoc />
    public GeoJsonFeature CalculateRoute(double originLon, double originLat, double destLon, double destLat, TradeRouterOptions? options = null)
    {
        return CalculateRoute(new Coordinate(originLon, originLat), new Coordinate(destLon, destLat), options);
    }

    /// <inheritdoc />
    public GeoJsonFeature CalculateRoute(string originPortCode, string destPortCode, TradeRouterOptions? options = null)
    {
        var originPort = Ports.GetByCode(originPortCode)
            ?? throw new ArgumentException($"Port '{originPortCode}' not found in port database.", nameof(originPortCode));
        var destPort = Ports.GetByCode(destPortCode)
            ?? throw new ArgumentException($"Port '{destPortCode}' not found in port database.", nameof(destPortCode));

        var feature = CalculateRoute(originPort.Coordinate, destPort.Coordinate, options);
        feature.Properties.PortOrigin = originPort;
        feature.Properties.PortDest = destPort;
        return feature;
    }

    /// <inheritdoc />
    public IReadOnlyList<GeoJsonFeature> CalculateRoutes(Coordinate origin, Coordinate destination, TradeRouterOptions? options = null)
    {
        origin.Validate();
        destination.Validate();

        options = (options ?? new TradeRouterOptions()).Clone();
        options.Validate();
        var units = options.Units;
        double speedKnots = options.SpeedKnots;
        bool appendOrigDest = options.AppendOriginDestination;
        bool includePorts = options.IncludePorts;
        bool returnPassages = options.ReturnPassages;
        var restrictions = options.Restrictions;
        string algorithm = options.Algorithm;

        List<(Port? OriginPort, Port? DestPort)> portMatrix;
        if (includePorts)
        {
            var matrix = Ports.GetSelectedPortMatrix(origin, destination, options.PortParameters);
            if (matrix.Count == 0)
            {
                portMatrix = [(null, null)];
            }
            else
            {
                portMatrix = matrix.Select(m => ((Port?)m.OriginPort, (Port?)m.DestPort)).ToList();
            }
        }
        else
        {
            portMatrix = [(null, null)];
        }

        var results = new List<GeoJsonFeature>(portMatrix.Count);

        foreach (var (pFrom, pTo) in portMatrix)
        {
            var routedOrigin = pFrom != null ? pFrom.Coordinate : origin;
            var routedDest = pTo != null ? pTo.Coordinate : destination;

            var (lengthKm, shortestPath) = Graph.ShortestPath(routedOrigin, routedDest, restrictions, algorithm);

            List<Coordinate> routeCoords;
            if (shortestPath == null || double.IsInfinity(lengthKm) || shortestPath.Count == 0)
            {
                routeCoords = [];
            }
            else
            {
                // A one-node path means both endpoints snap to the same network node. Emit a straight line
                // between the routed endpoints so the LineString always has two positions and a real length.
                routeCoords = shortestPath.Count == 1
                    ? [routedOrigin, routedDest]
                    : shortestPath; // freshly allocated per query, safe to take ownership

                if (includePorts && routeCoords.Count > 0)
                {
                    if (routeCoords[0] != routedOrigin)
                        routeCoords.Insert(0, routedOrigin);
                    if (routeCoords[^1] != routedDest)
                        routeCoords.Add(routedDest);
                }

                if (appendOrigDest && routeCoords.Count > 0)
                {
                    if (routeCoords[0] != origin)
                        routeCoords.Insert(0, origin);
                    if (routeCoords[^1] != destination)
                        routeCoords.Add(destination);
                }
            }

            var (normalizedCoords, traversedPassages) = RouteNormalizer.ProcessRoute(routeCoords, Graph, returnPassages);

            double totalLength = normalizedCoords.Count > 1
                ? Haversine.CalculatePathLength(normalizedCoords, units)
                : 0.0;
            double duration = totalLength > 0 ? Haversine.CalculateDurationHours(speedKnots, totalLength, units) : 0.0;

            var feature = new GeoJsonFeature
            {
                Geometry = GeoJsonGeometry.FromCoordinates(normalizedCoords),
                Properties = new TradeRouterProperties
                {
                    Length = totalLength,
                    Units = units.ToUnitString(),
                    DurationHours = duration,
                    PortOrigin = includePorts && pFrom != null ? pFrom : null,
                    PortDest = includePorts && pTo != null ? pTo : null,
                    TraversedPassages = returnPassages ? Passage.FilterValidPassages(traversedPassages) : null
                }
            };

            results.Add(feature);
        }

        return results;
    }

    /// <inheritdoc />
    public MovementResult CalculateMovement(MovementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMovement(request);

        // Movement legs always include their resolved endpoints so consecutive legs join end to end.
        var seaOptions = (request.SeaOptions ?? new TradeRouterOptions()).Clone();
        seaOptions.AppendOriginDestination = true;
        var units = seaOptions.Units;

        var resolvedByCode = new Dictionary<string, ResolvedLocation>(StringComparer.OrdinalIgnoreCase);
        var legResults = new List<LegResult>(request.Legs.Count);

        for (int i = 0; i < request.Legs.Count; i++)
        {
            var leg = request.Legs[i];
            int sequence = i + 1;
            var from = ResolveLocation(leg.From, request, resolvedByCode);
            var to = ResolveLocation(leg.To, request, resolvedByCode);
            ValidateWaypoint(sequence, "from", leg.FromKind, leg.Mode, from);
            ValidateWaypoint(sequence, "to", leg.ToKind, leg.Mode, to);

            if (i > 0)
            {
                var prior = legResults[^1].To;
                if (Haversine.DistanceKm(prior.Coordinate, from.Coordinate) > 0.001)
                {
                    throw new ArgumentException(
                        $"Leg {sequence} starts at {from.Label}, but leg {sequence - 1} ends at {prior.Label}. " +
                        "Movement legs must form one continuous chain.", nameof(request));
                }
            }

            var feature = leg.Mode == TransportMode.Sea
                ? CalculateSeaLeg(sequence, leg, from, to, seaOptions)
                : CalculateStraightLeg(from, to, leg.Mode, units, request.SpeedsKmh);

            feature.Properties.Leg = sequence;
            feature.Properties.Mode = leg.Mode.ToWireString();
            feature.Properties.Kind = leg.Kind.ToWireString();
            feature.Properties.From = from.Label;
            feature.Properties.To = to.Label;
            bool isSea = leg.Mode == TransportMode.Sea;
            bool followsSeaLeg = isSea && sequence > 1 && request.Legs[sequence - 2].Mode == TransportMode.Sea;
            feature.Properties.PortHours = isSea ? 2.0 * request.PortDwellHours : 0.0;
            if (isSea)
            {
                var (corridor, fraction) = request.SeaOperationalAllowance is { } fixedFraction
                    ? (SeaServiceAllowance.OverrideCorridor, fixedFraction)
                    : SeaServiceAllowance.Resolve(feature.Properties.TraversedPassages, from.Coordinate, to.Coordinate);
                feature.Properties.OperationalAllowanceFraction = fraction;
                feature.Properties.OperationalAllowanceCorridor = corridor;
                feature.Properties.OperationalAllowanceHours = feature.Properties.DurationHours * fraction;
                if (!seaOptions.ReturnPassages)
                {
                    feature.Properties.TraversedPassages = null;
                }
            }
            else
            {
                feature.Properties.OperationalAllowanceHours = 0.0;
            }
            feature.Properties.ConnectionHours = followsSeaLeg ? request.TransshipmentConnectionHours : 0.0;
            feature.Properties.TransitHours = feature.Properties.DurationHours
                + feature.Properties.PortHours
                + feature.Properties.OperationalAllowanceHours
                + feature.Properties.ConnectionHours;
            ApplyEmissions(feature, leg.Mode, units, request);

            legResults.Add(new LegResult(sequence, leg, from, to, feature));
        }

        return new MovementResult(legResults, units.ToUnitString(), request.CargoTonnes, request.CargoTeu);
    }

    /// <summary>
    /// Stamps the leg with its GLEC-style CO2e figures: intensity in g per tonne-km, kg per tonne of cargo for
    /// the leg, and absolute kg when the request states a cargo weight. Length is converted to kilometres first.
    /// </summary>
    private static void ApplyEmissions(GeoJsonFeature feature, TransportMode mode, DistanceUnit units, MovementRequest request)
    {
        var factors = request.Emissions;
        double lengthKm = feature.Properties.Length / (units.GetConversionFactorFromMeters() * 1000.0);
        double gramsPerTonneKm = factors.GramsPerTonneKm(mode, lengthKm);
        double kgPerTonne = gramsPerTonneKm * lengthKm / 1000.0;

        feature.Properties.Co2eGramsPerTonneKm = gramsPerTonneKm;
        feature.Properties.Co2eKgPerTonne = kgPerTonne;

        if (mode == TransportMode.Sea && request.CargoTeu.HasValue)
        {
            // Sea legs are charged per container when a TEU count is known: a light box still moves a whole slot.
            feature.Properties.Co2eGramsPerTeuKm = factors.SeaGramsPerTeuKm;
            feature.Properties.Co2eKg = factors.SeaGramsPerTeuKm * request.CargoTeu.Value * lengthKm / 1000.0;
            feature.Properties.Co2eBasis = "teu";
            return;
        }

        if (request.CargoTonnes.HasValue)
        {
            feature.Properties.Co2eKg = kgPerTonne * request.CargoTonnes.Value;
            feature.Properties.Co2eBasis = "tonnes";
        }
        else if (request.CargoTeu.HasValue)
        {
            feature.Properties.Co2eKg = kgPerTonne * request.CargoTeu.Value * factors.AverageTonnesPerTeu;
            feature.Properties.Co2eBasis = "teu_average_weight";
        }
    }

    private GeoJsonFeature CalculateSeaLeg(int sequence, MovementLeg leg, ResolvedLocation from, ResolvedLocation to, TradeRouterOptions options)
    {
        // Passages are always collected for a movement leg: the corridor allowance is chosen from them.
        // CalculateMovement drops them again when the caller did not ask for ReturnPassages.
        var routingOptions = options.ReturnPassages ? options : options.Clone();
        routingOptions.ReturnPassages = true;
        var feature = CalculateRoute(from.Coordinate, to.Coordinate, routingOptions);

        if (feature.Geometry is null || feature.Geometry.Positions.Count < 2)
        {
            throw new InvalidOperationException(
                $"Leg {sequence} ({leg}) has no sea route between {from.Label} and {to.Label} under the current passage restrictions.");
        }

        feature.Properties.PortOrigin ??= from.Port;
        feature.Properties.PortDest ??= to.Port;
        return feature;
    }

    private static GeoJsonFeature CalculateStraightLeg(
        ResolvedLocation from,
        ResolvedLocation to,
        TransportMode mode,
        DistanceUnit units,
        IReadOnlyDictionary<TransportMode, double> speedsKmh)
    {
        if (!speedsKmh.TryGetValue(mode, out double speedKmh) || speedKmh <= 0)
            throw new ArgumentException($"No positive speed configured for mode {mode}. Set MovementRequest.SpeedsKmh[{mode}].");

        var coords = RouteNormalizer.NormalizeRoute([from.Coordinate, to.Coordinate]);
        double length = Haversine.CalculatePathLength(coords, units);

        // Reuse the sea-leg duration helper by expressing the road speed in knots.
        double speedKnots = speedKmh / DistanceUnit.Km.GetSpeedCoefficient();

        return new GeoJsonFeature
        {
            Geometry = GeoJsonGeometry.FromCoordinates(coords),
            Properties = new TradeRouterProperties
            {
                Length = length,
                Units = units.ToUnitString(),
                DurationHours = Haversine.CalculateDurationHours(speedKnots, length, units),
                PortOrigin = from.Port,
                PortDest = to.Port
            }
        };
    }

    private ResolvedLocation ResolveLocation(
        Location location,
        MovementRequest request,
        Dictionary<string, ResolvedLocation> resolvedByCode)
    {
        bool cacheable = location.Code != null && !location.Coordinate.HasValue;
        if (cacheable && resolvedByCode.TryGetValue(location.Code!, out var cached))
            return cached;

        var resolved = ResolveUncached(location, request);
        resolved.Coordinate.Validate();

        if (cacheable)
            resolvedByCode[location.Code!] = resolved;

        return resolved;
    }

    /// <summary>
    /// Resolution order: an explicit coordinate on the location; <see cref="MovementRequest.Coordinates"/>;
    /// the embedded port list when UN/LOCODE agrees it is the same place; the UN/LOCODE list when it carries
    /// coordinates; the port list otherwise; then <see cref="MovementRequest.Resolver"/>.
    /// </summary>
    private ResolvedLocation ResolveUncached(Location location, MovementRequest request)
    {
        if (location.Coordinate.HasValue)
        {
            Port? knownPort = location.Code != null ? Ports.GetByCode(location.Code, location.Coordinate.Value) : null;
            UnLocode? knownLocation = location.Code != null ? UnLocodes.GetByCode(location.Code) : null;
            var functions = knownLocation?.Functions ?? (knownPort is null ? null : LocationFunctions.SeaPort);
            return new ResolvedLocation(location.Code, location.Name ?? knownLocation?.Name ?? knownPort?.Name, location.Coordinate.Value, null, "coordinates", functions);
        }

        string code = location.Code!;

        // A caller-supplied coordinate wins outright and no dataset record is attached, because the
        // stored position may differ from the override.
        if (request.Coordinates.TryGetValue(code, out var overridden))
        {
            UnLocode? knownLocation = UnLocodes.GetByCode(code);
            Port? knownPort = Ports.GetByCode(code, overridden);
            var functions = knownLocation?.Functions ?? (knownPort is null ? null : LocationFunctions.SeaPort);
            return new ResolvedLocation(code, knownLocation?.Name ?? knownPort?.Name, overridden, null, "coordinates", functions);
        }

        return ResolveCode(code, request.Resolver);
    }

    /// <inheritdoc />
    public ResolvedLocation Locate(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return ResolveCode(code.Trim().ToUpperInvariant(), null);
    }

    private ResolvedLocation ResolveCode(string code, ILocationResolver? resolver)
    {
        UnLocode? unLocode = UnLocodes.GetByCode(code);
        Port? port = unLocode?.Coordinate is { } knownPosition
            ? Ports.GetByCode(code, knownPosition)
            : Ports.GetByCode(code);

        // The port list has positions tuned to the lane network, so it wins when UN/LOCODE agrees on the name.
        // When the names disagree (the port list's CNSHG is Sanshan, UN/LOCODE's is Shanghai Pt) UN/LOCODE wins.
        if (port != null && (unLocode == null || NamesAgree(port.Name, unLocode.Name)))
            return new ResolvedLocation(code, port.Name, port.Coordinate, port, "ports", unLocode?.Functions ?? LocationFunctions.SeaPort);

        if (unLocode?.Coordinate is { } position)
            return new ResolvedLocation(code, unLocode.Name, position, null, "unlocode", unLocode.Functions);

        if (port != null)
            return new ResolvedLocation(code, port.Name, port.Coordinate, port, "ports", unLocode?.Functions ?? LocationFunctions.SeaPort);

        if (resolver != null && resolver.TryResolve(code, out var fromResolver, out var name))
            return new ResolvedLocation(code, name, fromResolver, null, "resolver");

        string known = unLocode != null
            ? $"UN/LOCODE knows '{code}' as {unLocode.Name} ({DescribeFunctions(unLocode.Functions)}) but publishes no coordinates for it."
            : $"Location '{code}' is in neither the embedded port list nor the UN/LOCODE list.";
        throw new ArgumentException($"{known} Add its coordinate to MovementRequest.Coordinates or supply an ILocationResolver.");
    }

    private static bool NamesAgree(string portName, string unLocodeName)
    {
        string a = NormaliseName(portName);
        string b = NormaliseName(unLocodeName);
        if (a.Length < 4 || b.Length < 4)
            return a == b;
        return a == b || a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal);
    }

    private static string NormaliseName(string name)
    {
        var chars = new char[name.Length];
        int n = 0;
        foreach (char c in name.Normalize(System.Text.NormalizationForm.FormD))
        {
            if (char.IsLetter(c))
                chars[n++] = char.ToLowerInvariant(c);
        }
        return new string(chars, 0, n);
    }

    private static string DescribeFunctions(LocationFunctions functions)
    {
        if (functions == LocationFunctions.None)
            return "no recorded function";
        var parts = new List<string>(4);
        if ((functions & LocationFunctions.SeaPort) != 0) parts.Add("sea port");
        if ((functions & LocationFunctions.Airport) != 0) parts.Add("airport");
        if ((functions & LocationFunctions.RailTerminal) != 0) parts.Add("rail terminal");
        if ((functions & LocationFunctions.RoadTerminal) != 0) parts.Add("road terminal");
        if ((functions & LocationFunctions.Multimodal) != 0) parts.Add("multimodal");
        if (parts.Count == 0) parts.Add(functions.ToString().ToLowerInvariant());
        return string.Join(", ", parts);
    }

    private static MaritimeGraph ValidateGraph(MaritimeGraph? graph)
    {
        if (graph is null)
            throw new InvalidOperationException("The graph factory returned null.");
        if (!graph.IsReadOnly)
            throw new InvalidOperationException("The graph factory must return a graph finalized with BuildIndex().");
        return graph;
    }

    private static void ValidateMovement(MovementRequest request)
    {
        if (request.Legs is null || request.Legs.Count == 0)
            throw new ArgumentException("A movement needs at least one leg.", nameof(request));
        if (request.Emissions is null)
            throw new ArgumentException("Movement emissions cannot be null.", nameof(request));
        request.Emissions.Validate();
        ValidatePositiveOptional(request.CargoTonnes, nameof(request.CargoTonnes));
        ValidatePositiveOptional(request.CargoTeu, nameof(request.CargoTeu));
        if (!double.IsFinite(request.PortDwellHours) || request.PortDwellHours < 0)
            throw new ArgumentOutOfRangeException(nameof(request.PortDwellHours), request.PortDwellHours, "Port dwell must be finite and non-negative.");
        if (request.SeaOperationalAllowance is { } allowance && (!double.IsFinite(allowance) || allowance < 0))
            throw new ArgumentOutOfRangeException(nameof(request.SeaOperationalAllowance), allowance, "Sea operational allowance must be finite and non-negative.");
        if (!double.IsFinite(request.TransshipmentConnectionHours) || request.TransshipmentConnectionHours < 0)
            throw new ArgumentOutOfRangeException(nameof(request.TransshipmentConnectionHours), request.TransshipmentConnectionHours, "Transshipment connection time must be finite and non-negative.");
        if (request.SpeedsKmh.ContainsKey(TransportMode.Sea))
            throw new ArgumentException("Sea speed is taken from SeaOptions.SpeedKnots; remove the Sea entry from SpeedsKmh.", nameof(request));

        foreach (var (mode, speed) in request.SpeedsKmh)
        {
            if (!Enum.IsDefined(mode) || mode == TransportMode.Sea)
                throw new ArgumentException($"SpeedsKmh contains unsupported mode '{mode}'.", nameof(request));
            if (!double.IsFinite(speed) || speed <= 0)
                throw new ArgumentOutOfRangeException(nameof(request), speed, $"Speed for {mode} must be finite and positive.");
        }

        (request.SeaOptions ?? new TradeRouterOptions()).Validate();
        for (int i = 0; i < request.Legs.Count; i++)
        {
            var leg = request.Legs[i] ?? throw new ArgumentException($"Movement leg {i + 1} is null.", nameof(request));
            if (!Enum.IsDefined(leg.Mode))
                throw new ArgumentException($"Movement leg {i + 1} has an unknown transport mode.", nameof(request));
            if (!Enum.IsDefined(leg.Kind) || !Enum.IsDefined(leg.FromKind) || !Enum.IsDefined(leg.ToKind))
                throw new ArgumentException($"Movement leg {i + 1} has an unknown leg or waypoint kind.", nameof(request));
            if (request.Legs.Count > 1 && leg.Kind == LegKind.Pickup && i != 0)
                throw new ArgumentException($"Pickup leg {i + 1} must be the first leg.", nameof(request));
            if (request.Legs.Count > 1 && leg.Kind == LegKind.Delivery && i != request.Legs.Count - 1)
                throw new ArgumentException($"Delivery leg {i + 1} must be the last leg.", nameof(request));
        }

        foreach (var (code, coordinate) in request.Coordinates)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("Movement coordinate keys cannot be blank.", nameof(request));
            coordinate.Validate();
        }
    }

    private static void ValidatePositiveOptional(double? value, string name)
    {
        if (value.HasValue && (!double.IsFinite(value.Value) || value.Value <= 0))
            throw new ArgumentOutOfRangeException(name, value, "Cargo amount must be finite and positive when supplied.");
    }

    private static void ValidateWaypoint(
        int sequence,
        string endpoint,
        WaypointKind waypointKind,
        TransportMode mode,
        ResolvedLocation location)
    {
        if (!location.Functions.HasValue)
            return;

        LocationFunctions functions = location.Functions.Value;
        LocationFunctions requiredByKind = waypointKind switch
        {
            WaypointKind.Unspecified or WaypointKind.Place => LocationFunctions.None,
            WaypointKind.Port => LocationFunctions.SeaPort,
            WaypointKind.Airport => LocationFunctions.Airport,
            WaypointKind.Station => LocationFunctions.RailTerminal,
            WaypointKind.Terminal => LocationFunctions.SeaPort | LocationFunctions.RailTerminal | LocationFunctions.RoadTerminal | LocationFunctions.Multimodal,
            WaypointKind.Depot => LocationFunctions.RoadTerminal | LocationFunctions.RailTerminal | LocationFunctions.Multimodal,
            _ => throw new ArgumentOutOfRangeException(nameof(waypointKind), waypointKind, "Unknown waypoint kind.")
        };

        if (requiredByKind != LocationFunctions.None && (functions & requiredByKind) == 0)
        {
            throw new ArgumentException(
                $"Leg {sequence} {endpoint} location {location.Label} is declared as {waypointKind}, " +
                $"but UN/LOCODE records {DescribeFunctions(functions)}.");
        }

        LocationFunctions requiredByMode = mode switch
        {
            TransportMode.Sea => LocationFunctions.SeaPort,
            TransportMode.Air => LocationFunctions.Airport,
            TransportMode.Rail => LocationFunctions.RailTerminal,
            TransportMode.Road => LocationFunctions.None,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown transport mode.")
        };
        if (requiredByMode != LocationFunctions.None && (functions & requiredByMode) == 0)
        {
            throw new ArgumentException(
                $"Leg {sequence} uses {mode}, but its {endpoint} location {location.Label} is recorded as " +
                $"{DescribeFunctions(functions)} rather than a compatible {mode.ToString().ToLowerInvariant()} terminal.");
        }
    }
}
