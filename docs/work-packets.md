# Work packets

Each packet is sized for one small agent. Read `AGENTS.md` first. A packet owns the paths listed and must not edit any other path. Shared files (`SA.Motion.sln`, `docs/verification.md`, `docs/provenance.md`, `plan.md` in `com.sa.unity`) belong to the integrator.

Why this order: a .NET test run takes seconds and needs no Editor, so any number of agents can run it at once. A Unity compile took 5 minutes 7 seconds here and one Editor serves everyone. Wave 1 is therefore pure .NET. Wave 2 uses Unity and runs one packet at a time.

Baseline (measured 2026-10-31): .NET 38 tests pass, Unity bridge 3 pass, SA package 35 pass. `PoseTransition.Apply` costs 43-47 ns at unit scale and 76-81 ns otherwise under Burst (`docs/verification.md`).

## Wave 1: pure .NET, run in parallel

New test projects: create `tests/<Name>/<Name>.csproj`, restore once without a lock file, commit the generated `packages.lock.json`, and run with `eng/dotnet_test.sh tests/<Name>/<Name>.csproj`. The integrator adds new projects to the solution.

| ID | Task | Owns | Done when |
|---|---|---|---|
| N1 | Make a finished transition cheap. Today `Apply` still evaluates the rotation sin and cos after `elapsed >= Duration`, which is most of the 43-47 ns at unit scale. Return the destination exactly, with no sin or cos, for any channel whose correction is cleared or finished. | `src/SA.Motion/Inertialization/PoseTransition.cs`, `tests/SA.Motion.Tests/TransitionEarlyOutTests.cs` | All 38 existing tests pass unchanged. New tests show output equal to the old path within 1e-6 for finished and cleared channels. The integrator reruns the cost probe and records the new ns. |
| N2 | Frame and inertia conversions: local joint frame to world, world inertia from principal axes and a frame, round trip, mirrored frames. | `src/SA.Motion/Mechanics/Frames.cs`, `tests/SA.Motion.Tests/FramesTests.cs` | Round trips on random non-identity frames agree within 1e-5. Degenerate frames return a defined default. Plan sections 6 and 8.2 are covered. |
| N3 | Bounded quaternion drive: torque from current and target rotation, angular velocities, stiffness, damping and a torque cap. Add a test-only single-body rotational integrator. | `src/SA.Motion/Mechanics/QuaternionDrive.cs`, `tests/SA.Motion.Tests/QuaternionDriveTests.cs` | `q` and `-q` targets give the same torque. Zero error gives zero torque. The cap holds. With critically damped gains the integrator reaches the target with no overshoot beyond 1% of the initial angle. Derived from standard mechanics; record the derivation in your handoff, not as a code comment. |
| N4 | Humanoid role mapping: from a flat list of (name, parent index, rest pose) and an optional explicit map, produce the 15 roles from plan section 5.1 or a list of diagnostics. Never guess from names alone. | `src/SA.Humanoid/*`, `tests/SA.Humanoid.Tests/*` | A Kyle-shaped skeleton and a differently oriented generic skeleton both map. Missing leg, cyclic parent and duplicate ownership each produce a named diagnostic. |
| N5 | Independent humanoid profile: typed read-only structs for role mass fractions and per-joint limits, with a total-mass check. Cite published anthropometric tables in the handoff. No GTA-derived value. | `src/SA.Humanoid/Profile/*`, `tests/SA.Humanoid.Tests/ProfileTests.cs` | Masses are positive and sum to the declared total. Every limit has min below max. Every number has a cited source or is marked as an authored starting value. |
| N6 | Idempotent transition request logic: given the previous request key and revision and a new desired key, decide keep, restart or misuse. Pure. Matches plan section 10.3. | `src/SA.Motion/Inertialization/TransitionRequest.cs`, `tests/SA.Motion.Tests/TransitionRequestTests.cs` | Same key repeated keeps the revision. New key increments once. Reused key with different membership returns misuse, never stale playback. |
| N7 | Review, do not write features: list every clause of plan section 10.2 and mark which existing test covers it. Add tests for the uncovered clauses only. | `tests/SA.Motion.Tests/ContractTests.cs`, `docs/handoff/N7.md` | Every clause maps to a named test or is listed as an open gap. Failures found are reported, not silently fixed. |

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
