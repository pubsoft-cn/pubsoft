#!/usr/bin/env bash
# Push all packed PubSoft nupkgs to the internal feed (BaGet-compatible v2 API).
# Usage:   PUBSOFT_NUGET_URL="http://host:port" NUGET_API_KEY="..." bash scripts/push-internal-feed.sh [pack_dir]
# Pack first: dotnet pack PubSoft.sln -c Release -o feed
set -euo pipefail

NUGET_URL="${PUBSOFT_NUGET_URL:?set PUBSOFT_NUGET_URL (e.g. https://your-internal-feed:port)}"
API_KEY="${NUGET_API_KEY:?set NUGET_API_KEY}"
PACK_DIR="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/feed}"

ok=0; fail=0
for f in "$PACK_DIR"/*.nupkg; do
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
[ "$fail" -eq 0 ]
