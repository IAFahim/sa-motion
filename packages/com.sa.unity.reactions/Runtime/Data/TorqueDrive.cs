using Unity.Entities;
using Unity.Mathematics;

namespace SA.Reactions
{
    public struct TorqueDrive : IComponentData
    {
        public Entity Parent;
        public quaternion TargetWorld;
        public float Stiffness;
        public float Damping;
        public float MaxTorque;
    }
}
