using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.ReactionControl.Brace
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct BalanceHint
    {
        public readonly float Support;
        public readonly float Lean;

        public BalanceHint(float support, float lean)
        {
            Support = Scalar.IsFinite(support) ? Clamp01(support) : 0f;
            Lean = Scalar.IsFinite(lean) ? (lean < -1f ? -1f : (lean > 1f ? 1f : lean)) : 0f;
        }

        public static BalanceHint Supported => new BalanceHint(1f, 0f);

        public static BalanceHint Airborne => new BalanceHint(0f, 0f);

        static float Clamp01(float value)
        {
            return value < 0f ? 0f : (value > 1f ? 1f : value);
        }
    }
}
