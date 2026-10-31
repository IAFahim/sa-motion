# Soft joint limits as a corrective drive

`SA.Motion.Mechanics.SoftLimits` restores cone-and-twist and hinge joint
limits as a torque drive instead of a solver constraint. The result is a
torque on the child body; negate it for the parent body. Inside the limits
the result is exactly `Float3.Zero`, including with nonzero angular
velocity. Outside, each violated limit contributes `Stiffness * excess`
plus `Damping * outwardRate` (outward motion only), with the vector sum
capped at `MaxTorque` preserving direction.

Derived from standard rigid-body mechanics (rotation-vector error and a
swing-twist decomposition); no Delirium code or data was read. All angles
are radians, all axes and torques are in the parent frame, angular
velocity is the child velocity relative to the parent in the parent frame.

## Frames

`relative` is the child rotation in the parent frame, `rest` is the same
joint's rest rotation. The deviation is `relative * inverse(rest)`,
canonicalized so `q` and `-q` give the same torque. `limbAxis` (cone-twist)
and `hingeAxis` (hinge) are unitized internally; a zero-length axis gives
zero torque. Torques are child torques: apply `-torque` to the parent.

## Cone-twist

The deviation `d` splits into `d = swing * twist`, where `twist` is the
rotation about `limbAxis` by `twistAngle = 2*atan2(dot(d, axis), d.W)` in
`[-pi, pi]` and `swing` is the remainder with angle in `[0, pi]`. The cone
excess is `swingAngle - ConeAngle`, the twist excess is the signed distance
of `twistAngle` outside `[TwistMin, TwistMax]`. The stiffness term is
`-swingAxis * Stiffness * coneExcess - limbAxis * Stiffness * twistExcess`.
The damping term projects the relative angular velocity onto each excess
direction and opposes it only while the projection points further outside.

When the deviation is a half turn about an axis perpendicular to the limb
axis the twist component is undefined; the twist excess is then taken as
zero and the whole deviation is handled as swing. A `ConeAngle` of `pi`
(or a twist range covering `[-pi, pi]`) never violates, so random poses
return exactly zero. A `ConeAngle` of zero (or `TwistMin == TwistMax`)
violates on any nonzero deviation in that channel.

## Hinge

Only the rotation about `hingeAxis` is limited; off-axis deviation is
ignored. The signed angle `2*atan2(dot(d, axis), d.W)` in `[-pi, pi]`
minus the nearer bound is the excess, the stiffness term is
`-hingeAxis * Stiffness * excess`, and damping opposes the axial angular
velocity only while it increases the excess. `Min == Max` pins the joint
at one angle; `[-pi, pi]` never violates.

## Totality

Non-finite inputs (NaN or infinite quaternion, axis, velocity, limit or
gain components), zero-length quaternions (treated as identity after
normalization), zero-length axes, negative `Stiffness` or `Damping`,
non-positive or non-finite `MaxTorque`, negative `ConeAngle`, and
`TwistMin > TwistMax` or `Hinge.Min > Hinge.Max` all return `Float3.Zero`
without throwing. Bad limits never produce torque in any pose.

## Tuning

`Stiffness` sets the radians of penetration per unit torque; `Damping`
near `2*sqrt(Stiffness*inertia)` settles a pushed rotor without regrowth.
The `RotorPushedPastHingeLimitSettlesInside` test pushes a hinge rotor with
inertia 2 (`Stiffness` 120, critical damping, cap 1000) from 0.8 rad to a
0.5 rad limit: the violation never exceeds 1.05x the initial 0.3 rad
excess, drops under 5 percent of the excess over the last 2.4 s of the
12 s run at 60 Hz, and reaches exactly zero inside the window.
