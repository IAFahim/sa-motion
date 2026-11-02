# Shared animation foundation: implemented contract

CF1 implements the engine-independent foundation requested in `/home/i/ruk2/plan.md`. Source is on `packet/CF1-shared-animation-foundation`, based on `a0d9b66`. Unity source, live scenes, manifests, installed DLLs, and the Rukhanka reference package are unchanged. No animation state-machine or parameter system was added.

## What the next implementation must reuse

| Entry point | Purpose |
|---|---|
| `CanonicalHumanoid.WriteReference` | Expanded 56-bone draft reference into caller-owned arrays |
| `MappingAssignment.Solve` in optional Content | Global one-to-one assignment with unmatched dummies and ambiguity detection |
| `CalibrationFrame.TryRotation` in optional Content | Primary plus secondary-axis alignment, including antiparallel primary axes |
| `RetargetTable.Compile` | Validate topology/ancestry/uniqueness and compile calibrated model/local rotation corrections |
| `RetargetTable.Apply`, `ApplyReference` | Compiled/reference retarget evaluation; target lengths stay fixed; Hips displacement uses height ratio |
| `IdentityRetarget.Apply` | Unique/exact-skeleton path that preserves animated translations on all bones |
| `PoseDecay.TryCreate`, `TryApply` | Velocity-aware finite translation/SO(3) smoothing; previous APIs remain intact |
| `SampleLocator.Locate`, `RootMotion.Delta` | Negative-time sampling, overlapping segments, turning loops and interval root accumulation |
| `CanonicalBlend.Evaluate` | Flat decoded canonical poses, override/additive layers and family-space masks |
| `TimelinePose.Evaluate` | Continuity identity, interruptions, seek/pause/reverse input, retryable missing data and transactional history |
| `PackedRotation`, `PackedPosition`, `SegmentDecoder` | Allocation-free codec and validated per-track offsets |
| `SacaContainer`, `AnimationCrc32` | Explicit wire directory/range checks and segment integrity |
| `SharedClipEncoder`, `SharedClipReader`, `RetargetFile` in optional Content | Portable raw/packed clip and table files, error validation and raw fallback |
| `ContentIdentity` in optional Content | Tagged canonical skeleton/motion/request hashes, UTF-8 NFC names, quaternion-sign and negative-zero normalization |
| `Residency` | Pin/release/eviction/generation primitives; native allocation and async I/O remain host-owned |
| `SkinPalette.Build` | Existing mesh inverse binds and mesh/model conversion in the declared multiplication order |

`SA.Animation.Content` references `SA.Animation`; the hot library does not reference Content. JSON, hashing, compression, file loading and the managed `SharedClip` convenience owner stay outside Burst jobs. For Unity, publish the validated track/block/segment/root data into immutable native storage. Pass borrowed spans into the hot kernels; do not put a `SharedClip` managed object into an ECS component/job.

## Actual input and storage rules

`AnimationFrame` contains Tick, RigEpoch, Continuity, SimulationSeconds, DeltaSeconds, TransitionSeconds, MaxHistoryGapSeconds and Mode. SimulationSeconds is the monotone simulation clock during Advance. ClipContribution.SampleSeconds can move backward independently. The timeline owns both values; SA never advances clip time. DeltaSeconds is positive simulation elapsed time, not a negative playback-rate substitute.

Contributions use a `(Slot,Generation)` handle, SampleSeconds, Weight, Layer and MaskIndex. Generation zero is invalid. MaskIndex `ushort.MaxValue` means full mask. Other indices select `masks[index*canonicalBoneCount+bone]`; this differs from the legacy Playback MaskIndex type. Keep mask tables immutable while bound; changing their contents requires a new continuity/binding identity. Layer ranges must cover contributions once in ascending layer order. Weights are finite and in [0,1].

Decoded scratch is `contributionCount*canonicalBoneCount` Pose values, ordered contribution-major. Additive references use the same offsets. Pose is the existing 40-byte in-memory TRS; unit scale is required by the shared-family v1 evaluator. Wire poses have seven floats and are not memory-castable to Pose.

All kernels use caller-owned spans. `RetargetTable.Apply` and decode/blend/palette operations may write partial **scratch** before returning an error. They never own committed scene state. `TimelinePose.Evaluate` stages both per-bone state and output, then commits them only on success. Keep its destination, committed pose, pose scratch, bone state, bone scratch and previous-input snapshots disjoint, as enforced by its alias checks.

The one intentional transient update on `NeedsData` is `PoseFrameState.Waiting=1`. It does not consume Tick/Continuity, change committed output, or overwrite history. It guarantees that the next resident evaluation creates a fresh boundary even if the wait was shorter than MaxHistoryGapSeconds. A host must suppress root delta and contacts while waiting. Duplicate successful ticks return Reused; never apply their root intent twice. Generation overflow permanently retires a residency slot instead of revalidating ancient handles.

## Retarget and calibration rules

Use the synthetic Root at index zero and parent-before-child ordering. Canonical clips bind by exact Family ID; character skins bind by exact target rig identity plus their own mesh palettes/inverse binds. The compiler rejects duplicate mapped roles, reversed anatomical ancestry, cycles, invalid parents and unsupported bone scales. Static helpers use their calibrated local pose, which authoring should preserve from bind by default.

The general orientation is `canonicalModel * inverse(canonicalReferenceModel) * targetCalibrationModel`. Fast local rows are compiled only when both direct parents correspond. Use ApplyReference to check any proposed optimization. RetargetFile reload recomputes corrections from calibration and rejects inconsistent stored matrices/modes, even if a caller recomputed the file hash. Explicit ModelMapped fallback rows are accepted when their PreRotation is identity.

Canonical reference input produces target **calibration** pose. An A-pose mesh's original inverse bind matrices are retained; calibration does not replace them. CalibrationFrame requires an approved secondary hint because a bone segment alone cannot resolve roll. This function does not discover trustworthy palm/pole hints or approve a rig; the Editor workflow still must do that visibly.

The unique/exact-skeleton path is explicit IdentityRetarget, not a guess based on equal bone count. Only use it when the target is bound to that exact family/reference skeleton. It preserves all local translation curves; ordinary humanoid table evaluation preserves target segment offsets and transfers only Hips displacement.

The checked-in `families/humanoid-v1.safamily` is a draft. Its 56 expanded locals are checked against the compiled reference by a test. It is intentionally marked `draft-awaiting-Unity-Avatar-conformance`; do not mint/publish a final family compatibility version until C08's Avatar gate passes.

## Smoothing and time

PoseDecay stores four Float3 values: initial position/rotation offsets and their velocities, 48 bytes. Duration and elapsed belong to character state. It uses the finite quintic basis and inverse SO(3) left Jacobian in plan section 10. Noncommuting incoming angular velocity is rotated into the correction frame before computing the offset derivative.

Supply incoming velocities for a new positive-duration boundary. The core receives data, never an evaluator callback. Prepare raw target poses at simulation boundary times -h/+h through sample/blend/retarget, then call PoseVelocity.TryBetween with interval 2h. Include incoming sample rates and changing blend weights; use a one-sided derivative at clamped endpoints. These are estimates; exact boundary algebra does not make finite differences exact derivatives of arbitrary content.

On a cut the first new output is at elapsed zero and preserves the previous corrected pose. Further interruptions replace the offset. Repeated Continuity updates weights/time without restarting. Normal history is pre-IK. Recovery from physics must seed an explicitly converted displayed pose; that adapter is not implemented here.

Seek/Teleport reset history and return raw destination. Pause reuses output. Reverse playback changes only contribution sample times; decay elapsed still grows. For rollback restore the complete caller-owned frame, bone, previous-input and pose state and replay; the library does not infer historical filtered state from a clip seek.

RootMotion.Delta evaluates `inverse(R(uPrev))*Power(cycle,kNow-kPrev)*R(uNow)`, cancelling the large common prefix before f32 composition. Its positive uniform height ratio scales translation once. Host code owns gameplay/root authority, collision and moving-platform snapshots. Reused, waiting, seek and teleport evaluations must not advance gameplay through stale root deltas.

## Assets, hashes and residency

SACA v1 has the plan's 128-byte header, 32-byte directory records, 64-byte clip/table metadata, and explicit little-endian fields. MaxChunks is 64 in this implementation. Header/record offsets are validated before slicing; all sample/block offsets are checked. Unknown required chunks reject; unknown optional chunks skip. Full artifact SHA-256 is verified by SacaFile.Verify/typed readers, not by the structural-only SacaContainer.ReadDirectory.

The header digest hashes the whole file with bytes 40..71 zeroed. Golden fixture README lists separate whole-file hashes, which include the populated digest field. Segment CRC32 is the IEEE variant, independent of the artifact digest. Pose jobs consume validated offsets; they do not recompute hashes or scan variable-length blocks.

The encoder strips exact normalized default/constant tracks, then uses 16-interval segments and smallest-three/translation16 packing. It checks all samples, quarters and midpoints in object space, including joint origins, 5 cm probes and caller-supplied contact markers. A failed segment is promoted to raw f32 and checked again. It does not reduce key counts. Its conservative size preflight requires enough capacity for raw fallback. This is a bounded baseline, not ACL-equivalent compression.

The current encoder stores the supplied absolute additive reference pose with AdditiveReferenceSeconds=0; source reference time is provenance/bake-key input. It does not offer an INFO/provenance authoring UI. Supply metadata through the catalog without changing shared motion semantics.

ContentIdentity.Skeleton is the versioned names/parents/reference **skeleton key**, not an automatic hash of arbitrary future descriptor fields. Include other family/contact/mapping/import policies in the versioned family/profile identity at publication. Motion hashes include family, actual normalized poses/root/reference/contacts, duration and root/loop policy. BakeKey additionally includes source dependencies, subasset, Avatar, profile and full Editor version; the profile owner must include every bake/compressor/import policy. No filename/label participates in Motion identity.

Residency owns metadata transitions, not memory. Its slot table must be single-writer or externally synchronized. Pin each borrowed segment before scheduling readers; release only after dependent CPU jobs/GPU fences complete. Evict returns the former payload to the host, which then frees its allocation. Old handles fail after replacement. Do not release a pin at job submission time or free pointers merely because a new generation was published.

## Verified and next

The .NET tests cover algebra, f32 moving-target derivatives, random quaternion packing, file corruption/truncation, raw fallback, segment boundaries, matching vs exhaustive optima, two different target hierarchies, interruption/retry/seek, palette reconstruction, pin lifetimes and zero warm managed allocations. Both library target builds are exercised from the .NET 10 test host. These are not Unity/Burst/player measurements.

Continue with C08's editor-only canonical Avatar and raw-baker conformance, followed by C11/C12 authoring and CPU ECS/timeline wiring. Reuse the fixtures and APIs above. C02's real inventory, automatic trusted calibration acquisition, batch publisher/catalog transport, native I/O jobs, detailed Inspector diagnostics, IK integration, GPU path and the real 50/500 corpus remain integration/content work. No completed Unity feature or rendered visual quality is claimed by CF1.
