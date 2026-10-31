import csv
import subprocess
import sys
import time
from pathlib import Path

project = Path("/home/i/ruk2")
scene = "Assets/ReactionsDemo/ReactionsDemo.unity"
telemetry = project / "Temp/sa/demo_telemetry.csv"


def unity(*args, timeout=120):
    return subprocess.run(["unity", "command", "--result-only", f"--timeout={timeout}", *args], cwd=project, capture_output=True, text=True)


unity("scene", "open", "--path", scene)
telemetry.unlink(missing_ok=True)
unity("editor.playmode", "start")
time.sleep(float(sys.argv[1]) if len(sys.argv) > 1 else 34)
unity("editor.playmode", "stop")
for _ in range(20):
    if telemetry.exists():
        break
    time.sleep(0.5)
rows = [{k: (float(v) if k not in ("mode", "event") else v) for k, v in row.items()} for row in csv.DictReader(telemetry.open())]
if not rows:
    print("no telemetry rows")
    sys.exit(1)


def window(low, high):
    return [r for r in rows if low <= r["time"] < high]


checks = []
standing = window(2.5, 4.8)
checks.append(("stands before the first hit", bool(standing) and all(0.9 < r["pelvis_y"] < 1.08 for r in standing)))
shove = window(5.0, 6.5)
baseline = sum(r["head_x"] for r in standing) / len(standing), sum(r["head_z"] for r in standing) / len(standing)
deflection = max(((r["head_x"] - baseline[0]) ** 2 + (r["head_z"] - baseline[1]) ** 2) ** 0.5 for r in shove) if shove else 0
checks.append((f"light shove deflects the head ({deflection:.2f} m)", deflection > 0.05))
checks.append(("strength dips after a shove", bool(shove) and min(r["strength"] for r in shove) < 0.8))
checks.append(("no explosion", all(abs(r["pelvis_y"]) < 3 and abs(r["head_y"]) < 3 for r in rows)))
down = window(21.0, 23.5)
checks.append(("knockdown goes limp", any(r["mode"] == "Limp" for r in down)))
checks.append(("knockdown puts the pelvis on the floor", bool(down) and min(r["pelvis_y"] for r in down) < 0.35))
after = window(26.0, 27.5)
checks.append(("recovers to standing", bool(after) and all(0.9 < r["pelvis_y"] < 1.08 for r in after)))
failed = [name for name, ok in checks if not ok]
for name, ok in checks:
    print(("pass " if ok else "FAIL ") + name)
print(f"rows {len(rows)} span {rows[0]['time']:.1f}-{rows[-1]['time']:.1f}s")
sys.exit(1 if failed else 0)
