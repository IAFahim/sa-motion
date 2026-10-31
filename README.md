# SA.Motion

SA.Motion is an engine-independent .NET library of finite-duration inertialization kernels plus a thin Unity bridge. The kernels (`ScalarDecay`, `VectorOffset`, `RotationOffset`, `PoseTransition` over `PoseHistory`) capture an offset from corrected output history and decay it to zero over a caller-chosen duration, so an interrupted motion restarts from the corrected pose and lands on the destination with zero end value, velocity and acceleration. Callers own state, timestamps and buffers; the kernels allocate no managed memory, touch no engine and return a default instead of throwing on bad input. Unity consumes the exact packed DLL through conversions and proof tests.

## Where code goes

| If the code... | It lives in |
|---|---|
| compiles without Unity | `src/` (netstandard2.1, C# 9) |
| must touch Entities, Physics, Burst attributes or rendering | `packages/` |
| exchanges data with a physics engine | `src/` as plain structs: snapshot in, intent out. Never an interface, never a callback |

## Use

```csharp
var source = Pose.Identity;
source.Position = new Float3(2, 0, 0);
var history = new PoseHistory();
history.Reset(source);
var transition = PoseTransition.Create(history, Pose.Identity, 0.5f);
var start = PoseTransition.Apply(ref transition, Pose.Identity, 0f);
var end = PoseTransition.Apply(ref transition, Pose.Identity, 1f);
```

`start.Position` is the corrected source `(2, 0, 0)`; `end` is the destination pose. Needs `SA.Motion.Inertialization` and `SA.Motion.Numerics`. The same code runs asserted in `tests/SA.Motion.Tests/ReadmeSampleTests.cs`.

## Build and test

```bash
eng/dotnet_test.sh
python3 eng/check_source_rules.py
```

Measured 2026-10-31 on the merged main: `eng/dotnet_test.sh` gives 134 passed, 0 failed, 0 skipped; `eng/dotnet_test.sh tests/SA.Humanoid.Tests/SA.Humanoid.Tests.csproj` gives 30 passed; `python3 eng/check_source_rules.py` prints `source rules: ok`.

## Not built yet

Planned work (reaction controller, Unity integration, GPU path) is listed in `docs/work-packets.md`. What exists is evidenced in `docs/verification.md`.

Licence: MIT, see `LICENSE`.
