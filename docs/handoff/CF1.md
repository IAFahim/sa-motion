# CF1: portable shared-animation foundation

Implemented on `packet/CF1-shared-animation-foundation`, isolated worktree `/home/i/GitHub/sa-motion-wt/CF1`, based on `a0d9b66`. Continue from this branch. The existing main checkout remains on its owner's branch and Unity still consumes its existing adapters/DLLs.

## Implemented

* Canonical humanoid reference generator and matching expanded draft `.safamily` descriptor: 56 bones, 15 required mappings. The descriptor explicitly awaits Unity Avatar conformance.
* Offline global bone assignment with dummy/unmapped choices and ambiguity analysis; primary/secondary-axis calibration math. Assignment is checked against exhaustive small optima.
* Generic retarget table compiler, different-parent/reference evaluator, validated fast local rows, height-scaled Hips, fixed target offsets and a separate Unique/exact-skeleton identity path.
* Original velocity-aware finite translation/SO(3) inertialization with the inverse left Jacobian. Existing `PoseTransition` and other public legacy behavior are unchanged.
* Absolute clip-time location, negative loops and interval root motion that preserves turning cycles without subtracting large accumulated f32 trajectories.
* Flat canonical blending with override/additive masks; timeline frame/state protocol for idempotent boundaries, interruptions, duplicate ticks, missing-data retry, seek/teleport/pause, reverse input and transactional output/history.
* Smallest-three quaternion and segment-range position codec, exact track stripping, random access, object-space validation at samples/quarters/midpoints and deterministic raw fallback.
* Versioned little-endian SACA raw/packed clip and table writer/readers with SHA-256, CRC32, checked bounds, enum/reserved/format validation and table-correction recomputation.
* Canonical skeleton/motion/request identity helpers, pin/generation/LRU residency primitives and original-bind skin palette math.
* Committed golden raw/packed/table files and their independent byte hashes, a complete standalone shared-motion/two-rig/interruption/palette fixture, and zero-allocation checks.

The hot source is in new files under `src/SA.Animation/{Data,Pose,Retarget,Streaming,Timeline}` and `src/SA.Motion/{Numerics,Inertialization}`. The optional allocating authoring/loading/compression project is `src/SA.Animation.Content`. The runtime core never references that project or Unity. Existing source files were not refactored; integration edits are the solution entry and Animation test project/lock file. Tests and draft family source are separate from all Unity scenes/samples.

## Actual receipts

Executed from the CF1 worktree:

```sh
eng/dotnet_test.sh tests/SA.Motion.Tests/SA.Motion.Tests.csproj
eng/dotnet_test.sh tests/SA.Animation.Tests/SA.Animation.Tests.csproj
python3 eng/check_source_rules.py
git diff --check
```

Motion: 315 passed, zero failures/skips, for net10.0 and netstandard2.1 library builds. Animation: 166 passed, zero failures/skips, for both library builds. That is 481 test cases per library target, executed by the .NET 10 host. The source-rule check reports `source rules: ok`; whitespace checks pass. The library projects enable their existing AOT/trim analyzers on the net10.0 build; this is not an actual NativeAOT/player run.

The tests include 20,000 quaternion codec round trips under 0.01 degrees, noncommuting moving-target angular-velocity finite differences, randomized general/fast table comparison, exhaustive matching optima, zero/short/overlapping segments, hash/CRC/offset/truncation rejection, tampered table corrections with a recomputed hash, late-failure atomicity, pooled epoch/reset and generation retirement. The complete warm decode/blend/retarget/smooth path records 0 B managed allocation over 1,000 calls after warm-up. No runtime speed or crowd benchmark was run.

Three golden fixtures were generated with a temporary standalone .NET exporter and then read by the committed tests; they are not regenerated during the tests. `tests/SA.Animation.Tests/Fixtures/README.md` documents their exact synthetic data and hashes. `families/humanoid-v1.safamily` is checked byte-for-value against the compiled f32 local reference through the JSON descriptor test.

## Ownership/behavior details the next AI must preserve

Read `docs/shared-family-contract.md` before coding. It defines actual names, the flat scratch layout, mask sentinel, unique translation path, duplicate-tick behavior, the transient Waiting flag, crypto placement and resource retirement.

Snapshot spans and output/staging spans must be distinct. Failed speculative kernels can overwrite scratch; only TimelinePose commits output/history. NeedsData sets Waiting without consuming Tick or Continuity. A same-tick retry is permitted after residency arrives; a successful duplicate is Reused and emits no repeated gameplay root action. Generations never wrap into an old valid handle: overflow retires a slot.

Host code must resolve and pin each exact clip/segment generation before calling the kernels. A metadata pin is not a guarantee that every bone segment is resident. Keep pins until dependent CPU jobs/GPU fences complete. The optional managed Content owner is for loading/standalone consumers; transfer validated data into caller-owned native storage for Burst.

## Not verified or integrated

No Unity script/scene/prefab/package manifest or installed DLL was modified. No Editor command, Unity compile, Play session, Inspector capture, real bake or rendered-person walk was run for this pure library packet, consistent with the repository's pure-src rule. Rukhanka files are untouched.

Not established: the draft's Unity Humanoid Avatar validity; real asset grouping/calibration accuracy; cross-source Humanoid bake/root extraction; Burst/IL2CPP/NativeAOT/player compilation; real mesh/contact quality; async native I/O and GPU fences in an engine; compression performance; 40,000-character throughput; or the 50-character/500-motion pilot.

## Next exact task

Implement C08's canonical Avatar and editor-only raw-baker conformance in the existing SA Unity package, using the official unity-cli skill and the lock-holding `eng/unity.sh` wrapper. First validate the checked-in draft on a preview-scene clone with no AnimatorController, then a translation-plus-turn clip and one real cross-Avatar source. Export into `SharedClipEncoder`, reload with `SharedClipReader`, compare raw canonical locals/root endpoints, and show any failure with object/path/fix. Do not publish a final family hash before this gate.

Before that integration, inspect the actual selected source inventory as C02 requires; choose a small real three-rig/twelve-clip slice. The remaining work includes map approval UI, catalog/batch publication and profile identity policy, native data ownership/scheduling, the real ECS timeline bridge, constraints/IK adapters, effective backend feedback and the person-facing reload/Play walk. Keep the portable algorithms and golden fixtures; do not rebuild a controller/state-machine layer.
