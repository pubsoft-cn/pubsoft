#!/usr/bin/env bash
# Push PubSoft.* nupkgs to nuget.org. Never touches internal Hwd.* packages —
# the PubSoft.* glob is a hard guard, and --skip-duplicate makes re-runs safe.
# Usage: NUGET_ORG_API_KEY="..." bash scripts/push-nuget-org.sh [pack_dir]
set -euo pipefail

# Dedicated var — NUGET_API_KEY is the internal-feed key; never send it to nuget.org.
: "${NUGET_ORG_API_KEY:?set NUGET_ORG_API_KEY (nuget.org key scoped to PubSoft.*)}"
PACK_DIR="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/feed}"

shopt -s nullglob
pkgs=("$PACK_DIR"/PubSoft.*.nupkg)
if ((${#pkgs[@]} == 0)); then echo "no PubSoft.*.nupkg in $PACK_DIR — run dotnet pack -o $PACK_DIR first"; exit 1; fi

dotnet nuget push "${pkgs[@]}" \
  --api-key "$NUGET_ORG_API_KEY" \
  --source https://api.nuget.org/v3/index.json \
  --skip-duplicate
