using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Animation
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Affine
    {
        public Float3 C0;
        public Float3 C1;
        public Float3 C2;
        public Float3 C3;

        public Affine(Float3 c0, Float3 c1, Float3 c2, Float3 c3)
        {
            C0 = c0;
            C1 = c1;
            C2 = c2;
            C3 = c3;
        }

        public static Affine Identity => new Affine(new Float3(1f, 0f, 0f), new Float3(0f, 1f, 0f), new Float3(0f, 0f, 1f), Float3.Zero);
    }
}
