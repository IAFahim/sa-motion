# Work packets

Each packet is sized for one small agent. Read `AGENTS.md` first. A packet owns the paths listed and must not edit any other path. Shared files (`SA.Motion.sln`, `docs/verification.md`, `docs/provenance.md`, `plan.md` in `com.sa.unity`) belong to the integrator.

Why this order: a .NET test run takes seconds and needs no Editor, so any number of agents can run it at once. A Unity compile took 5 minutes 7 seconds here and one Editor serves everyone. Wave 1 is therefore pure .NET. Wave 2 uses Unity and runs one packet at a time.

Baseline (measured 2026-10-31): .NET 38 tests pass, Unity bridge 3 pass, SA package 35 pass. `PoseTransition.Apply` costs 43-47 ns at unit scale and 76-81 ns otherwise under Burst (`docs/verification.md`).

## Wave 1: pure .NET, run in parallel

Each packet has its own spec in `docs/packets/<ID>.md`. The spec is the whole task. `src/SA.Humanoid` and its test project already exist with the 15-role contract (`Roles.cs`), so N4 and N5 only add files.

| ID | Task | Owns |
|---|---|---|
| N1 | Make a finished transition cheap | `Inertialization/PoseTransition.cs` |
| N2 | Frames and inertia conversions | `Mechanics/Frame*.cs`, `Float3x3.cs`, `Inertia.cs` |
| N3 | Bounded quaternion drive | `Mechanics/DriveGains.cs`, `QuaternionDrive.cs` |
| N4 | Humanoid role mapping | `SA.Humanoid/Mapping/*` |
| N5 | Independent humanoid profile | `SA.Humanoid/Profile/*`, `docs/profile.md` |
| N6 | Idempotent transition request | `Inertialization/TransitionRequest.cs` |
| N7 | Contract review of section 10.2 | `tests/.../ContractTests.cs` only |
| N8 | README and architecture overview | `README.md`, `docs/architecture.md` |
| N9 | Totality fuzz of existing kernels | `tests/.../TotalityTests.cs` only |

## Wave 2: Unity, one packet at a time under the lock

| ID | Task | Needs | Done when |
|---|---|---|---|
| U1 | SA integration of inertialization (plan P3): `Transitions.cs`, opt-in `Playback` overloads, fixed-clock exclusion, CPU correction order. Existing APIs unchanged. | N1, N6 | SA suite 35/35 plus new tests. `CrossFade` regression green. Interrupted and repeated requests tested. |
| U2 | Reactions package skeleton and the two-body Gate M fixture (plan P1): real limit joint, world refit, observation, rendered response. | N2, N3 | Plan section 8.3 passes with numeric traces and a captured frame. |
| U3 | Humanoid baker (plan P2): `RagdollBaker` consuming N4 and N5, unparented proxies, shared blobs. | N4, N5, U2 | One Avatar humanoid and one generic humanoid bake. Subscene reload and two instances work. |
| U4 | GPU inertialization (plan P4): mirror the specified math in HLSL. | U1 | Real GPU parity against the C# kernel on the same inputs. |

## Wave 3: blocked

| ID | Task | Blocked by |
|---|---|---|
| X1 | `SA.ReactionControl`: balance, steps, bracing, recovery ported from Delirium | Gate L decision in `docs/provenance.md`. A human decision. Include which functions, which license and who approved it. |
| X2 | Recovery animation content | Project-owned or licensed get-up clips. |
| X3 | Public IL2CPP or other platform claims | A player build on that platform. |

## Decision pending for the owner

Extract the existing SA pose algebra (`PoseAlgebra`, `Sampling`, `Evaluation`, `BlendSpace`, `Playback`, about 500 lines) into a portable library too, so the whole animation core moves between engines. These files mostly operate on spans but some take Entities blob and buffer types, so this is a small rewrite, not a copy. Not scheduled until the owner decides; the packets above do not depend on it.
