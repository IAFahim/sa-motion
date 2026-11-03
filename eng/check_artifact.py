import hashlib
import json
import sys
from pathlib import Path

bridge = Path(__file__).resolve().parents[1] / "packages/com.sa.motion.unity"
dll = hashlib.sha256((bridge / "Runtime/Plugins/SA.Motion.dll").read_bytes()).hexdigest()
recorded = json.loads((bridge / "artifact.json").read_text())["sha256"]
golden = json.loads((bridge / "Tests/Resources/SA.Motion/PackageSmoke.json").read_text())["Sha256"]
problems = [f"{name} {value} != dll {dll}" for name, value in (("artifact.json", recorded), ("PackageSmoke.json", golden)) if value != dll]
animation = hashlib.sha256((bridge / "Runtime/Plugins/SA.Animation.dll").read_bytes()).hexdigest()
recorded_animation = json.loads((bridge / "artifact.json").read_text()).get("animationSha256")
if animation != recorded_animation:
    problems.append(f"artifact.json animationSha256 {recorded_animation} != SA.Animation.dll {animation}")
content = hashlib.sha256((bridge / "Runtime/Plugins/SA.Animation.Content.dll").read_bytes()).hexdigest()
recorded_content = json.loads((bridge / "artifact.json").read_text()).get("contentSha256")
if content != recorded_content:
    problems.append(f"artifact.json contentSha256 {recorded_content} != SA.Animation.Content.dll {content}")
shape = hashlib.sha256((bridge / "Runtime/Plugins/SA.Shape.dll").read_bytes()).hexdigest()
recorded_shape = json.loads((bridge / "artifact.json").read_text()).get("shapeSha256")
if shape != recorded_shape:
    problems.append(f"artifact.json shapeSha256 {recorded_shape} != SA.Shape.dll {shape}")
reactions = bridge.parent / "com.sa.unity.reactions"
humanoid = hashlib.sha256((reactions / "Runtime/Plugins/SA.Humanoid.dll").read_bytes()).hexdigest()
recorded_humanoid = json.loads((reactions / "artifact.json").read_text())["sha256"]
if humanoid != recorded_humanoid:
    problems.append(f"reactions artifact.json {recorded_humanoid} != SA.Humanoid.dll {humanoid}")
balance = hashlib.sha256((reactions / "Runtime/Plugins/SA.Balance.dll").read_bytes()).hexdigest()
recorded_balance = json.loads((reactions / "artifact.balance.json").read_text())["sha256"]
if balance != recorded_balance:
    problems.append(f"reactions artifact.balance.json {recorded_balance} != SA.Balance.dll {balance}")
print("\n".join(problems) if problems else f"artifacts consistent: SA.Motion {dll[:12]} SA.Humanoid {humanoid[:12]}")
sys.exit(1 if problems else 0)
