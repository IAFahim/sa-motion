# Architecture

The repository is an engine-agnostic .NET motion library plus thin Unity adapters. The rule from `AGENTS.md`: code that compiles without Unity lives in `src/` (netstandard2.1, C# 9); code that must touch Entities, Physics, Burst attributes or rendering lives in `packages/`; physics data crosses as plain structs, snapshot in and intent out, never an interface or callback.

The reason is portability: the kernel math must move to any engine unchanged while Unity only converts, schedules and calls. The `PublicAbiIsUnmanagedAndEngineFree` test enforces this by checking the struct sizes, the netstandard2.1 target and that the portable assembly references no Unity assembly.

## Data flow

A caller animates one pose per frame in three steps. First it keeps history: `PoseHistory.Reset` stores the corrected output, and each frame `PoseHistory.Capture` shifts it to `Previous`/`Current` with the actual sample interval; a zero or invalid interval keeps the old history so velocity stays owned by real samples.

On interruption the caller builds a transition: `PoseTransition.Create` measures position, rotation and log-scale offsets between the corrected history and the new destination and fits each a finite-duration `ScalarDecay`, a normalized-time quintic with zero end value, velocity and acceleration. Out-of-range inputs select the destination directly instead of throwing.

Each frame the caller applies it: `PoseTransition.Apply` evaluates the decays at the elapsed time and adds them onto the live destination, so a moving destination is followed and the output lands exactly on it after the duration. An invalid signed destination scale clears only the scale correction. No managed memory is allocated per call; all state stays in caller-owned structs.

`src/SA.Humanoid` holds only the 15-role humanoid contract (`Roles.cs`); it has no kernel code yet.

## Unity consumption

Unity consumes the same DLL bytes that the .NET tests and the local NuGet consumer use. `eng/pack_bridge.py` packs `src/SA.Motion`, restores it from the task-local feed and stages the exact `lib/netstandard2.1/SA.Motion.dll` payload under `packages/com.sa.motion.unity/Runtime/Plugins`; the committed `artifact.json` records its SHA256 `14cf3593e78d1c46653929f4abeabb8129d504df8c252f5b60f330958c85da4e` with `netstandard`-only references.

The adapter itself is only `Runtime/Conversions.cs`, mapping `Float3`/`Quaternion` to `Unity.Mathematics` `float3`/`quaternion` and back, plus the `Samples~/CostProbe` and `Samples~/PlayerProof` scaffolding. Proof runs in `Tests/BurstKernelTests.cs`: the same transition math executes as a synchronous Burst IJob and as a Burst function pointer, asserting native sentinel 1 against managed control 0 and numerical match with the .NET fixture within tolerance.

## Evidence

Copied from `docs/verification.md`; every row was recorded 2026-10-31 on Unity 7000.0.0a12 unless noted. The portable count below predates the one README sample test added by this packet.

| Check | Actual result | Date |
|---|---|---|
| Portable numerical/edge/interruption/ABI/allocation tests | 38 passed, zero failed/skipped | 2026-10-31 |
| Local packed NuGet consumer | Passed; restored from the task-local feed, not a ProjectReference | 2026-10-31 |
| Unity bridge tests | 3 passed, zero failed/skipped | 2026-10-31 |
| Native execution proof | Synchronous IJob and function pointer asserted native sentinel=1; managed control=0 | 2026-10-31 |
| Cross-runtime data | Managed Unity/Burst matched the .NET consumer fixture within tolerance | 2026-10-31 |
| Artifact/ABI | Packed and imported DLL SHA256 matched; references only `netstandard`; POD size checks passed | 2026-10-31 |
| Existing SA regression suite | 35 passed, zero failed/skipped, before bridge integration | 2026-10-31 |
| Standalone player/native Burst | Passed on Linux CoreCLR with variable-input native job; see `docs/player-verification.md` | 2026-10-31 |
| Bounded kernel/conversion cost probe | Passed with native sentinel 1 / managed control 0; three runs in the cost section | 2026-10-31 |
| Delirium-derived controller | Not implemented; source/data gates remain unresolved | 2026-10-31 |

Cost figures are stated only as measured in `docs/verification.md`: unit scale 74-79 ns managed and 43-47 ns Burst per `PoseTransition.Apply` call; non-unit scale 114-124 ns managed and 76-81 ns Burst.

## Not built yet

Planned work (reaction controller port, Unity integration, GPU path, humanoid baker) is listed in `docs/work-packets.md` and gated in `docs/provenance.md`. Nothing in this file describes it.
