import hashlib
import json
import sys
from pathlib import Path

bridge = Path(__file__).resolve().parents[1] / "packages/com.sa.motion.unity"
dll = hashlib.sha256((bridge / "Runtime/Plugins/SA.Motion.dll").read_bytes()).hexdigest()
recorded = json.loads((bridge / "artifact.json").read_text())["sha256"]
golden = json.loads((bridge / "Tests/Resources/SA.Motion/PackageSmoke.json").read_text())["Sha256"]
problems = [f"{name} {value} != dll {dll}" for name, value in (("artifact.json", recorded), ("PackageSmoke.json", golden)) if value != dll]
print("\n".join(problems) if problems else f"bridge artifact consistent: {dll}")
sys.exit(1 if problems else 0)
