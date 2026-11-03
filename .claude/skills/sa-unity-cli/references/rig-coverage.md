# Preserve Humanoid and Generic coverage

The compact library must preserve normal rig support while replacing animation scheduling with the ECS timeline. Removing Animator state machines and parameters is not permission to remove Humanoid import, Generic clips, hierarchy handling, root motion, masks, extra bones or multiple skins/materials. Use the installed Rukhanka package as a read-only behavior reference before designing replacements.

These paths were inspected in the installed package in ruk2. They are navigation aids, not proof that SA matches every feature:

| Concern | Rukhanka reference under `Packages/com.rukhanka.animation/` | Existing SA entry point in com.sa.unity |
|---|---|---|
| Rig and Humanoid Avatar conversion | `Rukhanka.Hybrid/Rig/RigDefinitionBaker.cs`, `RigDefinitionBaker_HumanConversionData.cs` | `Editor/RigBuilder.cs`: `Skeleton`, `HumanBones`, `BuildRig` |
| Humanoid vs Generic clip bindings | `Rukhanka.Hybrid/AnimationClip/AnimationClipBaker.cs`: `ParseHumanoidCurveBinding`, `ParseGenericCurveBinding` | `Editor/RigBuilder.cs`: `BuildClip`; `Editor/ClipCompatibility.cs` |
| Humanoid runtime conversion | `Rukhanka.Runtime/AnimationEngine/AnimationProcessSystem_Jobs.cs`: `ApplyHumanoidPostTransform` | Unity authoring samples into SA per-bone poses; runtime consumes the baked data |
| Skin/mesh baking | `Rukhanka.Hybrid/SkinnedMesh/SkinnedMeshBaker.cs` | `Editor/RigBaker.cs` and SA skin authoring/baking |
| Masks | `Rukhanka.Hybrid/AvatarMask/AvatarMaskBaker.cs` | `Editor/RigBuilder.cs`: `BuildMask` |

SA already has both ordinary paths. `RigBuilder.Skeleton` gathers skin bones, optional extra bones, Humanoid Avatar bones and their hierarchy. `BuildClip` samples Unity clips on a hidden copy, restores the source rest pose between samples, and writes plain per-bone animation. An authoring Animator/Avatar does not mean an Animator Controller is required at runtime. Keep this working path while adding shared-family clips.

Keep three compatibility questions separate:

1. **Ordinary playback:** a valid Humanoid Avatar can supply Unity's Humanoid authoring conversion; Generic clips use their transform bindings on compatible skeletons. Preserve the existing import/setup paths. A Generic clip with unrelated paths does not become compatible simply because the runtime supports Generic animation.
2. **Shared-family retargeting:** canonical clips, target maps, rest-pose corrections and root/hip scaling must agree. Verify source and target rig IDs, parent order and mapping. A working character-specific bake is not proof of one shared clip working across every rig.
3. **Runtime proportions:** joints, skin stretch bases, grounding and optional downstream IK/physics need their own validation. Ordinary Humanoid/Generic playback support does not establish arbitrary scaling or fitting.

When claiming parity, list the supported behavior, the Rukhanka reference, the SA implementation and an actual observed check. At minimum preserve the existing Generic bake, a valid Humanoid cross-rig bake, an extra-bone hierarchy and a multiple-material character. For a code change, run the relevant existing `SharedAssetsTests`, `AuthoringTests`, `ClipCompatibilityTests` and GPU tests through `eng/unity.sh`, then do the person's walk on real assets. Do not call a source inspection a rendered compatibility test.

SH5 measured the real Synty shape scene. Earlier U5 measured Humanoid clips from UAL2 on UAL1 and multiple UAL material entities. The planned 50-character/500-animation pilot is still the gate for broader coverage. Preserve what already works, record the remaining gap precisely, and do not present the pilot as passed.
