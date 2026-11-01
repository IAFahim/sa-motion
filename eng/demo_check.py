import csv
import subprocess
import sys
import tempfile
import time
from pathlib import Path

project = Path("/home/i/ruk2")
args = [a for a in sys.argv[1:] if not a.startswith("--")]
stepped = "--stepped" in sys.argv
scene = args[1] if len(args) > 1 else "Assets/ReactionsDemo/ReactionsDemo.unity"
seconds = args[0] if args else "30"
telemetry = project / "Temp/sa/demo_telemetry.csv"


def unity(*args, timeout=120):
    return subprocess.run(["unity", "command", "--result-only", f"--timeout={timeout}", *args], cwd=project, capture_output=True, text=True)


SET_STOP = """
foreach (var behaviour in UnityEngine.Object.FindObjectsByType<UnityEngine.MonoBehaviour>(UnityEngine.FindObjectsInactive.Include))
{
    if (behaviour == null || behaviour.GetType().Name != "ReactionsDemo")
        continue;
    var serialized = new UnityEditor.SerializedObject(behaviour);
    serialized.FindProperty("StopAfterSeconds").floatValue = SECONDS;
    serialized.ApplyModifiedPropertiesWithoutUndo();
    return "stop after " + SECONDS;
}
return "no ReactionsDemo in the open scene";
"""


def set_stop_after(seconds):
    with tempfile.TemporaryDirectory() as directory:
        code = Path(directory) / "stop.cs"
        code.write_text(SET_STOP.replace("SECONDS", f"{float(seconds)}f"))
        return unity("script", "eval_file", "--file", str(code)).stdout


PAUSE = """
var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
if (world == null || !UnityEditor.EditorApplication.isPlaying)
    return "waiting";
var demo = UnityEngine.Object.FindFirstObjectByType<ReactionsDemo>();
if (demo == null || UnityEngine.Time.frameCount < 4)
    return "waiting";
UnityEditor.EditorApplication.isPaused = true;
world.GetExistingSystemManaged<Unity.Entities.UpdateWorldTimeSystem>().Enabled = false;
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var text = (System.Text.StringBuilder)typeof(ReactionsDemo).GetField("telemetry", flags).GetValue(demo);
text.Clear();
text.Append("time,mode,strength,pelvis_y,head_y,head_x,head_z,event,decision,feet,margin,distL,distR,jump,blend\\n");
typeof(ReactionsDemo).GetField("nextSample", flags).SetValue(demo, 0f);
System.AppDomain.CurrentDomain.SetData("sa.clock", (double)(float)typeof(ReactionsDemo).GetField("clock", flags).GetValue(demo));
return "paused";
"""

STEP = """
var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
var demo = UnityEngine.Object.FindFirstObjectByType<ReactionsDemo>();
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var clockField = typeof(ReactionsDemo).GetField("clock", flags);
var update = typeof(ReactionsDemo).GetMethod("Update", flags);
var clock = (double)System.AppDomain.CurrentDomain.GetData("sa.clock");
const float dt = 1f / 60f;
var init = world.GetExistingSystemManaged<Unity.Entities.InitializationSystemGroup>();
var sim = world.GetExistingSystemManaged<Unity.Entities.SimulationSystemGroup>();
var presentation = world.GetExistingSystemManaged<Unity.Entities.PresentationSystemGroup>();
for (var i = 0; i < FRAMES; i++)
{
    clock += dt;
    clockField.SetValue(demo, (float)(clock - UnityEngine.Time.deltaTime));
    update.Invoke(demo, null);
    world.SetTime(new Unity.Core.TimeData(clock, dt));
    init.Update();
    sim.Update();
    presentation.Update();
}
System.AppDomain.CurrentDomain.SetData("sa.clock", clock);
return "clock=" + clock.ToString("F2");
"""


def run_code(text, timeout=240):
    with tempfile.TemporaryDirectory() as directory:
        code = Path(directory) / "snippet.cs"
        code.write_text(text)
        return unity("script", "eval_file", "--file", str(code), timeout=timeout).stdout


def play_throttled():
    unity("editor.playmode", "start")
    for _ in range(480):
        if telemetry.exists():
            break
        time.sleep(0.5)
    unity("editor.playmode", "stop")


def play_stepped(game_seconds):
    unity("editor.playmode", "start")
    for _ in range(240):
        if '"paused"' in run_code(PAUSE):
            break
        time.sleep(0.5)
    remaining = int(float(game_seconds) * 60)
    while remaining > 0:
        chunk = min(600, remaining)
        run_code(STEP.replace("FRAMES", str(chunk)))
        remaining -= chunk
    unity("editor.playmode", "stop")
    for _ in range(40):
        if telemetry.exists():
            break
        time.sleep(0.5)


unity("scene", "open", "--path", scene)
telemetry.unlink(missing_ok=True)
if stepped:
    play_stepped(seconds)
else:
    print(set_stop_after(seconds).strip())
    play_throttled()
unity("scene", "open", "--path", scene)
rows = [{k: (float(v) if k not in ("mode", "event") else v) for k, v in row.items()} for row in csv.DictReader(telemetry.open())]
if not rows:
    print("no telemetry rows")
    sys.exit(1)


def window(low, high):
    return [r for r in rows if low <= r["time"] < high]


checks = []
standing = window(2.5, 4.8)
height = sorted(r["pelvis_y"] for r in standing)[len(standing) // 2] if standing else 0.0
checks.append((f"stands before the first hit (pelvis {height:.2f} m)", 0.6 < height < 1.2 and all(abs(r["pelvis_y"] - height) < 0.08 for r in standing)))
shove = window(5.0, 6.5)
baseline = sum(r["head_x"] for r in standing) / len(standing), sum(r["head_z"] for r in standing) / len(standing)
deflection = max(((r["head_x"] - baseline[0]) ** 2 + (r["head_z"] - baseline[1]) ** 2) ** 0.5 for r in shove) if shove else 0
checks.append((f"light shove deflects the head ({deflection:.2f} m)", deflection > 0.05))
checks.append(("strength dips after a shove", bool(shove) and min(r["strength"] for r in shove) < 0.8))
checks.append(("no explosion", all(abs(r["pelvis_y"]) < 3 and abs(r["head_y"]) < 3 for r in rows)))
down = window(21.0, 23.5)
checks.append(("knockdown goes limp", any(r["mode"] == "Limp" for r in down)))
checks.append(("knockdown puts the pelvis on the floor", bool(down) and min(r["pelvis_y"] for r in down) < 0.4 * height))
after = window(26.0, 27.5)
checks.append(("recovers to standing", bool(after) and all(abs(r["pelvis_y"] - height) < 0.1 for r in after)))
blending = [r for r in rows if r["blend"] > 0]
pop = max((r["jump"] for r in blending[:2]), default=999.0)
checks.append((f"recovery is inertialized, no pop ({pop:.1f} deg in one frame)", bool(blending) and pop < 15.0))
failed = [name for name, ok in checks if not ok]
for name, ok in checks:
    print(("pass " if ok else "FAIL ") + name)
print(f"rows {len(rows)} span {rows[0]['time']:.1f}-{rows[-1]['time']:.1f}s")
sys.exit(1 if failed else 0)
