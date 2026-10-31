using System.Runtime.InteropServices;

namespace SA.ReactionControl
{
    [StructLayout(LayoutKind.Sequential)]
    public struct BodyBalanceParams
    {
        public float BodyStiffness;
        public float ElbowGain;
        public float Damping;
        public uint MaxSteps;
        public float UpperStiffness;
        public float SomersaultThreshold;
        public float LeanRateThreshold;
        public float LeanRateThresholdY;
        public float RateGainXZ;
        public float ElbowResponseGain;
        public float LeanFloor;
        public float SpineMuscle;
        public float ShoulderHipRate;
        public float TurnIntervalSpan;
        public float TurnIntervalBase;
        public float TurnOffProb;
        public float TurnToTargetProb;
        public float TurnToVelocityProb;
        public float TurnAwayProb;
        public float TurnSideProb;
        public float TurnProbFloor;
        public float TurnSpeedGate;
        public float SpeedFloor;
        public float TwistGainA;
        public float CutSpan;
        public float RateCap;
        public float ElbowMax;
        public float CatchGainX;
        public float CatchGainY;
        public float CatchGainZ;
        public float StepClampLo;
        public float StepClampHi;
        public float StabiliseDamping;
        public float OwnOffsetX;
        public float OwnOffsetY;
        public float OwnOffsetZ;
        public float ArmsOutMinLean;
        public float ElbowContactAngle;
        public float DecisionLoseLean;
        public float DecisionFallLean;
        public float DecisionExitFraction;
        public float FallHoldTime;
        public float LookProbeRange;
        public float MaxDrive;
        public float UprightCorrect;
        public float TwistCorrect;
        public float TorsoPitchBias;
        public float LookVelLead;
        public byte UseHeadLook;
        public byte UseArmsOut;
        public byte UseBodyTurn;
        public byte UsePelvisFrame;

        public static BodyBalanceParams Standard => new BodyBalanceParams
        {
            BodyStiffness = 9f,
            ElbowGain = 0.6f,
            Damping = 0.7f,
            MaxSteps = 3u,
            UpperStiffness = 6f,
            SomersaultThreshold = 0.35f,
            LeanRateThreshold = 1.2f,
            LeanRateThresholdY = 1.5f,
            RateGainXZ = 0.8f,
            ElbowResponseGain = 0.9f,
            LeanFloor = 0.05f,
            SpineMuscle = 4f,
            ShoulderHipRate = 0.4f,
            TurnIntervalSpan = 2f,
            TurnIntervalBase = 1f,
            TurnOffProb = 0.6f,
            TurnToTargetProb = 0.1f,
            TurnToVelocityProb = 0.1f,
            TurnAwayProb = 0.05f,
            TurnSideProb = 0.05f,
            TurnProbFloor = 0.05f,
            TurnSpeedGate = 0.4f,
            SpeedFloor = 0.15f,
            TwistGainA = 0.5f,
            CutSpan = 0.6f,
            RateCap = 3f,
            ElbowMax = 2.4f,
            CatchGainX = 8f,
            CatchGainY = 10f,
            CatchGainZ = 8f,
            StepClampLo = 0.8f,
            StepClampHi = 1.2f,
            StabiliseDamping = 0.9f,
            OwnOffsetX = 0.1f,
            OwnOffsetY = 0.1f,
            OwnOffsetZ = 0.1f,
            ArmsOutMinLean = -0.15f,
            ElbowContactAngle = 1.9f,
            DecisionLoseLean = 0.12f,
            DecisionFallLean = 0.6f,
            DecisionExitFraction = 0.5f,
            FallHoldTime = 0.8f,
            LookProbeRange = 6f,
            MaxDrive = 400f,
            UprightCorrect = 0.5f,
            TwistCorrect = 0.4f,
            TorsoPitchBias = 0.06f,
            LookVelLead = 1.2f,
            UseHeadLook = 1,
            UseArmsOut = 1,
            UseBodyTurn = 0,
            UsePelvisFrame = 0,
        };
    }
}
