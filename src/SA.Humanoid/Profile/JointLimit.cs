using System.Runtime.InteropServices;

namespace SA.Humanoid.Profile
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct JointLimit
    {
        public JointLimit(float twistMin, float twistMax, float swing1Max, float swing2Max)
        {
            TwistMin = twistMin;
            TwistMax = twistMax;
            Swing1Max = swing1Max;
            Swing2Max = swing2Max;
        }

        public float TwistMin { get; }
        public float TwistMax { get; }
        public float Swing1Max { get; }
        public float Swing2Max { get; }
    }
}
