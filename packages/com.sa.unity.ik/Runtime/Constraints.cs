using Unity.Entities;
using Unity.Mathematics;

namespace SA.IK
{
    public struct IKEnabled : IComponentData { }

    [InternalBufferCapacity(1)]
    public struct LookAtConstraint : IBufferElementData
    {
        public Entity Bone;
        public Entity Target;
        public float3 Position;
        public float3 Forward;
        public float Weight;
        public float MaxRadians;
    }

    [InternalBufferCapacity(2)]
    public struct TwoBoneConstraint : IBufferElementData
    {
        public Entity Upper;
        public Entity Middle;
        public Entity End;
        public Entity Target;
        public Entity Pole;
        public float3 Position;
        public float3 PolePosition;
        public float Weight;
    }
}
