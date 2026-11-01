# Leg IK: planting, roll and reach

`SA.Motion.Legs` keeps feet on the ground without fighting the animation. It is portable .NET (`src/SA.Motion/Legs`); Unity only gathers the animated chain, casts one ray per foot, and writes the result back (`packages/com.sa.unity.reactions/Runtime/Character/LegIKSystem.cs`).

## What was wrong before

The old foot lock pinned the ankle point until the animation lifted it 13 cm, then flattened the foot onto the ground. A planted foot could not roll from heel to ball, so the knee and hip took all the motion and the leg looked stiff. The knee pole was a fixed character-forward instead of the animated knee, nothing eased the knee into full extension, and every threshold was a hard-coded number.

## The model

Each foot has two points on its sole, `Heel` and `Ball`, in the foot bone's space. They are found from the rest pose (the toe bone gives the ball, the heel sits behind the ankle) or set by hand in Leg IK Authoring, where orange gizmos show them.

1. **Terrain.** One ray under each foot gives the ground height. A free foot keeps the clearance it has above the animation's floor, so it hovers over a step exactly as it hovered over flat ground (`TerrainFollow`, `TerrainHalfLife`, `MaxStepHeight`).
2. **Plant.** A foot plants when the lowest point of its sole is within `PlantHeight` of the ground and is not rising faster than `PlantRise`. It lets go above `ReleaseHeight`, when rising faster than `ReleaseRise`, or when the animated foot has drifted `ReleaseDistance` from the pin. Between `PinRadius` and `ReleaseDistance` the pin is dragged along, so a mismatched clip slides rather than overstretches.
3. **Fulcrum roll.** A planted foot pins one point on the ground and rolls on it the way Cascadeur's fulcrum does: toe up rolls on the heel, heel up rolls on the ball, and over `RollBand` around flat the contact moves smoothly between them. The animated pitch is kept, so heel strike, foot flat, heel off and toe off all keep a still contact. `TurnPivot` picks the point a planted foot turns about.
4. **Slope.** A planted foot tilts with the ground (`SlopeAlign`, at most `MaxSlopeAlign`) on top of its animated pitch. Ground steeper than `MaxWalkableSlope` is never planted on.
5. **Blend.** Every plant and every release is inertialized over `BlendSeconds` from the foot's last output, so neither pops (the same Gears of War technique as clip switches).
6. **Pelvis.** The pelvis lowers by the deepest planted foot, so a foot on lower ground can reach it (`PelvisDrop`, `PelvisRaise`, `PelvisHalfLife`, `PelvisWeight`).
7. **Reach.** The leg is solved with soft two-bone IK: the knee keeps the animated bend (`KneeFollow`), eases into full extension over `Softness` instead of snapping straight, and may stretch up to `MaxStretch`. A target the animation already reaches is left exactly as animated.

## Baking the foot shape

Heel and ball are measured at bake time from the ankle's height above the character's floor. That height must be 3 to 35 percent of the leg length; otherwise the baker uses 13 percent of the leg length and warns (`FootHeight`, `FootShapes.Posed`). A skeleton that is posed or moved when the scene is baked, for example by a saved override on the Hip, used to give a heel and ball a metre from the foot, so no foot ever counted as near the ground and Plant feet on ground did nothing.

## Control

| What | How |
|---|---|
| Per clip | Rig Authoring > Resolved animations > **Foot IK** tick. Untick it for flips, jumps and anything airborne. |
| Per character | Leg IK Authoring: preset, custom settings, ground layers, heel and ball, start enabled. |
| At runtime or from a timeline | Write `LegIK.Weight` (0 to 1), or enable and disable the `LegIK` component. |
| Smoothing | The solver eases toward `Weight` times the clips' ticks over `WeightHalfLife`, so turning IK on or off never pops. Disabling the component is a hard stop that resets the legs. |
| If feet do not plant | After 3 seconds the Console names the cause: Weight 0, the playing clip has Foot IK unticked, the heel and ball baked far from the foot bone, or no ground under the feet. |
| Seeing it work | In Play mode in the Editor the Console says when a character's feet are planted, and green crosses in the Scene view mark each planted foot (Tools > SA > Show Leg IK Contacts). After 3 seconds without a planted foot the Console names the cause: Weight 0, the playing clip has Foot IK unticked, or no ground under the feet (`LegIKAdvice`). |

## Presets

| Preset | Use |
|---|---|
| `Gentle` (default) | Fixes sliding and rolls the feet, with small pelvis and slope corrections. Stays close to the animation. |
| `Standard` | Full terrain adaptation for walking characters. |
| `Grounded` | Heavy characters: sticky plants, slower release, slower pelvis. |
| `Agile` | Fast characters: quick release, crisp plants. |
| `Strict` | Cascadeur-like contacts that never slide; they only let go on lift. |

## Settings

Lengths and speeds are for a leg 0.85 m long (`LegParams.ReferenceLegLength`) and scale with the character. `LegParamsSpec` holds the range, unit, group and help text for each one; the Unity inspector draws its sliders from it.

| Setting | Group | Unit |
|---|---|---|
| `Weight` | Overall | ratio |
| `WeightHalfLife` | Overall | s |
| `PlantHeight` | Planting | m |
| `ReleaseHeight` | Planting | m |
| `PlantRise` | Planting | m/s |
| `ReleaseRise` | Planting | m/s |
| `PinRadius` | Planting | m |
| `ReleaseDistance` | Planting | m |
| `BlendSeconds` | Planting | s |
| `TurnPivot` | Roll | ratio |
| `HeelRoll` | Roll | ratio |
| `BallRoll` | Roll | ratio |
| `RollBand` | Roll | rad |
| `SlopeAlign` | Ground | ratio |
| `MaxSlopeAlign` | Ground | rad |
| `MaxWalkableSlope` | Ground | rad |
| `TerrainFollow` | Ground | ratio |
| `TerrainHalfLife` | Ground | s |
| `MaxStepHeight` | Ground | m |
| `Softness` | Reach | ratio |
| `MaxStretch` | Reach | ratio |
| `KneeFollow` | Reach | ratio |
| `PelvisWeight` | Pelvis | ratio |
| `PelvisDrop` | Pelvis | m |
| `PelvisRaise` | Pelvis | m |
| `PelvisHalfLife` | Pelvis | s |

## Verified

- `tests/SA.Motion.Tests/LegTests.cs` (22 tests, both library builds). The heel stays pinned within 1e-5 m while a toe-up foot rolls, and so does the ball for a heel-up foot. The roll is continuous through flat, with at most 0.3 mm of ankle motion per 0.0005 rad. A scripted heel-strike to toe-off gait keeps the heel, then the ball, within 1 mm while the animation slides 3 mm per frame. Release and replant move at most 1 mm on the switch frame. The pelvis settles at -0.12 m for ground 0.12 m lower and returns once free. Corrupt settings fall back to the defaults, results are bit-identical across runs, and the schema round-trips.
- `packages/com.sa.unity.reactions/Tests/LegIKTests.cs` (Unity, 4 tests): a planted foot stays within 2 mm while the body drifts 12 cm and follows a moving floor; release never teleports; an unticked clip fades the solver out; disabling or the ragdoll leaves the animated pose.
- Cost: 242 ns per leg single-threaded on .NET 10 x64 for the standard step cycle, down from about 687 ns before packet L4 (`docs/handoff/L4.md`; `dotnet run -c Release --project tests/SA.Motion.Benchmarks`). Tilted ground costs about 314 ns per leg. Plain-text profiles: `LegProfileText` (`docs/handoff/L2.md`).
- Kyle in Play mode (2026-11-01, per-frame CSV in `Temp/sa/`, from a probe that moved the root through the demo scene; it was removed afterwards because it moved Kyle in the owner's demo, and is in git history at `529f816`):
  - Standing idle: both planted, ankle drift 4.5 and 10 mm/s mean (IK off: 17 and 21 mm/s), no pops.
  - Walking, running and sprinting do **not** pass. With the root travelling at the clips' speed (2.2, 3.6 and 4.9 m/s, estimated from stance foot motion) a plant holds the anchor only about 0.1 s, then the state flips; plants chatter (24 to 32 events per 4 s per foot, planted 15 percent of a run).
  - One clip switch (walk to run, 8.1 s) produced a leg-bone step of 81 degrees in one frame, against 25 degrees with IK off. Elsewhere IK on and off are comparable.
  - Cause not found. Ruled out: the leg near full extension (reach ratio 0.92 to 0.96 at the spikes) and the inertialization history write-back (removing it made the 8.1 s spike worse, 88 degrees). Suspects: plant triggering while the foot is still sliding forward at touchdown, and the 0.16 s plant blend stacking on a foot whose anchor is still dragging.
  - The demo's in-place Kyle on a stationary root is a treadmill and is not a valid test of planting; use a travelling root, run from a scratch scene and never from the demo scene.
- Kyle in the Reactions demo (2026-11-01): the subscene instance carried a saved override that put the Hip at (0, 0, 0), so at bake time the skeleton was 1.06 m low and the baked heel and ball were 0.95 m from the ankle. With the override removed and the baker hardened, both feet plant from the first frame and stay planted through idle (frame time fixed at 1/60 s; the unfocused Editor ran at about 1 frame per second, so times are game time). With the corrupt scene put back, the baker estimates a sane shape, both feet still plant, and the Console warns for each foot. Walking and running were not re-measured.
- Not verified: the UAL mannequin, stairs and slopes in a real scene, toe-bone bending, and quadrupeds.
