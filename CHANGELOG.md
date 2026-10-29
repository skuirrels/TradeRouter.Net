# Changelog

All notable changes to TradeRouter.Net are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

[2.2.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.2.0
[2.1.1]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.1.1
[2.1.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.1.0
[2.0.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.0.0
