# Architecture

## Layers

| Layer | Paths | Role |
|---|---|---|
| Portable kernels | `src/SA.Motion` | Inertialization (`PoseTransition` over `PoseHistory`), frames/inertia conversions, bounded quaternion drive; MIT |
| Humanoid contract | `src/SA.Humanoid` | 15-role contract, rig mapping, cited mass profile, ragdoll layout builder; MIT |
| Controller port | `src/SA.ReactionControl` | Balance, fall, brace, drive, coordination, get-up areas plus fixed contracts; GPL-3.0-only, see `docs/provenance.md` |
| Tuning harness | `tests/SA.Harness` | Deterministic 15-body simulator for controller tuning; test support only, not shipped |
| Unity adapters | `packages/com.sa.motion.unity`, `packages/com.sa.unity.reactions` | Conversions, ragdoll baker, drive/track systems, demo sample; MIT, never import controller code |

The rule from `AGENTS.md`: code that compiles without Unity lives in `src/` (netstandard2.1, C# 9); code that must touch Entities, Physics, Burst attributes, or rendering lives in `packages/`; physics data crosses as plain structs, snapshot in and intent out, never an interface or callback. The portable assembly references no Unity assembly; `PublicAbiIsUnmanagedAndEngineFree` enforces the ABI side of that.

## Data flow: animation to physics to bones

Two paths exist. The demo runs on physical animation only; the ported controller is merged but not wired in (see `docs/demo.md`).

```
Animation clip (per-body target pose, world space)
        |
        v
+------------------+     +-------------------+
| PoseDriveSystem  |     | PoseTrackSystem   |
| rotation error   |     | body velocity     |
| -> angular       |     | toward animated   |
| impulse, same    |     | pose, strength    |
| physics step     |     | scaled            |
+--------+---------+     +---------+---------+
         |                         |
         v                         v
Unity Physics world: 15 bodies on ball joints,
masses from HumanProfile, hit impulses via RagdollHit
         |
         v
Bones: animated lengths kept; physics supplies
rotations plus the pelvis translation
```

`RagdollAuthoring` builds the 15 bodies, colliders, joints, and drives from the rig; a hit spreads impulse over the body by mass, drops strength with hit size, goes limp past the threshold, then resets to the animation pose after `LimpSeconds`.

The second path is interruption smoothing without physics:

```
Corrected output history (PoseHistory.Reset/Capture per frame)
        |
        v (on interruption)
PoseTransition.Create: offset between history and destination,
fitted as finite-duration ScalarDecay (zero end value/velocity/acceleration)
        |
        v (each frame)
PoseTransition.Apply: decays evaluated at elapsed time, added onto the
live destination, so a moving target is followed and the output lands on it
```

## Design rules

- Engine-agnostic by default. Kernel math must move to any engine unchanged; Unity only converts, schedules, and calls. New code defaults to `src/` and moves to `packages/` only when it cannot compile without Unity.
- Immutable data. Pure functions over `readonly` structs: same input, same output. Callers own state, timestamps, and buffers; no hidden state, no changing statics.
- Total functions. Every input has a defined output, including NaN and infinity: corrupted input falls back to the destination or a default, never a throw. `Quaternion.Multiply` is the plain Hamilton product; kernels normalize around it.
- No comments in code. Names carry the meaning; explanations live in `docs/`. `python3 eng/check_source_rules.py` prints `source rules: ok`.
- No allocation per call, no reflection, no LINQ in `src/`.

## What this file does not claim

Numbers live in `docs/verification.md`, `docs/player-verification.md`, and `docs/gate-m.md`; behavior limits live in `docs/demo.md`. Earlier revisions of this file copied those tables here and the copies went stale; they were removed so each figure has one home.
