# U5: designer setup

Select a character model/prefab and choose `Tools/SA/Set Up Character`, or `Tools/SA/Set Up Ragdoll`. Imported models and original materials remain intact; generated SA prefabs/materials live under `Assets/SA`. A scene instance can be configured directly with Undo support.

Animation packs accepts animation FBX files, controllers, individual clips and folders. The model/controller clips are included automatically. Clips with matching names remain separate, with editable per-character lookup names; aliases never change shared clip data. Humanoid clips use the target Humanoid Avatar. Generic animations still need compatible bindings; importing an unrelated generic skeleton is not automatic retargeting.

The primary Inspector has the starting clip and three simple switches: in-place, smooth transitions and foot planting. Advanced hides the optional bone/mask overrides and sampling/engine settings. Existing explicit settings remain supported.

In-place is a bake option per character/clip, not an edit to the source animation. It removes horizontal model-space motion using an automatically selected pelvis/root or an optional motion bone. Vertical motion and limb-local gait curves remain. The clip cache includes the selected mode and motion bone. Explicit additive reference clips are corrected in the same space.

Ground foot IK lives in the Reactions adapter. It raycasts Unity Physics ground layer 1, locks low feet in world/support space, follows moving support transforms and releases on lift/loss of ground. Correction blends in and out over 0.1 seconds, retaining the previous anchor during release. It preserves limb lengths and yields to physics-owned ragdoll poses. The current adapter assumes Y-up and uses automatic sole-height calibration; artistic offsets and broader ground-layer controls are not exposed in this first version.

`com.sa.unity.ik` contains optional head look-at and two-bone reach targets. Pure solvers live in `SA.Motion`, not the Unity animation package. Physical characters consume those constraints as desired joint poses, without teleporting simulated bones.

Ragdoll hit animation is a clamped one-shot on layer 2 with automatic fade-out. It uses an automatically generated UpperBody mask when the skeleton has a torso. Assign a Flinch clip or let the baker find a hit clip in the discovered animations. The base locomotion layer keeps advancing. A character with no compatible hit clip still receives physical hit reactions.

The locomotion demo uses phase-preserving `TransitionPlayback.PlaySynced`; one-shot `Play` remains phase-zero by design. The old demo mask lookup remains a compatibility fallback, while newly built prefabs use the in-place bake option.

Verification results are recorded in `docs/handoff/U5.md`. No balance-controller or universal retargeting claim is made by this packet.
