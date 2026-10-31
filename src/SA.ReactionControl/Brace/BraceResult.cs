using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.ReactionControl.Brace
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct BraceResult
    {
        public readonly BracePhase Phase;
        public readonly byte ArmMask;
        public readonly byte HasContact;
        public readonly float SmoothedDistance;
        public readonly float LeanIntent;
        public readonly float CrouchIntent;
        public readonly Float3 ReachLeft;
        public readonly Float3 ReachRight;

        public BraceResult(
            BracePhase phase,
            byte armMask,
            byte hasContact,
            float smoothedDistance,
            float leanIntent,
            float crouchIntent,
            Float3 reachLeft,
            Float3 reachRight)
        {
            Phase = phase;
            ArmMask = armMask;
            HasContact = hasContact;
            SmoothedDistance = smoothedDistance;
            LeanIntent = leanIntent;
            CrouchIntent = crouchIntent;
            ReachLeft = reachLeft;
            ReachRight = reachRight;
        }
    }
}
