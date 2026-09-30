using AwesomeAssertions;
using TradeRouter.Common;
using Xunit;

namespace TradeRouter.Tests;

public class UnitConversionTests
{
    [Theory]
    [InlineData("km", DistanceUnit.Km, 0.001)]
    [InlineData("m", DistanceUnit.Meters, 1.0)]
    [InlineData("mi", DistanceUnit.Miles, 0.000621371192)]
    [InlineData("ft", DistanceUnit.Feet, 3.28084)]
    [InlineData("in", DistanceUnit.Inches, 39.370)]
    [InlineData("deg", DistanceUnit.Degrees, 1.0 / 111325.0)]
    [InlineData("cen", DistanceUnit.Centimeters, 100.0)]
    [InlineData("rad", DistanceUnit.Radians, 1.0 / 6371008.8)]
    [InlineData("naut", DistanceUnit.NauticalMiles, 0.000539956803)]
    [InlineData("nm", DistanceUnit.NauticalMiles, 0.000539956803)]
    [InlineData("yd", DistanceUnit.Yards, 1.0936132983377078)]
    public void UnitParsingAndFactors_ShouldMatchExpected(string unitStr, DistanceUnit expectedUnit, double expectedFactor)
    {
        var parsed = DistanceUnitExtensions.Parse(unitStr);
        parsed.Should().Be(expectedUnit);

        double factor = parsed.GetConversionFactorFromMeters();
        factor.Should().BeApproximately(expectedFactor, 1e-6);
    }

    [Theory]
    [InlineData(DistanceUnit.Km, 1.852)]
    [InlineData(DistanceUnit.Meters, 1852.0)]
    [InlineData(DistanceUnit.Miles, 1.15078)]
    [InlineData(DistanceUnit.NauticalMiles, 1.0)]
    public void SpeedCoefficient_ShouldConvertKnotsCorrectly(DistanceUnit unit, double expectedCoef)
    {
        double coef = unit.GetSpeedCoefficient();
        coef.Should().BeApproximately(expectedCoef, 1e-4);
    }

    [Fact]
    public void HaversineDistance_KnownPoints_ShouldBeAccurate()
    {
        // London (0.1278 W, 51.5074 N) to Paris (2.3522 E, 48.8566 N) ~ 343 km
        var london = new Coordinate(-0.1278, 51.5074);
        var paris = new Coordinate(2.3522, 48.8566);

        double distKm = Haversine.Distance(london, paris, DistanceUnit.Km);
        distKm.Should().BeInRange(340.0, 350.0);

        double distNaut = Haversine.Distance(london, paris, DistanceUnit.NauticalMiles);
        distNaut.Should().BeInRange(180.0, 190.0);

        double distMiles = Haversine.Distance(london, paris, DistanceUnit.Miles);
        distMiles.Should().BeInRange(210.0, 220.0);
    }

    [Fact]
    public void DurationHours_Calculation_ShouldBeAccurate()
    {
        // 240 nautical miles at 24 knots should take exactly 10 hours
        double duration = Haversine.CalculateDurationHours(24.0, 240.0, DistanceUnit.NauticalMiles);
        duration.Should().BeApproximately(10.0, 1e-4);

        // 1852 km at 24 knots (~44.448 km/h) should take ~41.67 hours
        double kmDuration = Haversine.CalculateDurationHours(24.0, 1852.0, DistanceUnit.Km);
        kmDuration.Should().BeApproximately(1852.0 / (24.0 * 1.852), 1e-4);
    }

    [Fact]
    public void HaversineDistance_IdenticalCoordinates_ReturnsZeroWithoutNaN()
    {
        var pt = new Coordinate(121.47, 31.23);
        double dist = Haversine.Distance(pt, pt);
        dist.Should().Be(0.0);
        double.IsNaN(dist).Should().BeFalse();

        double distKm = Haversine.DistanceKm(pt, pt);
        distKm.Should().Be(0.0);
        double.IsNaN(distKm).Should().BeFalse();
    }

    [Fact]
    public void UnknownDistanceUnit_IsRejectedByPublicConversionHelpers()
    {
        var invalid = (DistanceUnit)999;

        FluentActions.Invoking(() => invalid.ToUnitString()).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => invalid.GetConversionFactorFromMeters()).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => invalid.GetSpeedCoefficient()).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void HaversineDistance_RejectsInvalidCoordinates()
    {
        var act = () => Haversine.DistanceKm(new Coordinate(0, 91), new Coordinate(0, 0));

        act.Should().Throw<ArgumentException>().WithMessage("*Latitude*");
    }

    public static TheoryData<DistanceUnit> AllUnits => new(Enum.GetValues<DistanceUnit>());

    [Theory]
    [MemberData(nameof(AllUnits))]
    public void UnitString_RoundTripsThroughParse(DistanceUnit unit)
    {
        DistanceUnitExtensions.Parse(unit.ToUnitString()).Should().Be(unit);
    }

    [Theory]
    [MemberData(nameof(AllUnits))]
    public void SpeedCoefficient_MatchesOneNauticalMileInTheSameUnit(DistanceUnit unit)
    {
        double oneNauticalMile = 1852.0 * unit.GetConversionFactorFromMeters();

        unit.GetSpeedCoefficient().Should().BeApproximately(oneNauticalMile, oneNauticalMile * 1e-5);
    }

    [Theory]
    [InlineData(" Kilometers ", DistanceUnit.Km)]
    [InlineData("METER", DistanceUnit.Meters)]
    [InlineData("miles", DistanceUnit.Miles)]
    [InlineData("foot", DistanceUnit.Feet)]
    [InlineData("inches", DistanceUnit.Inches)]
    [InlineData("degree", DistanceUnit.Degrees)]
    [InlineData("cm", DistanceUnit.Centimeters)]
    [InlineData("radians", DistanceUnit.Radians)]
    [InlineData("NauticalMiles", DistanceUnit.NauticalMiles)]
    [InlineData("nautical", DistanceUnit.NauticalMiles)]
    [InlineData("yard", DistanceUnit.Yards)]
    [InlineData(null, DistanceUnit.Km)]
    [InlineData("", DistanceUnit.Km)]
    [InlineData("   ", DistanceUnit.Km)]
    public void Parse_AcceptsAliasesAndDefaultsBlankToKilometres(string? unitStr, DistanceUnit expected)
    {
        DistanceUnitExtensions.Parse(unitStr).Should().Be(expected);
    }

    [Fact]
    public void Parse_RejectsUnknownUnitAndListsValidOnes()
    {
        var act = () => DistanceUnitExtensions.Parse("furlong");

        act.Should().Throw<ArgumentException>().WithMessage("Unsupported distance unit: 'furlong'. Valid units:*");
    }

    [Fact]
    public void OptionsUnitString_ReadsAndWritesUnits()
    {
        var options = new TradeRouterOptions { UnitString = "yd" };

        options.Units.Should().Be(DistanceUnit.Yards);
        options.UnitString.Should().Be("yd");
        FluentActions.Invoking(() => options.UnitString = "furlong").Should().Throw<ArgumentException>();
    }

    [Theory]
    [MemberData(nameof(AllUnits))]
    public void RouteLengthAndDuration_AreConsistentAcrossUnits(DistanceUnit unit)
    {
        var origin = new Coordinate(5.333333, 43.333333);
        var destination = new Coordinate(18.366667, -33.916667);
        var inKm = TradeRoutes.Calculate(origin, destination, new TradeRouterOptions());

        var inUnit = TradeRoutes.Calculate(origin, destination, new TradeRouterOptions { Units = unit });

        double expectedLength = inKm.Properties.Length * 1000.0 * unit.GetConversionFactorFromMeters();
        inUnit.Properties.Units.Should().Be(unit.ToUnitString());
        inUnit.Properties.Length.Should().BeApproximately(expectedLength, expectedLength * 1e-9);
        inUnit.Properties.DurationHours.Should().BeApproximately(inKm.Properties.DurationHours, inKm.Properties.DurationHours * 1e-5);
    }
}
