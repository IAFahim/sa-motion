using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.ReactionControl
{
    [StructLayout(LayoutKind.Sequential)]
    public struct JointDrive
    {
        public Quaternion Target;
        public Float3 TargetAngularVelocity;
        public float Stiffness;
        public float Damping;
        public float MaxTorque;

        public static JointDrive Passive => new JointDrive { Target = Quaternion.Identity };
    }
}
