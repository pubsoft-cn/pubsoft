#!/bin/bash
# Release-tier gate — the heavy mirror of ci.yml, run before version bumps and releases.
# verify-before-push.sh stays the fast per-push tier; this tier adds the CI-only hard
# gates (coverage, package-consumer smoke, native AOT consumer) so nothing is CI-first.
set -euo pipefail
cd "$(dirname "$0")/.."

echo "== [1/6] fast gate (format + ledger + build) =="
echo "== format =="
dotnet format PubSoft.sln --verify-no-changes --no-restore
echo "== public API ledger drift =="
dotnet format analyzers PubSoft.sln --verify-no-changes --diagnostics RS0016 RS0017 --severity warn
echo "== release build =="
dotnet build PubSoft.sln -c Release --no-restore

echo "== [2/6] tests with coverage =="
export PUBSOFT_REDIS="${PUBSOFT_REDIS:-localhost:7000}"
export PUBSOFT_MYSQL="${PUBSOFT_MYSQL:-Server=localhost;Port=3307;Database=pubsoft_test;User=root;Password=pubsoft-it}"
export PUBSOFT_AMQP="${PUBSOFT_AMQP:-amqp://guest:guest@localhost:5673}"
rm -rf coverage-results
dotnet test PubSoft.sln --no-restore --configuration Release \
  --collect:"XPlat Code Coverage" --settings coverage.runsettings --results-directory coverage-results

echo "== [3/6] coverage gate =="
bash scripts/check-coverage.sh

echo "== [4/6] pack =="
dotnet pack PubSoft.sln --no-restore --configuration Release

echo "== [5/6] package consumer smoke =="
bash scripts/verify-package-consumer.sh

echo "== [6/6] native AOT consumer =="
bash scripts/verify-aot-consumer.sh

echo "RELEASE GATE GREEN — safe to bump and release"
