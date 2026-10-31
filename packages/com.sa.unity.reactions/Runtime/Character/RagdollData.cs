using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace SA.Reactions
{
    public enum RagdollMode : byte { Tracking, Limp }

    public struct Ragdoll : IComponentData
    {
        public RagdollMode Mode;
        public float Strength;
        public float MaxStrength;
        public float Timer;
        public float RecoverRate;
        public float LimpSeconds;
        public float LimpImpulse;
        public float StrengthLossPerImpulse;
        public float StandHeight;
        public byte Spawned;
        public byte JointMode;
    }

    [InternalBufferCapacity(0)]
    public struct RagdollRoleSpec : IBufferElementData
    {
        public Entity Bone;
        public int Role;
        public int ParentRole;
        public BlobAssetReference<Collider> Shape;
        public float Mass;
        public float3 AnchorInParent;
        public quaternion JointFrameInParent;
        public quaternion JointFrameInChild;
        public float ConeAngle;
        public float2 PlaneRange;
        public float2 TwistRange;
        public float Frequency;
        public float DampingRatio;
        public float MaxTorque;
    }

    [InternalBufferCapacity(0)]
    public struct RagdollBody : IBufferElementData
    {
        public Entity Body;
        public int BoneIndex;
        public int ParentRole;
    }

    [InternalBufferCapacity(2)]
    public struct RagdollHit : IBufferElementData
    {
        public int Role;
        public float3 Impulse;
        public float3 Point;
    }

    public struct RagdollOwner : IComponentData
    {
        public Entity Rig;
        public int Role;
    }
}
