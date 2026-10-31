using System.Runtime.InteropServices;
using SA.Motion.Mechanics;
using SA.Motion.Numerics;

namespace SA.ReactionControl
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RoleBody
    {
        public Float3 Position;
        public Quaternion Rotation;
        public Float3 LinearVelocity;
        public Float3 AngularVelocity;
        public float InverseMass;
        public Float3x3 InverseInertiaWorld;
    }
}
