#!/usr/bin/env bash
set -euo pipefail
project="${SA_PROJECT:-/home/i/ruk2}"
log="${SA_UNITY_LOG:-$PWD/artifacts/unity-editor-current.log}"
editor="${SA_UNITY_EDITOR:-/home/i/Unity/Hub/Editor/7000.0.0a12/Editor/Unity}"
reachable() { unity command --result-only console get --tail 1 >/dev/null 2>&1; }
cd "$project"
if reachable; then echo "unity editor already reachable"; exit 0; fi
mkdir -p "$(dirname "$log")"
setsid nohup "$editor" -projectpath "$project" -logFile "$log" >/dev/null 2>&1 &
for _ in $(seq 1 120); do
  if reachable; then echo "unity editor reachable"; exit 0; fi
  sleep 5
done
echo "unity editor did not become reachable within 10 minutes; see $log" >&2
exit 1
