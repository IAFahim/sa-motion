using Unity.Entities;
using Unity.Mathematics;

namespace SA.Reactions
{
    public struct PoseTrack : IComponentData
    {
        public float3 Position;
        public quaternion Rotation;
        public float Gain;
        public float MaxLinearSpeed;
        public float MaxAngularSpeed;
    }
}
