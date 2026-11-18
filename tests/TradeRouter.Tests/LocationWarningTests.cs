using AwesomeAssertions;
using TradeRouter.Common;
using TradeRouter.Movements;
using Xunit;

namespace TradeRouter.Tests;

public class LocationWarningTests
{
    private static readonly Coordinate ShenzhenCity = new(114.06, 22.54);
    private static readonly Coordinate Alps = new(10.0, 46.5);
    private static readonly Coordinate Placeholder = new(0.0, 0.0);

    private static MovementRequest Pickup(Location pickup) => new()
    {
        Legs =
        [
            new MovementLeg(pickup, Location.FromCode("CNSHG"), TransportMode.Road, LegKind.Pickup),
            new MovementLeg("CNSHG", "SGSIN", TransportMode.Sea)
        ],
        RoadRoutingMode = RoadRoutingMode.EstimateOnly
    };

    private static LegResult FirstLeg(MovementRequest request) =>
        TradeRouterEngine.Default.CalculateMovement(request).Legs[0];

    [Fact]
    public void CoordinatesNearTheCode_CarryNoLocationWarning()
    {
        var leg = FirstLeg(Pickup(Location.FromCode("CNSZX", ShenzhenCity)));

        leg.Feature.Properties.LocationWarning.Should().BeNull();
        leg.From.Coordinate.Should().Be(ShenzhenCity);
    }

    [Fact]
    public void CoordinatesFarFromTheCode_AreUsedWithALocationWarning()
    {
        var leg = FirstLeg(Pickup(Location.FromCode("CNSZX", Alps)));

        leg.From.Coordinate.Should().Be(Alps);
        leg.From.Label.Should().Be("CNSZX");
        leg.Feature.Properties.LocationWarning.Should()
            .StartWith("Coordinates given for CNSZX lie ")
            .And.Contain("km from its listed position")
            .And.Contain("beyond the 50 km tolerance");
    }

    [Fact]
    public void LocationWarning_IsPrintedInTheTextReport()
    {
        var text = TradeRouterEngine.Default.CalculateMovement(Pickup(Location.FromCode("CNSZX", Alps))).ToText();

        text.Should().Contain("Leg 1 location = Coordinates given for CNSZX lie ");
        text.Should().NotContain("Leg 2 location");
    }

    [Fact]
    public void CoordinateCheck_CanBeTurnedOff()
    {
        var request = Pickup(Location.FromCode("CNSZX", Alps));
        request.CoordinateCodeToleranceKm = double.PositiveInfinity;

        FirstLeg(request).Feature.Properties.LocationWarning.Should().BeNull();
    }

    [Fact]
    public void CoordinateCheck_UsesTheConfiguredTolerance()
    {
        var request = Pickup(Location.FromCode("CNSZX", Alps));
        request.CoordinateCodeToleranceKm = 20_000.0;

        FirstLeg(request).Feature.Properties.LocationWarning.Should().BeNull();
    }

    [Fact]
    public void CustomCodeWithCoordinates_IsNotChecked()
    {
        var leg = FirstLeg(Pickup(Location.FromCode("SITE1", ShenzhenCity)));

        leg.Feature.Properties.LocationWarning.Should().BeNull();
        leg.From.Coordinate.Should().Be(ShenzhenCity);
    }

    [Fact]
    public void PlaceholderCoordinatesWithACode_AreIgnoredInFavourOfTheCode()
    {
        var leg = FirstLeg(Pickup(Location.FromCode("CNSZX", Placeholder, "Customer site")));

        leg.From.Coordinate.Should().Be(TradeRouterEngine.Default.Locate("CNSZX").Coordinate);
        leg.From.Name.Should().Be("Customer site");
        leg.Feature.Properties.LocationWarning.Should()
            .Be("Coordinates (0, 0) given for CNSZX are a placeholder and were ignored; the code's own position was used.");
        // The road leg is estimated from Shenzhen, not from the Atlantic.
        leg.Length.Should().BeLessThan(2_000.0);
    }

    [Fact]
    public void PlaceholderCoordinatesWithoutACode_AreUsedWithAWarning()
    {
        var leg = FirstLeg(Pickup(Location.FromCoordinate(Placeholder, "Customer site")));

        leg.From.Coordinate.Should().Be(Placeholder);
        leg.Feature.Properties.LocationWarning.Should()
            .Be("Coordinates (0, 0) are usually a placeholder for a missing position; they were used as given.");
    }

    [Fact]
    public void PlaceholderCoordinatesWithAnUnknownCode_AreUsedWithAWarning()
    {
        var leg = FirstLeg(Pickup(Location.FromCode("ZZZZZ", Placeholder)));

        leg.From.Coordinate.Should().Be(Placeholder);
        leg.From.Label.Should().Be("ZZZZZ");
        leg.Feature.Properties.LocationWarning.Should()
            .StartWith("Coordinates (0, 0) given for ZZZZZ are usually a placeholder, but ZZZZZ has no listed position");
    }

    [Fact]
    public void PlaceholderCoordinateOverride_IsIgnoredWithAWarning()
    {
        var request = Pickup(Location.FromCode("CNSZX"));
        request.Coordinates["CNSZX"] = Placeholder;

        var leg = FirstLeg(request);

        leg.From.Coordinate.Should().Be(TradeRouterEngine.Default.Locate("CNSZX").Coordinate);
        leg.Feature.Properties.LocationWarning.Should().Contain("MovementRequest.Coordinates gives (0, 0) for CNSZX");
    }

    [Fact]
    public void SamePlaceholderAtBothEnds_IsReportedOnce()
    {
        var request = new MovementRequest
        {
            Legs =
            [
                new MovementLeg(
                    Location.FromCode("CNSHG", Placeholder),
                    Location.FromCode("CNSHG", Placeholder),
                    TransportMode.Road,
                    LegKind.Pickup),
                new MovementLeg("CNSHG", "SGSIN", TransportMode.Sea)
            ]
        };

        FirstLeg(request).Feature.Properties.LocationWarning.Should()
            .Be("Coordinates (0, 0) given for CNSHG are a placeholder and were ignored; the code's own position was used.");
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidTolerance_IsRejected(double tolerance)
    {
        var request = Pickup(Location.FromCode("CNSZX"));
        request.CoordinateCodeToleranceKm = tolerance;

        var act = () => FirstLeg(request);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SamePlaceRoadLeg_IsExactlyZeroWithoutAnEstimateWarning()
    {
        var request = new MovementRequest
        {
            Legs =
            [
                new MovementLeg("CNSHG", "CNSHG", TransportMode.Road, LegKind.Pickup),
                new MovementLeg("CNSHG", "SGSIN", TransportMode.Sea)
            ],
            // No provider is configured, so a leg that needed routing would throw.
            RoadRoutingMode = RoadRoutingMode.RequireNetwork
        };

        var leg = FirstLeg(request);

        leg.Length.Should().Be(0.0);
        leg.DurationHours.Should().Be(0.0);
        leg.Feature.Properties.DistanceBasis.Should().Be("great_circle");
        leg.Feature.Properties.DistanceWarning.Should().BeNull();
        leg.Feature.Properties.LocationWarning.Should().BeNull();
    }

    [Fact]
    public void SuppliedRoadRoute_StillWinsForASamePlaceLeg()
    {
        var request = new MovementRequest
        {
            Legs = [new MovementLeg("CNSHG", "CNSHG", TransportMode.Road, LegKind.Pickup)]
        };
        request.RoadRouteOverrides[1] = new SuppliedRoute { DistanceKm = 12.0, Source = "yard" };

        var leg = FirstLeg(request);

        leg.Length.Should().Be(12.0);
        leg.Feature.Properties.DistanceBasis.Should().Be("supplied");
    }

    [Fact]
    public void SamePlaceRailLeg_IsExactlyZeroWithoutAWarning()
    {
        var request = new MovementRequest
        {
            Legs = [new MovementLeg("DEDUI", "DEDUI", TransportMode.Rail)]
        };

        var leg = FirstLeg(request);

        leg.Length.Should().Be(0.0);
        leg.Feature.Properties.DistanceWarning.Should().BeNull();
    }
}
