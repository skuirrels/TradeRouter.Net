using FluentAssertions;
using TradeRouter.Common;
using TradeRouter.GeoJson;
using TradeRouter.Graph;
using TradeRouter.Movements;
using TradeRouter.Ports;
using Xunit;

namespace TradeRouter.Tests;

public class CorrectnessRegressionTests
{
    [Fact]
    public void DirectedGraph_BidirectionalDijkstra_UsesIncomingEdgesForBackwardSearch()
    {
        var graph = new MaritimeGraph();
        int a = graph.AddNode(new Coordinate(0, 0));
        int b = graph.AddNode(new Coordinate(1, 0));
        int c = graph.AddNode(new Coordinate(2, 0));
        graph.AddDirectedEdge(a, b, 1);
        graph.AddDirectedEdge(b, c, 1);
        graph.BuildIndex();

        var result = graph.ShortestPath(new Coordinate(0, 0), new Coordinate(2, 0));

        result.LengthKm.Should().Be(2);
        result.Path.Should().Equal(new Coordinate(0, 0), new Coordinate(1, 0), new Coordinate(2, 0));
    }

    [Fact]
    public void AStar_CustomWeights_RemainsOptimalWhenWeightsAreBelowGeodesicDistance()
    {
        var graph = new MaritimeGraph();
        int a = graph.AddNode(new Coordinate(0, 0));
        int b = graph.AddNode(new Coordinate(0, 10));
        int c = graph.AddNode(new Coordinate(2, 0));
        graph.AddDirectedEdge(a, c, 10);
        graph.AddDirectedEdge(a, b, 1);
        graph.AddDirectedEdge(b, c, 1);
        graph.BuildIndex();

        var dijkstra = graph.ShortestPath(new Coordinate(0, 0), new Coordinate(2, 0), algorithm: "dijkstra");
        var astar = graph.ShortestPath(new Coordinate(0, 0), new Coordinate(2, 0), algorithm: "astar");

        astar.LengthKm.Should().Be(2);
        astar.LengthKm.Should().Be(dijkstra.LengthKm);
    }

    [Fact]
    public void IndexedGraph_IsFrozenAgainstMutation()
    {
        var graph = new MaritimeGraph();
        graph.AddNode(new Coordinate(0, 0));
        graph.BuildIndex();

        graph.IsReadOnly.Should().BeTrue();
        var act = () => graph.AddNode(new Coordinate(1, 1));
        act.Should().Throw<InvalidOperationException>().WithMessage("*read-only*");
    }

    [Fact]
    public void PortDatabase_DuplicateCodeRequiresExplicitDisambiguation()
    {
        var west = NewPort("XXABC", "West", -120, 0);
        var east = NewPort("XXABC", "East", 120, 0);
        var database = new PortDatabase([west, east]);

        database.GetByCodeCandidates("xxabc").Should().Equal(west, east);
        var ambiguous = () => database.GetByCode("XXABC");
        ambiguous.Should().Throw<InvalidOperationException>().WithMessage("*ambiguous*GetByCode(code, near)*");
        database.GetByCode("XXABC", new Coordinate(121, 0)).Should().BeSameAs(east);
    }

    [Fact]
    public void PortCountryFilter_NormalizesSpacesAndUnderscores()
    {
        var database = new PortDatabase(
        [
            NewPort("USAAA", "US", 0, 0, "United_states"),
            NewPort("GBAAA", "GB", 10, 0, "United Kingdom")
        ]);

        database.QueryClosestPort(new Coordinate(9, 0), country: "United States")!.PortCode.Should().Be("USAAA");
    }

    [Fact]
    public void DatelinePolygon_ContainsDatelineButNotGreenwich()
    {
        var area = new AreaFeature(
        [
            new Coordinate(170, -10), new Coordinate(-170, -10),
            new Coordinate(-170, 10), new Coordinate(170, 10)
        ], "date-line");

        area.Contains(new Coordinate(179, 0)).Should().BeTrue();
        area.Contains(new Coordinate(-179, 0)).Should().BeTrue();
        area.Contains(new Coordinate(0, 0)).Should().BeFalse();
        area.Area.Should().BeApproximately(400, 0.001);
        area.DistanceToPoint(new Coordinate(0, 0)).Should().BeGreaterThan(18000);
    }

    [Fact]
    public void AreaDistance_UsesNearestEdgeRatherThanOnlyVertices()
    {
        var area = new AreaFeature(
        [
            new Coordinate(0, 0), new Coordinate(20, 0),
            new Coordinate(20, 1), new Coordinate(0, 1)
        ], "wide");

        area.DistanceToPoint(new Coordinate(10, -1)).Should().BeApproximately(111.2, 2.0);
    }

    [Fact]
    public void InvalidRouteOptions_AreRejectedBeforeRouting()
    {
        var options = new TradeRouterOptions { SpeedKnots = double.NaN, Algorithm = "guess" };
        var act = () => TradeRoutes.Calculate(new Coordinate(0, 0), new Coordinate(1, 1), options: options);

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*speed*");
    }

    [Fact]
    public void UnknownRoutingAlgorithm_IsRejectedRatherThanFallingBack()
    {
        var options = new TradeRouterOptions { Algorithm = "guess" };
        var act = () => TradeRoutes.Calculate(new Coordinate(0, 0), new Coordinate(1, 1), options: options);

        act.Should().Throw<ArgumentException>().WithMessage("*algorithm*");
    }

    [Fact]
    public void UnknownPassageRestriction_IsRejectedRatherThanIgnored()
    {
        var options = new TradeRouterOptions { Restrictions = ["not_a_passage"] };
        var act = () => TradeRoutes.Calculate(new Coordinate(0, 0), new Coordinate(1, 1), options: options);

        act.Should().Throw<ArgumentException>().WithMessage("*Unknown passage*");
    }

    [Fact]
    public void MovementParser_PreservesWaypointKinds()
    {
        var leg = MovementParser.Parse("Airport GBLHR to Port GBFXT Road").Single();

        leg.FromKind.Should().Be(WaypointKind.Airport);
        leg.ToKind.Should().Be(WaypointKind.Port);
    }

    [Fact]
    public void Movement_RejectsSemanticEndpointMismatch()
    {
        var act = () => TradeRoutes.CalculateMovement("Airport CNSHG to Port GBFXT Road");

        act.Should().Throw<ArgumentException>().WithMessage("*CNSHG*declared as Airport*sea port*");
    }

    [Fact]
    public void Movement_RejectsDisconnectedLegChain()
    {
        var request = new MovementRequest
        {
            Legs =
            [
                new MovementLeg(Location.FromCoordinate(new Coordinate(0, 0)), Location.FromCoordinate(new Coordinate(1, 0)), TransportMode.Road, LegKind.Pickup),
                new MovementLeg(Location.FromCoordinate(new Coordinate(2, 0)), Location.FromCoordinate(new Coordinate(3, 0)), TransportMode.Road, LegKind.Delivery)
            ]
        };

        var act = () => TradeRouterEngine.Default.CalculateMovement(request);
        act.Should().Throw<ArgumentException>().WithMessage("*continuous chain*");
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Movement_RejectsInvalidCargo(double cargoTonnes)
    {
        var request = new MovementRequest
        {
            Legs = [new MovementLeg(Location.FromCoordinate(new Coordinate(0, 0)), Location.FromCoordinate(new Coordinate(1, 0)), TransportMode.Road)],
            CargoTonnes = cargoTonnes
        };

        var act = () => TradeRouterEngine.Default.CalculateMovement(request);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GeometryFactory_SplitsOrdinaryAntimeridianCrossing()
    {
        var geometry = GeoJsonGeometry.FromCoordinates(
        [
            new Coordinate(170, 10),
            new Coordinate(-170, 20)
        ]);

        var multiLine = geometry.Should().BeOfType<GeoJsonMultiLineString>().Subject;
        multiLine.Coordinates.Should().HaveCount(2);
        multiLine.Coordinates[0].Should().HaveCount(2);
        multiLine.Coordinates[0][^1][0].Should().Be(180);
        multiLine.Coordinates[1][0][0].Should().Be(-180);
        multiLine.Coordinates.SelectMany(segment => segment)
            .Should().OnlyContain(position => position[0] >= -180 && position[0] <= 180);
    }

    [Fact]
    public void GeometryFactory_SplitsWhenInputContainsExactPositiveBoundary()
    {
        var geometry = GeoJsonGeometry.FromCoordinates(
        [
            new Coordinate(170, 0),
            new Coordinate(180, 0),
            new Coordinate(190, 0)
        ]);

        var multiLine = geometry.Should().BeOfType<GeoJsonMultiLineString>().Subject;
        multiLine.Coordinates.Should().HaveCount(2);
        multiLine.Coordinates[0].SelectMany(position => position).Should().Equal(170, 0, 180, 0);
        multiLine.Coordinates[1].SelectMany(position => position).Should().Equal(-180, 0, -170, 0);
    }

    [Fact]
    public void GeometryConverter_RoundTripsMultiLineString()
    {
        GeoJsonGeometry geometry = new GeoJsonMultiLineString
        {
            Coordinates =
            [
                [new double[] { 170, 0 }, new double[] { 180, 0 }],
                [new double[] { -180, 0 }, new double[] { -170, 0 }]
            ]
        };

        string json = System.Text.Json.JsonSerializer.Serialize(geometry);
        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<GeoJsonGeometry>(json);

        roundTripped.Should().BeOfType<GeoJsonMultiLineString>();
        roundTripped!.Positions.Should().HaveCount(4);
    }

    [Fact]
    public void LineString_DirectSerialization_RemainsCompatible()
    {
        var line = GeoJsonLineString.FromCoordinates(
        [
            new Coordinate(1, 2),
            new Coordinate(3, 4)
        ]);

        string json = System.Text.Json.JsonSerializer.Serialize(line);

        json.Should().Contain("\"type\":\"LineString\"");
        json.Should().Contain("\"coordinates\":[[1,2],[3,4]]");
    }

    [Fact]
    public void GeometryFactory_PreservesTwoPositionsForZeroLengthRoute()
    {
        var point = new Coordinate(1, 2);

        var geometry = GeoJsonGeometry.FromCoordinates([point, point]);

        geometry.Should().BeOfType<GeoJsonLineString>();
        geometry!.Positions.Should().HaveCount(2);
    }

    [Fact]
    public void BlockedRoute_SerializesNullGeometry()
    {
        var route = TradeRoutes.Calculate(
            new Coordinate(103.85457, 1.25760),
            new Coordinate(23.62904, 37.94056),
            restrictions: [Passages.Passage.Suez, Passages.Passage.Gibraltar]);

        route.Geometry.Should().BeNull();
        route.ToJson().Should().Contain("\"geometry\":null");
    }

    private static Port NewPort(string code, string name, double longitude, double latitude, string country = "XX") => new()
    {
        PortCode = code,
        Name = name,
        Country = country,
        Coordinate = new Coordinate(longitude, latitude)
    };
}
