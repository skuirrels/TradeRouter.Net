using TradeRouter.Common;
using TradeRouter.Passages;

namespace TradeRouter.Movements;

/// <summary>
/// The operational allowance a sea leg receives on top of its travelling time, chosen by trade corridor.
/// </summary>
/// <remarks>
/// A shortest-path route sails straight from load port to discharge port. A scheduled container service does
/// not: it calls at several ports on the way, slows in restricted water and waits for berths. The allowance
/// is the fraction of travelling time that covers those things. One fraction cannot fit every trade, because
/// Asia–Europe loops make many intermediate calls while transpacific services sail almost direct and faster.
/// <para>
/// The fractions below were fitted on 12 September 2026 against observed port-to-port sailings, berth
/// departure to berth arrival, for legs departing in 2025 and 2026, from a proprietary
/// dataset: eleven direct lanes, 8,600 sailings, each fraction the median of observed hours divided by
/// this library's travelling hours at 16 knots, minus one. Corridors without observations keep the historical
/// default of 0.20. Details are recorded in DATA_PROVENANCE.md.
/// </para>
/// </remarks>
public static class SeaServiceAllowance
{
    /// <summary>Fraction applied where no corridor has been fitted, the value every leg used before calibration.</summary>
    public const double DefaultFraction = 0.20;

    /// <summary>Corridor label reported when the request fixes its own fraction.</summary>
    public const string OverrideCorridor = "override";

    /// <summary>Corridor label reported when no fitted corridor matches.</summary>
    public const string DefaultCorridor = "default";

    /// <summary>Fitted corridor fractions, keyed by the label reported on each leg.</summary>
    public static IReadOnlyDictionary<string, double> Fractions { get; } = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        ["asia-north-europe"] = 0.63,   // Malacca or Sunda, Suez and Gibraltar: CNSHA–NLRTM, CNSHA–DEHAM, CNSHA–FRLEH, VNSGN–NLRTM
        ["asia-mediterranean"] = 1.33,  // Malacca or Sunda and Suez, no Gibraltar: CNSHA–ITGOA, CNSHA–GRPIR
        ["gulf-europe"] = 1.57,         // Hormuz and Suez: AEJEA–NLRTM
        ["transpacific"] = 0.12,        // no passage, East Asia to the Americas: CNSHA–USLAX, VNSGN–USLAX
        ["panama"] = 0.24,              // Panama: CNSHA–USNYC
        ["transatlantic"] = 0.80,       // no passage, Europe or Africa to the Americas: NLRTM–USNYC
        ["asia-north-europe-cape"] = 0.22,  // no Suez, Asia to North Europe: the four asia-north-europe lanes on the Cape route
        ["asia-mediterranean-cape"] = 0.37, // no Suez, Asia to the Mediterranean: the two asia-mediterranean lanes on the Cape route
        ["gulf-europe-cape"] = 0.46,        // no Suez, Hormuz and the Cape: AEJEA–NLRTM on the Cape route
        [DefaultCorridor] = DefaultFraction
    };

    /// <summary>
    /// Chooses the corridor and fraction for a sea leg from the passages it crosses and its end points.
    /// </summary>
    /// <param name="traversedPassages">Passages the routed leg crosses, as reported by the router.</param>
    /// <param name="from">Start of the leg.</param>
    /// <param name="to">End of the leg.</param>
    /// <returns>The corridor label and the fraction of travelling time to add.</returns>
    public static (string Corridor, double Fraction) Resolve(IEnumerable<string>? traversedPassages, Coordinate from, Coordinate to)
    {
        var passages = new HashSet<string>(traversedPassages ?? [], StringComparer.OrdinalIgnoreCase);
        bool suez = passages.Contains(Passage.Suez);
        bool eastOfIndia = passages.Contains(Passage.Malacca) || passages.Contains(Passage.Sunda);
        double lonFrom = from.WithNormalizedLongitude().Longitude;
        double lonTo = to.WithNormalizedLongitude().Longitude;

        bool panama = passages.Contains(Passage.Panama);
        bool americanEnd = InAmericas(lonFrom) || InAmericas(lonTo);
        bool europeanEnd = InEurope(lonFrom, from.Latitude) || InEurope(lonTo, to.Latitude);
        bool eastAsianEnd = lonFrom >= 90.0 || lonTo >= 90.0;

        string corridor;
        if (panama && americanEnd)
        {
            corridor = "panama";
        }
        else if (suez && passages.Contains(Passage.Ormuz))
        {
            corridor = "gulf-europe";
        }
        else if (suez && eastOfIndia)
        {
            // Through Gibraltar the leg ends in North Europe, or beyond it in the Americas, which is unfitted.
            corridor = passages.Contains(Passage.Gibraltar)
                ? (americanEnd ? DefaultCorridor : "asia-north-europe")
                : "asia-mediterranean";
        }
        else if (!suez && europeanEnd && passages.Contains(Passage.SouthAfrica) && passages.Contains(Passage.Ormuz))
        {
            corridor = "gulf-europe-cape";
        }
        else if (!suez && europeanEnd && !americanEnd && ((eastOfIndia && passages.Contains(Passage.SouthAfrica)) || (panama && eastAsianEnd)))
        {
            // Asia–Europe services that avoid Suez sail round the Cape. A leg the router sends through Panama,
            // because the caller closed Suez but not Panama, is a similar distance and takes the same corridor.
            // Round the Cape only the Mediterranean is reached through Gibraltar.
            corridor = passages.Contains(Passage.Gibraltar) ? "asia-mediterranean-cape" : "asia-north-europe-cape";
        }
        else if (!suez && CrossesPacific(lonFrom, lonTo))
        {
            corridor = "transpacific";
        }
        else if (!suez && CrossesAtlantic(lonFrom, lonTo))
        {
            corridor = "transatlantic";
        }
        else
        {
            corridor = DefaultCorridor;
        }

        return (corridor, Fractions[corridor]);
    }

    // East Asia sits east of 90°E, the American Pacific coast west of 90°W.
    private static bool CrossesPacific(double lonA, double lonB) =>
        (lonA >= 90.0 && lonB <= -90.0) || (lonB >= 90.0 && lonA <= -90.0);

    // Europe and Africa lie between 30°W and 60°E; the Americas west of 30°W. No passage separates them.
    private static bool CrossesAtlantic(double lonA, double lonB) =>
        (InEuropeOrAfrica(lonA) && InAmericas(lonB)) || (InEuropeOrAfrica(lonB) && InAmericas(lonA));

    private static bool InEuropeOrAfrica(double lon) => lon is >= -30.0 and <= 60.0;

    // Europe, including the Mediterranean and Black Sea coasts, lies north of 30°N between 30°W and 42°E.
    // The bound keeps West Africa, the Red Sea and the Gulf out.
    private static bool InEurope(double lon, double lat) => lat >= 30.0 && lon is >= -30.0 and <= 42.0;

    private static bool InAmericas(double lon) => lon < -30.0 && lon > -170.0;
}
