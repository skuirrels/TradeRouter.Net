using AwesomeAssertions;
using TradeRouter.Common;
using TradeRouter.Locations;
using TradeRouter.Movements;
using Xunit;

namespace TradeRouter.Tests;

public class RoadDistanceEstimatorTests
{
    private static readonly ResolvedLocation From = new("FROM", "From", new Coordinate(0, 0), null, "test", LocationFunctions.RoadTerminal);
    private static readonly ResolvedLocation To = new("TO", "To", new Coordinate(1, 1), null, "test", LocationFunctions.RoadTerminal);

    [Fact]
    public void CircuityEstimator_DefaultAppliesThirtyPercentCircuity()
    {
        var estimate = CircuityRoadDistanceEstimator.Default.Estimate(new RoadDistanceEstimateRequest(1, From, To, 100));

        CircuityRoadDistanceEstimator.Default.Factor.Should().Be(CircuityRoadDistanceEstimator.DefaultFactor);
        estimate.DistanceKm.Should().BeApproximately(130.0, 1e-9);
        estimate.Model.Should().Be("circuity-1.3");
        estimate.Warning.Should().Contain("great-circle distance");
    }

    [Theory]
    [InlineData(1.0, 100.0, "circuity-1")]
    [InlineData(1.25, 125.0, "circuity-1.25")]
    [InlineData(1.4567, 145.67, "circuity-1.457")]
    public void CircuityEstimator_UsesSuppliedFactorAndNamesIt(double factor, double expectedKm, string expectedModel)
    {
        var estimate = new CircuityRoadDistanceEstimator(factor).Estimate(new RoadDistanceEstimateRequest(1, From, To, 100));

        estimate.DistanceKm.Should().BeApproximately(expectedKm, 1e-9);
        estimate.Model.Should().Be(expectedModel);
    }

    [Theory]
    [InlineData(0.99)]
    [InlineData(0.0)]
    [InlineData(-1.3)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void CircuityEstimator_RejectsFactorBelowOneOrNotFinite(double factor)
    {
        var act = () => new CircuityRoadDistanceEstimator(factor);

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*circuity factor must be finite and at least 1*");
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Estimators_RejectInvalidStraightLineDistance(double straightLineKm)
    {
        var request = new RoadDistanceEstimateRequest(1, From, To, straightLineKm);

        FluentActions.Invoking(() => CircuityRoadDistanceEstimator.Default.Estimate(request))
            .Should().Throw<ArgumentOutOfRangeException>().WithMessage("*Straight-line distance*");
        FluentActions.Invoking(() => DistanceDecayRoadDistanceEstimator.Default.Estimate(request))
            .Should().Throw<ArgumentOutOfRangeException>().WithMessage("*Straight-line distance*");
    }

    [Fact]
    public void Estimators_RejectNullRequest()
    {
        FluentActions.Invoking(() => CircuityRoadDistanceEstimator.Default.Estimate(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => DistanceDecayRoadDistanceEstimator.Default.Estimate(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Estimators_ReturnZeroForZeroStraightLineDistance()
    {
        var request = new RoadDistanceEstimateRequest(1, From, From, 0.0);

        CircuityRoadDistanceEstimator.Default.Estimate(request).DistanceKm.Should().Be(0.0);
        DistanceDecayRoadDistanceEstimator.Default.Estimate(request).DistanceKm.Should().Be(0.0);
    }

    [Fact]
    public void Movement_UsesConfiguredCircuityEstimatorForRoadLegs()
    {
        var request = new MovementRequest
        {
            Legs = MovementParser.Parse("Pickup GBLGW to Airport GBLHR Road"),
            RoadDistanceEstimator = new CircuityRoadDistanceEstimator(1.25)
        };

        var road = TradeRouterEngine.Default.CalculateMovement(request).Legs.Single();

        road.Length.Should().BeApproximately(road.Feature.Properties.StraightLineLength!.Value * 1.25, 1e-9);
        road.Feature.Properties.DistanceBasis.Should().Be("circuity_estimate");
        road.Feature.Properties.DistanceSource.Should().Be("circuity-1.25");
        road.Feature.Properties.DistanceWarning.Should().Contain("great-circle distance");
        road.DurationHours.Should().BeApproximately(road.Length / 60.0, 1e-9);
    }
}
