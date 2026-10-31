using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Motion.Mechanics
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Float3x3
    {
        public Float3 C0;
        public Float3 C1;
        public Float3 C2;

        public Float3x3(Float3 c0, Float3 c1, Float3 c2)
        {
            C0 = c0;
            C1 = c1;
            C2 = c2;
        }

        public static Float3x3 Identity => new Float3x3(
            new Float3(1f, 0f, 0f),
            new Float3(0f, 1f, 0f),
            new Float3(0f, 0f, 1f));

        public Float3 Multiply(Float3 v) => C0 * v.X + C1 * v.Y + C2 * v.Z;

        public Float3x3 Transpose() => new Float3x3(
            new Float3(C0.X, C1.X, C2.X),
            new Float3(C0.Y, C1.Y, C2.Y),
            new Float3(C0.Z, C1.Z, C2.Z));
    }
}
