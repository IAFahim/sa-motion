# Gate M: two-body drive against real Unity Physics

Package `com.sa.unity.reactions`, tests `SA.Reactions.Tests` (15 tests, all passing, headless Editor, Unity 7000.0.0a12, Unity Physics 6.7.0). Raw measurements: `Temp/sa/gate_m.json` written by `RecordGateMMeasurements`.

## What the drive is

`PoseDriveSystem` runs inside `PhysicsSystemGroup` after `PhysicsInitializeGroup` and before `PhysicsSimulationGroup`. For each child body it computes the rotation error between the child joint frame and its target in the parent joint frame, turns it into an angular acceleration with the implicit spring-damper (`AngularSpring`), solves the 3x3 system `(invI_parent + invI_child) J = h * a` for the angular impulse, clamps it to `MaxTorque * h`, and applies `-J` to the parent and `+J` to the child directly in the built `PhysicsWorld`. The same step's solver sees it.

## Measured

Setup: static parent with a rotated frame, child capsule with its centre of mass 0.25 m from the ball joint, gravity 9.81, drive 10 Hz at damping ratio 1, cap 1000 N·m, 4 s.

| Timestep | Steady error to target | Anchor separation |
|---|---|---|
| 30 Hz | 0.157 rad | 0 m |
| 60 Hz | 0.067 rad | 0 m |
| 120 Hz | 0.036 rad | 0 m |

The error is the gravity sag of a pure spring-damper: the drive torque is proportional to the error, so a constant load needs a constant error. It falls as the step shrinks because the drive applies its impulse more often. A passive arm (strength 0) falls 0.93 rad in 1.5 s.

## Gate M items

| # | Item | Result |
|---|---|---|
| 1 | Zero error gives no drive; target sign reversal gives the same drive | Passed (pure tests). World to inertia conversion round trip: `InertiaTests` in the library |
| 2 | Displacement decays; an external kick displaces; strength zero is passive | Passed |
| 3 | Equal and opposite internal impulses; caps hold and are reached; 30/60/120 Hz bounded | Passed (cap reached to 99%, never exceeded) |
| 4 | Anchor survives gravity and a tilted target; no hinge locking at a ball joint | Passed (free rotation about each of x, y, z within 6%) |
| 5 | Post-build mutation affects this step and survives export | Passed implicitly: the arm only holds because the drive acts through the built world. A focused negative test for component-only writes was not written |
| 6 | High impulses near a wall, dynamic-tree refit, tunneling bounds | **Not done.** The drive does not refit the broadphase. No ground or wall contact was tested |
| 7 | No skeletal parenting, no double stepping, no per-tick allocation, no competing writer | Parenting, stepping and writer by construction. **Allocation not measured** |

Gate M is therefore passed for items 1 to 4 and partly 5. Items 6 and 7 stay open, and a humanoid with ground contact has not been tested.

## Correction, same day

A first full-character run exploded. The cause was in the drive: `MotionVelocity.AngularVelocity` is in the body's inertia space, and the drive fed it into the damping term as if it were world space. The Gate M arms had nearly aligned inertia frames, so the original 15 tests did not expose it. `PoseDriveSystem` now rotates it into world space. After the fix the kick test needed a true world-space kick (the earlier one was also applied in the wrong space); the drive removes about three quarters of a 25 rad/s kick within one step, so the test now uses 80 rad/s and a threshold matched to that stiffness. 15 of 15 pass again; the steady errors above are unchanged.

A second finding from the full character: Unity's two-joint ragdoll factory (primary cone plus perpendicular cone) is unstable when the perpendicular range is the full (-pi/2, pi/2). Only the primary cone-and-twist joint is used.
