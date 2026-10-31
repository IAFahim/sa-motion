using System.Runtime.InteropServices;

namespace SA.Humanoid.Profile
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct RoleSpec
    {
        public RoleSpec(float massFraction, float densityWeight, JointLimit limit)
        {
            MassFraction = massFraction;
            DensityWeight = densityWeight;
            Limit = limit;
        }

        public float MassFraction { get; }
        public float DensityWeight { get; }
        public JointLimit Limit { get; }
    }
}
