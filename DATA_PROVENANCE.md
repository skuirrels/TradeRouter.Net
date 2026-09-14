# Embedded data provenance

This file records the exact inputs and transformations used for the binary resources in `src/TradeRouter/Data`.
SHA-256 values are hashes of the files as stored, not of their decompressed JSON.

## Maritime network and ports

- Distribution: [genthalili/searoute-py 1.6.0](https://github.com/genthalili/searoute-py/tree/1.6.0), commit `dde461382a60ff75f4577f6e06c411bc48cd7efc`.
- Source `marnet_searoute.geojson`: SHA-256 `111a82d949bb949c396a69a08828781314d3afe779c07e9754c3b2834dfca415`.
- Source `segment.geojson`: SHA-256 `56052b2ddd35de483ec00a29a7f0ffc37e162b0ee0249a9a435c8a8b2d8ffe0f`.
- Source `ports.geojson`: SHA-256 `8398cb9554a19cf37f53f932f3842f58fbd94fce4c8e2704c3f5a4205ea5977e`.
- Embedded `marnet.json.gz`: SHA-256 `8d9be0fb7881b77f46d21bd066779cd4c561f8f70c80f02a5e381706ff7aa880`; 9,708 nodes and 31,950 directed edges.
- Embedded `ports.json.gz`: SHA-256 `ccf97018a8d8c9cccafc08d69388dc0b483e39520f24bde94b36d6a8f8d2dca2`; 3,962 records.

`tools/build-marnet-data.py` combines the tagged Marnet network with its tagged antimeridian segments, converts every line to directed edges in both directions, and calculates weights with the same mean-earth-radius Haversine formula used by the library. `tools/build-ports-data.py` retains every source port record, including duplicate codes. Both scripts verify their input hashes and write deterministic gzip files.

The searoute-py project credits [Eurostat SeaRoute](https://github.com/eurostat/searoute) as the origin of the maritime network. Eurostat describes that network as based on the Oak Ridge National Labs CTA global shipping lane network, enriched around European coasts with AIS-derived lines.

## UN/LOCODE

- Publisher: [United Nations Economic Commission for Europe, UN/LOCODE](https://unlocode.unece.org/).
- Embedded `unlocode.json.gz`: SHA-256 `6e39b6efc925d804af5b304a4db2f701782cc4226fe31e2b97cd3b0bccfb052a`; 106,588 codes, of which 84,516 contain publisher coordinates.
- Embedded `unlocode-supplement.json`: SHA-256 `6ce12ca501948e6f8c5e962fe901673627e2d05a9e1a666118896261a2437716`; four attributed coordinate supplements, applied only when the UN/LOCODE row has no coordinate.
- Embedded `unlocode-seaport-supplement.json`: SHA-256 `3b515fb9e5ee7d178a1409d43c5503c05e55405b01c8fdc0c3eb4f4a3887a56e`; two attributed codes, BRALU and CADCN, whose UN/LOCODE function gains the sea-port flag. Each is a port-list record whose UN/LOCODE row records only a road terminal, confirmed as a deep-sea terminal by the source named on the entry.

Important limitation: the imported UN/LOCODE resource did not retain an edition or source-file checksum, and repository history does not identify one. It must therefore not be described as the current official edition. The [UNECE publications page](https://unlocode.unece.org/publications/) is the authority for the current production and pre-release datasets. A future refresh should replace this resource from a named publication and record its source checksum here.

## Licences and attribution

See `THIRD-PARTY-NOTICES.md`. Data licences are separate from the Apache-2.0 licence covering TradeRouter.Net's own code.

## Sea service allowance calibration

The per-corridor operational allowances in `src/TradeRouter/Movements/SeaServiceAllowance.cs` are not embedded data, but they are derived from observations and are recorded here for the same reason.

- Fitted: 12 September 2026.
- Source: a proprietary dataset of observed sailings, extracted on 28 August 2026 and not redistributed.
- Measure: for each direct sailing on a load-port to discharge-port pair, the last non-estimate departure event to the last non-estimate arrival event, in UTC. Legs over 120 days or with arrival before departure were dropped. Legs departing 2025 and 2026 only. The data records no routing, so it cannot show whether a sailing went through Suez or round the Cape; the Cape comparison below suggests most Asia–Europe sailings in this period went round the Cape.
- Fraction: median observed hours divided by this library's travelling hours at 16 knots for the same pair, minus one, then the median across the lanes in a corridor.

| Corridor | Lanes fitted | Sailings | Fraction |
| --- | --- | ---: | ---: |
| asia-north-europe | CNSHA–NLRTM, CNSHA–DEHAM, CNSHA–FRLEH, VNSGN–NLRTM | 3,885 | 0.63 |
| asia-mediterranean | CNSHA–ITGOA, CNSHA–GRPIR | 656 | 1.33 |
| gulf-europe | AEJEA–NLRTM | 113 | 1.57 |
| transpacific | CNSHA–USLAX, VNSGN–USLAX | 1,194 | 0.12 |
| panama | CNSHA–USNYC | 439 | 0.24 |
| transatlantic | NLRTM–USNYC | 2,745 | 0.80 |

Before calibration every leg used 0.20. On the eleven fitted lanes the mean error against the observed median fell from 11.5 days to 1.3 days, and the worst lane from 26.7 days to 3.6. Corridors without observations keep 0.20. The mart records Shanghai as `CNSHA`; the library routes it as `CNSHG`.

### Cape corridors

- Fitted: 14 September 2026, from the same proprietary dataset, extracted again since the first fit. Nothing from the dataset is embedded or read at runtime; only the resulting fractions are in the code.
- Measure: as above. The re-run reproduced the first fit closely, for example CNSHA–NLRTM 45.5 days from 2,092 sailings against 45.6 days, and 3,969 Asia–North Europe sailings against 3,885.
- Denominator: this library's travelling time at 16 knots for a movement leg with `Northwest`, `Suez` and `Panama` restricted, so the leg sails round the Cape of Good Hope.
- Fraction: median observed hours divided by that Cape travelling time, minus one, then the median across the lanes in a corridor.

| Corridor | Lane | Sailings | Observed days | Cape travel days | Lane fraction | Corridor fraction |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| asia-north-europe-cape | CNSHA–NLRTM | 2,092 | 45.5 | 36.09 | 0.261 | 0.22 |
| | CNSHA–DEHAM | 1,458 | 44.0 | 36.76 | 0.197 | |
| | CNSHA–FRLEH | 347 | 40.9 | 35.58 | 0.150 | |
| | VNSGN–NLRTM | 72 | 40.7 | 32.52 | 0.252 | |
| asia-mediterranean-cape | CNSHA–ITGOA | 547 | 53.6 | 35.53 | 0.508 | 0.37 |
| | CNSHA–GRPIR | 115 | 45.9 | 37.02 | 0.240 | |
| gulf-europe-cape | AEJEA–NLRTM | 114 | 42.2 | 28.86 | 0.462 | 0.46 |

The Asia–North Europe Cape fraction of 0.22 is close to the unfitted default and far below the Suez-route 0.63. Observed times therefore match Cape routing plus a normal service allowance, and the Suez-route fractions absorb the Cape detour. The two Mediterranean lanes disagree, so each is about five days from the corridor median. A leg the router sends through Panama between East Asia and Europe, which happens when Suez is closed and Panama is not, takes the North Europe Cape fraction; its travelling time is within a day of the Cape route. The `panama` corridor now requires one end in the Americas.
