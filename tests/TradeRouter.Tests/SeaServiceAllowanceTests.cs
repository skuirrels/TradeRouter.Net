using System.Text.Json;
using FluentAssertions;
using TradeRouter.Common;
using TradeRouter.Movements;
using TradeRouter.Passages;
using Xunit;

namespace TradeRouter.Tests;

public class SeaServiceAllowanceTests
{
    private static readonly Coordinate Shanghai = new(122.07, 30.63);
    private static readonly Coordinate Rotterdam = new(4.14, 51.95);
    private static readonly Coordinate Genoa = new(8.93, 44.40);
    private static readonly Coordinate JebelAli = new(55.03, 25.01);
    private static readonly Coordinate LosAngeles = new(-118.27, 33.73);
    private static readonly Coordinate NewYork = new(-74.05, 40.67);
    private static readonly Coordinate Singapore = new(103.85, 1.26);
    private static readonly Coordinate Melbourne = new(144.93, -37.83);
    private static readonly Coordinate Lagos = new(3.41, 6.42);
    private static readonly Coordinate Santos = new(-46.32, -23.97);

    [Theory]
    [InlineData("asia-north-europe", 0.63, new[] { Passage.Malacca, Passage.Babalmandab, Passage.Suez, Passage.Gibraltar })]
    [InlineData("asia-mediterranean", 1.33, new[] { Passage.Malacca, Passage.Babalmandab, Passage.Suez })]
    [InlineData("gulf-europe", 1.57, new[] { Passage.Ormuz, Passage.Babalmandab, Passage.Suez, Passage.Gibraltar })]
    public void Resolve_ChoosesCorridorFromPassages(string corridor, double fraction, string[] passages)
    {
        var (resolvedCorridor, resolvedFraction) = SeaServiceAllowance.Resolve(passages, Shanghai, Rotterdam);

        resolvedCorridor.Should().Be(corridor);
        resolvedFraction.Should().Be(fraction);
    }

    [Fact]
    public void Resolve_PanamaCorridorNeedsAnAmericanEndPoint()
    {
        SeaServiceAllowance.Resolve([Passage.Panama], Shanghai, NewYork).Should().Be(("panama", 0.24));
        SeaServiceAllowance.Resolve([Passage.Panama], NewYork, Shanghai).Should().Be(("panama", 0.24));
        // East Asia to North Europe through Panama is a Suez-avoiding Asia–Europe service, not the fitted Panama lane.
        SeaServiceAllowance.Resolve([Passage.Panama], Shanghai, Rotterdam).Should().Be(("asia-north-europe-cape", 0.22));
        SeaServiceAllowance.Resolve([Passage.Panama], Rotterdam, Shanghai).Should().Be(("asia-north-europe-cape", 0.22));
    }

    [Fact]
    public void Resolve_RecognisesCapeRoutesBetweenAsiaOrTheGulfAndEurope()
    {
        SeaServiceAllowance.Resolve([Passage.Sunda, Passage.SouthAfrica], Shanghai, Rotterdam).Should().Be(("asia-north-europe-cape", 0.22));
        SeaServiceAllowance.Resolve([Passage.SouthAfrica, Passage.Malacca], Rotterdam, Shanghai).Should().Be(("asia-north-europe-cape", 0.22));
        SeaServiceAllowance.Resolve([Passage.Sunda, Passage.SouthAfrica, Passage.Gibraltar], Shanghai, Genoa).Should().Be(("asia-mediterranean-cape", 0.37));
        SeaServiceAllowance.Resolve([Passage.Ormuz, Passage.SouthAfrica], JebelAli, Rotterdam).Should().Be(("gulf-europe-cape", 0.46));

        // The Cape alone does not make a Europe corridor: West Africa and Brazil keep the default.
        SeaServiceAllowance.Resolve([Passage.Sunda, Passage.SouthAfrica], Shanghai, Lagos).Should().Be(("default", 0.20));
        SeaServiceAllowance.Resolve([Passage.Sunda, Passage.SouthAfrica], Shanghai, Santos).Should().Be(("default", 0.20));
    }

    [Fact]
    public void Resolve_ReadsThePacificAndAtlanticFromTheEndPoints()
    {
        SeaServiceAllowance.Resolve([], Shanghai, LosAngeles).Should().Be(("transpacific", 0.12));
        SeaServiceAllowance.Resolve([], LosAngeles, Shanghai).Should().Be(("transpacific", 0.12));
        SeaServiceAllowance.Resolve([], Rotterdam, NewYork).Should().Be(("transatlantic", 0.80));
        SeaServiceAllowance.Resolve([], NewYork, Rotterdam).Should().Be(("transatlantic", 0.80));
    }

    [Fact]
    public void Resolve_FallsBackToTheHistoricalDefaultForUnfittedCorridors()
    {
        // Intra-Asia and Asia–Oceania have no observations.
        SeaServiceAllowance.Resolve([Passage.Sunda], Singapore, Melbourne).Should().Be(("default", 0.20));
        SeaServiceAllowance.Resolve([], Shanghai, Singapore).Should().Be(("default", 0.20));
        // Asia to the Americas through Suez and Gibraltar is unfitted too.
        SeaServiceAllowance.Resolve([Passage.Malacca, Passage.Suez, Passage.Gibraltar], Shanghai, NewYork).Should().Be(("default", 0.20));
        // A Mediterranean origin without Suez is not the Gulf corridor.
        SeaServiceAllowance.Resolve([Passage.Gibraltar], Genoa, NewYork).Should().Be(("transatlantic", 0.80));
        SeaServiceAllowance.Resolve(null, JebelAli, Singapore).Should().Be(("default", 0.20));
    }

    [Fact]
    public void Movement_SeaLegsReportTheirCorridorAndUseItsFraction()
    {
        var plan = MovementPlan.From(Waypoint.Port("CNSHG")).ThenTo(Waypoint.Port("NLRTM"), TransportMode.Sea);
        var result = TradeRoutes.CalculateMovement(plan);
        var sea = result.Legs[0];

        sea.Feature.Properties.OperationalAllowanceCorridor.Should().Be("asia-north-europe");
        sea.Feature.Properties.OperationalAllowanceFraction.Should().Be(0.63);
        sea.OperationalAllowanceHours.Should().BeApproximately(sea.DurationHours * 0.63, 1e-9);
        sea.Feature.Properties.TraversedPassages.Should().BeNull("the caller did not ask for passages");

        using var doc = JsonDocument.Parse(result.ToJson());
        var properties = doc.RootElement.GetProperty("features")[0].GetProperty("properties");
        properties.GetProperty("operational_allowance_corridor").GetString().Should().Be("asia-north-europe");
        properties.GetProperty("operational_allowance_fraction").GetDouble().Should().Be(0.63);
        properties.TryGetProperty("traversed_passages", out _).Should().BeFalse();
    }

    [Fact]
    public void Movement_PassagesAreStillReturnedWhenAskedFor()
    {
        var plan = MovementPlan.From(Waypoint.Port("CNSHG")).ThenTo(Waypoint.Port("NLRTM"), TransportMode.Sea);
        var result = TradeRoutes.CalculateMovement(plan, seaOptions: new TradeRouterOptions { ReturnPassages = true });

        result.Legs[0].Feature.Properties.TraversedPassages.Should().Contain(Passage.Suez);
        result.Legs[0].Feature.Properties.OperationalAllowanceCorridor.Should().Be("asia-north-europe");
    }

    [Fact]
    public void Movement_ARequestFractionOverridesEveryCorridor()
    {
        var request = new MovementRequest
        {
            Legs = MovementParser.Parse("Port CNSHG to Port NLRTM Sea"),
            SeaOperationalAllowance = 0.5
        };

        var sea = TradeRouterEngine.Default.CalculateMovement(request).Legs[0];

        sea.Feature.Properties.OperationalAllowanceCorridor.Should().Be("override");
        sea.Feature.Properties.OperationalAllowanceFraction.Should().Be(0.5);
        sea.OperationalAllowanceHours.Should().BeApproximately(sea.DurationHours * 0.5, 1e-9);
    }

    [Fact]
    public void Movement_CorridorFractionsMatchTheObservedLanesWithinFourDays()
    {
        // Observed port-to-port medians, berth departure to berth arrival, legs departing 2025–26.
        // Corridor fraction plus travelling time at 16 knots should land within four days of each.
        var observedDays = new Dictionary<(string From, string To), double>
        {
            [("CNSHG", "NLRTM")] = 45.6,
            [("CNSHG", "ITGOA")] = 53.8,
            [("AEJEA", "NLRTM")] = 42.1,
            [("CNSHG", "USLAX")] = 15.2,
            [("NLRTM", "USNYC")] = 15.6
        };

        foreach (var ((from, to), observed) in observedDays)
        {
            var plan = MovementPlan.From(Waypoint.Port(from)).ThenTo(Waypoint.Port(to), TransportMode.Sea);
            var sea = TradeRoutes.CalculateMovement(plan).Legs[0];
            double modelledDays = (sea.DurationHours + sea.OperationalAllowanceHours) / 24.0;

            modelledDays.Should().BeApproximately(observed, 4.0, $"{from} to {to} should follow the fitted corridor");
        }
    }

    [Theory]
    [InlineData(new[] { Passage.Northwest, Passage.Suez }, Passage.Panama)]
    [InlineData(new[] { Passage.Northwest, Passage.Suez, Passage.Panama }, Passage.SouthAfrica)]
    public void Movement_AsiaToEuropeAvoidingSuezUsesTheCapeCorridor(string[] restrictions, string expectedPassage)
    {
        var plan = MovementPlan.From(Waypoint.Port("CNSHG")).ThenTo(Waypoint.Port("NLRTM"), TransportMode.Sea);
        var options = new TradeRouterOptions { Restrictions = [.. restrictions], ReturnPassages = true };
        var sea = TradeRoutes.CalculateMovement(plan, seaOptions: options).Legs[0];

        sea.Feature.Properties.TraversedPassages.Should().Contain(expectedPassage).And.NotContain(Passage.Suez);
        sea.Feature.Properties.OperationalAllowanceCorridor.Should().Be("asia-north-europe-cape");
        sea.Feature.Properties.OperationalAllowanceFraction.Should().Be(0.22);
    }

    [Fact]
    public void Movement_CapeCorridorFractionsMatchTheObservedLanesWithinFiveDays()
    {
        // The same 2025–26 observed medians, modelled on the Cape route. The two Mediterranean lanes sit
        // either side of their corridor median, which leaves each about five days out.
        var observedDays = new Dictionary<(string From, string To), double>
        {
            [("CNSHG", "NLRTM")] = 45.5,
            [("CNSHG", "DEHAM")] = 44.0,
            [("CNSHG", "ITGOA")] = 53.6,
            [("CNSHG", "GRPIR")] = 45.9,
            [("AEJEA", "NLRTM")] = 42.2
        };
        var options = new TradeRouterOptions { Restrictions = [Passage.Northwest, Passage.Suez, Passage.Panama] };

        foreach (var ((from, to), observed) in observedDays)
        {
            var plan = MovementPlan.From(Waypoint.Port(from)).ThenTo(Waypoint.Port(to), TransportMode.Sea);
            var sea = TradeRoutes.CalculateMovement(plan, seaOptions: options).Legs[0];
            double modelledDays = (sea.DurationHours + sea.OperationalAllowanceHours) / 24.0;

            sea.Feature.Properties.OperationalAllowanceCorridor.Should().EndWith("-cape");
            modelledDays.Should().BeApproximately(observed, 5.0, $"{from} to {to} round the Cape should follow the fitted corridor");
        }
    }
}
