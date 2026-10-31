using System.Runtime.InteropServices;

namespace SA.ReactionControl.Drive
{
    [StructLayout(LayoutKind.Sequential)]
    public struct EffectorAngles
    {
        public float Twist;
        public float Lean1;
        public float Lean2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DriveLimits
    {
        public float TwistMin;
        public float TwistMax;
        public float Lean1Min;
        public float Lean1Max;
        public float Lean2Min;
        public float Lean2Max;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DriveParams
    {
        public float Strength;
        public float Damping;
        public float MaxTorque;
        public float MinDamping;
        public float StiffnessCap;
        public float LimitMargin;
        public float SoftPull;

        public static DriveParams Standard => new DriveParams
        {
            Strength = 12f,
            Damping = 2f,
            MaxTorque = 200f,
            MinDamping = 0.05f,
            StiffnessCap = 1000f,
            LimitMargin = 0.02f,
            SoftPull = 0.25f,
        };
    }
}
