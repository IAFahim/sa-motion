from pathlib import Path
import argparse
import shutil
import uuid

parser = argparse.ArgumentParser()
parser.add_argument('--project', type=Path, required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
source = root / 'packages/com.sa.motion.unity/Samples~/CostProbe'
destination = args.project.resolve() / 'Assets/SA.Motion.CostProof'
marker = destination / '.sa-motion-cost-proof-owned'
if destination.exists() and not marker.exists():
    raise RuntimeError('Refusing to overwrite an unowned cost-probe directory')
destination.mkdir(parents=True, exist_ok=True)
marker.write_text('Generated from canonical SA.Motion cost probe.\n')
for path in source.rglob('*'):
    target = destination / path.relative_to(source)
    if path.is_dir(): target.mkdir(parents=True, exist_ok=True)
    else:
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, target)
        meta = target.with_name(target.name + '.meta')
        if not meta.exists():
            meta.write_text('fileFormatVersion: 2\nguid: ' + uuid.uuid5(uuid.NAMESPACE_URL, 'sa-motion-cost/' + target.relative_to(destination).as_posix()).hex + '\n')
print(destination)
