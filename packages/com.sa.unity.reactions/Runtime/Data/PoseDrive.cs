using Unity.Entities;
using Unity.Mathematics;

namespace SA.Reactions
{
    public struct PoseDrive : IComponentData
    {
        public Entity Parent;
        public quaternion ParentJoint;
        public quaternion ChildJoint;
        public quaternion Target;
        public float Frequency;
        public float DampingRatio;
        public float MaxTorque;
        public float Strength;
    }

    public struct PoseDriveReport : IComponentData
    {
        public float3 ParentImpulse;
        public float3 ChildImpulse;
        public float3 Error;
        public bool Singular;
        public bool Applied;
    }
}
