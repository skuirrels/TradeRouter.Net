using System.Text.Json;
using FluentAssertions;
using TradeRouter.Common;
using TradeRouter.Movements;
using Xunit;

namespace TradeRouter.Tests;

public class MovementTests
{
    private const string ShanghaiMovement = """
        Pickup GBLGW to Port GBFXT Road
        Port GBFXT to Port CNSHG Sea
        Delivery from port CNSHG to place CNSHZ Road
        """;

    // AUMRS is Melrose, an inland South Australian town 800 km from Melbourne, so its delivery leg is by road.
    private const string MelbourneMovement = """
        Pickup GBLGW to Port GBFXT Road
        Port GBFXT to Port SGSIN Sea
        Port SGSIN to Port AUMEL Sea
        Delivery from port AUMEL to place AUMRS Road
        """;

    // Real coordinates for codes UNECE publishes without any, used only to exercise caller overrides.
    private static readonly Dictionary<string, Coordinate> ExtraPlaces = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GBLON"] = new Coordinate(-0.1276, 51.5072),   // London
        ["AUDND"] = new Coordinate(145.2100, -37.9900)  // Dandenong
    };

    [Fact]
    public void Parser_ParsesPickupMainAndDeliveryLines()
    {
        var legs = MovementParser.Parse(ShanghaiMovement);

        legs.Should().HaveCount(3);

        legs[0].Kind.Should().Be(LegKind.Pickup);
        legs[0].Mode.Should().Be(TransportMode.Road);
        legs[0].From.Code.Should().Be("GBLGW");
        legs[0].To.Code.Should().Be("GBFXT");

        legs[1].Kind.Should().Be(LegKind.Main);
        legs[1].Mode.Should().Be(TransportMode.Sea);
        legs[1].From.Code.Should().Be("GBFXT");
        legs[1].To.Code.Should().Be("CNSHG");

        legs[2].Kind.Should().Be(LegKind.Delivery);
        legs[2].Mode.Should().Be(TransportMode.Road);
        legs[2].From.Code.Should().Be("CNSHG");
        legs[2].To.Code.Should().Be("CNSHZ");
    }

    [Fact]
    public void Parser_RejectsMalformedLineWithLineNumber()
    {
        var text = "Port GBFXT to Port SGSIN Sea\nthis is not a leg";

        var act = () => MovementParser.Parse(text);

        act.Should().Throw<FormatException>().WithMessage("Line 2*");
    }

    [Fact]
    public void Movement_ThreeLegs_RoadIsStraightAndSeaIsRouted()
    {
        var result = TradeRoutes.CalculateMovement(ShanghaiMovement);

        result.Legs.Should().HaveCount(3);
        result.Units.Should().Be("km");

        var pickup = result.Legs[0];
        pickup.Leg.Mode.Should().Be(TransportMode.Road);
        pickup.Feature.Geometry!.Coordinates.Should().HaveCount(2);
        pickup.Length.Should().BeInRange(160.0, 210.0);
        pickup.Feature.Properties.DistanceBasis.Should().Be("circuity_estimate");
        pickup.DurationHours.Should().BeApproximately(pickup.Length / 60.0, 1e-6);
        pickup.To.Port.Should().NotBeNull();
        pickup.To.Port!.Name.Should().Be("Felixstowe");

        var main = result.Legs[1];
        main.Leg.Mode.Should().Be(TransportMode.Sea);
        main.Feature.Geometry!.Coordinates.Count.Should().BeGreaterThan(20);
        main.Feature.Properties.PortOrigin!.PortCode.Should().Be("GBFXT");
        main.To.Label.Should().Be("CNSHG");
        main.To.Source.Should().Be("unlocode", "UN/LOCODE says CNSHG is Shanghai Pt while the port list says Sanshan, so UN/LOCODE wins");
        main.To.Name.Should().Be("Shanghai Pt");
        main.To.Port.Should().BeNull();
        main.Feature.Properties.PortDest.Should().BeNull();
        main.Length.Should().BeInRange(19000.0, 20500.0, "Felixstowe to Shanghai via Suez");

        var delivery = result.Legs[2];
        delivery.Leg.Kind.Should().Be(LegKind.Delivery);
        delivery.Length.Should().BeGreaterThan(0.0);

        result.TotalLength.Should().BeApproximately(result.Legs.Sum(l => l.Length), 1e-6);
        result.TotalDurationHours.Should().BeApproximately(result.Legs.Sum(l => l.DurationHours), 1e-6);
        result.LengthByMode[TransportMode.Sea].Should().BeApproximately(main.Length, 1e-6);
        result.LengthByMode[TransportMode.Road].Should().BeApproximately(pickup.Length + delivery.Length, 1e-6);
    }

    [Fact]
    public void Movement_FourLegs_ProducesFeatureCollectionWithLegMetadata()
    {
        var result = TradeRoutes.CalculateMovement(MelbourneMovement);

        result.Legs.Should().HaveCount(4);
        result.Legs.Count(l => l.Leg.Mode == TransportMode.Sea).Should().Be(2);

        string json = result.ToJson();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("type").GetString().Should().Be("FeatureCollection");
        var features = root.GetProperty("features");
        features.GetArrayLength().Should().Be(4);

        var second = features[1].GetProperty("properties");
        second.GetProperty("leg").GetInt32().Should().Be(2);
        second.GetProperty("mode").GetString().Should().Be("sea");
        second.GetProperty("kind").GetString().Should().Be("main");
        second.GetProperty("from").GetString().Should().Be("GBFXT");
        second.GetProperty("to").GetString().Should().Be("SGSIN");

        root.GetProperty("properties").GetProperty("legs").GetInt32().Should().Be(4);
        root.GetProperty("properties").GetProperty("total_length").GetDouble().Should().BeApproximately(result.TotalLength, 1e-6);
    }

    [Fact]
    public void Movement_ConsecutiveLegsJoinEndToEnd()
    {
        var result = TradeRoutes.CalculateMovement(MelbourneMovement);

        for (int i = 0; i < result.Legs.Count - 1; i++)
        {
            var end = result.Legs[i].Feature.Geometry!.Coordinates[^1];
            var start = result.Legs[i + 1].Feature.Geometry!.Coordinates[0];
            start[0].Should().BeApproximately(end[0], 1e-9, $"leg {i + 2} must start where leg {i + 1} ends");
            start[1].Should().BeApproximately(end[1], 1e-9);
        }
    }

    [Fact]
    public void Movement_ShortSeaLegSnappingToOneNode_StillHasLength()
    {
        // Melbourne port and Altona, 10 km away, snap to the same Marnet node.
        var request = new MovementRequest
        {
            Legs =
            [
                new MovementLeg(
                    Location.FromCoordinate(new Coordinate(144.95, -37.84), "Melbourne water"),
                    Location.FromCoordinate(new Coordinate(144.82, -37.86), "Altona water"),
                    TransportMode.Sea)
            ]
        };
        var result = TradeRouterEngine.Default.CalculateMovement(request);

        var leg = result.Legs[0];
        leg.Feature.Geometry!.Coordinates.Count.Should().BeGreaterThanOrEqualTo(2);
        leg.Length.Should().BeInRange(5.0, 60.0);
        leg.DurationHours.Should().BeGreaterThan(0.0);
    }

    [Fact]
    public void Movement_UnknownCodeWithoutCoordinate_ThrowsNamingTheCode()
    {
        var act = () => TradeRoutes.CalculateMovement("Pickup XXZZZ to Port GBFXT Road");

        act.Should().Throw<ArgumentException>().WithMessage("*XXZZZ*neither*");
    }

    [Fact]
    public void Movement_CodeKnownToUnLocodeButUncoordinated_ThrowsWithItsName()
    {
        // Khalidia has no coordinate in UNECE or a verified supplement, and is not in the port list.
        var act = () => TradeRoutes.CalculateMovement("Delivery from port AEJEA to place AEKHA Road");

        act.Should().Throw<ArgumentException>().WithMessage("*AEKHA*Khalidia*no coordinates*");
    }

    [Fact]
    public void Movement_SupplementedCoordinates_ResolveWithTheirSource()
    {
        var gatwick = TradeRouterEngine.Default.UnLocodes.GetByCode("GBLGW")!;
        gatwick.Coordinate.Should().NotBeNull("the supplement file fills Gatwick");
        gatwick.CoordinateSource.Should().StartWith("Wikipedia");

        var melrose = TradeRouterEngine.Default.UnLocodes.GetByCode("AUMRS")!;
        melrose.Coordinate!.Value.Latitude.Should().BeApproximately(-32.817, 0.01, "Melrose is in South Australia, not near Melbourne");

        var guildford = TradeRouterEngine.Default.UnLocodes.GetByCode("GBGDD")!;
        guildford.Coordinate.Should().Be(new Coordinate(-0.577344, 51.234683));
        guildford.CoordinateSource.Should().StartWith("OpenStreetMap Wiki");

        var resolvedGuildford = TradeRoutes.Locate("GBGDD");
        resolvedGuildford.Name.Should().Be("Guildford");
        resolvedGuildford.Coordinate.Should().Be(guildford.Coordinate.Value);
        resolvedGuildford.Source.Should().Be("unlocode");

        TradeRouterEngine.Default.UnLocodes.GetByCode("SGSIN")!.CoordinateSource.Should().Be("UNECE");
    }

    [Fact]
    public void Movement_GeoNamesSupplement_ResolvesLightwaterByUnLocode()
    {
        var database = TradeRouterEngine.Default.UnLocodes;
        var lightwater = database.GetByCode("GBLGE")!;
        lightwater.Name.Should().Be("Lightwater");
        lightwater.Coordinate.Should().Be(new Coordinate(-0.67147, 51.34846));
        lightwater.CoordinateSource.Should().Be("GeoNames (geonameId 7116406)");

        var resolved = TradeRoutes.Locate("GBLGE");
        resolved.Source.Should().Be("unlocode");
        resolved.Coordinate.Should().Be(lightwater.Coordinate.Value);
        database.CountWithCoordinates.Should().Be(102287);
    }

    [Fact]
    public void Movement_RejectsPortLabelWhenUnLocodeSaysAirport()
    {
        // The port list calls CNTSN Tianjin, while embedded UN/LOCODE records only an airport function.
        var act = () => TradeRoutes.CalculateMovement("Port FRLEH to Port CNTSN Sea");

        act.Should().Throw<ArgumentException>().WithMessage("*CNTSN*declared as Port*airport*");
    }

    [Theory]
    [InlineData("BRALU")] // Alumar, Sao Luis: UN/LOCODE records only a road terminal
    [InlineData("CADCN")] // Duncan Bay, Campbell River: UN/LOCODE records only a road terminal
    public void Movement_AcceptsSeaPortConfirmedBySupplement(string code)
    {
        TradeRouterEngine.Default.UnLocodes.GetByCode(code)!.IsSeaPort.Should().BeTrue();

        var plan = MovementPlan
            .From(Waypoint.Port("NLRTM"))
            .ThenTo(Waypoint.Port(code), TransportMode.Sea);
        var result = TradeRoutes.CalculateMovement(plan);

        var destination = result.Legs[0].To;
        destination.Source.Should().Be("ports");
        destination.Port!.PortCode.Should().Be(code);
        result.Legs[0].Length.Should().BeGreaterThan(0.0);
    }

    [Fact]
    public void Movement_StillRejectsInlandPortListRecordAsSeaPort()
    {
        // Calgary is in the port list as an inland terminal 650 km from the nearest lane point.
        var plan = MovementPlan
            .From(Waypoint.Port("USSEA"))
            .ThenTo(Waypoint.Port("CACAL"), TransportMode.Sea);
        var act = () => TradeRoutes.CalculateMovement(plan);

        act.Should().Throw<ArgumentException>().WithMessage("*CACAL*declared as Port*");
    }

    [Fact]
    public void Movement_ResolvesAirportsAndPortsFromUnLocodeWithoutCallerCoordinates()
    {
        // Heathrow and the Port of Shanghai both carry coordinates in UN/LOCODE; Felixstowe comes from the port list.
        var result = TradeRoutes.CalculateMovement("Airport GBLHR to Port GBFXT Road\nPort GBFXT to Port CNSHG Sea");

        var heathrow = result.Legs[0].From;
        heathrow.Source.Should().Be("unlocode");
        heathrow.Name.Should().Be("Heathrow Apt/London");
        heathrow.Coordinate.Longitude.Should().BeApproximately(-0.45, 0.01);

        var felixstowe = result.Legs[0].To;
        felixstowe.Source.Should().Be("ports", "the port list position is used when UN/LOCODE agrees on the name");
        felixstowe.Port.Should().NotBeNull();

        var shanghai = result.Legs[1].To;
        shanghai.Source.Should().Be("unlocode");
        shanghai.Coordinate.Latitude.Should().BeApproximately(30.63, 0.01, "Shanghai Pt in UN/LOCODE is the Yangshan area");

        TradeRouterEngine.Default.UnLocodes.Count.Should().BeGreaterThan(100000);
        TradeRouterEngine.Default.UnLocodes.CountWithCoordinates.Should().BeGreaterThan(80000);
    }

    [Fact]
    public void Movement_UsesResolverForUnknownCodes()
    {
        // XXLON is in neither embedded list, so only the resolver can place it.
        var request = new MovementRequest { Legs = MovementParser.Parse("Pickup XXLON to Port GBFXT Road") };
        request.Resolver = new StubResolver(("XXLON", ExtraPlaces["GBLON"], "London"));

        var result = TradeRouterEngine.Default.CalculateMovement(request);

        result.Legs[0].From.Source.Should().Be("resolver");
        result.Legs[0].From.Name.Should().Be("London");
        result.Legs[0].Length.Should().BeInRange(130.0, 170.0);
    }

    [Fact]
    public void Movement_HonoursUnitsAndModeSpeeds()
    {
        var request = new MovementRequest
        {
            Legs =
            [
                new MovementLeg(
                    Location.FromCoordinate(ExtraPlaces["GBLON"], "London"),
                    Location.FromCoordinate(new Coordinate(1.3108, 51.9630), "Felixstowe"),
                    TransportMode.Rail,
                    LegKind.Pickup)
            ]
        };
        request.SeaOptions = new TradeRouterOptions { Units = DistanceUnit.NauticalMiles };
        request.SpeedsKmh[TransportMode.Rail] = 100.0;

        var result = TradeRouterEngine.Default.CalculateMovement(request);

        result.Units.Should().Be("naut");
        double expectedHours = result.Legs[0].Length / (100.0 * 0.539956803);
        result.Legs[0].DurationHours.Should().BeApproximately(expectedHours, 1e-6);
    }

    [Fact]
    public void Parser_ReportsRealLineNumberWhenBlankLinesPrecedeTheBadLine()
    {
        var text = "Port GBFXT to Port SGSIN Sea\r\n\r\n\r\nthis is not a leg";

        var act = () => MovementParser.Parse(text);

        act.Should().Throw<FormatException>().WithMessage("Line 4*");
    }

    [Fact]
    public void Parser_RejectsUnknownModeInsteadOfGuessing()
    {
        var act = () => MovementParser.Parse("Port GBFXT to Port SGSIN Barge");

        act.Should().Throw<FormatException>().WithMessage("*Barge*");
    }

    [Fact]
    public void Parser_AcceptsModeSynonymsAndLowerCaseCodes()
    {
        var legs = MovementParser.Parse("pickup gblgw to port gbfxt truck\nport gbfxt to port sgsin vessel");

        legs[0].Mode.Should().Be(TransportMode.Road);
        legs[0].From.Code.Should().Be("GBLGW");
        legs[1].Mode.Should().Be(TransportMode.Sea);
    }

    [Fact]
    public void Movement_BlockedSeaLeg_ThrowsNamingTheLeg()
    {
        // Singapore to Piraeus with Suez and Gibraltar closed has no route; the coordinate-only destination
        // also exercises Location.FromCoordinate.
        var request = new MovementRequest
        {
            Legs =
            [
                new MovementLeg(
                    Location.FromCode("SGSIN"),
                    Location.FromCoordinate(new Coordinate(23.62904, 37.94056), "Piraeus"),
                    TransportMode.Sea)
            ],
            SeaOptions = new TradeRouterOptions { Restrictions = { Passages.Passage.Suez, Passages.Passage.Gibraltar } }
        };

        var act = () => TradeRouterEngine.Default.CalculateMovement(request);

        act.Should().Throw<InvalidOperationException>().WithMessage("Leg 1*Piraeus*");
    }

    [Fact]
    public void Movement_InvalidSuppliedCoordinate_ThrowsForStraightLegs()
    {
        var request = new MovementRequest { Legs = MovementParser.Parse("Pickup GBLON to Port GBFXT Road") };
        request.Coordinates["GBLON"] = new Coordinate(-0.19, 951.1);

        var act = () => TradeRouterEngine.Default.CalculateMovement(request);

        act.Should().Throw<ArgumentException>().WithMessage("*Latitude*");
    }

    [Fact]
    public void Movement_LowerCaseCoordinateKeys_Resolve()
    {
        var request = new MovementRequest { Legs = MovementParser.Parse("Pickup GBLON to Port GBFXT Road") };
        request.Coordinates["gblon"] = ExtraPlaces["GBLON"];

        var result = TradeRouterEngine.Default.CalculateMovement(request);

        result.Legs[0].From.Source.Should().Be("coordinates");
        result.Legs[0].Length.Should().BeInRange(130.0, 170.0);
    }

    [Fact]
    public void Movement_SeaSpeedInSpeedsKmh_IsRejected()
    {
        var request = new MovementRequest { Legs = MovementParser.Parse("Port GBFXT to Port SGSIN Sea") };
        request.SpeedsKmh[TransportMode.Sea] = 30.0;

        var act = () => TradeRouterEngine.Default.CalculateMovement(request);

        act.Should().Throw<ArgumentException>().WithMessage("*SpeedKnots*");
    }

    [Fact]
    public void Movement_ResolverDoesNotOverrideEmbeddedPortCoordinates()
    {
        var request = new MovementRequest { Legs = MovementParser.Parse("Port GBFXT to Port SGSIN Sea") };
        request.Resolver = new StubResolver(("GBFXT", new Coordinate(100.0, 0.0), "Wrong place"));

        var result = TradeRouterEngine.Default.CalculateMovement(request);

        var felixstowe = TradeRouterEngine.Default.Ports.GetByCode("GBFXT")!;
        result.Legs[0].From.Coordinate.Should().Be(felixstowe.Coordinate);
        result.Legs[0].From.Name.Should().Be("Felixstowe");
    }

    [Fact]
    public void Movement_JunctionLocationIsResolvedOnce()
    {
        var resolver = new CountingResolver(("XXBBB", new Coordinate(-0.190278, 51.148056)), ("XXAAA", new Coordinate(1.0, 51.0)));
        var request = new MovementRequest
        {
            Legs = MovementParser.Parse("Pickup XXBBB to Port GBFXT Road\nPort GBFXT to Port XXAAA Road\nPort XXAAA to Port GBFXT Road"),
            Resolver = resolver
        };

        TradeRouterEngine.Default.CalculateMovement(request);

        resolver.Calls.Should().Be(2, "XXBBB and XXAAA are each resolved once; GBFXT comes from the port database");
    }

    [Fact]
    public void SingleRoute_EndpointsSnappingToOneNode_ReturnsTwoPointLine()
    {
        var melbourne = TradeRouterEngine.Default.Ports.GetByCode("AUMEL")!.Coordinate;
        var altona = TradeRouterEngine.Default.UnLocodes.GetByCode("AUALT")!.Coordinate!.Value;

        var route = TradeRoutes.Calculate(melbourne, altona);

        route.Geometry!.Coordinates.Should().HaveCount(2);
        route.Properties.Length.Should().BeInRange(5.0, 60.0);
    }

    [Fact]
    public void Coordinate_ToString_IsCultureInvariant()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            new Coordinate(-0.19, 51.1).ToString().Should().Be("[-0.190000, 51.100000]");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Movement_AirLegs_AreStraightLinesWithNoLanePointsOrChokePoints()
    {
        var request = new MovementRequest
        {
            Legs = MovementParser.Parse("""
                Pickup GBLGW to Airport GBLHR Road
                Airport GBLHR to Airport AUMEL Air
                Delivery from airport AUMEL to place AUMRS Road
                """),
            SeaOptions = new TradeRouterOptions { ReturnPassages = true }
        };
        var result = TradeRouterEngine.Default.CalculateMovement(request);

        var flight = result.Legs[1];
        flight.Leg.Mode.Should().Be(TransportMode.Air);
        flight.Feature.Geometry!.Coordinates.Should().HaveCount(2, "an air leg is one straight great-circle line");
        flight.Length.Should().BeInRange(16500.0, 17300.0, "Heathrow to Melbourne great-circle distance");
        flight.DurationHours.Should().BeApproximately(flight.Length / 800.0, 1e-6);
        flight.Feature.Properties.TraversedPassages.Should().BeNullOrEmpty();
        result.LengthByMode.Should().ContainKeys(TransportMode.Air, TransportMode.Road);
    }

    [Fact]
    public void Emissions_UseGlecDefaultsPerModeAndAirDistanceBand()
    {
        var f = EmissionFactors.GlecDefaults;

        f.GramsPerTonneKm(TransportMode.Sea, 15000).Should().Be(7.6);
        f.GramsPerTonneKm(TransportMode.Road, 100).Should().Be(92.0);
        f.GramsPerTonneKm(TransportMode.Rail, 500).Should().Be(28.0);
        f.GramsPerTonneKm(TransportMode.Air, 999).Should().Be(1130.0);
        f.GramsPerTonneKm(TransportMode.Air, 1000).Should().Be(700.0);
        f.GramsPerTonneKm(TransportMode.Air, 3700).Should().Be(700.0);
        f.GramsPerTonneKm(TransportMode.Air, 3701).Should().Be(630.0);
    }

    [Fact]
    public void Emissions_PerLegAndTotals_FollowIntensityTimesDistance()
    {
        var result = TradeRoutes.CalculateMovement(MelbourneMovement, cargoTonnes: 20.0);

        var road = result.Legs[0];
        road.Co2eGramsPerTonneKm.Should().Be(92.0);
        road.Co2eKgPerTonne.Should().BeApproximately(92.0 * road.Length / 1000.0, 1e-9);
        road.Co2eKg.Should().BeApproximately(road.Co2eKgPerTonne * 20.0, 1e-9);

        var sea = result.Legs[1];
        sea.Co2eGramsPerTonneKm.Should().Be(7.6);
        sea.Co2eKgPerTonne.Should().BeApproximately(7.6 * sea.Length / 1000.0, 1e-9);

        result.CargoTonnes.Should().Be(20.0);
        result.TotalCo2eKgPerTonne.Should().BeApproximately(result.Legs.Sum(l => l.Co2eKgPerTonne), 1e-9);
        result.TotalCo2eKg.Should().BeApproximately(result.TotalCo2eKgPerTonne * 20.0, 1e-9);
    }

    [Fact]
    public void Emissions_WithoutCargoWeight_ReportOnlyPerTonneFigures()
    {
        var result = TradeRoutes.CalculateMovement("Port GBFXT to Port SGSIN Sea");

        result.CargoTonnes.Should().BeNull();
        result.TotalCo2eKg.Should().BeNull();
        result.Legs[0].Co2eKg.Should().BeNull();
        result.Legs[0].Co2eKgPerTonne.Should().BeGreaterThan(100.0, "15,400 km at 7.6 g per tonne-km");

        using var doc = JsonDocument.Parse(result.ToJson());
        var props = doc.RootElement.GetProperty("features")[0].GetProperty("properties");
        props.GetProperty("co2e_g_per_tonne_km").GetDouble().Should().Be(7.6);
        props.TryGetProperty("co2e_kg", out _).Should().BeFalse();
        doc.RootElement.GetProperty("properties").GetProperty("total_co2e_kg_per_tonne").GetDouble().Should().BeGreaterThan(100.0);
    }

    [Fact]
    public void Emissions_ConvertLengthToKilometresWhenUnitsDiffer()
    {
        var request = new MovementRequest { Legs = MovementParser.Parse("Port GBFXT to Port SGSIN Sea") };
        request.SeaOptions = new TradeRouterOptions { Units = DistanceUnit.NauticalMiles };

        var inNaut = TradeRouterEngine.Default.CalculateMovement(request);
        var inKm = TradeRoutes.CalculateMovement("Port GBFXT to Port SGSIN Sea");

        inNaut.Legs[0].Co2eKgPerTonne.Should().BeApproximately(inKm.Legs[0].Co2eKgPerTonne, 1e-6);
    }

    [Fact]
    public void Emissions_CustomFactorsAreHonoured()
    {
        var request = new MovementRequest { Legs = MovementParser.Parse("Pickup GBLGW to Port GBFXT Road") };
        request.Emissions = new EmissionFactors { RoadGramsPerTonneKm = 50.0 };

        var result = TradeRouterEngine.Default.CalculateMovement(request);

        result.Legs[0].Co2eGramsPerTonneKm.Should().Be(50.0);
    }

    [Fact]
    public void Emissions_SeaLegsUsePerTeuRateWhenTeuIsGiven()
    {
        var result = TradeRoutes.CalculateMovement(MelbourneMovement, cargoTonnes: 12.0, cargoTeu: 2.0);

        var sea = result.Legs[1];
        sea.Co2eBasis.Should().Be("teu");
        sea.Feature.Properties.Co2eGramsPerTeuKm.Should().Be(76.0);
        sea.Co2eKg.Should().BeApproximately(76.0 * 2.0 * sea.Length / 1000.0, 1e-9);

        var road = result.Legs[0];
        road.Co2eBasis.Should().Be("tonnes", "non-sea legs use the stated weight");
        road.Co2eKg.Should().BeApproximately(road.Co2eKgPerTonne * 12.0, 1e-9);

        result.CargoTeu.Should().Be(2.0);
        result.TotalCo2eKg.Should().BeApproximately(result.Legs.Sum(l => l.Co2eKg!.Value), 1e-9);

        using var doc = JsonDocument.Parse(result.ToJson());
        var seaProps = doc.RootElement.GetProperty("features")[1].GetProperty("properties");
        seaProps.GetProperty("co2e_basis").GetString().Should().Be("teu");
        seaProps.GetProperty("co2e_g_per_teu_km").GetDouble().Should().Be(76.0);
        doc.RootElement.GetProperty("properties").GetProperty("cargo_teu").GetDouble().Should().Be(2.0);
    }

    [Fact]
    public void Emissions_TeuOnly_InfersAverageWeightForNonSeaLegs()
    {
        var result = TradeRoutes.CalculateMovement(MelbourneMovement, cargoTeu: 1.0);

        var road = result.Legs[0];
        road.Co2eBasis.Should().Be("teu_average_weight");
        road.Co2eKg.Should().BeApproximately(road.Co2eKgPerTonne * 10.0, 1e-9, "GLEC average of 10 t per TEU");
        result.Legs[1].Co2eBasis.Should().Be("teu");
        result.CargoTonnes.Should().BeNull();
        result.TotalCo2eKg.Should().NotBeNull();
    }

    [Fact]
    public void Time_DefaultModelIncludesExplicitSeaAndConnectionAllowances()
    {
        var result = TradeRoutes.CalculateMovement(MelbourneMovement);

        var road = result.Legs[0];
        road.PortHours.Should().Be(0.0);
        road.TransitHours.Should().Be(road.DurationHours);

        var sea = result.Legs[1];
        sea.PortHours.Should().Be(48.0, "24 h at each end of a sea leg");
        sea.DurationHours.Should().BeApproximately(sea.Length / (16.0 * 1.852), 1e-6, "steaming at 16 knots");
        sea.Feature.Properties.OperationalAllowanceCorridor.Should().Be("asia-north-europe", "Felixstowe to Singapore crosses Gibraltar, Suez and Malacca");
        sea.OperationalAllowanceHours.Should().BeApproximately(sea.DurationHours * 0.63, 1e-9);
        sea.ConnectionHours.Should().Be(0.0, "the first sea leg has no preceding service to connect from");
        sea.TransitHours.Should().BeApproximately(sea.DurationHours * 1.63 + 48.0, 1e-9);

        var connectingSea = result.Legs[2];
        connectingSea.ConnectionHours.Should().Be(48.0, "consecutive sea legs are modelled as a transshipment");

        result.TotalPortHours.Should().Be(2 * 48.0);
        connectingSea.Feature.Properties.OperationalAllowanceCorridor.Should().Be("default", "Singapore to Melbourne has no fitted corridor");
        result.TotalOperationalAllowanceHours.Should().BeApproximately(
            sea.DurationHours * 0.63 + connectingSea.DurationHours * 0.20,
            1e-9);
        result.TotalConnectionHours.Should().Be(48.0);
        result.TotalTransitHours.Should().BeApproximately(
            result.TotalDurationHours
            + result.TotalOperationalAllowanceHours
            + result.TotalPortHours
            + result.TotalConnectionHours,
            1e-9);
        (result.TotalTransitHours / 24.0).Should().BeInRange(53.0, 56.0, "the corridor allowances put the UK–Melbourne movement inside the 43 to 63 days carriers publish");

        using var doc = JsonDocument.Parse(result.ToJson());
        var seaProperties = doc.RootElement.GetProperty("features")[1].GetProperty("properties");
        seaProperties.GetProperty("port_hours").GetDouble().Should().Be(48.0);
        seaProperties.GetProperty("operational_allowance_hours").GetDouble().Should().BeApproximately(sea.OperationalAllowanceHours, 1e-9);
        seaProperties.GetProperty("connection_hours").GetDouble().Should().Be(0.0);
        var totals = doc.RootElement.GetProperty("properties");
        totals.GetProperty("total_operational_allowance_hours").GetDouble().Should().BeApproximately(result.TotalOperationalAllowanceHours, 1e-9);
        totals.GetProperty("total_connection_hours").GetDouble().Should().Be(48.0);
        totals.GetProperty("total_transit_hours").GetDouble().Should().BeApproximately(result.TotalTransitHours, 1e-9);
    }

    [Fact]
    public void Time_AllAllowancesCanBeSwitchedOffForPureTravelTime()
    {
        var request = new MovementRequest
        {
            Legs = MovementParser.Parse("Port GBFXT to Port SGSIN Sea"),
            PortDwellHours = 0.0,
            SeaOperationalAllowance = 0.0,
            TransshipmentConnectionHours = 0.0
        };

        var result = TradeRouterEngine.Default.CalculateMovement(request);

        result.Legs[0].PortHours.Should().Be(0.0);
        result.TotalTransitHours.Should().Be(result.TotalDurationHours);
    }

    [Fact]
    public void Time_InvalidOperationalAllowanceIsRejected()
    {
        var request = new MovementRequest
        {
            Legs = MovementParser.Parse("Port GBFXT to Port SGSIN Sea"),
            SeaOperationalAllowance = double.NaN
        };

        var act = () => TradeRouterEngine.Default.CalculateMovement(request);

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*operational allowance*");
    }

    [Fact]
    public void Time_InvalidConnectionTimeIsRejected()
    {
        var request = new MovementRequest
        {
            Legs = MovementParser.Parse("Port GBFXT to Port SGSIN Sea"),
            TransshipmentConnectionHours = -1.0
        };

        var act = () => TradeRouterEngine.Default.CalculateMovement(request);

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*connection time*");
    }

    [Fact]
    public void Locate_ResolvesCodesFromPortsThenUnLocode()
    {
        var paris = TradeRoutes.Locate("FRPAR");
        paris.Source.Should().Be("unlocode", "Paris is not a sea port");
        paris.Coordinate.Latitude.Should().BeApproximately(48.85, 0.01);

        var london = TradeRoutes.Locate("gblon");
        london.Source.Should().Be("ports");
        london.Port!.Name.Should().Be("London");

        var jebelAli = TradeRoutes.Locate("AEJEA");
        jebelAli.Source.Should().Be("ports", "UN/LOCODE has no coordinates for Jebel Ali");

        var act = () => TradeRoutes.Locate("XXZZZ");
        act.Should().Throw<ArgumentException>().WithMessage("*XXZZZ*");
    }

    private sealed class CountingResolver(params (string Code, Coordinate Coordinate)[] entries) : ILocationResolver
    {
        public int Calls { get; private set; }

        public bool TryResolve(string code, out Coordinate coordinate, out string? name)
        {
            Calls++;
            foreach (var (c, coord) in entries)
            {
                if (string.Equals(c, code, StringComparison.OrdinalIgnoreCase))
                {
                    coordinate = coord;
                    name = null;
                    return true;
                }
            }
            coordinate = default;
            name = null;
            return false;
        }
    }

    private sealed class StubResolver(params (string Code, Coordinate Coordinate, string Name)[] entries) : ILocationResolver
    {
        public bool TryResolve(string code, out Coordinate coordinate, out string? name)
        {
            foreach (var (c, coord, n) in entries)
            {
                if (string.Equals(c, code, StringComparison.OrdinalIgnoreCase))
                {
                    coordinate = coord;
                    name = n;
                    return true;
                }
            }
            coordinate = default;
            name = null;
            return false;
        }
    }
}
