# Handoff R2c: balance repair, stopped by integrator (B1 passed)

B1 passed stand + pushes on origin/packet/B1. Per integrator notice, R2c work stops here.
Best config + numbers below, then diagnosis for R9.

## Best config (this branch head, stand firstFall 81 -> 129)

- R2b `AnkleClearance` sole->ankle goal offset REMOVED from `SolveLeg`
  (E25: with the +0.10 m offset 129 -> 80; without it 80 -> 129). Why: the
  offset commands ~25 deg stance crouch the 900-strength knee servo cannot
  hold (0.29 rad static error); the legs buckle fast. Without it the legs run
  straight and sink slowly instead. Neither is correct; the right fix is
  correct-height goals with a shallow crouch the servos can hold. R9 must
  re-tune crouch depth vs knee authority; the `AnkleClearance` field and tick
  seed stay dormant meanwhile.
- `BalanceParams.SwingStiffness` (new, Standard 0.25): swing-leg hip/knee/ankle
  strengths scale, damping kept full. Fixes the swing-foot rocket (foot reached
  0.63 m chasing a 0.15 m target at 1.0; tracks at ~0.09 m at 0.25).
- `FootPlacement.Update` touchdown + descend-commit: land when the stepping foot
  has contact, stance foot has contact, and spanFraction < 0.5; below 0.5 the
  lift target blends to ground height instead of hovering. Fixture-safe (static
  fixtures never move the foot, spanFraction stays huge; R1 suite green).
- Tick flight limit (`BalanceController.FlightLimit` = 25, `FlightLeft` state):
  a step that never touches down is force-planted at the swing foot's ground
  projection with contacts restored. R1 `Update` untouched by the limit.
- Tick stance ankle servo 700/3 -> 3000/5.3 (damping matched to coupled
  transverse inertia per harness.md). First real mover: 88 -> 97.
- Tick height servo references probe ground height, not the diving targets
  (no behavior change: Update overwrites targets each tick, so both refs agreed;
  kept as the correct reference).
1400 (87, neutral), hip 2000 (masks ankle gain: 97 on hip-2000, 117 on revert;
kept 1000), spine 400 (83, whiplash), hip 700 (61), pelvis damping 60 (neutral),
pelvis Passive (78, the anchor helps).

## Measured (branch head, deterministic)

| Check | Target | Measured |
|---|---|---|
| stand 10 s | pelvis <= 0.12 m, torso <= 15 deg | FAIL: start 1.0000 end 0.4555 maxDrift 0.5030 maxTilt 27.18 supported 129/600, Falling from tick 129 |
| push 8x60 N s | no fall 5 s, step > 0.1 m push side | FAIL: dir 0 falls (Falling); best 0.745 m / pushSide 0.391 m |
| push 400 N s | `Falling` latched | PASS (`HugePush_LatchesFalling`) |
| rerun identical | bit-identical | PASS (`Rerun_BitIdentical`) |
| hostile | passive + finite | PASS (`Hostile_PassiveAndFinite`, ShortSpans/Countdown/Ownership/Taper/WideStep) |
| R1 Balance/* | green | PASS (47 tests) |
| Profile | green | 10/11: `HalfHeightHalvesLengths` FAILS pre-existing (expects PelvisHeight 0.475 = 0.95-era; Standard is 1.0 since R2b, actual 0.5). R10-owned, not touched. |

Failing commands:
`eng/dotnet_test.sh tests/SA.ReactionControl.Tests/SA.ReactionControl.Tests.csproj --filter FullyQualifiedName~BalanceTickTests.StandTenSeconds_HoldsHeightAndUpright`
`... --filter FullyQualifiedName~BalanceTickTests.PushEightDirections_NoFallAndSteps` (dir 0).

## Experiment log (one line each: change + stand before -> after)

- E11 touchdown (contact + contact + spanFraction<0.5) + descend-commit (<0.5):
  81 -> 81 (never fires: swing foot rockets, contact unreachable).
- E12 tick flight limit 25 + ForceLand: 81 -> 81 (force-land at t79, body spent).
- E13 `SwingStiffness` 1.0 -> 0.25: 81 -> 86 (rocket 0.63 m -> track 0.09 m).
- E14 PelvisHeight 1.0 -> 0.97: 86 -> 85 neutral; reverted (also breaks R10 HalfHeight).
- E15 height-servo lowY -> probe ground: 85 -> 85 identical (Update overwrites
  targets each tick); kept as correct reference.
- E16 right-knee correction mirrored: 85 -> 66; reverted (both-+X is correct).
- E17 stepping-foot planted=contact (liftoff hysteresis): 85 -> 93 BUT breaks
  `Mirror_MirrorsEveryOutput` (drive-target mismatch ~1e-4..4e-4); reverted.
- E18 ankle 700 -> 1400: 86 -> 87 neutral; reverted.
- E19 pelvis drive -> Passive: 86 -> 78; reverted (anchor helps).
- E20 hip 1000 -> 2000: 86 -> 88, tilt worse; kept briefly, then reverted
  (masks E22: ankle-3000 gives 97 on hip-2000 but 117 on hip-1000).
- E21 pelvis damping 30 -> 60 (record value): neutral; reverted to 30.
- E22 ankle 700/3 -> 3000/5.3: 88 -> 97. KEPT.
- E23 spine 200/5 -> 400/6.5: 117 -> 83; reverted.
- E24 hip 1000/10 -> 700/8: 117 -> 61; reverted (1000 is the sweet spot).
 - Final revert of E14 under E22: 117 -> 129.
 - E25 sole->ankle offset (+0.10 m) restored in `SolveLeg`: 129 -> 80;
  re-removed: 80 -> 129. NOTE: the offset was dropped accidentally by the
  E13 edit, so E13-E24 numbers above are all no-offset numbers; the R2b
  baseline 81 is with-offset. R9 must re-tune crouch vs knee authority.

Gains sweep (LegStiffness 1/1.5/2/3, all legs): 85/81/84/82 — authority is not
the lever. Passive-vs-control: uncontrolled pelvis drops below 0.5 at t149;
control was faster only because Holding aborts at COM offset while falling.

## Diagnosis for R9

1. Pre-step sink (t0-50, double support, frozen XZ, knees ~straight, feet flat):
   pelvis -3 cm with no step. Ankle roll under topple load is the visible mode;
   E22 (4x ankle) halves the rate but does not stop it. Lateral drift grows
   (dR 0.06 -> 0.28) while HipShift saturates; zeroing the whole pelvis servo
   changes 88 -> 89, so the servo is ineffective, not poisonous.
2. Spurious step fires at drift ~0.25 m; single-support stance then collapses
   ~6x faster than double support (one ankle carries full topple gradient).
3. Steps can now land mechanically (touchdown + swing tracking + flight limit)
   but land too late: the body is spent by touchdown. Earlier gate and/or
   stronger single-support ankle (caps are shared 500; schedule says half
   stiffness) are the next levers, not gains.
4. E17 exposed a genuine stance-path L/R asymmetry: with both feet planted,
   mirrored inputs give hip lean2 -0.0007 vs +0.0148 and torso z 0.030 vs
   -0.037 (should mirror to 1e-4). Suspect cross-product handedness in the
   stance IK chain interacting with `side`. The swing path masks it. R9 should
   bisect `SolveLeg` stance branch vs `Mirror_MirrorsEveryOutput` before
   re-attempting liftoff hysteresis.

## Requests

- R1 `Balance/*` changed (all measured above): `SwingStiffness` field +
  swing scaling in `SolveLeg`/`SolveAnkle`/`WriteLegDrives`; touchdown/commit
  in `Update`; height-servo ground reference.
- `docs/tuning.md`: added `AnkleClearance` (R2b gap) and `SwingStiffness` rows
  to the table (Profile test requires every field present); appended R2c log rows.
- No contract change. `docs/port/R2.md` amended (tick consts, flight, landing).

## Reporting

Changed files: `src/SA.ReactionControl/Balance/BalanceTypes.cs`,
`FootPlacement.cs`, `BalanceSolve.cs`, `src/SA.ReactionControl/BalanceTick/
BalanceController.cs`, `BalanceTickTypes.cs`, `docs/tuning.md`,
`docs/port/R2.md`, `docs/handoff/R2c.md` (this file).
Ran: `python3 eng/check_source_rules.py` = `source rules: ok`;
full `SA.ReactionControl.Tests`: 116 pass + 4 fail (stand, push-dir-0,
pre-existing R10 HalfHeight; plus HugePush/rerun/hostile/R1/Profile-10 green).
Did not verify: Unity, harness package, B1's approach. Numbers from runs only.
