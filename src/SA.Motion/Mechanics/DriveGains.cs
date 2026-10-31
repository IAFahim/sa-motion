using System.Runtime.InteropServices;

namespace SA.Motion.Mechanics
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DriveGains
    {
        public float Stiffness;
        public float Damping;
        public float MaxTorque;
    }
}
