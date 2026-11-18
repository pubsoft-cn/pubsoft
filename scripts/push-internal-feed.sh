#!/usr/bin/env bash
# Push all packed PubSoft nupkgs to the internal feed (BaGet-compatible v2 API).
# Usage:   PUBSOFT_NUGET_URL="http://host:port" NUGET_API_KEY="..." bash scripts/push-internal-feed.sh [pack_dir]
# Pack first: dotnet pack PubSoft.sln -c Release -o feed   (or scripts/pack-dev.sh)
#
# Same-version overwrite guard: a wave version that was already pushed from this
# directory is refused. Repacking under an identical version and pushing again
# leaves consumers holding whichever binary their cache resolved first -
# untraceable. Repack with a fresh stamp instead (pack-dev.sh [dir] [label]).
set -euo pipefail

NUGET_URL="${PUBSOFT_NUGET_URL:?set PUBSOFT_NUGET_URL (e.g. https://your-internal-feed:port)}"
API_KEY="${NUGET_API_KEY:?set NUGET_API_KEY}"
PACK_DIR="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/feed}"
LEDGER="$PACK_DIR/.pushed-waves"

wave="$(ls "$PACK_DIR"/PubSoft.*.nupkg 2>/dev/null | head -1 | xargs -r basename | \
  grep -oE '[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$' || true)"
if [ -n "$wave" ] && grep -qxF "$wave" "$LEDGER" 2>/dev/null; then
  echo "REFUSED: wave ${wave} was already pushed from ${PACK_DIR}." >&2
  echo "Same-version repush leaves consumers with untraceable binaries; repack with a fresh stamp." >&2
  exit 1
fi

ok=0; fail=0
for f in "$PACK_DIR"/PubSoft.*.nupkg; do
  code=$(curl -s -o /dev/null -w "%{http_code}" -X PUT \
    -H "X-NuGet-ApiKey: ${API_KEY}" \
    -H "Content-Type: application/octet-stream" \
    --data-binary "@${f}" "${NUGET_URL}/api/v2/package/")
  if [ "$code" = "200" ] || [ "$code" = "201" ]; then
    ok=$((ok+1))
  else
    echo "FAIL($code): $(basename "$f")"; fail=$((fail+1))
  fi
done
echo "=== pushed: ${ok} ok, ${fail} fail ==="
if [ "$fail" -eq 0 ] && [ -n "$wave" ]; then
  echo "$wave" >> "$LEDGER"
fi
[ "$fail" -eq 0 ]
