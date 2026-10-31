using System.Runtime.InteropServices;
using SA.Motion.Mechanics;
using SA.Motion.Numerics;

namespace SA.ReactionControl.Balance
{
    [StructLayout(LayoutKind.Sequential)]
    public struct BalanceParams
    {
        public Float3 Up;
        public Float3 GravityUp;
        public float DeltaTime;
        public float PelvisHeight;
        public float LegLength;
        public float HipSpread;
        public float FootWidth;
        public float StepOffset;
        public float StepLead;
        public float SwingLeadGain;
        public float SupportInsideMargin;
        public float WideStanceRelease;
        public float YawCenter;
        public float YawHalfSpan;
        public float LeanBias;
        public float StepHeight;
        public float StepClamp;
        public float AbortThreshold;
        public bool Taper;
        public bool LowAbort;
        public bool ContactResponse;
        public float LegStiffness;
        public int MaxStepDelay;
        public float FootSupportHalfWidth;
        public float FootSupportHalfLength;
        public float ReachBias;
        public float ReachForwardScale;
        public float ReachLateralScale;
        public float Epsilon;
        public float LiftGain;
        public float SoleOffset;
        public float AnkleClearance;
        public float AnkleBackstepBias;
        public float AirborneLift;
        public float YawPredictHorizon;
        public float YawPredictGain;
        public float YawValidRange;
        public float YawBlendRate;
        public float YawLimit;
        public float PelvisLeanForwardLimit;
        public float PelvisLeanBackLimit;
        public float PelvisLeadGain;
        public float LateralRespond;
        public float LateralLeanGain;
        public float LateralScale;
        public float ForwardRespond;
        public float ForwardLeanGain;
        public float ForwardScale;
        public float LeanAccumulatorRate;
        public float ToeUprightBand;
        public float ToeUprightGain;
        public float MinLeadDistance;
        public float MaxStepCorrection;
        public float SwingMinDistance;
        public float SwingBlendBase;
        public float SwingReach;
        public float SwingAheadHorizon;
        public float SwingLiftGain;
        public float SwingLiftBase;
        public float SwingLiftMin;
        public float SwingLiftMax;
        public float ToeAlignGain;
        public float ShinAlignGain;
        public float ToeSpinFactor;
        public float AnkleSwingOffset;
        public float DropGain;
        public float DropCapFraction;
        public float SingleSupportSink;
        public float LeanClamp;
        public float LateralLeanRange;
        public float ForwardLeanRange;
        public float CentrePreviewTime;
        public float HipHalfTwist;
        public float HipHalfLean1;
        public float HipHalfLean2;
        public float HipMidTwist;
        public float HipMidLean1;
        public float HipMidLean2;
        public float KneeHalfRange;
        public float KneeMid;
        public float AnkleHalfPitch;
        public float AnkleMidPitch;
        public float AnkleHalfYaw;
        public float AnkleMidYaw;
        public float SpineHalfLean;
        public float SpineMidLean;
        public float HipStrength;
        public float HipDamping;
        public float KneeStrength;
        public float KneeDamping;
        public float AnkleStrength;
        public float AnkleDamping;
        public float SpineStrength;
        public float SpineDamping;
        public float MaxTorque;
        public float SwingStiffness;

        public static BalanceParams Standard => new BalanceParams
        {
            Up = new Float3(0f, 1f, 0f),
            GravityUp = new Float3(0f, 1f, 0f),
            DeltaTime = 0.0167f,
            PelvisHeight = 1.0f,
            LegLength = 1.0f,
            HipSpread = 0.11f,
            FootWidth = 0.22f,
            StepOffset = 0.05f,
            StepLead = 0.2f,
            SwingLeadGain = 0.1f,
            SupportInsideMargin = 0.0f,
            WideStanceRelease = 0.05f,
            MinLeadDistance = 0.07f,
            YawHalfSpan = 0.15f,
            LeanBias = 0.0f,
            StepHeight = 0.12f,
            StepClamp = 0.6f,
            AbortThreshold = 0.0f,
            Taper = false,
            LowAbort = false,
            ContactResponse = false,
            LegStiffness = 1.0f,
            MaxStepDelay = 30,
            FootSupportHalfWidth = 0.09f,
            FootSupportHalfLength = 0.16f,
            ReachBias = 0.05f,
            ReachForwardScale = 1.0f,
            ReachLateralScale = 1.0f,
            Epsilon = 1e-6f,
            LiftGain = 0.2f,
            SoleOffset = 0.06f,
            AnkleClearance = 0.0f,
            AnkleBackstepBias = 0.05f,
            AirborneLift = 0.02f,
            YawPredictHorizon = 0.06f,
            YawPredictGain = 1.0f,
            YawValidRange = 3.0f,
            YawBlendRate = 0.1f,
            YawLimit = 1.0f,
            PelvisLeanForwardLimit = 0.4f,
            PelvisLeanBackLimit = 0.3f,
            PelvisLeadGain = 1.0f,
            LateralRespond = 1.0f,
            LateralLeanGain = 1.0f,
            LateralScale = 1.0f,
            ForwardRespond = 1.0f,
            ForwardLeanGain = 1.0f,
            ForwardScale = 1.0f,
            LeanAccumulatorRate = 0.06f,
            ToeUprightBand = 0.1f,
            ToeUprightGain = 2.0f,
            MaxStepCorrection = 0.08f,
            SwingMinDistance = 0.05f,
            SwingBlendBase = 2.0f,
            SwingReach = 1.0f,
            SwingAheadHorizon = 0.12f,
            SwingLiftGain = 1.0f,
            SwingLiftBase = 0.03f,
            SwingLiftMin = 0.05f,
            SwingLiftMax = 0.45f,
            ToeAlignGain = 1.0f,
            ShinAlignGain = 0.3f,
            ToeSpinFactor = 0.05f,
            AnkleSwingOffset = 0.15f,
            DropGain = 2.0f,
            DropCapFraction = 0.25f,
            SingleSupportSink = 0.05f,
            LeanClamp = 0.06f,
            LateralLeanRange = 0.1f,
            ForwardLeanRange = 0.1f,
            CentrePreviewTime = 0.15f,
            HipHalfTwist = 0.6f,
            HipHalfLean1 = 0.7f,
            HipHalfLean2 = 0.7f,
            HipMidTwist = 0.0f,
            HipMidLean1 = 0.0f,
            HipMidLean2 = 0.0f,
            KneeHalfRange = 1.2f,
            KneeMid = -1.0f,
            AnkleHalfPitch = 0.5f,
            AnkleMidPitch = 0.0f,
            AnkleHalfYaw = 0.4f,
            AnkleMidYaw = 0.0f,
            SpineHalfLean = 0.4f,
            SpineMidLean = 0.0f,
            HipStrength = 150.0f,
            HipDamping = 15.0f,
            KneeStrength = 120.0f,
            KneeDamping = 12.0f,
            AnkleStrength = 60.0f,
            AnkleDamping = 6.0f,
            SpineStrength = 100.0f,
            SpineDamping = 10.0f,
            MaxTorque = 400.0f,
            SwingStiffness = 0.25f
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BalanceJoint3
    {
        public Frame Frame;
        public Float3 Position;
        public float HalfTwist;
        public float HalfLean1;
        public float HalfLean2;
        public float MidTwist;
        public float MidLean1;
        public float MidLean2;
        public Float3 Desired;
        public Float3 Actual;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BalanceJoint1
    {
        public Frame Frame;
        public Frame ChildFrame;
        public Float3 Position;
        public float HalfRange;
        public float Mid;
        public float Desired;
        public float Actual;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BalancePart
    {
        public Frame Frame;
        public Float3 Velocity;
        public Float3 AngularVelocity;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BalanceLeg
    {
        public BalanceJoint3 Hip;
        public BalancePart Thigh;
        public BalanceJoint1 Knee;
        public BalancePart Shin;
        public BalanceJoint3 Ankle;
        public BalancePart Foot;
        public Frame Limb;
        public float ThighLength;
        public float ShinLength;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BalanceSupport
    {
        public bool Contact;
        public Float3 Target;
        public Float3 Normal;
        public Float3 GroundVelocity;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BalanceSnapshot
    {
        public Float3 Com;
        public Float3 Velocity;
        public Float3 HorizontalVelocity;
        public Float3 AngularVelocity;
        public Frame Root;
        public BalanceLeg Left;
        public BalanceLeg Right;
        public BalanceJoint3 Spine;
        public BalancePart Pelvis;
        public BalanceSupport SupportLeft;
        public BalanceSupport SupportRight;
        public float HeightTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BalanceFeet
    {
        public byte SteppingLeg;
        public int Countdown;
        public Float3 FootLeft;
        public Float3 FootRight;
        public Float3 Centre;
        public Float3 Start;
        public Float3 PrevTarget;
        public Float3 PrevSole;
        public float HeightLeft;
        public float HeightRight;
        public float DistLeft;
        public float DistRight;
        public bool InsideSupport;
        public bool LeftPlanted;
        public bool RightPlanted;
        public bool NearFootStep;
        public bool NoContactStep;
        public bool WasAlone;
        public bool WasFar;

        public static BalanceFeet Initial(int maxStepDelay)
        {
            return new BalanceFeet { Countdown = maxStepDelay };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BalancePelvisState
    {
        public Float3 Offset;
        public Float3 BlendVelocity;
        public Float3 Centre;
        public float LeanAccumulator;
        public float LateralLean;
        public float ForwardLean;
        public float YawLeft;
        public float YawRight;
        public float Lift;
        public float HipShift;
        public float AnklePitch;
        public bool FixedVelocity;
    }
}
