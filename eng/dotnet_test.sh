#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
project="${1:-tests/SA.Motion.Tests/SA.Motion.Tests.csproj}"
mkdir -p artifacts/tmp
export TMPDIR="$PWD/artifacts/tmp" NUGET_PACKAGES="$PWD/artifacts/nuget" DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1
lock="$(dirname "$project")/packages.lock.json"
if [ -f "$lock" ]; then locked="--locked-mode"; else locked=""; fi
for framework in net10.0 netstandard2.1; do
  echo "library build under test: $framework"
  selected=()
  [ "$framework" = netstandard2.1 ] && selected=(-p:SaLibraryFramework=netstandard2.1)
  dotnet restore "$project" --force $locked "${selected[@]}" >/dev/null
  dotnet test "$project" --configuration Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false "${selected[@]}" "${@:2}"
done
