using System.IO.Compression;
using System.Text.Json;
using AwesomeAssertions;
using TradeRouter.Locations;
using TradeRouter.Movements;
using Xunit;

namespace TradeRouter.Tests;

public class RailSupplementTests
{
    private sealed record Entry(string Code, string Name, string Source);

    private static List<Entry> LoadEntries()
    {
        using var stream = typeof(TradeRouterEngine).Assembly.GetManifestResourceStream("TradeRouter.Data.unlocode-rail-supplement.json")
            ?? throw new InvalidOperationException("Rail supplement is not embedded.");
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.EnumerateArray()
            .Select(item => new Entry(
                item.GetProperty("code").GetString()!,
                item.GetProperty("name").GetString()!,
                item.GetProperty("source").GetString()!))
            .ToList();
    }

    private static Dictionary<string, LocationFunctions> LoadPublishedFunctions()
    {
        using var raw = typeof(TradeRouterEngine).Assembly.GetManifestResourceStream("TradeRouter.Data.unlocode.json.gz")!;
        using var gzip = new GZipStream(raw, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gzip);
        var result = new Dictionary<string, LocationFunctions>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in doc.RootElement.EnumerateArray())
            result[item[0].GetString()!] = (LocationFunctions)item[4].GetInt32();
        return result;
    }

    [Fact]
    public void Entries_AreUniqueSourcedAndNameTheirCode()
    {
        var entries = LoadEntries();

        entries.Should().NotBeEmpty();
        entries.Select(entry => entry.Code).Should().OnlyHaveUniqueItems();
        foreach (var entry in entries)
        {
            var record = TradeRouterEngine.Default.UnLocodes.GetByCode(entry.Code);
            record.Should().NotBeNull($"{entry.Code} must be a UN/LOCODE");
            record!.Name.Should().Be(entry.Name, $"{entry.Code} must name its UN/LOCODE record");
            // Beaumont (USBPT) has no UN/LOCODE coordinate and resolves from the port list.
            FluentActions.Invoking(() => TradeRoutes.Locate(entry.Code))
                .Should().NotThrow($"{entry.Code} must resolve to a position for rail legs");
            entry.Source.Should().Contain("https://", $"{entry.Code} must cite a source");
        }
    }

    [Fact]
    public void Entries_AddOnlyTheRailFunctionWhereUnecePublishesNone()
    {
        var published = LoadPublishedFunctions();

        foreach (var entry in LoadEntries())
        {
            var original = published[entry.Code];
            (original & (LocationFunctions.RailTerminal | LocationFunctions.Multimodal))
                .Should().Be(LocationFunctions.None, $"{entry.Code} is supplemented only because UNECE records no rail function");
            TradeRouterEngine.Default.UnLocodes.GetByCode(entry.Code)!.Functions
                .Should().Be(original | LocationFunctions.RailTerminal, $"{entry.Code} must keep its published functions");
        }
    }

    [Fact]
    public void Movement_LosAngelesToChicagoByCodeRoutesOnTheRailNetwork()
    {
        var plan = MovementPlan
            .From(Waypoint.Station("USLAX"))
            .ThenTo(Waypoint.Station("USCHI"), TransportMode.Rail);

        var leg = TradeRoutes.CalculateMovement(plan).Legs.Single();

        leg.Feature.Properties.DistanceBasis.Should().Be("rail_network");
        leg.Length.Should().BeInRange(3_300.0, 3_800.0);
    }

    [Fact]
    public void Movement_TangerMedThroughMontrealToDetroitByCodeRoutesOnTheRailNetwork()
    {
        var plan = MovementPlan
            .From(Waypoint.Port("MAPTM"))
            .ThenTo(Waypoint.Port("CAMTR"), TransportMode.Sea)
            .ThenTo(Waypoint.Station("USDET"), TransportMode.Rail);

        var rail = TradeRoutes.CalculateMovement(plan).Legs[1];

        rail.Feature.Properties.DistanceBasis.Should().Be("rail_network");
        rail.Feature.Properties.Railroads.Should().Contain("CN");
        rail.Length.Should().BeInRange(850.0, 1_000.0);
    }

    [Theory]
    [InlineData("USJOT", "USLAX")] // Chicago-area ramp to the Los Angeles ports.
    [InlineData("USCHS", "USATL")] // Port of Charleston inland.
    [InlineData("USSLC", "USOAK")]
    [InlineData("USEZA", "USCHI")] // Port Elizabeth on-dock rail to Chicago.
    [InlineData("USMIA", "USJAX")] // FEC from PortMiami.
    [InlineData("USSAT", "USLRD")]
    [InlineData("USPIT", "USCMH")]
    [InlineData("USEWI", "USLAX")] // BNSF Logistics Park Chicago.
    [InlineData("USNTI", "USOAK")] // UP Global 2.
    [InlineData("USEEU", "USCHI")] // BNSF Logistics Park Kansas City.
    [InlineData("USV7X", "USCHI")] // NS Croxton.
    [InlineData("USMQM", "USLAX")] // UP Marion, across the river from Memphis.
    [InlineData("USSXT", "USLAX")] // UP Santa Teresa.
    [InlineData("CABRP", "CAMTR")] // CN Brampton.
    [InlineData("CAEDM", "CAPRR")]
    [InlineData("CAREG", "CAWNP")]
    [InlineData("CADEL", "CATOR")] // Deltaport on-dock rail.
    [InlineData("CAMNT", "CAHAL")]
    public void Movement_SupplementedRampsRouteOnTheRailNetworkByCode(string origin, string destination)
    {
        var plan = MovementPlan
            .From(Waypoint.Station(origin))
            .ThenTo(Waypoint.Station(destination), TransportMode.Rail);

        var leg = TradeRoutes.CalculateMovement(plan).Legs.Single();

        leg.Feature.Properties.DistanceBasis.Should().Be("rail_network");
        leg.Feature.Properties.DistanceWarning.Should().BeNull();
        leg.Length.Should().BeGreaterThan(leg.Feature.Properties.StraightLineLength!.Value);
    }

    [Fact]
    public void Movement_AnchorageHasNoRailLinkToTheLower48()
    {
        var plan = MovementPlan
            .From(Waypoint.Station("USANC"))
            .ThenTo(Waypoint.Station("USCHI"), TransportMode.Rail);

        var leg = TradeRoutes.CalculateMovement(plan).Legs.Single();

        leg.Feature.Properties.DistanceBasis.Should().NotBe("rail_network");
        leg.Feature.Properties.DistanceWarning.Should().Contain("No connected main line");
    }

    [Theory]
    [InlineData("USBNA")] // Nashville: no source names a container terminal.
    [InlineData("USBHM")] // Birmingham: the NS terminal at McCalla is 31 km out.
    [InlineData("USTUL")] // Tulsa: only the bulk Port of Catoosa, 24 km out.
    [InlineData("USRCX")] // Rochelle: UP idled Global III in 2019.
    public void Movement_UnsourcedUsMarketsStillNeedADeclaredRailFunction(string code)
    {
        var plan = MovementPlan
            .From(Waypoint.Station("USCHI"))
            .ThenTo(Waypoint.Station(code), TransportMode.Rail);

        FluentActions.Invoking(() => TradeRoutes.CalculateMovement(plan))
            .Should().Throw<ArgumentException>().WithMessage("*AdditionalLocationFunctions*");
    }
}
