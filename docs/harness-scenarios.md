# Harness push scenarios

`Scenarios.Run(controller, push, seconds)` applies one `PushSpec`
(role, direction, impulse, at-seconds) to a fresh `HarnessWorld`
at 60 Hz and returns a `ScenarioResult`.
`Scenarios.Sweep(controller, impulses, directions, seconds)`
pushes the torso center at 0.5 s along evenly spaced horizontal
directions and returns `impulses.Length * directions` results
in impulse-major order. `Controllers.Passive` writes zero drives;
`Controllers.HoldPose` holds the start pose with the H1 gain schedule.

Fell: pelvis below 0.5 m or torso lean past 60 deg at the end.
disp: peak pelvis displacement from start, m. lean: peak torso
lean from upright, deg. rec: seconds after the push until height
stays within 0.1 m and lean within 15 deg through the end
(full window when never). steps: distinct foot lifts past
0.05 m. finalY: end pelvis height, m.

Passive (12/12 fall; no push also falls: disp 1.16, lean 138.5,
rec 5.00, steps 2, finalY 0.13):
| J | dir | Fell | disp | lean | rec | steps | finalY |
| 10 | +X | True | 1.40 | 92.1 | 4.50 | 0 | 0.12 |
| 10 | +Z | True | 1.30 | 93.8 | 4.50 | 1 | 0.12 |
| 10 | -X | True | 1.17 | 115.4 | 4.50 | 2 | 0.14 |
| 10 | -Z | True | 1.05 | 126.1 | 4.50 | 1 | 0.12 |
| 50 | +X | True | 1.38 | 92.2 | 4.50 | 0 | 0.12 |
| 50 | +Z | True | 1.35 | 96.1 | 4.50 | 0 | 0.12 |
| 50 | -X | True | 1.31 | 93.0 | 4.50 | 0 | 0.12 |
| 50 | -Z | True | 1.28 | 98.0 | 4.50 | 0 | 0.12 |
| 100 | +X | True | 1.43 | 94.0 | 4.50 | 0 | 0.12 |
| 100 | +Z | True | 1.36 | 91.4 | 4.50 | 0 | 0.12 |
| 100 | -X | True | 1.44 | 90.1 | 4.50 | 1 | 0.12 |
| 100 | -Z | True | 1.33 | 94.2 | 4.50 | 0 | 0.12 |

Hold (0/12 fall; ramped to 1000 N s with no 5 s fall; pushes
past ~181 N s near-identical under the 8 m/s harness clamp):
| J | dir | Fell | disp | lean | rec | steps | finalY |
| 10 | +X | False | 0.10 | 30.9 | 4.10 | 0 | 0.99 |
| 10 | +Z | False | 0.14 | 58.9 | 4.50 | 0 | 0.99 |
| 10 | -X | False | 0.11 | 34.5 | 4.13 | 0 | 0.99 |
| 10 | -Z | False | 0.13 | 52.2 | 4.50 | 0 | 0.99 |
| 50 | +X | False | 0.25 | 28.1 | 4.50 | 0 | 0.96 |
| 50 | +Z | False | 0.19 | 64.6 | 4.50 | 0 | 0.99 |
| 50 | -X | False | 0.29 | 22.4 | 4.10 | 0 | 0.96 |
| 50 | -Z | False | 0.17 | 64.6 | 4.43 | 0 | 0.98 |
| 100 | +X | False | 0.32 | 30.1 | 4.50 | 0 | 0.96 |
| 100 | +Z | False | 0.21 | 56.5 | 4.50 | 0 | 0.99 |
| 100 | -X | False | 0.34 | 30.6 | 4.37 | 0 | 0.96 |
| 100 | -Z | False | 0.19 | 65.9 | 4.35 | 0 | 0.99 |

Recovery claims cover 5 s windows: the stiff hold rocks past
60 deg lean after ~8 s even unpushed.
