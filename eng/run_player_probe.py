from pathlib import Path
import hashlib
import json
import os
import subprocess

root = Path(__file__).resolve().parents[1]
artifacts = root / 'artifacts'
player = artifacts / 'player/SA.Motion.Proof.x86_64'
report_path = artifacts / 'player-proof-final.json'
log_path = artifacts / 'player-proof-final.log'
with (artifacts / 'player-proof-launch.log').open('w') as output:
    result = subprocess.run([str(player), '-batchmode', '-sa-motion-report', str(report_path), '-logFile', str(log_path)], cwd=root, env=dict(os.environ, TMPDIR=str(artifacts / 'tmp')), stdout=output, stderr=subprocess.STDOUT, timeout=90)
if result.returncode != 0:
    raise RuntimeError('Player proof exited ' + str(result.returncode))
report = json.loads(report_path.read_text())
manifest = json.loads((root / 'packages/com.sa.motion.unity/artifact.json').read_text())
loaded = artifacts / 'player/SA.Motion.Proof_Data/Managed/SA.Motion.dll'
player_hash = hashlib.sha256(loaded.read_bytes()).hexdigest()
if not report['Passed'] or report['NativeExecution'] != 1 or report['ManagedControl'] != 0:
    raise RuntimeError('Player native/numerical proof failed')
for key in ['ExpectedFixtureSha256', 'VerifiedBuildInputSha256', 'LoadedDllSha256']:
    if report[key] != manifest['sha256'] or report[key] != player_hash:
        raise RuntimeError('Player or current package identity mismatch: ' + key)
if not (artifacts / 'player/SA.Motion.Proof_Data/Plugins/lib_burst_generated.so').is_file():
    raise RuntimeError('Native Burst player payload is absent')
errors = [line for line in log_path.read_text(errors='replace').splitlines() if 'Exception:' in line or line.startswith('ERROR:')]
if errors:
    raise RuntimeError('Player runtime log contains exception/error lines')
print(json.dumps({'passed': True, 'exitCode': result.returncode, 'backend': report['ScriptingBackend'], 'sha256': player_hash, 'report': str(report_path)}))
