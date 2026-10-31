using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.ReactionControl.Brace
{
    public enum BracePhase : byte
    {
        Idle = 0,
        Reaching = 1,
        Braced = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BraceState
    {
        public BracePhase Phase;
        public byte ArmMask;
        public byte FartherSide;
        public byte HasContact;
        public float SmoothedDistance;
        public float HoldTimer;
        public float StaggerLeft;
        public float StaggerRight;
        public float Lean;
        public Float3 ReachLeft;
        public Float3 ReachRight;
        public Float3 ContactLeft;
        public Float3 ContactRight;
        public Float3 NormalLeft;
        public Float3 NormalRight;
    }
}
