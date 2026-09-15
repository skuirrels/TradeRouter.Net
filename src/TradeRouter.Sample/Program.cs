using System.Diagnostics;
using TradeRouter;
using TradeRouter.Common;
using TradeRouter.Movements;
using TradeRouter.Passages;
using TradeRouter.Ports;

// TradeRouter.Net sample: exercises each public entry point and prints the results.
// Run with:  dotnet run --project src/TradeRouter.Sample
// Pass --geojson to print the full GeoJSON feature for the first route.

bool printGeoJson = args.Contains("--geojson", StringComparer.OrdinalIgnoreCase);

Console.WriteLine("TradeRouter.Net sample");
Console.WriteLine(new string('=', 60));

// 1. Coordinates in, GeoJSON feature out. This is how to route a place by position: pass its longitude and
//    latitude yourself, for a location the embedded data does not know or when you hold exact positions.
//    Marseille and Cape Town are FRMRS and ZACPT in the data; this pair is kept as coordinates because it is
//    the reference route the routing tests are measured against. Every other example names places by code.
var stopwatch = Stopwatch.StartNew();
var marseille = new Coordinate(5.333333, 43.333333);
var capeTown = new Coordinate(18.366667, -33.916667);
var route = TradeRoutes.Calculate(marseille, capeTown, appendOrigDest: true);
stopwatch.Stop();

Print("1. Marseille to Cape Town, given as coordinates",
    $"{route.Properties.Length:N1} {route.Properties.Units}, " +
    $"{route.Properties.DurationHours:N1} h at 16 kn, " +
    $"{route.Geometry?.Positions.Count ?? 0} points, cold start {stopwatch.ElapsedMilliseconds} ms");

// 2. Passage restrictions: Jebel Ali to St John's, Antigua, avoiding Suez, so the route goes round the Cape.
var viaCape = TradeRoutes.Calculate(At("AEJEA"), At("AGSJO"), restrictions: [Passage.Suez], returnPassages: true);
Print("2. AEJEA Jebel Ali to AGSJO St John's avoiding Suez",
    $"{viaCape.Properties.Length:N0} km via {PassageNames(viaCape.Properties.TraversedPassages)}");

// 3. Port codes (UN/LOCODE) straight into the port-to-port overload.
var portToPort = TradeRoutes.Calculate("FRLEH", "CNTSN");
Print("3. FRLEH to CNTSN by port code",
    $"{portToPort.Properties.PortOrigin!.Name} to {portToPort.Properties.PortDest!.Name}, {portToPort.Properties.Length:N0} km");

// 4. Inland places resolved to the nearest container terminals.
var viaPorts = TradeRoutes.Calculate(
    At("FRPAR"), At("JPTYO"),
    includePorts: true,
    appendOrigDest: true,
    portParams: new PortParameters { OnlyTerminals = true });
Print("4. FRPAR Paris to JPTYO Tokyo via nearest terminals",
    $"{viaPorts.Properties.PortOrigin?.PortCode} ({viaPorts.Properties.PortOrigin?.Name}) to " +
    $"{viaPorts.Properties.PortDest?.PortCode} ({viaPorts.Properties.PortDest?.Name}), {viaPorts.Properties.Length:N0} km");

// 5. Alternative algorithm and units.
var transPacific = TradeRoutes.Calculate(At("JPYOK"), At("USLAX"), units: DistanceUnit.NauticalMiles, algorithm: "astar");
Print("5. JPYOK Yokohama to USLAX Los Angeles, A*, nautical miles",
    $"{transPacific.Properties.Length:N0} {transPacific.Properties.Units}, {transPacific.Geometry?.Type} split at the antimeridian");

// 6. Unreachable when every passage is closed: null geometry, zero length.
var blocked = TradeRoutes.Calculate(At("SGSIN"), At("GRPIR"), restrictions: [Passage.Suez, Passage.Gibraltar]);
Print("6. SGSIN Singapore to GRPIR Piraeus with Suez and Gibraltar closed",
    $"{blocked.Geometry?.Positions.Count ?? 0} points, {blocked.Properties.Length} km");

// 7. Preferred ports per area: one route per port share. The polygon is Belgium's border, so it is coordinates.
Coordinate[] belgium =
[
    new(2.539, 51.129), new(2.658, 50.797), new(3.123, 50.780), new(4.180, 50.029),
    new(4.885, 50.153), new(4.844, 49.817), new(5.368, 49.660), new(5.463, 49.502),
    new(5.839, 49.606), new(5.738, 49.963), new(6.413, 50.380), new(5.740, 50.813),
    new(5.823, 51.124), new(4.706, 51.474), new(3.830, 51.621), new(3.315, 51.346),
    new(2.539, 51.129)
];
var areaBelgium = new AreaFeature(belgium, "BE", [new PortProps("BEANR", 250), new PortProps("FRLEH", 200)]);
var routes = TradeRoutes.CalculateRoutes(At("BEBRU"), At("JPTYO"), new TradeRouterOptions
{
    IncludePorts = true,
    PortParameters = new PortParameters { PortsInAreasFrom = [areaBelgium] }
});
Print("7. BEBRU Brussels to JPTYO Tokyo with weighted preferred ports",
    string.Join("; ", routes.Select(r =>
        $"{r.Properties.PortOrigin?.PortCode} share {r.Properties.PortOrigin?.Share:P0}: {r.Properties.Length:N0} km")));

// 8. GeoJSON output, ready for Leaflet, Mapbox or any GIS tool.
string geoJson = route.ToJson(writeIndented: printGeoJson);
Print("8. GeoJSON", printGeoJson ? Environment.NewLine + geoJson : $"{geoJson.Length:N0} characters, first 100: {geoJson[..100]}...");

// 9. The README diagram's worked example: Shanghai to London, following each step.
var shanghai = TradeRoutes.Locate("CNSHG");
var london = TradeRoutes.Locate("GBLON");
var graph = TradeRouterEngine.Default.Graph;
var shanghaiLane = graph.GetCoordinate(graph.FindNearestNode(shanghai.Coordinate));
var londonLane = graph.GetCoordinate(graph.FindNearestNode(london.Coordinate));
var lanePath = TradeRoutes.Calculate(shanghai.Coordinate, london.Coordinate, returnPassages: true);
var finished = TradeRoutes.Calculate(shanghai.Coordinate, london.Coordinate, appendOrigDest: true);
Print("9. CNSHG Shanghai to GBLON London, step by step",
    $"request        CNSHG {shanghai.Name} ({shanghai.Source}) to GBLON {london.Name} ({london.Source}), km, 16 knots{Environment.NewLine}" +
    $"   snap           Shanghai lane point {Haversine.Distance(shanghai.Coordinate, shanghaiLane):N1} km away, London lane point {Haversine.Distance(london.Coordinate, londonLane):N1} km away{Environment.NewLine}" +
    $"   shortest path  {lanePath.Geometry?.Positions.Count ?? 0} lane points, {lanePath.Properties.Length:N0} km via {PassageNames(lanePath.Properties.TraversedPassages)}{Environment.NewLine}" +
    $"   finished route {finished.Geometry?.Positions.Count ?? 0} points, {finished.Properties.Length:N0} km, {finished.Properties.DurationHours:N1} h{Environment.NewLine}" +
    $"   result         GeoJSON Feature, {finished.ToJson().Length:N0} characters");

// 10 to 15. Multi-leg movements. Sea legs use the maritime network; road legs use a configured provider
//    or the labelled built-in circuity estimate. Air legs use great-circle distance.
//    Every code resolves from the embedded port list or UN/LOCODE list, so examples do not embed coordinates.
//    Gatwick (GBLGW), Shanghai Railway Station (CNSHZ) and Melrose (AUMRS) have no coordinates in the UNECE
//    list; the library's supplement file fills them from cited sources. AUMRS is Melrose, an inland South
//    Australian town about 800 km from Melbourne, so its delivery leg is by road.

PrintMovement(
    "10. Movement with one sea leg",
    MovementPlan
        .From(Waypoint.Place("GBLGW"))
        .PickupTo(Waypoint.Port("GBFXT"), TransportMode.Road)
        .ThenTo(Waypoint.Port("CNSHG"), TransportMode.Sea)
        .DeliverTo(Waypoint.Place("CNSHZ"), TransportMode.Road));

PrintMovement(
    "11. Movement with several sea legs, a light 12 t load in one 40-foot container",
    MovementPlan
        .From(Waypoint.Place("GBLGW"))
        .PickupTo(Waypoint.Port("GBFXT"), TransportMode.Road)
        .ThenTo(Waypoint.Port("SGSIN"), TransportMode.Sea)
        .ThenTo(Waypoint.Port("AUMEL"), TransportMode.Sea)
        .DeliverTo(Waypoint.Place("AUMRS"), TransportMode.Road),
    tonnes: 12.0,
    teu: 2.0);

var airPlan = MovementPlan
    .From(Waypoint.Place("GBLGW"))
    .PickupTo(Waypoint.Airport("GBLHR"), TransportMode.Road)
    .ThenTo(Waypoint.Airport("AUMEL"), TransportMode.Air)
    .DeliverTo(Waypoint.Place("AUMRS"), TransportMode.Road);
var airRequest = new MovementRequest
{
    Legs = airPlan.Legs.ToList(),
    SeaOptions = new TradeRouterOptions { ReturnPassages = true },
    CargoTonnes = 20.0,
    RoadRoutingMode = RoadRoutingMode.EstimateOnly
};
PrintMovementRequest("12. Movement with an air leg using built-in road estimates", airRequest);

var roadPlan = MovementPlan
    .From(Waypoint.Place("GBLGW"))
    .PickupTo(Waypoint.Airport("GBLHR"), TransportMode.Road);
var estimatedRoadRequest = new MovementRequest
{
    Legs = roadPlan.Legs.ToList(),
    CargoTonnes = 20.0,
    RoadRoutingMode = RoadRoutingMode.EstimateOnly
};
PrintMovementRequest("13. Gatwick to Heathrow using the built-in road estimate", estimatedRoadRequest);

const string osrmUrlVariable = "TRADEROUTER_OSRM_URL";
string? osrmUrl = Environment.GetEnvironmentVariable(osrmUrlVariable);
if (string.IsNullOrWhiteSpace(osrmUrl))
{
    string skippedOsrm = $"Skipped: set {osrmUrlVariable}=http://127.0.0.1:5000 after starting OSRM as shown in the README.";
    Print(
        "14. Gatwick to Heathrow using an OSRM container",
        skippedOsrm);
    Print(
        "15. Cape Town to Guildford via Johannesburg and Heathrow using an OSRM container",
        skippedOsrm);
}
else
{
    if (!Uri.TryCreate(osrmUrl, UriKind.Absolute, out var osrmBaseUri))
        throw new ArgumentException($"{osrmUrlVariable} must be an absolute HTTP or HTTPS URI. Received: {osrmUrl}");

    using var osrmHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    var osrmRoadRouteProvider = new OsrmRoadRouteProvider(
        osrmHttpClient,
        osrmBaseUri,
        dataVersion: Environment.GetEnvironmentVariable("TRADEROUTER_OSRM_DATA_VERSION"));
    var osrmRoadRequest = new MovementRequest
    {
        Legs = roadPlan.Legs.ToList(),
        CargoTonnes = 20.0,
        RoadRouteProvider = osrmRoadRouteProvider,
        RoadRoutingMode = RoadRoutingMode.RequireNetwork
    };
    await PrintMovementRequestAsync("14. Gatwick to Heathrow using an OSRM container", osrmRoadRequest);

    var capeTownToGuildfordPlan = MovementPlan
        .From(Waypoint.Place("ZACPT"))
        .PickupTo(Waypoint.Airport("ZAJNB"), TransportMode.Road)
        .ThenTo(Waypoint.Airport("GBLHR"), TransportMode.Air)
        .DeliverTo(Waypoint.Place("GBGDD"), TransportMode.Road);
    var capeTownToGuildfordRequest = new MovementRequest
    {
        Legs = capeTownToGuildfordPlan.Legs.ToList(),
        CargoTonnes = 20.0,
        RoadRouteProvider = osrmRoadRouteProvider,
        RoadRoutingMode = RoadRoutingMode.RequireNetwork
    };
    await PrintMovementRequestAsync(
        "15. Cape Town to Guildford via Johannesburg and Heathrow using an OSRM container",
        capeTownToGuildfordRequest);
}

static void PrintMovement(string title, MovementPlan plan, double tonnes = 20.0, double? teu = null)
{
    // CO2e per leg uses GLEC well-to-wheel defaults per mode. With a TEU count, sea legs are charged per
    // container (76 g per TEU-km) rather than per tonne, so a light box is not under-counted.
    var request = new MovementRequest
    {
        Legs = plan.Legs.ToList(),
        SeaOptions = new TradeRouterOptions { ReturnPassages = true },
        CargoTonnes = tonnes,
        CargoTeu = teu
    };
    PrintMovementRequest(title, request);
}

static void PrintMovementRequest(string title, MovementRequest request)
{
    PrintMovementResult(title, TradeRoutes.CalculateMovement(request));
}

static async Task PrintMovementRequestAsync(string title, MovementRequest request)
{
    PrintMovementResult(title, await TradeRoutes.CalculateMovementAsync(request));
}

static void PrintMovementResult(string title, MovementResult movement)
{
    Console.WriteLine();
    Console.WriteLine(title);
    Console.WriteLine(movement.ToText());
}

static string PassageNames(IReadOnlyList<string>? tags)
{
    if (tags == null || tags.Count == 0)
        return "";
    return string.Join(", ", tags.Select(Passage.GetDisplayName));
}

// Position of a UN/LOCODE from the embedded port list or UN/LOCODE list.
static Coordinate At(string code) => TradeRoutes.Locate(code).Coordinate;

static void Print(string title, string detail)
{
    Console.WriteLine();
    Console.WriteLine(title);
    Console.WriteLine("   " + detail);
}
