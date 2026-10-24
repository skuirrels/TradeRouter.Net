using System.Reflection;
using AwesomeAssertions;
using TradeRouter.Common;
using TradeRouter.GeoJson;
using TradeRouter.Passages;
using TradeRouter.SeaAreas;
using Xunit;

namespace TradeRouter.Tests;

public class SeaAreaTests
{
    // Supply Chain Intelligence sampled each path every 5 km against the polygons; the change request allows ±25 km.
    private const double AcceptanceToleranceKm = 25.0;

    public static TheoryData<string, string, string[], (string Id, double EnterKm, double ExitKm)[]> AcceptanceRoutes => new()
    {
        {
            "SAJED", "PKKHI", [Passage.Babalmandab],
            [(SeaArea.RedSea, 10, 1157), (SeaArea.GulfOfAden, 1162, 2093), (SeaArea.ArabianSea, 2098, 4114)]
        },
        {
            "SGSIN", "NLRTM", [Passage.Malacca, Passage.Babalmandab, Passage.Suez, Passage.Gibraltar],
            [
                (SeaArea.StraitOfSingapore, 4, 52), (SeaArea.StraitOfMalacca, 57, 1081), (SeaArea.AndamanSea, 1086, 1267),
                (SeaArea.BayOfBengal, 1272, 1898), (SeaArea.IndianOcean, 1903, 2854), (SeaArea.LaccadiveSea, 2859, 3771),
                (SeaArea.ArabianSea, 3776, 6119), (SeaArea.GulfOfAden, 6124, 6994), (SeaArea.RedSea, 6999, 8928),
                (SeaArea.MediterraneanSea, 9394, 12628), (SeaArea.NorthAtlanticOcean, 12973, 14624),
                (SeaArea.BayOfBiscay, 14629, 14685), (SeaArea.EnglishChannel, 14690, 15285), (SeaArea.NorthSea, 15291, 15497)
            ]
        },
        {
            "CNSHA", "USLAX", [],
            [
                (SeaArea.EastChinaSea, 71, 468), (SeaArea.YellowSea, 474, 578), (SeaArea.KoreaStrait, 584, 993),
                (SeaArea.SeaOfJapan, 998, 2072), (SeaArea.NorthPacificOcean, 2314, 10693)
            ]
        },
        {
            // One North Atlantic entry, merged across the unlisted Sargasso Sea hole.
            "USHOU", "BEANR", [],
            [
                (SeaArea.GulfOfMexico, 34, 1711), (SeaArea.StraitsOfFlorida, 1713, 1896), (SeaArea.NorthAtlanticOcean, 1902, 8538),
                (SeaArea.EnglishChannel, 8544, 9149), (SeaArea.NorthSea, 9154, 9346)
            ]
        },
        {
            // The change request lists only the Bosporus; the route also passes the Dardanelles, tagged since 2.0.0.
            "TRIST", "EGPSD", [Passage.Bosporus, Passage.Dardanelles],
            [(SeaArea.AegeanSea, 270, 672), (SeaArea.MediterraneanSea, 797, 1472)]
        }
    };

    [Fact]
    public void Catalogue_HoldsEveryConstantOnce()
    {
        var constants = typeof(SeaArea)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        SeaArea.Catalogue.Should().HaveCount(42);
        SeaArea.Catalogue.Select(area => area.Id).Should().BeEquivalentTo(constants);
        SeaArea.Catalogue.Select(area => area.NeId).Should().OnlyHaveUniqueItems();
        SeaArea.ValidSeaAreas.Should().BeEquivalentTo(constants);
    }

    [Fact]
    public void Catalogue_IdsAreLowerSnakeCaseEnglishNames()
    {
        foreach (var area in SeaArea.Catalogue)
        {
            area.Id.Should().Be(area.Name.ToLowerInvariant().Replace(' ', '_'));
            area.Wikidata.Should().MatchRegex("^Q[0-9]+$");
        }
    }

    [Fact]
    public void Get_ReturnsCatalogueEntryAndRejectsUnknownIds()
    {
        var redSea = SeaArea.Get("RED_SEA");

        redSea.Should().Be(new SeaAreaInfo(SeaArea.RedSea, "Red Sea", "Q23406", 1159115521));
        var act = () => SeaArea.Get("sargasso_sea");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [MemberData(nameof(AcceptanceRoutes))]
    public void AcceptanceRoute_ReportsSeaAreasAndPassages(
        string origin,
        string destination,
        string[] expectedPassages,
        (string Id, double EnterKm, double ExitKm)[] expectedAreas)
    {
        var route = TradeRouterEngine.Default.CalculateRoute(origin, destination, new TradeRouterOptions
        {
            AppendOriginDestination = true,
            ReturnPassages = true,
            ReturnSeaAreas = true
        });

        route.Properties.TraversedPassages.Should().BeEquivalentTo(expectedPassages);
        var areas = route.Properties.TraversedSeaAreas!;
        areas.Select(area => area.Id).Should().Equal(expectedAreas.Select(area => area.Id));
        for (int i = 0; i < expectedAreas.Length; i++)
        {
            areas[i].EnterKm.Should().BeApproximately(expectedAreas[i].EnterKm, AcceptanceToleranceKm, $"{areas[i].Id} entry");
            areas[i].ExitKm.Should().BeApproximately(expectedAreas[i].ExitKm, AcceptanceToleranceKm, $"{areas[i].Id} exit");
            areas[i].Name.Should().Be(SeaArea.Get(areas[i].Id).Name);
            areas[i].Wikidata.Should().Be(SeaArea.Get(areas[i].Id).Wikidata);
        }

        areas[^1].ExitKm.Should().BeLessThanOrEqualTo(route.Properties.Length + 1e-6);
    }

    [Fact]
    public void AntimeridianRoute_GivesSameAreasWithAndWithoutTheMultiLineStringSplit()
    {
        var route = TradeRouterEngine.Default.CalculateRoute("CNSHA", "USLAX", new TradeRouterOptions
        {
            AppendOriginDestination = true,
            ReturnSeaAreas = true
        });
        var split = route.Geometry.Should().BeOfType<GeoJsonMultiLineString>().Subject;

        var fromSplit = SeaAreaIndex.Default.Traverse(split.Coordinates
            .SelectMany(segment => segment)
            .Select(Coordinate.FromArray)
            .ToList());

        var continuous = route.Properties.TraversedSeaAreas!;
        fromSplit.Select(area => area.Id).Should().Equal(continuous.Select(area => area.Id));
        for (int i = 0; i < continuous.Count; i++)
        {
            // The split adds a vertex on the antimeridian, a few kilometres off the great circle.
            fromSplit[i].EnterKm.Should().BeApproximately(continuous[i].EnterKm, 5.0);
            fromSplit[i].ExitKm.Should().BeApproximately(continuous[i].ExitKm, 5.0);
        }
    }

    [Fact]
    public void ReturnSeaAreasOff_LeavesRouteOutputUnchanged()
    {
        var byDefault = TradeRouterEngine.Default.CalculateRoute("SGSIN", "NLRTM");
        var switchedOff = TradeRouterEngine.Default.CalculateRoute("SGSIN", "NLRTM", new TradeRouterOptions { ReturnSeaAreas = false });

        switchedOff.Properties.TraversedSeaAreas.Should().BeNull();
        switchedOff.ToJson().Should().Be(byDefault.ToJson());
        switchedOff.ToJson().Should().NotContain("traversed_sea_areas");
    }

    [Fact]
    public void SeaAreaDistances_StayInKilometresWhateverTheRouteUnits()
    {
        var km = TradeRouterEngine.Default.CalculateRoute("SAJED", "PKKHI", new TradeRouterOptions { ReturnSeaAreas = true });
        var naut = TradeRouterEngine.Default.CalculateRoute("SAJED", "PKKHI", new TradeRouterOptions
        {
            ReturnSeaAreas = true,
            Units = DistanceUnit.NauticalMiles
        });

        naut.Properties.TraversedSeaAreas.Should().BeEquivalentTo(km.Properties.TraversedSeaAreas);
        // Karachi's port position lies just outside the Arabian Sea polygon, so the last exit falls short of the end.
        naut.Properties.TraversedSeaAreas![^1].ExitKm.Should().BeInRange(km.Properties.Length - 5.0, km.Properties.Length);
    }

    [Fact]
    public void MovementSeaLeg_CountsSeaAreasFromTheStartOfTheLeg()
    {
        var result = TradeRoutes.CalculateMovement(
            """
            Pickup GBLGW to Port GBFXT Road
            Port GBFXT to Port CNSHG Sea
            """,
            seaOptions: new TradeRouterOptions { ReturnSeaAreas = true, Units = DistanceUnit.NauticalMiles });

        result.Legs[0].Feature.Properties.TraversedSeaAreas.Should().BeNull();
        var sea = result.Legs[1].Feature.Properties;
        var areas = sea.TraversedSeaAreas!;
        areas[0].Id.Should().Be(SeaArea.NorthSea);
        areas[0].EnterKm.Should().BeLessThan(50.0);
        areas[^1].Id.Should().Be(SeaArea.EastChinaSea);
        double legKm = sea.Length * 1.852;
        areas[^1].ExitKm.Should().BeInRange(legKm - 100.0, legKm + 0.001);
    }

    [Fact]
    public void Traverse_MergesAReturnThroughAnUnlistedHole()
    {
        var index = new SeaAreaIndex([Area("ocean", Square(0, 0, 10, 10), Square(4, 4, 6, 6))]);

        var areas = index.Traverse([new Coordinate(1, 5), new Coordinate(9, 5)]);

        areas.Should().ContainSingle();
        areas[0].EnterKm.Should().Be(0.0);
        areas[0].ExitKm.Should().BeApproximately(Haversine.DistanceKm(new Coordinate(1, 5), new Coordinate(9, 5)), 1e-6);
    }

    [Fact]
    public void Traverse_ListsAReturnSeparatelyWhenAnotherListedAreaLiesBetween()
    {
        var index = new SeaAreaIndex(
        [
            Area("ocean", Square(0, 0, 10, 10), Square(4, 4, 6, 6)),
            Area("sea", Square(4, 4, 6, 6))
        ]);

        var areas = index.Traverse([new Coordinate(1, 5), new Coordinate(9, 5)]);

        areas.Select(area => area.Id).Should().Equal("ocean", "sea", "ocean");
        areas[0].ExitKm.Should().BeApproximately(areas[1].EnterKm, 1e-6);
        areas[1].ExitKm.Should().BeApproximately(areas[2].EnterKm, 1e-6);
        areas[1].ExitKm.Should().BeApproximately(Haversine.DistanceKm(new Coordinate(1, 5), new Coordinate(6, 5)), 0.05);
    }

    [Fact]
    public void Traverse_ListsAReturnSeparatelyWhenAnOverlappingAreaSpansTheGap()
    {
        // The route is already inside "sea" when it leaves "ocean" for the hole, and still inside it on return.
        var index = new SeaAreaIndex(
        [
            Area("ocean", Square(0, 0, 10, 10), Square(4, 4, 6, 6)),
            Area("sea", Square(3, 4, 7, 6))
        ]);

        var areas = index.Traverse([new Coordinate(1, 5), new Coordinate(9, 5)]);

        areas.Select(area => area.Id).Should().Equal("ocean", "sea", "ocean");
    }

    [Fact]
    public void Traverse_ListsEveryOverlappingAreaAndIgnoresUnlistedWater()
    {
        var index = new SeaAreaIndex(
        [
            Area("wide", Square(0, 0, 10, 10)),
            Area("narrow", Square(3, -5, 7, 15))
        ]);

        var areas = index.Traverse([new Coordinate(-5, 5), new Coordinate(15, 5)]);

        areas.Select(area => area.Id).Should().Equal("wide", "narrow");
        areas[0].EnterKm.Should().BeApproximately(Haversine.DistanceKm(new Coordinate(-5, 5), new Coordinate(0, 5)), 0.05);
        areas[1].EnterKm.Should().BeApproximately(Haversine.DistanceKm(new Coordinate(-5, 5), new Coordinate(3, 5)), 0.05);
        areas[1].ExitKm.Should().BeLessThan(areas[0].ExitKm);
        index.Traverse([new Coordinate(20, 20), new Coordinate(25, 25)]).Should().BeEmpty();
    }

    [Fact]
    public void Traverse_FollowsAnAreaSplitAtTheAntimeridianAsOneEntry()
    {
        var index = new SeaAreaIndex([Area("pacific", Square(170, 0, 180, 10), Square(-180, 0, -170, 10))]);

        var continuous = index.Traverse([new Coordinate(175, 5), new Coordinate(185, 5)]);
        var wrapped = index.Traverse([new Coordinate(175, 5), new Coordinate(-175, 5)]);

        double lengthKm = Haversine.DistanceKm(new Coordinate(175, 5), new Coordinate(-175, 5));
        continuous.Should().ContainSingle().Which.ExitKm.Should().BeApproximately(lengthKm, 1e-6);
        wrapped.Should().ContainSingle().Which.ExitKm.Should().BeApproximately(lengthKm, 1e-6);
        wrapped[0].EnterKm.Should().Be(0.0);
    }

    private static (SeaAreaInfo Info, IReadOnlyList<double[]> Rings) Area(string id, params double[][] rings) =>
        (new SeaAreaInfo(id, id, "Q1", id.GetHashCode()), rings);

    private static double[] Square(double minX, double minY, double maxX, double maxY) =>
        [minX, minY, maxX, minY, maxX, maxY, minX, maxY, minX, minY];
}
