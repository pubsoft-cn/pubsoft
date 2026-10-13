#!/usr/bin/env bash
# Publishes and runs the Native AOT smoke consumer against freshly packed feed packages.
# Requires a native toolchain (clang or gcc) — the script skips with a notice when none is found.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FEED="${PUBSOFT_LOCAL_FEED:-/tmp/pubsoft-aot-feed}"
NUGET_CONFIG="$(mktemp --suffix=.config)"
OUT_DIR="$(mktemp -d)"
PKG_CACHE="$(mktemp -d)"
trap 'rm -f "$NUGET_CONFIG"; rm -rf "$OUT_DIR" "$PKG_CACHE"' EXIT

if ! command -v clang >/dev/null 2>&1 && ! command -v gcc >/dev/null 2>&1 && ! command -v cc >/dev/null 2>&1; then
    echo "No native toolchain (clang/gcc/cc) found; skipping Native AOT smoke."
    exit 0
fi

dotnet pack "$ROOT/PubSoft.sln" -c Release -o "$FEED" --nologo >/dev/null

cat > "$NUGET_CONFIG" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$FEED" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local"><package pattern="PubSoft.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
XML

export NUGET_PACKAGES="$PKG_CACHE"
dotnet publish "$ROOT/tests/PubSoft.AotSmoke/PubSoft.AotSmoke.csproj" \
    -c Release -r linux-x64 --configfile "$NUGET_CONFIG" -o "$OUT_DIR" --nologo

"$OUT_DIR/PubSoft.AotSmoke"
