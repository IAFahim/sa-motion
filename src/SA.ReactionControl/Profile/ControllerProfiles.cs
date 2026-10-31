using SA.Motion.Numerics;
using SA.ReactionControl.Balance;
using SA.ReactionControl.Brace;
using SA.ReactionControl.CatchFall;
using SA.ReactionControl.Coordination;
using SA.ReactionControl.Drive;

namespace SA.ReactionControl.Profile
{
    public static partial class ControllerProfiles
    {
        public const float ReferenceMass = 70f;
        public const float ReferenceHeight = 1.75f;

        public static ControllerProfile Standard => new ControllerProfile
        {
            Name = "Standard",
            Balance = BalanceParams.Standard,
            BodyBalance = BodyBalanceParams.Standard,
            CatchFall = CatchFallParams.Standard,
            Brace = BraceParams.Standard,
            Drive = DriveParams.Standard,
            Coordination = CoordinationParams.Standard,
            GetUp = GetUpParams.Standard,
        };

        public static ControllerProfile ForBody(float totalMass, float height)
        {
            ControllerProfile standard = Standard;
            if (!Scalar.IsFinite(totalMass) || !Scalar.IsFinite(height) || totalMass <= 0f || height <= 0f)
            {
                return standard;
            }
            float massFactor = totalMass / ReferenceMass;
            float heightFactor = height / ReferenceHeight;
            ControllerProfile result = standard;
            ScaleBalanceMass(ref result, massFactor);
            ScaleBalanceLength(ref result, heightFactor);
            ScaleBodyBalanceMass(ref result, massFactor);
            ScaleBodyBalanceLength(ref result, heightFactor);
            ScaleBraceMass(ref result, massFactor);
            ScaleBraceLength(ref result, heightFactor);
            ScaleCatchFallMass(ref result, massFactor);
            ScaleCatchFallLength(ref result, heightFactor);
            ScaleCoordinationMass(ref result, massFactor);
            ScaleDriveMass(ref result, massFactor);
            ScaleGetUpLength(ref result, heightFactor);
            return result;
        }

        static void ScaleBalanceMass(ref ControllerProfile result, float massFactor)
        {
            result.Balance.HipStrength *= massFactor;
            result.Balance.HipDamping *= massFactor;
            result.Balance.KneeStrength *= massFactor;
            result.Balance.KneeDamping *= massFactor;
            result.Balance.AnkleStrength *= massFactor;
            result.Balance.AnkleDamping *= massFactor;
            result.Balance.SpineStrength *= massFactor;
            result.Balance.SpineDamping *= massFactor;
            result.Balance.MaxTorque *= massFactor;
        }

        static void ScaleBalanceLength(ref ControllerProfile result, float heightFactor)
        {
            result.Balance.PelvisHeight *= heightFactor;
            result.Balance.LegLength *= heightFactor;
            result.Balance.HipSpread *= heightFactor;
            result.Balance.FootWidth *= heightFactor;
            result.Balance.StepOffset *= heightFactor;
            result.Balance.WideStanceRelease *= heightFactor;
            result.Balance.StepHeight *= heightFactor;
            result.Balance.StepClamp *= heightFactor;
            result.Balance.MaxStepCorrection *= heightFactor;
            result.Balance.FootSupportHalfWidth *= heightFactor;
            result.Balance.FootSupportHalfLength *= heightFactor;
            result.Balance.ReachBias *= heightFactor;
            result.Balance.SoleOffset *= heightFactor;
            result.Balance.AnkleBackstepBias *= heightFactor;
            result.Balance.AirborneLift *= heightFactor;
            result.Balance.SwingMinDistance *= heightFactor;
            result.Balance.SwingLiftBase *= heightFactor;
            result.Balance.SwingLiftMin *= heightFactor;
            result.Balance.SwingLiftMax *= heightFactor;
            result.Balance.SingleSupportSink *= heightFactor;
            result.Balance.MinLeadDistance *= heightFactor;
        }

        static void ScaleBodyBalanceMass(ref ControllerProfile result, float massFactor)
        {
            result.BodyBalance.BodyStiffness *= massFactor;
            result.BodyBalance.UpperStiffness *= massFactor;
            result.BodyBalance.SpineMuscle *= massFactor;
            result.BodyBalance.CatchGainX *= massFactor;
            result.BodyBalance.CatchGainY *= massFactor;
            result.BodyBalance.CatchGainZ *= massFactor;
            result.BodyBalance.MaxDrive *= massFactor;
        }

        static void ScaleBodyBalanceLength(ref ControllerProfile result, float heightFactor)
        {
            result.BodyBalance.OwnOffsetX *= heightFactor;
            result.BodyBalance.OwnOffsetY *= heightFactor;
            result.BodyBalance.OwnOffsetZ *= heightFactor;
            result.BodyBalance.LookProbeRange *= heightFactor;
        }

        static void ScaleBraceMass(ref ControllerProfile result, float massFactor)
        {
            var brace = result.Brace;
            result.Brace = new BraceParams(
                brace.BodyStiffness * massFactor,
                brace.ArmDamping * massFactor,
                brace.WristStiffness * massFactor,
                brace.WristDamping * massFactor,
                brace.MaxTorque * massFactor,
                brace.BraceDistance,
                brace.MaxReach,
                brace.PredictionTime,
                brace.ReachAbsorptionTime,
                brace.TargetSpeedLimit,
                brace.ApproachFloor,
                brace.HysteresisBand,
                brace.ReleaseMargin,
                brace.ContactHoldTime,
                brace.SideSpread,
                brace.StaggerTime,
                brace.CrouchAmount,
                brace.LeanGain,
                brace.GuardStiffness * massFactor,
                brace.GuardDamping * massFactor);
        }

        static void ScaleBraceLength(ref ControllerProfile result, float heightFactor)
        {
            var brace = result.Brace;
            result.Brace = new BraceParams(
                brace.BodyStiffness,
                brace.ArmDamping,
                brace.WristStiffness,
                brace.WristDamping,
                brace.MaxTorque,
                brace.BraceDistance * heightFactor,
                brace.MaxReach * heightFactor,
                brace.PredictionTime,
                brace.ReachAbsorptionTime,
                brace.TargetSpeedLimit,
                brace.ApproachFloor * heightFactor,
                brace.HysteresisBand * heightFactor,
                brace.ReleaseMargin * heightFactor,
                brace.ContactHoldTime,
                brace.SideSpread * heightFactor,
                brace.StaggerTime,
                brace.CrouchAmount * heightFactor,
                brace.LeanGain,
                brace.GuardStiffness,
                brace.GuardDamping);
        }

        static void ScaleCatchFallMass(ref ControllerProfile result, float massFactor)
        {
            var catchFall = result.CatchFall;
            result.CatchFall = new CatchFallParams(
                catchFall.LegsStiffness * massFactor,
                catchFall.TorsoStiffness * massFactor,
                catchFall.ArmsStiffness * massFactor,
                catchFall.ForwardMaxArmOffset,
                catchFall.BackwardsMinArmOffset,
                catchFall.SpinReductionGain,
                catchFall.UseHeadLook,
                catchFall.Reach,
                catchFall.ProbeLength,
                catchFall.MinExtension,
                catchFall.StiffnessFloor,
                catchFall.GainFloor,
                catchFall.RelaxRate,
                catchFall.RecoverRate,
                catchFall.GainRate,
                catchFall.ArmCapScale,
                catchFall.FallBias,
                catchFall.SpinTorqueScale,
                catchFall.SpinTorqueMax * massFactor,
                catchFall.ClavFollowRatio,
                catchFall.ElbowRest,
                catchFall.TorqueLimit * massFactor,
                catchFall.SafeMissStiffness * massFactor,
                catchFall.SpineTwistLimit);
        }

        static void ScaleCatchFallLength(ref ControllerProfile result, float heightFactor)
        {
            var catchFall = result.CatchFall;
            result.CatchFall = new CatchFallParams(
                catchFall.LegsStiffness,
                catchFall.TorsoStiffness,
                catchFall.ArmsStiffness,
                catchFall.ForwardMaxArmOffset * heightFactor,
                catchFall.BackwardsMinArmOffset * heightFactor,
                catchFall.SpinReductionGain,
                catchFall.UseHeadLook,
                catchFall.Reach * heightFactor,
                catchFall.ProbeLength * heightFactor,
                catchFall.MinExtension * heightFactor,
                catchFall.StiffnessFloor,
                catchFall.GainFloor,
                catchFall.RelaxRate,
                catchFall.RecoverRate,
                catchFall.GainRate,
                catchFall.ArmCapScale,
                catchFall.FallBias,
                catchFall.SpinTorqueScale,
                catchFall.SpinTorqueMax,
                catchFall.ClavFollowRatio,
                catchFall.ElbowRest,
                catchFall.TorqueLimit,
                catchFall.SafeMissStiffness,
                catchFall.SpineTwistLimit);
        }

        static void ScaleCoordinationMass(ref ControllerProfile result, float massFactor)
        {
            result.Coordination.MinLegStiffness *= massFactor;
            result.Coordination.MaxLegStiffness *= massFactor;
        }

        static void ScaleDriveMass(ref ControllerProfile result, float massFactor)
        {
            result.Drive.MaxTorque *= massFactor;
        }

        static void ScaleGetUpLength(ref ControllerProfile result, float heightFactor)
        {
            result.GetUp.LyingHeadDrop *= heightFactor;
            result.GetUp.UprightHeadRise *= heightFactor;
            result.GetUp.MaxAlignOffset *= heightFactor;
            result.GetUp.ClearDistance *= heightFactor;
        }
    }
}
