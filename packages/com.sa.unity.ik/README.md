# SA IK

Install alongside SA and `com.sa.motion.unity`. The solvers are engine-independent `SA.Motion.Kinematics` functions; this package resolves bones, schedules Burst jobs and connects ECS targets.

Add `IKAuthoring` to an SA character. Drop a scene target into **Head Target**. For a hand or foot, choose its limb in **Reach Targets** and drop the target there. Bone mapping comes from the Humanoid Avatar or standard names; no chain setup is needed. A pole target is optional. Targets should be in the character's subscene, or gameplay can update the baked ECS constraint/target directly.

`IKTargetMotionAuthoring` gives a target a small animated path for demonstrations. The reactions demo includes moving head/hand targets on its animation-driven character.

Constraints run after animation and foot planting. `ExternalPoseControl` suppresses visual constraints when physics owns the skeleton; Reactions instead applies the same targets to the desired physical pose before joint drives. Weights and maximum head angle are configurable.

Public runtime buffers are `LookAtConstraint` and `TwoBoneConstraint`, on a rig with `IKEnabled` and CPU bones. Constraint target entities use current local transforms/parents instead of stale cached bone world matrices. Inertial history retains the final constrained rotations without capturing a second timestep.
