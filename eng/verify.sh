#!/usr/bin/env bash
set -uo pipefail
cd "$(dirname "$0")/.."
unity=1
[ "${1:-}" = "--no-unity" ] && unity=0
mkdir -p artifacts/verify
failures=0
rows=()

step() {
  local name="$1"; shift
  local started=$SECONDS
  if "$@" >"artifacts/verify/$name.log" 2>&1; then status=pass; else status=FAIL; failures=$((failures + 1)); fi
  rows+=("$name|$status|$((SECONDS - started))")
  printf '%-30s %-5s %4ss\n' "$name" "$status" "$((SECONDS - started))"
}

step source-rules python3 eng/check_source_rules.py
for project in tests/*/*.csproj; do
  case "$project" in *PackageSmoke*) continue ;; esac
  step "dotnet-$(basename "$(dirname "$project")")" eng/dotnet_test.sh "$project"
done

if [ "$unity" = 1 ]; then
  step bridge-pack python3 eng/pack_bridge.py
  step humanoid-pack python3 eng/pack_humanoid.py
  step bridge-artifact-consistent python3 eng/check_artifact.py
  step unity-start eng/unity_start.sh
  step unity-compile eng/unity.sh compile
  while read -r assembly; do
    [ -z "$assembly" ] && continue
    step "unity-$assembly" eng/unity.sh test "$assembly"
  done < eng/unity-test-assemblies.txt
fi

python3 - "$failures" "${rows[@]}" <<'PY'
import json, subprocess, sys, time
failures, rows = int(sys.argv[1]), sys.argv[2:]
report = {
    "commit": subprocess.run(["git", "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip(),
    "dirty": bool(subprocess.run(["git", "status", "--porcelain"], capture_output=True, text=True).stdout.strip()),
    "time": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
    "failures": failures,
    "steps": [dict(zip(("name", "status", "seconds"), row.split("|"))) for row in rows],
}
open("artifacts/verify/report.json", "w").write(json.dumps(report, indent=2) + "\n")
PY
echo "failures: $failures (logs and report.json in artifacts/verify)"
exit $((failures > 0))
