namespace TradeRouter.SeaAreas;

/// <summary>
/// Catalogue entry for a named sea area.
/// </summary>
/// <param name="Id">Lower snake_case identifier taken from the English name, such as "red_sea".</param>
/// <param name="Name">English display name, such as "Red Sea".</param>
/// <param name="Wikidata">Wikidata item, as a cross-reference only: the North and South halves of an ocean share one item.</param>
/// <param name="NeId">Natural Earth <c>ne_id</c> of the polygon in <c>ne_50m_geography_marine_polys</c>.</param>
public sealed record SeaAreaInfo(string Id, string Name, string Wikidata, long NeId);
