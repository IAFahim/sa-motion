# Shape rendering: failures reproduced in SH5

Read this for changes to the SA shape, skinning, package or demo path. See `docs/handoff/SH5.md` in sa-motion for full receipts and `docs/shape.md` for the portable contract. These are measured SA integration findings, not a general claim about every Unity version or graphics backend.

## Preserve the CPU/GPU boundary

| Boundary | Required representation |
|---|---|
| Portable affine and CPU/blob palette | 3 x 4, 48 bytes |
| GPU palette buffer, uploads, readbacks and test fixtures | `float4x4`, 64-byte stride |
| GPU affine homogeneous row | `(0, 0, 0, 1)` |

The previous GPU tests allocated or read 48-byte elements for 64-byte matrices. Fix both producer and consumer and compare the homogeneous row too. Do not convert all CPU/blob storage to 4 x 4 just to make GPU tests compile. Exercise the actual production HLSL, as `SkinningShaderTests` does, as well as CPU/GPU pose parity.

For weighted affine skinning, blend the position matrices first. Transform normals by the inverse transpose of that blended linear part. Transform tangents by the forward linear part, then orthogonalize against the resulting normal. Blending per-bone inverse transposes gives a different result under nonuniform scaling. Keep finite fallbacks for singular matrices, zero weights and degenerate directions. Preserve tangent handedness and the existing assertions.

`SharedFamilyPaletteSystem` and `ShapeSkinPaletteSystem` must run after `SkinningSystem` and before `Unity.Rendering.EntitiesGraphicsSystem`. Shaped rigs use the `NoSkin` guard so a later compute dispatch does not overwrite their CPU shape palette. Check ordering and actual palette values before debugging lighting.

Vulkan validates declared structured buffers even when an unskinned branch returns before reading them. Bind a valid identity sentinel and influence buffer at runtime startup, before the first skin is resident. Editor material previews need their own valid bindings in Edit mode. `SkinPreviewBindings` owns the preview buffers and disposes them on disable, reload and quit; do not leak them or overwrite active Play-mode resources.

## A proportion profile must agree with the rig

Changing a bone's skin stretch alone is not a longer articulated limb. Pair the parent's skin stretch with the matching child joint offset: thigh stretch with knee offset, lower-leg stretch with ankle offset, upper-arm stretch with elbow offset, forearm stretch with hand offset. Build the stretch basis from the actual child direction, not an assumed local axis. Keep the original mesh inverse binds; the shape overlay supplies the changed geometry. A new definition needs its own definition hash even if the target rig ID stays the same.

`KeepBase` in the workshop checks reference support height. A near-zero reference support error does not establish terrain planting or absence of locomotion sliding. Walk/Run in this scene are explicitly in place. Changing visual joints and skin does not prove live physics resizing, clothing fitting or morph support.

## Test portable packages and saved scenes

- Resolve package source with `UnityEditor.PackageManager.PackageInfo.FindForAssetPath(...).resolvedPath`; a Git UPM package normally lives in `Library/PackageCache`, not a physical `Packages/com.sa.unity` directory. Do not fix this by modifying the cache. Change its source repository and update the pinned dependency.
- Include test packages in the Unity manifest's `testables`, and require a nonzero test count. A passing embedded-package test does not prove the Git installation works.
- Pin Git dependencies to immutable, published commits. A package subdirectory uses `?path=packages/com.sa.motion.unity#<commit>`. Absolute `/home/...` symlinks are local conveniences, not portable package dependencies.
- Preserve `.meta` GUIDs and permanent materials, textures and profiles while relocating assets. Delete scratch assets only after checking their references. Reopening the scene caught a lost material reference in SH5; an in-memory successful view had hidden it.
- A black character can be a directional light facing away. A pink character can be a missing material/shader reference. Check those before rewriting the shader.
- Inspect the actual Game view PNG, its aspect ratio, the palette and the Console. A nonzero shape request or an updated revision counter does not prove that the character rendered differently.

## Measured baseline and limits

On Unity 7000.0.0a12, Linux, Intel RPL-S, the final pinned Git packages passed 95/95 `SA.Tests` and 3/3 `SA.Motion.Unity.Tests` on Vulkan. The preceding OpenGL run passed 94/94 before the extra startup test. Do not relabel the OpenGL run as 95 tests.

The real Synty demo had 52 rigid bones and 50 palette matrices. At fixed Idle time zero, longer legs/right arm changed 50,345 character-view pixels above 3/255. Restore neutral reproduced all local poses/palettes exactly and changed zero viewport pixels at that threshold. Evidence is in sa-demo's `docs/evidence/`.

Not established: every Humanoid/Generic rig, the 50-character/500-animation pilot, wardrobe/morph coverage, terrain planting, live ragdoll resizing, other GPUs or player/platform builds. Humanoid source clips need the canonical baking/mapping path; Generic clips need compatible skeleton bindings or an explicitly validated map. The old first-install Entities Graphics NRE storm was not reproduced; no Unity engine fix is claimed.
