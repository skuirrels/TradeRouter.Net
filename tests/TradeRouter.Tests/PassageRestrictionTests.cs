using AwesomeAssertions;
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
    [InlineData(Passage.VolgaDon, "Volga-Don Canal")]
    [InlineData(Passage.Kiel, "Kiel Canal")]
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

    [Fact]
    public void ClosingSuezOnly_RotterdamToYokohama_DoesNotOpenNorthwest()
    {
        var viaOptions = TradeRoutes.Calculate("NLRTM", "JPYOK", new TradeRouterOptions
        {
            Restrictions = [Passage.Suez],
            ReturnPassages = true
        });
        var viaHelper = TradeRoutes.Calculate(
            At("NLRTM"),
            At("JPYOK"),
            restrictions: [Passage.Suez],
            returnPassages: true);

        foreach (var route in new[] { viaOptions, viaHelper })
        {
            route.Geometry.Should().NotBeNull();
            route.Properties.TraversedPassages.Should().NotContain([Passage.Northwest, Passage.Bering, Passage.Suez]);
            route.Properties.TraversedPassages.Should().Contain(Passage.Panama);
        }
    }

    [Fact]
    public void AllowNorthwest_RotterdamToYokohama_UsesArcticLanes()
    {
        var route = TradeRoutes.Calculate(
            At("NLRTM"),
            At("JPYOK"),
            returnPassages: true,
            allowNorthwest: true);

        route.Properties.TraversedPassages.Should().Contain(Passage.Northwest);
    }

    [Fact]
    public void AllowNorthwest_WhenNorthwestIsAlsoRestricted_KeepsItClosed()
    {
        var route = TradeRoutes.Calculate("NLRTM", "JPYOK", new TradeRouterOptions
        {
            Restrictions = [Passage.Northwest],
            AllowNorthwest = true,
            ReturnPassages = true
        });

        route.Properties.TraversedPassages.Should().NotContain(Passage.Northwest);
    }

    [Fact]
    public void GetClosedPassages_AddsNorthwestUnlessAllowed()
    {
        var options = new TradeRouterOptions { Restrictions = [Passage.Suez] };

        options.Restrictions.Should().BeEquivalentTo([Passage.Suez]);
        options.GetClosedPassages().Should().BeEquivalentTo([Passage.Suez, Passage.Northwest]);

        options.AllowNorthwest = true;
        var clone = options.Clone();
        clone.AllowNorthwest.Should().BeTrue();
        clone.GetClosedPassages().Should().BeEquivalentTo([Passage.Suez]);
    }

    [Fact]
    public void VolgaDon_IsOpenByDefaultAndReportedOutOfTheCaspian()
    {
        var route = TradeRoutes.Calculate("AZBAK", "RUNVS", new TradeRouterOptions { ReturnPassages = true }); // Baku to Novorossiysk

        new TradeRouterOptions().GetClosedPassages().Should().NotContain(Passage.VolgaDon);
        route.Geometry.Should().NotBeNull();
        route.Properties.TraversedPassages.Should().BeEquivalentTo([Passage.VolgaDon]);
    }

    [Fact]
    public void VolgaDon_Closed_LeavesNoRouteOutOfTheCaspian()
    {
        var options = new TradeRouterOptions { Restrictions = [Passage.VolgaDon] };

        var route = TradeRoutes.Calculate("AZBAK", "RUNVS", options);

        route.Geometry.Should().BeNull();
        route.Properties.Length.Should().Be(0.0);
        var movement = () => TradeRoutes.CalculateMovement("Port AZBAK to Port RUNVS Sea", seaOptions: options);
        movement.Should().Throw<InvalidOperationException>().WithMessage("*no sea route between AZBAK and RUNVS*");
    }

    [Theory]
    [InlineData("AZBAK", "RUASF")] // Baku to Astrakhan, up the lower Volga
    [InlineData("RURND", "RUNVS")] // Rostov-on-Don to Novorossiysk, down the lower Don
    [InlineData("AZBAK", "KZAAU")] // Baku to Aktau
    public void VolgaDon_Closed_KeepsTheLowerVolgaAndDonOnTheirOwnSeas(string origin, string destination)
    {
        var open = TradeRoutes.Calculate(origin, destination, new TradeRouterOptions { ReturnPassages = true });
        var closed = TradeRoutes.Calculate(origin, destination, new TradeRouterOptions
        {
            Restrictions = [Passage.VolgaDon],
            ReturnPassages = true
        });

        open.Properties.TraversedPassages.Should().BeEmpty();
        closed.Geometry.Should().NotBeNull();
        closed.Properties.Length.Should().Be(open.Properties.Length);
    }

    [Theory]
    [InlineData("DEHAM", "DKFRC", 350, 450, 956.3)] // Hamburg to Fredericia
    [InlineData("DEHAM", "PLSZZ", 450, 650, 1177.8)] // Hamburg to Szczecin
    public void Kiel_IsOpenByDefaultAndShortensElbeToBalticRoutes(string origin, string destination, double minKm, double maxKm, double skagenKm)
    {
        var open = TradeRoutes.Calculate(origin, destination, new TradeRouterOptions { ReturnPassages = true });
        var closed = TradeRoutes.Calculate(origin, destination, new TradeRouterOptions
        {
            Restrictions = [Passage.Kiel],
            ReturnPassages = true
        });

        new TradeRouterOptions().GetClosedPassages().Should().NotContain(Passage.Kiel);
        open.Properties.TraversedPassages.Should().BeEquivalentTo([Passage.Kiel]);
        open.Properties.Length.Should().BeInRange(minKm, maxKm);
        open.Geometry!.Positions.Max(position => position[1]).Should().BeLessThan(56.0);

        // Closed, the route goes back round Skagen, at 57.7 N, with its 2.6.1 length.
        closed.Properties.TraversedPassages.Should().BeEmpty();
        closed.Properties.Length.Should().BeApproximately(skagenKm, 0.1);
        closed.Geometry!.Positions.Max(position => position[1]).Should().BeGreaterThan(57.5);
    }

    [Theory]
    [InlineData("NLRTM", "SGSIN", 15521.4)] // Rotterdam to Singapore
    [InlineData("DEHAM", "USNYC", 6479.9)] // Hamburg to New York
    [InlineData("DEHAM", "GBFXT", 688.0)] // Hamburg to Felixstowe
    [InlineData("DEHAM", "NLRTM", 574.9)] // Hamburg to Rotterdam
    [InlineData("DEHAM", "NOBGO", 1010.3)] // Hamburg to Bergen
    [InlineData("PLGDN", "FIHEL", 791.7)] // Gdansk to Helsinki
    [InlineData("DKAAR", "SESTO", 1007.3)] // Aarhus to Stockholm
    [InlineData("SEGOT", "NOOSL", 278.7)] // Gothenburg to Oslo
    public void Kiel_LeavesRoutesThatDoNotUseItUnchanged(string origin, string destination, double expectedKm)
    {
        var open = TradeRoutes.Calculate(origin, destination, new TradeRouterOptions { ReturnPassages = true });
        var closed = TradeRoutes.Calculate(origin, destination, new TradeRouterOptions
        {
            Restrictions = [Passage.Kiel],
            ReturnPassages = true
        });

        open.Properties.TraversedPassages.Should().NotContain(Passage.Kiel);
        open.Properties.Length.Should().BeApproximately(expectedKm, 0.1);
        closed.Properties.Length.Should().Be(open.Properties.Length);
    }

    [Theory]
    [InlineData("DEBRB")] // Brunsbuettel, at the Elbe end
    [InlineData("DEKEL")] // Kiel, at the Baltic end
    [InlineData("DEHUS")] // Husum, on the North Sea
    [InlineData("DETOE")] // Toenning, on the North Sea
    [InlineData("DEECK")] // Eckernfoerde, on the Baltic
    [InlineData("DESLS")] // Schleswig, on the Baltic
    public void Kiel_Closed_KeepsPortsBesideTheCanalOnTheirOwnSeas(string port)
    {
        var options = new TradeRouterOptions { Restrictions = [Passage.Kiel], ReturnPassages = true };

        var toRotterdam = TradeRoutes.Calculate(port, "NLRTM", options);
        var toGdansk = TradeRoutes.Calculate(port, "PLGDN", options);

        toRotterdam.Geometry.Should().NotBeNull();
        toGdansk.Geometry.Should().NotBeNull();
        toRotterdam.Properties.TraversedPassages.Should().BeEmpty();
        toGdansk.Properties.TraversedPassages.Should().BeEmpty();
    }

    [Fact]
    public void Kiel_Closed_LeavesRendsburgOnTheCanalWithoutARoute()
    {
        var open = TradeRoutes.Calculate("DEREN", "NLRTM", new TradeRouterOptions { ReturnPassages = true });
        var closed = TradeRoutes.Calculate("DEREN", "NLRTM", new TradeRouterOptions { Restrictions = [Passage.Kiel] });

        open.Properties.TraversedPassages.Should().BeEquivalentTo([Passage.Kiel]);
        closed.Geometry.Should().BeNull();
    }

    private static Coordinate At(string code) => TradeRoutes.Locate(code).Coordinate;
}
