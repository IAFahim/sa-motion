# Handoff R2b: balance tick repair

## Baseline (origin/integrate/r2, dcab749)

`StandTenSeconds`: `stand pelvis start 1.0000 end 0.3289 maxDrift 0.6734 maxTilt 42.15 supported 175/600 decision Falling firstFall 175`.
Push/latch/rerun/hostile unmeasured behind the stand failure.

## Experiment log (one line each: change + stand before -> after)

- E00 baseline, no change: firstFall 175, end 0.3289, maxTilt 42.15 (red).
- E01 WriteLegDrives knee +e -> -e (matches FixKnee, contract.md): firstFall 175 -> 175, end 0.3289 -> 0.3289 (no-op as predicted, FixKnee overwrote it).
- E02 knee A/B (FixKnee +error, WriteLegDrives -error): firstFall 175 -> 55, end 0.3225, maxTilt 39.82. -error wins; matches HarnessWorld RotX90 foot-backward analysis.
- E03 delete FixKnee (WriteLegDrives now identical): firstFall 175 -> 175. Pure no-op confirmed; knee sign settled at actual-desired.
- E04 absolute hip lean actuals via SplitRotation in BuildLeg/WriteLegDrives: firstFall 175 -> 11, end 0.7875, maxTilt 32.13. REVERTED: procedural frames follow segments, so absolute SplitRotation carries the constant body-mount offset and folds the legs; deflection needs a seed reference (tick state, next).
- E05 seed-relative hip anchor (mount-anchored thigh targets, planted legs, reseed on touchdown): firstFall 175 -> 71, phantom step at t20 never lands. REVERTED; trace shows the fall mechanism is geometric (swing foot cannot reach ground), so anchor is confounded. Revisit after fix 4 if still sinking.
- E06a joint-based leg frames (hip/knee/ankle joints from segment midpoints, shin knee->ankle 0.43 like source leg_ik): firstFall 175 -> 153, end 0.1102, maxTilt 68.94. KEPT as proven bug fix (old shin 0.265 could never span hip height); stance now reaches ground.
- E06b ankle-clearance seed (0.10 from bind) + probe-lift removal, sole-space goals: firstFall 153 -> 38, end 0.4245. REGRESSED via 0.95 crouch transient (see E06c); clearance + lift removal KEPT.
- E06c PelvisHeight Standard 0.95 -> 1.0 (measured bind straight-stand): firstFall 38 -> 62, maxTilt 19.68.
- E07 source free gate restored in Decide + SupportInsideMargin -0.18 -> 0.0: firstFall 62 -> 62 (gate dormant until E09 quad fix; R1 suite green).
- E08 planted-ankle world-flat anchor (FlatAnchor, same pattern as pelvis/torso): firstFall 62 -> 130, maxTilt 30.06. KEPT.
- E09 foot/root forward-axis batch (Support horizontalized quad, Update toe/sole/rootForward + SolveAnkle forward to length axis Y): firstFall 130 -> 80. Support/toe/sole/rootForward KEPT (R1 green, gate now functional: no steps until tilt 5 deg); PelvisControl flatForward/toeSum tried then reverted as neutral (E09bis: 80 -> 81).
- E10 seed-relative hip anchor retry under fixed geometry: firstFall 80 -> 74. REVERTED (neutral-negative); hips stay limp-follow.

## Done

- `docs/contract.md` (new, 19 lines): `JointDrive.Target` world-orientation
  convention with worked knee example; R6/R7 dissenters noted for R9.
- Knee sign settled by A/B (+e falls at 55, -e at 175) and dissenter fixed;
  `FixKnee` removed as dead weight.
- Joint-based leg frames, sole/ankle convention with seeded clearance,
  probe lift removed, pelvis height 1.0, free gate + margin 0.0,
  planted-ankle flat anchor, forward-axis batch (Support horizontalized,
  Update toe/sole/rootForward, SolveAnkle forward).
- `python3 eng/check_source_rules.py`: `source rules: ok`. R1 `Balance/*`
  suites green (47 pass); only the two R2 scenario checks fail.

## Not done (packet checks NOT met)

| Check | Target | Measured |
|---|---|---|
| 1 stand 10 s | pelvis <= 0.12 m, torso <= 15 deg | FAIL: start 1.0000 end 0.4573 maxDrift 0.5023 maxTilt 18.59 supported 81/600, Falling from tick 81 |
| 2 push 8x60 N s | no fall 5 s, step > 0.1 m push side | FAIL: dir 0 falls (Falling); step 1.029 m / pushSide 1.027 m met, no-fall not |
| 3 push 400 N s | `Falling` latched | PASS (`HugePush_LatchesFalling` green) |
| 4 rerun identical | bit-identical drives | PASS (`Rerun_BitIdentical` green) |
| 5 hostile | passive + finite | PASS (`Hostile_PassiveAndFinite` green; ShortSpans/Countdown/Ownership/Taper/WideStep green) |

Exact failing command: `eng/dotnet_test.sh
tests/SA.ReactionControl.Tests/SA.ReactionControl.Tests.csproj --filter
FullyQualifiedName~BalanceTickTests.StandTenSeconds` then `.PushEightDirections`.

## Limiting-factor diagnosis

Single-support stance sinks: pelvis drifts down, IK commands growing knee
bend to fit the shrinking span, COM leaves support, abort latches. Leg hip
drives are limp followers (frame-following composition); seeded absolute
thigh anchors scored 80 -> 74 (reverted). Swing steps lift but never land
within 55+ ticks (touchdown vs lift shaping), so recovery steps cannot
catch the sink. Fore-aft pelvis servo direction is unvalidated (neutral in
bisect). Next, in order: (a) make steps land (Update lift/landing
hysteresis), (b) stance stiffness and gains (fix 7: hip/ankle strengths,
height-servo rate), (c) fore-aft sign review against pushes.

## Requests (R1 folder changed, all measured above)

- `BalanceSnapshot.BuildLeg`: joint-based frames/lengths (was center-based).
- `BalanceSolve.WriteLegDrives` + `SolveLeg`: knee sign fix, ankle-space
  goals via `AnkleClearance`, planted-ankle `FlatAnchor`.
- `FootPlacement.Decide`: source free gate; `Support`: horizontalized quad;
  forward axes to +Y (toe/sole/rootForward/swing-forward).
- `BalanceTypes`: `AnkleClearance` (new, Standard 0.0), `PelvisHeight`
  1.0, `SupportInsideMargin` 0.0. `PelvisControl` untouched (bisect neutral).
- No contract change needed. `ProbePointLift` tick const removed (redundant).
