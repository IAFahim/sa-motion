# SA.Motion

SA.Motion is an engine-independent .NET motion library plus thin Unity adapters. Portable inertialization kernels and mechanics live in `src/SA.Motion`, the 15-role humanoid contract/mapping/profile in `src/SA.Humanoid`, a ported reaction controller in `src/SA.ReactionControl`, and the ragdoll packages plus a two-character demo under `packages/`. Everything portable is pure functions over caller-owned `readonly` structs: same input, same output, no throw on bad data. Unity only converts, schedules, and calls.

## What exists today

| Layer | Paths | Licence |
|---|---|---|
| Portable library | `src/SA.Motion`: inertialization, frames/inertia, bounded quaternion drive | MIT |
| Humanoid | `src/SA.Humanoid`: roles, rig mapping, cited mass profile, ragdoll layout | MIT |
| Controller port | `src/SA.ReactionControl`: balance, fall, brace, drive, coordination, get-up | GPL-3.0-only |
| Unity packages | `packages/com.sa.motion.unity` (bridge), `packages/com.sa.unity.reactions` (ragdoll) | MIT |
| Demo | `Samples~/ReactionsDemo`: inertialized switcher plus physical-animation ragdoll | MIT |

How it fits together: `docs/architecture.md`. Every document: `docs/index.md`. Measured results: `docs/verification.md`.

## Checks

`eng/verify.sh` runs every check in one command; `eng/verify.sh --demo` adds the headless Reactions demo. Real output, run 2026-10-31 on this branch:

```text
source-rules                   pass     0s
dotnet-SA.Harness              pass     0s
dotnet-SA.Harness.Tests        pass     5s
dotnet-SA.Humanoid.Tests       pass     2s
dotnet-SA.Motion.Tests         pass     2s
dotnet-SA.ReactionControl.Tests pass     2s
bridge-pack                    pass     2s
humanoid-pack                  pass     1s
bridge-artifact-consistent     pass     0s
unity-start                    pass     0s
unity-compile                  pass   307s
unity-SA.Tests                 pass     1s
unity-SA.Motion.Unity.Tests    pass     1s
unity-SA.Reactions.Tests       pass     1s
reactions-demo                 pass    35s
failures: 0 (logs and report.json in artifacts/verify)
```

That is 290 .NET tests (Motion 146, Humanoid 57, ReactionControl 79, Harness 8), three Unity suites, and 7 of 7 demo telemetry checks (`knockdown puts the pelvis on the floor`: pass). `python3 eng/check_source_rules.py` prints `source rules: ok`.

## Licence map

`src/SA.Motion`, `src/SA.Humanoid`, and both packages are MIT (see `LICENSE`). `src/SA.ReactionControl` and `tests/SA.ReactionControl.Tests` are GPL-3.0-only, translated from Delirium Physics rev `8e4ad4e` under Gate L; any build containing them stays private, with no registry, public repo, or binary release (see `docs/provenance.md`). No GTA files, game values, or unlock bypasses anywhere.

## Not built yet

From `docs/work-packets.md`: R9 (assemble and tune one controller from the merged areas), then a GPL adapter package feeding it from `RoleBody` snapshots; recovery animation content (X2, so get-up returns "no suitable clip"); public IL2CPP/platform claims (X3); any release of `SA.ReactionControl` (X4, needs legal advice). From `docs/demo.md`: the demo is physical animation, not a balance controller; limp joints are ball-and-socket; recovery is a reset; single character, no crowd figures.
