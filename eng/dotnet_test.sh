#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
project="${1:-tests/SA.Motion.Tests/SA.Motion.Tests.csproj}"
mkdir -p artifacts/tmp
export TMPDIR="$PWD/artifacts/tmp" NUGET_PACKAGES="$PWD/artifacts/nuget" DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1
lock="$(dirname "$project")/packages.lock.json"
if [ -f "$lock" ]; then locked="--locked-mode"; else locked=""; fi
dotnet restore "$project" --force $locked >/dev/null
dotnet test "$project" --configuration Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false "${@:2}"
