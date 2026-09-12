using System.Text.Json;
using FluentAssertions;
using SeaRoute.Common;
using SeaRoute.Movements;
using SeaRoute.Passages;
using Xunit;

namespace SeaRoute.Tests;

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

    [Theory]
    [InlineData("asia-north-europe", 0.63, new[] { Passage.Malacca, Passage.Babalmandab, Passage.Suez, Passage.Gibraltar })]
    [InlineData("asia-mediterranean", 1.33, new[] { Passage.Malacca, Passage.Babalmandab, Passage.Suez })]
    [InlineData("gulf-europe", 1.57, new[] { Passage.Ormuz, Passage.Babalmandab, Passage.Suez, Passage.Gibraltar })]
    [InlineData("panama", 0.24, new[] { Passage.Panama })]
    public void Resolve_ChoosesCorridorFromPassages(string corridor, double fraction, string[] passages)
    {
        var (resolvedCorridor, resolvedFraction) = SeaServiceAllowance.Resolve(passages, Shanghai, Rotterdam);

        resolvedCorridor.Should().Be(corridor);
        resolvedFraction.Should().Be(fraction);
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
        var result = SeaRouter.CalculateMovement(plan);
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
        var result = SeaRouter.CalculateMovement(plan, seaOptions: new SeaRouteOptions { ReturnPassages = true });

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

        var sea = SeaRouteEngine.Default.CalculateMovement(request).Legs[0];

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
            var sea = SeaRouter.CalculateMovement(plan).Legs[0];
            double modelledDays = (sea.DurationHours + sea.OperationalAllowanceHours) / 24.0;

            modelledDays.Should().BeApproximately(observed, 4.0, $"{from} to {to} should follow the fitted corridor");
        }
    }
}
