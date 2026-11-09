# SOMA (MotionBricks) to humanoid-v1: Route D retarget

Packet P08. Code: `src/SA.Soma` (MIT, netstandard2.1 + net10.0, no Unity, no GPL). Tests: `tests/SA.Soma.Tests`.
It follows L4's Route D. SOMA poses go straight into canonical humanoid-v1 samples, then into `SharedClipEncoder.Encode` with contacts and root travel. No Mecanim is involved.

## Pieces

| Type | Job |
|---|---|
| `SomaRig` | The 30 model joints: names, parents, their index in the 77-joint USD skeleton, rest translations, and bind world positions and rotations. Also the fixed 30 to 56 table (`TargetModel`) and the 27-joint source rig (synthetic Root plus 26 joints; the 4 hand-end markers are excluded) |
| `SomaMirror` | X mirror: p' = (-x, y, z), q' = (qx, -qy, -qz, qw) |
| `SomaAxes` | Runtime Z-up (MotionBricks `FacingDirectionType.MUJOCO`, +90 deg about X) back to model Y-up, applied to the Hips (root) local only. Also heading yaw from the leg joints |
| `SomaCalibration` | Builds the A-pose world rotation for every canonical bone from the mirrored bind geometry with `CalibrationFrame.TryRotation`, plus the source bind locals and the leg height ratio |
| `SomaRetarget.Create` | Builds the target calibration (A-pose locals) and the source map, then calls `RetargetTable.Compile` |
| `SomaFrame.ToSource` | One frame of SOMA locals to the 27 mirrored source locals, expressed in the frame's heading frame |
| `SomaClip.Convert` | n frames, as (n,30,7) or (n,77,7) floats, to 56-bone samples plus the planar root track. It uses `RetargetTable.Apply` |
| `SomaContacts.Merge` | [Lheel, Ltoe, Rheel, Rtoe] to [Lfoot, Rfoot, Lhand, Rhand] = [max, max, 0, 0] (decision B7) |
| `SomaClip.Encode` | `SharedClipEncoder.Encode` with `RootPolicy.Planar`, packed tracks, `CompressionLimits.Default`, and the two family foot sole points as error markers |

## Fixed table (no name scorer)

SOMA model index to SA index: Hips 0 to 1, Spine1 1 to 2, Spine2 2 to 3, Chest 3 to 4 (UpperChest, mapping A), **Neck2 5 to 5 (Neck)**, Head 6 to 6, Jaw 7, LeftEye 8, RightEye 9, LeftShoulder..LeftHand 10-13 to 10-13, RightShoulder..RightHand 16-19 to 29-32, LeftLeg..LeftToeBase 22-25 to 48-51, RightLeg..RightToeBase 26-29 to 52-55.
- SA Root 0 is the synthetic source root. It is identity in the samples, and travel goes into the `root` argument.
- Fingers 14-28 and 33-47 are `Static` rows: they keep the canonical reference (identity local).
- Neck1 is not mapped. SA Neck takes Neck2's **world** rotation, which already contains Neck1 times Neck2. SA Head takes Head's world rotation, so Head world rotation is kept exactly (`FastLocal` under Neck2).
- The hand-end markers (ThumbEnd, MiddleEnd) are used only as bind geometry, to calibrate the hand and forearm.

## Calibration (A-pose to canonical T-pose)

Every SA world rotation is `W_sa = M(G_soma) * C`, where `C = inv(M(G_bind)) * A`:
- `M` is the mirror.
- `G_bind` is the SOMA bind world rotation.
- `A` is the SA world rotation of the same bone posed like the SOMA bind pose.

`RetargetTable.Compile` computes exactly this `C` as `inv(sourceModel) * targetModel`, provided that (a) the source rig is mirrored before compiling and (b) the target calibration is the A-pose, not the T-pose. Both are done in `SomaRetarget.Create`.

`A` comes from `CalibrationFrame.TryRotation(T-pose primary, T-pose secondary, A primary, A secondary)` per bone:

| Bone | Primary | Secondary |
|---|---|---|
| Hips, Spine, Chest | to the next spine joint | hip line (RightLeg minus LeftLeg); +X on the SA side for Spine and Chest |
| UpperChest | to Neck1 | shoulder line |
| Neck | Neck2 to Head | shoulder line |
| Head | to the eye midpoint | eye line |
| Shoulders | to the upper arm | +Y |
| Upper arms | to the elbow | elbow hinge: cross(upper, fore) in the A-pose; cross(bone, +Z) in the T-pose, so a forward elbow bend maps to forward |
| Forearms | to the wrist | thumb direction (ThumbEnd minus Hand); +Z in the T-pose |
| Hands | to MiddleEnd (SA: middle proximal) | thumb direction |
| Thighs, shins | to the next leg joint | +Z |
| Feet | to the toe | +Y |
| Jaw, eyes, toes, fingers | none: they take their parent's A rotation, so the bind is neutral relative to the parent | |

The local +X bone axis remap is part of `C`. The test `CorrectionRemapsTheSomaLocalBoneAxisOntoTheCanonicalBone` checks that `C * (canonical bone direction)` equals the mirrored SOMA child rest translation (+X along the bone on the left, -X on the right) within 0.05 deg for all 17 chain bones.

Where the existing primitives do not fit:
- `RetargetTable.Compile` assumes source and target share one anatomical pose and one handedness. It cannot build the A-pose itself, so `SomaCalibration.APose` builds it, and the mirror happens before compiling.
- `CalibrationFrame.TryRotation` needs a secondary axis. The bone direction alone leaves twist undefined, so the secondaries above are a design choice. They are verified only on bone directions, not on twist (see Risks).
- `MappingAssignment.Solve` is not used (L4: the name scorer confuses SOMA "LeftLeg" with the thigh).

## Hips, root and height

- **Root.** The hips are projected to the ground. Heading is forward = cross(RightLeg minus LeftLeg, up), the same joints the engine uses. Root frame 0 is identity, as `Encode` requires. Root n is the planar travel and yaw relative to frame 0, scaled by the height ratio.
- **Hips sample.** It is expressed in that frame, so hips x and z are always 0, and `RetargetTable` maps y as `ref + (y - 0.998) * ratio`.
- **Height ratio** = SA thigh plus shin length divided by SOMA thigh plus shin length = **0.9922**.
- **Hip reference.** Posing SA in the calibrated A-pose with hips at 1.0 m left the ankles 1.36 cm high. The SOMA bind legs splay outward, and the hip-to-thigh offsets differ (SA 5 cm, SOMA 8.3 cm). So the target hip reference is lowered to **0.9864 m**, which puts the A-pose ankles at the canonical 0.10 m.
- **First attempt.** With a hip-height ratio of 0.9719 and the reference at 1.0 m, flat feet floated 1.2-1.9 cm and the toe-off frames reached 3.4 cm. That approach was rejected.

## Measured (Linux, net10.0 and the netstandard2.1 library build, 2026-11-08)

Synthetic tests (VERIFIED, `eng/dotnet_test.sh tests/SA.Soma.Tests/SA.Soma.Tests.csproj`: 11 passed in both builds):
- The mirror is conjugation by the X reflection: 200 random rotations and vectors, error under 1e-5. M(q r) = M(q) M(r).
- The T-pose maps to identity: all 55 non-root locals within 0.02 deg, and the hips at the hip reference within 0.1 mm.
- The bind pose maps to the A-pose within 0.05 deg per bone. Bone directions match the mirrored SOMA directions within 0.05 deg.
- A raised SOMA left arm (+X) ends up raised on SA LeftHand (13) at x < -0.1, while the right hand stays down.
- The embedded bind rotations reproduce the bind positions from the rest translations within 0.1 mm.
- A synthetic 40-frame walk encodes inside the default limits. Contacts are bit-identical after `Load`, and root travel is kept to 1e-5 m.

Eval clips (VERIFIED, `SomaEvalDataTests`; 30 fps MotionBricks FP32 runtime output from the L3 reference requests):

| Clip | Frames | Bytes | Encoder error | Readback vs pre-encode | Worst bone direction | Planted foot-frames | Highest planted support point | Highest planted family sole point | Lowest sole | Root travel x,z |
|---|---|---|---|---|---|---|---|---|---|---|
| req1 | 26 | 11216 | 0.0020 deg / 0.053 mm | 0.040 deg / 0.053 mm | 1.17 deg | 22 | 0.59 cm | 1.90 cm | -1.12 cm | -0.204, 1.147 m |
| req2 | 50 | 18160 | 0.0022 deg / 0.063 mm | 0.040 deg / 0.058 mm | 1.17 deg | 46 | 1.79 cm | 2.09 cm | -1.07 cm | -1.181, 0.975 m |
| req3_chain | 50 | 18160 | 0.0023 deg / 0.057 mm | 0.056 deg / 0.052 mm | 1.32 deg | 51 | 0.05 cm | 4.18 cm | -2.79 cm | 0.000, 0.000 m |

How to read the table:
- **Support point.** The lower of two points: the family sole point (foot bone 50/54 offset (0, -0.1, 0.08)) and a toe-ball point (toes bone 51/55 offset (0, -0.035, 0)). The 2.5 cm gate is on this support point.
- **Family sole point alone.** It exceeds 2.5 cm only on toe-only contact frames, where the heel is lifted and the mid-foot point rises (req3_chain, 4.18 cm). The merged contact `max(heel, toe)` cannot tell those frames apart (B7 loses the roll).
- **Lowest sole.** Negative values are penetration on swing or heel-strike frames, down to 2.8 cm.
- **Worst bone direction.** Data frames carry non-rest bone translations, up to 4.7 mm per L1. SA uses its own rest geometry, so up to 1.3 deg of direction difference is expected. The gate is 2 deg.

## Reverse retarget (SA to SOMA)

`SomaUnretarget.ToSoma(retarget, saLocal, saRoot, somaLocal)` turns one humanoid-v1 pose (56 locals) back into the 30 SOMA model joints as skeleton-local transforms, model Y-up, metres. It is closed form, allocation-free (stackalloc) and returns `AnimationError` without throwing.
- Every mapped bone: `G_soma = M(W_sa * inv(C))` with `C = Rows[bone].Correction`, then `local = inv(G_soma[parent]) * G_soma[joint]`. `FastLocal` rows give the same `W_sa = G * C`, so one formula covers both modes.
- Hips: SA model position (root, bone 0 and bone 1 composed) maps back as `sourceRef + (p - targetRef) / ratio`, then mirrored. `saRoot` is the root track entry from `SomaClip.Convert` (identity for a single pose), so the result is relative to the first frame's heading frame, like the forward root track.
- Neck1 gets its bind local; Neck2 takes the rest, so the Neck2 world rotation (all SA Neck carries) is exact.
- Hand-end markers get their rest translation and identity rotation, as in the MotionBricks data. Non-root translations are the rest translations.
- `ToSkeleton(somaLocal, skeletonReference, skeletonLocal)` writes the 77-joint layout: model joints at `SomaRig.SkeletonIndex`, every other joint and every non-root translation from the caller's 77-joint reference (the network's `reference_local`). `Write` packs poses as px py pz qx qy qz qw floats. The 77-joint bind is not in this repository, so the caller supplies it.

Measured round trip SOMA to SA to SOMA (`SomaUnretargetTests`, net10.0 and netstandard2.1, 2026-11-09, angles in double precision):

| Input | Local rotation, all mapped joints except Neck1/Neck2 | World rotation, all but Neck1 | Hips position | Model position, outside the neck chain |
|---|---|---|---|---|
| 24 random poses, Neck1 at bind | 0.00002 deg | 0.00007 deg | 0.002 mm | 0.002 mm |
| req1, req2, req3_chain eval clips | 0.00002 deg | 0.00005 deg | 0.0001 mm | 0.0005 mm |

The SA calibration A-pose maps to the SOMA bind pose within 0.00003 deg with hips at 0.99804 m, and the SA reference pose survives reverse then forward within 0.00001 deg.

What is lost, because the forward map drops it:
- **Neck1 versus Neck2 split.** Only Neck1 x Neck2 reaches SA. On the eval clips the Neck1 local differs from bind by up to 15.8 deg (Neck2 local the same amount the other way), which moves the Neck2, Head, Jaw and eye positions by up to 21.5 mm (req1 12.2, req2 17.1, req3_chain 21.5 mm). Their world rotations are exact.
- **Bone translations.** The forward uses SA rest geometry, so the data's non-rest translations (up to 5.0 mm on the eval clips) come back as rest translations.
- **Hips planar position and heading** live in the SA root track, not the pose. Without `saRoot` the pose comes back in its own heading frame, hips at x = z = 0.
- **Markers and fingers** carry no motion (markers are not mapped; SA fingers are `Static`).
- No twist is lost: each mapped bone carries a full rotation both ways.

## Regenerating the eval data (never commit it)

`testdata/eval-only/` is gitignored. The clips are derived from MotionBricks runtime output, which is covered by the NVIDIA evaluation licence.
1. Convert each `reports/L3/work/ref/*.npz` (keys `poses` (n,77,7) and `feet` (n,4)) to JSON with python3 -I.
2. Keep the 30 model joints, using `SomaRig.SkeletonIndex` order.
3. Write `{"space":"runtime-zup-mujoco","fps":30,"frames":n,"poses":[n*30*7],"contacts":[n*4]}`. Use `"space":"model-y-up"` for engine output with an identity `model_to_runtime`.

Without the folder, the test is reported as skipped with that reason.

## Provenance and licence

The bind and rest numbers in `SomaRig` were read with `pxr` from `motionbricks/assets/human/soma_base_skel_minimal_no_root.usd`:
- sha256 `4ab93d78a0f1c06d3bcbedee5ceebb5d9d5b002fd0a88638e9399ed80b61871d`, motionbricks commit 54fc563.
- metersPerUnit 0.01, divided by 100.
- MotionBricks states that assets are CC-BY-4.0. Attribution: "SOMA skeleton bind geometry from NVIDIA MotionBricks, CC-BY-4.0".

No motion data, BVH or ONNX content is in the repository.

## Risks and unknowns

- **Twist** (INFERRED correct, not measured). The secondary axes fix the roll of the arms, forearms and hands from bind geometry. A wrong guess shows as a constant roll on that bone. Bone directions cannot detect it. Check it visually on a Unity character before trusting wrist and forearm roll.
- **Heading yaw** follows the pelvis line each frame. That puts a per-stride yaw wobble into the root track (the samples compensate exactly, so the pose is unchanged). If the root is used for steering, smooth it.
- **Foot penetration**, up to 2.8 cm, is not corrected. That is leg IK's job at runtime.
- **Static fingers.** Fingers stay at the canonical flat hand, because MotionBricks has no finger output.
- **Space.** Runtime Z-up versus model Y-up is a caller choice (`SomaSpace`). Passing the wrong one lays the character on its back. It is not autodetected.

## Baking a generated motion (`tools/SA.Soma.Bake`)

```bash
dotnet run --project tools/SA.Soma.Bake -- families/humanoid-v1.safamily in.saib out.saclip
```

Reads a sa-inbetween `.saib` (version 1, Y-up, metres, 77 joints), converts it with `SomaClip.Convert`, merges contacts (B7), encodes with `SomaClip.Encode`, then reads the bytes back with `SharedClipReader.Load` against the family id minted from the `.safamily` file. The output is an ordinary SA shared clip: the runtime plays it like any other, so nothing about generation reaches the ECS hot path. Check on a 50-frame generated clip: 18160 bytes, same as the `req2` row above.

## Extracting a pose from a clip

`sa-soma-bake pose <family> <in.saclip> <time[,time...]> <soma_rest.pose> <out.pose>` (a time is seconds, `s+3f` for frame 3 from the start, `e-3f` for 3 frames before the end; with several times the output name carries `{}` for the index) samples one frame of a baked clip, runs the reverse retarget and writes a 77-joint `.pose` that `sa-inbetween` accepts as a context or goal. The rest pose supplies the joint offsets and finger rotations the humanoid rig does not carry; any pose written by `make_pose_library.py` works. Checked end to end: a generated move baked to `.saclip`, its first and last frame extracted, and a second generation run from them reproduced both frames at 0.0 degrees on every body joint (Neck1/Neck2 excepted, see above).
