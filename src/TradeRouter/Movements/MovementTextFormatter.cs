using System.Globalization;
using System.Text;
using TradeRouter.Passages;

namespace TradeRouter.Movements;

internal static class MovementTextFormatter
{
    // The Basis column fits the longest basis, "teu_average_weight", with a space before it.
    internal static string Format(MovementResult movement, bool includeExplanations)
    {
        var output = new StringBuilder();
        AppendInvariant(output,
            $"{"Leg",-4}{"Kind",-10}{"Mode",-6}{"From",-7}{"To",-7}{"Distance",9} {"Distance basis",20}{"Modelled transit time",24}{"CO2e rate",16}{"CO2e per tonne",16}{"CO2e total",12}{"Basis",20}  Choke points");
        AppendInvariant(output,
            $"{"",-34}{"",9} {"",20}{"hours",24}{"g per t-km",16}{"kg per t cargo",16}{"kg",12}");

        foreach (var leg in movement.Legs)
        {
            AppendInvariant(output,
                $"{leg.Sequence,-4}{leg.Leg.Kind,-10}{leg.Leg.Mode,-6}{leg.From.Label,-7}{leg.To.Label,-7}{leg.Length,9:N0} {movement.Units}{leg.Feature.Properties.DistanceBasis,20}{leg.TransitHours,24:N1}{leg.Co2eGramsPerTonneKm,16:N1}{leg.Co2eKgPerTonne,16:N1}{FormatNullable(leg.Co2eKg),12}{leg.Co2eBasis,20}  {FormatPassages(leg.Feature.Properties.TraversedPassages)}");
        }

        AppendInvariant(output,
            $"{"Total",-34}{movement.TotalLength,9:N0} {movement.Units}{"",20}{movement.TotalTransitHours,24:N1}{"",16}{movement.TotalCo2eKgPerTonne,16:N1}{FormatNullable(movement.TotalCo2eKg),12}{"",20}  {FormatCargo(movement)}");
        AppendInvariant(output,
            $"Modelled minimum = {movement.TotalDurationHours:N1} h travel + {movement.TotalOperationalAllowanceHours:N1} h sea operations + {movement.TotalPortHours:N0} h port handling + {movement.TotalConnectionHours:N0} h connections = {movement.TotalTransitHours:N1} h ({movement.TotalTransitHours / 24.0:N1} days)");

        if (includeExplanations)
        {
            output.AppendLine(FormatDistanceBasisExplanation(movement));
            output.AppendLine("Timing         = planning lower bound from configured assumptions; excludes carrier schedules, customs and disruption");
            output.AppendLine("CO2e rate      = grams of CO2e emitted moving 1 tonne 1 km (configured factor for the mode)");
            output.AppendLine("CO2e per tonne = rate × leg distance: kg of CO2e for each tonne of cargo carried over the leg");
            output.AppendLine(FormatTotalExplanation(movement));
        }

        return output.ToString().TrimEnd();
    }

    private static string FormatPassages(IReadOnlyList<string>? passages) =>
        passages is null or { Count: 0 }
            ? string.Empty
            : string.Join(", ", passages.Select(Passage.GetDisplayName));

    private static string FormatDistanceBasisExplanation(MovementResult movement)
    {
        const string network = "Distance basis = maritime_network/road_network are network routes";
        const string calculated = "; circuity_estimate is a planning estimate; great_circle is straight-line";
        bool hasExternallyProvidedRoute = movement.Legs.Any(
            leg => string.Equals(leg.Feature.Properties.DistanceBasis, "supplied", StringComparison.Ordinal));

        return hasExternallyProvidedRoute
            ? $"{network}; supplied is an externally provided route result{calculated}"
            : $"{network}{calculated}";
    }

    private static string FormatCargo(MovementResult movement)
    {
        string tonnes = movement.CargoTonnes.HasValue
            ? FormattableString.Invariant($"for {movement.CargoTonnes:N0} t of cargo")
            : string.Empty;
        string teu = movement.CargoTeu.HasValue
            ? FormattableString.Invariant($"{(tonnes.Length == 0 ? "for" : "in")} {movement.CargoTeu:N0} TEU")
            : string.Empty;
        return string.Join(" ", new[] { tonnes, teu }.Where(value => value.Length > 0));
    }

    private static string FormatNullable(double? value) =>
        value?.ToString("N0", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string FormatTotalExplanation(MovementResult movement)
    {
        if (!movement.TotalCo2eKg.HasValue)
            return "CO2e total     = not calculated because cargo weight or TEU count was not supplied";

        if (!movement.CargoTeu.HasValue)
            return "CO2e total     = kg of CO2e for this shipment: CO2e per tonne × cargo weight";

        double? seaRate = movement.Legs
            .FirstOrDefault(leg => leg.Leg.Mode == TransportMode.Sea)?
            .Feature.Properties.Co2eGramsPerTeuKm;
        string rate = seaRate.HasValue
            ? seaRate.Value.ToString("N0", CultureInfo.InvariantCulture)
            : "the configured";
        return $"CO2e total     = kg of CO2e for this shipment: {rate} g per TEU-km on sea legs, the greater of cargo weight and 10 t per TEU on road and rail legs, cargo weight on air legs";
    }

    private static void AppendInvariant(StringBuilder output, FormattableString line) =>
        output.AppendLine(line.ToString(CultureInfo.InvariantCulture));
}
