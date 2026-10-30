using System.Text.Json;
using AwesomeAssertions;
using TradeRouter.Common;
using TradeRouter.Movements;
using Xunit;

namespace TradeRouter.Tests;

public class RailServiceAllowanceTests
{
    private static MovementRequest Rail(string from, string to) => new()
    {
        Legs = MovementParser.Parse($"Station {from} to Station {to} Rail")
    };

    [Fact]
    public void Defaults_KeepRailTransitTimeAsDistanceDividedBySpeed()
    {
        var leg = TradeRouterEngine.Default.CalculateMovement(Rail("CAMTR", "USDET")).Legs.Single();

        leg.DurationHours.Should().BeApproximately(leg.Length / 80.0, 1e-6);
        leg.RailTerminalHours.Should().Be(0.0);
        leg.RailInterchangeHours.Should().Be(0.0);
        leg.BorderHours.Should().Be(0.0);
        leg.TransitHours.Should().Be(leg.DurationHours);
        leg.Feature.Properties.BorderCrossings.Should().Be(1, "the counts are reported even when no hours are charged");
    }

    [Fact]
    public void TerminalDwell_IsAddedForBothEndsOfEveryRailLeg()
    {
        var request = Rail("CAMTR", "USDET");
        request.RailOriginTerminalHours = 24.0;
        request.RailDestinationTerminalHours = 12.0;

        var result = TradeRouterEngine.Default.CalculateMovement(request);
        var leg = result.Legs.Single();

        leg.RailTerminalHours.Should().Be(36.0);
        leg.TransitHours.Should().BeApproximately(leg.DurationHours + 36.0, 1e-9);
        result.TotalRailTerminalHours.Should().Be(36.0);
        result.TotalTransitHours.Should().BeApproximately(result.TotalDurationHours + 36.0, 1e-9);
    }

    [Fact]
    public void BorderCrossing_IsChargedOnACanadaToUnitedStatesLeg()
    {
        var request = Rail("CAMTR", "USDET");
        request.RailBorderCrossingHours = 6.0;

        var leg = TradeRouterEngine.Default.CalculateMovement(request).Legs.Single();

        leg.Feature.Properties.BorderCrossings.Should().Be(1);
        leg.BorderHours.Should().Be(6.0);
        leg.TransitHours.Should().BeApproximately(leg.DurationHours + 6.0, 1e-9);
    }

    [Fact]
    public void BorderCrossing_IsNotChargedWithinOneCountry()
    {
        var request = Rail("USLAX", "USCHI");
        request.RailBorderCrossingHours = 6.0;

        var leg = TradeRouterEngine.Default.CalculateMovement(request).Legs.Single();

        leg.Feature.Properties.BorderCrossings.Should().Be(0);
        leg.BorderHours.Should().Be(0.0);
    }

    [Theory]
    [InlineData("US", "CA", 1)]
    [InlineData("CA", "US", 1)]
    [InlineData("MX", "US", 1)]
    [InlineData("CA", "MX", 2)]
    [InlineData("US", "US", 0)]
    [InlineData("GB", "US", 0)]
    [InlineData(null, "US", 0)]
    public void CountBorderCrossings_FollowsTheEndCountries(string? from, string? to, int expected)
    {
        RailServiceAllowance.CountBorderCrossings(from, to).Should().Be(expected);
    }

    [Fact]
    public void CountClassIInterchanges_CountsOnlyClassIRailroadsWithALongStretchOfTheRoute()
    {
        (string?, double)[] segments =
        [
            ("BNSF", 1_400.0),
            ("UP", 600.0),      // parallel line the shortest path borrows: not counted
            ("BNSF", 900.0),
            ("NIRC", 70.0),     // commuter track, not Class I
            (null, 5.0),
            ("NS", 1_200.0)
        ];

        RailServiceAllowance.CountClassIInterchanges(segments).Should().Be(1);
        RailServiceAllowance.CountClassIInterchanges([("UP", 500.0), ("BNSF", 500.0)]).Should().Be(0);
        RailServiceAllowance.CountClassIInterchanges([]).Should().Be(0);
    }

    [Theory]
    [InlineData("USLAX", "USCHI", 0)] // BNSF or UP single-line, though the path weaves between their lines
    [InlineData("CAMTR", "USDET", 0)] // CN single-line
    [InlineData("USLAX", "USNYC", 1)] // western railroad to eastern railroad
    public void Interchanges_AreEstimatedFromTheRoutedPath(string from, string to, int expected)
    {
        var request = Rail(from, to);
        request.RailInterchangeHours = 24.0;

        var leg = TradeRouterEngine.Default.CalculateMovement(request).Legs.Single();

        leg.Feature.Properties.RailInterchanges.Should().Be(expected);
        leg.RailInterchangeHours.Should().Be(24.0 * expected);
        leg.TransitHours.Should().BeApproximately(leg.DurationHours + 24.0 * expected, 1e-9);
    }

    [Fact]
    public void InterchangeCounts_OverrideTheEstimateAndApplyToGreatCircleLegs()
    {
        var request = new MovementRequest
        {
            Legs =
            [
                new MovementLeg(
                    Location.FromCoordinate(new Coordinate(-0.1278, 51.5074), "London"),
                    Location.FromCoordinate(new Coordinate(1.3108, 51.9630), "Felixstowe"),
                    TransportMode.Rail,
                    LegKind.Main)
            ],
            RailInterchangeHours = 10.0
        };
        request.RailInterchangeCounts[1] = 2;

        var leg = TradeRouterEngine.Default.CalculateMovement(request).Legs.Single();

        leg.Feature.Properties.DistanceBasis.Should().Be("great_circle");
        leg.Feature.Properties.RailInterchanges.Should().Be(2);
        leg.RailInterchangeHours.Should().Be(20.0);
        leg.Feature.Properties.BorderCrossings.Should().Be(0, "coordinate-only ends have no country");
    }

    [Fact]
    public void Allowances_AreReportedInTheJsonAndOnlyOnRailLegs()
    {
        var request = new MovementRequest
        {
            Legs = MovementParser.Parse(
                """
                Port MAPTM to Port CAMTR Sea
                Station CAMTR to Station USDET Rail
                """),
            RailOriginTerminalHours = 24.0,
            RailDestinationTerminalHours = 24.0,
            RailBorderCrossingHours = 12.0
        };

        var result = TradeRouterEngine.Default.CalculateMovement(request);

        using var doc = JsonDocument.Parse(result.ToJson());
        var sea = doc.RootElement.GetProperty("features")[0].GetProperty("properties");
        sea.TryGetProperty("rail_terminal_hours", out _).Should().BeFalse();
        sea.TryGetProperty("border_hours", out _).Should().BeFalse();
        var rail = doc.RootElement.GetProperty("features")[1].GetProperty("properties");
        rail.GetProperty("rail_terminal_hours").GetDouble().Should().Be(48.0);
        rail.GetProperty("rail_interchanges").GetInt32().Should().Be(0);
        rail.GetProperty("border_crossings").GetInt32().Should().Be(1);
        rail.GetProperty("border_hours").GetDouble().Should().Be(12.0);
        var totals = doc.RootElement.GetProperty("properties");
        totals.GetProperty("total_rail_terminal_hours").GetDouble().Should().Be(48.0);
        totals.GetProperty("total_rail_interchange_hours").GetDouble().Should().Be(0.0);
        totals.GetProperty("total_border_hours").GetDouble().Should().Be(12.0);
        result.Legs[0].RailTerminalHours.Should().Be(0.0);
        result.TotalTransitHours.Should().BeApproximately(
            result.Legs[0].TransitHours + result.Legs[1].DurationHours + 48.0 + 12.0, 1e-9);
    }

    [Fact]
    public void TextSummary_NamesRailAllowancesOnlyWhenSet()
    {
        var plain = TradeRouterEngine.Default.CalculateMovement(Rail("CAMTR", "USDET"));
        var request = Rail("CAMTR", "USDET");
        request.RailOriginTerminalHours = 24.0;
        var withDwell = TradeRouterEngine.Default.CalculateMovement(request);

        MovementTextFormatter.Format(plain, includeExplanations: false).Should().NotContain("rail terminals");
        MovementTextFormatter.Format(withDwell, includeExplanations: false).Should().Contain("+ 24 h rail terminals");
    }

    [Theory]
    [InlineData(nameof(MovementRequest.RailOriginTerminalHours), -1.0)]
    [InlineData(nameof(MovementRequest.RailDestinationTerminalHours), double.NaN)]
    [InlineData(nameof(MovementRequest.RailInterchangeHours), double.PositiveInfinity)]
    [InlineData(nameof(MovementRequest.RailBorderCrossingHours), -0.5)]
    public void InvalidAllowanceHours_AreRejected(string property, double value)
    {
        var request = Rail("CAMTR", "USDET");
        typeof(MovementRequest).GetProperty(property)!.SetValue(request, value);

        var act = () => TradeRouterEngine.Default.CalculateMovement(request);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(property);
    }

    [Fact]
    public void InterchangeCount_ForANonRailLegIsRejected()
    {
        var request = new MovementRequest { Legs = MovementParser.Parse("Port MAPTM to Port CAMTR Sea") };
        request.RailInterchangeCounts[1] = 0;

        var act = () => TradeRouterEngine.Default.CalculateMovement(request);

        act.Should().Throw<ArgumentException>().WithMessage("*does not refer to a rail leg*");
    }

    [Fact]
    public void InterchangeCount_MustNotBeNegative()
    {
        var request = Rail("CAMTR", "USDET");
        request.RailInterchangeCounts[1] = -1;

        var act = () => TradeRouterEngine.Default.CalculateMovement(request);

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*non-negative*");
    }
}
