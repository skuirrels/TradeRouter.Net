using AwesomeAssertions;
using TradeRouter.Common;
using TradeRouter.Locations;
using Xunit;

namespace TradeRouter.Tests;

/// <summary>
/// Codes added add-only from the UNECE 2025-1 release by tools/merge-unlocode-release.py.
/// </summary>
public class UnLocodeReleaseMergeTests
{
    [Fact]
    public void Database_HoldsTheOriginalImportPlusTheAddedCodes()
    {
        TradeRouterEngine.Default.UnLocodes.Count.Should().Be(116_022);
        TradeRouterEngine.Default.UnLocodes.CountWithCoordinates.Should().Be(113_543);
    }

    [Theory]
    [InlineData("ZAZCO", "Malmesbury", 18.73333, -33.45, LocationFunctions.Multimodal)]
    [InlineData("DEAL9", "Allach", 11.45, 48.2, LocationFunctions.Multimodal)]
    [InlineData("THCHO", "Chonburi", 100.98333, 13.36667, LocationFunctions.Multimodal)]
    [InlineData("VNTNT", "Tay Ninh", 106.1, 11.3, LocationFunctions.Multimodal)]
    public void Locate_ResolvesAddedCodesAtTheirUnecePosition(string code, string name, double lon, double lat, LocationFunctions functions)
    {
        var record = TradeRouterEngine.Default.UnLocodes.GetByCode(code)!;
        record.Name.Should().Be(name);
        record.Functions.Should().Be(functions);
        record.CoordinateSource.Should().Be("UNECE");

        var resolved = TradeRoutes.Locate(code);
        resolved.Source.Should().Be("unlocode");
        resolved.Coordinate.Should().Be(new Coordinate(lon, lat));
    }

    [Fact]
    public void Locate_KeepsExistingCodesAsTheyWere()
    {
        // Bloemfontein was in the original import and keeps its UNECE position.
        TradeRoutes.Locate("ZABFN").Coordinate.Should().Be(new Coordinate(26.2, -29.13333));
    }

    [Theory]
    [InlineData("NOKRN", "Kristiansund")] // 2025-1 adds NOKRN as Kroderen, 300 km inland.
    [InlineData("NLWIL", "Willemstad")]   // 2025-1 adds NLWIL as Wilp.
    [InlineData("UASVP", "Sevastopol")]
    public void Locate_LeavesPortListCodesToThePortList(string code, string portName)
    {
        TradeRouterEngine.Default.UnLocodes.GetByCode(code).Should().BeNull("the merge does not add codes the port list uses");
        var resolved = TradeRoutes.Locate(code);
        resolved.Source.Should().Be("ports");
        resolved.Name.Should().StartWith(portName);
    }

    [Theory]
    [InlineData("CZBYT", 17.19, 49.54)] // Bystrocice, published at 171.33E.
    [InlineData("LKHET", 80.48, 7.51)]  // Hettimulla, Sri Lanka, published at 80.08W.
    [InlineData("LKWLP", 79.89, 6.94)]  // Wellampitiya, published at 0,0.
    public void AddedCodes_WithACoordinateFarOutsideTheirCountry_IgnoreTheUneceValue(string code, double lon, double lat)
    {
        // The merge held the UNECE coordinate back; the added-code fill places the code from its GeoNames link.
        var record = TradeRouterEngine.Default.UnLocodes.GetByCode(code)!;
        record.CoordinateSource.Should().StartWith("Added fill, GeoNames ");
        Haversine.DistanceKm(record.Coordinate!.Value, new Coordinate(lon, lat)).Should().BeLessThan(2.0);
    }
}
