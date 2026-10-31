# H1 harness: pure .NET articulated test support

Test support only. Not shipped, not the product solver. Unity Physics remains
the sole solver in the product. The harness exists so controller agents can
tune in seconds without Unity: a deterministic 15-body humanoid simulator
built from `SA.Humanoid` roles and `HumanProfile` masses.

## API

`HarnessWorld.Create(totalMass)` builds the standing bind pose below with zero
velocity. `Step(dt)` advances fixed-step dynamics. `Bodies` exposes the 15
`SA.ReactionControl.RoleBody` entries indexed by `(int)Role`, in world space.
`ApplyDrives` stores a 15-entry `JointDrive` array indexed by role and applies
it every substep. `ApplyImpulse(role, impulse, worldPoint)` adds an external
impulse. `ProbeGround(foot)` returns a `SupportProbe` for a foot. Introspection
helpers for tuning: `TotalEnergy`, `JointAnchorSeparation`,
`JointLimitViolation` (radians of excess, 0 when inside), `BodyRadius`,
`BodyHalfLength`, `BodyPrincipalInertia`, `TranslateAll`.

`Step` clamps `dt` to `[0, 1/30]`, ignores non-finite input, and substeps at
240 Hz or 480 Hz (`SubstepRate` 480, `MaxSubsteps` 32): 1/60 s runs 8 substeps
of 1/480 s, 1/120 s runs 4, so both rates integrate the identical substep
stream and agree bit-for-bit on passive runs. Everything is total (never
throws; bad input is sanitized) and deterministic (fixed order, no randomness).

## Bodies

Capsule per role, segment along local Y, mass from
`HumanProfile.Mass(role, totalMass)` (fractions sum to 1). Solid-cylinder
principal inertia, foot pitched 90 degrees about X so its segment lies flat.

| Role | Mass at 70 kg | Radius | Half length | Bind center |
|---|---|---|---|---|
| Pelvis | 7.82 | 0.11 | 0.05 | (0, 1.00, 0) |
| Torso | 22.60 | 0.13 | 0.14 | (0, 1.30, 0) |
| Head | 4.86 | 0.10 | 0.04 | (0, 1.61, 0) |
| UpperArm L/R | 1.90 | 0.05 | 0.11 | (+-0.23, 1.31, 0) |
| Forearm L/R | 1.13 | 0.04 | 0.10 | (+-0.26, 1.04, 0) |
| Hand L/R | 0.43 | 0.035 | 0.05 | (+-0.275, 0.84, 0) |
| Thigh L/R | 9.91 | 0.075 | 0.15 | (+-0.11, 0.745, 0) |
| Shin L/R | 3.03 | 0.055 | 0.14 | (+-0.11, 0.315, 0) |
| Foot L/R | 0.96 | 0.05 | 0.08 | (+-0.11, 0.05, 0) |

Feet start exactly touching (lowest sample equals radius). Character faces +Z,
left is -X. Masses: 7.82 + 22.60 + 4.86 + 2*1.90 + 2*1.13 + 2*0.43 + 2*9.91 +
2*3.03 + 2*0.96 = 70.0.

## Joints

14 ball joints. Child/parent from `Roles.Parent`. Anchor: joint point in bind
pose, stored in each body frame. Joint points: waist (0, 1.10, 0), neck
(0, 1.50, 0), shoulders (+-0.21, 1.45, 0), elbows (+-0.25, 1.17, 0), wrists
(+-0.27, 0.92, 0), hips (+-0.11, 0.96, 0), knees (+-0.11, 0.53, 0), ankles
(+-0.11, 0.10, 0). Excursion is measured against the bind relative rotation.
Limits come from `HumanProfile.Of(role).Limit`. Elbows and knees are treated as
hinges: their twist channel maps to the anatomical flexion axis (sign per
side, matching the mirrored profile ranges: ForearmLeft -X, ForearmRight +X,
ShinLeft +X, ShinRight -X), swings map to the other two body axes. All other
joints measure twist about local Y with swings about X/Z against
swing1Max/swing2Max.

## Solver

Semi-implicit Euler. Gravity 9.81. Ground plane y = 0 with Coulomb friction
0.8 and zero restitution. Each substep: forces (gravity, drives, linear 0.2
and angular 2.0 damping), then 32 sequential-impulse iterations over joint
anchors, joint limits (velocity, inelastic, small return bias), and contacts
(3 samples per capsule, accumulated normal with friction capped by mu times
normal), then one pass of position projections (contacts 0.35, anchors 0.5
with mass weighting, limits partial 0.1), then integration with quaternion
spin update. Velocity biases are zero; resting contact error converges to the
1 mm slop by projection, which adds no velocity and cannot pump bouncing.
Joint viscous friction removes 5 percent of relative spin per iteration so leg
momentum funnelling through ankle/wrist levers cannot spin light extremities.
Speeds clamp at 8 m/s and 30 rad/s. Inverse inertia worlds refresh every
substep through `SA.Motion.Mechanics.Inertia`.

## Drives

`ApplyDrives` indices are world-space: `Target` is the desired world
orientation, `TargetAngularVelocity` the desired world spin. Each substep the
harness splits `QuaternionDrive.Torque` into a proportional part (explicit)
and a derivative part against relative spin (child minus parent), each
clamped once by shared `MaxTorque` (single-clamp-sum semantics), applied as
equal and opposite angular impulses on child and parent. The derivative part
carries an implicit stability factor `1 / (1 + c dt w)`, so any damping is
stable; the proportional part is explicit with wide margins at 480 Hz. The
pelvis entry is a world torque (orientation only, no position servo). Drive
damping uses relative spin because world-spin damping pumped the parent and
buckled the figure; the pelvis entry keeps world-spin damping.

Gain schedule used by the hold check (stiffness, damping):
pelvis 2000, torso 200, head 80, upper arm 30, forearm 15, hand 10, thigh 1000,
shin 900, foot 700; damping is critical on the transverse coupled inertia
`2 sqrt(k / (1/Ix + 1/Ipx))`; torque cap is half the stiffness. Rules found by
measurement: stiffness must exceed the full-body topple gradient per supported
joint (ankles carry half the body each); the pelvis world gain must dominate
the heaviest child drive (2000 anchors the torso) or torso corrections
whiplash the light pelvis; damping must use coupled pair inertia, never single
body minima, and the implicit factor makes it unconditionally stable.

## Probes

`ProbeGround` uses the lowest capsule sample: distance is sample height minus
radius (negative is penetration), hit is set within 6 cm, point is the ground
projection, normal is up, velocity is the body point velocity. `TotalEnergy`
is kinetic plus `m g y` gravitational energy.

## Measured stability limits

1 m drop, 5 s at 60 Hz: worst per-step energy rise 0.015 percent (limit 1
percent); resting penetration 2 mm (limit 20 mm); worst anchor separation over
the run 19.5 mm (limit 20 mm); resting limit excess 0.0003 rad (limit 5
degrees); strong-gain hold keeps the pelvis within 33 mm over 5 s (limit 100
mm); 60 Hz vs 120 Hz resting poses agree bit-identically (limit 50 mm).
Impact transient penetration reaches 4 cm mid-collapse; the 20 mm check
applies to the resting state. The pile has no self-collision; bodies settle
through each other onto the ground. Turn-clamp-free drive application relies
on joint friction plus implicit derivative damping for light-axis stability.
