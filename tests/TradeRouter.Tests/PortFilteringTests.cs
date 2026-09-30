using AwesomeAssertions;
using TradeRouter.Common;
using TradeRouter.Graph;
using TradeRouter.Ports;
using Xunit;

namespace TradeRouter.Tests;

public class PortFilteringTests
{
    // Synthetic ports on the equator, so every expected choice follows from the fixture alone.
    private static readonly Coordinate NearAaOne = new(0.1, 0);
    private static readonly Coordinate NearCcOne = new(10.6, 0);

    private static PortDatabase CreateDatabase() => new(
    [
        CreatePort("AAONE", "Aland", 0.0, terminal: 0.0),
        CreatePort("AATWO", "Aland", 1.0, terminal: 1.0, "BB"),
        CreatePort("AATHR", "Aland", 2.0, terminal: 1.0, "cc"),
        CreatePort("BBONE", "Bbland", 10.0, terminal: 1.0, "AA"),
        CreatePort("CCONE", "Ccland", 10.5, terminal: 0.0)
    ]);

    private static Port CreatePort(string code, string country, double longitude, double terminal, params string[] toCountries) => new()
    {
        PortCode = code,
        Name = code,
        Country = country,
        TerminalFlag = terminal,
        ToCountries = toCountries,
        Coordinate = new Coordinate(longitude, 0.0)
    };

    [Fact]
    public void QueryClosestPort_WithoutFilters_ReturnsNearestPort()
    {
        CreateDatabase().QueryClosestPort(NearAaOne)!.PortCode.Should().Be("AAONE");
    }

    [Fact]
    public void QueryClosestPort_OnlyTerminals_SkipsNearerNonTerminal()
    {
        CreateDatabase().QueryClosestPort(NearAaOne, onlyTerminals: true)!.PortCode.Should().Be("AATWO");
    }

    [Theory]
    [InlineData("BB", "AATWO")]
    [InlineData("cc", "AATHR")]
    [InlineData("C-C", "AATHR")]
    public void QueryClosestPort_ToCountry_KeepsOnlyPortsServingThatCountry(string toCountry, string expected)
    {
        CreateDatabase().QueryClosestPort(NearAaOne, toCountry: toCountry)!.PortCode.Should().Be(expected);
    }

    [Theory]
    [InlineData("BB")]
    [InlineData("Bbland")]
    [InlineData("bb land")]
    public void QueryClosestPort_Country_MatchesCodePrefixOrCountryName(string country)
    {
        CreateDatabase().QueryClosestPort(NearAaOne, country: country)!.PortCode.Should().Be("BBONE");
    }

    [Fact]
    public void QueryClosestPort_StrictFilterMatchingNothing_ReturnsNull()
    {
        var db = CreateDatabase();

        db.QueryClosestPort(NearAaOne, country: "ZZ").Should().BeNull();
        db.QueryClosestPort(NearAaOne, toCountry: "ZZ").Should().BeNull();
    }

    [Fact]
    public void QueryClosestPort_NonStrictFilterMatchingNothing_FallsBackToNearestPort()
    {
        var db = CreateDatabase();

        db.QueryClosestPort(NearAaOne, country: "ZZ", strict: false)!.PortCode.Should().Be("AAONE");
        db.QueryClosestPort(NearAaOne, toCountry: "ZZ", strict: false)!.PortCode.Should().Be("AAONE");
    }

    [Fact]
    public void QueryClosestPort_NonStrict_DropsOnlyTheFilterThatMatchesNothing()
    {
        // CCONE is the only Ccland port and is not a terminal, so the country filter is dropped
        // while the terminal filter still applies.
        var db = CreateDatabase();

        db.QueryClosestPort(NearCcOne, onlyTerminals: true, country: "CC").Should().BeNull();
        db.QueryClosestPort(NearCcOne, onlyTerminals: true, country: "CC", strict: false)!.PortCode.Should().Be("BBONE");
    }

    [Fact]
    public void QueryClosestPort_OnlyTerminalsWithNoTerminals_RespectsStrict()
    {
        var db = new PortDatabase([CreatePort("AAONE", "Aland", 0.0, terminal: 0.0)]);

        db.QueryClosestPort(NearAaOne, onlyTerminals: true).Should().BeNull();
        db.QueryClosestPort(NearAaOne, onlyTerminals: true, strict: false)!.PortCode.Should().Be("AAONE");
    }

    [Fact]
    public void GetSelectedPortMatrix_AppliesLoadingAndDischargeCountries()
    {
        var parameters = new PortParameters { CountryPol = "AA", CountryPod = "BB" };

        var matrix = CreateDatabase().GetSelectedPortMatrix(NearAaOne, NearCcOne, parameters);

        matrix.Should().ContainSingle();
        matrix[0].OriginPort.PortCode.Should().Be("AAONE");
        matrix[0].DestPort.PortCode.Should().Be("BBONE");
        matrix[0].OriginPort.Share.Should().Be(1.0);
        matrix[0].DestPort.Share.Should().Be(1.0);
    }

    [Fact]
    public void GetSelectedPortMatrix_CountryRestricted_RequiresOriginPortToServeDischargeCountry()
    {
        var unrestricted = new PortParameters { CountryPod = "CC" };
        var restricted = new PortParameters { CountryPod = "CC", CountryRestricted = true };
        var db = CreateDatabase();

        db.GetSelectedPortMatrix(NearAaOne, NearCcOne, unrestricted)[0].OriginPort.PortCode.Should().Be("AAONE");

        var matrix = db.GetSelectedPortMatrix(NearAaOne, NearCcOne, restricted);
        matrix[0].OriginPort.PortCode.Should().Be("AATHR");
        matrix[0].DestPort.PortCode.Should().Be("CCONE");
    }

    [Fact]
    public void GetSelectedPortMatrix_StrictFilterWithNoMatch_ReturnsEmptyMatrix()
    {
        var parameters = new PortParameters { CountryPol = "ZZ" };

        CreateDatabase().GetSelectedPortMatrix(NearAaOne, NearCcOne, parameters).Should().BeEmpty();
    }

    [Fact]
    public void CalculateRoute_IncludePortsWithCountryRestriction_RoutesFromFilteredPort()
    {
        var engine = CreateEquatorEngine();
        var options = new TradeRouterOptions
        {
            IncludePorts = true,
            PortParameters = new PortParameters { CountryPod = "CC", CountryRestricted = true }
        };

        var route = engine.CalculateRoute(NearAaOne, NearCcOne, options);

        route.Properties.PortOrigin!.PortCode.Should().Be("AATHR");
        route.Properties.PortDest!.PortCode.Should().Be("CCONE");
        route.Geometry!.Coordinates[0][0].Should().BeApproximately(2.0, 1e-9);
    }

    [Fact]
    public void CalculateRoute_IncludePortsWithNoMatchingPort_FallsBackToRawCoordinates()
    {
        var engine = CreateEquatorEngine();
        var options = new TradeRouterOptions
        {
            IncludePorts = true,
            AppendOriginDestination = true,
            PortParameters = new PortParameters { CountryPol = "ZZ" }
        };

        var route = engine.CalculateRoute(NearAaOne, NearCcOne, options);

        route.Properties.PortOrigin.Should().BeNull();
        route.Properties.PortDest.Should().BeNull();
        route.Geometry!.Coordinates[0][0].Should().BeApproximately(0.1, 1e-9);
    }

    [Fact]
    public void CalculateRoute_EmbeddedPortsHonourCountryFilters()
    {
        var paris = new Coordinate(2.3522, 48.8566);
        var newYork = new Coordinate(-74.0, 40.7);
        var options = new TradeRouterOptions
        {
            IncludePorts = true,
            PortParameters = new PortParameters { CountryPol = "BE", CountryPod = "US" }
        };

        var route = TradeRoutes.Calculate(paris, newYork, options);

        route.Properties.PortOrigin!.PortCode.Should().StartWith("BE");
        route.Properties.PortDest!.PortCode.Should().StartWith("US");
    }

    [Fact]
    public void CalculateRoute_EmbeddedPortsCountryRestrictedOriginServesDischargeCountry()
    {
        var paris = new Coordinate(2.3522, 48.8566);
        var newYork = new Coordinate(-74.0, 40.7);
        var options = new TradeRouterOptions
        {
            IncludePorts = true,
            PortParameters = new PortParameters { CountryPol = "FR", CountryPod = "US", CountryRestricted = true }
        };

        var route = TradeRoutes.Calculate(paris, newYork, options);

        route.Properties.PortOrigin!.PortCode.Should().StartWith("FR");
        route.Properties.PortOrigin.ToCountries.Should().Contain("US");
    }

    private static TradeRouterEngine CreateEquatorEngine()
    {
        var graph = new MaritimeGraph();
        for (int lon = 0; lon < 11; lon++)
            graph.AddEdge(new Coordinate(lon, 0), new Coordinate(lon + 1, 0));
        graph.BuildIndex();
        return new TradeRouterEngine(graph, CreateDatabase());
    }
}

public class PreferredPortAreaTests
{
    private static readonly Coordinate[] Belgium =
    [
        new(2.54, 51.13), new(4.18, 50.03), new(5.84, 49.61), new(6.41, 50.38),
        new(5.82, 51.12), new(3.83, 51.62), new(2.54, 51.13)
    ];

    private static readonly Coordinate Brussels = new(4.35, 50.85);
    private static readonly Coordinate Paris = new(2.35, 48.86);
    private static readonly Coordinate Tokyo = new(139.68, 35.78);

    [Fact]
    public void GetPreferredPorts_PointOutsideStrictArea_ReturnsNothing()
    {
        var area = new AreaFeature(Belgium, "BE", [new PortProps("BEANR")]);

        TradeRouterEngine.Default.Ports.GetPreferredPorts(Paris, [area]).Should().BeEmpty();
    }

    [Fact]
    public void GetPreferredPorts_NonStrictArea_UsesNearestAreaWithin2000Km()
    {
        var area = new AreaFeature(Belgium, "BE", [new PortProps("BEANR")]);

        var ports = TradeRouterEngine.Default.Ports.GetPreferredPorts(Paris, [area], strictArea: false);

        ports.Should().ContainSingle().Which.PortCode.Should().Be("BEANR");
    }

    [Fact]
    public void GetPreferredPorts_NonStrictArea_IgnoresAreasBeyond2000Km()
    {
        var area = new AreaFeature(Belgium, "BE", [new PortProps("BEANR")]);

        TradeRouterEngine.Default.Ports.GetPreferredPorts(Tokyo, [area], strictArea: false).Should().BeEmpty();
    }

    [Fact]
    public void GetPreferredPorts_NonStrictArea_ChoosesTheCloserOfTwoAreas()
    {
        var belgium = new AreaFeature(Belgium, "BE", [new PortProps("BEANR")]);
        var spain = new AreaFeature([new(-9.0, 36.0), new(3.0, 36.0), new(3.0, 43.5), new(-9.0, 43.5)], "ES", [new PortProps("ESVLC")]);

        var ports = TradeRouterEngine.Default.Ports.GetPreferredPorts(Paris, [spain, belgium], strictArea: false);

        ports.Should().ContainSingle().Which.PortCode.Should().Be("BEANR");
    }

    [Fact]
    public void GetPreferredPorts_NestedAreas_UseTheSmallestContainingArea()
    {
        var europe = new AreaFeature([new(-10.0, 35.0), new(30.0, 35.0), new(30.0, 60.0), new(-10.0, 60.0)], "EU", [new PortProps("NLRTM")]);
        var belgium = new AreaFeature(Belgium, "BE", [new PortProps("BEANR")]);

        var ports = TradeRouterEngine.Default.Ports.GetPreferredPorts(Brussels, [europe, belgium]);

        ports.Should().ContainSingle().Which.PortCode.Should().Be("BEANR");
    }

    [Fact]
    public void GetPreferredPorts_AreaWithoutPreferredPorts_ReturnsNothing()
    {
        var area = new AreaFeature(Belgium, "BE");

        TradeRouterEngine.Default.Ports.GetPreferredPorts(Brussels, [area]).Should().BeEmpty();
    }

    [Fact]
    public void GetPreferredPorts_SharesAboveOneAreNormalisedAndSortedDescending()
    {
        var area = new AreaFeature(Belgium, "BE", [new PortProps("FRLEH", 200), new PortProps("BEANR", 250)]);

        var ports = TradeRouterEngine.Default.Ports.GetPreferredPorts(Brussels, [area]);

        ports.Select(p => p.PortCode).Should().Equal("BEANR", "FRLEH");
        ports[0].Share.Should().BeApproximately(250.0 / 450.0, 1e-12);
        ports[1].Share.Should().BeApproximately(200.0 / 450.0, 1e-12);
    }

    [Fact]
    public void GetPreferredPorts_SharesSummingBelowOneAreKeptAsGiven()
    {
        var area = new AreaFeature(Belgium, "BE", [new PortProps("FRLEH", 0.2), new PortProps("BEANR", 0.3)]);

        var ports = TradeRouterEngine.Default.Ports.GetPreferredPorts(Brussels, [area]);

        ports[0].Share.Should().Be(0.3);
        ports[1].Share.Should().Be(0.2);
    }

    [Fact]
    public void GetPreferredPorts_TopLimitsTheResultToTheLargestShares()
    {
        var area = new AreaFeature(Belgium, "BE", [new PortProps("FRLEH", 1), new PortProps("BEANR", 3), new PortProps("NLRTM", 2)]);

        var ports = TradeRouterEngine.Default.Ports.GetPreferredPorts(Brussels, [area], top: 2);

        ports.Select(p => p.PortCode).Should().Equal("BEANR", "NLRTM");
    }

    [Fact]
    public void GetPreferredPorts_OverflowingShares_AreRejected()
    {
        var area = new AreaFeature(Belgium, "BE", [new PortProps("FRLEH", double.MaxValue), new PortProps("BEANR", double.MaxValue)]);

        var act = () => TradeRouterEngine.Default.Ports.GetPreferredPorts(Brussels, [area]);

        act.Should().Throw<ArgumentException>().WithMessage("*'BE' overflow*");
    }

    public static TheoryData<object, object> CustomCoordinateValues => new()
    {
        { 3.2, 51.3 },
        { 3.2f, 51.3f },
        { 3, 51 },
        { 3L, 51L },
        { "3.2", "51.3" }
    };

    [Theory]
    [MemberData(nameof(CustomCoordinateValues))]
    public void GetPreferredPorts_UnknownPortWithXyProps_CreatesCustomPort(object x, object y)
    {
        var props = new Dictionary<string, object?> { ["x"] = x, ["y"] = y };
        var area = new AreaFeature(Belgium, "BE", [new PortProps("XXCUS", 1.0, props)]);

        var port = TradeRouterEngine.Default.Ports.GetPreferredPorts(Brussels, [area]).Single();

        port.PortCode.Should().Be("XXCUS");
        port.Name.Should().Be("XXCUS");
        port.Share.Should().Be(1.0);
        port.Coordinate.Longitude.Should().BeApproximately(Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture), 1e-6);
        port.Coordinate.Latitude.Should().BeApproximately(Convert.ToDouble(y, System.Globalization.CultureInfo.InvariantCulture), 1e-6);
    }

    public static TheoryData<Dictionary<string, object?>?> UnusableProps => new()
    {
        null,
        new Dictionary<string, object?> { ["x"] = 3.2 },
        new Dictionary<string, object?> { ["x"] = "east", ["y"] = 51.3 },
        new Dictionary<string, object?> { ["x"] = 3.2, ["y"] = null },
        new Dictionary<string, object?> { ["x"] = 3.2m, ["y"] = 51.3m }
    };

    [Theory]
    [MemberData(nameof(UnusableProps))]
    public void GetPreferredPorts_UnknownPortWithoutUsableXy_IsRejected(Dictionary<string, object?>? props)
    {
        var area = new AreaFeature(Belgium, "BE", [new PortProps("XXCUS", 1.0, props)]);

        var act = () => TradeRouterEngine.Default.Ports.GetPreferredPorts(Brussels, [area]);

        act.Should().Throw<ArgumentException>().WithMessage("*'XXCUS' in area 'BE' is not in the port list and has no x/y*");
    }

    [Fact]
    public void GetPreferredPorts_CustomPortWithInvalidLatitude_IsRejected()
    {
        var props = new Dictionary<string, object?> { ["x"] = 3.2, ["y"] = 95.0 };
        var area = new AreaFeature(Belgium, "BE", [new PortProps("XXCUS", 1.0, props)]);

        var act = () => TradeRouterEngine.Default.Ports.GetPreferredPorts(Brussels, [area]);

        act.Should().Throw<ArgumentException>().WithMessage("*Latitude*");
    }

    [Theory]
    [InlineData("", 1.0)]
    [InlineData("  ", 1.0)]
    [InlineData("BEANR", 0.0)]
    [InlineData("BEANR", -1.0)]
    [InlineData("BEANR", double.NaN)]
    [InlineData("BEANR", double.PositiveInfinity)]
    public void PortProps_RejectsBlankIdOrNonPositiveShare(string portId, double share)
    {
        var act = () => new PortProps(portId, share);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CalculateRoutes_SharedAreasWithNonStrictMatch_UsePreferredPortsAtBothEnds()
    {
        var area = new AreaFeature(Belgium, "BE", [new PortProps("BEANR")]);
        var options = new TradeRouterOptions
        {
            IncludePorts = true,
            PortParameters = new PortParameters { PortsInAreas = [area], StrictArea = false }
        };

        var routes = TradeRoutes.CalculateRoutes(Paris, Tokyo, options);

        routes.Should().ContainSingle();
        routes[0].Properties.PortOrigin!.PortCode.Should().Be("BEANR", "Paris is outside Belgium but within 2000 km of it");
        routes[0].Properties.PortDest!.PortCode.Should().NotBe("BEANR", "Tokyo is too far from Belgium and falls back to its nearest port");
    }
}
