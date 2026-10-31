using System.Runtime.InteropServices;

namespace SA.ReactionControl
{
    [StructLayout(LayoutKind.Sequential)]
    public struct GetUpChoice
    {
        public byte HasClip;
        public int ClipId;
    }
}
