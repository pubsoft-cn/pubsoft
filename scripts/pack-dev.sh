#!/usr/bin/env bash
# Pack a uniquely-stamped dev wave: one shared stamp for all packages so
# inter-package nuspec dependencies stay consistent and consumers can tell
# which wave they resolved. Stamp = UTC date + seconds-of-day (semver-safe,
# sortable, unique per second).
#
# Usage: pack-dev.sh [output_dir] [label]
#   label - optional local-only suffix (e.g. "local1") appended to the stamp.
#           NEVER repack a wave under a stamp that was already pushed:
#           same-version different-bits is untraceable. The push script
#           records pushed waves and refuses to accept a stamp twice.
set -euo pipefail
OUT="${1:-feed}"
LABEL="${2:-}"
if [ -n "$LABEL" ] && ! [[ "$LABEL" =~ ^[A-Za-z0-9]+$ ]]; then
  echo "label must be alphanumeric (semver identifier)" >&2; exit 1
fi
STAMP="$(date -u +%s)"
STAMP="$((STAMP % 86400))"
[ -n "$LABEL" ] && STAMP="${STAMP}.${LABEL}"
VERSION="dev.$(date -u +%Y%m%d).${STAMP}"
rm -f "$OUT"/*.nupkg "$OUT"/*.snupkg 2>/dev/null || true
mkdir -p "$OUT"
dotnet pack PubSoft.sln -c Release -o "$OUT" -p:PubSoftStream=dev -p:PubSoftDevStamp="$STAMP"
echo "== wave: 0.1.0-${VERSION} -> $OUT"
