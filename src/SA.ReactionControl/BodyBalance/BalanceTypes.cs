using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.ReactionControl
{
    public enum BalanceDecision : byte
    {
        Recovering = 0,
        LosingBalance = 1,
        Falling = 2,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BalanceInput
    {
        public float DeltaTime;
        public uint Stepping;
        public byte Failed;
        public byte Catching;
        public byte Grounded;
        public byte ContactUpper;
        public Float3 PushSource;
        public byte HasPushSource;
        public Float3 LookTarget;
        public byte HasLookTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BalanceOutput
    {
        public BalanceDecision Decision;
        public byte HasHeadLook;
        public Float3 HeadLookTarget;
        public Float3 HeadLookVelocity;
        public float HeadLookStiffness;
        public Float3 SpineLean;
        public float LeanResponse;
        public Float3 TurnIntent;
        public byte HasTurn;
        public float StepClamp;
        public byte HasStepClamp;
        public Float3 CatchFallGains;
        public byte HasCatchFall;
        public float StabiliseStiffness;
        public float StabiliseDamping;
        public byte HasStabilise;
        public uint StepBudget;
    }
}
