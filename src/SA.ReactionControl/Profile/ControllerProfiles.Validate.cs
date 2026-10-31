using System;
using System.Collections.Generic;
using SA.Motion.Numerics;

namespace SA.ReactionControl.Profile
{
    public static partial class ControllerProfiles
    {
        public static string[] Validate(in ControllerProfile profile)
        {
            var problems = new List<string>();
            CheckBalance(problems, in profile.Balance);
            CheckBodyBalance(problems, in profile.BodyBalance);
            CheckCatchFall(problems, in profile.CatchFall);
            CheckBrace(problems, in profile.Brace);
            CheckDrive(problems, in profile.Drive);
            CheckCoordination(problems, in profile.Coordination);
            CheckGetUp(problems, in profile.GetUp);
            if (problems.Count == 0)
            {
                return Array.Empty<string>();
            }
            return problems.ToArray();
        }

        static int Classify(float value, float minimum, float maximum, bool banZero)
        {
            if (!Scalar.IsFinite(value))
            {
                return 1;
            }
            if (value < minimum || value > maximum)
            {
                return 2;
            }
            if (banZero && value == 0f)
            {
                return 3;
            }
            return 0;
        }

        static int Classify(int value, int minimum, int maximum)
        {
            if (value < minimum || value > maximum)
            {
                return 2;
            }
            return 0;
        }

        static int Classify(uint value, uint minimum, uint maximum)
        {
            if (value < minimum || value > maximum)
            {
                return 2;
            }
            return 0;
        }

        static int ClassifyFlag(byte value)
        {
            if (value > 1)
            {
                return 2;
            }
            return 0;
        }

        static int ClassifyVector(Float3 value)
        {
            if (!value.IsFinite)
            {
                return 1;
            }
            if (value.LengthSquared < 1e-12f)
            {
                return 3;
            }
            return 0;
        }

        static string Reason(int code, float minimum, float maximum)
        {
            if (code == 1)
            {
                return ": non-finite value";
            }
            if (code == 2)
            {
                return ": out of range [" + minimum + ", " + maximum + "]";
            }
            return ": zero disables behavior";
        }

        static void Float(List<string> problems, string field, float value, float minimum, float maximum, bool banZero)
        {
            int code = Classify(value, minimum, maximum, banZero);
            if (code != 0)
            {
                problems.Add(field + Reason(code, minimum, maximum));
            }
        }

        static void Count(List<string> problems, string field, int value, int minimum, int maximum)
        {
            if (Classify(value, minimum, maximum) != 0)
            {
                problems.Add(field + ": out of range [" + minimum + ", " + maximum + "]");
            }
        }

        static void Count(List<string> problems, string field, uint value, uint minimum, uint maximum)
        {
            if (Classify(value, minimum, maximum) != 0)
            {
                problems.Add(field + ": out of range [" + minimum + ", " + maximum + "]");
            }
        }

        static void Flag(List<string> problems, string field, byte value)
        {
            if (ClassifyFlag(value) != 0)
            {
                problems.Add(field + ": out of range [0, 1]");
            }
        }

        static void Vector(List<string> problems, string field, Float3 value)
        {
            int code = ClassifyVector(value);
            if (code == 1)
            {
                problems.Add(field + ": non-finite value");
            }
            else if (code == 3)
            {
                problems.Add(field + ": zero vector disables orientation");
            }
        }

        static void CheckBalance(List<string> problems, in Balance.BalanceParams p)
        {
            Vector(problems, "Balance.Up", p.Up);
            Vector(problems, "Balance.GravityUp", p.GravityUp);
            Float(problems, "Balance.DeltaTime", p.DeltaTime, 0f, 1f, true);
            Float(problems, "Balance.PelvisHeight", p.PelvisHeight, 0f, 3f, true);
            Float(problems, "Balance.LegLength", p.LegLength, 0f, 3f, true);
            Float(problems, "Balance.HipSpread", p.HipSpread, 0f, 1f, false);
            Float(problems, "Balance.FootWidth", p.FootWidth, 0f, 1f, true);
            Float(problems, "Balance.StepOffset", p.StepOffset, 0f, 1f, false);
            Float(problems, "Balance.StepLead", p.StepLead, 0f, 2f, false);
            Float(problems, "Balance.SwingLeadGain", p.SwingLeadGain, 0f, 10f, false);
            Float(problems, "Balance.SupportInsideMargin", p.SupportInsideMargin, -1f, 0f, false);
            Float(problems, "Balance.WideStanceRelease", p.WideStanceRelease, 0f, 2f, false);
            Float(problems, "Balance.YawCenter", p.YawCenter, -3.2f, 3.2f, false);
            Float(problems, "Balance.YawHalfSpan", p.YawHalfSpan, 0f, 3.2f, true);
            Float(problems, "Balance.LeanBias", p.LeanBias, -3.2f, 3.2f, false);
            Float(problems, "Balance.StepHeight", p.StepHeight, 0f, 1f, false);
            Float(problems, "Balance.StepClamp", p.StepClamp, 0f, 2f, true);
            Float(problems, "Balance.AbortThreshold", p.AbortThreshold, 0f, 10f, false);
            Float(problems, "Balance.LegStiffness", p.LegStiffness, 0f, 10f, false);
            Count(problems, "Balance.MaxStepDelay", p.MaxStepDelay, 1, 600);
            Float(problems, "Balance.FootSupportHalfWidth", p.FootSupportHalfWidth, 0f, 1f, true);
            Float(problems, "Balance.FootSupportHalfLength", p.FootSupportHalfLength, 0f, 1f, true);
            Float(problems, "Balance.ReachBias", p.ReachBias, 0f, 1f, false);
            Float(problems, "Balance.ReachForwardScale", p.ReachForwardScale, 0f, 5f, true);
            Float(problems, "Balance.ReachLateralScale", p.ReachLateralScale, 0f, 5f, true);
            Float(problems, "Balance.Epsilon", p.Epsilon, 0f, 0.01f, true);
            Float(problems, "Balance.LiftGain", p.LiftGain, 0f, 10f, false);
            Float(problems, "Balance.SoleOffset", p.SoleOffset, 0f, 0.5f, false);
            Float(problems, "Balance.AnkleBackstepBias", p.AnkleBackstepBias, 0f, 1f, false);
            Float(problems, "Balance.AirborneLift", p.AirborneLift, 0f, 1f, false);
            Float(problems, "Balance.YawPredictHorizon", p.YawPredictHorizon, 0f, 2f, false);
            Float(problems, "Balance.YawPredictGain", p.YawPredictGain, 0f, 10f, false);
            Float(problems, "Balance.YawValidRange", p.YawValidRange, 0f, 3.2f, true);
            Float(problems, "Balance.YawBlendRate", p.YawBlendRate, 0f, 1f, false);
            Float(problems, "Balance.YawLimit", p.YawLimit, 0f, 3.2f, true);
            Float(problems, "Balance.PelvisLeanForwardLimit", p.PelvisLeanForwardLimit, 0f, 1.6f, true);
            Float(problems, "Balance.PelvisLeanBackLimit", p.PelvisLeanBackLimit, 0f, 1.6f, true);
            Float(problems, "Balance.PelvisLeadGain", p.PelvisLeadGain, 0f, 10f, false);
            Float(problems, "Balance.LateralRespond", p.LateralRespond, 0f, 10f, false);
            Float(problems, "Balance.LateralLeanGain", p.LateralLeanGain, 0f, 10f, false);
            Float(problems, "Balance.LateralScale", p.LateralScale, 0f, 10f, false);
            Float(problems, "Balance.ForwardRespond", p.ForwardRespond, 0f, 10f, false);
            Float(problems, "Balance.ForwardLeanGain", p.ForwardLeanGain, 0f, 10f, false);
            Float(problems, "Balance.ForwardScale", p.ForwardScale, 0f, 10f, false);
            Float(problems, "Balance.LeanAccumulatorRate", p.LeanAccumulatorRate, 0f, 1f, false);
            Float(problems, "Balance.ToeUprightBand", p.ToeUprightBand, 0f, 1f, false);
            Float(problems, "Balance.ToeUprightGain", p.ToeUprightGain, 0f, 20f, false);
            Float(problems, "Balance.MinLeadDistance", p.MinLeadDistance, 0f, 2f, false);
            Float(problems, "Balance.MaxStepCorrection", p.MaxStepCorrection, 0f, 1f, true);
            Float(problems, "Balance.SwingMinDistance", p.SwingMinDistance, 0f, 2f, false);
            Float(problems, "Balance.SwingBlendBase", p.SwingBlendBase, 0f, 20f, true);
            Float(problems, "Balance.SwingReach", p.SwingReach, 0f, 10f, false);
            Float(problems, "Balance.SwingAheadHorizon", p.SwingAheadHorizon, 0f, 2f, false);
            Float(problems, "Balance.SwingLiftGain", p.SwingLiftGain, 0f, 10f, false);
            Float(problems, "Balance.SwingLiftBase", p.SwingLiftBase, 0f, 2f, false);
            Float(problems, "Balance.SwingLiftMin", p.SwingLiftMin, 0f, 2f, false);
            Float(problems, "Balance.SwingLiftMax", p.SwingLiftMax, 0f, 2f, true);
            Float(problems, "Balance.ToeAlignGain", p.ToeAlignGain, 0f, 10f, false);
            Float(problems, "Balance.ShinAlignGain", p.ShinAlignGain, 0f, 10f, false);
            Float(problems, "Balance.ToeSpinFactor", p.ToeSpinFactor, 0f, 10f, false);
            Float(problems, "Balance.AnkleSwingOffset", p.AnkleSwingOffset, -1f, 1f, false);
            Float(problems, "Balance.DropGain", p.DropGain, 0f, 20f, false);
            Float(problems, "Balance.DropCapFraction", p.DropCapFraction, 0f, 1f, false);
            Float(problems, "Balance.SingleSupportSink", p.SingleSupportSink, 0f, 2f, false);
            Float(problems, "Balance.LeanClamp", p.LeanClamp, 0f, 1f, true);
            Float(problems, "Balance.LateralLeanRange", p.LateralLeanRange, 0f, 1f, true);
            Float(problems, "Balance.ForwardLeanRange", p.ForwardLeanRange, 0f, 1f, true);
            Float(problems, "Balance.CentrePreviewTime", p.CentrePreviewTime, 0f, 2f, false);
            Float(problems, "Balance.HipHalfTwist", p.HipHalfTwist, 0f, 3.2f, true);
            Float(problems, "Balance.HipHalfLean1", p.HipHalfLean1, 0f, 3.2f, true);
            Float(problems, "Balance.HipHalfLean2", p.HipHalfLean2, 0f, 3.2f, true);
            Float(problems, "Balance.HipMidTwist", p.HipMidTwist, -3.2f, 3.2f, false);
            Float(problems, "Balance.HipMidLean1", p.HipMidLean1, -3.2f, 3.2f, false);
            Float(problems, "Balance.HipMidLean2", p.HipMidLean2, -3.2f, 3.2f, false);
            Float(problems, "Balance.KneeHalfRange", p.KneeHalfRange, 0f, 3.2f, true);
            Float(problems, "Balance.KneeMid", p.KneeMid, -3.2f, 3.2f, false);
            Float(problems, "Balance.AnkleHalfPitch", p.AnkleHalfPitch, 0f, 3.2f, true);
            Float(problems, "Balance.AnkleMidPitch", p.AnkleMidPitch, -3.2f, 3.2f, false);
            Float(problems, "Balance.AnkleHalfYaw", p.AnkleHalfYaw, 0f, 3.2f, true);
            Float(problems, "Balance.AnkleMidYaw", p.AnkleMidYaw, -3.2f, 3.2f, false);
            Float(problems, "Balance.SpineHalfLean", p.SpineHalfLean, 0f, 3.2f, true);
            Float(problems, "Balance.SpineMidLean", p.SpineMidLean, -3.2f, 3.2f, false);
            Float(problems, "Balance.HipStrength", p.HipStrength, 0f, 10000f, true);
            Float(problems, "Balance.HipDamping", p.HipDamping, 0f, 1000f, false);
            Float(problems, "Balance.KneeStrength", p.KneeStrength, 0f, 10000f, true);
            Float(problems, "Balance.KneeDamping", p.KneeDamping, 0f, 1000f, false);
            Float(problems, "Balance.AnkleStrength", p.AnkleStrength, 0f, 10000f, true);
            Float(problems, "Balance.AnkleDamping", p.AnkleDamping, 0f, 1000f, false);
            Float(problems, "Balance.SpineStrength", p.SpineStrength, 0f, 10000f, true);
            Float(problems, "Balance.SpineDamping", p.SpineDamping, 0f, 1000f, false);
            Float(problems, "Balance.MaxTorque", p.MaxTorque, 0f, 10000f, true);
        }

        static void CheckBodyBalance(List<string> problems, in BodyBalanceParams p)
        {
            Float(problems, "BodyBalance.BodyStiffness", p.BodyStiffness, 0f, 100f, true);
            Float(problems, "BodyBalance.ElbowGain", p.ElbowGain, 0f, 5f, false);
            Float(problems, "BodyBalance.Damping", p.Damping, 0f, 5f, false);
            Count(problems, "BodyBalance.MaxSteps", p.MaxSteps, 1u, 16u);
            Float(problems, "BodyBalance.UpperStiffness", p.UpperStiffness, 0f, 100f, true);
            Float(problems, "BodyBalance.SomersaultThreshold", p.SomersaultThreshold, 0f, 3.2f, false);
            Float(problems, "BodyBalance.LeanRateThreshold", p.LeanRateThreshold, 0f, 20f, false);
            Float(problems, "BodyBalance.LeanRateThresholdY", p.LeanRateThresholdY, 0f, 20f, false);
            Float(problems, "BodyBalance.RateGainXZ", p.RateGainXZ, 0f, 10f, false);
            Float(problems, "BodyBalance.ElbowResponseGain", p.ElbowResponseGain, 0f, 10f, false);
            Float(problems, "BodyBalance.LeanFloor", p.LeanFloor, 0f, 1f, false);
            Float(problems, "BodyBalance.SpineMuscle", p.SpineMuscle, 0f, 100f, true);
            Float(problems, "BodyBalance.ShoulderHipRate", p.ShoulderHipRate, 0f, 10f, false);
            Float(problems, "BodyBalance.TurnIntervalSpan", p.TurnIntervalSpan, 0f, 30f, false);
            Float(problems, "BodyBalance.TurnIntervalBase", p.TurnIntervalBase, 0f, 30f, false);
            Float(problems, "BodyBalance.TurnOffProb", p.TurnOffProb, 0f, 1f, false);
            Float(problems, "BodyBalance.TurnToTargetProb", p.TurnToTargetProb, 0f, 1f, false);
            Float(problems, "BodyBalance.TurnToVelocityProb", p.TurnToVelocityProb, 0f, 1f, false);
            Float(problems, "BodyBalance.TurnAwayProb", p.TurnAwayProb, 0f, 1f, false);
            Float(problems, "BodyBalance.TurnSideProb", p.TurnSideProb, 0f, 1f, false);
            Float(problems, "BodyBalance.TurnProbFloor", p.TurnProbFloor, 0f, 1f, false);
            Float(problems, "BodyBalance.TurnSpeedGate", p.TurnSpeedGate, 0f, 30f, false);
            Float(problems, "BodyBalance.SpeedFloor", p.SpeedFloor, 0f, 30f, false);
            Float(problems, "BodyBalance.TwistGainA", p.TwistGainA, 0f, 10f, false);
            Float(problems, "BodyBalance.CutSpan", p.CutSpan, 0f, 10f, false);
            Float(problems, "BodyBalance.RateCap", p.RateCap, 0f, 30f, true);
            Float(problems, "BodyBalance.ElbowMax", p.ElbowMax, 0f, 3.2f, true);
            Float(problems, "BodyBalance.CatchGainX", p.CatchGainX, 0f, 100f, true);
            Float(problems, "BodyBalance.CatchGainY", p.CatchGainY, 0f, 100f, true);
            Float(problems, "BodyBalance.CatchGainZ", p.CatchGainZ, 0f, 100f, true);
            Float(problems, "BodyBalance.StepClampLo", p.StepClampLo, 0f, 5f, false);
            Float(problems, "BodyBalance.StepClampHi", p.StepClampHi, 0f, 5f, false);
            Float(problems, "BodyBalance.StabiliseDamping", p.StabiliseDamping, 0f, 5f, false);
            Float(problems, "BodyBalance.OwnOffsetX", p.OwnOffsetX, -2f, 2f, false);
            Float(problems, "BodyBalance.OwnOffsetY", p.OwnOffsetY, -2f, 2f, false);
            Float(problems, "BodyBalance.OwnOffsetZ", p.OwnOffsetZ, -2f, 2f, false);
            Float(problems, "BodyBalance.ArmsOutMinLean", p.ArmsOutMinLean, -3.2f, 3.2f, false);
            Float(problems, "BodyBalance.ElbowContactAngle", p.ElbowContactAngle, 0f, 3.2f, false);
            Float(problems, "BodyBalance.DecisionLoseLean", p.DecisionLoseLean, 0f, 3.2f, false);
            Float(problems, "BodyBalance.DecisionFallLean", p.DecisionFallLean, 0f, 3.2f, false);
            Float(problems, "BodyBalance.DecisionExitFraction", p.DecisionExitFraction, 0f, 1f, false);
            Float(problems, "BodyBalance.FallHoldTime", p.FallHoldTime, 0f, 30f, false);
            Float(problems, "BodyBalance.LookProbeRange", p.LookProbeRange, 0f, 100f, true);
            Float(problems, "BodyBalance.MaxDrive", p.MaxDrive, 0f, 10000f, true);
            Float(problems, "BodyBalance.UprightCorrect", p.UprightCorrect, 0f, 10f, false);
            Float(problems, "BodyBalance.TwistCorrect", p.TwistCorrect, 0f, 10f, false);
            Float(problems, "BodyBalance.TorsoPitchBias", p.TorsoPitchBias, -3.2f, 3.2f, false);
            Float(problems, "BodyBalance.LookVelLead", p.LookVelLead, 0f, 10f, false);
            Flag(problems, "BodyBalance.UseHeadLook", p.UseHeadLook);
            Flag(problems, "BodyBalance.UseArmsOut", p.UseArmsOut);
            Flag(problems, "BodyBalance.UseBodyTurn", p.UseBodyTurn);
            Flag(problems, "BodyBalance.UsePelvisFrame", p.UsePelvisFrame);
        }

        static void CheckCatchFall(List<string> problems, in CatchFall.CatchFallParams p)
        {
            Float(problems, "CatchFall.LegsStiffness", p.LegsStiffness, 0f, 1000f, true);
            Float(problems, "CatchFall.TorsoStiffness", p.TorsoStiffness, 0f, 1000f, true);
            Float(problems, "CatchFall.ArmsStiffness", p.ArmsStiffness, 0f, 1000f, true);
            Float(problems, "CatchFall.ForwardMaxArmOffset", p.ForwardMaxArmOffset, -2f, 2f, false);
            Float(problems, "CatchFall.BackwardsMinArmOffset", p.BackwardsMinArmOffset, -2f, 2f, false);
            Float(problems, "CatchFall.SpinReductionGain", p.SpinReductionGain, 0f, 10f, false);
            Flag(problems, "CatchFall.UseHeadLook", p.UseHeadLook);
            Float(problems, "CatchFall.Reach", p.Reach, 0f, 5f, true);
            Float(problems, "CatchFall.ProbeLength", p.ProbeLength, 0f, 20f, true);
            Float(problems, "CatchFall.MinExtension", p.MinExtension, 0f, 5f, false);
            Float(problems, "CatchFall.StiffnessFloor", p.StiffnessFloor, 0f, 1f, false);
            Float(problems, "CatchFall.GainFloor", p.GainFloor, 0f, 1f, false);
            Float(problems, "CatchFall.RelaxRate", p.RelaxRate, 0f, 30f, false);
            Float(problems, "CatchFall.RecoverRate", p.RecoverRate, 0f, 30f, false);
            Float(problems, "CatchFall.GainRate", p.GainRate, 0f, 30f, false);
            Float(problems, "CatchFall.ArmCapScale", p.ArmCapScale, 0f, 10f, true);
            Float(problems, "CatchFall.FallBias", p.FallBias, 0f, 10f, false);
            Float(problems, "CatchFall.SpinTorqueScale", p.SpinTorqueScale, 0f, 20f, false);
            Float(problems, "CatchFall.SpinTorqueMax", p.SpinTorqueMax, 0f, 10000f, true);
            Float(problems, "CatchFall.ClavFollowRatio", p.ClavFollowRatio, 0f, 1f, false);
            Float(problems, "CatchFall.ElbowRest", p.ElbowRest, 0f, 3.2f, false);
            Float(problems, "CatchFall.TorqueLimit", p.TorqueLimit, 0f, 10000f, true);
            Float(problems, "CatchFall.SafeMissStiffness", p.SafeMissStiffness, 0f, 1000f, true);
            Float(problems, "CatchFall.SpineTwistLimit", p.SpineTwistLimit, 0f, 3.2f, true);
        }

        static void CheckBrace(List<string> problems, in Brace.BraceParams p)
        {
            Float(problems, "Brace.BodyStiffness", p.BodyStiffness, 0f, 1000f, true);
            Float(problems, "Brace.ArmDamping", p.ArmDamping, 0f, 100f, false);
            Float(problems, "Brace.WristStiffness", p.WristStiffness, 0f, 1000f, true);
            Float(problems, "Brace.WristDamping", p.WristDamping, 0f, 100f, false);
            Float(problems, "Brace.MaxTorque", p.MaxTorque, 0f, 10000f, true);
            Float(problems, "Brace.BraceDistance", p.BraceDistance, 0f, 10f, false);
            Float(problems, "Brace.MaxReach", p.MaxReach, 0f, 3f, true);
            Float(problems, "Brace.PredictionTime", p.PredictionTime, 0f, 5f, false);
            Float(problems, "Brace.ReachAbsorptionTime", p.ReachAbsorptionTime, 0f, 5f, false);
            Float(problems, "Brace.TargetSpeedLimit", p.TargetSpeedLimit, 0f, 30f, true);
            Float(problems, "Brace.ApproachFloor", p.ApproachFloor, 0f, 5f, false);
            Float(problems, "Brace.HysteresisBand", p.HysteresisBand, 0f, 5f, false);
            Float(problems, "Brace.ReleaseMargin", p.ReleaseMargin, 0f, 5f, false);
            Float(problems, "Brace.ContactHoldTime", p.ContactHoldTime, 0f, 30f, false);
            Float(problems, "Brace.SideSpread", p.SideSpread, 0f, 2f, false);
            Float(problems, "Brace.StaggerTime", p.StaggerTime, 0f, 5f, false);
            Float(problems, "Brace.CrouchAmount", p.CrouchAmount, 0f, 1f, false);
            Float(problems, "Brace.LeanGain", p.LeanGain, 0f, 10f, false);
            Float(problems, "Brace.GuardStiffness", p.GuardStiffness, 0f, 1000f, true);
            Float(problems, "Brace.GuardDamping", p.GuardDamping, 0f, 100f, false);
        }

        static void CheckDrive(List<string> problems, in Drive.DriveParams p)
        {
            Float(problems, "Drive.Strength", p.Strength, 0f, 10000f, true);
            Float(problems, "Drive.Damping", p.Damping, 0f, 100f, false);
            Float(problems, "Drive.MaxTorque", p.MaxTorque, 0f, 10000f, true);
            Float(problems, "Drive.MinDamping", p.MinDamping, 0f, 100f, false);
            Float(problems, "Drive.StiffnessCap", p.StiffnessCap, 0f, 100000f, true);
            Float(problems, "Drive.LimitMargin", p.LimitMargin, 0f, 1f, false);
            Float(problems, "Drive.SoftPull", p.SoftPull, 0f, 1f, false);
        }

        static void CheckCoordination(List<string> problems, in Coordination.CoordinationParams p)
        {
            Float(problems, "Coordination.HysteresisMargin", p.HysteresisMargin, 0f, 100f, false);
            Float(problems, "Coordination.FadeTime", p.FadeTime, 0f, 30f, false);
            Float(problems, "Coordination.MinLegStiffness", p.MinLegStiffness, 0f, 10000f, true);
            Float(problems, "Coordination.MaxLegStiffness", p.MaxLegStiffness, 0f, 10000f, true);
            Float(problems, "Coordination.SupportedAnkleResponse", p.SupportedAnkleResponse, 0f, 20f, false);
            Float(problems, "Coordination.SupportedKneeResponse", p.SupportedKneeResponse, 0f, 20f, false);
            Float(problems, "Coordination.SwingAnkleResponse", p.SwingAnkleResponse, 0f, 20f, false);
            Float(problems, "Coordination.SwingKneeResponse", p.SwingKneeResponse, 0f, 20f, false);
        }

        static void CheckGetUp(List<string> problems, in GetUpParams p)
        {
            Float(problems, "GetUp.LyingHeadDrop", p.LyingHeadDrop, 0f, 3f, false);
            Float(problems, "GetUp.UprightHeadRise", p.UprightHeadRise, 0f, 3f, false);
            Float(problems, "GetUp.PoseDot", p.PoseDot, -1f, 1f, false);
            Float(problems, "GetUp.MaxAlignOffset", p.MaxAlignOffset, 0f, 50f, true);
            Float(problems, "GetUp.ClearDistance", p.ClearDistance, 0f, 50f, true);
        }
    }
}
