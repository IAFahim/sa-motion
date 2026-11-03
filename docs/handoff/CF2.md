# CF2: optional runtime shape foundation

The requested proportion feature is an optional SA.Shape addon with two generic seams in SA.Animation. Its portable algorithms are implemented and tested. The ECS timeline still chooses clips, times, layers and cuts. No Animator/state-machine/Unity parameter machinery or UMA dependency was introduced.

Branch: `packet/CF2-runtime-shape-addon`. Worktree: `/home/i/GitHub/sa-motion-wt/CF2`. Base: b7fdaec, including CF1, C08 conformance/family publication and the full-corpus report. The first green implementation commit is 16c4569; the branch tip also contains the boundary/package checks and this handoff. Do not assume the CF1-named worktree still owns that original branch: it is being used for later verification work.

## What changed

* New optional `src/SA.Shape/SA.Shape.csproj`, v0.1.0, targeting net10.0 and netstandard2.1 with C# 9. It references SA.Animation; SA.Animation has no addon reference. No Content or Unity dependency in Shape.
* Bounded critically damped parameter filtering; immutable-base log length/skin effects; per-bone basis/pivot skin deformation; reference support grounding and updated hip ratio; bounded corrective morph weights.
* Geometry overlays reuse original rig maps/rotation corrections. Rigid joint offsets remain separate from skin thickness, and original mesh inverse binds stay untouched.
* Shape-neutral inertial history with model-space hip residuals, including animated/shaped ancestors. Frozen-shape incoming velocities, interruption/retry/seek/revision/epoch rules and staged Prepare/Commit preserve committed state on failure.
* Rotation-only IK using changed limb lengths and deformed sole offsets, including twist intermediates, explicit target orientation and unreachable residuals. Position/normal palettes and exact blended-matrix normal reference.
* SA.Animation 0.1.1 adds RetargetGeometry/ApplyGeometry/ApplyReferenceGeometry and TimelinePose.Prepare/Commit. Existing evaluation signatures remain compatible; wire formats and asset identity are unchanged. The version bump prevents the addon depending on an older 0.1.0 core without those hooks.
* [The full shape contract](../shape.md), the tracked [plan sections 22/23](../shared-animation-plan.md), and `/home/i/ruk2/plan.md` explain the formulas, source ownership, buffer lifetimes, exact proposed source/hash schema and bounded follow-ups. Section 22's original CF1 counts are historical; section 23 is the current addon handoff.

No Unity scripts, scenes, prefabs, manifests, installed DLLs, Rukhanka package files or UMA files were changed by CF2. All implementation was pure `src/`; AGENTS specifically says not to run Unity commands for this packet. The person's walk for this deliverable was the linked plan/source handoff and an actual package-only consumer, not an unperformed Unity UI walkthrough.

## Actual verification

From this worktree, all commands below completed successfully:

| Command | Observed result |
|---|---|
| `eng/dotnet_test.sh tests/SA.Shape.Tests/SA.Shape.Tests.csproj` | 37 passed, zero failed/skipped for each library target |
| `eng/dotnet_test.sh tests/SA.Animation.Tests/SA.Animation.Tests.csproj` | 178 passed, zero failed/skipped for each library target; includes two new core staging tests |
| `eng/dotnet_test.sh tests/SA.Motion.Tests/SA.Motion.Tests.csproj` | 315 passed, zero failed/skipped for each library target |
| `python3 eng/check_shape_package.py` | Pack both target assets/README/dependency, restore in an isolated local-feed package cache, build package-only consumers for both targets, and execute .NET 10 consumer; zero build warnings/errors |
| `python3 eng/check_source_rules.py` | `source rules: ok` |
| `git diff --check` and staged equivalent | Passed |

Total is 530 cases per target, 1,060 across the two runs. Tests are hosted by .NET 10; the netstandard run loads netstandard library assets. ShapeContractTests explicitly checks the framework of Shape, Animation, Motion and Content. This caught an initial transitive-reference selection problem; explicit direct core references in the new test project fix it. Assertions were retained.

ShapePipelineTests runs an encoded compressed shared clip through two differently shaped characters without changing clip bytes or shared rows. Other checks cover exact neutral restoration, no accumulation, channel reversal/bounds, arbitrary skin axes/pivots, normal/tangent perpendicularity, singular LBS blends, growing limbs through interrupted cuts, model-space hips under shaped animated helpers, late composition overflow, discarded staging, same-tick/pause/seek/epoch/missing-data behavior, unique-creature translation tracks, random shape/animation round trips, actual changed reach lengths and sole anchors, malformed data and alias rejection.

The warmed complete sampling/blending/parameter/shape/retarget/inertialization/contact/palette loop measured **0 managed bytes for 1,000 calls** after 200 warm-up calls, on both builds under the test host. No Burst timing, GPU timing, native allocation, crowd throughput or visual quality figure is inferred from that allocation check.

Package output is local and ignored by git:

```text
artifacts/shape-packages/SA.Motion.0.1.0.nupkg
artifacts/shape-packages/SA.Animation.0.1.1.nupkg
artifacts/shape-packages/SA.Shape.0.1.0.nupkg
```

The executable package consumer printed:

```text
SA.Shape package smoke passed; animation=0.1.1.0; motion=0.1.0.0
shape package check: both target assets, isolated local-feed restore/build and executable consumer passed
```

`eng/check_shape_package.py` is the repeatable recipe. Run the Shape test script first so Release assemblies and the SDK reference pack are available, then run the package checker. It uses the existing reference-pack cache to stay offline; the first ad hoc consumer attempt lacked that local source and failed NETStandard.Library.Ref resolution. The committed checker includes it. No NuGet registry was published to, and no Unity package was installed. Build all three packages from the same checkout; don't mix an older same-version Motion artifact from another feed into this development packet.

## What the next AI must preserve

Read [docs/shape.md](../shape.md) for exact API and update order. Reuse these kernels; do not convert the original plan's math into a second implementation.

ShapeRig, retargeting, morph, IK and palette outputs are scratch on failure. ShapeTimelinePose.Evaluate is transactional for the pose/history/snapshots. For whole-frame rendering/contact atomicity use ShapeTimelinePose.Prepare, do every fallible downstream stage, then Commit and publish. Action Reused skips commit/publication. On NeedsData, record only the proposed Motion.Waiting flag and keep parameters/geometry/root/display fixed. Inputs/buffers must stay unchanged between stage and commit; the host enforces single-writer/job dependencies.

Keep ordinary animation history pre-IK and neutral. A new proportion revision is not a new animation Continuity. Differentiate incoming animation using the same shape at -h/+h. Unique clips normalize against base geometry so their non-hip translations survive. Hips residuals use model coordinates, not independent local-position subtraction. Do not rescale accumulated root/world contact anchors or replace original inverse binds.

## Remaining work, explicitly unverified

SH1-SH4 in the contract are the exact follow-up packets: a cold `.sashape` importer/canonical identity writer with golden assets; optional Unity native-data/timeline adapter and visible controls; actual mesh palette/normal/morph/contact upload and validation; synchronized collider/anchor/inertia/cloth policy plus real corpus/performance gates.

The source schema and affine mass/inertia formula are specified in the contract; those authoring and physical adapters are not implemented. Per-rig shape bases/pivots/support markers, mesh corrective morphs and wardrobe fits need approved real content. This is not a generic mesh repair/generation system. Bilateral pelvis policy, ground casts, moving-platform snapshots and physics root authority stay in the host/existing contacts adapter.

No real Unity character was proportion-edited or rendered in CF2. Unity/Burst/AOT player execution, GPU LBS/DQS behavior, physical resizing, live ragdoll shape changes, renderer normal/tangent quality, clothing and the 50-character/500-animation shape sweep are **not verified**. Use the current C-series integration branch rather than rebuilding an adapter that another packet has already completed. Read AGENTS before driving the official Unity CLI, do the actual visible Editor walk for that adapter, and leave no harness in the owner's scene.
