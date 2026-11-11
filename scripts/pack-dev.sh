#!/usr/bin/env bash
# Pack a uniquely-stamped dev wave: one shared stamp for all 42 packages so
# inter-package nuspec dependencies stay consistent and consumers can tell
# which wave they resolved. Stamp = UTC date + seconds-of-day (semver-safe,
# sortable, unique per second).
set -euo pipefail
STAMP="$(date -u +%s)"
STAMP="$((STAMP % 86400))"
VERSION="dev.$(date -u +%Y%m%d).${STAMP}"
OUT="${1:-feed}"
rm -f "$OUT"/*.nupkg "$OUT"/*.snupkg 2>/dev/null || true
mkdir -p "$OUT"
dotnet pack PubSoft.sln -c Release -o "$OUT" -p:PubSoftStream=dev -p:PubSoftDevStamp="$STAMP"
echo "== wave: 0.1.0-${VERSION} -> $OUT"
