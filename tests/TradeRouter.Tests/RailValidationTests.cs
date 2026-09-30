using AwesomeAssertions;
using TradeRouter.Locations;
using TradeRouter.Movements;
using Xunit;

namespace TradeRouter.Tests;

public class RailValidationTests
{
    [Theory]
    [InlineData("INTMX to INTKD Rail")]
    [InlineData("Depot INTMX to Station INTKD Rail")]
    public void Rail_AcceptsMultimodalFacility(string legs)
    {
        var result = TradeRoutes.CalculateMovement(legs);

        result.Legs.Should().ContainSingle().Which.Length.Should().BeGreaterThan(0.0);
    }

    [Fact]
    public void Rail_RejectionNamesAdditionalLocationFunctions()
    {
        var act = () => TradeRoutes.CalculateMovement("GBFXT to DEDUI Rail");

        act.Should().Throw<ArgumentException>().WithMessage(
            "Leg 1 uses Rail, but its from location GBFXT is recorded as sea port rather than a compatible rail terminal. " +
            "If the UN/LOCODE record is incomplete, declare the function in MovementRequest.AdditionalLocationFunctions.");
    }

    [Fact]
    public void Rail_AdditionalLocationFunctionsAcceptDeclaredRailTerminal()
    {
        // Maslianico is recorded only as a road terminal; the caller's own sources are assumed to show a rail siding.
        var plan = MovementPlan
            .From(Waypoint.Station("ITMLN"))
            .ThenTo(Waypoint.Station("DEDUI"), TransportMode.Rail);
        var request = new MovementRequest { Legs = [.. plan.Legs] };

        FluentActions.Invoking(() => TradeRouterEngine.Default.CalculateMovement(request))
            .Should().Throw<ArgumentException>()
            .WithMessage("*ITMLN is declared as Station, but UN/LOCODE records road terminal.*AdditionalLocationFunctions.");

        request.AdditionalLocationFunctions["itmln"] = LocationFunctions.RailTerminal;
        var leg = TradeRouterEngine.Default.CalculateMovement(request).Legs.Single();

        leg.Feature.Properties.From.Should().Be("ITMLN");
        leg.Length.Should().BeGreaterThan(500.0);
    }

    [Fact]
    public void Rail_AdditionalLocationFunctionsDoNotAffectOtherCodes()
    {
        var request = new MovementRequest { Legs = MovementParser.Parse("GBFXT to DEDUI Rail") };
        request.AdditionalLocationFunctions["ITMLN"] = LocationFunctions.RailTerminal;

        FluentActions.Invoking(() => TradeRouterEngine.Default.CalculateMovement(request))
            .Should().Throw<ArgumentException>().WithMessage("Leg 1 uses Rail, but its from location GBFXT*");
    }

    [Theory]
    [InlineData("rail override on road leg", "Rail route override sequence 1 does not refer to a rail leg.*")]
    [InlineData("rail override past end", "Rail route override sequence 3 is outside the movement's leg range.*")]
    [InlineData("rail override negative distance", "Rail route override sequence 2 has an invalid distance.*")]
    [InlineData("road override on rail leg", "Road route override sequence 2 does not refer to a road leg.*")]
    [InlineData("blank additional function key", "Additional location function keys cannot be blank.*")]
    [InlineData("unknown additional function flag", "Additional location functions for ITMLN contain unknown flags.*")]
    public void Rail_RejectsInvalidRequest(string invalidRequest, string expectedMessage)
    {
        var request = new MovementRequest { Legs = MovementParser.Parse("Pickup GBLGW to GBFXT Road\nDEHAM to DEDUI Rail") };
        Action<MovementRequest> mutate = invalidRequest switch
        {
            "rail override on road leg" => r => r.RailRouteOverrides[1] = new SuppliedRoute { DistanceKm = 10 },
            "rail override past end" => r => r.RailRouteOverrides[3] = new SuppliedRoute { DistanceKm = 10 },
            "rail override negative distance" => r => r.RailRouteOverrides[2] = new SuppliedRoute { DistanceKm = -1 },
            "road override on rail leg" => r => r.RoadRouteOverrides[2] = new SuppliedRoute { DistanceKm = 10 },
            "blank additional function key" => r => r.AdditionalLocationFunctions[" "] = LocationFunctions.RailTerminal,
            "unknown additional function flag" => r => r.AdditionalLocationFunctions["ITMLN"] = (LocationFunctions)256,
            _ => throw new ArgumentOutOfRangeException(nameof(invalidRequest))
        };
        mutate(request);

        var act = () => TradeRouterEngine.Default.CalculateMovement(request);

        act.Should().Throw<ArgumentException>().WithMessage(expectedMessage);
    }
}
