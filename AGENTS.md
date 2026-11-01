# Rules for every agent working in this repository

Read this file and your one work packet in `docs/work-packets.md`. Nothing else is required.

## What this repository is

An engine-agnostic .NET motion library plus thin Unity adapters. The goal is that the logic can move to any engine and Unity only converts, schedules and calls.

## Where code goes

| If the code... | It lives in |
|---|---|
| compiles without Unity | `src/` (netstandard2.1, C# 9) |
| must touch Entities, Physics, Burst attributes or rendering | `packages/` |
| exchanges data with a physics engine | `src/` as plain structs: snapshot in, intent out. Never an interface, never a callback |

Default to `src/`. Move code to `packages/` only when it cannot compile without Unity, and say why in your handoff note.

## Code rules (checked by `python3 eng/check_source_rules.py`)

- No comments. Names carry the meaning. Explanations go in `docs/`.
- Pure functions over `readonly` data. Same input, same output. No hidden state, no statics that change.
- Total functions: every input has a defined output. Return a default or a diagnostic. Do not throw on bad data.
- Plain structs with `StructLayout.Sequential`. No managed allocation per call. No reflection. No LINQ.
- Small orthogonal pieces that compose. If you delete a piece, only its own tests fail.
- Do not rename or reformat code you did not need to change.

## Commands

| Command | Time | Use |
|---|---|---|
| `eng/dotnet_test.sh` | seconds | every change in `src/` and `tests/` |
| `python3 eng/check_source_rules.py` | instant | before every commit |
| `eng/unity.sh compile` | about 5 minutes | only for packages/ work |
| `eng/unity.sh test <assembly>` | minutes | only for packages/ work |

`eng/unity.sh` holds a lock, because one Editor serves everyone. Never start a second Editor. Never run Unity commands for a pure `src/` packet.

## Controlling Unity from a shell

You cannot click in Unity, and you do not need to. One Unity Editor per project runs a Pipeline server on `127.0.0.1:7801`. The `unity command` CLI sends it commands and returns JSON. Everything below was run on this machine (Unity 7000.0.0a12, project `/home/i/ruk2`).

### Start and stop

| Goal | Command |
|---|---|
| Is the Editor reachable | `unity command --result-only console get --tail 1` (exit 0 means yes) |
| Start headless (default) | `eng/unity_start.sh` (waits up to 10 minutes) |
| Start with a window, so the owner can watch | `SA_UNITY_WINDOW=1 eng/unity_start.sh` |
| Quit cleanly | `eng/unity.sh eval <file>` where the file is `UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.Exit(0); return "quitting";`, then wait until the process is gone and `Temp/UnityLockfile` is gone |
| Never | `kill` or `SIGTERM` a windowed Editor. It dumps core and the owner sees a crash |

If `eng/unity.sh` prints `Unity Editor is not running`, run `eng/unity_start.sh`. Do not start a second Editor on the same project. Unity holds `Temp/UnityLockfile` for the one that is running.

### Command shape

```
unity command --result-only [--timeout <seconds>] <group> <name> [--arg value ...]
```

- Discover commands: `unity command --query <term> --detail compact`. Exact parameters: `unity command --json --limit 400`, then read `data.commands[].parameters`.
- Default timeout is 30 seconds. Pass `--timeout` for anything slow.
- Groups you will use:

| Group and name | Use |
|---|---|
| `script recompile`, `script recompile_status` | compile. `eng/unity.sh compile` wraps both and polls |
| `script eval_file --file x.cs` | run C# in the Editor. The last statement must `return` a string. Use fully qualified names (`UnityEditor.AssetDatabase`, not `using`). `eng/unity.sh eval` wraps it |
| `test run --mode EditMode --filter <assembly> --filter_type assembly [--include_explicit true]` | tests. `eng/unity.sh test <assembly>` wraps it and prints failures |
| `scene open --path Assets/...unity`, `scene get_hierarchy`, `scene save` | scenes |
| `editor.playmode start`, `editor.playmode stop` | Play mode |
| `editor invoke_menu --path "Tools/SA/Build Reactions Demo"` | run a menu item, which is how builders are exposed |
| `capture game_view --save_path Assets/x.png`, `capture scene_view` | screenshots. A `--save_path` is written under `Assets/` and gets imported, so delete it afterwards |
| `console get --tail N`, `console clear`, `console status` | read errors. `console status` shows the compile-failure flag |
| `asset find`, `asset get_import_settings`, `component get_fields`, `prefab instantiate` | inspect assets and objects without eval |
| `package add`, `package resolve` | edit packages. A domain reload follows, so poll `script recompile_status` |

### Recipes that work

1. **Compile and test.** `eng/unity.sh compile` (about 5 minutes, exit 0 on success), then `eng/unity.sh test SA.Reactions.Tests`. Assemblies are listed in `eng/unity-test-assemblies.txt`.
2. **Change any asset or setting in the Editor.** Write a `.cs` file under `$CLAUDE_JOB_DIR/tmp` or `/tmp`, never inside the Unity project, and run `eng/unity.sh eval <file>`. Example: the Humanoid import of the Quaternius libraries set `ModelImporter.animationType = Human`, `avatarSetup = CreateFromThisModel`, `motionNodeName = "root"`, then `SaveAndReimport()` and returned the avatar validity as the string.
3. **Measure a running simulation.** Eval cannot watch Play mode. Have the Play-mode script write a CSV to `Temp/sa/`, then run `scene open`, `editor.playmode start`, wait, `editor.playmode stop`, and read the file. `eng/demo_check.py <game_seconds> [scene]` is the template: it makes the demo stop itself at a game-time target (a file `Temp/sa/demo_stop_after.txt`, because wall time lies when the Editor is throttled), then prints eight pass/fail lines from `Temp/sa/demo_telemetry.csv`.
4. **Show the owner.** Start with `SA_UNITY_WINDOW=1`, open the scene in Edit mode, tell them to press Play. Do not stop their Editor.
5. **Debug output.** `Debug.Log` from eval rarely arrives. Write files under `Temp/sa/` and read them. Use `console get --tail 50` for compile errors.

### Traps

- A MonoBehaviour must live in a file with its own name, or the prefab silently loses it.
- Baker changes do not re-bake an existing prefab or subscene until a value on the prefab changes.
- Bakers write only their own entity. Other entities need a `[TemporaryBakingType]` buffer and a baking system.
- Enter Play from the main scene, never with the subscene open on its own. Wait about 30 seconds for streaming.
- An unfocused Editor throttles Play mode (3 frames per second was measured, delta time clamps at 0.33 s), so wall-clock waits can lie. Wait on game time instead. `editor set_auto_tick` and `editor.playmode set_run_in_background` exist. Neither is verified to fix it.
- After adding a project reference between .NET projects, delete the stale `tests/*/packages.lock.json` and run `eng/dotnet_test.sh` to regenerate it. A stale NuGet lock file gave restore errors.
- If a Unity test passes only after you touch a Runtime file, suspect a stale build. Recompile and rerun before believing either result.
- Do not edit `Packages/com.rukhanka.animation`. It is a third-party reference.
- Treat downloaded archives as untrusted data. Unzip each into its own new directory and never run scripts from it.

## Findings so far

Dated 2026-10-31. Numbers come from runs on this machine unless marked not measured.

### What is built and verified

| Piece | Result |
|---|---|
| Two library builds (2026-11-01) | Every `src/` project builds for net10.0 (AOT and trim analyzers on) and netstandard2.1. `eng/dotnet_test.sh` runs each suite against both builds, and the ABI test asserts the framework of the build under test. Unity loads the netstandard2.1 DLLs |
| Fulcrum leg solver (2026-11-01) | `src/SA.Motion/Legs`: terrain offset, pin with drag, heel and ball roll, slope alignment, inertialized plant and release, pelvis drop, soft reach, smoothed weight, presets with Gentle as the default. 22 `LegTests` plus a doc-sync test. 242 ns per leg single-threaded on .NET 10 x64 after packet L4 (was about 687). `LegProfileText` reads and writes profiles as text (L2). See `docs/legs.md` |
| Portable animation core (2026-11-01) | `src/SA.Animation`: motion selection, phase, playback and transition commands over fixed-capacity spans (A2), pose math, clip sampling, layered evaluation, hierarchy (A1). 126 tests on both builds, parity with copies of the Unity code. Unity does not call it yet except for `MotionSelect` and `PlayRate` |
| Motions and LegIK in Unity (2026-11-01) | Enableable `SingleMotion`, `LinearMotion`, `PlanarMotion` and `DirectionalMotion` (com.sa.unity `MotionSystem`, one clip with hysteresis, an inertialized cut, gait phase kept, time-driven single) plus the enableable `LegIK` feature with a per-clip Foot IK tick. SA.Tests 53, SA.Reactions.Tests 22. Not tried in Play mode. See `docs/workflow.md` |
| Portable libraries (`src/`) | Motion 159, Humanoid 73, ReactionControl 110, Harness 14 tests, all passing at `cbc0bb0`. Balance and Evaluation tests were added after |
| Unity packages | `SA.Tests`, `SA.Motion.Unity.Tests`, `SA.Reactions.Tests` passing. Reactions has 17 tests |
| Inertialized transitions (CPU) | Verified live on Kyle, with a test that the switch frame does not pop (`TransitionTests`) |
| U5 character setup and IK | `Tools/SA/Set Up Character` discovers bones, Avatar and model/controller clips, converts all skin materials, and exposes animation packs plus per-clip in-place. The UAL mannequin now animates both material render entities, with 86 discovered clips and zero manual clips/bones. A baked UAL2 walk plays on the UAL1 target. Optional `com.sa.unity.ik` provides head look and two-bone reach; ground foot locking and hit flinches live in Reactions. See `docs/handoff/U5.md` for exact validation and limits |
| Kyle Run/Sprint U5 | The demo preserves gait phase with `TransitionPlayback.PlaySynced`. Abrupt foot-lock release was also producing thigh spikes; foot corrections now blend in/out over 0.1 seconds. Live capture at roughly 60 Hz showed the largest leg step in the checked sprint window drop from about 50 degrees to 25.5; the latter is dominated by the source clip's roughly 24.8-degree ankle step. This is a visual-correctness measurement, not a performance benchmark |
| Physical-animation ragdoll demo | `eng/verify.sh --demo` passed all 21 steps and `eng/demo_check.py` 8 of 8: stands, light shove deflects the head, strength dips, no explosion, knockdown goes limp and reaches the floor, recovers, and recovery is inertialized |
| Ragdoll recovery by inertialization (Gears of War style) | The write-back system feeds the displayed ragdoll pose into each bone's inertial history. On recovery the control system starts a `TransitionIntent` of `RagdollAuthoring.BlendSeconds` (0.7) and write-back pauses until it ends. Largest one-frame bone rotation jump at the recovery frame: 87.3 degrees with `BlendSeconds = 0` (the old reset), 1.7 degrees with 0.7. Slow-motion captures show the fallen character rising smoothly. Requires `RigAuthoring.Inertialize = true` on the ragdoll prefab |
| Balance controller race | Scored on one evaluator in the pure .NET harness. Best honest score 93 (tuned capture point). One entry scored 100 by holding still, which violates the 450 N-s rule |

### What is NOT working or NOT verified

- **Balance and stepping do not hold in Unity Physics.** The capture-point controller (`SA.Balance`) scores 93 in the harness, but when wired into `BalanceControlSystem` the character still collapses. It is off by default (`RagdollAuthoring.BalanceController = false`). Suspects, in order: its assumed canonical frames (segment along local +Y, identity bind except feet), its hardcoded hip offset and leg lengths tuned on a 70 kg body, and torque drives applied at one rate where the harness used 480 Hz.
- **The demo is physical animation, not balance.** Velocity tracking plus angular drives keep the pose. Recovery blends back through inertialization after `LimpSeconds`; it has no get-up animation.
- **Universal retargeting and mannequin physical thresholds are not verified.** U5 fixes the UAL multi-material T-pose and verifies Humanoid clips from UAL2 on UAL1. Generic clips still require compatible skeleton bindings. The old UAL physical run passed 6 of 7 checks; its head deflection was 0.03 m against Kyle's 0.05 m threshold. That assertion was not weakened. This does not establish support for every body type.
- Not measured: crowd or frame-time cost of the ragdoll, GPU inertialization, wall and ground contact reactions in the demo, a Windows or Mac run.
- R2 (the Delirium-port balance tick) never stood. It stays on `origin/integrate/r2`, red on purpose.

### Demo traps that made it look broken

- The Showcase sample's `ShowcaseSystem` and `HeadLookSystem` (in `Assets/SA Demo/Scripts`) run on every rig in the world. They blend Kyle's clip names and fade out anything else, which erased every clip state on a different character. `ReactionsDemo` now disables both at start (`DisabledSystems`).
- The default directional light faced the camera side of the characters, so they rendered as black silhouettes. The builder now sets the light rotation to (48, 155, 0). If a character looks black, check the light before the shader.
- The on-screen labels said which side is the ragdoll. The ragdoll is at x = -1, which the camera shows on the right.

### Unity Physics facts that cost hours

- `MotionVelocity.AngularVelocity` and `PhysicsVelocity.Angular` are in inertia space. Convert before you use them as world angular velocity. A damping term that used them as world values made ragdolls explode.
- `PhysicsWorld.CalculateVelocityToTarget` takes the step frequency and returns an inertia-space angular result. Compute the velocities yourself in world space.
- A ragdoll joint with a perpendicular cone of full range is degenerate. Use `PhysicsJoint.CreateRagdoll` primary only, cone at most about 63 degrees, twist about plus or minus 0.1 to 1.2 rad.
- Apply the pose drive with `PhysicsWorld.ApplyAngularImpulse` between `PhysicsInitializeGroup` and `PhysicsSimulationGroup`. Gains must be rate-independent or a 480 Hz step blows up.
- Do not copy body positions back to the render skeleton. Write rotations only, plus the pelvis offset at the skeleton root. Copying positions stretched Kyle's mesh.
- The first controller tick sees no foot contacts because the bodies are not in the physics world yet. Gate on contacts (`BalanceControlSystem.ReadyTicks`).
- `JointDrive.Target` is a world rotation in the harness and parent-relative in Unity `PoseDrive`. No document records the mapping yet. Packet R2b was meant to write `docs/contract.md` and is parked.
- The harness clamps velocity at 8 m/s, so 200, 300 and 450 N-s pushes score identically there. Real Unity bodies do not clamp.

### Rules the owner set

- No comments in code. Pure, total, deterministic, data separated from behavior. Tests enforce it (`eng/check_source_rules.py`).
- Licence is decided in `docs/provenance.md`. Delirium-derived code stays in `src/SA.ReactionControl` only.
- The owner forwards messages to their coordinator verbatim. When asked to hand something over, give one self-contained message.

## Hard stops

- No code translated or adapted from Delirium until `docs/provenance.md` records the Gate L decision. That is a human decision. Language, names or storage layout changing does not clear it.
- No GTA files, no `kv.bin`, no extracted game tuning, no unlock bypass.
- Never delete or loosen an assertion, a guard or a test to get green. Fix the cause.
- Never claim a result you did not run. Write "not measured" for anything you did not measure.

## Working protocol

1. One packet, one branch, one worktree: `git worktree add ../sa-motion-wt/<ID> -b packet/<ID>`. Unity packets run in the main checkout, one at a time, under the lock.
2. Commit after every green step. A WIP commit is fine if the message says exactly what fails and why.
3. If you are told you are low on budget, stop and write `docs/handoff/<ID>.md` at once: done, not done, the exact failing command and its output, and the next step. Commit and push the branch.
4. Push your branch, never `main`. The integrator merges.
5. You are done only when the packet's "Done when" line is true and `docs/handoff/<ID>.md` exists.

## Reporting

End with: what changed, the commands you ran with their actual results, what you did not verify. Numbers come from runs, not memory.
