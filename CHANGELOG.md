# Changelog

All notable changes to TradeRouter.Net are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- A sea leg at a code that UN/LOCODE records as a named airport, such as Shanghai (`CNSHA`), Ningbo (`CNNGB`), Xiamen (`CNXMN`), Qingdao (`CNTAO`), Dalian (`CNDLC`) or Tianjin (`CNTSN`), now starts or ends at the port-list port under that code instead of being rejected as an airport. Since 2.13.0 these codes resolved to the airport everywhere. A movement that uses such a code only at the end of sea legs or as a `Waypoint.Port`, and never on an air leg or as a `Waypoint.Airport`, now means the port, so a road leg that joins the sea leg there ends at the port too. The port-list record is used only when it lies within 50 km of the maritime network; the port list also files inland cities under airport codes, such as Zhengzhou (`CNCGO`) and Tucson (`USTUS`), and a sea leg there is still rejected. A movement that also uses the code on an air leg keeps the airport, because every leg touching a code shares one position, and `Locate` still returns the airport.
- Antwerp Churchill Terminal (`BEANT`), a container terminal on the Churchilldok in the port of Antwerp that UNECE records only as multimodal, can end a sea leg. The sea-port supplement gains the entry.
- Helsinki (`FIHEL`) and Kaohsiung (`TWKHH`) can end an air leg: UNECE 2025-1 lists both as airports, while the embedded rows record the sea port only. Milan (`ITMIL`) can too, because MIL is the IATA code for all Milan airports. Each keeps its position.
- Nine UN/LOCODEs that UNECE publishes without a coordinate now resolve, from reviewed rows in the coordinate supplement: Kansas City, Kansas (`USKCK`), Arjuna offshore terminal (`IDAJN`), Kocaeli Cengiz Topel Airport (`TRKCO`), Qingshuihe, Shenzhen (`CNQSE`), Foshan New Port (`CNFRT`), Luton (`GBLUT`), Cavite City (`PHCVE`), Purfleet (`GBPFT`) and Toronto (`CAYTO`). 113,541 of 116,022 codes now have coordinates. Jining (`CNJII`), Jinjiang (`CNJJI`) and Samulsakom (`THSAS`) stay unplaced, because their UNECE subdivision contradicts every candidate.

## [2.13.0] - 2026-11-17

### Fixed

- 33 UN/LOCODEs that UNECE records as named airports, such as Ningbo Lishe (`CNNGB`), Shanghai Hongqiao (`CNSHA`), Qingdao Jiaodong (`CNTAO`), Tianjin Binhai (`CNTSN`) and John F. Kennedy (`USJFK`), now resolve to the airport rather than to the port list's record under the same code, 2.5 to 45 km away, which is the city or its port filed under the airport's IATA code. Each now lies within 2 km of its airport. The resolver lets UN/LOCODE win when it records the code with the airport function, without the sea-port function, under a name containing "Apt" or "Airport", and has a position for it. A sea leg is still rejected at these codes, because UN/LOCODE records no sea-port function for them, and a caller who declares the sea-port function in `MovementRequest.AdditionalLocationFunctions` still gets the port-list record. Port-to-port `TradeRoutes.Calculate` still reads the port list alone and is unchanged.
- Ningbo (`CNNBO`) now carries the rail-terminal and airport functions that UNECE 2025-1 lists, besides the sea port, so it can end a rail or air leg. The rail supplement gains the entry and a new airport supplement, `unlocode-airport-supplement.json`, holds the airport function; the embedded UNECE row is unchanged.
- `TradeRoutes.Calculate` accepts Ningbo's own code: `CNNBO` and 21 other official Chinese and Thai city or port codes, such as Tianjin Pt (`CNTNG`), Xiamen (`CNXAM`), Dalian Pt (`CNDAG`) and Krabi (`THKRV`), are aliased to the port-list record that the port list files under the city's airport code, within 10 km and under the same name. They threw "not found" before. `tools/find-port-code-aliases.py` now lists these candidates too.

## [2.12.0] - 2026-11-17

### Added

- `MovementResult.ToText()` prints a `Leg <n> warning = <distance_warning>` line after the summary line, before the footnotes, for each leg whose distance fell back: a road leg estimated because its road-route provider returned `Unavailable` or `OutsideCoverage`, and a rail leg the rail network could not route. A provider outage, such as a key that Google Routes rejects with HTTP 403, no longer looks like an ordinary estimate. The label is padded to the width of the other footnote names, line breaks in the warning become spaces and trailing whitespace is dropped, and the lines appear with or without explanations. Reports with no fallback are unchanged, including those with ordinary road estimates, whose caveat stays in the distance basis footnote. The GeoJSON shape is unchanged.

### Fixed

- A road leg's `distance_warning` after a provider fallback no longer doubles the full stop when the provider's message already ends with one, as most built-in Google Routes and OSRM messages do: `osrm returned Unavailable: The OSRM request timed out. Estimated ...` rather than `timed out.. Estimated`. The message is trimmed, a full stop is added only when it does not already end with `.`, `!` or `?`, and a blank message reads `no reason supplied.`
- Charleston, Illinois (`USILC`) now resolves to Charleston, the Coles County seat, rather than St. Charles, Illinois, 269 km north. The generated GeoNames row is replaced by a reviewed one, because the St. Charles feature in GeoNames also carries the code.

## [2.11.0] - 2026-11-16

### Added

- 9,434 UN/LOCODEs that the embedded list lacked, from the UNECE 2025-1 release, 8,177 of them with a coordinate, so 112,023 of 116,022 codes now resolve without a caller coordinate. Examples are Allach (`DEAL9`), Chonburi (`THCHO`), Tay Ninh (`VNTNT`) and Malmesbury (`ZAZCO`). The merge is add-only: existing codes keep their name, coordinate and functions, and codes the port list uses are left alone, so every code that resolved before resolves as it did. 55 added coordinates that fall far outside their country, such as Bystrocice (`CZBYT`) at 171°E, are held back for review.
- `tools/merge-unlocode-release.py` repeats the merge from a pinned UNECE release zip.
- Coordinates for 1,509 more UN/LOCODEs that UNECE publishes without one, so 113,532 of 116,022 codes now resolve. 1,485 come from `tools/fill-added-unlocode-coordinates.py`: GeoNames features linked to the code, Wikidata items confirmed by GeoNames, or the only GeoNames place of the code's name in its country, each confirmed in its UNECE subdivision where it has one. 24 more were researched by hand, such as Yichang port (`CNYCG`) and the Taba Border Crossing (`ILTAB`). Existing supplement rows are unchanged, and codes the port list uses, and the original sea-port and airport codes reviewed earlier, are left alone. Of the 55 coordinates the 2025-1 merge held back, 45 are now placed from GeoNames.

## [2.10.0] - 2026-11-13

### Added

- `EmissionFactors.GlecV32Defaults` carries the v3.2 values, for reporting that must stay comparable with a baseline calculated before v3.3. Pass it as `MovementRequest.Emissions`.

### Changed

- `EmissionFactors.GlecDefaults` now carries the [GLEC Framework v3.3](https://smartfreightcentre.org/news/13311209) (Smart Freight Centre, October 2026) Module 2 defaults, so default CO2e figures for sea and air legs change.
- Sea: 73.4 g per TEU-km from Table 18, industry-average dry container, end-user intensity, up from 71.7, or 7.34 g per tonne-km at 10 t per TEU, up from 7.17.
- Air, Table 1, including the +95 km distance adjustment: freighter 1,512 g per tonne-km under 1,500 km and 638 from 1,500 km (were 1,516 and 608), belly freight 1,332 and 930 (were 1,239 and 936), service unknown 1,413 and 799 (were 1,363 and 788).
- Road, 125 g, and rail, 30.7 g in Europe and 16.1 g in North America, are unchanged in v3.3.

### Documentation and sample

- The README shows a cached OSRM provider in an ASP.NET Core app, with Redis, SQL Server, PostgreSQL and in-memory stores, its settings coming from configuration.
- Sample example 18 sends two movements that share the Gatwick to Heathrow leg through `CachingRoadRouteProvider` in front of OSRM, and reports how many OSRM calls the cache saved. It needs `TRADEROUTER_OSRM_URL`, `TRADEROUTER_ROAD_CACHE_PARTITION` and `TRADEROUTER_ROAD_CACHE_DAYS`, which `run-with-osrm.sh` sets.

## [2.9.0] - 2026-11-03

### Road-route caching

- New companion package `TradeRouter.Net.Caching`. `CachingRoadRouteProvider` wraps any `IRoadRouteProvider` and serves repeated road legs from a store, keyed on a caller-chosen partition and the endpoints rounded to `CoordinateDecimals`. Successful routes are kept for `TimeToLive` from the time they were fetched, `NoRoute` and `OutsideCoverage` results only when `NegativeTimeToLive` is set, and `Unavailable` results never. A store failure is reported to `OnCacheError` and the leg is routed without the cache.
- `DistributedRoadRouteCache` stores results in any `IDistributedCache`, such as Redis, SQL Server or PostgreSQL, with an absolute expiry. Implement `IRoadRouteCache` to use another store.
- The caching provider caches whatever the wrapped provider returns. Google Maps Platform terms do not allow Routes API results to be cached, so do not wrap `GoogleRoutesRoadRouteProvider` in it.
- Both packages take their version from `src/Directory.Build.props` and are released together under one tag. `TradeRouter.Net` itself is unchanged.

## [2.8.0] - 2026-10-30

### Location data

- 1,143 airports have coordinates, so movements and `Locate` can name them by code. UNECE publishes none for them, and air legs to or from them previously threw. They include Narita (`JPNRT`), Haneda (`JPHND`), Kansai (`JPKIX`), Dallas-Fort Worth (`USDFW`), Dulles (`USIAD`), Detroit (`USDTW`), Beijing Capital (`CNPEK`), Malpensa (`ITMXP`), Linate (`ITLIN`), Arlanda (`SEARN`), Stansted (`GBSTN`), Montreal (`CAYUL`), Vancouver (`CAYVR`), Jakarta (`IDCGK`) and Addis Ababa (`ETADD`). `tools/fill-airport-coordinates.py` places each at the OurAirports airport that is the only open one in its country carrying its IATA code, when the names share a distinctive word. 271 airport codes remain without a position.
- 20 of the 27 codes that 2.6.1 rejected as port-list namesakes now resolve to their own UN/LOCODE place, because they are airports: Latrobe (`USLBE`) is at Arnold Palmer Regional Airport in Pennsylvania, Madison (`USMDN`) in Indiana, and Shantou (`CNSWA`) and Wuxi (`CNWUX`) at their airports. Seven still throw.

## [2.7.0] - 2026-10-30

### Sea routing

- The Kiel Canal is a passage: `Passage.Kiel`, `kiel`, as Eurostat SeaRoute names it. searoute-py has no lane through the canal, so routes between the Elbe or the North Sea and the Baltic went round Skagen. `tools/build-marnet-data.py` now adds Eurostat SeaRoute's canal line from Brunsbüttel to Kiel-Holtenau, 96 km, with Eurostat's approach lanes up and down the Elbe and out to Kiel Bay. It is open by default, so those routes now use it and report it in `traversed_passages`: Hamburg to Fredericia is 398 km instead of 956 km, Hamburg to Szczecin 522 km instead of 1,178 km, Rotterdam to Gdansk 1,250 km instead of 1,640 km and Bremerhaven to Stockholm 1,155 km instead of 1,656 km. Listing it in `Restrictions` restores the route round Skagen with its earlier length. Routes that do not use it are unchanged.
- Eurostat's lanes to Husum, Tönning and Eckernförde are added too. These ports had no lane, and without one they would snap to the canal. Twelve ports beside the canal now snap to an added lane point nearer them: Brunsbüttel, Holtenau, Kiel, Laboe, Rendsburg, Eckernförde, Schleswig, Kappeln, Husum, Tönning, Büsum and Cuxhaven. Their routes now include the lane to the port, so a route that does not use the canal can be longer: Husum to Rotterdam is 572 km instead of 479 km, and Kiel to Gothenburg 421 km instead of 406 km. With it closed, places on the canal have no sea route: Rendsburg, and in movements UN/LOCODE sea ports such as Audorf (`DEAUO`), Budelsdorf (`DEUSF`) and Obereiderhafen (`DEOEX`).
- The lane network has 9,748 nodes and 32,034 directed edges, up from 9,708 and 31,950. The earlier nodes and edges are unchanged.

## [2.6.1] - 2026-10-30

### Location resolution

- A code whose port-list records are duplicated no longer throws in movements, `Locate` or port-to-port `Calculate`. Resolution takes the record nearest the code's UN/LOCODE position, or, without one, the terminal record when the records lie within 50 km of each other. Twelve codes had no UN/LOCODE position and threw `InvalidOperationException` for every mode, including Nhava Sheva (`INNSA`), Davao (`PHDVO`) and Palembang (`IDPLM`). Port-to-port `Calculate` also threw for the 26 duplicated codes that do have a position, such as Vancouver (`CAVAN`).
- A code that UN/LOCODE lists without a position no longer falls back to a port-list record for a different place. With no position to compare, the names must agree, allowing for spelling variants such as Haerbin and Harbin. Latrobe (`USLBE`) had resolved to Laguna Beach, California, and Madison (`USMDN`) to Mandan, North Dakota. 27 codes now throw an `ArgumentException` naming both places, 21 of them in the United States. Shantou (`CNSWA`) and Wuxi (`CNWUX`) are among them, because UN/LOCODE files those codes as airports; the ports are `CNSTG` and `CNWXS`.

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

[2.13.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.13.0
[2.12.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.12.0
[2.11.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.11.0
[2.10.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.10.0
[2.9.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.9.0
[2.8.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.8.0
[2.7.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.7.0
[2.6.1]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.6.1
[2.6.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.6.0
[2.5.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.5.0
[2.4.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.4.0
[2.3.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.3.0
[2.2.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.2.0
[2.1.1]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.1.1
[2.1.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.1.0
[2.0.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.0.0
