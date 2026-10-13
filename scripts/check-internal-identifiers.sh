#!/usr/bin/env bash
# Fail if tracked files contain internal organization identifiers.
# These tokens must never appear in the public repository — internal
# endpoints, internal names, internal DNS.
#
# Every token is assembled from fragments so this script never literally
# contains a forbidden pattern and can scan itself without exclusions.
# Override via PUBSOFT_FORBIDDEN_PATTERNS (regex, |-separated).
set -euo pipefail

PARK="park"; LOT="lot"
IP3="192"; IP4="168"
HW="hw"; D="d"
BE="be"; ND="nd"
PATTERNS="${PUBSOFT_FORBIDDEN_PATTERNS:-${PARK}${LOT}|${IP3}\.${IP4}\.|\b${HW}${D}\b|\b${BE}${ND}\b}"

mapfile -d '' files < <(git ls-files -z)
hits=$(grep -lE "$PATTERNS" "${files[@]}" 2>/dev/null || true)
if [ -n "$hits" ]; then
  echo "Forbidden internal identifiers found in tracked files:"
  echo "$hits" | while IFS= read -r f; do
    echo "  $f"
    grep -nE "$PATTERNS" -- "$f" | head -3 | sed 's/^/    /'
  done
  exit 1
fi
echo "OK: no internal identifiers in tracked files"
