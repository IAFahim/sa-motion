# SA Reactions

Physics-driven characters for Unity Physics (Entities 6.7, Unity 7000.0).

## What it does

- `RagdollAuthoring` on a rig builds a 15-body ragdoll automatically: colliders, masses (published segment fractions), joints and per-joint drives. A humanoid Animator is used when there is one; otherwise the names are mapped by `SA.Humanoid` and validated (Robot Kyle is a generic rig and works that way).
- Each tick the ragdoll is pulled toward the animation by two things: per-joint angular drives (`PoseDriveSystem`) and velocity tracking of every body toward its animated pose (`PoseTrackSystem`). Strength scales both.
- `RagdollHit` buffer elements push the character. The impulse spreads over the whole body by mass, a share goes to the struck body, and strength drops with the size of the hit. A big enough hit makes the character limp.
- After `LimpSeconds` the ragdoll is reset to the animation pose and strength ramps back.
- Bones keep their animated lengths. Physics supplies rotations, plus the pelvis translation which moves the whole skeleton.

## Setup

Add `com.sa.motion.unity` and `com.sa.unity.reactions` to the project manifest next to `com.sa.unity`. Put `RagdollAuthoring` next to `RigAuthoring` (CPU engine) on a unit-scale rig inside a subscene.

## Demo

Import the `Reactions Demo` sample, build the SA Showcase Kyle prefabs, then run `Tools/SA/Build Reactions Demo` and open `Assets/ReactionsDemo/ReactionsDemo.unity`. Keys 1 to 4 deliver a shove, a front shove, a head hit and a knockdown; the same hits also run on a timer. See `docs/demo.md` in the repository for what it shows and what it does not.

## Limits

- Not a balance controller. The character stands because it is tracked toward its animation, not because it steps or shifts weight. The ported balance code in `SA.ReactionControl` is merged but not wired in.
- Joints are ball-and-socket. Unity's cone-and-twist ragdoll joints explode under a hard knockdown in this setup (`RagdollAuthoring.JointMode = 0` keeps them for experiments).
- No get-up animation: after a knockdown the pose is reset.
- One unit-scale rig per ragdoll; pooled instances share collision groups.
