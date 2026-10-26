#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
consumer_project="$repo_root/tests/PubSoft.PackageConsumerSmoke/PubSoft.PackageConsumerSmoke.csproj"
allowlist_path="$repo_root/scripts/package-dependency-allowlist.json"
mapfile -t package_ids < <(python3 -c 'import json, sys; print("\n".join(sorted(json.load(open(sys.argv[1], encoding="utf-8")))))' "$allowlist_path")
consumer_projects=()
for package_id in "${package_ids[@]}"; do
  project="$repo_root/tests/${package_id}ConsumerSmoke/${package_id}ConsumerSmoke.csproj"
  consumer_projects+=("$project")
done
feed="$(mktemp -d)"
packages="$(mktemp -d)"
nuget_config="$(mktemp)"
trap 'rm -rf "$feed" "$packages" "$nuget_config"' EXIT

dotnet restore "$repo_root/PubSoft.sln"
dotnet pack "$repo_root/PubSoft.sln" -c Release -o "$feed" --no-restore
python3 "$repo_root/scripts/sync-package-deps.py" --check
python3 "$repo_root/scripts/verify-package-dependencies.py" "$feed"
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