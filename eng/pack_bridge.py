from pathlib import Path
import hashlib
import json
import os
import shutil
import subprocess
import zipfile

root = Path(__file__).resolve().parents[1]
artifacts = root / 'artifacts'
artifacts.mkdir(exist_ok=True)
(artifacts / 'tmp').mkdir(exist_ok=True)
env = dict(os.environ, TMPDIR=str(artifacts / 'tmp'), DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER='1')

def run(args, capture=False):
    return subprocess.run(args, cwd=root, env=env, check=True, text=True, stdout=subprocess.PIPE if capture else None)

run(['dotnet', 'pack', 'src/SA.Motion/SA.Motion.csproj', '--configuration', 'Release', '--no-restore', '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false', '--output', str(artifacts / 'packages')])
package = artifacts / 'packages' / 'SA.Motion.0.1.0.nupkg'
with zipfile.ZipFile(package) as archive:
    payload = archive.read('lib/netstandard2.1/SA.Motion.dll')
sha = hashlib.sha256(payload).hexdigest()
cache = artifacts / 'smoke-packages' / sha
run(['dotnet', 'restore', 'tests/PackageSmoke/PackageSmoke.csproj', '--source', str(artifacts / 'packages'), '--packages', str(cache), '--no-cache'])
run(['dotnet', 'build', 'tests/PackageSmoke/PackageSmoke.csproj', '--configuration', 'Release', '--no-restore', '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false'])
report = json.loads(run(['dotnet', 'tests/PackageSmoke/bin/Release/net10.0/PackageSmoke.dll'], capture=True).stdout)
if report['Sha256'] != sha:
    raise RuntimeError('Local NuGet consumer loaded different DLL bytes')
if any(name.startswith('Unity') for name in report['References']):
    raise RuntimeError('Portable DLL references Unity')
bridge = root / 'packages' / 'com.sa.motion.unity'
plugin = bridge / 'Runtime' / 'Plugins'
plugin.mkdir(parents=True, exist_ok=True)
(plugin / 'SA.Motion.dll').write_bytes(payload)
shutil.copyfile(root / 'LICENSE', plugin / 'SA.Motion.LICENSE.txt')
fixtures = bridge / 'Tests' / 'Resources' / 'SA.Motion'
fixtures.mkdir(parents=True, exist_ok=True)
(fixtures / 'PackageSmoke.json').write_text(json.dumps(report, indent=2) + '\n')
(artifacts / 'package-smoke.json').write_text(json.dumps(report, indent=2) + '\n')
(bridge / 'artifact.json').write_text(json.dumps({'package': package.name, 'framework': 'netstandard2.1', 'sha256': sha, 'version': report['Version'], 'assemblyReferences': report['References']}, indent=2) + '\n')
print(json.dumps({'sha256': sha, 'nugetConsumer': 'passed', 'plugin': str(plugin / 'SA.Motion.dll')}))
