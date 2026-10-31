import hashlib
import json
import sys
from pathlib import Path

bridge = Path(__file__).resolve().parents[1] / "packages/com.sa.motion.unity"
dll = hashlib.sha256((bridge / "Runtime/Plugins/SA.Motion.dll").read_bytes()).hexdigest()
recorded = json.loads((bridge / "artifact.json").read_text())["sha256"]
golden = json.loads((bridge / "Tests/Resources/SA.Motion/PackageSmoke.json").read_text())["Sha256"]
problems = [f"{name} {value} != dll {dll}" for name, value in (("artifact.json", recorded), ("PackageSmoke.json", golden)) if value != dll]
reactions = bridge.parent / "com.sa.unity.reactions"
humanoid = hashlib.sha256((reactions / "Runtime/Plugins/SA.Humanoid.dll").read_bytes()).hexdigest()
recorded_humanoid = json.loads((reactions / "artifact.json").read_text())["sha256"]
if humanoid != recorded_humanoid:
    problems.append(f"reactions artifact.json {recorded_humanoid} != SA.Humanoid.dll {humanoid}")
print("\n".join(problems) if problems else f"artifacts consistent: SA.Motion {dll[:12]} SA.Humanoid {humanoid[:12]}")
sys.exit(1 if problems else 0)
