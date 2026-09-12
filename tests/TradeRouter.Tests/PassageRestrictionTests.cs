using FluentAssertions;
using TradeRouter.Common;
using TradeRouter.Passages;
using Xunit;

namespace TradeRouter.Tests;

public class PassageRestrictionTests
{
    [Theory]
    [InlineData(Passage.Babalmandab, "Bab-el-Mandeb")]
    [InlineData(Passage.Ormuz, "Hormuz")]
    [InlineData(Passage.SouthAfrica, "Cape of Good Hope")]
    [InlineData(Passage.Chili, "Magellan Strait")]
    public void DisplayName_UsesRecognizableGeographicNames(string passage, string expected)
    {
        Passage.GetDisplayName(passage).Should().Be(expected);
    }

    [Fact]
    public void ValidPassages_Filter_ShouldOnlyKeepKnownPassages()
    {
        var input = new[] { "suez", "invalid_strait", "PANAMA", "gibraltar", "suez", "dardanelles" };
        var valid = Passage.FilterValidPassages(input);

        valid.Should().Contain(["suez", "panama", "gibraltar", "dardanelles"]);
        valid.Should().NotContain("invalid_strait");
        valid.Count.Should().Be(4);
    }

    [Fact]
    public void TestPassages_SuezRoute_TraversedPassagesMatch()
    {
        var origin = new Coordinate(52.99, 25.01);
        var dest = new Coordinate(-61.87, 17.15);

        var route = TradeRoutes.Calculate(
            origin,
            dest,
            appendOrigDest: true,
            restrictions: [Passage.Northwest, Passage.Chili],
            returnPassages: true);

        route.Properties.TraversedPassages.Should().NotBeNull();
        var passages = route.Properties.TraversedPassages!;
        passages.Should().BeEquivalentTo([Passage.Suez, Passage.Ormuz, Passage.Babalmandab, Passage.Gibraltar]);
    }

    [Fact]
    public void TestRestrictionPassage_AvoidSuez_RoutesViaSouthAfrica()
    {
        var origin = new Coordinate(52.99, 25.01);
        var dest = new Coordinate(-61.87, 17.15);

        var route = TradeRoutes.Calculate(
            origin,
            dest,
            appendOrigDest: true,
            restrictions: [Passage.Suez],
            returnPassages: true);

        route.Properties.TraversedPassages.Should().NotBeNull();
        var passages = route.Properties.TraversedPassages!;
        passages.Should().BeEquivalentTo([Passage.Ormuz, Passage.SouthAfrica]);
    }

    [Fact]
    public void TestTransPacific_ViaPanama()
    {
        var origin = new Coordinate(140.02, 35.51);
        var dest = new Coordinate(-97.36, 27.81);

        var route = TradeRoutes.Calculate(
            origin,
            dest,
            appendOrigDest: true,
            returnPassages: true);

        route.Properties.TraversedPassages.Should().NotBeNull();
        var passages = route.Properties.TraversedPassages!;
        passages.Should().Contain(Passage.Panama);
    }

    [Fact]
    public void TestRestrictedPaths_WhenBlocked_ReturnsNullGeometryAndZeroLength()
    {
        // Route from Singapore to Piraeus with both Suez and Gibraltar restricted
        var origin = new Coordinate(103.85457, 1.25760);
        var dest = new Coordinate(23.62904, 37.94056);

        var route = TradeRoutes.Calculate(
            origin,
            dest,
            restrictions: [Passage.Suez, Passage.Gibraltar],
            returnPassages: true);

        route.Geometry.Should().BeNull();
        route.Properties.Length.Should().Be(0.0);
        route.Properties.DurationHours.Should().Be(0.0);
    }
}
