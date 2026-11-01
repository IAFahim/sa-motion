# Leg IK: planting, roll and reach

`SA.Motion.Legs` keeps feet on the ground without fighting the animation. It is portable .NET (`src/SA.Motion/Legs`); Unity only gathers the animated chain, casts one ray per foot, and writes the result back (`packages/com.sa.unity.reactions/Runtime/Character/LegIKSystem.cs`).

## What was wrong before

The old foot lock pinned the ankle point until the animation lifted it 13 cm, then flattened the foot onto the ground. A planted foot could not roll from heel to ball, so the knee and hip took all the motion and the leg looked stiff. The knee pole was a fixed character-forward instead of the animated knee, nothing eased the knee into full extension, and every threshold was a hard-coded number.

## The model

Each foot has two points on its sole, `Heel` and `Ball`, in the foot bone's space. They are found from the rest pose (the toe bone gives the ball, the heel sits behind the ankle) or set by hand in Leg IK Authoring, where orange gizmos show them.

1. **Terrain.** One ray under each foot gives the ground height. A free foot keeps the clearance it has above the animation's floor, so it hovers over a step exactly as it hovered over flat ground (`TerrainFollow`, `TerrainHalfLife`, `MaxStepHeight`).
2. **Plant.** A foot plants when the lowest point of its sole is within `PlantHeight` of the ground, is not rising faster than `PlantRise`, and is moving over the ground slower than `PlantSpeed` (a foot that touches down still sliding waits for it to slow instead of planting and tearing loose; the ground's own velocity is subtracted, and 0 turns the check off). It lets go above `ReleaseHeight`, when rising faster than `ReleaseRise`, or when the animated foot has drifted `ReleaseDistance` from the pin. Between `PinRadius` and `ReleaseDistance` the pin is dragged along, so a mismatched clip slides rather than overstretches.
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
| `PlantSpeed` | Planting | m/s |
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

- `tests/SA.Motion.Tests/LegTests.cs` (22 tests) and `PlantSpeedTests.cs` (10 tests: a sliding foot waits, zero turns the check off, a moving ground is subtracted, a run touchdown plants once and tears loose without the check), both library builds. The heel stays pinned within 1e-5 m while a toe-up foot rolls, and so does the ball for a heel-up foot. The roll is continuous through flat, with at most 0.3 mm of ankle motion per 0.0005 rad. A scripted heel-strike to toe-off gait keeps the heel, then the ball, within 1 mm while the animation slides 3 mm per frame. Release and replant move at most 1 mm on the switch frame. The pelvis settles at -0.12 m for ground 0.12 m lower and returns once free. Corrupt settings fall back to the defaults, results are bit-identical across runs, and the schema round-trips.
- `packages/com.sa.unity.reactions/Tests/LegIKTests.cs` (Unity, 5 tests): a planted foot stays within 2 mm while the body drifts 12 cm and follows a moving floor; a dragged pin stays dragged for 20 frames; release never teleports; an unticked clip fades the solver out; disabling or the ragdoll leaves the animated pose.
- Cost: 242 ns per leg single-threaded on .NET 10 x64 for the standard step cycle, down from about 687 ns before packet L4 (`docs/handoff/L4.md`; `dotnet run -c Release --project tests/SA.Motion.Benchmarks`). Tilted ground costs about 314 ns per leg. Plain-text profiles: `LegProfileText` (`docs/handoff/L2.md`).
- Kyle walking, running and sprinting in Play mode (2026-11-01, 1/60 s frames, root travelling diagonally across the demo floor at 2.2, 3.6 and 6.2 m/s; the solver inputs of every frame were recorded and replayed in .NET, and the replay matched Unity's plant state on 3358 of 3360 foot frames):
  - Before: walking planted 33 to 36 times per 4 s per foot (a stride is 0.8 s, so 10 is right) and running 28 to 33 (a stride is 0.53 s, so 15 is right). Two causes:
    1. `LegIKSystem` restored each planted foot's pin from the support-local copy it took at touchdown, so every drag of the pin (`PinRadius`) was thrown away next frame. The drift to the old pin grew past `ReleaseDistance`, the foot let go and planted again. Fixed: the support-local copy is refreshed every planted frame. Unity test `ADraggedPinStaysDraggedInsteadOfSnappingBackToWhereItPlanted` fails on the old code at frame 10.
    2. The run clip's foot lands still travelling about 7 m/s over the ground and slows to a standstill within three frames. It planted on the first of them and tore loose on the next. Fixed by `PlantSpeed` (1.5 m/s): a foot only plants once it moves slower than that over the ground (`LegInput.GroundVelocity` is subtracted, so a foot standing on a moving body still plants).
  - After, in Play: walking 9 to 12 events per 4 s with the foot planted 40 to 51 percent of the time; running 13 to 16 events, planted 10 percent (the clip's stance is only two or three frames long, so the planting is brief but clean). Sprinting plants in a few single frames or not at all: the foot is already rising when it slows down. No clip switch adds more than 11 degrees of upper-leg step in a frame against 8 degrees in the animation. The 81 degree step an earlier probe reported does not reproduce at 1/60 s; that probe most likely ran in the unfocused Editor, where one frame lasted 0.33 s and covered twenty normal ones.
  - The run and sprint clips sink the sole 4 to 5 cm into the floor mid-stance; planting on that does not lift the foot more than a brief blip. Raising the ground under those clips is a content job.
  - The demo's in-place Kyle on a stationary root is a treadmill and is not a valid test of planting; drive a travelling root.
  - How: a temporary buffer in `LegIKSystem` took the inputs per frame, a throw-away eval stepped the world at a fixed 1/60 s (see `AGENTS.md`, Traps), and a console project replayed them through `LegRig.Step`. The capture was reverted afterwards.
- Kyle in the Reactions demo (2026-11-01): the subscene instance carried a saved override that put the Hip at (0, 0, 0), so at bake time the skeleton was 1.06 m low and the baked heel and ball were 0.95 m from the ankle. With the override removed and the baker hardened, both feet plant from the first frame and stay planted through idle (frame time fixed at 1/60 s; the unfocused Editor ran at about 1 frame per second, so times are game time). With the corrupt scene put back, the baker estimates a sane shape, both feet still plant, and the Console warns for each foot. Walking and running are covered above.
- The UAL demo (`eng/demo_check.py 30 Assets/ReactionsDemo/ReactionsDemoUAL.unity`, 2026-11-02, unfocused Editor): 7 of 8 pass (stands, shove, strength dip, no explosion, knockdown, pelvis on floor, recovers). The recovery pop check reads 43 degrees in one frame; Kyle passes at 1.7. That run had 0.33 s frames and a fixed-frame rerun was not possible (see `AGENTS.md`, Traps), so whether the UAL pop is real is open.
- Not verified: stairs and slopes in a real scene, the UAL mannequin's planting, toe-bone bending, and quadrupeds.
