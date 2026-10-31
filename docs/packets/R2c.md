# R2c: Continue the balance tick repair

Read `AGENTS.md`, `docs/packets/R-common.md`, `docs/packets/R2b.md` (same rules, same checks, same owned paths), `docs/contract.md`, `docs/handoff/R2b.md` (especially "Limiting-factor diagnosis" and the experiment log E01 to E10), then this file. They are your whole task.

**Start from R2b's branch:**

```bash
cd /home/i/GitHub/sa-motion && git fetch origin && git worktree add ../sa-motion-wt/R2c -b packet/R2c origin/packet/R2b && cd ../sa-motion-wt/R2c
```

Main is green; this branch is red on purpose (`StandTenSeconds` and `PushEightDirections` fail). Your handoff goes in `docs/handoff/R2c.md` and your experiments continue the numbering at E11.

## State

Stand: first fall at tick 81, pelvis 1.00 to 0.46, tilt 18.6 degrees. Checks 3 to 5 already pass. R2b's diagnosis: single-support stance sinks, hip drives are limp followers, swing steps lift but never land within 55 ticks.

## Do, in this order

1. **Make steps land.** Lift and landing hysteresis in `FootPlacement.Update` and the tick's touchdown logic: a swing foot that has reached the target must descend and report planted, and a step must not stay in flight longer than a stated limit.
2. **Stance stiffness and gains.** Hip and ankle strengths, the height-servo rate, the gain schedule in `docs/harness.md`. Change one value at a time.
3. **Fore-aft pelvis servo sign.** Review it against the eight push directions; the R2b bisect was neutral for standing, but pushes may disagree.
4. Anything the numbers point to.

## Stop rule

25 more logged experiments (E11 to E35) or the checks passing, whichever is first. Same rules as R2b: never loosen a threshold, edit the harness or hide a failing test. If the stand check still fails after E35, write the best configuration and the numbers.

A second approach, packet B1 (an independent standard-control balance controller), is being built in parallel. If the integrator tells you B1 has passed the stand and push scenarios, stop and write your handoff.
