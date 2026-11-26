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
dotnet format analyzers PubSoft.sln --verify-no-changes --diagnostics RS0016 RS0017 --severity warn

echo "== [4] release build =="
dotnet build PubSoft.sln -c Release --no-restore

echo "== [5] tests =="
# Mirror the CI env so integration tests never silently skip locally — a skipped
# MySQL/AMQP test here runs for real in CI, so local green would lie about push safety.
export PUBSOFT_REDIS="${PUBSOFT_REDIS:-localhost:7000}"
export PUBSOFT_MYSQL="${PUBSOFT_MYSQL:-Server=localhost;Port=3307;Database=pubsoft_test;User=root;Password=pubsoft-it}"
export PUBSOFT_AMQP="${PUBSOFT_AMQP:-amqp://guest:guest@localhost:5673}"
dotnet test PubSoft.sln -c Release --no-restore

echo "== [6] doc drift (warn) =="
dotnet run --project tools/PubSoft.DocsTool -c Release -- --check || echo "  (drift warnings — run: dotnet run --project tools/PubSoft.DocsTool -c Release -- --sync)"

echo "ALL GREEN — safe to push"
