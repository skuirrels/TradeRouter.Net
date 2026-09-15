#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
sample_dir="$repo_root/src/TradeRouter.Sample"

if grep -R -n -E 'RoadRouteOverrides|SuppliedRoadRoute|DistanceKm[[:space:]]*=[[:space:]]*[0-9]' "$sample_dir" --include='*.cs'; then
  echo "Sample road distances must use the internal estimator or a configured IRoadRouteProvider such as OSRM." >&2
  echo "Fixed road-route overrides are not permitted." >&2
  exit 1
fi
