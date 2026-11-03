---
name: sa-unity-cli
description: Drive the shared Unity Editor for SA animation, shape and ragdoll work through the official Unity CLI. Use for SA scene, shader, package, Inspector, bake and runtime verification tasks in sa-motion, com.sa.unity, sa-demo or ruk2.
---

# SA Unity CLI

Read the project's `AGENTS.md` first. Load the official `unity-cli` skill too; if it is not available in the client, `unity skill show` prints it without installing anything. This companion records SA project requirements and measured failures. Keep it separate from the official plugin cache so a plugin upgrade does not erase it.

For rig compatibility or scope decisions, read [references/rig-coverage.md](references/rig-coverage.md). For rendering, proportions or package changes, read [references/shape-rendering.md](references/shape-rendering.md). The maintained source for this skill is `.claude/skills/sa-unity-cli/` in `IAFahim/sa-motion`; Codex discovers the same source through `.agents/skills/sa-unity-cli`.

## Choose the checkout and Editor before changing anything

- Run `git status --short --branch` in each repository involved, then `git fetch origin`. Read the requested issue and its latest comments with `gh issue view <number> --repo <owner/repo> --comments`. Identify the actual handoff branch; a local package checkout can be much older than a published fix. Fetching does not update the working tree. Integrate needed changes without discarding local edits.
- `ruk2` is a Unity project, not necessarily a Git repository. `Packages/com.sa.unity` is a separate repository. Never edit `Packages/com.rukhanka.animation`; Rukhanka is a reference implementation. Portable SA kernels belong in `src/`, Unity adapters in `packages/`. SA uses ECS timeline commands; do not introduce Animator state machines or parameters to schedule playback.
- Find the local `sa-motion` checkout containing `eng/unity.sh`. Check its branch and the project package resolution before assuming it is the code under test. Worktrees isolate source changes; this SA setup shares one Editor under the wrapper lock. Do not start an Editor per source worktree.
- Run `unity status --format json`. Pin direct Editor calls with `--project-path <absolute Unity project root>`. The live project can be `sa-demo` while the shell is in `ruk2`; sending a command to the wrong project is a real mutation of that project.
- A failed connection can mean import, domain reload, Safe Mode or a modal dialog. Inspect process arguments, the Editor log and `unity command ... editor status` before deciding the Editor is closed. Do not invoke the starter while an existing process is importing. Never kill a windowed Editor. Quit through `EditorApplication.Exit` only when a shutdown is needed and authorized by the task.

## Compile, test and execute C# through the wrappers

Run these from the chosen `sa-motion` checkout, replacing the project path each time:

```sh
SA_PROJECT=/absolute/path/to/project eng/unity_start.sh
SA_PROJECT=/absolute/path/to/project eng/unity.sh compile
SA_PROJECT=/absolute/path/to/project eng/unity.sh test SA.Tests
SA_PROJECT=/absolute/path/to/project eng/unity.sh test SA.Motion.Unity.Tests
SA_PROJECT=/absolute/path/to/project eng/unity.sh eval /tmp/sa-state.cs
```

Start only after confirming there is no Editor already running for that project. `SA_UNITY_WINDOW=1` requests a visible window. On another machine, set `SA_UNITY_EDITOR` to its installed Editor executable if the starter's default path is wrong. `SA_PROJECT` defaults to `/home/i/ruk2`, so always pass it when targeting a different project. These variables choose tooling targets; they must not become hidden runtime feature switches.

The wrapper locks the shared Editor and is the required path for compiling, running tests and executing C#. Do not use `unity test`, a second batch Editor or a separate eval path to bypass that lock. Read `eng/unity-test-assemblies.txt` for the assemblies relevant to other packages. Pure `src/` changes use `eng/dotnet_test.sh` and need no Editor.

`script recompile_status` reports success as either `completed` or `up_to_date`; check `failed` and `errors` as well. Waiting on `up_to_date` wastes the full timeout. A test run with zero tests, a compile failure or a timeout is not a passing test run. Do not weaken an assertion to repair a failure.

Discover other operations rather than guessing names:

```sh
unity command --caller plugin --skill unity-cli --project-path /absolute/path/to/project --query capture --detail compact
unity command --caller plugin --skill unity-cli --project-path /absolute/path/to/project --result-only editor status
unity command --caller plugin --skill unity-cli --project-path /absolute/path/to/project --result-only console get --tail 30
```

Keep the SA invocation labels `--caller plugin --skill unity-cli`. Prefer JSON for scripts and keep stderr separate from stdout. The wrapper tolerates trailing CLI hint text by decoding the first JSON object. Check the command's exit status and failure fields; do not treat a partial response as completion.

## Inspect state before editing

Write the following to a temporary `.cs` file outside `Assets`, then run it with `eng/unity.sh eval`:

```csharp
var report = "project=" + UnityEngine.Application.dataPath + "; playing=" + UnityEditor.EditorApplication.isPlaying + "; paused=" + UnityEditor.EditorApplication.isPaused;
for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
{
    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
    report += "\nscene=" + scene.path + "; dirty=" + scene.isDirty;
}
return report;
```

Use live Editor APIs for scenes, prefabs, materials and assets when the Editor is reachable. Do not overwrite a dirty scene or hand-edit its YAML. Reopen changed assets from disk after an import before judging the result. For disposable objects, use a preview scene or `HideFlags.HideAndDontSave`; creating then deleting a normal scene object can still leave a dirty flag and block later tests with a save dialog. Do not dismiss a save dialog by discarding the owner's changes.

For a full Inspector PNG plus text, use `com.sa.unity.inspect` through eval:

```csharp
return SA.Inspect.InspectorSnapshot.Report(SA.Inspect.Targets.Find("Main Camera"), "Temp/look/camera.png", 700).ToString();
```

For an open window, use `SA.Inspect.WindowSnapshot.Report("Console", "Temp/look/console.png")`. Read the PNG as well as its text: most Inspector controls are IMGUI and their actual help boxes may exist only in the pixels. Never capture the desktop; it can expose the owner's private windows. Remove temporary captures and imported capture assets when finished.

Never copy a struct containing `BlobArray` out of a Unity blob. Read it through a typed `ref`: `ref var clip = ref reference.Value`. Relative offsets in a copied blob struct can crash the Editor.

## Prove the person's result

For the shape workshop, open `Assets/Scenes/ShapeWalk.unity` or invoke `Tools/SA/Shape Workshop/Open Scene`. Enter Play from the host, not from `Assets/ShapeWalk/Character.unity` alone. Wait for SubScene streaming. A Play-mode flag alone does not prove frames are advancing; compare `Time.frameCount` or game time at two observations.

Check default Idle, move Both legs / Left leg / Right arm, switch Walk and Run, use Pause clip, then Restore neutral. Confirm the visible body changes and returns. Read a fresh runtime Console after test runs; a static diagnostic from a temporary test world is not a current runtime fault. Use deterministic stepping from `AGENTS.md` when timing matters; an unfocused Editor can run at about 3 Hz with a 0.33 s delta.

Set the actual Game view resolution before taking screenshots. `capture game_view` can resize an existing texture without changing the render resolution. In the workshop, invoke `Tools/SA/Shape Workshop/Frame Game View`; it uses `UnityEditor.PlayModeWindow.SetCustomRenderingResolution(1280, 720, "Shape workshop")`. Capture from Unity, then inspect the PNG. For a pixel comparison, use the same clip time, camera, lighting and resolution and exclude the changing UI.

Save, reopen the host from disk and Play again. Check the materials, profile, closed SubScene and package dependencies still resolve. Temporary probes and hidden switches never belong in a shipped scene. Report the exact platform/API, commands, actual pass counts, visible observations and remaining unverified gates. A single mapped rig is not proof of arbitrary Humanoid/Generic support or a player build.
