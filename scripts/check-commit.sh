#!/bin/bash
# Commit-review gate — run on the pending batch before staging.
# Usage: bash scripts/check-commit.sh ["<type>: <draft message>"]
cd "$(dirname "$0")/.." ; fail=0
ok(){ echo "  ✓ $1"; }; bad(){ echo "  ✗ $1"; fail=1; }

echo "== [1] package governance =="
for proj in src/*/*/*.csproj; do
  dir=$(dirname "$proj"); pkg=$(basename "$proj" .csproj)
  # non-packable projects (build-time analyzers, etc.) are exempt from shipping docs/registration
  grep -q 'IsPackable>false' "$proj" && continue
  [ -f "$dir/PublicAPI.Unshipped.txt" ] || bad "missing ledger: $pkg"
  [ -f "$dir/README.md" ] || bad "missing README.md: $pkg"
  [ -f "$dir/README.zh-CN.md" ] || bad "missing README.zh-CN.md: $pkg"
  grep -q "$pkg" README.md || bad "not registered in root README.md: $pkg"
  grep -q "$(basename "$proj")" PubSoft.sln || bad "not in PubSoft.sln: $pkg"
done; [ $fail -eq 0 ] && ok "all packages: ledger + en/zh README + root README + sln"

echo "== [2] docs bilingual pairs =="
miss=0
for d in docs/*.md; do
  b=$(basename "$d")
  case "$b" in *.zh-CN.md|README.md|test-coverage-*.md) ;;
    *) [ -f "docs/${b%.md}.zh-CN.md" ] || { bad "missing zh-CN pair: $b"; miss=1; } ;;
  esac
done; [ $miss -eq 0 ] && ok "docs/* bilingual pairs complete"

echo "== [3a] root-file bilingual pairs =="
miss=0
# Governance-facing root docs must carry zh-CN pairs; legal/AI-index files are exempt.
for f in CONTRIBUTING.md CODE_OF_CONDUCT.md CHANGELOG.md SECURITY.md README.md; do
  [ -f "$f" ] && [ ! -f "${f%.md}.zh-CN.md" ] && { bad "missing zh-CN pair: $f"; miss=1; }
done
[ $miss -eq 0 ] && ok "root governance docs bilingual pairs complete"

echo "== [3] docs governance (warn-level rollout) =="
warn=0
for f in docs/*.md; do
  b=$(basename "$f")
  case "$b" in README.md|README.zh-CN.md) continue;; esac
  grep -q '^last-reviewed:' "$f" || { echo "  ! no front-matter last-reviewed: $b"; warn=1; }
done
for r in src/*/*/README.md; do
  pkg=$(basename $(dirname "$r"))
  grep -qiE '^#|scope|选项|Options' "$r" || echo "  ! README lacks standard sections: $pkg"
done
[ $warn -eq 0 ] && ok "docs front-matter complete" || echo "  (front-matter rollout — warnings only until backfilled)"

echo "== [4] commit type =="
if [ $# -ge 1 ]; then
  case "$1" in feat:*|fix:*|tests:*|ci:*|tools:*|lift:*|security:*|docs:*|build:*)
    ok "type ok: ${1%%:*}" ;;
    *) bad "bad type '${1%%:*}' — allowed: feat/fix/tests/ci/tools/lift/security/docs/build" ;;
  esac
else echo "  (skip — pass a draft message to validate)"; fi

echo "== [5] trigger: new package artifacts =="
for proj in $(git diff --name-only --diff-filter=A HEAD -- 'src/*/*.csproj' 'src/*/*.csproj' 2>/dev/null; git status --porcelain | awk '/^\?\?/{print $2}' | grep -E 'src/.*/.*\.csproj$'); do
  dir=$(dirname "$proj"); pkg=$(basename "$proj" .csproj)
  grep -q 'IsPackable>false' "$proj" 2>/dev/null && continue
  for req in "$dir/README.md" "$dir/README.zh-CN.md" "$dir/PublicAPI.Unshipped.txt"; do
    [ -f "$req" ] || bad "new package $pkg missing $(basename "$req")"
  done
  grep -q "$pkg" README.md || bad "new package $pkg not registered in root README"
  grep -q "$(basename "$proj")" PubSoft.sln || bad "new package $pkg not in PubSoft.sln"
done

echo "== [6] staged-scope drift (if any staged) =="
if git diff --cached --quiet 2>/dev/null; then echo "  (nothing staged)"; else
  git diff --cached --name-only | grep -q 'PublicAPI.Unshipped' \
    && ok "ledger touched in stage" || echo "  (no ledger file staged — verify new public symbols are synced)"
fi

exit $fail
