using System.Runtime.InteropServices;

namespace SA.ReactionControl.Brace
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct BraceParams
    {
        public readonly float BodyStiffness;
        public readonly float ArmDamping;
        public readonly float WristStiffness;
        public readonly float WristDamping;
        public readonly float MaxTorque;
        public readonly float BraceDistance;
        public readonly float MaxReach;
        public readonly float PredictionTime;
        public readonly float ReachAbsorptionTime;
        public readonly float TargetSpeedLimit;
        public readonly float ApproachFloor;
        public readonly float HysteresisBand;
        public readonly float ReleaseMargin;
        public readonly float ContactHoldTime;
        public readonly float SideSpread;
        public readonly float StaggerTime;
        public readonly float CrouchAmount;
        public readonly float LeanGain;
        public readonly float GuardStiffness;
        public readonly float GuardDamping;

        public BraceParams(
            float bodyStiffness,
            float armDamping,
            float wristStiffness,
            float wristDamping,
            float maxTorque,
            float braceDistance,
            float maxReach,
            float predictionTime,
            float reachAbsorptionTime,
            float targetSpeedLimit,
            float approachFloor,
            float hysteresisBand,
            float releaseMargin,
            float contactHoldTime,
            float sideSpread,
            float staggerTime,
            float crouchAmount,
            float leanGain,
            float guardStiffness,
            float guardDamping)
        {
            BodyStiffness = bodyStiffness;
            ArmDamping = armDamping;
            WristStiffness = wristStiffness;
            WristDamping = wristDamping;
            MaxTorque = maxTorque;
            BraceDistance = braceDistance;
            MaxReach = maxReach;
            PredictionTime = predictionTime;
            ReachAbsorptionTime = reachAbsorptionTime;
            TargetSpeedLimit = targetSpeedLimit;
            ApproachFloor = approachFloor;
            HysteresisBand = hysteresisBand;
            ReleaseMargin = releaseMargin;
            ContactHoldTime = contactHoldTime;
            SideSpread = sideSpread;
            StaggerTime = staggerTime;
            CrouchAmount = crouchAmount;
            LeanGain = leanGain;
            GuardStiffness = guardStiffness;
            GuardDamping = guardDamping;
        }

        public static BraceParams Standard => new BraceParams(
            bodyStiffness: 40f,
            armDamping: 6f,
            wristStiffness: 30f,
            wristDamping: 5f,
            maxTorque: 200f,
            braceDistance: 0.9f,
            maxReach: 0.75f,
            predictionTime: 0.35f,
            reachAbsorptionTime: 0.25f,
            targetSpeedLimit: 3f,
            approachFloor: 0.05f,
            hysteresisBand: 0.05f,
            releaseMargin: 0.08f,
            contactHoldTime: 0.4f,
            sideSpread: 0.12f,
            staggerTime: 0.15f,
            crouchAmount: 0.12f,
            leanGain: 0.5f,
            guardStiffness: 8f,
            guardDamping: 2f);
    }
}
