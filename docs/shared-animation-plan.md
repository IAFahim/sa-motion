# Compact SA: shared animation, ECS timelines, inertialization and optional proportions

Implementation specification and foundation handoff, 2026-11-03. The portable shared-animation foundation and optional runtime-proportions addon are implemented and tested. [Section 22](#22-implemented-foundation-and-next-task) records CF1; [section 23](#23-optional-runtime-proportions-implemented-addon-and-exact-handoff) gives the current CF2 source, algorithms and next tasks. The real mesh/physics shape adapter and 50/500 pilot remain integration work.

The previous reactions/ragdoll plan is preserved verbatim in [the archive](archive/active-humanoid-reactions-2026-10-31.md). Its physical-animation work remains separate. This plan governs the compact animation library. Existing verification claims are in `/home/i/GitHub/sa-motion/AGENTS.md`, especially “Findings so far.”

**Start here, implementation AI:** read sections 22 and 23 and their linked contracts/handoffs. CF2 contains the original numerical foundation plus later C08/family publication and the optional SA.Shape addon. Check current integration handoffs before selecting a packet; do not repeat a gate that has already passed or overwrite newer C-series work. Reuse the tested algorithms, implement the thin visible integrations, then run the real pilot. Keep the ECS timeline in charge; add no AnimatorController/state-machine/Unity animation parameter machinery.

## 1. Product contract

Build a small, engine-independent animation evaluator. The ECS timeline decides **what plays and at what time**. SA samples, blends, retargets, smooths interruptions, optionally solves contacts, and supplies skinning transforms.

```text
OFFLINE
source clips + source Avatar/rig -> canonical family bake -> shared compressed clip
target rig + approved T-pose    -> retarget table          -> shared rig asset
mesh bones + existing bindposes -> skin binding           -> mesh asset

RUNTIME
ECS timeline -> explicit clip/time/weight/mask inputs
             -> decompress and blend canonical local poses
             -> canonical hierarchy -> target rig table
             -> target-local inertialization
             -> target hierarchy -> optional IK -> skin palette
```

“Generic” means that runtime animation does not depend on Unity Humanoid, an Avatar, an Animator, or an AnimatorController. Unity Humanoid remains an **offline conversion tool**. A runtime family identifier describes data compatibility, not an engine import mode.

Binding decisions:

1. Humanoid first; separately versioned quadruped and other families later. `Unique:<skeleton-content-hash>` handles a creature whose clips already fit it. Never assume all animals share one quadruped anatomy.
2. Bake each source motion once **per compatible family and bake profile**. Never bake it once per destination character. Different families can require different baked data.
3. Keep the existing meshes, bone weights, hierarchy, and inverse bind matrices. Retargeting controls the existing skeleton; it does not re-rig 40,000 meshes.
4. The timeline owns selection, priority, scheduling, and sample time. SA owns pose evaluation and smoothing. No Unity state machines, transitions graph, Animator parameters, controller parser, or runtime muscle evaluator in the new core.
5. A transition switches to the incoming evaluation immediately and decays a pose offset. The outgoing clip is no longer sampled after the switch. Simultaneous clips are still supported for deliberate blends and layers.
6. Runtime data is immutable and shared. Per-character mutable state consists of pose scratch, transition history, contact state, handles, and clocks supplied by the host.
7. CPU/Burst is the first complete backend. GPU work follows correctness and actual measurement. A smaller library does not require recreating every Rukhanka subsystem.
8. Unsupported input produces a named diagnostic before publication or binding. No silently frozen animation, guessed bone roll, or unannounced slower backend.

God of War-style interruption quality is a visual target. No claim is made that these original kernels reproduce that game's proprietary implementation.

## 2. Existing work to reuse and inspect

The following are observations from files read for this plan, not new benchmark results.

| Existing location | What it supplies | Decision |
|---|---|---|
| `/home/i/GitHub/sa-motion/src/SA.Animation` | Portable pose math, sampling, hierarchy, layers, playback, phase, and motion selection | Extend this core; avoid another animation math library |
| `src/SA.Motion/Numerics` in that repository | `Float3`, `Quaternion`, `Pose`, explicit Hamilton multiplication | Reuse the existing numeric types |
| `src/SA.Motion/Inertialization` | Finite decay, pose history, interruption/request handling | Preserve existing compatibility behavior; add the velocity-aware path in section 10 |
| `src/SA.Motion/Kinematics/TwoBoneIK.cs` and `Legs/` | Two-bone geometry and foot planting | Reuse after verifying delta/frame semantics |
| `src/SA.Humanoid/Mapping` | Fifteen physical/anatomical roles and mapping diagnostics | Useful offline suggestions; its fifteen roles are not the canonical animation skeleton |
| `Packages/com.sa.unity/Editor/RigBuilder.cs` | Skeleton collection, clone sampling, cache keys, masks, skins | Reuse proven asset discovery and skin conversion; replace character-specific canonical clip baking |
| `Packages/com.sa.unity/Runtime/Data/Rig.cs` | Current `Clip.RigId`, `BlobArray<BonePose>`, rig and mask blobs | These currently bind clips to a target rig; new canonical clips reference `FamilyHash` |
| `Packages/com.sa.unity/Runtime/Core/Inertial.cs` and `TransitionPlayback.cs` | Current Unity bridge and idempotent requests | Reuse integration lessons; timeline mode must not also advance playback |
| `Packages/com.sa.unity/Runtime/Resources/SA/Animation.compute` | Existing sample/blend/hierarchy/skin GPU path | Its sampling stride currently uses target `BoneCount`; changing only an ID would be incorrect |
| `Packages/com.rukhanka.animation/Rukhanka.Runtime/AnimationEngine` | Established sampling, layered application, root-loop handling, and skinning reference | Read as a behavior/reference package; leave its files untouched |
| Rukhanka `Rukhanka.Hybrid/AnimationClip` and `Rig` | Source import and humanoid conversion reference | Inspect when designing conformance fixtures; do not pull the controller subsystem into SA |

The reference package's humanoid path evaluates muscle ranges and pre/post rotations. This plan intentionally spends that engine-specific work offline and stores ordinary canonical transforms. Do not transplant its runtime muscle evaluator and call it compact generic retargeting. Reuse SA's owned portable code; reference-package code reuse must follow its actual redistribution terms.

The local `tl` README describes separate `Apply` and `Advance` operations; the Unity workspace manifest inspected here does not include a timeline package. Therefore section 3 defines an adapter contract rather than claiming a particular installed timeline API works in this scene. Bind the real timeline at integration time without changing the animation kernels.

## 3. Timeline boundary: the entire required control surface

Implement ordinary unmanaged structs and functions over caller-owned spans. The names below are new proposed APIs, not claims about APIs already available.

| Input | Fields and meaning |
|---|---|
| `AnimationFrame` | `Tick:u64`, `RigEpoch:u32`, `Continuity:u64`, `SimulationSeconds:f64`, `DeltaSeconds:f32`, `TransitionSeconds:f32`, `MaxHistoryGapSeconds:f32`, `Mode:u8` |
| `ClipContribution` | Generational `ClipHandle`, `SampleSeconds:f64`, `Weight:f32`, `Layer:u16`, `MaskIndex:u16`; additive mode belongs to the layer |
| `LayerInput` | Explicit order, override/additive mode, contribution range, overall weight; masks are family-bone weights |
| `RootSource` | Disabled or one designated contribution; gameplay may own root motion instead |
| Optional constraints | Plain target positions/orientations, pole hints, weights, contact snapshots, and named coordinate spaces |

`SimulationSeconds` is the monotone simulation clock during Advance; `SampleSeconds` is the timeline-owned clip clock and may move backward. `DeltaSeconds` is positive elapsed simulation time used by filters, even when clip sample time runs backward. `Tick` identifies a simulation evaluation, not the clip time. Views, spans, ECS buffers, and callbacks never become fields of a serialized runtime struct. The host supplies flat views as function arguments.

Start with explicit bind-time capacities: 8 contributions and 4 layers per character. These are defaults, not limits embedded in the file format. A host may allocate larger declared capacities. Exceeding a capacity returns `CapacityExceeded` and leaves the previous valid output; do not choose clips to discard silently.

### 3.1 Ownership and ordering

```text
timeline evaluates active tracks into one AnimationFrame and contribution array
host resolves and pins all referenced clip segments
SA evaluates exactly once for Tick
host commits pose, root intent, and diagnostics
timeline advances exactly once under its own scheduler
```

Timeline-driven rigs are excluded from `PlaybackSystem`/`Playback.Step`. The timeline must not write an absolute sample time and then have SA advance it again. Existing convenience `Playback` and motion selectors remain optional callers for other users; they are not dependencies of the timeline evaluator.

`Continuity` changes on a semantic cut, restart, rig swap, or explicit animation discontinuity. It stays constant when a blend weight, aim value, or continuously advancing sample time changes. Use a full identifier owned by the timeline/host, not a 32-bit hash of clip names. `RigEpoch` changes on pooled-instance reset or rig/table replacement.

At binding, keep an exact signature of ordered clip IDs, layer modes, and masks. Reusing an ID with a different signature is `ContinuityMisuse`; the previous pose remains valid. A boundary that changes membership issues a new ID. Continuous weights are excluded from this signature. The same ID every tick must never restart a transition.

### 3.2 Temporal behavior

| Situation | Required result |
|---|---|
| First evaluation or new rig epoch | Evaluate destination; initialize history; no invented outgoing velocity |
| Advance, same continuity | Evaluate current destination; continue existing decay |
| New continuity with all inputs resident | Capture current corrected pose and velocity; initialize a new offset at elapsed zero |
| Second interruption before completion | Capture the already corrected output; replace the old offset; never stack transition chains |
| Duplicate tick with identical inputs | Return the previously committed result; do not advance history, contacts, or root delta |
| Duplicate tick with different inputs | `TickConflict`; caller submits a new evaluation identity or seek |
| Pause | Reuse committed pose; do not create history with a zero interval |
| Seek/scrub | Evaluate destination at exact requested time, clear offsets and contacts, emit zero root delta |
| Reverse playback in running simulation | Sample backward; smoothing still advances with positive simulation delta |
| Rollback | Restore a saved complete state and replay ticks, or explicitly use Seek; a negative delta is not rollback |
| Teleport | Reset root/contact history; pose smoothing is reset unless the host explicitly provides a new boundary |
| Missing new clip/segment | Keep committed pose; return `NeedsData`; do not consume the new continuity ID |
| Missing data for an already running contribution | Hold pose, clear contact locks/root deltas, invalidate velocity history; resume with a new smoothing boundary once resident |

Holding while data streams does not pause timeline time. When the clip arrives, evaluate its **current** requested time and smooth into that pose. The residency change creates a new internal boundary revision; it does not restart the timeline clip. Track whether the previous committed output is usable, so a first-ever missing clip displays the approved calibration pose with a visible diagnostic.

An inertialized pose depends on history; scrubbing is deliberately stateless. To reproduce the smoothed pose at an arbitrary historical tick, restore a checkpoint and replay. Do not promise random-access history reconstruction from clip time alone.

### 3.3 Runtime entry points and failures

Keep a short surface: `ValidateBinding`, `RequiredSegments`, `EvaluateCanonical`, `Retarget`, `SmoothPose`, `SolveConstraints`, `BuildSkinPalette`. A thin `EvaluateFrame` composes these. The I/O owner loads bytes and publishes validated views; kernels never perform file I/O or wait for data.

Failures are plain codes plus asset/bone/track indices: `InvalidFrame`, `InvalidHandle`, `FamilyMismatch`, `NeedsData`, `CapacityExceeded`, `BudgetExceeded`, `TimeOutOfRange`, `TickConflict`, `ContinuityMisuse`, `UnsupportedScale`, `UnsupportedTranslationChannel`, `InvalidTable`. The adapter formats human messages. Preflight the complete frame before writing committed output. Scratch may be overwritten on failure; committed pose and state must remain coherent.

The implemented evaluator sets only a transient `PoseFrameState.Waiting` flag on NeedsData, preserving committed Tick/Continuity/output/history and forcing a fresh boundary on residency recovery. Duplicate-tick idempotence applies to successful commits. An attempt that returns NeedsData does not consume its tick or continuity ID, so the host may retry it after residency changes. While holding, the adapter reuses only the last pose: it emits identity root delta and disables live contact locking. The next successful evaluation detects the gap from last-valid simulation time and reseeds history/contacts; it does not reuse stale velocities or root deltas.

### 3.4 Concrete kernel composition and scratch layout

Use these function shapes when freezing C00's declarations; names can fit existing SA conventions, but the ownership and sequencing are fixed:

```text
LocateSamples(clipMetadata, sampleSeconds) -> first, second, alpha, segmentIndex
DecodePose(validatedTracks, validatedBlocks, segmentBytes, first, second, alpha,
           familyReference, outputLocalSpan) -> Result
BlendCanonical(contributions, layers, decodedLocalSpan, additiveReferenceSpan,
               flatMasks, familyReference, canonicalLocalOutput) -> Result
Retarget(canonicalParents, canonicalLocal, rows, rho,
         canonicalModelScratch, targetModelScratch, targetLocalOutput) -> Result
BeginDecay(correctedOutput, outgoingVelocity, incomingRaw, incomingVelocity,
           duration, perBoneDecayState) -> Result
ApplyDecay(incomingRaw, perBoneDecayState, elapsed, duration, correctedOutput)
BuildModel(targetParents, correctedLocal, modelOutput)
BuildSkinPalette(modelPose, paletteToRig, originalBindMatrices,
                 meshFromModel, paletteOutput) -> Result
```

Do not create a `Span<ReadOnlySpan<T>>` or a `Span` of ref structs: that is not a C# 9-compatible multi-clip representation. Decode each resident contribution into caller-owned flat scratch, indexed `contributionIndex*canonicalBoneCount+bone`. Additive references use the same flat indexing or validated immutable offsets. Layer records hold integer ranges, and masks use integer bases into one borrowed flat span. DecodePose borrows a **single** segment view for its call; the engine host resolves handles and calls it for each contribution before blending. This keeps the math safe/span-based without inventing a callback interface or copying serialized blobs into managed objects.

Required per-character scratch is K*C decoded local poses, C canonical locals/models, T raw target locals/models, T corrected locals/models, and a T-bone decay/history slice, where K is declared contribution capacity, C canonical count, T target count. Reuse scratch between nonoverlapping stages after proving lifetimes. Never reuse committed output as speculative scratch.

The frame driver first validates all counts, signatures, handles and resident segments. It prepares incoming boundary velocities when required, runs the raw pipeline into scratch, applies/reset/continues decay according to section 3.2, computes root intent and constraints, then commits output and mutable state once. Neighboring-time boundary samples reuse scratch sequentially and never commit. A failure before commit returns the previous pose plus a diagnostic. C00 records the exact public widths/alignment; it does not choose a different architecture.

## 4. Source layout and a compact dependency boundary

All paths in `src/` below belong to `/home/i/GitHub/sa-motion`. Unity package paths belong to the existing separate `com.sa.unity` Git repository.

```text
src/SA.Motion/                 existing numerics, IK, finite decay
src/SA.Animation/
  Data/                        family/table views, inputs, result codes
  Pose/                        existing sampling, blend, hierarchy
  Retarget/                    reference solver and validated fast rows
  Streaming/                   segment addressing, validated codec decode
  Timeline/                    input validation and frame composition, no timeline engine
src/SA.Animation.Content/      optional offline/load-time project
                               audit DTOs, mapping, hashes, writer, compressor, validation
tests/SA.Animation.Tests/      numerical and hostile-input fixtures
tests/SA.Animation.Content.Tests/
Packages/com.sa.unity/Editor/  preview scene baker, map UI, library import, inspectors
Packages/com.sa.unity/Runtime/ Unity ECS scheduling, buffer lifetime, renderer bridge
```

`SA.Animation.Content` may allocate during import, compression, loading, and reporting. It must not become a dependency of the hot core. No JSON, cryptography provider, Unity assembly, reflection, registry, or plugin interface is required to evaluate a pose. Existing library targets are netstandard2.1 and .NET 10; preserve the repository's C# 9-compatible neutral source conventions and target checks.

Use `StructLayout.Sequential` for in-memory unmanaged structs; use explicit field serialization for disk. In-memory ABI and asset wire layout are separate. Methods receive state explicitly and have defined invalid-input behavior. No per-frame allocations, LINQ, reflection, callbacks, changing static state, or asset-name lookups in a pose job. Follow the repository's no-comments rule in production code; explanations belong in docs.

Do not create a general animation graph framework, a controller language, a resource manager abstraction, or a new ECS. Do not add a project for each small algorithm. The host owns asynchronous loading and native memory; SA reports the exact segments it needs.

## 5. Audit first: determine the real workload

Implement an Editor action **Tools/SA/Animation Library/Audit**. It takes visible source folders and an output location. It visits imported model/prefab assets and clip sub-assets, not live posed scene instances. Never count `.meta` files or preview clips as distinct content.

For each character record source GUID/subasset ID, dependency hash, source file hash, every renderer's ordered bones and bind matrices, complete ancestor closure, exact relative bone paths, parent indices, imported local bind transforms, Avatar validity/type, unit/root conversion, and scale diagnostics. Include unskinned ancestors that affect the skin. Report duplicate names with full paths.

An imported default transform is not automatically evidence of the skin's bind pose. Where a bind reconstruction is required, derive each skinned bone's bind model matrix as `ModelFromMeshAtBind * inverse(originalBindMatrix)`, with unskinned ancestors coming from validated source hierarchy data. Compare that evidence with the imported reference and across applicable renderer bindings. Preserve differing per-mesh bind data; diagnose incoherent/missing evidence instead of replacing it with a currently displayed or calibrated pose.

Use deterministic parent-before-child ordering, siblings sorted by full ordinal path. Duplicate sibling names require an explicit stable disambiguation in the importer; a name-only path is ambiguous. Detect cycles and invalid parents before forward kinematics.

Maintain separate identities:

| Identity | Included data | Purpose |
|---|---|---|
| `TopologyKey` | normalized candidate names, semantic role candidates, parent topology | Group likely rigs for review |
| `BindRigHash` | exact paths, parents, f32 bind transforms, root conversion | Share genuinely identical skeleton assets |
| `MappingTemplateHash` | family, topology-to-role mapping, approved roll/calibration rules | Reuse mapping work across a rig type |
| `RetargetHash` | exact family hash, target topology, calibrated transforms, corrections, translation policy, height ratio | Share complete tables only when numerically identical |
| `SkinBindingHash` | rig, renderer transform, ordered palette-to-rig indices, original inverse bind matrices | Handle different meshes/material slots independently |

Identical names/hierarchy do **not** establish identical bone lengths, rest orientations, bind matrices, or table bytes. Proportion variants can share a mapping template while retaining different target offsets and height ratios. Mesh-specific skin bindings stay separate even when tables are shared.

For each animation record clip subasset ID, duration, rate, Humanoid/Generic, source rig/Avatar, importer root/loop/additive/mirror settings, transform bindings, source dependencies, and provenance. A source file with ten clips creates ten bake requests. Group exact source duplicates before doing expensive baking, but perform canonical-payload deduplication afterward too.

Outputs: `characters.jsonl`, `clips.jsonl`, `rig-groups.json`, `audit-summary.json`, plus an Inspector/Console summary: characters, exact rigs, mapping templates, humanoid candidates, creature candidates, unknowns, rejected scales, and source clips. `Unknown` is a valid classification; never guess a family from bone count alone.

The 40,000-character and million-clip figures are design inputs, not measured inventory. No scale claim is complete until this action runs on the real selected content.

## 6. Transform conventions and canonical humanoid v1

### 6.1 Required algebra

Use metres, radians, seconds, f32 pose data, and f64 timeline/sample time. Model space means the character's normalized root space, not world space. Axes are +Y up, +Z forward, +X toward the character's right; anatomical Left has negative X in the reference pose. Numeric cross product is explicitly `cross(X,Y)=Z`.

Quaternions are unit `(x,y,z,w)` Hamilton active rotations. `A * B` applies B then A. A hierarchy composes `Qmodel[b] = Qmodel[parent] * Qlocal[b]`. `Rotate(q,v)` is `q*(v,0)*inverse(q)`. Required convention test: a +90-degree rotation about +Y maps `(0,0,1)` to `(1,0,0)`.

For unit scale, a rigid transform is `(Q,P)`:

```text
Compose(A,B) = (A.Q * B.Q, A.P + Rotate(A.Q,B.P))
Inverse(A)   = (inverse(A.Q), Rotate(inverse(A.Q),-A.P))
```

Normalize valid rotations; reject nonfinite data offline. Quaternion distance is `2*acos(clamp(abs(dot(a,b)),0,1))`. Treat q and -q as the same orientation. For deterministic identity bytes choose W positive, and lexicographic X/Y/Z sign when W is zero. During interpolation align neighboring signs locally rather than forcing W positive every frame.

Normalize source units and proper root rotation once offline, recording the conversion. V1 accepts unit bone scale plus a positive uniform root unit conversion and positive uniform runtime instance scale. Reject mirrored, sheared, nonuniform, and animated bone scales with the exact bone path. Supporting these later requires matrix-aware retargeting and skin validation; quaternion corrections cannot encode reflection. Unity's import coordinate conversion precedes this check.

### 6.2 Family asset: a reproducible draft, then an immutable version

Create `families/humanoid-v1.safamily`, a documented UTF-8 JSON descriptor. Its fixed bone order is below; these are SA indices, **not** Unity enum values. Every canonical bone has identity reference rotation and unit scale. Positions listed are model-space reference positions; derive local translation by subtracting the parent's position. The synthetic `Root` is motion separation, never an imported deform bone.

| Index | Bone | Parent | Model reference position in metres |
|---|---|---|---|
| 0 | Root | -1 | (0,0,0) |
| 1 | Hips | 0 | (0,1.00,0) |
| 2 | Spine | 1 | (0,1.12,0) |
| 3 | Chest | 2 | (0,1.32,0) |
| 4 | UpperChest | 3 | (0,1.46,0) |
| 5 | Neck | 4 | (0,1.56,0) |
| 6 | Head | 5 | (0,1.68,0) |
| 7 | Jaw | 6 | (0,1.63,0.075) |
| 8 | LeftEye | 6 | (-0.032,1.72,0.075) |
| 9 | RightEye | 6 | (0.032,1.72,0.075) |
| 10 | LeftShoulder | 4 | (-0.10,1.46,0) |
| 11 | LeftUpperArm | 10 | (-0.20,1.46,0) |
| 12 | LeftLowerArm | 11 | (-0.50,1.46,0) |
| 13 | LeftHand | 12 | (-0.75,1.46,0) |
| 14–28 | Left finger joints | recipe below | recipe below |
| 29 | RightShoulder | 4 | (0.10,1.46,0) |
| 30 | RightUpperArm | 29 | (0.20,1.46,0) |
| 31 | RightLowerArm | 30 | (0.50,1.46,0) |
| 32 | RightHand | 31 | (0.75,1.46,0) |
| 33–47 | Right finger joints | recipe below | recipe below |
| 48 | LeftUpperLeg | 1 | (-0.095,0.95,0) |
| 49 | LeftLowerLeg | 48 | (-0.095,0.53,0.015) |
| 50 | LeftFoot | 49 | (-0.095,0.10,0) |
| 51 | LeftToes | 50 | (-0.095,0.035,0.16) |
| 52 | RightUpperLeg | 1 | (0.095,0.95,0) |
| 53 | RightLowerLeg | 52 | (0.095,0.53,0.015) |
| 54 | RightFoot | 53 | (0.095,0.10,0) |
| 55 | RightToes | 54 | (0.095,0.035,0.16) |

Finger recipe: ordered Thumb, Index, Middle, Ring, Little; each has Proximal, Intermediate, Distal. Left indices start at 14, right at 33. For finger f and joint j in 0..2, index is `base+3*f+j`; parent is its hand for j=0, otherwise index-1. Let side sign s=-1 for Left, +1 for Right. Add `(s*d[j],0,z[f])` to the hand's model position. Distances d are `(0.035,0.060,0.080)` for Thumb and `(0.045,0.075,0.095)` for other fingers; z offsets are `(0.055,0.025,0,-0.025,-0.045)`. Names follow `LeftIndexProximal`, etc. This is a simple reproducible open-hand reference, not an artistic mesh prescription.

Reference sole markers are `(-0.095,0,0.08)` and `(0.095,0,0.08)`. Canonical hip height Hc is 1.00 m. Store end/contact markers as descriptor data; do not reinterpret `LeftFoot` as a sole point.

Required target mappings: Hips, Spine, Head, both UpperArm/LowerArm/Hand and both UpperLeg/LowerLeg/Foot: fifteen roles. Other canonical bones are present in clips but optional on targets. Missing intermediate bones are handled by the model-space algorithm in section 9, not by copying canonical locals.

Generate the descriptor in C01 and validate a Unity Humanoid Avatar from it in C08. Validate finger orientation, T-pose acceptance, and representative source import there. The final family hash is minted only after that gate. If the geometry needs an edit during the draft, record it; after publication any bone order, hierarchy, reference transform, axis, or semantic change creates a new family version and hash. Never make a silent repair under an existing family hash.

### 6.3 Extensibility

Kernels operate on counts, parents, tracks, and mappings; they contain no Humanoid switch. The humanoid descriptor and Unity importer contain the names. A quadruped family must choose a concrete anatomy and contact roles, with its own reference and bake path. Unity Humanoid cannot serve as a quadruped converter. A unique family stores that original rig's complete reference skeleton and compatible clips; runtime uses identity mapping and the same sampler/blender/skin stages.

## 7. Offline mapping and calibration: the part a name table cannot solve

### 7.1 Suggest a map without hiding ambiguity

Prefer a valid imported Avatar's explicit semantic mapping, then a saved rig template, then alias suggestions. Keep full original paths. Name normalization for suggestions strips namespaces and separators, splits camel case, and recognizes side tokens and known aliases (`pelvis/hips`, `thigh/upperleg`, `calf/shin/lowerleg`, `forearm/lowerarm`). Never infer Left by searching for any letter “l.”

For name suggestions solve a small one-to-one assignment offline. Candidate score: exact approved full path 100, Avatar mapping 95, exact normalized semantic alias 80, explicit side match +15, compatible anatomical ancestry +10. Wrong explicit side and impossible ancestry are forbidden edges; unknown candidate score is zero. Maximize total score with a dummy “unmapped” option using a standard rectangular Hungarian assignment, then validate mapped ancestry and uniqueness. Recompute an alternative with each chosen edge forbidden; a total-score gap under 15 makes that choice ambiguous. Store scores and alternatives in the report. These are initial policy constants, versioned with the mapper, and are not proof of anatomy.

All fifteen required roles, side consistency, ancestor order, finite transforms, valid segment lengths, and the calibration must pass before a rig is approved. Optional missing roles produce explicit warnings. A mesh may be Humanoid-like without being safely automappable; it remains `NeedsReview` until fixed once for that rig template.

### 7.2 Preserve bind pose; author a separate calibration pose

Three different concepts must remain separate:

* **Bind pose:** the pose against which the mesh's original inverse bind matrices were authored.
* **Calibration pose:** the target posed into the family's reference stance, usually T-pose.
* **Animation pose:** the sampled/blended pose for this frame.

A target bound in A-pose must reach its **calibrated T-pose** when the canonical T-pose plays. Requiring it to remain in its A-pose is the wrong correctness test. Never overwrite inverse binds with calibration inverses; that would change the mesh's binding.

Work on a clone in a preview scene. Load a trusted imported Avatar reference or saved calibration first. Do not assume zero Humanoid muscles mean T-pose. Where proposing a geometric calibration, align a segment's current model direction toward its canonical reference direction using a FromTo rotation, then update descendants. Apply only rotations, preserve target segment lengths, and keep unmapped helper locals at their bind values by default.

Bone roll needs a second direction. A line from elbow to wrist cannot tell whether a palm faces up or down. Each ambiguous template therefore needs an approved pole/palm/roll hint or a trusted Avatar frame. A deterministic frame construction, when those hints exist, is:

```text
Frame(u,v):
  x = normalize(u)
  y = normalize(v - x*dot(x,v))
  z = cross(x,y)
  return matrixWithColumns(x,y,z)

QtargetCalibrationModel = quaternion(Frame(U,V) * transpose(Frame(u,v)))
```

Here u,v are the target bone's local reference segment and secondary hint, and U,V are the desired model-space directions. Reject zero/parallel hints. Convert desired model rotation to local using the already calibrated actual parent, then run FK again. Verify actual child directions and visual palms/knees afterward, especially where unskinned helper bones intervene. Geometric suggestions never approve themselves merely because a quaternion is finite.

The mapping Inspector must expose unresolved bones, alternate suggestions, pose/axis preview, and a visible **Approve calibration** action. Persist the approved local calibration rotations and fixed translations for every target bone, original bind locals separately, a source bind hash, authoring revision, height measurement, and original full paths. Approval on an incompatible bind hash is invalidated.

### 7.3 Height and proportion data

In the approved upright calibration pose compute `Ht = hipsY - referenceGroundY`; ground comes from approved sole markers or a visible authoring measurement. Do not use ankle height as ground and do not read scene placement as a bone offset. Require Ht>epsilon and set `rho=Ht/Hc`. Store target limb lengths for IK as well. Warn when height and summed leg lengths strongly disagree; a saved Hip override must fail/review rather than silently corrupt every clip.

Height ratio scales root/hip **displacements**, not target bone lengths or mesh geometry. Two rigs with equal hip height and different arms will still have different hand contacts; section 11 handles that limitation.

## 8. Canonical baker: Unity solves source Humanoid conversion once

### 8.1 Isolation and setup

Build **Tools/SA/Animation Library/Bake** with a visible job report, progress, cancellation, retry, and cache summary. Use an empty preview scene and a canonical skeleton clone. Never sample the owner's open character instance or save a posed prefab. Disable/remove gameplay scripts and constraints on the preview copy; do not invoke AnimationEvents or IK callbacks from arbitrary content.

Canonical Humanoid sources need valid source Avatars and a valid canonical Avatar. A Generic source only shares directly with an exactly compatible canonical skeleton, or uses an explicitly approved offline source-to-family mapping. An arbitrary animal/Generic FBX must not pass by changing its import type blindly.

Use a manual `PlayableGraph`, one `AnimationClipPlayable`, and an `AnimationPlayableOutput` into an editor-only Animator with the canonical Avatar. No AnimatorController is needed. Set AlwaysAnimate, foot IK off and playable IK callbacks off. Restore the pristine reference before each clip, rebind, evaluate time zero, then advance monotonically by the exact sample intervals. Do not both `SetTime(next)` and advance another full interval. Dispose graph/clones in `finally` and cancel without publishing partial assets.

The precise root-motion behavior of this project's Unity 7000.0.0a12 must be established by C08's conformance fixtures before batch baking. The recipe is not an assertion that manual graph root accumulation has already been tested here.

### 8.2 Sampling and separating locomotion

For duration T>0 and requested rate f, set `N=max(2,ceil(T*f)+1)` and `t[i]=T*i/(N-1)`. The actual uniform interval is T/(N-1); do not pretend it is exactly 1/f. Start at 60 Hz for the pilot. A zero-duration pose has one sample. Reject negative/nonfinite duration; require explicit trimming/splitting for the declared asset-size limit.

Record the canonical deform bones' model transforms Gc(t) and the accumulated locomotion root W(t) from the graph. For the chosen root profile, form root-relative bone transforms `Pc(t)=Inverse(W(t))*Gc(t)`. Export local curves by differencing adjacent canonical parents. The synthetic Root pose in these curves is identity; locomotion is stored separately as `R(t)=Inverse(W(0))*W(t)`, so R(0)=identity.

Supported explicit root profiles:

| Profile | W(t) contains | Consequence |
|---|---|---|
| None | Identity | Authored displacement stays in pose; no root delta is emitted |
| Planar | XZ displacement and continuous yaw; Y=0 | Vertical jump/bob remains in Hips; gameplay owns elevation |
| FullRigid | Translation and orientation | Explicit flight/cinematic root ownership is required |

The baker must accumulate root motion monotonically; resetting the preview root each sample loses trajectory. Prove whether the selected API yields applied root transforms or deltas, and the deltas' coordinate frame, using translation-plus-turn fixtures. If accumulating deltas, convert them into the same family frame before composing W. Do not infer the root trajectory from the hips alone: bob, crouch, and locomotion are different signals. Unity's body transform, root transform, and Hips are distinct concepts. [Unity root-motion documentation](https://docs.unity3d.com/6000.0/Documentation/Manual/RootMotion.html)

Keep full terminal samples. Loop root R(T) is the complete cycle displacement even if the pose wraps. Disable accidental importer loop correction in the test fixture and verify the last endpoint; any loop-pose correction actually used becomes part of the bake profile. A large pose seam is reported and requires authoring approval, not an automatic hidden smoothing edit.

Humanoid v1 uses rotation transfer plus Hips translation. Disable optional stretch/translation degrees of freedom in the canonical bake profile and validate that non-Hips local translations stay at the canonical reference within 1e-5 m. If they do not, reject as `UnsupportedTranslationChannel` or explicitly convert that intent into a separately specified contact/constraint track. Do not export moving non-Hips joints and then silently discard them in the target solver. Unique clips can keep their animated local translations through the identity path.

Normalize quaternions, sign-align adjacent samples for interpolation, and preserve a separate additive reference pose. An additive source is first evaluated to canonical absolute poses; deltas are formed relative to its explicit canonical reference. Never apply a delta track as an ordinary pose.

An authored in-place clip may have no usable locomotion trajectory. Preserve that fact; declared desired speed is metadata, not measured root motion. Warn about an entirely frozen result with the source, family, and likely binding/import fix.

### 8.3 Cache, deduplication, and publication

Use SHA-256 for durable identities. The complete dependency key includes source file/dependency content, clip subasset identity and settings, source Avatar/reference, canonical family hash, sample profile, root profile, additive reference, loop/mirror policy, mapper version, baker version, compressor profile, and full Unity editor version. It contains **no destination character or target table**.

Keep three identities distinct:

* `BakeKey`: request/dependencies; answers whether work can be skipped.
* `MotionPayloadHash`: family plus normalized uncompressed tracks, duration, root, reference, contacts, and behavior flags; ignores file names and labels.
* `ArtifactHash`: the actual encoded output bytes and codec version.

Exact canonical payload matches may share motion bytes while keeping labels, source provenance, and gameplay event metadata separate. Similarity fingerprints only propose duplicates; never merge near-matches automatically. Mirrored, trimmed, additive, contact, and root variants must retain their semantics.

Write to a temporary output, validate decode against raw samples, then atomically rename into the content-addressed store and update the catalog last. A cache hit verifies the expected hash and preserves timestamps. A cancellation or crash never makes an incomplete asset look ready. Produce per-source statuses `Cached`, `Baked`, `Duplicate`, `NeedsReview`, `Rejected`, and `Failed`, with exact repair guidance.

## 9. Runtime sampling, blending, and retargeting

### 9.1 Sample and blend in canonical space

For a nonzero duration T, clamp one-shot sample time to [0,T]. For a loop, use `u=t-floor(t/T)*T`, including negative t. Time-to-frame is `f=u*(N-1)/T`, then `i=floor(f)`, `j=min(i+1,N-1)`, `alpha=f-i`. A zero-duration pose always selects sample zero. The terminal one-shot sample selects N-1 exactly.

Decode just those two samples. Interpolate translation linearly and quaternion with shortest-path normalized linear interpolation, matching the existing portable core. Keep the same interpolation in raw reference, codec checks, CPU, and eventual GPU code. Do not compare an nlerp runtime to a slerp reference and attribute the difference to compression.

For each override layer and bone:

```text
w[k] = clampFinitePositive(contributionWeight[k]) * clamp01(mask[k,b])
W = sum(w)
anchor = quaternion of largest-weight sample; stable contribution order breaks ties
qMean = normalize(sum(w[k] * hemisphere(sampleQ[k],anchor)))
pMean = sum(w[k] * sampleP[k]) / W
coverage = clamp01(W) * clamp01(layerWeight)
pose = interpolate(previousLayerPose, meanPose, coverage)
```

When W=0 retain the previous layer pose. Start from the canonical reference. Process layers in explicit ascending order. Masks have canonical bone count, so the same upper-body mask works across target rigs. Validate masks and all family hashes at binding.

For additive layers form local `dq=inverse(qReference)*qSample` and `dp=pSample-pReference`. In stable contribution order apply `q=q*Exp(weight*Log(dq))` and `p+=weight*dp`, including layer/mask weight. Here Log/Exp use full-angle rotation vectors as defined in section 10. V1 weights are in [0,1]. Additive order is meaningful because quaternion multiplication is not commutative. Default additive root contribution is disabled.

This does not require keeping the outgoing clip active during an inertialized cut. Deliberate 2D locomotion blends can still contain several incoming clips. A mask/layer change that creates a jump requests a new continuity ID; continuously changing a weight does not.

### 9.2 General retarget solver: the correctness reference

Definitions, all in normalized character model space after removing locomotion:

* `Qc0[c]`: canonical calibration model rotation of canonical bone c.
* `Qc[c]`: sampled/blended canonical model rotation this frame.
* `Qt0[t]`: target calibration model rotation of target bone t.
* `map[t]`: canonical index for target bone t, or -1.
* `K[t]=inverse(Qc0[map[t]])*Qt0[t]`: offline model-space correction.

Export the target with a synthetic Root at index zero, identity pose, and all imported top-level bones beneath it. Keep original bones and their parents below this root; skin palette maps account for the index shift. Ancestor closure includes armature/helper transforms. All target bones are sorted parent-before-child.

At runtime:

```text
canonicalModel = FK(canonicalLocal)
targetRoot = identity
for t in target order after synthetic Root:
    p = actualTargetParent[t]
    localP = targetCalibrationLocalP[t]
    localQ = targetCalibrationLocalQ[t]

    if map[t] >= 0:
        wantedModelQ = canonicalModelQ[map[t]] * K[t]
        localQ = inverse(targetModelQ[p]) * wantedModelQ

    if t is mapped Hips:
        wantedModelP = targetCalibrationModelHipsP
                     + rho * (canonicalModelHipsP - canonicalCalibrationModelHipsP)
        localP = Rotate(inverse(targetModelQ[p]), wantedModelP-targetModelP[p])

    targetLocal[t] = (localP, normalize(localQ), unitScale)
    targetModel[t] = Compose(targetModel[p],targetLocal[t])
```

The calibration translation of every non-Hips target bone remains fixed. Arms and legs retain **target** lengths. Never copy canonical joint positions or all canonical local translations to the character; that stretches it.

This handles different immediate parents, optional missing canonical bones, static helper bones, arbitrary reference joint axes, and target T/A-pose differences, provided the mapping and calibration are approved. It is O(C+T) after blending. It is a rotation-transfer policy, not a guarantee of matching hand/foot positions on every body shape.

Proof obligations:

1. With canonical calibration input, `wantedModelQ=Qc0*inverse(Qc0)*Qt0=Qt0`; the target reproduces its calibration.
2. For an identical canonical target, K is identity and the output is the input pose.
3. For a mapped bone, reconstructing its target model rotation gives the requested orientation even if its actual parent is a helper or a different mapped anatomical ancestor.
4. Fixed target translations preserve target segment lengths. Hips moves only under its declared displacement policy.

The model-space offset is deliberate: `Qc*inverse(Qc0)*Qt0`. Do not replace it with `Qt0*inverse(Qc0)*Qc` or multiply an arbitrary local “rotation fix.” Rotations generally do not commute.

### 9.3 Fast rows, compiled from verified topology

When target bone t and its immediate target parent p map to canonical bone c and its immediate canonical parent cp, precompute:

```text
A[t] = inverse(Qt0[p]) * Qc0[cp]
B[t] = inverse(Qc0[c]) * Qt0[t]
targetLocalQ[t] = A[t] * canonicalLocalQ[c] * B[t]
```

This follows by substituting `targetModelQ[p]=canonicalModelQ[cp]*K[p]` into the general solver. It is two quaternion multiplies plus normalization for the row. Topology conditions are checked offline; never activate it just because bone counts match.

Use mixed row modes `Static`, `ModelMapped`, and `FastLocal`. The full target FK still runs to provide parent rotations and positions for general rows and skinning. Do not apply IK or inertialization inside this pass: that would invalidate the fast-row parent equality. First produce the complete raw target-local pose, then smooth it, then rebuild FK.

Run the reference and compiled solver on the same randomized canonical poses for every baked table. Required discrepancy is below 1e-5 radians and 1e-5 metres on valid unit-scale fixtures. A failed row is repaired or emitted as `ModelMapped`; do not loosen the comparison.

### 9.4 Extra bones and unique creatures

Unmapped tails, hair, capes, sockets, and helper bones keep their approved calibration local pose, which defaults to their original bind local pose. A mapped parent still carries them. Optional target-specific supplemental clips use a separate target-rig hash and target-local layer after retargeting, before smoothing. Such clips are explicitly outside cross-character family sharing.

Inline twist bones require an explicit quality decision; leaving them static can deform a heavily weighted forearm poorly. V1 reports `UnconfiguredDeformHelper` for such bones and requires a visual approval or a saved twist policy. Do not advertise automatic high-quality twist distribution on an untested rig.

A later optional twist policy can use section 17's constrained algorithm. Arbitrary extra spinal joints, digitigrade legs, facial systems, animated scales, and different anatomical topology are not solved by name mapping alone. Route incompatible content into a dedicated family or Unique group, with a named explanation.

Unique clips store the unique skeleton's own locals, including animated translations when explicitly allowed. They use the same codec, timeline, layer, smoothing, hierarchy, and skin functions; retarget is an identity fast path. V1 still enforces the scale restrictions in section 6.

## 10. Finite, interruptible, velocity-aware inertialization

### 10.1 Existing behavior and the selected new contract

The existing `PoseTransition` projects the outgoing offset onto a direction/axis, and `ScalarDecay` clamps radial velocity to be nonpositive. Its API does not receive incoming destination velocity. Keep that behavior available to existing callers and tests, but do not describe it as exact first-derivative matching for a moving incoming clip.

For the timeline path implement a compact `PoseDecay` in `SA.Motion/Inertialization`, using the finite quintic basis below. It stores an initial offset and initial offset velocity for position and rotation, plus duration/elapsed at character level. The new creation function explicitly receives outgoing and incoming velocities. It has no outgoing clip handle.

Apply it to the **retargeted target-local pose**, before IK/contacts. Each character has its own history; sharing clips does not imply sharing its smoothing state. Store this pre-IK corrected output for ordinary transitions, so a ground constraint does not feed its correction back into the animation filter.

### 10.2 Translation: exact boundary conditions

At a cut, let y0 and vOut be the latest corrected local position and its outgoing velocity, and x0,vIn the new raw destination position/velocity at the cut boundary. Set `e0=y0-x0`, `d0=vOut-vIn`. For duration D>0 and u=clamp(elapsed/D,0,1):

```text
b0(u) = 1 - 10*u^3 + 15*u^4 - 6*u^5
b1(u) = u - 6*u^3 + 8*u^4 - 3*u^5
e(u)  = b0(u)*e0 + D*b1(u)*d0
outputPosition(t) = destinationPosition(t) + e(u)
```

The offset starts at e0 with derivative d0, and reaches zero offset, velocity, and acceleration at D. Initial offset acceleration is zero; matching the outgoing acceleration is not promised. Evaluate once per character the two basis values, then apply them to all bones. At elapsed>=D return the raw destination exactly and retire the offset. Nonpositive duration is an explicit cut; nonfinite input yields a diagnostic and a defined reset.

This can overshoot when the outgoing velocity is large. Exact derivative matching and monotone radial decay are different policies. Do not silently clamp the derivative and continue claiming exact matching. A content/profile safety bound may explicitly choose a cut or a separate clamped policy, with that result reported.

### 10.3 Rotation: use the logarithm and the correct angular frames

Define full-angle vector functions:

```text
Exp(r) = (axis*sin(theta/2), cos(theta/2)), theta=length(r), axis=r/theta
Log(q) = axis * 2*atan2(length(q.xyz),q.w), with q normalized to shortest hemisphere
```

Use series limits near zero. Pick a deterministic axis/sign at exactly pi. Estimate local angular velocity in the bone's **parent frame** as `Log(qNow*inverse(qPrevious))/historyInterval`. This is the left angular velocity; do not substitute `inverse(qPrevious)*qNow`, which is expressed in a different frame.

At the transition boundary:

```text
E0 = normalize(qOut * inverse(qIn))
r0 = Log(E0)
v  = omegaOut - Rotate(E0,omegaIn)
rdot0 = LeftJacobianInverse(r0,v)
r(u) = b0(u)*r0 + D*b1(u)*rdot0
outputQ(t) = normalize(Exp(r(u)) * destinationQ(t))
```

The rotation of omegaIn by E0 is required because `qOut=E0*qIn`. Merely subtracting the angular-velocity vectors is only an approximation for a nonidentity offset.

For `theta=length(r)`:

```text
LeftJacobianInverse(r,v) = v - 0.5*cross(r,v) + a*cross(r,cross(r,v))
a = (1 - 0.5*theta*cot(theta/2)) / theta^2
```

For theta<1e-3, use `a=1/12 + theta^2/720 + theta^4/30240`. This form remains finite near pi, unlike an unguarded division by sin(theta). The derivation is `omegaOut = Jleft(r)*rdot + Rotate(Exp(r),omegaIn)`; inverting it establishes the initial derivative. At D, r,rdot,rddot are zero and the output rejoins the moving incoming orientation without a residual offset.

A fixed-vector polynomial can exceed the principal-log range during decay; Exp still defines a rotation. Reject nonfinite state, use bounded visible transition durations, and test extreme angular velocities. Normalized quaternion signs must not create false 360-degree history velocities.

### 10.4 Getting velocities and choosing a cut boundary

The boundary output is the last committed pre-IK corrected pose. The first evaluation of the new destination is at elapsed zero, matching that pose; afterward elapsed advances by simulation delta. Capture this discrete scheduling rule in a two-frame test, rather than adding delta before the first apply.

Outgoing velocity uses the actual previous positive history interval, never the current frame's unrelated delta. Incoming velocity must describe the **whole incoming evaluation**, including playback rate, animated blend weights, additive layers, and retargeting. For v1 evaluate the incoming raw target-local pipeline at boundary simulation times -h and +h, with h=1/240 s by default:

```text
vIn = (positionPlus-positionMinus)/(2*h)
omegaIn = Log(qPlus*inverse(qMinus))/(2*h)
```

If timeline contributions cannot be evaluated at neighboring simulation times, the adapter must supply those boundary velocities explicitly or choose the documented approximate policy. Do not reach backward through a generic managed callback from a Burst job. The host prepares at most two extra input snapshots only at a boundary. At one-shot endpoints use a one-sided derivative; for looping data use unwrapped times. Fixed-time samples have zero destination velocity. Sampling a constant weight when the timeline's weight is changing is an approximation and must be labeled.

These neighboring evaluations run only the stateless raw sample/blend/retarget pipeline; they do not advance smoothing, contacts, root ownership, timeline clocks, or committed state. The equations match the supplied boundary velocities exactly in real arithmetic. Velocities estimated by finite differences remain estimates of the real incoming derivative, so report estimator/f32 error separately from the algebraic boundary test.

On interruption sample the latest corrected pose/velocity again and replace the decay. A repeated continuity ID only updates the destination and elapsed; it never recreates e0 or r0. Zero-delta calls do not update history. Seek, teleport, table change, entity reuse, long residency gaps, and unavailable history reset it. A visible `MaxHistoryGapSeconds` defaults to 0.1 s; larger gaps initialize zero outgoing velocity instead of extrapolating stale history.

For ragdoll takeover/recovery, use a separate explicit boundary that captures the actually displayed physical pose in the same target-local frames, then seed this solver. Do not sample a stale animation pose while recovering and do not feed contact IK into ordinary transition history. Physical control remains an optional consumer/producer outside the compact animation core.

### 10.5 Minimal numeric fixtures

* Translation: outgoing x=1, vOut=2; incoming x=4, vIn=-1. Then e0=-3, d0=3; at elapsed zero output is 1 and velocity is 2. At D it is the current incoming value/velocity.
* Rotation: choose a 90-degree offset around X and incoming angular velocity around Y. Test the actual quaternion finite-difference derivative; this catches omission of `Rotate(E0,omegaIn)` and incorrect Jacobian sign.
* Switch A->B->C inside D. First C output equals the corrected B output at the boundary. A and B receive no sampling requests after C starts.
* Same continuity for 100 ticks reaches the exact destination after D; it must not freeze at the initial offset.
* Seek to the same pose after different playback histories returns the same raw pose and zero root delta.

## 11. Root motion, contacts, IK, and skinning

### 11.1 Looping root motion without losing turns

Let normalized root R(0)=identity, duration T, and cycle delta `C=R(T)`. For an unwrapped sample time t:

```text
k = floor(t/T)
u = t - k*T
RootUnwrapped(t) = Power(C,k) * R(u)
RootDelta(tPrev,tNow) = Inverse(RootUnwrapped(tPrev)) * RootUnwrapped(tNow)
```

Use exponentiation by squaring; negative k uses inverse(C), zero k is identity. Pose sampling loops, but trajectory accumulates. Multiplying a translation by the loop count is incorrect for a turning clip. Test multiple cycles, partial cycles, exact boundaries, large positive/negative times, and forward/reverse cancellation. Compute loop counts with signed 64-bit temporaries after checking `abs(k)<=2^31-1`; reject nonfinite/out-of-range time and nonfinite composition results.

For per-tick root intent avoid subtracting two enormous accumulated f32 trajectories. Use the equivalent interval form directly: `Inverse(R(uPrev)) * Power(C,kNow-kPrev) * R(uNow)`. Compute the loop-count difference in signed 64-bit arithmetic. This cancels the large common cycle prefix before numeric evaluation. Absolute accumulated root is a reference/debug operation; ordinary gameplay integrates the small accepted deltas in its own world-coordinate system.

Scale the canonical root translation by rho and keep its rotation. Uniform translation scaling is compatible with rigid-transform composition. Runtime uniform character-instance scale applies once at the host boundary, not both in rho and skinning.

V1 chooses **one root authority**: gameplay movement, or one timeline-designated root contribution. Locomotion blend trees may still blend their bone poses. Mixed root deltas require an additional documented velocity/twist blend policy and are deferred; never accidentally sum masked upper-body root motion. Seek and teleports emit identity delta and reset the previous root sample.

The host applies accepted root motion once, resolves gameplay collision, and supplies the actual resulting model-to-world transform to IK. Animation root intent is not assumed to equal achieved displacement. Pose inertialization does not secretly steer or smooth the gameplay root.

### 11.2 Contacts need more than quaternion correction

After retargeting and smoothing, run FK. Hand/foot target data declares `Model`, `World`, or `SupportLocal` space. The engine adapter supplies physics queries and support transforms as plain snapshots; the portable solver makes no Physics calls.

Reuse `SA.Motion/Legs` for production planting. Its current verified behavior and limitations remain in `docs/legs.md`. Contact intent may be authored per clip or generated offline as a reviewable suggestion. For suggestions, reconstruct a sole marker in locomotion/world trajectory, measure height and velocity, and propose contact where height<0.025 m and speed<0.10 m/s for at least 0.05 s. These initial visible thresholds are scaled by body height and validated on the pilot. In-place clips require declared travel to infer stance velocity; do not classify by root-relative foot speed alone.

At plant, capture the anchor in the support's local space. Each tick transform it by the current support transform, preserving a moving platform. Use actual character movement, a confidence/weight ramp, and bounded reach. Release on loss of intent, excessive drag, teleport, invalid support generation, missing data, or unreachable contact. Default weight blend-in/out is 0.1 s, consistent with the existing demo lesson. Unreachable contacts report a visible diagnostic; do not stretch limbs to meet them.

For a basic two-bone limb, lengths a,b, hip/shoulder H, requested target F and pole P:

```text
d = clamp(length(F-H),abs(a-b)+epsilon,a+b-epsilon)
e = normalize(F-H)
p = normalize((P-H)-e*dot(P-H,e))
x = (a*a-b*b+d*d)/(2*d)
h = sqrt(max(a*a-x*x,0))
knee = H+x*e+h*p
end = H+d*e
```

Degenerate poles use the previous bend direction, then a deterministic perpendicular. Convert solved model rotations back to locals using the actual solved parent. Use segment rotations, never copy all solved child positions into the render skeleton.

In the existing `TwoBoneIK.Solve`, `LowerDelta` already includes the upper rotation and is relative to the **original** lower model orientation. Thus apply `upperNew=UpperDelta*upperOld` and `lowerNew=LowerDelta*lowerOld`; multiplying the upper delta a second time is wrong. Verify this in a nonidentity parent fixture before wiring it to a table.

Use the existing pelvis/soft-reach behavior for two planted feet. Foot orientation aligns the approved sole normal to the ground while retaining intended facing; the ankle is not the contact point. Hand contacts use the same two-bone math and explicit target orientation. Different proportions cannot preserve feet, hands, pelvis trajectory, and every source angle simultaneously; constraint priorities are visible profile data.

### 11.3 Existing mesh binding remains the truth

For renderer mesh coordinate system M, normalized rig/model space A, current bone model matrix G[j], and the original bind matrix B[j] mapping mesh-local vertices to that bone's bind-local coordinates:

```text
SkinMatrix[j] = MeshFromModel * G[paletteToRig[j]] * B[j]
MeshFromModel = inverse(ModelFromMesh)
vSkinnedMesh = sum(weight[j] * SkinMatrix[j] * vMeshBind)
```

Apply any root normalization consistently to both `ModelFromMesh` and the bone model transforms. Do not normalize only the skeleton and leave a renderer transform in another frame. The original B[j] can remain intact when the same model-space normalization applies to bones and renderer. Unit conversions embedded inside bone-local scales require the unsupported-scale check or an explicit validated content conversion.

Use affine matrices for the hierarchy/palette. The host supplies instance-to-world placement once. Verify every renderer/material slot, every mesh's palette order, attached rigid pieces, and bind-pose reconstruction. In bind pose the palette must reproduce the original mesh, while the calibration T-pose is expected to deform an A-pose-bound mesh into T-pose. Never use the T-pose test to replace the original inverse binds.

## 12. Compression v1: a small documented codec with measurable error

Use uniform sampling, exact default/constant stripping, segment-local translation ranges, and 8-byte smallest-three quaternions. Keep the decoder small and allocation-free. Do not start by porting all of ACL or a C++ decoder into Burst. ACL is a mature C++ reference and a useful later benchmark; its bytes are not automatically readable by this codec. [ACL project](https://github.com/nfrechette/acl)

### 12.1 Segments and track classes

Classify each translation and rotation track as exact family-default, exact constant, or dynamic. Only strip exact normalized f32 equality/orientation equivalence initially; approximate constant stripping is deferred. Rotation, translation, and additive-reference semantics remain separate. Unit scale is implicit in v1.

A segment spans at most 16 sample intervals and contains up to 17 samples. Neighboring segments duplicate their shared endpoint. For first interpolation sample i use segment `min(floor(i/16),segmentCount-1)`; the segment contains i and i+1, including when i is exactly a segment boundary. A one-sample pose has one segment. Segment count is `max(1,ceil((N-1)/16))`.

This overlap makes random access independent of prior segments. Do not delta-chain one segment to the previous segment. Default/constant tracks come from resident metadata and need no segment sample payload.

### 12.2 Quaternion encoding, exactly

Normalize q. Choose its largest absolute component, ties by lowest component index m in X,Y,Z,W order; negate the full quaternion if q[m]<0. Drop component m. The other components lie within [-1/sqrt(2),+1/sqrt(2)]. For each retained component x encode signed i16:

```text
a = 1/sqrt(2)
n = clamp(roundHalfAwayFromZero(32767*x/a),-32767,32767)
decode x = n*a/32767
missing = sqrt(max(0,1-x*x-y*y-z*z))
```

Store a u16 metadata word with m in bits 0..1 and all other bits zero, then three little-endian i16 values in original component order excluding m: exactly 8 bytes. -32768 is invalid. Reconstruct and normalize. Hemisphere-align the two decoded rotations before nlerp; the dropped index may change between samples.

For identity `(0,0,0,1)`, bytes are `03 00 00 00 00 00 00 00`. Negated identity must produce the same bytes. A +90-degree X rotation has a tie between X and W, chooses X, and decodes correctly with W positive. Test randomized unit rotations, exact ties, near pi, and corrupt metadata.

### 12.3 Translation encoding

Per dynamic translation track per segment store f32 min[3] and max[3]. Per sample/component encode u16 `roundHalfAwayFromZero(65535*(x-min)/(max-min))`, clamped to [0,65535]. If max=min, use integer zero and decode min. Decoded component error is bounded by `(max-min)/(2*65535)` before floating-point rounding. Large ranges or failed object-space checks fall back to f32.

### 12.4 Validation and deterministic fallback

Start from raw canonical samples. Encode a segment, decode it through the production decoder, and compare **the reconstructed motion** at all samples, midpoints, and quarter intervals against the raw sampler. Include FK marker errors, not just per-track angular errors. Initial quality budgets for the 1 m hip-height family are:

| Metric, compression only | Maximum initial budget |
|---|---|
| Joint angle | 0.15 degrees |
| Joint/contact-marker model position | 0.002 m |
| Root position | 0.0005 m |
| Root orientation | 0.05 degrees |

Markers include joint origins, soles, fingertips, and per-bone 5 cm offset probes. The whole clip is validated, including duplicated segment endpoints. V1 stores root samples raw, so their codec error should be negligible; still test it.

If any checkpoint fails, rewrite that segment's dynamic blocks as raw f32 and revalidate. This simple deterministic fallback gives a correct baseline without a complicated bit allocator. If it still fails, the reference/interpolation/format implementation is wrong; do not publish. A future optimizer may promote only responsible tracks, but that is not required for v1.

Also run the comparison through every distinct pilot retarget table. For very tall targets, tighten the canonical positional budget by at least their height ratio and validate actual markers; arm-proportion differences can amplify angular errors independently of height. Finite checkpoints are an empirical quality gate, not a formal continuous-time bound.

Report raw bytes, encoded bytes including headers/root/metadata, default/constant counts, raw-fallback fraction, max errors with bone/time, compress time, decode time, and peak memory. No compression ratio is promised before measurements. General-purpose archive compression may wrap stored segments only if the host decompresses outside the animation job and the codec identity records it; it does not replace random-access pose compression.

## 13. Durable assets: engines must not own the format

### 13.1 Source-of-truth assets

Keep family definitions, approved rig calibrations/mapping templates, raw canonical motion intermediates, and provenance outside Unity-only serialization. Unity `.asset` objects and ECS blobs are generated adapters and may be deleted/rebuilt. Do not serialize a `BlobAssetReference`, native pointer, C# struct dump, or an Avatar as the portable asset.

| Artifact | Content | Ownership |
|---|---|---|
| `.safamily` | UTF-8 JSON: schema version, family name/version, conventions, ordered bones/parents/reference locals, semantic IDs, contacts, required roles | Canonical definition |
| `.sarig` | UTF-8 JSON: schema version, source bind hash, root normalization, original bind locals, approved calibration locals, paths, map, hints, height/rho, template/version | Editable rig source |
| `.saclip` | SACA binary container, kind 1; raw codec 0 or packed codec 1 | Runtime motion |
| `.saret` | SACA binary container, kind 2; compiled per-rig retarget rows | Runtime table |
| Catalog | Bake-key-to-artifact mapping, logical IDs, source/provenance, labels, compatible family, quality report, packed-file location | Offline/load-time index |
| Mesh binding | Renderer transform, original inverse binds, palette-to-rig map, mesh identity | Renderer adapter; document its arrays independently |

Every final descriptor explicitly carries `schemaVersion`, `familyVersion` or target version, units/conventions, and a content hash. A clearly labeled draft can omit its final compatibility hash until the Avatar gate passes. Golden JSON examples must contain the **expanded** 56 bones; no runtime parser understands the shorthand recipe in this document. Unknown required fields/versions reject, while explicitly optional metadata may be preserved or skipped.

Use an explicit little-endian canonical identity serializer for hashing semantic data: a schema tag, length-prefixed NFC UTF-8 strings, exact-width integers, and f32/f64 fields in documented order. Normalize negative zero to positive zero, reject NaN/infinity, and canonicalize quaternion signs for identity. Do not use culture-dependent JSON text or managed `GetHashCode()` as a durable ID. Retain source raw bytes/provenance; normalized identity does not replace them.

### 13.2 SACA binary header v1

All integers are little-endian, floats IEEE-754; wire records are byte-packed. This is a selected layout for implementation, not an existing registered standard. Verify every size with golden byte fixtures. The implemented directory limit is 64 chunks. A file is at most 256 MiB in v1; at most 1,024 bones, 1,000,000 samples and 3,600 s per clip. Over-limit content is explicitly split/rejected offline. These bounds are versioned and are not adjustable invisibly in a job.

Header is 128 bytes:

| Offset | Type | Field |
|---|---|---|
| 0 | byte[4] | ASCII `SACA` |
| 4 | u16 | Major=1 |
| 6 | u16 | Minor=0 |
| 8 | u32 | Kind: 1 clip, 2 retarget |
| 12 | u32 | HeaderBytes=128 |
| 16 | u64 | FileBytes |
| 24 | u64 | DirectoryOffset |
| 32 | u32 | DirectoryCount |
| 36 | u32 | Flags=0 |
| 40 | byte[32] | SHA-256 of the whole file with these 32 bytes zeroed |
| 72 | byte[32] | FamilyHash |
| 104 | byte[24] | Reserved=0 |

Directory entries are 32 bytes: `Tag:ASCII[4]` at 0, `Flags:u32` at 4, `Offset:u64` at 8, `Length:u64` at 16, `Count:u32` at 24, `Stride:u32` at 28. Offset is file-relative. Flag bit 0 means required; all other bits are zero. All chunk starts and the directory start are aligned to 16 bytes. Unknown optional chunks can be skipped; unknown required chunks reject. Required chunks cannot repeat. Fixed records require `Length=Count*Stride`; variable payloads use Count=0, Stride=0.

Validate magic/version, actual file length, count limits, offset alignment, checked arithmetic, non-overlap with header/directory/other chunks, required chunks, enum values, reserved zeros, family/target hashes, finite fields, normalized rotations, and track/parent bounds **before** publishing native views. A file parser returns diagnostics and never allows an unchecked byte offset into Burst.

Hash verification occurs on full artifact publication. For partial streaming from a validated immutable artifact, verify the independently recorded segment CRC and the host's content-addressed transport/pack integrity. CRC is corruption detection, not content authenticity; the host must not treat it as a replacement for the artifact hash.

### 13.3 Clip chunks

`META` is one 64-byte record:

| Offset | Type | Field |
|---|---|---|
| 0 | u32 | BoneCount, equals family bone count |
| 4 | u32 | SampleCount N |
| 8 | f64 | DurationSeconds T |
| 16 | u32 | Flags: bit 0 loop, bit 1 additive; others zero |
| 20 | u32 | Codec: 0 raw, 1 packed |
| 24 | u32 | SegmentIntervals=16 |
| 28 | u32 | SegmentCount |
| 32 | u32 | RootPolicy: 0 None, 1 Planar, 2 FullRigid |
| 36 | f32 | RootPositionScale=1 in v1; rho belongs to target data |
| 40 | f64 | AdditiveReferenceSeconds; zero if not applicable |
| 48 | byte[16] | Reserved=0 |

`TRKS` contains exactly 2*BoneCount records, 32 bytes each, in bone order: translation channel 0, rotation channel 1. Record: `Bone:u16` at 0, `Channel:u8` at 2, `Storage:u8` at 3 (0 family-default, 1 constant, 2 dynamic), `Constant:f32[4]` at 4, `Reserved:byte[12]` at 20. Translation constant uses xyz, w=0. Default/dynamic records have zero Constant bytes. Root's pose channels are default identity/zero. Scale is implicit unit.

`SEGI` contains SegmentCount records, 32 bytes each: `FirstSample:u32`, `SampleCount:u32`, `DataOffset:u64`, `DataLength:u64`, `Crc32:u32`, `Reserved:u32=0`. DataOffset is relative to `DATA`. CRC-32 uses the standard IEEE polynomial 0xEDB88320 with initial/final XOR 0xFFFFFFFF over that exact segment payload. Segment i starts at sample 16*i; sample count is min(17,N-16*i). Check all ranges, overlap rules, and endpoint coverage.

`DATA` is the concatenation of segment payloads in SEGI order, with no unindexed trailing bytes. Each payload walks dynamic tracks in TRKS order. Each dynamic block starts with `Encoding:u8`, three zero bytes, `BlockBytes:u32` including this 8-byte prefix:

| Encoding | Header after prefix | Sample payload | BlockBytes for F segment samples |
|---|---|---|---|
| 1 position16 | f32 min[3], max[3] | 3*u16 | 32+6*F |
| 2 quaternion8 | none | u16 metadata + 3*i16 | 8+8*F |
| 3 position32 | none | 3*f32 | 8+12*F |
| 4 quaternion32 | none | 4*f32 | 8+16*F |

Codec 0 permits only encodings 3/4. Codec 1 permits 1/3 for position and 2/4 for rotation. The host validates each block and builds a compact offset view when loading the segment; a warm pose sample never scans variable-length blocks or parses JSON. Empty dynamic sets permit zero-length DATA segments with the CRC of empty bytes. No padding exists inside a segment; wire loads must not assume native alignment.

`ROOT` contains N records of `position:f32[3]`, `rotation:f32[4]`, 28 bytes each, normalized and including the final cycle delta. None uses identity records. `REFP`, required only for additive clips, contains BoneCount 28-byte absolute canonical local pose records. It is the actual reference pose, which need not be one of the motion's samples. Root reference is identity.

Optional `CONT` contains N records of four f32 confidences [left foot, right foot, left hand, right hand], 16 bytes each in [0,1]. Missing CONT means no authored contact intent, not “all feet planted.” Actual environment targets remain host data. Optional `INFO` is UTF-8 provenance JSON for offline tools; runtime skips it. Root/contact metadata stays resident when the host selects a clip; bone segments stream as needed.

### 13.4 Retarget chunks

`META` is one 64-byte record: `TargetBoneCount:u32` at 0, `Flags:u32=0` at 4, `HipHeightRatio:f32` at 8, `Reserved:u32=0` at 12, `TargetRigHash:byte[32]` at 16, and 16 reserved zero bytes at 48.

`BONE` has TargetBoneCount records, 64 bytes each: `Parent:i32` at 0, `SourceCanonical:i32` at 4, `Mode:u32` at 8 (0 Static, 1 ModelMapped, 2 FastLocal), `Flags:u32` at 12 (bit 0 Hips), `CalibrationLocalPosition:f32[3]` at 16, `CalibrationLocalRotation:f32[4]` at 28, reserved 20 bytes at 44. Static rows have SourceCanonical=-1; mapped rows have an in-range source. Exactly one mapped Hips row is required for a humanoid target. Synthetic Root row zero has parent -1, SourceCanonical=0, Mode=ModelMapped, and identity local pose/corrections; other parents must be smaller nonnegative indices. Unsupported flags reject.

`FIXQ` holds K[t], quaternion f32[4], per row. Static rows store identity. `PREQ` holds A[t] for fast rows and identity otherwise. B[t] is K[t]. Recompute and verify the fast-row topology condition and correction values during content validation; do not trust a corrupt mode field. Hip calibration model position is reconstructed by FK, not stored twice. Optional `INFO` carries names/review provenance. Skin inverse binds are absent from this table because different meshes can share it.

The core binds a table to the exact FamilyHash and TargetRigHash. Matching only a family label such as “Humanoid” is insufficient. Format version, family version, mapper/baker/compressor versions, and runtime package version are independent; changing a codec does not silently change skeleton compatibility.

## 14. Streaming, memory, and ECS lifetime

### 14.1 Memory must grow with shared content and active state

```text
libraryDisk = sum(encoded unique motions per family/profile)
           + sum(unique rig tables) + mesh bindings + catalog
residentRAM = loaded shared metadata/segments
            + sum(active character pose/history/contact state)
            + upload/scratch buffers
```

There is no characters*animations term. There is still characters*bones state and potentially mesh-specific data. The plan does not promise all 40,000 characters can animate every frame at unlimited bone count.

Illustration, not measurement: 56 bones, a 3 s clip at 60 Hz with endpoint has 181 samples. An uncompressed 10-float TRS pose costs `56*181*40=405,440` bytes per clip, before metadata. A million such clips is about 405 GB decimal. Baking those separately for 40,000 characters would multiply that cost by 40,000. Compression, deduplication and residency budgets are necessary even when clips are shared.

Use paged/on-disk catalog shards and load descriptors for referenced motions; do not instantiate a managed object for every clip at startup. The host's visible budget defaults to 64 MiB of shared resident motion data for the pilot and reports actual usage. This is a starting budget, not a measured optimal size. Account for metadata, root arrays, block offsets, duplicated endpoints, in-flight requests, and GPU upload copies too.

### 14.2 Required segment algorithm

Resolve each contribution's i,j as in section 9. Resolve its overlapping segment, deduplicate those segment requests by `(ArtifactHash,SegmentIndex)`, and request them from the host. Incoming boundary velocity snapshots can require additional neighboring segments; preflight those too. Prefetch the next segment in the current playback direction and all timeline-known near-future entries, within the budget. Never block a pose job on a file/network request.

Asset slots use `(index:u32,generation:u32)`. Binding resolves stable content IDs to these process-local handles. A slot reused after eviction increments generation; old handles fail. CPU jobs borrow immutable views pinned until their dependent job handles complete. GPU resources remain pinned through the completion fence. Publishing a new pointer and freeing the old allocation immediately is forbidden. The host makes the publication and retirement order explicit.

When the budget is exhausted, evict least-recently-used **unpinned** segments, ties by stable content ID/index. Pin only the active need window; do not pin the entire catalog or a character's full animation library. If all candidates are pinned, return `BudgetExceeded/NeedsData` and follow the hold policy. Test two characters sharing a segment while a third tries to evict it.

Small v1 libraries may load an entire selected clip. The public requirement reporting still operates in segments so expanding the loader later does not change animation semantics. Do not claim streaming performance from a fixture that preloaded every clip.

### 14.3 Unity scheduling and renderer paths

The canonical Unity path is one explicit source mode per rig: **Shared family library** or the existing **Rig clips** compatibility path. Its time source is visibly **Timeline** or **Playback**. Legacy scenes retain their current serialized behavior. A missing library/family/map shows a setup action; no invisible Temp file or environment switch selects a backend.

Run timeline contribution collection before animation evaluation. Resolve/pin content before scheduling jobs. Each job writes exclusively to its character's slices. Sequence: canonical sample/blend, canonical FK, raw target retarget/FK, target-local smoothing, target FK, optional constraints, final target FK, palette and renderer upload. Root intent goes through the host's movement authority before world-space constraints.

Reuse existing ECS/skin infrastructure. Read blobs by typed `ref`; copying a struct containing relative `BlobArray` offsets can read garbage and has crashed this Editor before. Do not assign one blob's relative arrays into another blob. Build a verified independent wire-to-blob adapter or native owned arrays, and test lifetime/stride explicitly.

CPU is required when using the new smoothing/IK path until GPU equivalents pass parity. Show the effective backend in the Inspector. Current GPU `SampleMotion` uses target bone count as sample stride; canonical count, target count, table base, and segment handles must become separate concepts before it can sample shared-family clips.

## 15. Bounded implementation packets for a smaller AI

Implement one packet at a time. Read the project AGENTS first, own only the named paths, keep the numerical fixtures, and leave a handoff with actual commands/results and next atom. The root Unity workspace is not a Git repository; `sa-motion` and `Packages/com.sa.unity` are separate repositories. Work in a branch/worktree appropriate to the repository rules. Publishing, merging, and release are separate user workflows, not prerequisites for producing this document.

All new filenames below are proposed. `src/`, `tests/`, and `docs/` in this table are relative to `sa-motion`; Unity paths are relative to its separate SA package. Freeze agreed shared structs in C00 so packets do not independently redesign the ABI.

| Packet | Owns and implements | Dependencies | Required receipt |
|---|---|---|---|
| C00: freeze the boundary | `docs/shared-family-contract.md`, API declaration draft, current baseline notes | This plan | Short exact struct/function signatures, caps, frame modes, ownership; no runtime code changes |
| C01: family definition | Content family descriptor/generator; `families/humanoid-v1.safamily`; descriptor tests | C00 | 56 expanded bones, fixed order/parents, reference FK/units/hash; Unity Avatar acceptance is still pending C08 |
| C02: inventory | `Editor/LibraryAudit.cs`, offline audit DTOs and grouping | C01 | Real selected content report; exact/structural groups separated; no invented counts |
| C03: map/calibration data | Content map/template/calibration builder and validators; its tests | C01, C02 | Required roles, ambiguous names, T/A-pose and bind/calibration separation, hash invalidation |
| C04: raw canonical evaluation | `SA.Animation/Data` views and `Pose` integration; its tests | C00, C01 | Raw sample boundaries, negative looping, layers/masks/additive, wrong family rejected; reuse existing math |
| C05: table math | `SA.Animation/Retarget`, content table compiler; retarget tests | C03, C04 | General/fast parity, different parents, optional missing nodes, fixed lengths, hip ratio |
| C06: exact inertialization | New `SA.Motion/Inertialization/PoseDecay` path and tests; retain existing APIs | C00 | Translation and SO(3) derivative fixtures, A->B->C, duration endpoint, sign/pi/invalid cases |
| C07: timeline evaluator | `SA.Animation/Timeline`, bounded state and tests | C04–C06 | One clock, duplicate ticks, idempotent boundaries, seek/reverse/pause/rollback reset, no sampling old clips |
| C08: Unity raw baker | `Editor/CanonicalClipBaker.cs`, conformance fixtures under tests/scratch | C01, C04 | Valid canonical Avatar; raw exports from real Humanoid sources; root translation+turn, vertical motion, endpoint, cancellation |
| C09: wire and codec | Neutral reader/writer/decoder/compressor; byte fixtures and hostile-input tests | C04, C05, C08 | Section 13 exact bytes, golden hashes, decoder parity, error/fallback reports, no unchecked offsets |
| C10: batch/cache/residency | Content bake catalog/dedup publisher; native segment host bridge | C08, C09 | Cache invalidation, rename dedup, resume/cancel, budget/pin/generation/eviction tests |
| C11: person-facing authoring | `Editor/AnimationLibraryWindow.cs`, map/calibration UI and Inspector changes | C02, C03, C08–C10 | Audit->fix->approve->bake is usable by mouse, visible progress/errors, Undo, clean preview scene |
| C12: CPU ECS integration | SA Runtime systems, authoring/baker adapters, timeline bridge, skin bindings | C07, C09–C11 | Real source/target shared artifact, timeline timing, all renderers, pooling/reload, live interruption and seek |
| C13: contact integration | Existing optional IK/Legs adapter and contact intent binding | C12 | Different proportions, actual travelling root, slopes/platforms, unreachable/release/teleport; no stretched bones |
| C14: pilot | Pilot content manifest, quality/size/timing reports and handoff | C12; C13 for contact claims | 50 characters/500 motions and the specific matrix in section 16, or an honest content shortfall |
| C15: size/performance/package | Measurement fixtures, trimming/load smoke, docs/provenance refresh | C14 | Standalone consumer with no Unity references; measured hot cost/allocation/residency; no unsupported platform claims |
| C16: optional GPU | Existing compute/HLSL and GPU adapter only | C15 and a demonstrated CPU bottleneck | Real CPU/GPU parity for identical canonical/table/decay data, correct stride and fences |

First useful vertical slice: three genuinely different humanoid rigs and twelve clips, raw CPU data, shared clip IDs, approved tables, timeline times, and one interruptible transition. Get that running through C12 before spending time on full-batch optimization. Keep C09's first codec simple; do not hold the vertical slice hostage to an ACL port.

For each packet the small AI must report: modified files; the invariant implemented; the smallest meaningful command that passed/failed; fixture assets/content IDs; what was not verified; and the next exact function/fixture to implement. Mark a packet complete only when its receipt exists. Do not respond with another architecture essay or replace these decisions with a state-machine framework.

## 16. Pilot and acceptance: prove the pipeline before scaling

### 16.1 Test corpus and coverage

Inventory at least 50 real character assets covering at least five distinct approved rig templates when that many exist: T/A-pose, different axes/names/parent chains, short/tall/limb variants, helper/twist bones, missing optional joints, multi-renderer/material meshes, and unique/nonhuman rejection cases. If the available data contains fewer templates, say so instead of counting copies as diversity.

Select 500 actual motions across sources: idle, forward/back/strafe walk/run/sprint, crouch, jump, turn, reach, upper-body additive, and licensed recovery content if available. Include zero/short poses, in-place/travelling clips and root turns. Source clips must be approved/compatible with their family. Do not fabricate 500 by renaming the same animation.

Run every compatible humanoid character/motion pair through a streamed numerical sweep: 25,000 pairs if all 50*500 are compatible, without materializing their pose products. Check all decoded frames for finite rotations/positions, fixed non-Hips target translations, correct family hashes, valid palettes, and reference/fast parity. Performance timing is a separate fixed workload, not this exhaustive audit.

Human visual review is stratified rather than pretending 25,000 pairs were watched: at least 12 representative motions per distinct template plus named extreme-proportion, twist and contact cases. Record selected character/clip IDs and captures. Unity Humanoid-on-each-target can be a quality reference for these reviewed cases, but differences are measured and classified; a rotation-only table is not promised bit-identical to Unity's muscle/IK retargeter.

### 16.2 Acceptance receipts

| Area | Required evidence |
|---|---|
| Family independence | Standalone .NET consumer samples, blends, retargets and builds palettes using files with zero Unity references |
| Sharing | All 50 humanoid targets point at the same motion artifacts per family/profile; re-baking after adding a target creates zero new motion payloads |
| Calibration | Canonical reference produces each target's approved calibration; original bind reconstruction still reproduces each mesh |
| Retarget algebra | Identity and random nonidentity rest fixtures; general/fast agreement; helper/missing-parent paths; no copied canonical lengths |
| Root motion | 1 m translation, 90-degree turn, four turning cycles, negative time and reverse cancellation, seek zero delta, no double application |
| Inertialization | Boundary position/orientation continuity, finite-difference velocity match with moving input, exact completion, rapid interruptions, masks, no repeated-ID restarts |
| Timeline | Absolute-time control, one advance, fixed-time sample, pause, reverse, seek, duplicate tick, membership misuse, pooled reuse and state reset |
| Compression | Section 12 budgets on raw canonical and pilot retarget markers, including seams, midpoints/quarters and raw fallback |
| I/O | Hash/corrupt/truncated/overflow/unknown-major rejection; immutable publication; failure does not commit partial pose |
| Residency | Cold/evict/reload, budget exhaustion, generation checks, CPU job and GPU fence lifetimes, missing incoming clips recover smoothly |
| Mesh output | Every renderer and material slot animates, original palette order/bind matrices, no stale target stride |
| Contacts | Actual movement and stance anchors measured on proportion variants; slope/platform/teleport/release; no unsupported all-body claim |
| Owner workflow | Mouse-only audit/map/bake/preview/timeline playback, useful diagnostics, Undo, scene reload, no leftover probes |

For a transition, compare the output at the **same boundary time** just before/after changing destination: angle tolerance 0.05 degrees, position 0.0001 m. Separately report the maximum natural frame-to-frame step in degrees/metres and the source baseline; a fast source ankle can already move many degrees per frame. Do not equate every large animated frame step with a failed transition, and do not relax a source comparison to hide a real switch discontinuity.

Run allocation checks and stage timings at 1, 50, 500, and 5,000 active characters where hardware permits, with 1/2/4 contributions and 56/100/200 target bones. Include both shared and diverse clip/time workloads, cold and warm residency, random seeks and frequent cuts. Report sample/decode, blend, retarget, smoothing, IK, palette, upload and rendering separately, with median/p95, hardware/backend and active bone count. Do not describe theoretical multiply counts as measured frame rate. A 40,000-character run is an additional explicit experiment, not implied by content capacity.

## 17. Later algorithms and intentionally bounded extensions

### 17.1 Optional inline twist distribution

This is a constrained extension, not an automatic fix for every extra bone. Accept only an approved, unbranched deform chain whose translations are collinear with its primary axis within a documented tolerance, with mapped start p, intermediate twist helpers, and mapped distal d. Store per-helper cumulative twist fractions in [0,1], nondecreasing, plus approved local/reference axes. Preserve distal orientation and all translations. Reject arbitrary branching/offset chains into manual policy.

After pose smoothing and constraints, use the final desired endpoint rotations. Let `Qref=QparentCurrent*CalibrationLocalQ[p]`, `D=inverse(Qref)*QpDesired`, and unit primary axis u in p's calibration local frame. Extract:

```text
Twist = normalize((u*dot(D.xyz,u), D.w))
Swing = D*inverse(Twist)
QhelperDesired = Qref * Swing * Exp(fraction*Log(Twist))
               * inverse(QpCalibrationModel) * QhelperCalibrationModel
```

For the start bone, its calibration-relative trailing factor is identity and it gets its configured fraction. For helper bones convert the desired model orientation to a local against its newly solved actual parent. Restore mapped distal `QdDesired` when visiting it; do not apply the full twist again. Inline offsets remain fixed; rotating about the collinear primary axis preserves the endpoint location. Re-run FK and verify that both endpoint positions/orientations and weighted mesh probes remain valid.

Projection can be degenerate near a 180-degree swing perpendicular to u. Use a previous unwrapped twist only during Advance, reset on Seek, and diagnose the singular case. A stateless fallback is identity Twist and all rotation in Swing, which preserves the endpoint but may change deformation continuity. No automatic approval without mesh/animation review. Because distribution changes the displayed chain, ordinary history is kept at its pre-distribution control stage; physical recovery needs an explicit inverse/seed policy and is not covered by enabling this option alone.

### 17.2 Other extensions

* Replace the Unity source baker with an engine-independent retargeter only after raw canonical exports and Unity-reference fixtures exist. Keep the file contract unchanged and version the baker key.
* Add quadrupeds by auditing real anatomies, defining compatible families and contact roles, and creating a suitable offline converter. Share the generic evaluator, not a humanoid muscle model.
* Add more codecs behind explicit codec IDs with golden fixtures and actual decode measurements. ACL integration is optional; using its name does not establish Burst compatibility.
* Add sparse curve/key reduction only with random-access indexing and the same object-space quality gates. Never let a clip seek decode the whole animation from its first key.
* Add mixed-root blending through an explicit rigid-velocity policy only when gameplay needs it; default single-root authority remains easy to understand.
* Blend shapes, facial animation, motion matching, physics reactions and authoring controllers remain optional separate consumers. They must not turn the compact pose evaluator into an engine framework.

## 18. Required Unity CLI workflow for implementation agents

The owner explicitly pointed out Claude's official **unity-cli** skill. Read it before driving Unity. The copy inspected for this handoff is [SKILL.md](/home/i/.claude/plugins/cache/claude-plugins-official/unity/0.1.6-beta/skills/unity-cli/SKILL.md), plus its `references/integration-advanced.md`. If that cache path moves, use `unity skill show` or locate the installed `unity-cli/SKILL.md`. Prefer installed `--help` and the live command catalog when old skill examples differ. Do not install another plugin or run `unity setup` just to read the skill.

Read `/home/i/GitHub/sa-motion/AGENTS.md` first. Its shell-control and verification sections remain the project's source of truth. Multiple Editors can run on this machine: always target `/home/i/ruk2` for direct commands. Never assume the historical port 7801 is current and never start a second Editor for this project based on one failed discovery call.

Discovery/read-only examples verified during this planning session:

```sh
unity --version
unity status --json
unity command --help
unity command --project-path /home/i/ruk2 --caller plugin --skill unity-cli --result-only editor status
unity command --project-path /home/i/ruk2 --caller plugin --skill unity-cli --query compile --detail compact
unity command --project-path /home/i/ruk2 --caller plugin --skill unity-cli --query inspect --detail compact --limit 10
```

The inspected CLI is 1.0.0-beta.15; the Editor is 7000.0.0a12. At the read-only check, ruk2 was ready, not compiling, Play mode stopped. Discovery initially failed inside the restricted sandbox despite `unity status` listing the Editor; the same live status command succeeded outside that sandbox. A later agent must check its current permissions/connection rather than concluding the Editor is closed or opening a duplicate instance. The owner subsequently enabled full access for this session.

Compile/test/evaluate C# in this project **only through the existing lock-holding wrapper**, as the project instructions require:

```sh
/home/i/GitHub/sa-motion/eng/unity_start.sh
/home/i/GitHub/sa-motion/eng/unity.sh compile
/home/i/GitHub/sa-motion/eng/unity.sh test SA.Tests
/home/i/GitHub/sa-motion/eng/unity.sh eval /tmp/sa-state-check.cs
```

The start command is for an actually absent Editor only; `SA_UNITY_WINDOW=1` requests a window when starting. These are future implementation commands, not tests run for this Markdown-only task. Use `eng/dotnet_test.sh` for neutral library changes and `python3 eng/check_source_rules.py` before their commits, as defined in that repository. Do not use a second batch Editor or `dotnet build` on generated Unity projects to bypass the wrapper.

Before touching a scene, prefab, or script, use eval to check `EditorApplication.isPlaying`, open scene paths, and `scene.isDirty`. For Inspector/window evidence use the existing `com.sa.unity.inspect` package and the “See the Inspector” recipe: PNG plus text from **inside Unity**, never a desktop capture. Keep captures under `Temp/look/` and remove them afterward. Do not copy blob structs containing relative array offsets.

Before calling a Unity feature done: perform the person's walk—open their actual scene/defaults, Play, use the visible controls, inspect useful feedback/Console, reload from disk, and repeat. Changes to generated `Assets/ReactionsDemo` go through **Tools/SA/Build Reactions Demo**. Test probes belong in tests/preview/scratch scenes and are removed when finished. Never leave a harness in a demo scene and never add hidden behavior switches.

## 19. Original planning evidence

The first version of this document was a plan and algorithm specification. Its original evidence follows. The later implemented portable foundation and C# receipts are recorded in section 22. Actual asset inventory, canonical Avatar validation, Unity baker tests, Burst/IL2CPP/player compilation, rendered retarget quality, engine streaming measurements and the 50/500 pilot remain unverified.

What was actually done for the plan:

* Read the SA project/repository guidance, work packets, existing portable kernels, current SA baker/data/systems/GPU path, and relevant Rukhanka conversion/root code without editing that third-party package.
* Read the official Unity CLI skill and installed help; discover compile/Inspector commands and obtain a read-only ready/stopped Editor status. No Play mode, scene, prefab, or library source was changed.
* Check this design's formulas with the standalone seeded Python verifier in section 21, using Python double precision. It ran successfully with seed 20261103: 500 general/fast retarget cases; 500 translation/rotation boundary-velocity cases; 1,000 root round trips; 20,000 quaternion round trips; nine segment-boundary cases; and wire-record size checks.

Recorded maxima in that **algebra verifier**, not in the real runtime:

| Check | Observed maximum |
|---|---|
| General target orientation error | 4.95e-16 rad |
| Fast/general rotation discrepancy | 9.84e-16 rad |
| Rotation initial velocity error | 4.58e-9 rad/s |
| Translation initial velocity error | 2.05e-9 m/s |
| Smallest-three quaternion error | 0.003432 degrees |

These results validate multiplication order, the rotation-velocity Jacobian, loop composition and packing math for the tested double-precision cases. They do not validate f32 rounding, all supported scales, real rig calibration, Unity root extraction, mesh deformation, compression positional budgets, or production asset parsing. Those remain explicit packet receipts.

## 20. Primary references for implementation

The architecture and selected algorithms above are this plan's design; external documentation establishes the engine/API/reference-library facts below.

* [Unity Humanoid retargeting](https://docs.unity3d.com/6000.0/Documentation/Manual/Retargeting.html): valid Humanoid Avatars provide the engine's offline source/target correspondence.
* [Unity root motion](https://docs.unity3d.com/6000.0/Documentation/Manual/RootMotion.html): body/root transforms and import policies must not be confused with Hips translation.
* [Manual PlayableGraph evaluation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Playables.PlayableGraph.Evaluate.html) and [clip foot IK control](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.AnimationClipPlayable.SetApplyFootIK.html): public editor-baker API entry points; actual project conformance is still required.
* [HumanPoseHandler.GetHumanPose](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/HumanPoseHandler.GetHumanPose.html): body position has Avatar human-scale normalization; it is not an interchangeable metre-space Hips curve.
* [Unity mesh bind poses](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh-bindposes.html): preserve original mesh/bone binding and palette correspondence.
* [Animation Compression Library](https://github.com/nfrechette/acl): mature independent codec/reference; optional future comparison, no dependency required by the compact v1 core.

## 21. Runnable algebra verifier

Copy the following into a temporary Python file and run it with `python3`. It needs only the standard library. It is an independent mathematical reference for translation into C# tests, not a replacement for the actual .NET/Unity receipts. Its temporary filename is not a runtime switch or a scene component.

```python
import math
import random
import struct
import json

rng = random.Random(20261103)

def add(a,b): return tuple(x+y for x,y in zip(a,b))
def sub(a,b): return tuple(x-y for x,y in zip(a,b))
def scale(a,s): return tuple(x*s for x in a)
def dot(a,b): return sum(x*y for x,y in zip(a,b))
def norm(a): return math.sqrt(dot(a,a))
def cross(a,b): return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
def unit(a): return scale(a,1/norm(a))
def mul(a,b): return add(add(scale(b[:3],a[3]),scale(a[:3],b[3])),cross(a[:3],b[:3]))+(a[3]*b[3]-dot(a[:3],b[:3]),)
def inv(q): return scale(q[:3],-1)+(q[3],)
def rot(q,v): return mul(mul(q,v+(0,)),inv(q))[:3]
def exp(r):
    t=norm(r)
    return scale(r,math.sin(t/2)/t if t>1e-14 else .5)+(math.cos(t/2),)
def log(q):
    q=unit(q)
    if q[3]<0: q=scale(q,-1)
    s=norm(q[:3])
    return scale(q[:3],2*math.atan2(s,q[3])/s if s>1e-14 else 2)
def qerror(a,b): return norm(log(mul(a,inv(b))))
def randomv(s=1): return tuple(rng.uniform(-s,s) for _ in range(3))
def randomq(): return exp(scale(unit(randomv()),rng.uniform(-math.pi,math.pi)))
I=(0.,0.,0.,1.)
Z=(0.,0.,0.)
def fk(parents,localq,localp):
    qs=[]; ps=[]
    for p,q,x in zip(parents,localq,localp):
        qs.append(mul(qs[p],q) if p>=0 else q)
        ps.append(add(ps[p],rot(qs[p],x)) if p>=0 else x)
    return qs,ps
def general(cp,cq,cpos,cq0,cpos0,tp,tq0local,tpos0,mapping,rho):
    cmq,cmp=fk(cp,cq,cpos)
    c0q,c0p=fk(cp,cq0,cpos0)
    t0q,t0p=fk(tp,tq0local,tpos0)
    resultq=[]; resultp=[]; modelq=[]; modelp=[]
    for t,p in enumerate(tp):
        parentq=modelq[p] if p>=0 else I
        parentp=modelp[p] if p>=0 else Z
        q=tq0local[t]; x=tpos0[t]; c=mapping[t]
        if c>=0: q=mul(inv(parentq),mul(cmq[c],mul(inv(c0q[c]),t0q[t])))
        if c==1:
            want=add(t0p[t],scale(sub(cmp[c],c0p[c]),rho))
            x=rot(inv(parentq),sub(want,parentp))
        resultq.append(q); resultp.append(x)
        modelq.append(mul(parentq,q)); modelp.append(add(parentp,rot(parentq,x)))
    return resultq,resultp,modelq,modelp

assert norm(sub(rot(exp((0,math.pi/2,0)),(0,0,1)),(1,0,0)))<1e-12
cp=[-1,0,1,2,3,3,5,6,3,8,9]
tp=[-1,0,1,2,3,3,5,6,3,8,9,10]
mapping=[0,1,-1,2,4,5,-1,7,8,-1,9,10]
max_general=0.; max_fast=0.
for _ in range(500):
    cq0=[I]+[randomq() for _ in cp[1:]]
    cq=[I]+[randomq() for _ in cp[1:]]
    cpos=[Z]+[randomv() for _ in cp[1:]]
    cposanim=list(cpos); cposanim[1]=add(cposanim[1],randomv(.2))
    tq0=[I]+[randomq() for _ in tp[1:]]
    tpos=[Z]+[randomv() for _ in tp[1:]]
    rho=rng.uniform(.3,3)
    outq,outp,mq,mp=general(cp,cq,cposanim,cq0,cpos,tp,tq0,tpos,mapping,rho)
    cmq,cmp=fk(cp,cq,cposanim); c0q,c0p=fk(cp,cq0,cpos); t0q,t0p=fk(tp,tq0,tpos)
    for t,c in enumerate(mapping):
        if c>=0:
            expected=mul(cmq[c],mul(inv(c0q[c]),t0q[t]))
            max_general=max(max_general,qerror(mq[t],expected))
        if c!=1: assert norm(sub(outp[t],tpos[t]))<1e-10
    assert norm(sub(mp[1],add(t0p[1],scale(sub(cmp[1],c0p[1]),rho))))<1e-10
    rq,rp,rmq,rmp=general(cp,cq0,cpos,cq0,cpos,tp,tq0,tpos,mapping,rho)
    assert max(qerror(a,b) for a,b in zip(rq,tq0))<1e-10
    tqmatch=[I]+[randomq() for _ in cp[1:]]
    tpmatch=[Z]+[randomv() for _ in cp[1:]]
    gq,_,_,_=general(cp,cq,cposanim,cq0,cpos,cp,tqmatch,tpmatch,list(range(len(cp))),rho)
    t0mq,_=fk(cp,tqmatch,tpmatch)
    for t,p in enumerate(cp):
        a=mul(inv(t0mq[p]),c0q[p]) if p>=0 else I
        b=mul(inv(c0q[t]),t0mq[t])
        fast=mul(mul(a,cq[t]),b)
        max_fast=max(max_fast,qerror(fast,gq[t]))

def basis(u): return (1-10*u**3+15*u**4-6*u**5,u-6*u**3+8*u**4-3*u**5)
def basisd(u): return (-30*u*u+60*u**3-30*u**4,1-18*u*u+32*u**3-15*u**4)
def jinv(r,v):
    t=norm(r)
    a=1/12+t*t/720+t**4/30240 if t<1e-3 else (1-.5*t/math.tan(t/2))/(t*t)
    return add(sub(v,scale(cross(r,v),.5)),scale(cross(r,cross(r,v)),a))
max_ang_derivative=0.; max_pos_derivative=0.
for i in range(500):
    r0=scale(unit(randomv()),1e-8 if i==0 else rng.uniform(0,math.pi-.00001))
    wout=randomv(5); win=randomv(5); qin=randomq(); duration=rng.uniform(.05,1)
    d0=jinv(r0,sub(wout,rot(exp(r0),win)))
    def output(t):
        b0,b1=basis(t/duration)
        r=add(scale(r0,b0),scale(d0,duration*b1))
        return mul(exp(r),mul(exp(scale(win,t)),qin))
    h=1e-7
    observed=scale(log(mul(output(h),inv(output(-h)))),1/(2*h))
    max_ang_derivative=max(max_ang_derivative,norm(sub(observed,wout)))
    assert qerror(output(duration),mul(exp(scale(win,duration)),qin))<1e-10
    observed_end=scale(log(mul(output(duration+h),inv(output(duration-h)))),1/(2*h))
    assert norm(sub(observed_end,win))<1e-6
    y0=randomv(); x0=randomv(); vo=randomv(); vi=randomv()
    e0=sub(y0,x0); de0=sub(vo,vi)
    def position(t):
        b0,b1=basis(t/duration)
        return add(add(x0,scale(vi,t)),add(scale(e0,b0),scale(de0,duration*b1)))
    assert norm(sub(position(0),y0))<1e-10
    pv=scale(sub(position(h),position(-h)),1/(2*h))
    max_pos_derivative=max(max_pos_derivative,norm(sub(pv,vo)))
assert max_ang_derivative<1e-6
assert max_pos_derivative<1e-6

def compose(a,b): return (mul(a[0],b[0]),add(a[1],rot(a[0],b[1])))
def inverse(a): return (inv(a[0]),rot(inv(a[0]),scale(a[1],-1)))
def power(a,k):
    if k<0: a=inverse(a); k=-k
    result=(I,Z)
    while k:
        if k&1: result=compose(result,a)
        a=compose(a,a); k>>=1
    return result
def root(u):
    w=math.pi/2
    return exp((0,w*u,0)),((1-math.cos(w*u))/w,0,math.sin(w*u)/w)
cycle=root(1)
def unwrap(t):
    k=math.floor(t)
    return compose(power(cycle,k),root(t-k))
for _ in range(1000):
    a=rng.uniform(-100,100); b=rng.uniform(-100,100)
    delta=compose(inverse(unwrap(a)),unwrap(b))
    ka=math.floor(a); kb=math.floor(b)
    interval=compose(compose(inverse(root(a-ka)),power(cycle,kb-ka)),root(b-kb))
    assert qerror(delta[0],interval[0])<1e-10 and norm(sub(delta[1],interval[1]))<1e-10
    back=compose(inverse(unwrap(b)),unwrap(a))
    ident=compose(delta,back)
    assert qerror(ident[0],I)<1e-10 and norm(ident[1])<1e-10
assert norm(power(cycle,4)[1])<1e-12
assert norm(sub(unwrap(-1e-8)[1],unwrap(1e-8)[1]))<1e-6

def roundaway(x): return math.floor(x+.5) if x>=0 else -math.floor(-x+.5)
def encode(q):
    q=unit(q); m=max(range(4),key=lambda j:abs(q[j]))
    if q[m]<0: q=scale(q,-1)
    values=[max(-32767,min(32767,roundaway(q[j]*math.sqrt(2)*32767))) for j in range(4) if j!=m]
    return struct.pack('<Hhhh',m,*values)
def decode(data):
    m,*n=struct.unpack('<Hhhh',data)
    assert m<4 and all(v!=-32768 for v in n)
    xs=[v/(math.sqrt(2)*32767) for v in n]
    missing=math.sqrt(max(0,1-dot(xs,xs)))
    xs.insert(m,missing)
    return unit(xs)
max_codec=0.
for _ in range(20000):
    q=randomq(); encoded=encode(q)
    assert encoded==encode(scale(q,-1)) and len(encoded)==8
    max_codec=max(max_codec,qerror(q,decode(encoded)))
assert encode(I).hex()=='0300000000000000'
assert max_codec<math.radians(.01)
for n in [1,2,16,17,18,32,33,34,1001]:
    segments=max(1,math.ceil((n-1)/16))
    for i in range(n):
        s=min(i//16,segments-1); first=16*s; last=min(first+16,n-1)
        assert first<=i<=last and min(i+1,n-1)<=last

assert struct.calcsize('<4sHHIIQQII32s32s24s')==128
assert struct.calcsize('<4sIQQII')==32
assert struct.calcsize('<IIdIIIIIfd16s')==64
assert struct.calcsize('<IIQQII')==32
assert struct.calcsize('<iiII3f4f20s')==64

print(json.dumps({'seed':20261103,'retarget_trials':500,'retarget_max_rotation_error_rad':max_general,'fast_max_rotation_error_rad':max_fast,'inertialization_trials':500,'rotation_velocity_max_error_rad_per_s':max_ang_derivative,'translation_velocity_max_error_m_per_s':max_pos_derivative,'root_trials':1000,'codec_trials':20000,'codec_max_rotation_error_degrees':math.degrees(max_codec),'segment_boundary_fixtures':9,'wire_layout_sizes':'128/32/64/32/64 bytes'},indent=2))
```



## 22. Implemented foundation and next task

The following is the original CF1 implementation receipt. Its portable C# kernels and fixtures are retained in CF2; section 23 supplies the current branch and optional shape addon. CF1 itself changed no live Unity assets. The tracked portable copy of this plan is `docs/shared-animation-plan.md`. Worktree names are not branch guarantees: check git before starting further work.

Read [the actual API and lifetime contract](shared-family-contract.md) and [the complete CF1 handoff](handoff/CF1.md). They supersede earlier proposed names where the implementation has made them concrete.

| Foundation | Implemented source | What remains |
|---|---|---|
| C00/C01 contract and canonical reference | [CanonicalHumanoid](../src/SA.Animation/Retarget/CanonicalHumanoid.cs), [expanded family reference](../families/humanoid-v1.safamily), actual unmanaged timeline inputs | C08/family publication are recorded on the CF2 base; real-corpus validation remains |
| C03 mapping/calibration math | [MappingAssignment and CalibrationFrame](../src/SA.Animation.Content/MappingAssignment.cs): Hungarian assignment, ambiguity, primary/roll alignment | Real inventory, trusted Avatar/hint acquisition and visible approval UI |
| C04/C05 pose/table foundation | [CanonicalBlend](../src/SA.Animation/Pose/CanonicalBlend.cs), [RetargetTable](../src/SA.Animation/Retarget/RetargetTable.cs), explicit identity path, palette math | Real character bindings and CPU ECS/render adapter |
| C06 interruption math | [PoseDecay](../src/SA.Motion/Inertialization/PoseDecay.cs), SO(3) log/exp/Jacobian, incoming/outgoing velocities | Adapter supplies boundary samples; legacy APIs retained |
| C07 timeline protocol | [TimelinePose](../src/SA.Animation/Timeline/TimelinePose.cs): atomic state/output, repeated cuts, seek, missing-data retry, epoch/reset | Connect the owner's real timeline once; keep Playback from advancing it too |
| Root trajectory | [RootMotion](../src/SA.Animation/Pose/RootMotion.cs): signed cycles, turns, height scale and cancellation of large common prefixes | Verify actual Unity extraction and gameplay authority |
| C09 file/codec foundation | [optional Content project](../src/SA.Animation.Content/SA.Animation.Content.csproj), allocation-free decoder, raw/packed SACA, object-space fallback, [golden fixtures](../tests/SA.Animation.Tests/Fixtures/README.md) | Real corpus quality/performance and import/export UI |
| C10 identity and lifetime primitives | Tagged canonical hashes; [Residency](../src/SA.Animation/Streaming/Residency.cs) pins, deterministic eviction and stale-handle protection | Catalog/batch publication, native allocations, async I/O and engine job/fence ownership |

The checked-in C# tests include a complete synthetic path: encode one shared motion, load it once, evaluate it through two different character hierarchies, blend, interrupt, decay and build skin palettes. There are committed independent golden raw/packed/table bytes. The complete warm decode/blend/retarget/smooth loop records **0 B managed allocation over 1,000 calls after warm-up**. This is a standalone .NET receipt, not a Burst or crowd timing claim.

Actual checks in the foundation worktree:

```sh
eng/dotnet_test.sh tests/SA.Motion.Tests/SA.Motion.Tests.csproj
eng/dotnet_test.sh tests/SA.Animation.Tests/SA.Animation.Tests.csproj
python3 eng/check_source_rules.py
git diff --check
```

Results: **315 Motion tests and 166 Animation tests pass for each of net10.0 and netstandard2.1 library builds**: 481 cases per target. Source rules and whitespace checks pass. The tests exercise the real C# kernels, including f32 errors; the older Python verifier in section 21 remains an independent algebra reference.

Concrete implementation details to preserve:

* `AnimationFrame.SimulationSeconds` is separate from signed/reversing clip `SampleSeconds`. Filters advance through positive simulation time.
* Full-mask sentinel for the new ClipContribution is `ushort.MaxValue`; other masks address the flat family-bone weight array. Keep bound masks immutable or issue a new identity.
* NeedsData sets a transient Waiting bit without consuming a cut/tick. Same-tick retry is allowed; a successful duplicate returns Reused and must not repeat gameplay root motion.
* Unique/exact-skeleton playback uses `IdentityRetarget.Apply`, preserving all local translation tracks. Humanoid table playback keeps target segment offsets and transfers Hips displacement.
* The cold Content owner allocates and hashes. Unity jobs borrow validated native data and call the hot span kernels; they do not hold a managed SharedClip object.
* Releasing a pin requires actual CPU/GPU completion. Eviction retires a generation; generation overflow retires the slot permanently.
* Kernel outputs are scratch until successful commit. The timeline evaluator stages both bone state and displayed pose, so even a failure on a later bone cannot partially commit earlier bones.
* Skeleton identity helpers hash the declared skeleton fields. Final family/profile identities must also include their versioned contact/import/mapping policies. Do not replace the complete bake key with a filename or label.

**Updated starting point:** section 23 supersedes CF1's original request to begin at C08. The CF2 base includes the subsequent C08 conformance/family publication. Read the current handoffs before advancing the real inventory, timeline/render adapter and corpus; do not reset published family identities or repeat completed integration packets. The historical CF1 test counts above are not CF2's current counts.

## 23. Optional runtime proportions: implemented addon and exact handoff

**Decision: separate optional addon, with small generic seams in the animation core.** SA.Shape 0.1.0 references SA.Animation 0.1.1, which references SA.Motion. SA.Animation does not reference SA.Shape. Characters that do not use customization keep the original APIs. A later Unity package should be `com.sa.unity.shape`, depending on the core adapter; no AnimatorController, Unity animation parameter system, runtime Avatar or UMA dependency belongs in this addon.

The implementation is on `packet/CF2-runtime-shape-addon`, in `/home/i/GitHub/sa-motion-wt/CF2`, based on b7fdaec. That base includes CF1 and the subsequent C08/family publication; the earlier section 22 receipt remains historical. Other agents are working in other worktrees. Do not assume `/home/i/GitHub/sa-motion-wt/CF1` is still checked out to the original CF1 branch, or overwrite later C-series work.

Read [the full implemented shape contract](shape.md) and [CF2's handoff](handoff/CF2.md). These contain exact API/storage rules, durable authoring schema, effect formulas, update order, failure semantics and small follow-up packets. The difficult numerical part is real C# with tests, not a request for the next AI to translate pseudocode.

### 23.1 Source to reuse

| Piece | Implemented source | Contract |
|---|---|---|
| Bounded channel smoothing | [ShapeParameters](../src/SA.Shape/ShapeParameters.cs) | Critically damped value/velocity, exact neutral restoration, no repeated multiplication into last frame |
| Proportion evaluation | [ShapeRig](../src/SA.Shape/ShapeRig.cs) | Immutable base offsets plus positive log length/skin effects, reference supports/height ratio, no shared-data mutation |
| Runtime data | [ShapeData](../src/SA.Shape/ShapeData.cs) | Unmanaged metadata, borrowed spans, rig/definition identity, per-character geometry revision |
| Generic retarget seam | [RetargetTable](../src/SA.Animation/Retarget/RetargetTable.cs) | ApplyGeometry and ApplyReferenceGeometry; existing corrections reused; no SA.Shape dependency |
| Neutral motion history | [ShapeMotionSpace](../src/SA.Shape/ShapeMotionSpace.cs) | Length-independent animation residuals; model-space hip residual; compose through corrected parents |
| Atomic staged evaluation | [ShapeTimelinePose](../src/SA.Shape/ShapeTimelinePose.cs) | Prepare/Commit plus convenience Evaluate; no display/history commit after a failed composition |
| Contacts/IK | [ShapeContacts](../src/SA.Shape/ShapeContacts.cs) | Updated limb lengths, changed sole offsets, explicit reach residual, rotations only |
| Skin and normals | [ShapeSkinPalette](../src/SA.Shape/ShapeSkinPalette.cs) | Rigid model * separate skin deformation * original inverse bind; inverse-transpose normal support |
| Corrective morph weights | [ShapeMorphs](../src/SA.Shape/ShapeMorphs.cs) | Bounded authored base plus channel effects; renderer uploads weights |
| Executable examples | [ShapePipelineTests](../tests/SA.Shape.Tests/ShapePipelineTests.cs), [ShapeBoundaryTests](../tests/SA.Shape.Tests/ShapeBoundaryTests.cs) | One compressed motion shared by changing bodies, frozen-shape incoming velocities, atomic failure, unique translations and zero warm allocations |

### 23.2 The algorithm, without the traps

Keep the animation skeleton rigid. Each joint has a current parent-space offset and unit local scale. Put thickness/head/foot stretch in a separate per-bone affine D, with an authored bone-local basis and pivot. Do not inherit nonuniform skin scale through the joint hierarchy: that creates shear and damages both retargeting and IK.

```text
amount_j = clamp(parameter_j, min_j, max_j) - default_j
length_i = exp(sum(logLength_ij * amount_j))
offset_i = baseCalibrationPosition_i * length_i + sum(offsetDelta_ij * amount_j)
skinScale_i = exp(sum(logSkinScale_ij * amount_j))
L_i = R(basis_i) * diag(skinScale_i) * transpose(R(basis_i))
D_i = affine(L_i, pivot_i - L_i*pivot_i)
palette_i = MeshFromModel * rigidModel_i * D_i * originalInverseBind_i
```

The child offset and the parent's skin length are separate authored effects. A longer wrist offset does not tell arbitrary forearm vertices how to stretch. Pair the effects using the rig's actual axes and use corrective morphs where mesh weights/branches require them. Neutral parameter defaults restore the original offsets and identity D exactly. Clamped inputs and explicit allowed positive ranges prevent numeric overflow/reflection; authoring must validate combined extremes and actual geometry. Keep the original inverse binds; rebinding a changed rest pose cancels the intended shape.

Ground the reference by measuring two approved deformed support markers, moving the hips so the lower support stays at its base height, then recomputing rigid reference FK. Set `rho = baseBindingRatio * newHipHeight/baseHipHeight` and retain the new hip model reference. Free mode skips the ground-restoring shift. Asymmetric legs still require runtime IK; reference grounding is not a two-foot terrain solve. A no-hips creature uses Free mode.

### 23.3 Shape changes and inertialization

Store animation history in shape-neutral residual space, rather than smoothing the physical offsets themselves:

```text
ordinary bone neutral P = (targetLocalP - currentOffset) / currentMotionScale
hips neutral P = (targetHipModelP - currentHipModelReference) / currentHeightRatio
neutral Q = targetLocalQ
neutral S = 1
```

For ordinary bones, motionScale is the positive length ratio. Hips use the height ratio. PoseDecay/TimelinePose smooth these neutral transforms using the existing finite quintic/SO(3) algorithm. Compose current offsets/scales afterwards. Hips restore a model position first, then convert it through the corrected actual parent. A purely local hip formula fails when shaped ancestors rotate; the implementation and a dedicated test cover that case.

A new shape revision does not change animation Continuity or restart a clip cut. Shape channels have their own value/velocity filter. This lets an interrupted transition follow the same animation path while the body grows. It cannot promise unchanged world vertices when a user abruptly changes a body parameter. Use the shape filter for a gradual visible change.

Incoming cut velocities must come from animation samples at boundary simulation times -h/+h using one frozen candidate shape. Normalize both and differentiate over 2h. Do not include the shape-filter derivative. Include animation sample rate and blend-weight change; use one-sided differences at endpoints. Outgoing history is already neutral. Reverse sample time still uses positive simulation dt for both filters.

Unique/exact-rig clips retain all translation curves: normalize against their base geometry, then compose against the current geometry. Do not route those clips through the ordinary humanoid table path, which fixes non-hip translations to current offsets. Physics recovery similarly normalizes an explicitly converted current displayed pose before seeding history; world-space physics positions are not neutral coordinates.

### 23.4 Frame order and ownership

```text
requested values
  -> next smoothed parameters, in scratch
  -> candidate shape geometry, rebuilt from immutable base
shared clip -> sample/blend -> retarget using candidate geometry
  -> ShapeTimelinePose.Prepare (normalize -> stage neutral history -> compose)
  -> optional contacts/IK -> scratch palettes/morph upload data
  -> ShapeTimelinePose.Commit
  -> publish render/contact data and commit parameters/geometry
```

Check both error and FrameAction. Reused means use the committed result and skip commit/publication; it does not prepare fresh scratch. NeedsData holds the old tick/pose/shape/parameters/root; only record the proposed Motion.Waiting flag, then retry. Any other error discards the candidate. Do every fallible stage before committing when whole-frame atomicity is required. Never modify the same character or staging inputs between Prepare and Commit. Single-writer/job dependencies are the adapter's responsibility.

Definition/target replacement needs a new RigEpoch or explicit Seek/Teleport. Same-tick shape edits and changed shapes during Pause/zero dt reject; preview customization through Seek. Increment the per-character revision when geometry, deformation or corrective morph settings change. A supplemental pose-geometry stamp catches accidentally reused revisions, but does not hash skins/morphs and does not replace a content identity. Restore parameters, geometry, neutral history, snapshots and contacts together for rollback.

Keep animation history pre-IK. Reach measures the new rigid distances, solves from original model rotations, updates the upper rotation, recomputes FK, then updates the middle rotation relative to its actual parent. Local translations never stretch. Anchor a sole using `ankle = supportAnchor - desiredFootRotation * (D_foot * soleLocalOffset)`. Anchors/poles are in the current model coordinates; the host converts from world/platform coordinates. Unreachable contacts return a residual for pelvis/release priority. Do not rescale a planted world anchor when the body grows.

RootMotion.Delta scales each new interval's translation by the current height ratio once. Never rescale accumulated world root position when shape changes. Correct normals use inverse transpose; exact LBS normals invert the blended position matrix, rather than blending per-bone inverse transposes. ShapeSkinPalette.Blend is the executable reference. Normalize/orthogonalize final normals/tangents in the renderer; singular blends return an explicit diagnostic.

### 23.5 Physics and durable authoring are bounded follow-ups

The full shape contract specifies `.sashape` JSON fields and an exact SHA-256 canonical byte recipe. Those cold parser/writer/UI tasks remain for the next AI. The runtime structs are borrowed memory views, not a disk format. Definitions bind to exact target identities and resolve bone paths offline; identical rigs share them. Author-approved shape bounds, bases/pivots, markers and mesh corrective morphs are content work, not inferred from a channel name.

A live physical body must update collider geometry, COM/inertia and joint anchors coherently at a fixed-step boundary, or visibly limit customization to a kinematic/rebuild mode. The future adapter's mass math is fixed: for a per-body affine A, old mass m, COM c and COM inertia I, set `C=(0.5*trace(I)*Identity-I)/m`, `C'=A*C*transpose(A)`, `c'=A*c+t`, `I'=m'*(trace(C')*Identity-C')`. Choose fixed mass or fixed density (`m'=m*det(A)`). Diagonalize the resulting symmetric inertia for principal axes; componentwise scaling of the old inertia diagonal is wrong. Primitive collider refitting needs inertia from the fitted primitives instead. This formula is specified, not implemented or physically verified here. Cloth/wardrobe/atlasing/mesh generation remain independent addons.

### 23.6 Verification and next exact task

CF2 receipts: **37 Shape tests, 178 Animation tests and 315 Motion tests pass on each library target** (`net10.0` and `netstandard2.1`), 530 cases per target. Shape tests explicitly assert the loaded framework of Shape, Animation, Motion and Content so a netstandard run cannot silently exercise a net10 dependency. The complete warm sample/blend/shape/retarget/inertialize/contact/skin path allocates **0 managed bytes across 1,000 calls after warm-up** in the .NET test host. This is not a Burst, GPU or crowd timing claim.

The optional .nupkg has both target assemblies and an SA.Animation >=0.1.1 dependency. A package-only consumer is built for both targets and run under .NET 10; see the handoff for exact commands/results. No package was published to a registry. The live Unity project/reference packages remain untouched by CF2. Rendered mesh quality, Unity/Burst compatibility/performance, morph/normal upload, source importer, physics resizing and the real 50/500 shape sweep are not verified by these portable tests.

**Next smaller-AI task:** read AGENTS and the current integration handoffs; use these tested kernels. Follow SH1-SH4 in the shape contract: importer/binding, optional Unity assembly/visible controls, real mesh renderer/contact integration, then physical and corpus gates. Prefer an existing newer C-series adapter over rebuilding one from section 22's old starting point. Drive Unity with its official CLI and `eng/unity.sh`, do the person's visible Editor walk, and leave no test harness in their scene. Do not rewrite the numerical foundation, import UMA as a mandatory core dependency, add state machines, or call the rendered/physics integration complete before measuring it.
