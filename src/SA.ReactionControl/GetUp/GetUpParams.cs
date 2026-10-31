using System.Runtime.InteropServices;

namespace SA.ReactionControl
{
    [StructLayout(LayoutKind.Sequential)]
    public struct GetUpParams
    {
        public float LyingHeadDrop;
        public float UprightHeadRise;
        public float PoseDot;
        public float MaxAlignOffset;
        public float ClearDistance;

        public static GetUpParams Standard => new GetUpParams
        {
            LyingHeadDrop = 0.3f,
            UprightHeadRise = 0.35f,
            PoseDot = 0.5f,
            MaxAlignOffset = 5f,
            ClearDistance = 1.5f
        };
    }
}
