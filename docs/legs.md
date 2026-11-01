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
  - After the speed gate, in Play: walking 9 to 12 events per 4 s with the foot planted 40 to 51 percent of the time; running 13 to 16 events, planted 10 percent; sprinting barely planted, because the foot is already rising when it slows down. Replay on the same inputs showed that the default rise limits (`PlantRise` 0.1, `ReleaseRise` 0.6) were what kept it from planting, and that looser limits also keep the foot closer to the animation (walking: largest gap between the solved and the animated ankle 7.5 cm instead of 12.5 cm).
  - Gentle (the default preset) now uses `PlantRise` 1 and `ReleaseRise` 1.5. In Play on Kyle at 1/60 s, per 4 s per foot: walking 9 to 10 events (planted 47 to 52 percent), running 15 (29 to 30 percent), sprinting 15 to 16 (9 to 20 percent): one plant per stride in every gait, which is the right count (walk stride 0.8 s, run 0.53 s, sprint 0.53 s). Standard, Grounded, Agile and Strict keep their old rise limits.
  - At the two clip switches that jump the stance foot (walk to run and sprint to run) the solved ankle trails the animated one by 32 and 49 cm for one frame and closes it within five frames (0.16 s of inertialization), with no jump in the solved pose itself. The other two switches stay under 15 cm.
  - Before the Gentle change, no clip switch added more than 11 degrees of upper-leg step in a frame against 8 degrees in the animation. The 81 degree step an earlier probe reported does not reproduce at 1/60 s; that probe most likely ran in the unfocused Editor, where one frame lasted 0.33 s and covered twenty normal ones.
  - The run and sprint clips sink the sole 4 to 5 cm into the floor mid-stance; planting on that does not lift the foot more than a brief blip. Raising the ground under those clips is a content job.
  - The demo's in-place Kyle on a stationary root is a treadmill and is not a valid test of planting; drive a travelling root.
  - How: a temporary buffer in `LegIKSystem` took the inputs per frame, a throw-away eval stepped the world at a fixed 1/60 s (see `AGENTS.md`, Traps), and a console project replayed them through `LegRig.Step`. The capture was reverted afterwards.
- Kyle in the Reactions demo (2026-11-01): the subscene instance carried a saved override that put the Hip at (0, 0, 0), so at bake time the skeleton was 1.06 m low and the baked heel and ball were 0.95 m from the ankle. With the override removed and the baker hardened, both feet plant from the first frame and stay planted through idle (frame time fixed at 1/60 s; the unfocused Editor ran at about 1 frame per second, so times are game time). With the corrupt scene put back, the baker estimates a sane shape, both feet still plant, and the Console warns for each foot. Walking and running are covered above.
- The Reactions demos with `eng/demo_check.py 30 <scene> --stepped` (2026-11-02, the world stepped by hand at exactly 1/60 s, which the throttled Editor cannot do on its own): Kyle and the UAL mannequin both pass all 8 checks, with a recovery pop of 2.2 and 2.4 degrees in one frame. Run unstepped in the throttled Editor (0.33 s frames) the UAL pop reads 43 degrees, which is the frame time and not a pop.
- A tilted floor (2026-11-02): the demo's Floor plane was tilted 7.72 degrees about Z and lowered 9 cm, while the physics ground was a separate flat box, so characters stood on a floor nobody could see. `ReactionsDemo` now builds the physics ground from the Floor object's position, tilt and size (field `Floor`, found by the name Floor when empty) and sets each character's root height to the plane's height at its position when it first appears. In Play on Kyle, at idle both feet planted with the anchor exactly on the plane (gap 0.0000 m) and the anchor normal equal to the plane's normal (-0.134, 0.991, 0); the Game view shows both characters standing on the plane. Walking and running up the 7.7 degree incline (1/60 s steps, root height following the plane): all 834 planted foot-frames had the anchor on the plane to 0.1 mm, walking 9 events per 4 s per foot (planted 47 to 60 percent), running 11 to 12 events in the part of the run that was still on the 20 m plane. `demo_check.py --stepped` on the tilted scene: 8 of 8 pass.
- A 12 cm step (2026-11-02): in Play, with the physics floor flattened and a 2 m long, 12 cm high box added in memory, Kyle walked at 2.2 m/s up onto the step and off it (1/60 s steps). All 250 planted foot-frames that were clear of the edges had the anchor on the surface: 198 on the floor and 52 on the step top at exactly 0.12 m, largest gap 0.0000 m. The 34 frames near an edge were anchored on either the floor or the step top, never between. Before the fix this showed a real defect: the solver sampled one ground point per foot (the heel-to-ball pivot), so a foot with its toe over the step edge rested on the floor and the toe sank into the step.
- The UAL mannequin (2026-11-02; the UAL demo's clips are baked in place, and UAL faces +z). Its solver inputs were recorded in Play and replayed in .NET (0 mismatches of 3000 foot frames), and the replay can add a travelling root to the recorded positions. This found a bug in the clip bake, not in the solver: Unity's humanoid clips move the Animator's root object up and down (the body bounce) instead of the hips, and `RigBuilder.BuildClip` only read the bones, so the bounce was lost. Baked ankle heights were wrong by up to 4 cm in a stride (walk 4 cm too low, jog 4 cm too high, sprint right), which made the walk sink and the jog hover. `BuildClip` now adds the root object's vertical movement to the top-level bones, and the baked ankle heights equal Unity's own sampling frame for frame (test `ARootObjectBouncedByTheClipLiftsTheBakedTopBones`; it fails on the old bake with 1.0 instead of 1.25). After the fix, in Play on the UAL mannequin with a ground big enough for the whole run (the demo's physics ground is only as big as its visible Floor, 20 m in the UAL scene; an earlier capture ran off the end of it and wrongly showed the sprint never planting): the walk plants 5 to 6 times per 4 s per foot and stays planted 70 percent of the time; the jog, at a root speed of 6.1 m/s, plants once per stride (9 events per 4 s per foot for 4 to 5 strides) and stays planted 7 to 10 percent of the time, because its foot slides for about six frames after touching down and plants only for the last frames, and that count does not change for root speeds from 5.1 to 7.1 m/s; the sprint plants once per stride (10 to 13 events per 4 s, 19 to 26 percent planted) at a root speed of 2 to 3 m/s and in about 1 percent of frames at 4 to 7 m/s. There is no root speed at which its foot stands still: the mean horizontal speed of the contact point while the sole is within 3 cm of the ground is 5.98 m/s at a root speed of 0, falls to 4.03 m/s at 6 to 7 m/s and rises again, so the sprint's ground contact is a skim, not a stance, and Unity records no root travel for these clips (`averageSpeed` is about 0), so no authored speed exists to match. The plants at 2 to 3 m/s are the few frames where the contact slows below `PlantSpeed`. A parameter sweep in the replay (`PlantSpeed` up to 12, `PinRadius` up to 0.3, `PlantRise` up to 5, `PlantHeight` up to 0.1) did not make the jog plant more than once per stride, so no preset was added: the stance of those clips is short and the default already plants once per stride when the speeds match.
- Heel and ball against a real step, every frame (2026-11-02): Kyle walked up onto a 12 cm step and off it at 2.2 m/s in Play (physics floor flattened and a 2 m long box added in memory); for every planted foot-frame the world position of the solver's heel and ball points (from the post-solver bone chain, not `LocalToWorld`) was compared with the surface under it. More than 10 frames after a plant started (0.16 s, the blend time), 267 of 270 points were within 1 cm of the surface or above it; the 3 deeper points sit at a step edge (a foot that plants with its heel on the floor and its toe over the step; the deepest was 11 cm). In the first 10 frames after a plant, 167 of 292 points were more than 1 cm below the surface (up to 12 cm at the step): that is the inertialized blend from the clip's own pose, which sinks the sole 4 to 5 cm in this clip, to the planted pose. On screen, one frame near the leading edge showed no visible sinking.
- Two ground samples per foot (2026-11-02): `LegIKSystem` now casts a ray at the heel point and one at the ball point and rests the foot on the higher hit, so a foot with its toe over a step edge stands on the step. Same 12 cm step test, steady planted frames (more than 10 frames after the plant): 280 heel and ball points, the deepest 0.9 mm below the surface and none deeper than 1 cm (before: 3 of 270 deeper than 1 cm, up to 11 cm); in the first 10 frames the deepest fell from 12 cm to 6.6 cm. Up the 7.7 degree incline the anchors are still on the plane to 0.1 mm (787 planted foot-frames), walking 10 events and running 11 to 12 per 4 s per foot as before, and the demo checks pass 8 of 8 (recovery pop 2.6 degrees). Unity test `ATiltedFootWithItsBallOverAStepRestsOnTheStepNotTheFloorUnderItsHeel` fails on the single-ray code (anchor at 0.0 instead of 0.1). Cost, from the explicit Unity test `LegIKCostTests` (`eng/unity.sh test SA.Reactions.Tests --include_explicit true`, 1,000 characters with one leg each on a floor with one body, Burst job, this machine, two runs each): 111 and 112 ns per leg with one ray, 123 and 117 ns per leg with two, about 5 to 10 percent more; a world with many bodies would make each ray dearer.
- Plant blend length (2026-11-02, replay, `BlendSeconds` with everything else at Gentle; sink is the depth of the lower of heel and ball below the ground in the first 10 planted frames, pop is the largest one-frame ankle move beyond what the animation moves): Kyle's clips (which sink the sole 4 to 5 cm themselves) at 0.16 s: mean sink 2.6 cm, pop 3.4 cm; 0.12 s: 2.2 cm, 7.1 cm; 0.08 s: 1.6 cm, 12.5 cm; 0.04 s: 1.0 cm, 23.1 cm. The UAL mannequin sinks 0.1 to 0.2 cm on average at every length (pop 2.3 to 5.4 cm). A shorter blend trades sink for pops, so Gentle keeps 0.16 s; the remaining sink is the clip's own penetration being blended out.
- Not verified: toe-bone bending, and quadrupeds.
