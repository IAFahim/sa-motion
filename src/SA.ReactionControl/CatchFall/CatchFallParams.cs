using System.Runtime.InteropServices;

namespace SA.ReactionControl.CatchFall
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct CatchFallParams
    {
        public readonly float LegsStiffness;
        public readonly float TorsoStiffness;
        public readonly float ArmsStiffness;
        public readonly float ForwardMaxArmOffset;
        public readonly float BackwardsMinArmOffset;
        public readonly float SpinReductionGain;
        public readonly byte UseHeadLook;
        public readonly float Reach;
        public readonly float ProbeLength;
        public readonly float MinExtension;
        public readonly float StiffnessFloor;
        public readonly float GainFloor;
        public readonly float RelaxRate;
        public readonly float RecoverRate;
        public readonly float GainRate;
        public readonly float ArmCapScale;
        public readonly float FallBias;
        public readonly float SpinTorqueScale;
        public readonly float SpinTorqueMax;
        public readonly float ClavFollowRatio;
        public readonly float ElbowRest;
        public readonly float TorqueLimit;
        public readonly float SafeMissStiffness;
        public readonly float SpineTwistLimit;

        public CatchFallParams(
            float legsStiffness,
            float torsoStiffness,
            float armsStiffness,
            float forwardMaxArmOffset,
            float backwardsMinArmOffset,
            float spinReductionGain,
            byte useHeadLook,
            float reach,
            float probeLength,
            float minExtension,
            float stiffnessFloor,
            float gainFloor,
            float relaxRate,
            float recoverRate,
            float gainRate,
            float armCapScale,
            float fallBias,
            float spinTorqueScale,
            float spinTorqueMax,
            float clavFollowRatio,
            float elbowRest,
            float torqueLimit,
            float safeMissStiffness,
            float spineTwistLimit)
        {
            LegsStiffness = legsStiffness;
            TorsoStiffness = torsoStiffness;
            ArmsStiffness = armsStiffness;
            ForwardMaxArmOffset = forwardMaxArmOffset;
            BackwardsMinArmOffset = backwardsMinArmOffset;
            SpinReductionGain = spinReductionGain;
            UseHeadLook = useHeadLook;
            Reach = reach;
            ProbeLength = probeLength;
            MinExtension = minExtension;
            StiffnessFloor = stiffnessFloor;
            GainFloor = gainFloor;
            RelaxRate = relaxRate;
            RecoverRate = recoverRate;
            GainRate = gainRate;
            ArmCapScale = armCapScale;
            FallBias = fallBias;
            SpinTorqueScale = spinTorqueScale;
            SpinTorqueMax = spinTorqueMax;
            ClavFollowRatio = clavFollowRatio;
            ElbowRest = elbowRest;
            TorqueLimit = torqueLimit;
            SafeMissStiffness = safeMissStiffness;
            SpineTwistLimit = spineTwistLimit;
        }

        public static CatchFallParams Standard => new CatchFallParams(
            6.0f, 8.0f, 12.0f, 0.35f, -0.25f, 0.0f, 1,
            0.70f, 2.0f, 0.15f, 0.15f, 0.20f, 0.5f, 2.0f, 1.5f, 1.0f,
            0.5f, 2.0f, 60.0f, 0.5f, 0.35f, 80.0f, 1.0f, 1.0f);
    }
}
