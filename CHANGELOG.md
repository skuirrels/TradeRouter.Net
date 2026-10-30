# Changelog

All notable changes to TradeRouter.Net are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.6.0] - 2026-10-30

### Location resolution

- A code no longer resolves to a same-named port-list entry more than 50 km from its UN/LOCODE position; the UN/LOCODE position wins. The port list had filed namesakes under 277 codes over 100 km away, 194 of them in the United States, so Pittsburgh (`USPIT`) resolved to Pilottown, Louisiana, Phoenix (`USPHX`) to Louisiana, Nashville (`USBNA`) and Birmingham (`USBHM`) to Michigan, and Monterrey (`MXMTY`) to Chiapas. Rail legs to those codes now route to the right city: Chicago to Phoenix is 2,913 km on the network instead of 1,470 km via New Orleans, and Monterrey to Laredo routes on the network instead of a 1,398 km straight line. The resolved `Port` is null for these codes. Port-to-port `Calculate` still uses the port list and is unchanged.
- 31 codes whose UN/LOCODE or GeoNames position was wrong where the port list was right are corrected from the World Port Index or GeoNames, so they keep resolving to the port list: for example Pago Pago (`ASPPG`), which UNECE lists in the northern hemisphere, High Point (`USHPZ`), which it places in West Virginia, and Rafina (`GRRAF`), whose GeoNames link named a hamlet in Thessaly rather than the Attica ferry port.

### Rail timing

- Rail legs can carry time allowances on top of distance divided by speed, reported in `transit_hours`: `MovementRequest.RailOriginTerminalHours` and `RailDestinationTerminalHours` for terminal dwell, `RailInterchangeHours` for each interchange between Class I railroads, and `RailBorderCrossingHours` for each crossing of the US–Canada or US–Mexico border. All default to zero, so rail times are unchanged unless a request sets them. Negative or non-finite values are rejected.
- Rail legs report `rail_terminal_hours`, `rail_interchanges`, `rail_interchange_hours`, `border_crossings` and `border_hours`, and movements report `total_rail_terminal_hours`, `total_rail_interchange_hours` and `total_border_hours`. The same figures are on `LegResult` and `MovementResult`, and the text summary names them when a request sets them.
- Interchanges are estimated by `RailServiceAllowance.CountClassIInterchanges` as the number of Class I railroads owning at least 1,000 km of a routed leg, less one, because the rail network names track owners rather than train operators. `MovementRequest.RailInterchangeCounts` sets the count for a leg when it is known, including great-circle and supplied rail legs. Border crossings are counted from the UN/LOCODE country of each end.

### Rail terminals

- 44 more US codes are rail terminals in the rail supplement, so they can be named as stations and joined by rail legs without declaring `AdditionalLocationFunctions`. UNECE records no rail function for them, and a probe of 81 major US rail markets found 54 rejected. Each entry cites a page naming the terminal, mostly the Norfolk Southern, BNSF, CN and Union Pacific terminal lists. They include Joliet, Portland, Stockton, Lathrop, Charleston, Jacksonville, Mobile, Columbus, Philadelphia, Worcester, Charlotte, Greer, Salt Lake City, Phoenix, Reno, Saint Paul, Omaha, Indianapolis, Cincinnati, Louisville, San Antonio, Pittsburgh and Elizabeth. Albuquerque, Birmingham, Boston, Nashville, Orlando, Tampa and Tulsa still need `AdditionalLocationFunctions`, because no source documents a terminal within 25 km.
- 21 more codes are rail terminals in the rail supplement: the main Canadian hubs Brampton, Edmonton, Regina, Moncton, Surrey, Delta (Deltaport) and Pitt Meadows, and US suburban ramps with their own UN/LOCODE, Elwood, Edgerton, Cicero, Northlake, Dolton, Harvey, Bensenville, Bedford Park, North Bergen, Croxton, Austell, Rossville, Marion (Arkansas) and Santa Teresa. Each cites the railroad's or terminal operator's page. Rochelle (Union Pacific Global III) is left out because it was idled in 2019.
- 11 Mexican codes are rail terminals in the rail supplement: Manzanillo, Veracruz, Altamira, Guadalajara, Queretaro, Toluca, Silao, Aguascalientes, Hermosillo, Chihuahua and Saltillo. A probe of 25 major Mexican rail markets found 17 rejected as rail stations. Each entry cites a page naming the terminal, from port and terminal operators, Grupo SID, a rate catalogue registered with the rail regulator and news reports. Ciudad Juarez, Piedras Negras, Irapuato, Ramos Arizpe, Tampico and Puebla still need `AdditionalLocationFunctions`, because no source found names a terminal there.

## [2.5.0] - 2026-10-30

### Location data

- Tanger Med (`MAPTM`) has a coordinate, so movements can name it by code. UNECE publishes none and the port list does not hold it, so it previously needed a caller coordinate. Port-to-port `Calculate("MAPTM", ...)` still needs coordinates, because the port list lacks it.
- Montreal (`CAMTR`) is a rail terminal in the rail supplement, so a sea leg into Montreal can continue by rail on the network without declaring `AdditionalLocationFunctions`. It also gains a UN/LOCODE coordinate at the port; the port list still places it for movements, so routes are unchanged.
- 414 more sea ports have coordinates, from the NGA World Port Index, Wikidata and GeoNames, so movements can name them by code. `tools/fill-sea-port-coordinates.py` builds them from pinned sources and places a port only when a code link, a second source, its UN/LOCODE subdivision or its distance from the coast supports the position. 1,330 sea-port codes still have no position.

## [2.4.0] - 2026-10-29

### Sea routing

- The Volga–Don Canal is a passage: `Passage.VolgaDon`, `volga_don`. It is the only lane out of the Caspian Sea, so routes leaving the Caspian now report it in `traversed_passages`, and listing it in `Restrictions` closes it. It is open by default, so routes are unchanged.
- `tools/build-marnet-data.py` tags the canal stretch of the untagged searoute-py line between the Caspian Sea and Taganrog Bay. The lower Volga and lower Don stay untagged, so Astrakhan and Rostov-on-Don keep their routes to their own seas when the canal is closed.

## [2.3.0] - 2026-10-29

### Sea routing

- The Caspian Sea is a sea area: `SeaArea.CaspianSea`, `caspian_sea`, Wikidata Q5484, from the same pinned Natural Earth 1:50m marine polygons. Routes between Caspian ports, and routes leaving it over the Volga–Don link, now report it in `traversed_sea_areas`.

## [2.2.0] - 2026-10-26

### Road routing

- `GoogleRoutesRoadRouteProvider` routes road legs through the Google Maps Platform Routes API, as an alternative to `OsrmRoadRouteProvider`. It sends `DRIVE` requests without live traffic, so calls stay on the Compute Routes Essentials SKU, and reports an endpoint snapped beyond `maximumSnapDistanceMeters` as `OutsideCoverage`.
- `IRoadRouteProvider` documents the contract for custom providers: which status to return, how each `RoadRoutingMode` treats it, and the thread-safety requirement.
- Sample examples 16 and 17 route road legs with Google when `TRADEROUTER_GOOGLE_MAPS_API_KEY` is set.

### Documentation

- The README compares the OSRM, Google and custom road providers, covers Google's caching and display terms, and shows a custom provider.

## [2.1.1] - 2026-10-24

### Documentation

- The package README covers sea areas throughout: a Sea area term, how areas are found, the 42 recognised identifiers, which movement legs carry them, the limits of the 1:50m polygons, and their measured cost and load time.
- The output-model diagram shows `traversed_sea_areas` on `TradeRouterProperties`.

No code changes.

## [2.1.0] - 2026-10-24

### Sea routing

- `ReturnSeaAreas` reports the named sea areas a route passes through as `traversed_sea_areas`, in the order it enters them, with `enter_km` and `exit_km` from the start of the route. Each sea leg of a movement carries it too, counted from the start of the leg.
- 42 seas, gulfs, straits and oceans from Natural Earth 1:50m marine polygons, as `SeaArea` constants with a `SeaArea.Catalogue` of names, Wikidata items and Natural Earth IDs.
- `TradeRoutes.Calculate` takes a `returnSeaAreas` argument. The 2.0.0 signature is kept, so code compiled against 2.0.0 runs unchanged.

## [2.0.0] - 2026-10-15

First release on nuget.org.

### Sea routing

- Shortest sea route between two UN/LOCODE port codes or coordinates on the Marnet network from searoute-py 1.6.0, returned as RFC 7946 GeoJSON.
- Antimeridian crossings split into `MultiLineString`; routes with no path return a feature with null geometry.
- Canals and straits can be avoided by name. The Northwest Passage stays closed unless `AllowNorthwest` is set.
- Inland points resolve to the nearest terminal, and preferred ports can be weighted per area.
- Array-backed graph search, an instance `TradeRouterEngine` and the static `TradeRoutes` facade.

### Locations

- The UNECE UN/LOCODE list is embedded and resolves every movement location by code.
- A sourced coordinate supplement fills 17,771 codes UNECE publishes without coordinates, and corrects 147 UNECE coordinates that fall far outside their country.
- A reviewed alias list maps 61 official UN/LOCODEs to the differently coded record in the port list.
- Sea-port and rail-terminal function supplements confirm ports and freight rail terminals that UNECE under-records.

### Multi-leg movements

- A typed `MovementPlan` API routes pickup, main and delivery legs by sea, road, rail and air, with totals and a GeoJSON `FeatureCollection`.
- A modelled minimum transit time: 16 knot sea speed, port dwell, connection allowances and a sea service allowance fitted per trade corridor, including Cape corridors.
- Optional OSRM road routing through `IRoadRouteProvider`, with a calibrated distance-decay estimator as the fallback.
- North American rail legs route on the BTS North American Rail Network. Rail legs elsewhere are measured by great-circle distance, and supplied rail routes can be imported.

### Emissions

- Well-to-wheel CO2e per leg and in totals. Every factor is a GLEC Framework v3.2 (Smart Freight Centre, October 2025) Module 2 default, the basis ISO 14083 builds on.
- Sea: 71.7 g per TEU-km from Table 18, industry-average dry container, or 7.17 g per tonne-km at the GLEC average of 10 t per TEU. Legs are charged per TEU when a container count is given.
- Road: 125 g per tonne-km, the European starting value for an HGV over 20 t, with GLEC's +5% distance adjustment.
- Rail: 16.1 g per tonne-km on the North American rail network, the US average, and 30.7 g elsewhere from Table 4, European diesel, average or mixed load.
- FCL road and rail legs are charged per container at no less than 10 t per TEU.
- Air: Table 1 factors for the leg's `AirService`, freighter, belly freight or unknown, in bands under and from 1,500 km, including GLEC's +95 km distance adjustment.

### Packaging

- Targets `net8.0` and `net10.0` with no dependencies beyond the base class library and `System.Text.Json`.
- All data is embedded as compressed JSON. A symbols package with Source Link ships alongside.
- Releases are published from GitHub Actions through nuget.org Trusted Publishing.

[2.6.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.6.0
[2.5.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.5.0
[2.4.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.4.0
[2.3.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.3.0
[2.2.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.2.0
[2.1.1]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.1.1
[2.1.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.1.0
[2.0.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.0.0
