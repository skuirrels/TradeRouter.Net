using System.Collections.Frozen;
using TradeRouter.Data;

namespace TradeRouter.SeaAreas;

/// <summary>
/// Named sea areas a route can report in <c>traversed_sea_areas</c>: seas, gulfs, straits and oceans drawn from
/// Natural Earth 1:50m marine polygons. Names, Wikidata cross-references and Natural Earth IDs are in
/// <see cref="Catalogue"/>, loaded from the embedded selection.
/// </summary>
public static class SeaArea
{
    /// <summary>Red Sea.</summary>
    public const string RedSea = "red_sea";

    /// <summary>Gulf of Aden.</summary>
    public const string GulfOfAden = "gulf_of_aden";

    /// <summary>Arabian Sea.</summary>
    public const string ArabianSea = "arabian_sea";

    /// <summary>Gulf of Oman.</summary>
    public const string GulfOfOman = "gulf_of_oman";

    /// <summary>Persian Gulf.</summary>
    public const string PersianGulf = "persian_gulf";

    /// <summary>Laccadive Sea.</summary>
    public const string LaccadiveSea = "laccadive_sea";

    /// <summary>Bay of Bengal.</summary>
    public const string BayOfBengal = "bay_of_bengal";

    /// <summary>Andaman Sea.</summary>
    public const string AndamanSea = "andaman_sea";

    /// <summary>Strait of Malacca.</summary>
    public const string StraitOfMalacca = "strait_of_malacca";

    /// <summary>Strait of Singapore.</summary>
    public const string StraitOfSingapore = "strait_of_singapore";

    /// <summary>Java Sea.</summary>
    public const string JavaSea = "java_sea";

    /// <summary>South China Sea.</summary>
    public const string SouthChinaSea = "south_china_sea";

    /// <summary>Gulf of Thailand.</summary>
    public const string GulfOfThailand = "gulf_of_thailand";

    /// <summary>Taiwan Strait.</summary>
    public const string TaiwanStrait = "taiwan_strait";

    /// <summary>East China Sea.</summary>
    public const string EastChinaSea = "east_china_sea";

    /// <summary>Yellow Sea.</summary>
    public const string YellowSea = "yellow_sea";

    /// <summary>Bo Hai.</summary>
    public const string BoHai = "bo_hai";

    /// <summary>Korea Strait.</summary>
    public const string KoreaStrait = "korea_strait";

    /// <summary>Sea of Japan.</summary>
    public const string SeaOfJapan = "sea_of_japan";

    /// <summary>Philippine Sea.</summary>
    public const string PhilippineSea = "philippine_sea";

    /// <summary>Mediterranean Sea.</summary>
    public const string MediterraneanSea = "mediterranean_sea";

    /// <summary>Aegean Sea.</summary>
    public const string AegeanSea = "aegean_sea";

    /// <summary>Black Sea.</summary>
    public const string BlackSea = "black_sea";

    /// <summary>Strait of Gibraltar.</summary>
    public const string StraitOfGibraltar = "strait_of_gibraltar";

    /// <summary>North Sea.</summary>
    public const string NorthSea = "north_sea";

    /// <summary>English Channel.</summary>
    public const string EnglishChannel = "english_channel";

    /// <summary>Baltic Sea.</summary>
    public const string BalticSea = "baltic_sea";

    /// <summary>Bay of Biscay.</summary>
    public const string BayOfBiscay = "bay_of_biscay";

    /// <summary>Gulf of Guinea.</summary>
    public const string GulfOfGuinea = "gulf_of_guinea";

    /// <summary>Caribbean Sea.</summary>
    public const string CaribbeanSea = "caribbean_sea";

    /// <summary>Gulf of Mexico.</summary>
    public const string GulfOfMexico = "gulf_of_mexico";

    /// <summary>Straits of Florida.</summary>
    public const string StraitsOfFlorida = "straits_of_florida";

    /// <summary>Mozambique Channel.</summary>
    public const string MozambiqueChannel = "mozambique_channel";

    /// <summary>Coral Sea.</summary>
    public const string CoralSea = "coral_sea";

    /// <summary>Tasman Sea.</summary>
    public const string TasmanSea = "tasman_sea";

    /// <summary>North Atlantic Ocean.</summary>
    public const string NorthAtlanticOcean = "north_atlantic_ocean";

    /// <summary>South Atlantic Ocean.</summary>
    public const string SouthAtlanticOcean = "south_atlantic_ocean";

    /// <summary>North Pacific Ocean.</summary>
    public const string NorthPacificOcean = "north_pacific_ocean";

    /// <summary>South Pacific Ocean.</summary>
    public const string SouthPacificOcean = "south_pacific_ocean";

    /// <summary>Indian Ocean.</summary>
    public const string IndianOcean = "indian_ocean";

    /// <summary>Arctic Ocean.</summary>
    public const string ArcticOcean = "arctic_ocean";

    /// <summary>Southern Ocean.</summary>
    public const string SouthernOcean = "southern_ocean";

    private static readonly Lazy<IReadOnlyList<SeaAreaInfo>> LazyCatalogue =
        new(EmbeddedResources.LoadSeaAreaCatalogue, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<FrozenDictionary<string, SeaAreaInfo>> LazyById =
        new(() => Catalogue.ToFrozenDictionary(area => area.Id, StringComparer.OrdinalIgnoreCase), LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<FrozenSet<string>> LazyIds =
        new(() => Catalogue.Select(area => area.Id).ToFrozenSet(StringComparer.OrdinalIgnoreCase), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Every recognised sea area, in the order of the embedded selection.</summary>
    public static IReadOnlyList<SeaAreaInfo> Catalogue => LazyCatalogue.Value;

    /// <summary>Gets all recognised sea-area identifiers.</summary>
    public static IReadOnlySet<string> ValidSeaAreas => LazyIds.Value;

    /// <summary>Returns the catalogue entry for a recognised sea-area identifier.</summary>
    public static SeaAreaInfo Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return LazyById.Value.TryGetValue(id.Trim(), out var area)
            ? area
            : throw new ArgumentException($"Unknown sea area identifier '{id}'.", nameof(id));
    }
}
