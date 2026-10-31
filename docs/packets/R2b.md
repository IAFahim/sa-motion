# R2b: Repair the balance tick until the stand check passes

Read `AGENTS.md`, `docs/packets/R-common.md`, `docs/packets/R2.md` (the original checks, unchanged), `docs/port/R1.md`, `docs/port/R2.md`, `docs/handoff/R1.md` and `docs/handoff/R2.md`, then this file. They are your whole task.

**Start from the integration branch, not main:**

```bash
cd /home/i/GitHub/sa-motion && git fetch origin && git worktree add ../sa-motion-wt/R2b -b packet/R2b origin/integrate/r2 && cd ../sa-motion-wt/R2b
```

`integrate/r2` is current `main` plus the R2 packet. Main is green; this branch is red on purpose: `BalanceTickTests.StandTenSeconds_HoldsHeightAndUpright` and `PushEightDirections_NoFallAndSteps` fail.

## Owns

`src/SA.ReactionControl/BalanceTick/*`, **and now also** `src/SA.ReactionControl/Balance/*` (R1's folder, because R2's four change requests are against it), `tests/SA.ReactionControl.Tests/BalanceTick/*` and `.../Balance/*`, `docs/port/R1.md` and `docs/port/R2.md` (update them when you change behavior), `docs/contract.md` (new), `docs/handoff/R2b.md`, and appended rows in `docs/tuning.md` under `Tuning log`. Edit nothing else. Do not touch `tests/SA.Harness*`; the harness is the fixed referee.

## The problem

At 60 Hz the controller stands for about 175 ticks (2.9 s) and then falls: pelvis 1.00 to 0.33 m, 42 degrees of tilt. Target: 10 s, pelvis within 0.12 m, torso within 15 degrees.

## Do, in this order, one change at a time

For every experiment write one line in `docs/handoff/R2b.md`: what you changed, the stand numbers before and after (first fall tick, end pelvis height, max tilt). Keep a change only if the numbers improve or it fixes a proven bug.

1. **Settle what `JointDrive.Target` means.** `tests/SA.Harness/HarnessWorld.cs` (`IntegrateForces`) compares `drive.Target` to the child's **world** rotation and takes the relative angular velocity against the parent. The Unity side currently uses a target relative to the parent. Decide which convention the controllers must emit, check that R1's `BalanceSolve`, R2's tick, `Drive.ToJointDrive` (R6) and R7's `BlendDrive` all agree, and write the rule in `docs/contract.md` (under 40 lines, with one worked example). Fix any controller that disagrees. Do not edit `Contracts/*`.
2. **Knee direction.** R2's handoff suspects R1's knee `RotX(desired - actual)` pushes the wrong way (bend is negative when bent). Run the A/B the handoff describes: read the knee hinge branch of `HarnessWorld.cs` (around lines 314 to 345) and decide with numbers, not by reading.
3. **Hip lean actuals.** `BuildLeg` measures hip twist but leaves lean actuals at zero, so the correction assumes zero lean. Measure lean1 and lean2 with `SplitRotation`, as `HipTwist` does.
4. **Foot origin convention.** Foot targets sit at probe-point height (the sole) while leg frames use body centers. Pick one convention, state it in `docs/port/R1.md`, and make the code consistent. Remove the local +0.05 lift in R2 if the convention makes it unnecessary.
5. **Step gate.** R1's `Decide` dropped the source `free` (inside-support) gate, so any float asymmetry starts a step, and R2 compensates with a 0.09 m deadband. Re-examine the gate against the hysteresis tests; keep the deadband only if the gate cannot be restored.
6. **Stance thigh anchor.** If the stance still sinks, freeze the stance-thigh orientation as an absolute reference, like the torso anchor, instead of tracking the IK lean.
7. Anything else the numbers point to. Examples: gain schedule (`docs/harness.md`), ankle strategy, height servo gain.

## Stop rule

After **25 logged experiments**, or when the checks pass, stop. If the stand check still fails after 25, write the best configuration, the numbers, and your diagnosis of what limits it (harness torque limits? a structural mismatch between the port and the harness?). Do not loosen a threshold, edit the harness, or hide a failing test.

## Checks (same as R2; record the table in the handoff)

1. Stand 10 s: pelvis within 0.12 m, torso within 15 degrees.
2. 60 N·s at the torso in eight horizontal directions: no fall after 5 s, and for the largest pushes at least one foot steps more than 0.1 m on the push side.
3. 400 N·s: the report says `Falling` and stays so.
4. Two identical runs give bit-identical drives.
5. NaN, infinity and short spans give passive drives and a finite report.

Run `eng/dotnet_test.sh tests/SA.ReactionControl.Tests/SA.ReactionControl.Tests.csproj` and `python3 eng/check_source_rules.py` before each commit. Push `packet/R2b`, never main.
