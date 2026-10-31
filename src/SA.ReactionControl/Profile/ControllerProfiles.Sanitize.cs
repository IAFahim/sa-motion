using SA.Motion.Numerics;
using SA.ReactionControl.Brace;
using SA.ReactionControl.CatchFall;

namespace SA.ReactionControl.Profile
{
    public static partial class ControllerProfiles
    {
        public static ControllerProfile Sanitize(in ControllerProfile profile)
        {
            ControllerProfile standard = Standard;
            ControllerProfile result = profile;
            SanitizeBalance(ref result, in profile, in standard);
            SanitizeBodyBalance(ref result, in profile, in standard);
            SanitizeCatchFall(ref result, in profile, in standard);
            SanitizeBrace(ref result, in profile, in standard);
            SanitizeDrive(ref result, in profile, in standard);
            SanitizeCoordination(ref result, in profile, in standard);
            SanitizeGetUp(ref result, in profile, in standard);
            return result;
        }

        static float Fix(float value, float standard, float minimum, float maximum, bool banZero)
        {
            if (Classify(value, minimum, maximum, banZero) == 0)
            {
                return value;
            }
            return standard;
        }

        static int Fix(int value, int standard, int minimum, int maximum)
        {
            if (Classify(value, minimum, maximum) == 0)
            {
                return value;
            }
            return standard;
        }

        static uint Fix(uint value, uint standard, uint minimum, uint maximum)
        {
            if (Classify(value, minimum, maximum) == 0)
            {
                return value;
            }
            return standard;
        }

        static byte FixFlag(byte value, byte standard)
        {
            if (ClassifyFlag(value) == 0)
            {
                return value;
            }
            return standard;
        }

        static Float3 FixVector(Float3 value, Float3 standard)
        {
            if (ClassifyVector(value) == 0)
            {
                return value;
            }
            return standard;
        }

        static void SanitizeBalance(ref ControllerProfile result, in ControllerProfile profile, in ControllerProfile standard)
        {
            result.Balance.Up = FixVector(profile.Balance.Up, standard.Balance.Up);
            result.Balance.GravityUp = FixVector(profile.Balance.GravityUp, standard.Balance.GravityUp);
            result.Balance.DeltaTime = Fix(profile.Balance.DeltaTime, standard.Balance.DeltaTime, 0f, 1f, true);
            result.Balance.PelvisHeight = Fix(profile.Balance.PelvisHeight, standard.Balance.PelvisHeight, 0f, 3f, true);
            result.Balance.LegLength = Fix(profile.Balance.LegLength, standard.Balance.LegLength, 0f, 3f, true);
            result.Balance.HipSpread = Fix(profile.Balance.HipSpread, standard.Balance.HipSpread, 0f, 1f, false);
            result.Balance.FootWidth = Fix(profile.Balance.FootWidth, standard.Balance.FootWidth, 0f, 1f, true);
            result.Balance.StepOffset = Fix(profile.Balance.StepOffset, standard.Balance.StepOffset, 0f, 1f, false);
            result.Balance.StepLead = Fix(profile.Balance.StepLead, standard.Balance.StepLead, 0f, 2f, false);
            result.Balance.SwingLeadGain = Fix(profile.Balance.SwingLeadGain, standard.Balance.SwingLeadGain, 0f, 10f, false);
            result.Balance.SupportInsideMargin = Fix(profile.Balance.SupportInsideMargin, standard.Balance.SupportInsideMargin, -1f, 0f, false);
            result.Balance.WideStanceRelease = Fix(profile.Balance.WideStanceRelease, standard.Balance.WideStanceRelease, 0f, 2f, false);
            result.Balance.YawCenter = Fix(profile.Balance.YawCenter, standard.Balance.YawCenter, -3.2f, 3.2f, false);
            result.Balance.YawHalfSpan = Fix(profile.Balance.YawHalfSpan, standard.Balance.YawHalfSpan, 0f, 3.2f, true);
            result.Balance.LeanBias = Fix(profile.Balance.LeanBias, standard.Balance.LeanBias, -3.2f, 3.2f, false);
            result.Balance.StepHeight = Fix(profile.Balance.StepHeight, standard.Balance.StepHeight, 0f, 1f, false);
            result.Balance.StepClamp = Fix(profile.Balance.StepClamp, standard.Balance.StepClamp, 0f, 2f, true);
            result.Balance.AbortThreshold = Fix(profile.Balance.AbortThreshold, standard.Balance.AbortThreshold, 0f, 10f, false);
            result.Balance.LegStiffness = Fix(profile.Balance.LegStiffness, standard.Balance.LegStiffness, 0f, 10f, false);
            result.Balance.MaxStepDelay = Fix(profile.Balance.MaxStepDelay, standard.Balance.MaxStepDelay, 1, 600);
            result.Balance.FootSupportHalfWidth = Fix(profile.Balance.FootSupportHalfWidth, standard.Balance.FootSupportHalfWidth, 0f, 1f, true);
            result.Balance.FootSupportHalfLength = Fix(profile.Balance.FootSupportHalfLength, standard.Balance.FootSupportHalfLength, 0f, 1f, true);
            result.Balance.ReachBias = Fix(profile.Balance.ReachBias, standard.Balance.ReachBias, 0f, 1f, false);
            result.Balance.ReachForwardScale = Fix(profile.Balance.ReachForwardScale, standard.Balance.ReachForwardScale, 0f, 5f, true);
            result.Balance.ReachLateralScale = Fix(profile.Balance.ReachLateralScale, standard.Balance.ReachLateralScale, 0f, 5f, true);
            result.Balance.Epsilon = Fix(profile.Balance.Epsilon, standard.Balance.Epsilon, 0f, 0.01f, true);
            result.Balance.LiftGain = Fix(profile.Balance.LiftGain, standard.Balance.LiftGain, 0f, 10f, false);
            result.Balance.SoleOffset = Fix(profile.Balance.SoleOffset, standard.Balance.SoleOffset, 0f, 0.5f, false);
            result.Balance.AnkleBackstepBias = Fix(profile.Balance.AnkleBackstepBias, standard.Balance.AnkleBackstepBias, 0f, 1f, false);
            result.Balance.AirborneLift = Fix(profile.Balance.AirborneLift, standard.Balance.AirborneLift, 0f, 1f, false);
            result.Balance.YawPredictHorizon = Fix(profile.Balance.YawPredictHorizon, standard.Balance.YawPredictHorizon, 0f, 2f, false);
            result.Balance.YawPredictGain = Fix(profile.Balance.YawPredictGain, standard.Balance.YawPredictGain, 0f, 10f, false);
            result.Balance.YawValidRange = Fix(profile.Balance.YawValidRange, standard.Balance.YawValidRange, 0f, 3.2f, true);
            result.Balance.YawBlendRate = Fix(profile.Balance.YawBlendRate, standard.Balance.YawBlendRate, 0f, 1f, false);
            result.Balance.YawLimit = Fix(profile.Balance.YawLimit, standard.Balance.YawLimit, 0f, 3.2f, true);
            result.Balance.PelvisLeanForwardLimit = Fix(profile.Balance.PelvisLeanForwardLimit, standard.Balance.PelvisLeanForwardLimit, 0f, 1.6f, true);
            result.Balance.PelvisLeanBackLimit = Fix(profile.Balance.PelvisLeanBackLimit, standard.Balance.PelvisLeanBackLimit, 0f, 1.6f, true);
            result.Balance.PelvisLeadGain = Fix(profile.Balance.PelvisLeadGain, standard.Balance.PelvisLeadGain, 0f, 10f, false);
            result.Balance.LateralRespond = Fix(profile.Balance.LateralRespond, standard.Balance.LateralRespond, 0f, 10f, false);
            result.Balance.LateralLeanGain = Fix(profile.Balance.LateralLeanGain, standard.Balance.LateralLeanGain, 0f, 10f, false);
            result.Balance.LateralScale = Fix(profile.Balance.LateralScale, standard.Balance.LateralScale, 0f, 10f, false);
            result.Balance.ForwardRespond = Fix(profile.Balance.ForwardRespond, standard.Balance.ForwardRespond, 0f, 10f, false);
            result.Balance.ForwardLeanGain = Fix(profile.Balance.ForwardLeanGain, standard.Balance.ForwardLeanGain, 0f, 10f, false);
            result.Balance.ForwardScale = Fix(profile.Balance.ForwardScale, standard.Balance.ForwardScale, 0f, 10f, false);
            result.Balance.LeanAccumulatorRate = Fix(profile.Balance.LeanAccumulatorRate, standard.Balance.LeanAccumulatorRate, 0f, 1f, false);
            result.Balance.ToeUprightBand = Fix(profile.Balance.ToeUprightBand, standard.Balance.ToeUprightBand, 0f, 1f, false);
            result.Balance.ToeUprightGain = Fix(profile.Balance.ToeUprightGain, standard.Balance.ToeUprightGain, 0f, 20f, false);
            result.Balance.MinLeadDistance = Fix(profile.Balance.MinLeadDistance, standard.Balance.MinLeadDistance, 0f, 2f, false);
            result.Balance.MaxStepCorrection = Fix(profile.Balance.MaxStepCorrection, standard.Balance.MaxStepCorrection, 0f, 1f, true);
            result.Balance.SwingMinDistance = Fix(profile.Balance.SwingMinDistance, standard.Balance.SwingMinDistance, 0f, 2f, false);
            result.Balance.SwingBlendBase = Fix(profile.Balance.SwingBlendBase, standard.Balance.SwingBlendBase, 0f, 20f, true);
            result.Balance.SwingReach = Fix(profile.Balance.SwingReach, standard.Balance.SwingReach, 0f, 10f, false);
            result.Balance.SwingAheadHorizon = Fix(profile.Balance.SwingAheadHorizon, standard.Balance.SwingAheadHorizon, 0f, 2f, false);
            result.Balance.SwingLiftGain = Fix(profile.Balance.SwingLiftGain, standard.Balance.SwingLiftGain, 0f, 10f, false);
            result.Balance.SwingLiftBase = Fix(profile.Balance.SwingLiftBase, standard.Balance.SwingLiftBase, 0f, 2f, false);
            result.Balance.SwingLiftMin = Fix(profile.Balance.SwingLiftMin, standard.Balance.SwingLiftMin, 0f, 2f, false);
            result.Balance.SwingLiftMax = Fix(profile.Balance.SwingLiftMax, standard.Balance.SwingLiftMax, 0f, 2f, true);
            result.Balance.ToeAlignGain = Fix(profile.Balance.ToeAlignGain, standard.Balance.ToeAlignGain, 0f, 10f, false);
            result.Balance.ShinAlignGain = Fix(profile.Balance.ShinAlignGain, standard.Balance.ShinAlignGain, 0f, 10f, false);
            result.Balance.ToeSpinFactor = Fix(profile.Balance.ToeSpinFactor, standard.Balance.ToeSpinFactor, 0f, 10f, false);
            result.Balance.AnkleSwingOffset = Fix(profile.Balance.AnkleSwingOffset, standard.Balance.AnkleSwingOffset, -1f, 1f, false);
            result.Balance.DropGain = Fix(profile.Balance.DropGain, standard.Balance.DropGain, 0f, 20f, false);
            result.Balance.DropCapFraction = Fix(profile.Balance.DropCapFraction, standard.Balance.DropCapFraction, 0f, 1f, false);
            result.Balance.SingleSupportSink = Fix(profile.Balance.SingleSupportSink, standard.Balance.SingleSupportSink, 0f, 2f, false);
            result.Balance.LeanClamp = Fix(profile.Balance.LeanClamp, standard.Balance.LeanClamp, 0f, 1f, true);
            result.Balance.LateralLeanRange = Fix(profile.Balance.LateralLeanRange, standard.Balance.LateralLeanRange, 0f, 1f, true);
            result.Balance.ForwardLeanRange = Fix(profile.Balance.ForwardLeanRange, standard.Balance.ForwardLeanRange, 0f, 1f, true);
            result.Balance.CentrePreviewTime = Fix(profile.Balance.CentrePreviewTime, standard.Balance.CentrePreviewTime, 0f, 2f, false);
            result.Balance.HipHalfTwist = Fix(profile.Balance.HipHalfTwist, standard.Balance.HipHalfTwist, 0f, 3.2f, true);
            result.Balance.HipHalfLean1 = Fix(profile.Balance.HipHalfLean1, standard.Balance.HipHalfLean1, 0f, 3.2f, true);
            result.Balance.HipHalfLean2 = Fix(profile.Balance.HipHalfLean2, standard.Balance.HipHalfLean2, 0f, 3.2f, true);
            result.Balance.HipMidTwist = Fix(profile.Balance.HipMidTwist, standard.Balance.HipMidTwist, -3.2f, 3.2f, false);
            result.Balance.HipMidLean1 = Fix(profile.Balance.HipMidLean1, standard.Balance.HipMidLean1, -3.2f, 3.2f, false);
            result.Balance.HipMidLean2 = Fix(profile.Balance.HipMidLean2, standard.Balance.HipMidLean2, -3.2f, 3.2f, false);
            result.Balance.KneeHalfRange = Fix(profile.Balance.KneeHalfRange, standard.Balance.KneeHalfRange, 0f, 3.2f, true);
            result.Balance.KneeMid = Fix(profile.Balance.KneeMid, standard.Balance.KneeMid, -3.2f, 3.2f, false);
            result.Balance.AnkleHalfPitch = Fix(profile.Balance.AnkleHalfPitch, standard.Balance.AnkleHalfPitch, 0f, 3.2f, true);
            result.Balance.AnkleMidPitch = Fix(profile.Balance.AnkleMidPitch, standard.Balance.AnkleMidPitch, -3.2f, 3.2f, false);
            result.Balance.AnkleHalfYaw = Fix(profile.Balance.AnkleHalfYaw, standard.Balance.AnkleHalfYaw, 0f, 3.2f, true);
            result.Balance.AnkleMidYaw = Fix(profile.Balance.AnkleMidYaw, standard.Balance.AnkleMidYaw, -3.2f, 3.2f, false);
            result.Balance.SpineHalfLean = Fix(profile.Balance.SpineHalfLean, standard.Balance.SpineHalfLean, 0f, 3.2f, true);
            result.Balance.SpineMidLean = Fix(profile.Balance.SpineMidLean, standard.Balance.SpineMidLean, -3.2f, 3.2f, false);
            result.Balance.HipStrength = Fix(profile.Balance.HipStrength, standard.Balance.HipStrength, 0f, 10000f, true);
            result.Balance.HipDamping = Fix(profile.Balance.HipDamping, standard.Balance.HipDamping, 0f, 1000f, false);
            result.Balance.KneeStrength = Fix(profile.Balance.KneeStrength, standard.Balance.KneeStrength, 0f, 10000f, true);
            result.Balance.KneeDamping = Fix(profile.Balance.KneeDamping, standard.Balance.KneeDamping, 0f, 1000f, false);
            result.Balance.AnkleStrength = Fix(profile.Balance.AnkleStrength, standard.Balance.AnkleStrength, 0f, 10000f, true);
            result.Balance.AnkleDamping = Fix(profile.Balance.AnkleDamping, standard.Balance.AnkleDamping, 0f, 1000f, false);
            result.Balance.SpineStrength = Fix(profile.Balance.SpineStrength, standard.Balance.SpineStrength, 0f, 10000f, true);
            result.Balance.SpineDamping = Fix(profile.Balance.SpineDamping, standard.Balance.SpineDamping, 0f, 1000f, false);
            result.Balance.MaxTorque = Fix(profile.Balance.MaxTorque, standard.Balance.MaxTorque, 0f, 10000f, true);
        }

        static void SanitizeBodyBalance(ref ControllerProfile result, in ControllerProfile profile, in ControllerProfile standard)
        {
            result.BodyBalance.BodyStiffness = Fix(profile.BodyBalance.BodyStiffness, standard.BodyBalance.BodyStiffness, 0f, 100f, true);
            result.BodyBalance.ElbowGain = Fix(profile.BodyBalance.ElbowGain, standard.BodyBalance.ElbowGain, 0f, 5f, false);
            result.BodyBalance.Damping = Fix(profile.BodyBalance.Damping, standard.BodyBalance.Damping, 0f, 5f, false);
            result.BodyBalance.MaxSteps = Fix(profile.BodyBalance.MaxSteps, standard.BodyBalance.MaxSteps, 1u, 16u);
            result.BodyBalance.UpperStiffness = Fix(profile.BodyBalance.UpperStiffness, standard.BodyBalance.UpperStiffness, 0f, 100f, true);
            result.BodyBalance.SomersaultThreshold = Fix(profile.BodyBalance.SomersaultThreshold, standard.BodyBalance.SomersaultThreshold, 0f, 3.2f, false);
            result.BodyBalance.LeanRateThreshold = Fix(profile.BodyBalance.LeanRateThreshold, standard.BodyBalance.LeanRateThreshold, 0f, 20f, false);
            result.BodyBalance.LeanRateThresholdY = Fix(profile.BodyBalance.LeanRateThresholdY, standard.BodyBalance.LeanRateThresholdY, 0f, 20f, false);
            result.BodyBalance.RateGainXZ = Fix(profile.BodyBalance.RateGainXZ, standard.BodyBalance.RateGainXZ, 0f, 10f, false);
            result.BodyBalance.ElbowResponseGain = Fix(profile.BodyBalance.ElbowResponseGain, standard.BodyBalance.ElbowResponseGain, 0f, 10f, false);
            result.BodyBalance.LeanFloor = Fix(profile.BodyBalance.LeanFloor, standard.BodyBalance.LeanFloor, 0f, 1f, false);
            result.BodyBalance.SpineMuscle = Fix(profile.BodyBalance.SpineMuscle, standard.BodyBalance.SpineMuscle, 0f, 100f, true);
            result.BodyBalance.ShoulderHipRate = Fix(profile.BodyBalance.ShoulderHipRate, standard.BodyBalance.ShoulderHipRate, 0f, 10f, false);
            result.BodyBalance.TurnIntervalSpan = Fix(profile.BodyBalance.TurnIntervalSpan, standard.BodyBalance.TurnIntervalSpan, 0f, 30f, false);
            result.BodyBalance.TurnIntervalBase = Fix(profile.BodyBalance.TurnIntervalBase, standard.BodyBalance.TurnIntervalBase, 0f, 30f, false);
            result.BodyBalance.TurnOffProb = Fix(profile.BodyBalance.TurnOffProb, standard.BodyBalance.TurnOffProb, 0f, 1f, false);
            result.BodyBalance.TurnToTargetProb = Fix(profile.BodyBalance.TurnToTargetProb, standard.BodyBalance.TurnToTargetProb, 0f, 1f, false);
            result.BodyBalance.TurnToVelocityProb = Fix(profile.BodyBalance.TurnToVelocityProb, standard.BodyBalance.TurnToVelocityProb, 0f, 1f, false);
            result.BodyBalance.TurnAwayProb = Fix(profile.BodyBalance.TurnAwayProb, standard.BodyBalance.TurnAwayProb, 0f, 1f, false);
            result.BodyBalance.TurnSideProb = Fix(profile.BodyBalance.TurnSideProb, standard.BodyBalance.TurnSideProb, 0f, 1f, false);
            result.BodyBalance.TurnProbFloor = Fix(profile.BodyBalance.TurnProbFloor, standard.BodyBalance.TurnProbFloor, 0f, 1f, false);
            result.BodyBalance.TurnSpeedGate = Fix(profile.BodyBalance.TurnSpeedGate, standard.BodyBalance.TurnSpeedGate, 0f, 30f, false);
            result.BodyBalance.SpeedFloor = Fix(profile.BodyBalance.SpeedFloor, standard.BodyBalance.SpeedFloor, 0f, 30f, false);
            result.BodyBalance.TwistGainA = Fix(profile.BodyBalance.TwistGainA, standard.BodyBalance.TwistGainA, 0f, 10f, false);
            result.BodyBalance.CutSpan = Fix(profile.BodyBalance.CutSpan, standard.BodyBalance.CutSpan, 0f, 10f, false);
            result.BodyBalance.RateCap = Fix(profile.BodyBalance.RateCap, standard.BodyBalance.RateCap, 0f, 30f, true);
            result.BodyBalance.ElbowMax = Fix(profile.BodyBalance.ElbowMax, standard.BodyBalance.ElbowMax, 0f, 3.2f, true);
            result.BodyBalance.CatchGainX = Fix(profile.BodyBalance.CatchGainX, standard.BodyBalance.CatchGainX, 0f, 100f, true);
            result.BodyBalance.CatchGainY = Fix(profile.BodyBalance.CatchGainY, standard.BodyBalance.CatchGainY, 0f, 100f, true);
            result.BodyBalance.CatchGainZ = Fix(profile.BodyBalance.CatchGainZ, standard.BodyBalance.CatchGainZ, 0f, 100f, true);
            result.BodyBalance.StepClampLo = Fix(profile.BodyBalance.StepClampLo, standard.BodyBalance.StepClampLo, 0f, 5f, false);
            result.BodyBalance.StepClampHi = Fix(profile.BodyBalance.StepClampHi, standard.BodyBalance.StepClampHi, 0f, 5f, false);
            result.BodyBalance.StabiliseDamping = Fix(profile.BodyBalance.StabiliseDamping, standard.BodyBalance.StabiliseDamping, 0f, 5f, false);
            result.BodyBalance.OwnOffsetX = Fix(profile.BodyBalance.OwnOffsetX, standard.BodyBalance.OwnOffsetX, -2f, 2f, false);
            result.BodyBalance.OwnOffsetY = Fix(profile.BodyBalance.OwnOffsetY, standard.BodyBalance.OwnOffsetY, -2f, 2f, false);
            result.BodyBalance.OwnOffsetZ = Fix(profile.BodyBalance.OwnOffsetZ, standard.BodyBalance.OwnOffsetZ, -2f, 2f, false);
            result.BodyBalance.ArmsOutMinLean = Fix(profile.BodyBalance.ArmsOutMinLean, standard.BodyBalance.ArmsOutMinLean, -3.2f, 3.2f, false);
            result.BodyBalance.ElbowContactAngle = Fix(profile.BodyBalance.ElbowContactAngle, standard.BodyBalance.ElbowContactAngle, 0f, 3.2f, false);
            result.BodyBalance.DecisionLoseLean = Fix(profile.BodyBalance.DecisionLoseLean, standard.BodyBalance.DecisionLoseLean, 0f, 3.2f, false);
            result.BodyBalance.DecisionFallLean = Fix(profile.BodyBalance.DecisionFallLean, standard.BodyBalance.DecisionFallLean, 0f, 3.2f, false);
            result.BodyBalance.DecisionExitFraction = Fix(profile.BodyBalance.DecisionExitFraction, standard.BodyBalance.DecisionExitFraction, 0f, 1f, false);
            result.BodyBalance.FallHoldTime = Fix(profile.BodyBalance.FallHoldTime, standard.BodyBalance.FallHoldTime, 0f, 30f, false);
            result.BodyBalance.LookProbeRange = Fix(profile.BodyBalance.LookProbeRange, standard.BodyBalance.LookProbeRange, 0f, 100f, true);
            result.BodyBalance.MaxDrive = Fix(profile.BodyBalance.MaxDrive, standard.BodyBalance.MaxDrive, 0f, 10000f, true);
            result.BodyBalance.UprightCorrect = Fix(profile.BodyBalance.UprightCorrect, standard.BodyBalance.UprightCorrect, 0f, 10f, false);
            result.BodyBalance.TwistCorrect = Fix(profile.BodyBalance.TwistCorrect, standard.BodyBalance.TwistCorrect, 0f, 10f, false);
            result.BodyBalance.TorsoPitchBias = Fix(profile.BodyBalance.TorsoPitchBias, standard.BodyBalance.TorsoPitchBias, -3.2f, 3.2f, false);
            result.BodyBalance.LookVelLead = Fix(profile.BodyBalance.LookVelLead, standard.BodyBalance.LookVelLead, 0f, 10f, false);
            result.BodyBalance.UseHeadLook = FixFlag(profile.BodyBalance.UseHeadLook, standard.BodyBalance.UseHeadLook);
            result.BodyBalance.UseArmsOut = FixFlag(profile.BodyBalance.UseArmsOut, standard.BodyBalance.UseArmsOut);
            result.BodyBalance.UseBodyTurn = FixFlag(profile.BodyBalance.UseBodyTurn, standard.BodyBalance.UseBodyTurn);
            result.BodyBalance.UsePelvisFrame = FixFlag(profile.BodyBalance.UsePelvisFrame, standard.BodyBalance.UsePelvisFrame);
        }

        static void SanitizeCatchFall(ref ControllerProfile result, in ControllerProfile profile, in ControllerProfile standard)
        {
            result.CatchFall = new CatchFallParams(
                Fix(profile.CatchFall.LegsStiffness, standard.CatchFall.LegsStiffness, 0f, 1000f, true),
                Fix(profile.CatchFall.TorsoStiffness, standard.CatchFall.TorsoStiffness, 0f, 1000f, true),
                Fix(profile.CatchFall.ArmsStiffness, standard.CatchFall.ArmsStiffness, 0f, 1000f, true),
                Fix(profile.CatchFall.ForwardMaxArmOffset, standard.CatchFall.ForwardMaxArmOffset, -2f, 2f, false),
                Fix(profile.CatchFall.BackwardsMinArmOffset, standard.CatchFall.BackwardsMinArmOffset, -2f, 2f, false),
                Fix(profile.CatchFall.SpinReductionGain, standard.CatchFall.SpinReductionGain, 0f, 10f, false),
                FixFlag(profile.CatchFall.UseHeadLook, standard.CatchFall.UseHeadLook),
                Fix(profile.CatchFall.Reach, standard.CatchFall.Reach, 0f, 5f, true),
                Fix(profile.CatchFall.ProbeLength, standard.CatchFall.ProbeLength, 0f, 20f, true),
                Fix(profile.CatchFall.MinExtension, standard.CatchFall.MinExtension, 0f, 5f, false),
                Fix(profile.CatchFall.StiffnessFloor, standard.CatchFall.StiffnessFloor, 0f, 1f, false),
                Fix(profile.CatchFall.GainFloor, standard.CatchFall.GainFloor, 0f, 1f, false),
                Fix(profile.CatchFall.RelaxRate, standard.CatchFall.RelaxRate, 0f, 30f, false),
                Fix(profile.CatchFall.RecoverRate, standard.CatchFall.RecoverRate, 0f, 30f, false),
                Fix(profile.CatchFall.GainRate, standard.CatchFall.GainRate, 0f, 30f, false),
                Fix(profile.CatchFall.ArmCapScale, standard.CatchFall.ArmCapScale, 0f, 10f, true),
                Fix(profile.CatchFall.FallBias, standard.CatchFall.FallBias, 0f, 10f, false),
                Fix(profile.CatchFall.SpinTorqueScale, standard.CatchFall.SpinTorqueScale, 0f, 20f, false),
                Fix(profile.CatchFall.SpinTorqueMax, standard.CatchFall.SpinTorqueMax, 0f, 10000f, true),
                Fix(profile.CatchFall.ClavFollowRatio, standard.CatchFall.ClavFollowRatio, 0f, 1f, false),
                Fix(profile.CatchFall.ElbowRest, standard.CatchFall.ElbowRest, 0f, 3.2f, false),
                Fix(profile.CatchFall.TorqueLimit, standard.CatchFall.TorqueLimit, 0f, 10000f, true),
                Fix(profile.CatchFall.SafeMissStiffness, standard.CatchFall.SafeMissStiffness, 0f, 1000f, true),
                Fix(profile.CatchFall.SpineTwistLimit, standard.CatchFall.SpineTwistLimit, 0f, 3.2f, true));
        }

        static void SanitizeBrace(ref ControllerProfile result, in ControllerProfile profile, in ControllerProfile standard)
        {
            result.Brace = new BraceParams(
                Fix(profile.Brace.BodyStiffness, standard.Brace.BodyStiffness, 0f, 1000f, true),
                Fix(profile.Brace.ArmDamping, standard.Brace.ArmDamping, 0f, 100f, false),
                Fix(profile.Brace.WristStiffness, standard.Brace.WristStiffness, 0f, 1000f, true),
                Fix(profile.Brace.WristDamping, standard.Brace.WristDamping, 0f, 100f, false),
                Fix(profile.Brace.MaxTorque, standard.Brace.MaxTorque, 0f, 10000f, true),
                Fix(profile.Brace.BraceDistance, standard.Brace.BraceDistance, 0f, 10f, false),
                Fix(profile.Brace.MaxReach, standard.Brace.MaxReach, 0f, 3f, true),
                Fix(profile.Brace.PredictionTime, standard.Brace.PredictionTime, 0f, 5f, false),
                Fix(profile.Brace.ReachAbsorptionTime, standard.Brace.ReachAbsorptionTime, 0f, 5f, false),
                Fix(profile.Brace.TargetSpeedLimit, standard.Brace.TargetSpeedLimit, 0f, 30f, true),
                Fix(profile.Brace.ApproachFloor, standard.Brace.ApproachFloor, 0f, 5f, false),
                Fix(profile.Brace.HysteresisBand, standard.Brace.HysteresisBand, 0f, 5f, false),
                Fix(profile.Brace.ReleaseMargin, standard.Brace.ReleaseMargin, 0f, 5f, false),
                Fix(profile.Brace.ContactHoldTime, standard.Brace.ContactHoldTime, 0f, 30f, false),
                Fix(profile.Brace.SideSpread, standard.Brace.SideSpread, 0f, 2f, false),
                Fix(profile.Brace.StaggerTime, standard.Brace.StaggerTime, 0f, 5f, false),
                Fix(profile.Brace.CrouchAmount, standard.Brace.CrouchAmount, 0f, 1f, false),
                Fix(profile.Brace.LeanGain, standard.Brace.LeanGain, 0f, 10f, false),
                Fix(profile.Brace.GuardStiffness, standard.Brace.GuardStiffness, 0f, 1000f, true),
                Fix(profile.Brace.GuardDamping, standard.Brace.GuardDamping, 0f, 100f, false));
        }

        static void SanitizeDrive(ref ControllerProfile result, in ControllerProfile profile, in ControllerProfile standard)
        {
            result.Drive.Strength = Fix(profile.Drive.Strength, standard.Drive.Strength, 0f, 10000f, true);
            result.Drive.Damping = Fix(profile.Drive.Damping, standard.Drive.Damping, 0f, 100f, false);
            result.Drive.MaxTorque = Fix(profile.Drive.MaxTorque, standard.Drive.MaxTorque, 0f, 10000f, true);
            result.Drive.MinDamping = Fix(profile.Drive.MinDamping, standard.Drive.MinDamping, 0f, 100f, false);
            result.Drive.StiffnessCap = Fix(profile.Drive.StiffnessCap, standard.Drive.StiffnessCap, 0f, 100000f, true);
            result.Drive.LimitMargin = Fix(profile.Drive.LimitMargin, standard.Drive.LimitMargin, 0f, 1f, false);
            result.Drive.SoftPull = Fix(profile.Drive.SoftPull, standard.Drive.SoftPull, 0f, 1f, false);
        }

        static void SanitizeCoordination(ref ControllerProfile result, in ControllerProfile profile, in ControllerProfile standard)
        {
            result.Coordination.HysteresisMargin = Fix(profile.Coordination.HysteresisMargin, standard.Coordination.HysteresisMargin, 0f, 100f, false);
            result.Coordination.FadeTime = Fix(profile.Coordination.FadeTime, standard.Coordination.FadeTime, 0f, 30f, false);
            result.Coordination.MinLegStiffness = Fix(profile.Coordination.MinLegStiffness, standard.Coordination.MinLegStiffness, 0f, 10000f, true);
            result.Coordination.MaxLegStiffness = Fix(profile.Coordination.MaxLegStiffness, standard.Coordination.MaxLegStiffness, 0f, 10000f, true);
            result.Coordination.SupportedAnkleResponse = Fix(profile.Coordination.SupportedAnkleResponse, standard.Coordination.SupportedAnkleResponse, 0f, 20f, false);
            result.Coordination.SupportedKneeResponse = Fix(profile.Coordination.SupportedKneeResponse, standard.Coordination.SupportedKneeResponse, 0f, 20f, false);
            result.Coordination.SwingAnkleResponse = Fix(profile.Coordination.SwingAnkleResponse, standard.Coordination.SwingAnkleResponse, 0f, 20f, false);
            result.Coordination.SwingKneeResponse = Fix(profile.Coordination.SwingKneeResponse, standard.Coordination.SwingKneeResponse, 0f, 20f, false);
        }

        static void SanitizeGetUp(ref ControllerProfile result, in ControllerProfile profile, in ControllerProfile standard)
        {
            result.GetUp.LyingHeadDrop = Fix(profile.GetUp.LyingHeadDrop, standard.GetUp.LyingHeadDrop, 0f, 3f, false);
            result.GetUp.UprightHeadRise = Fix(profile.GetUp.UprightHeadRise, standard.GetUp.UprightHeadRise, 0f, 3f, false);
            result.GetUp.PoseDot = Fix(profile.GetUp.PoseDot, standard.GetUp.PoseDot, -1f, 1f, false);
            result.GetUp.MaxAlignOffset = Fix(profile.GetUp.MaxAlignOffset, standard.GetUp.MaxAlignOffset, 0f, 50f, true);
            result.GetUp.ClearDistance = Fix(profile.GetUp.ClearDistance, standard.GetUp.ClearDistance, 0f, 50f, true);
        }
    }
}
