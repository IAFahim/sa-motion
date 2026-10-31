using System.Runtime.InteropServices;
using SA.ReactionControl.Balance;
using SA.Motion.Numerics;

namespace SA.ReactionControl.BalanceTick
{
    [StructLayout(LayoutKind.Sequential)]
    public struct BalanceInput
    {
        public float Timestep;
        public bool WideStep;
        public float DesiredHeight;

        public static BalanceInput Standard => new BalanceInput { Timestep = 0.0166667f, WideStep = false, DesiredHeight = 0f };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BalanceControllerState
    {
        public BalanceFeet Feet;
        public BalancePelvisState Pelvis;
        public Float3 PlantLeft;
        public Float3 PlantRight;
        public float AnkleClearance;
        public bool FallingLatched;
        public bool Seeded;
        public float LastComHeight;
        public static BalanceControllerState Initial(int maxStepDelay)
        {
            var countdown = maxStepDelay > 0 && maxStepDelay < 1000000 ? maxStepDelay : 30;
            return new BalanceControllerState { Feet = BalanceFeet.Initial(countdown) };
        }
    }

    public enum BalanceDecision : byte
    {
        Standing = 0,
        Stepping = 1,
        Falling = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BalanceReport
    {
        public bool Supported;
        public byte SteppingLeg;
        public bool StepInProgress;
        public float StepDistance;
        public BalanceDecision Decision;
        public float ComHeight;
    }
}
