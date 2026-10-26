#!/usr/bin/env bash
# PublicAPI.*.txt regen via the analyzer's own codefix (RS0016/RS0017 fixers) —
# the .NET-native generation path; --check fails on drift without writing.
set -euo pipefail
cd "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/.."
if [[ "${1:-}" == "--check" ]]; then
  dotnet format analyzers PubSoft.sln --verify-no-changes --diagnostics RS0016 RS0017 --severity warn
else
  dotnet format analyzers PubSoft.sln --diagnostics RS0016 RS0017 --severity warn
fi
