using FluentAssertions;
using TradeRouter.Movements;
using Xunit;

namespace TradeRouter.Tests;

public class MovementPlanTests
{
    [Fact]
    public void FluentPlan_BuildsTypedContinuousLegs()
    {
        var origin = MovementPlan.From(Waypoint.Place("GBLGW"));
        var pickup = origin.PickupTo(Waypoint.Port("GBFXT"), TransportMode.Road);
        var plan = pickup
            .ThenTo(Waypoint.Port("SGSIN"), TransportMode.Sea)
            .ThenTo(Waypoint.Port("AUMEL"), TransportMode.Sea)
            .DeliverTo(Waypoint.Place("AUMRS"), TransportMode.Road);

        origin.Legs.Should().BeEmpty("movement plans are immutable");
        pickup.Legs.Should().ContainSingle();
        plan.Legs.Should().HaveCount(4);
        plan.Legs.Select(leg => leg.Kind).Should().Equal(
            LegKind.Pickup,
            LegKind.Main,
            LegKind.Main,
            LegKind.Delivery);
        plan.Legs.Select(leg => leg.Mode).Should().Equal(
            TransportMode.Road,
            TransportMode.Sea,
            TransportMode.Sea,
            TransportMode.Road);
        plan.Legs[1].From.Code.Should().Be("GBFXT");
        plan.Legs[1].To.Code.Should().Be("SGSIN");
        plan.Legs[1].FromKind.Should().Be(WaypointKind.Port);
        plan.Legs[1].ToKind.Should().Be(WaypointKind.Port);
    }

    [Fact]
    public void FluentPlan_CalculatesThroughThePublicApi()
    {
        var plan = MovementPlan
            .From(Waypoint.Port("GBFXT"))
            .ThenTo(Waypoint.Port("SGSIN"), TransportMode.Sea);

        var result = TradeRoutes.CalculateMovement(plan);

        result.Legs.Should().ContainSingle();
        result.Legs[0].Length.Should().BeGreaterThan(15_000.0);
    }

    [Fact]
    public void FluentPlan_ProtectsPickupAndDeliveryOrdering()
    {
        var mainCarriage = MovementPlan
            .From(Waypoint.Port("GBFXT"))
            .ThenTo(Waypoint.Port("SGSIN"), TransportMode.Sea);
        var completed = mainCarriage.DeliverTo(Waypoint.Place("SGSIN"), TransportMode.Road);

        var latePickup = () => mainCarriage.PickupTo(Waypoint.Port("AUMEL"), TransportMode.Sea);
        var afterDelivery = () => completed.ThenTo(Waypoint.Port("AUMEL"), TransportMode.Sea);

        latePickup.Should().Throw<InvalidOperationException>().WithMessage("*first leg*");
        afterDelivery.Should().Throw<InvalidOperationException>().WithMessage("*after the delivery leg*");
    }
}
