# What we are building and how to use it

## Goals, in priority order

1. **A clean workflow first.** Content is authored and baked (the game is made with timeline baking through `tl`). At runtime you name a clip and it plays. There is no state machine and no Mecanim.
2. **No blending between clips.** A switch is a cut, and inertialization removes the pop (the Gears of War technique). Only one clip is sampled per character, which is also the cheapest possible evaluation.
3. **Leave Unity cleanly.** Logic lives in portable .NET (`src/`, net10.0 first, netstandard2.1 as backup). Unity packages only convert, schedule and call. Moving to another engine means rewriting the adapters, not the logic.
4. **Modular.** Every motion and every feature is one enableable component with all its configuration inside, one authoring component, and one baker. Add only what a character needs, and delete what it does not.
5. **Very high performance.** Data-oriented, allocation-free, Burst in Unity today and the CoreCLR JIT tomorrow, with every cost measured.
6. **Later.** GTA IV style physical reactions (active ragdoll, balance, stepping) are parked until the workflow is right. The balance and reaction code in `src/SA.Balance` and `src/SA.ReactionControl` stays as it is.

## Layers

| Layer | Where | What |
|---|---|---|
| Portable logic | `src/SA.Motion`, `src/SA.Animation` | Inertialization, the leg solver, motion selection, phase math; next is the whole pose evaluator (packets A1 and A2) |
| Unity animation | `/home/i/ruk2/Packages/com.sa.unity` | Clip baking, CPU and GPU evaluation, motion components and `MotionSystem` |
| Unity features | `packages/com.sa.unity.reactions`, `com.sa.unity.ik` | `LegIK`, ragdoll, look-at and reach |
| Timeline | `tl` (your repo) | Designer-authored timelines baked into deterministic data; the animation tracks are the next step |

## Setting up a character in Unity

1. Select the model and run **Tools > SA > Set Up Character**. It finds the bones, the Avatar and the clips, converts the skinned meshes and adds **Rig Authoring**.
2. Add the motions it needs with the **Add Single / 1D / 2D / 8-way** buttons on Rig Authoring. Each one is its own component; dropping a clip into a slot is enough, because the clip joins the character's animations automatically.

| Component | Plays | You write at runtime |
|---|---|---|
| Single Motion Authoring | one clip | `SingleMotion.Clip`, or `Time` when **Time Driven** is on |
| Linear Motion Authoring | the clip whose threshold is nearest a value (1D, for example speed) | `LinearMotion.Value` |
| Planar Motion Authoring | the clip whose point is nearest a 2D value | `PlanarMotion.Value` |
| Directional Motion Authoring | one of eight heading clips, or idle (8-way strafing) | `DirectionalMotion.Velocity` (local, x right, y forward) |

   Any Humanoid clip plays on any Humanoid character: Unity's own retargeting runs when the clip is baked, so a clip from another pack or another skeleton just works. A clip that cannot move the character (a Generic clip on a Humanoid, a Humanoid clip on a Generic rig, or one whose bones are named differently) is called out on Rig Authoring above **Resolved animations** and in the bake log, instead of silently freezing the pose.
3. Tick **Inertialize** on Rig Authoring so switches are smooth. Without it a switch falls back to a cross-fade of `Transition Seconds`.
4. Tick **Plant feet on ground** on Rig Authoring for planted, rolling feet (the Gentle preset). **Customize planting** adds Leg IK Authoring for the preset, the ground layers, and the heel and ball. The floor needs a collider inside a SubScene. In Play mode the Console says "feet are planted" and green crosses in the Scene view mark each planted foot (**Tools > SA > Show Leg IK Contacts**); if no foot has planted after 3 seconds the Console says why. Untick **Foot IK** per clip under **Resolved animations** for flips, jumps and other airborne clips. `docs/legs.md` explains every setting.

## Why clips are baked as sampled poses (and what Rukhanka does)

Rukhanka (`Packages/com.rukhanka.animation`, the reference) bakes a clip as keyframe curves keyed by bone-name hash, or by muscle for Humanoid clips, cached on disk per clip and avatar. A muscle curve is mapped onto the target avatar's bones while the animation runs, so one baked clip serves every humanoid and nothing is baked per character. It does not warn when a clip matches no bone either.

Here each clip is sampled at `Sample Rate` (30 Hz) on a copy of the rig, through Unity's own retargeting, once per clip and rig. Evaluating a sample is a lerp and a transition is an inertialized cut, so the runtime stays tiny. The price is memory per rig and clip library: UAL1's 43 clips (66 s, 66 skeleton bones) are about 5.2 MB sampled (2,051 frames x 66 bones x 40 bytes, from the blob layout), against about 0.9 MB as authored keyframes (48,350 keys at an assumed 20 bytes each, an estimate; Rukhanka's real format was not measured). Bake time is not a cost: `RigBuilder.BuildClip` took 0.19 s for all 43 clips on that rig (4 ms on average, 46 ms for the slowest), measured in the Editor on 2026-11-02.

Decision: keep the sampled bake, and do not add Rukhanka's disk cache. Baking is already about 4 ms per clip, and Unity caches a SubScene's baked result itself. The sampled bake is verified to retarget a Humanoid clip from another pack (UAL2 on UAL1), and replacing it with curve evaluation plus muscle retargeting would be a rewrite of the evaluator for no change a designer can see today. Revisit it only if memory per rig becomes a problem.

## Rules the runtime follows

- **One motion drives the character.** Among the enabled motions, the highest `Playback.Priority` wins; on a tie the more specific one wins (directional, planar, linear, then single). Disabling the winner hands control to the next. With nothing enabled, the last pose is left alone, so your own code can drive `ClipState` directly.
- **A switch is a cut.** When the chosen clip changes, the old clip is removed and an inertialized transition starts. Asking for the same clip again changes nothing, so you can write the same request every frame.
- **Hysteresis.** Linear, planar and directional motions keep their clip until the value is clearly closer to another one (`Hysteresis`, `AngleHysteresis`, `SpeedHysteresis`), so a value jittering at a boundary never flickers.
- **Gait phase is kept.** Looping motion switches start the new clip at the old clip's phase, so a walk to run switch does not restart the stride.
- **Speed matching.** With **Match Rate** on, the playback rate is the motion's speed divided by the clip's **Reference Speed**, clamped to the rate limits, so feet slide less between clips.
- **Exact time.** A time-driven Single Motion shows the clip at `Time` seconds and never advances it. That is how a timeline scrubs.

## Driving it from tl

Planned, not built yet. A tl animation track writes a `SingleMotion` (clip and time) or enables a motion and writes its value. A foot IK track writes `LegIK.Weight`. Because everything is plain data with idempotent requests, a timeline can set the whole state every frame and the same frame always gives the same pose.

## Verified

- Portable: `SA.Motion.Tests` 306 and `SA.Animation.Tests` 126, each run against both the net10.0 and netstandard2.1 builds; `eng/verify.sh --no-unity` passes 12 of 12. `SA.Animation` now also holds the portable pose evaluator (A1) and playback commands (A2); Unity still runs its own copy until an adapter packet switches it over.
- Unity: `SA.Tests` 57 (including `MotionTests`: one-clip cuts with inertialization, hysteresis, priority and disabling, exact timeline time, 8-way with idle and speed matching), `SA.Reactions.Tests` 29, `SA.Motion.Unity.Tests` 3, `SA.IK.Tests` 3.
- Attaching a clip from another pack (2026-11-02): a Single Motion on the UAL1 mannequin with a UAL2 clip it does not own, built from the model with Set Up Character, played in Play mode (the clip's phase advanced and looped, the mannequin moved on screen). A Generic Ellen clip on the same Humanoid mannequin froze the pose with no message; `ClipCompatibility` now reports that, tested by `ClipCompatibilityTests`. The inspector check was run through the same functions, not seen on screen. The baker's warning came out as a `[Worker0]` line in `Logs/Editor.log` (the CLI's `console get` does not show worker lines), the same channel as the Kyle bake warning.
- Not verified: Play mode on a real character with these components, the authoring components in a real prefab, and any tl integration.
