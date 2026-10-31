# Ragdoll layout

`RagdollBuilder.Build` turns a mapped skeleton into body and joint frames for
the Unity baker. It is independent MIT code: no Delirium, GTA, `kv.bin` or
game tuning was read or translated. Mass fractions come from
`SA.Humanoid.Profile` (de Leva-derived, see `docs/profile.md`); every size
ratio below is an authored starting value (A).

## API

Namespace `SA.Humanoid.Build`. `BodyLayout` (role, `WorldFromBody` frame,
capsule segment, radius, box half extents, mass, centre of mass),
`JointLayout` (child, parent, anchors in each body frame, joint frames,
`RestRelative`, profile limit), `RagdollLayout` (`Bodies` with `Roles.Count`
entries indexed by role, `Joints` with `Roles.Count - 1` entries ordered by
child role skipping the pelvis root, `Diagnostics`, `IsValid`).
`RagdollBuilder.Build(skeleton, boneOfRole, totalMass)` validates the map with
`HumanoidMapping.Map` and keeps every mapping diagnostic verbatim.

## Frames and segments

World rest poses compose `SkeletonBone.Rest` down the parent chain with a
per-bone uniform scale. A body origin sits at its role joint; its x axis
points at the next role joint. Preferred next roles are Torso for the pelvis
and Head for the torso, then the single distal neighbour down each limb.
Head, hands and feet have no distal role, so they continue the parent segment
direction with the parent segment length. When the preferred joint is missing
or coincident, the frame falls back to the longest viable role-child segment,
then the reversed parent segment, then world +X. The y/z orientation follows
a fixed world-up reference (world +Z within 0.99 of the segment axis).

Each joint anchor is the child joint position expressed in the parent and
child body frames, so both name the same world point (the child-side anchor
is the body origin). Joint frames carry the child body orientation: the
child-side frame is identity and the parent-side frame equals `RestRelative`,
which is `inverse(parent body rotation) * child body rotation`, the neutral
drive target. Rest relative rotation is therefore identity in joint frames.

## Sizes (BuildParams.Standard, all A)

Capsules span joint to joint in body space with centre of mass at the middle.
Radii multiply segment length by `LimbRadiusRatio` 0.12 (upper arm, forearm,
thigh, shin), `HeadRadiusRatio` 0.28, `HandRadiusRatio` 0.16 and
`FootRadiusRatio` 0.16. Pelvis and torso are boxes centred mid-segment with
half extents `(length/2, widthRatio * length, depthRatio * length)`; width
0.80 and depth 0.50 for the pelvis, width 0.40 and depth 0.28 for the torso.
Mass is `HumanProfile.Mass(role, totalMass)`. Nested bone scales compose
multiplicatively, so a uniform character scale lives on the root bone only.

## Scale, totality and allocation

A bone scale must be finite, positive and uniform within a relative tolerance
of 1e-4; anything else records `BadBoneScale` and bakes at unit scale.
Non-finite positions or invalid rotations are sanitized to zero and identity
and record `BadRestValue`. Segments under 1 mm record `ZeroLengthSegment`.
A non-positive or non-finite total mass records `BadTotalMass` with all
masses zero. Missing or unusable joints use identity frames and zero vectors.
`IsValid` is true exactly when there are no diagnostics. The builder allocates
only the returned arrays plus transient per-call scratch arrays; no lists,
dictionaries, closures or LINQ escape or persist.

## Diagnostics

Mapping codes 0-6 are documented with packet N4. Build-local codes reuse the
`Diagnostic` struct: 7 `ZeroLengthSegment`, 8 `BadBoneScale`, 9
`BadRestValue`, 10 `BadTotalMass`, named in `BuildDiagnostics` and sorted by
role then code with the mapping diagnostics. Geometry is only diagnosed for
bones and joints that influence a mapped role.

## Mirror note

Negating skeleton X mirrors body positions and segment axes exactly, and
radii, masses, limits and local quantities are unchanged. Frame twist follows
the world-up convention, so mirrored full orientations match up to the
convention's half-turn about the segment axis, not `Frames.Mirror` directly:
a reflected right-handed frame cannot keep both its x axis on the mirrored
segment and its exact orientation.
