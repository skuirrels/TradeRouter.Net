# Changelog

All notable changes to TradeRouter.Net are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0] - 2026-10-15

First release on nuget.org. The library was developed as SeaRoute.Net and renamed TradeRouter.Net before release.

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

- Well-to-wheel CO2e per leg and in totals, from the GLEC Framework v3.2 default intensities.
- Sea legs are charged per TEU when a container count is given. FCL road and rail legs are charged per container at no less than 10 t per TEU.
- North American rail is charged at the GLEC US average of 16.1 g per tonne-km.
- Air legs are charged for their service: freighter, belly freight or unknown.

### Breaking changes from SeaRoute.Net

- The package, namespaces and facade are renamed from SeaRoute.Net and `SeaRouter` to TradeRouter.Net and `TradeRoutes`.
- `GeoJsonFeature.Geometry` is now a nullable `GeoJsonGeometry`.
- Indexed custom graphs are immutable.
- Invalid algorithms, restrictions and physical values now throw instead of being ignored.

### Packaging

- Targets `net8.0` and `net10.0` with no dependencies beyond the base class library and `System.Text.Json`.
- All data is embedded as compressed JSON. A symbols package with Source Link ships alongside.
- Releases are published from GitHub Actions through nuget.org Trusted Publishing.

[2.0.0]: https://github.com/skuirrels/TradeRouter.Net/releases/tag/v2.0.0
