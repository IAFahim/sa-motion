using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.ReactionControl
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SupportProbe
    {
        public byte Hit;
        public float Distance;
        public Float3 Point;
        public Float3 Normal;
        public Float3 PointVelocity;
    }
}
