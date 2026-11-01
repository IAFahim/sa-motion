using System.Runtime.InteropServices;
using SA.Motion.Inertialization;

namespace SA.Animation
{
    [StructLayout(LayoutKind.Sequential)]
    public struct TransitionIntent
    {
        public RequestState Request;
        public float Seconds;
        public float Elapsed;
        public byte Pending;
    }
}
