# Tuning the capture-point balance controller (C1)

Independent deterministic tuning of `SA.Balance.BalanceParams` for the
capture-point `StandingController`. Objective is `Evaluator.RunFast`;
the final check is the full `Evaluator.Run` scorecard plus a hold-out
of 16 half-offset directions at 100 and 150 N-s that the optimizer
never trains on.

## Optimizer

Elitist (1 + lambda) evolution strategy over a normalized 18-gene
vector, decoded through the clamped ranges below. Gaussian mutation
from a seeded xorshift64* stream (seed 20261031), so the run is
bit-deterministic given the same binary. Sigma decays linearly from
0.12 to 0.04 across generations. A greedy coordinate-descent refine
(step 0.05, up to 2 sweeps) polishes the evolved best. No push
direction, harness internals, or wall-clock value enter the genome.

## Search ranges

| Gene | Field | Min | Max | Standard |
|---|---|---|---|---|
| 0 | UprightGain | 0.05 | 1.0 | 0.3 |
| 1 | UprightDamp | 0.0 | 0.5 | 0.15 |
| 2 | UprightMax | 0.1 | 0.6 | 0.3 |
| 3 | AnkleGain | 0.5 | 5.0 | 2.0 |
| 4 | HipGain | 0.0 | 1.0 | 0.3 |
| 5 | HipVelGain | 0.0 | 0.3 | 0.05 |
| 6 | HipRollGain | 0.2 | 2.5 | 1.0 |
| 7 | HipRollVelGain | 0.0 | 0.6 | 0.2 |
| 8 | LeanMax | 0.1 | 0.6 | 0.3 |
| 9 | StepMargin | 0.02 | 0.25 | 0.1 |
| 10 | StepTime | 0.12 | 0.35 | 0.22 |
| 11 | StepLead | 0.0 | 1.0 | 0.5 |
| 12 | MaxReach | 0.4 | 0.9 | 0.6 |
| 13 | FallReach | 0.8 | 2.5 | 1.5 |
| 14 | FallSpeed | 1.5 | 4.5 | 2.35 |
| 15 | SupportRadius | 0.05 | 0.35 | 0.2 |
| 16 | StanceBoost | 1.0 | 2.5 | 1.5 |
| 17 | StepHeight | 0.05 | 0.2 | 0.1 |

Stiffness, damping, gravity, com-height, height-error and fall-tone
fields stay at `BalanceParams.Standard`: the harness schedule carries
them and the stand already passes.

## Log

Baseline: fast 69.411, full 80 (8/8/8/0/0/0, stand pass).

Single-gene probes at normalized step 0.1 from standard (fast):

| Gene | - | + |
|---|---|---|
| UprightGain | 59.837 | 61.837 |
| UprightDamp | 65.683 | 60.090 |
| UprightMax | 64.694 | 70.021 |
| AnkleGain | 73.023 | 59.204 |
| HipGain | 60.520 | 64.756 |
| HipVelGain | 63.476 | 67.964 |
| HipRollGain | 69.051 | 67.975 |
| HipRollVelGain | 67.735 | 69.328 |
| LeanMax | 70.851 | 69.356 |
| StepMargin | 70.244 | 64.181 |
| StepTime | 65.984 | 64.285 |
| StepLead | 65.712 | 64.370 |
| MaxReach | 70.415 | 69.514 |
| FallReach | 69.469 | 69.469 |
| FallSpeed | 45.365 | 74.298 |
| SupportRadius | 70.343 | 68.976 |
| StanceBoost | 65.105 | 69.303 |
| StepHeight | 65.186 | 69.931 |

Evolution, 16 generations x width 8, seed 20261031 (best fast):

| Generation | Best fast | Wall clock |
|---|---|---|
| -1 (standard) | 69.469 | 0 s |
| 0 | 73.239 | 40 s |
| 1 | 78.543 | 80 s |
| 2 | 86.458 | 120 s |
| 3 | 86.458 | 160 s |
| 4 | 90.672 | 210 s |
| 5 | 90.672 (probe full 83: r200 5, r300 3) | 280 s |
| 6 | 91.284 | 320 s |
| 7 | 91.284 | 360 s |
| 8 | 91.284 | 400 s |
| 9 | 94.388 | 440 s |
| 10 | 94.388 | 480 s |
| 11 | 94.388 (probe full 89: r200 3, r300 4) | 560 s |
| 12 | 94.388 | 600 s |
| 13 | 94.388 | 640 s |
| 14 | 94.388 | 680 s |
| 15 | 94.388 | 720 s |
| refine | 94.813, no sweep gain | 788 s |

The 18-dim best scored full 88 (8/8/8/7/5/3) but the hold-out
regressed to 26 of 32 against standard 30 of 32: the extra genes
overfit to the even training directions. Repair reverts toward
standard, one at a time (hold-out tuned vs standard): StepTime 0.22
gives 26 vs 30; plus UprightMax, HipGain, LeanMax at standard gives
28; plus StepLead 0.5 gives 29; HipRollVelGain 0.2 drops to 28 and is
reverted; StepMargin 0.1 with SupportRadius 0.2 drops to 27 and is
reverted.

Finding: FallSpeed alone carries the gain. Standard latches Falling
past 2.35 m/s center speed, forfeiting recoverable 200 and 300 N-s
pushes; 3.71 steps through them instead. `Tuned` is therefore
`Standard` with `FallSpeed = 3.7087204`, the evolved value kept
exact. Below 200 N-s the latch never binds, so every 0 to 150 N-s
trajectory is bit-identical to standard and the hold-out matches
standard exactly at 30 of 32. Adding FallReach 2.5 changes nothing
and was dropped.

## Final scorecard (Tuned)

c1-tuned: score 93/100, stand pass (drift 0.044 m, lean 6.2 deg), deterministic True

| Impulse | Recovered/8 | Fell | With steps | Recovery s | Peak m | Latched |
|---|---|---|---|---|---|---|
| 60 | 8 | 0 | 5 | 0.46 | 0.17 | 0 |
| 100 | 8 | 0 | 8 | 1.35 | 0.39 | 0 |
| 150 | 8 | 0 | 8 | 2.58 | 0.93 | 0 |
| 200 | 7 | 1 | 7 | 3.75 | 1.37 | 0 |
| 300 | 3 | 5 | 3 | 2.04 | 1.54 | 0 |
| 450 | 4 | 4 | 4 | 2.48 | 1.36 | 0 |

Hold-out (16 half-offset directions x 100 and 150 N-s, 32 runs):
tuned 30, standard 30. Full card in `docs/leaderboard/c1-tuned.json`.
