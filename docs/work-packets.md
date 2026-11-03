# Work packets

## Shared animation foundation

CF1 (`packet/CF1-shared-animation-foundation`) implements the portable shared-family kernels from `/home/i/ruk2/plan.md`. Its spec is `docs/packets/CF1.md`; the actual API/lifetime contract is `docs/shared-family-contract.md`; measured receipts and next integration work are in `docs/handoff/CF1.md`. Keep the existing Unity/legacy path intact and continue from this branch when adding the canonical baker and timeline adapter.

CF2 (`packet/CF2-runtime-shape-addon`, base b7fdaec including the later C08 family publication) adds the optional portable SA.Shape package. Read `docs/packets/CF2.md`, the implemented contract in `docs/shape.md`, and `docs/handoff/CF2.md`. It owns no Unity adapter or scene. SH1-SH4 in the contract are the bounded follow-up tasks; use the current integration state before selecting one, since other C-series packets may already be complete. The core's only new seams are geometry overlays and staged timeline commit; SA.Animation never references SA.Shape.

SH5 completes the SH1-SH4 shader/test/view recovery on `main` in sa-motion, com.sa.unity and sa-demo. Read `docs/packets/SH5.md`, `docs/handoff/SH5.md` and the `sa-unity-cli` companion skill before continuing Unity integration. The measured renderer result and the unverified pilot/physics gates are separate; preserve existing Humanoid/Generic authoring and ECS playback while extending the shared-family and shape paths.

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
| U3 (demo-level done: `RagdollBaker`, see `docs/demo.md`; Gate B not claimed) | Humanoid baker (plan P2): `RagdollBaker` consuming N4 and N5, unparented proxies, shared blobs. | N4, N5, U2 | One Avatar humanoid and one generic humanoid bake. Subscene reload and two instances work. |
| U4 | GPU inertialization (plan P4): mirror the specified math in HLSL. | U1 | Real GPU parity against the C# kernel on the same inputs. |
| U5 (done on `packet/U5-simple-setup` in both repos; see `docs/handoff/U5.md`) | Automatic character/animation-pack setup, multi-material skins, in-place, gait-phase retention, optional IK/foot locking and hit flinches. | U1, U3 | Full verification 22/22, Kyle demo 8/8, live UAL and cross-pack Humanoid sampling. |

## Wave R: controller port and harness (Gate L decided, see `docs/provenance.md`)

Status 2026-10-31: R1, R3 to R8, H1 and N11 are merged and tested (SA.ReactionControl 79 tests, harness 8, humanoid 57). They are **not yet connected** to Unity; the demo uses physical animation instead. Next: R2 (tick and carried fields), R9 (assemble), R10 (profile), then a GPL adapter package that feeds the controller from `RoleBody` snapshots and applies its `JointDrive` output.

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

## Wave R2: next agents

Status: R10, H2, N12, N13, T1 and D1 are merged into main (green). R2 passed none of its stand checks; it lives on `origin/integrate/r2` until R2b makes it green.

| ID | Task | Needs | Owns |
|---|---|---|---|
| R2 | Balance controller tick on top of R1, measured in the harness | R1, H1 (merged) | `BalanceTick/` |
| R10 | Independent profile, validation and tuning log | R1, R3 to R8 (merged) | `Profile/`, `docs/tuning.md` |
| H2 | Harness push scenarios and outcome metrics | H1 (merged) | `tests/SA.Harness/Scenarios/` |
| N12 | Hinge axis and range inference | none (MIT) | `SA.Humanoid/Hinge/` |
| N13 | Soft joint limits as a corrective drive | none (MIT) | `SA.Motion/Mechanics/SoftLimits.cs` |
| T1 | Totality fuzz of every controller entry point | R1, R3 to R8 (merged) | `tests/.../Totality/` |
| D1 | Documentation refresh | none | `README.md`, `docs/index.md`, `docs/architecture.md` |
| R2b | Repair the balance tick until the stand check passes (R2 is on `integrate/r2`, red on purpose) | R2 on `integrate/r2` | `BalanceTick/`, `Balance/`, `docs/contract.md` |
| R2c | Continue the balance tick repair from `packet/R2b` (stop after E35) | R2b branch | `BalanceTick/`, `Balance/` |
| B1 to B9 | The balance race: nine independent MIT controllers scored by `tests/SA.Balance.Evaluation` (leaderboard in `docs/leaderboard.md`) | scoreboard on main | `src/SA.Balance.<Name>/` |
| R9 | Assemble one reaction controller and tune it | R2b green, R10, H2 merged | `Controller/` |

## Balance wave 2 (on top of the winning B1, scoreboard on main)

B1 won the race (`capture-point`, 80 of 100, merged). R2, R2b and R2c (the port-based tick) are closed red and stay on their branches; R9 is retired. Second wave: C1 (auto-tune), C2 (larger pushes), C3 (arm and torso momentum). Leaderboard: `docs/leaderboard.md`.

## Wave 3: still blocked

| ID | Task | Blocked by |
|---|---|---|
| X2 | Recovery animation content | Project-owned or licensed get-up clips. R8 returns "no suitable clip" until then. |
| X3 | Public IL2CPP or other platform claims | A player build on that platform. |
| X4 | Any release or distribution of `SA.ReactionControl` | Real legal advice; see `docs/provenance.md`. |
