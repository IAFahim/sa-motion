using SA.Motion.Numerics;

namespace SA.Motion.Legs
{
    public static class FootHeight
    {
        public const float TypicalRatio = 0.13f;
        public const float MinRatio = 0.03f;
        public const float MaxRatio = 0.35f;
        public const float MaxReachRatio = 0.4f;

        public static bool Plausible(float ankleHeight, float legLength) =>
            Usable(legLength) && Scalar.IsFinite(ankleHeight) && ankleHeight >= MinRatio * legLength && ankleHeight <= MaxRatio * legLength;

        public static float Resolve(float ankleHeight, float legLength) =>
            Plausible(ankleHeight, legLength) ? ankleHeight : Usable(legLength) ? TypicalRatio * legLength : 0f;

        public static bool PlausibleReach(float reach, float legLength) =>
            Usable(legLength) && Scalar.IsFinite(reach) && reach >= 0f && reach <= MaxReachRatio * legLength;

        static bool Usable(float legLength) => Scalar.IsFinite(legLength) && legLength > 0f;
    }
}
