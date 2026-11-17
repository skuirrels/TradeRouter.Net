using System.Globalization;
using AwesomeAssertions;
using TradeRouter.Movements;
using Xunit;

namespace TradeRouter.Tests;

public class MovementTextTests
{
    [Fact]
    public void ToText_ReportsTheCompleteMovementWithoutCallerFormatting()
    {
        var plan = MovementPlan
            .From(Waypoint.Place("GBLGW"))
            .PickupTo(Waypoint.Port("GBFXT"), TransportMode.Road)
            .ThenTo(Waypoint.Port("SGSIN"), TransportMode.Sea)
            .ThenTo(Waypoint.Port("AUMEL"), TransportMode.Sea)
            .DeliverTo(Waypoint.Place("AUMRS"), TransportMode.Road);
        var result = TradeRoutes.CalculateMovement(
            plan,
            seaOptions: new TradeRouterOptions { ReturnPassages = true },
            cargoTonnes: 12.0,
            cargoTeu: 2.0);

        string text = result.ToText();

        text.Should().StartWith("Leg Kind");
        text.Should().Contain("Modelled transit time");
        text.Should().Contain("Distance basis");
        text.Should().Contain("Pickup    Road  GBLGW  GBFXT");
        text.Should().Contain("Main      Sea   GBFXT  SGSIN");
        text.Should().Contain("Delivery  Road  AUMEL  AUMRS");
        text.Should().Contain("Gibraltar, Suez, Bab-el-Mandeb, Malacca");
        text.Should().Contain("Sunda");
        text.Should().Contain("Modelled minimum =");
        text.Should().Contain("sea operations");
        text.Should().Contain("port handling");
        text.Should().Contain("connections");
        text.Should().Contain("planning lower bound");
        text.Should().Contain("circuity_estimate is a planning estimate");
        text.Should().NotContain("supplied is");
        text.Should().Contain(result.TotalLength.ToString("N0", CultureInfo.InvariantCulture));
        text.Should().Contain(result.TotalTransitHours.ToString("N1", CultureInfo.InvariantCulture));
        text.Should().Contain(result.TotalCo2eKg!.Value.ToString("N0", CultureInfo.InvariantCulture));
        text.Should().Contain("for 12 t of cargo in 2 TEU");
        text.Should().Contain("73.4 g per TEU-km on sea legs");
        text.Should().Contain("  teu_average_weight", "12 t in 2 TEU is charged at 10 t per TEU inland, in a column wide enough for the basis");
        text.Should().Contain("the greater of cargo weight and 10 t per TEU on road and rail legs");
    }

    [Fact]
    public void ToText_CanOmitColumnExplanations()
    {
        var result = TradeRoutes.CalculateMovement("Port GBFXT to Port SGSIN Sea");

        string text = result.ToText(includeExplanations: false);

        text.Should().Contain("GBFXT  SGSIN");
        text.Should().Contain("Modelled minimum =");
        text.Should().NotContain("grams of CO2e emitted");
    }

    // The report for a movement with no fallback, as it was before fallback warnings were added. The road legs are
    // circuity estimates, so they carry the estimator's distance_warning, which the report does not print.
    private static readonly string[] NoFallbackTable =
    [
        "Leg Kind      Mode  From   To      Distance       Distance basis   Modelled transit time       CO2e rate  CO2e per tonne  CO2e total               Basis  Choke points",
        "                                                                                   hours      g per t-km  kg per t cargo          kg",
        "1   Pickup    Road  GBLGW  GBFXT        183 km   circuity_estimate                     3.0           125.0            22.9         457  teu_average_weight  ",
        "2   Main      Sea   GBFXT  SGSIN     15,402 km    maritime_network                   895.2             7.3           113.0       2,261                 teu  Gibraltar, Suez, Bab-el-Mandeb, Malacca",
        "3   Main      Sea   SGSIN  AUMEL      7,300 km    maritime_network                   391.6             7.3            53.6       1,072                 teu  Sunda",
        "4   Delivery  Road  AUMEL  AUMRS      1,011 km   circuity_estimate                    16.8           125.0           126.4       2,527  teu_average_weight  ",
        "Total                                23,896 km                                     1,306.7                           315.9       6,317                      for 12 t of cargo in 2 TEU",
        "Modelled minimum = 786.0 h travel + 376.7 h sea operations + 96 h port handling + 48 h connections = 1,306.7 h (54.4 days)",
    ];

    private static readonly string[] NoFallbackNotes =
    [
        "Distance basis = maritime_network/road_network are network routes; circuity_estimate is a planning estimate; great_circle is straight-line",
        "Timing         = planning lower bound from configured assumptions; excludes carrier schedules, customs and disruption",
        "CO2e rate      = grams of CO2e emitted moving 1 tonne 1 km (configured factor for the mode)",
        "CO2e per tonne = rate × leg distance: kg of CO2e for each tonne of cargo carried over the leg",
        "CO2e total     = kg of CO2e for this shipment: 73.4 g per TEU-km on sea legs, the greater of cargo weight and 10 t per TEU on road and rail legs, cargo weight on air legs",
    ];

    private const string GoogleForbidden =
        "Google Routes returned HTTP 403 PERMISSION_DENIED: The caller does not have permission";

    [Fact]
    public void ToText_WithoutFallback_IsUnchanged()
    {
        var result = CalculateNoFallbackMovement();

        result.Legs.Should().Contain(leg => leg.Feature.Properties.DistanceWarning != null,
            "ordinary estimates carry the estimator's caveat, which the report leaves to the distance basis footnote");
        result.ToText().ReplaceLineEndings("\n").Should().Be(string.Join("\n", NoFallbackTable.Concat(NoFallbackNotes)).TrimEnd());
        result.ToText(includeExplanations: false).ReplaceLineEndings("\n").Should().Be(string.Join("\n", NoFallbackTable).TrimEnd());
    }

    [Theory]
    [InlineData(RoadRouteStatus.Unavailable, GoogleForbidden,
        "Leg 1 warning  = google-routes returned Unavailable: Google Routes returned HTTP 403 PERMISSION_DENIED: The caller does not have permission. Estimated with a distance-decay circuity model because no road-network route was available.")]
    [InlineData(RoadRouteStatus.Unavailable, "The OSRM request timed out. ",
        "Leg 1 warning  = google-routes returned Unavailable: The OSRM request timed out. Estimated with a distance-decay circuity model because no road-network route was available.")]
    [InlineData(RoadRouteStatus.Unavailable, "  ",
        "Leg 1 warning  = google-routes returned Unavailable: no reason supplied. Estimated with a distance-decay circuity model because no road-network route was available.")]
    [InlineData(RoadRouteStatus.OutsideCoverage, "no coverage\u2028for\r\nthis area",
        "Leg 1 warning  = google-routes returned OutsideCoverage: no coverage for this area. Estimated with a distance-decay circuity model because no road-network route was available.")]
    public async Task ToText_PrintsRoadProviderFallbackWarning(RoadRouteStatus status, string message, string expected)
    {
        var request = new MovementRequest
        {
            Legs = MovementParser.Parse("Pickup GBLGW to Port GBFXT Road\nPort GBFXT to Port NLRTM Sea"),
            RoadRouteProvider = new StubRoadProvider(new RoadRouteResult { Status = status, Source = "google-routes", Message = message }),
            RoadRoutingMode = RoadRoutingMode.PreferNetworkThenEstimate
        };

        var result = await TradeRouterEngine.Default.CalculateMovementAsync(request);

        result.Legs[0].Feature.Properties.DistanceBasis.Should().Be("circuity_estimate");
        foreach (bool includeExplanations in new[] { true, false })
        {
            string[] lines = result.ToText(includeExplanations).ReplaceLineEndings("\n").Split('\n');
            int summary = Array.FindIndex(lines, line => line.StartsWith("Modelled minimum = ", StringComparison.Ordinal));
            lines[summary + 1].Should().Be(expected, "the warning follows the legs table and its summary line");
            lines.Should().ContainSingle(line => line.Contains("warning", StringComparison.Ordinal), "only the fallback leg warns");
            if (includeExplanations)
                lines[summary + 2].Should().StartWith("Distance basis = ", "the warning comes before the footnotes");
            else
                lines.Should().HaveCount(summary + 2);
        }
        result.ToJson().Should().NotContainEquivalentOf("fallback", "the GeoJSON shape is unchanged");
    }

    [Fact]
    public async Task ToText_EstimateOnlyWithAProvider_PrintsNoWarning()
    {
        var request = new MovementRequest
        {
            Legs = MovementParser.Parse("Pickup GBLGW to Port GBFXT Road"),
            RoadRouteProvider = new StubRoadProvider(new RoadRouteResult { Status = RoadRouteStatus.Unavailable, Source = "google-routes" }),
            RoadRoutingMode = RoadRoutingMode.EstimateOnly
        };

        var result = await TradeRouterEngine.Default.CalculateMovementAsync(request);

        result.Legs[0].Feature.Properties.DistanceWarning.Should().NotBeNull("the estimate still carries its caveat");
        result.ToText().Should().NotContain("warning");
    }

    [Fact]
    public async Task ToText_TrimsTrailingWhitespaceFromAWarning()
    {
        var request = new MovementRequest
        {
            Legs = MovementParser.Parse("Pickup GBLGW to Port GBFXT Road"),
            RoadRouteProvider = new StubRoadProvider(new RoadRouteResult { Status = RoadRouteStatus.Unavailable, Source = "stub", Message = "down" }),
            RoadRoutingMode = RoadRoutingMode.PreferNetworkThenEstimate,
            RoadDistanceEstimator = new StubEstimator(new RoadDistanceEstimate(200, "stub-model", "Stub warning.\n"))
        };

        var result = await TradeRouterEngine.Default.CalculateMovementAsync(request);

        result.ToText().ReplaceLineEndings("\n").Split('\n')
            .Should().Contain("Leg 1 warning  = stub returned Unavailable: down. Stub warning.");
    }

    [Fact]
    public void ToText_PrintsRailFallbackWarning()
    {
        var result = TradeRoutes.CalculateMovement("DEDUI to PLWAW Rail");

        string[] lines = result.ToText().ReplaceLineEndings("\n").Split('\n');

        lines.Should().Contain(
            "Leg 1 warning  = PLWAW is 5,419 km from the nearest main line of the embedded North American rail network, beyond the 25 km limit; great-circle distance used.");
    }

    [Fact]
    public void ToText_PadsTheLabelLikeTheFootnotesAndNumbersEachLeg()
    {
        var result = TradeRoutes.CalculateMovement("DEDUS to DEDUI Road\nDEDUI to PLWAW Rail");

        result.ToText(includeExplanations: false).ReplaceLineEndings("\n").Split('\n').Last()
            .Should().StartWith("Leg 2 warning  = PLWAW is ");
    }

    private static MovementResult CalculateNoFallbackMovement()
    {
        var plan = MovementPlan
            .From(Waypoint.Place("GBLGW"))
            .PickupTo(Waypoint.Port("GBFXT"), TransportMode.Road)
            .ThenTo(Waypoint.Port("SGSIN"), TransportMode.Sea)
            .ThenTo(Waypoint.Port("AUMEL"), TransportMode.Sea)
            .DeliverTo(Waypoint.Place("AUMRS"), TransportMode.Road);
        return TradeRoutes.CalculateMovement(
            plan,
            seaOptions: new TradeRouterOptions { ReturnPassages = true },
            cargoTonnes: 12.0,
            cargoTeu: 2.0);
    }

    private sealed class StubRoadProvider(RoadRouteResult result) : IRoadRouteProvider
    {
        public ValueTask<RoadRouteResult> RouteAsync(RoadRouteRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(result);
    }

    private sealed class StubEstimator(RoadDistanceEstimate estimate) : IRoadDistanceEstimator
    {
        public RoadDistanceEstimate Estimate(RoadDistanceEstimateRequest request) => estimate;
    }
}
