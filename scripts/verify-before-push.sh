#!/bin/bash
# Pre-push gate mirroring .github/workflows/ci.yml step order.
# Fail fast on the same things CI fails on — never push red.
set -euo pipefail
cd "$(dirname "$0")/.."

echo "== format check =="
dotnet format PubSoft.sln --verify-no-changes --no-restore

echo "== public API ledger drift =="
bash scripts/sync-public-api.sh --check

echo "== release build =="
dotnet build PubSoft.sln -c Release --no-restore

echo "== tests =="
export PUBSOFT_REDIS="${PUBSOFT_REDIS:-localhost:7000}"
dotnet test PubSoft.sln -c Release --no-restore

echo "ALL GREEN — safe to push"
