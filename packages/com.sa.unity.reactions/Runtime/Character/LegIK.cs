using SA.Motion.Legs;
using Unity.Entities;
using Unity.Mathematics;

namespace SA.Reactions
{
    public struct LegIK : IComponentData, IEnableableComponent
    {
        public LegParams Params;
        public float Weight;
        public float BodyScale;
        public uint GroundMask;
        public Entity Pelvis;
        public LegRigState State;
        public uint LastFrame;
    }

    [InternalBufferCapacity(2)]
    public struct LegIKFoot : IBufferElementData
    {
        public Entity Upper;
        public Entity Lower;
        public Entity Foot;
        public FootShape Shape;
        public LegState State;
        public Entity Support;
        public float3 SupportAnchor;
    }
}
