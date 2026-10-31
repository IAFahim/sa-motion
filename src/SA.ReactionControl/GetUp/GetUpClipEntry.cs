using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.ReactionControl
{
    [StructLayout(LayoutKind.Sequential)]
    public struct GetUpClipEntry
    {
        public int ClipId;
        public RestingPose StartPose;
        public Float3 RootOffset;
    }
}
