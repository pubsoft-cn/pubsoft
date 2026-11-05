#!/bin/bash
# Pre-push gate mirroring .github/workflows/ci.yml step order + content governance.
# Fail fast on the same things CI fails on — never push red.
set -euo pipefail
cd "$(dirname "$0")/.."

echo "== [0] commit-content gate =="
bash scripts/check-commit.sh

echo "== [1] internal-identifier scan (if configured) =="
if [ -n "${PUBSOFT_FORBIDDEN_PATTERNS:-}" ] && [ -f scripts/check-internal-identifiers.sh ]; then
  bash scripts/check-internal-identifiers.sh
else
  echo "  (skipped — PUBSOFT_FORBIDDEN_PATTERNS not set locally; CI runs it)"
fi

echo "== [2] format check =="
dotnet format PubSoft.sln --verify-no-changes --no-restore

echo "== [3] public API ledger drift =="
bash scripts/sync-public-api.sh --check

echo "== [4] release build =="
dotnet build PubSoft.sln -c Release --no-restore

echo "== [5] tests =="
export PUBSOFT_REDIS="${PUBSOFT_REDIS:-localhost:7000}"
dotnet test PubSoft.sln -c Release --no-restore

echo "== [6] doc drift (warn) =="
bash scripts/check-doc-drift.sh || echo "  (drift warnings — see internal/doc-drift/report.txt)"

echo "ALL GREEN — safe to push"
