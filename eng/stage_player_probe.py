from pathlib import Path
import argparse
import shutil
import json
import math
import uuid

parser = argparse.ArgumentParser()
parser.add_argument('--project', type=Path, required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
source = root / 'packages/com.sa.motion.unity/Samples~/PlayerProof'
destination = args.project.resolve() / 'Assets/SA.Motion.Proof'
marker = destination / '.sa-motion-proof-owned'
if destination.exists() and not marker.exists():
    raise RuntimeError('Refusing to overwrite an unowned proof directory')
destination.mkdir(parents=True, exist_ok=True)
marker.write_text('Generated staging from canonical sa-motion player proof.\n')
for path in source.rglob('*'):
    relative = path.relative_to(source)
    target = destination / relative
    if path.is_dir():
        target.mkdir(parents=True, exist_ok=True)
    else:
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, target)
fixture = destination / 'Resources/SA.Motion.Player/PackageSmoke.json'
fixture.parent.mkdir(parents=True, exist_ok=True)
shutil.copyfile(root / 'packages/com.sa.motion.unity/Tests/Resources/SA.Motion/PackageSmoke.json', fixture)
inputs = {
    'CurrentPosition': [1, 2, 3], 'CurrentRotation': [0, math.sin(0.5), 0, math.cos(0.5)], 'CurrentScale': [2, 3, 4],
    'HistoryInterval': 0.02, 'Duration': 1, 'Elapsed': 0.5,
    'ScalarMagnitude': 1, 'ScalarVelocity': 0, 'ScalarDuration': 1
}
fixture.with_name('Input.json').write_text(json.dumps(inputs, indent=2)+'\n')
for path in destination.rglob('*'):
    if path.name.startswith('.') or path.suffix == '.meta':
        continue
    meta = path.with_name(path.name + '.meta')
    if not meta.exists():
        guid = uuid.uuid5(uuid.NAMESPACE_URL, 'sa-motion-player/' + path.relative_to(destination).as_posix()).hex
        text = 'fileFormatVersion: 2\nguid: ' + guid + '\n'
        if path.is_dir(): text += 'folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n'
        meta.write_text(text)
print(destination)
