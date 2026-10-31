using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.ReactionControl
{
    [StructLayout(LayoutKind.Sequential)]
    public struct GetUpAlignment
    {
        public byte Valid;
        public Float3 Position;
        public float Yaw;
    }
}
