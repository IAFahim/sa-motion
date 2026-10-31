# Work packets

Each packet is sized for one small agent. Read `AGENTS.md` first. A packet owns the paths listed and must not edit any other path. Shared files (`SA.Motion.sln`, `docs/verification.md`, `docs/provenance.md`, `plan.md` in `com.sa.unity`) belong to the integrator.

Why this order: a .NET test run takes seconds and needs no Editor, so any number of agents can run it at once. A Unity compile took 5 minutes 7 seconds here and one Editor serves everyone. Wave 1 is therefore pure .NET. Wave 2 uses Unity and runs one packet at a time.

Baseline (measured 2026-10-31): .NET 38 tests pass, Unity bridge 3 pass, SA package 35 pass. `PoseTransition.Apply` costs 43-47 ns at unit scale and 76-81 ns otherwise under Burst (`docs/verification.md`).

## Wave 1: pure .NET, run in parallel (done 2026-10-31: all nine merged; 134 + 30 tests pass)

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
| U1 (done on branch `u1-inertialization` of com.sa.unity, not merged to main; baker path unverified) | SA integration of inertialization (plan P3): `Transitions.cs`, opt-in `Playback` overloads, fixed-clock exclusion, CPU correction order. Existing APIs unchanged. | N1, N6 | SA suite 35/35 plus new tests. `CrossFade` regression green. Interrupted and repeated requests tested. |
| U2 (done, see `docs/gate-m.md`; items 6 and 7 open) | Reactions package skeleton and the two-body Gate M fixture (plan P1): real limit joint, world refit, observation, rendered response. | N2, N3 | Plan section 8.3 passes with numeric traces and a captured frame. |
| U3 | Humanoid baker (plan P2): `RagdollBaker` consuming N4 and N5, unparented proxies, shared blobs. | N4, N5, U2 | One Avatar humanoid and one generic humanoid bake. Subscene reload and two instances work. |
| U4 | GPU inertialization (plan P4): mirror the specified math in HLSL. | U1 | Real GPU parity against the C# kernel on the same inputs. |

## Wave R: controller port and harness (Gate L decided, see `docs/provenance.md`)

Pure .NET, start all at once. Shared rules: `docs/packets/R-common.md`. The shared contract (`RoleBody`, `SupportProbe`, `JointDrive`) is fixed by the integrator.

| ID | Task | Source (Delirium @ 8e4ad4e) | Owns |
|---|---|---|---|
| R1 | Balance core: support, foot placement, pelvis control, balance solve | `nmdbm.rs` 1-843 | `Balance/` |
| R3 | Loss of balance and catch-fall decisions | `nmbb.rs` | `BodyBalance/` |
| R4 | Catch fall: probes and arm response | `nmcf.rs` | `CatchFall/` |
| R5 | Bracing against walls | `nmbr.rs` | `Brace/` |
| R6 | Effectors, soft limits, drive conversion | `nmrs.rs` 396-721, `ragdoll.rs` 1589-1720 | `Drive/` |
| R7 | Behavior coordination, locomotion drive | `nm.rs` 490-560 and 1295-1450, `locomotion_drive.rs` | `Coordination/` |
| R8 | Get-up selection and root alignment | `getup.rs`, `ragdoll.rs` ~3076 | `GetUp/` |
| H1 | Pure .NET articulated test harness (test support only) | none | `tests/SA.Harness*` |

| N11 | Ragdoll layout from a mapped skeleton (unblocks U3 humanoid baker) | none (MIT) | `SA.Humanoid/Build/` |

Second wave, after R1 and H1 merge: R2 (`nmdbm.rs` 843-1474, body adapter and tick), R9 (assemble all areas into one controller tick and tune it in H1), R10 (independent parameter profile and tuning log).

## Wave 3: still blocked

| ID | Task | Blocked by |
|---|---|---|
| X2 | Recovery animation content | Project-owned or licensed get-up clips. R8 returns "no suitable clip" until then. |
| X3 | Public IL2CPP or other platform claims | A player build on that platform. |
| X4 | Any release or distribution of `SA.ReactionControl` | Real legal advice; see `docs/provenance.md`. |
