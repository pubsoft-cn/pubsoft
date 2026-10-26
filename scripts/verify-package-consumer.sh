#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
consumer_project="$repo_root/tests/PubSoft.PackageConsumerSmoke/PubSoft.PackageConsumerSmoke.csproj"
# Shipped set == smoke set is enforced by AssertConsumerParity in
# tests/Directory.Build.targets, so the loop can glob the smoke projects directly.
mapfile -t consumer_projects < <(find "$repo_root/tests" -mindepth 2 -maxdepth 2 \
  -name "*ConsumerSmoke.csproj" ! -name "PubSoft.PackageConsumerSmoke.csproj" | sort)
feed="$(mktemp -d)"
packages="$(mktemp -d)"
nuget_config="$(mktemp)"
trap 'rm -rf "$feed" "$packages" "$nuget_config"' EXIT

dotnet restore "$repo_root/PubSoft.sln"
dotnet pack "$repo_root/PubSoft.sln" -c Release -o "$feed" --no-restore
cat > "$nuget_config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local">
      <package pattern="PubSoft.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
EOF

NUGET_PACKAGES="$packages" dotnet restore "$consumer_project" --configfile "$nuget_config"
NUGET_PACKAGES="$packages" dotnet run --project "$consumer_project" -c Release --no-restore
for project in "${consumer_projects[@]}"; do
  NUGET_PACKAGES="$packages" dotnet restore "$project" --configfile "$nuget_config"
  NUGET_PACKAGES="$packages" dotnet run --project "$project" -c Release --no-restore
done