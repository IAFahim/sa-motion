#!/usr/bin/env bash
set -euo pipefail
project="${SA_PROJECT:-/home/i/ruk2}"
exec 9>"${SA_UNITY_LOCK:-/tmp/sa-unity.lock}"
flock 9
cd "$project"

if ! unity command --result-only console get --tail 1 >/dev/null 2>&1; then
  echo "Unity Editor is not running or its Pipeline server is unreachable. Run eng/unity_start.sh first." >&2
  exit 3
fi

recompile() {
  unity command --result-only script recompile >/dev/null 2>&1 || true
  sleep 4
  local status
  for _ in $(seq 1 100); do
    status=$(unity command --result-only script recompile_status 2>&1) || true
    grep -q '"status": "completed"' <<<"$status" && break
    sleep 3
  done
  python3 -c 'import json,sys; d=json.loads(sys.stdin.read()); print("compile failed:", d.get("failed")); [print(e) for e in d.get("errors", [])[:25]]; sys.exit(1 if d.get("failed") else 0)' <<<"$status"
}

test_assembly() {
  unity command --result-only console clear >/dev/null 2>&1
  unity command --result-only --timeout 540 test run --mode EditMode --filter "$1" --filter_type assembly "${@:2}" | python3 -c '
import json,sys
d=json.load(sys.stdin)
print(d["Summary"])
failed=[r for r in d["Results"] if r["Status"]!="Passed"]
for r in failed: print("FAIL", r["FullName"], "::", (r["Message"] or "")[:2500].replace("\n"," "))
sys.exit(1 if failed or d["Summary"]["Total"]==0 else 0)'
}

eval_file() {
  unity command --result-only --timeout 300 script eval_file --file "$1"
}

case "${1:-}" in
  compile) recompile ;;
  test) test_assembly "${2:?assembly name, e.g. SA.Tests}" "${@:3}" ;;
  eval) eval_file "${2:?path to a .cs file whose last statement returns a string}" ;;
  *) echo "usage: eng/unity.sh compile | test <assembly> [--include_explicit true] | eval <file.cs>" >&2; exit 2 ;;
esac
