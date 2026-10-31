using System.Runtime.InteropServices;

namespace SA.ReactionControl.Coordination
{
    [StructLayout(LayoutKind.Sequential)]
    public struct CoordinationParams
    {
        public float HysteresisMargin;
        public float FadeTime;
        public float MinLegStiffness;
        public float MaxLegStiffness;
        public float SupportedAnkleResponse;
        public float SupportedKneeResponse;
        public float SwingAnkleResponse;
        public float SwingKneeResponse;

        public static CoordinationParams Standard => new CoordinationParams
        {
            HysteresisMargin = 5f,
            FadeTime = 0.25f,
            MinLegStiffness = 50f,
            MaxLegStiffness = 600f,
            SupportedAnkleResponse = 3f,
            SupportedKneeResponse = 2f,
            SwingAnkleResponse = 1f,
            SwingKneeResponse = 2f,
        };
    }
}
