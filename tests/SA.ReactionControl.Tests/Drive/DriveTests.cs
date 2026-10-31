using System;
using SA.Motion.Numerics;
using SA.ReactionControl.Drive;
using Xunit;

namespace SA.ReactionControl.Tests.Drive;

public class DriveTests
{
    static DriveLimits Limits()
    {
        return new DriveLimits
        {
            TwistMin = -1.5f,
            TwistMax = 1.5f,
            Lean1Min = -0.8f,
            Lean1Max = 0.8f,
            Lean2Min = -0.8f,
            Lean2Max = 0.8f,
        };
    }

    static DriveParams Params(float strength)
    {
        var par = DriveParams.Standard;
        par.Strength = strength;
        return par;
    }

    static EffectorAngles Angles(float twist, float lean1, float lean2)
    {
        return new EffectorAngles { Twist = twist, Lean1 = lean1, Lean2 = lean2 };
    }

    [Fact]
    public void ZeroStrengthGivesPassive()
    {
        var drive = ReactionControl.Drive.Drive.ToJointDrive(Angles(0.3f, 0.2f, -0.1f), Limits(), Params(0f));
        Assert.Equal(0f, drive.Stiffness);
        Assert.Equal(0f, drive.Damping);
        Assert.Equal(0f, drive.MaxTorque);
        Assert.Equal(1f, drive.Target.W);
        Assert.Equal(JointDrive.Passive.Target.W, drive.Target.W);
    }

    [Fact]
    public void StiffnessScalesLinearlyAndRespectsCap()
    {
        var low = ReactionControl.Drive.Drive.ToJointDrive(Angles(0.3f, 0.2f, -0.1f), Limits(), Params(4f));
        var high = ReactionControl.Drive.Drive.ToJointDrive(Angles(0.3f, 0.2f, -0.1f), Limits(), Params(8f));
        Assert.InRange(low.Stiffness, 3.999f, 4.001f);
        Assert.InRange(high.Stiffness, 7.999f, 8.001f);
        Assert.InRange(high.MaxTorque, 199f, 201f);
        var huge = ReactionControl.Drive.Drive.ToJointDrive(Angles(0.3f, 0.2f, -0.1f), Limits(), Params(1e30f));
        Assert.Equal(DriveParams.Standard.StiffnessCap, huge.Stiffness);
        Assert.True(huge.Target.IsValidRotation);
    }

    [Fact]
    public void NegatedTargetGivesSameDrive()
    {
        var target = ReactionControl.Drive.Drive.FromAngles(Angles(0.4f, -0.3f, 0.2f));
        var current = Quaternion.Identity;
        var forward = ReactionControl.Drive.Drive.TrackingError(current, target);
        var negated = ReactionControl.Drive.Drive.TrackingError(current, -target);
        Assert.InRange(negated.X, forward.X - 1e-6f, forward.X + 1e-6f);
        Assert.InRange(negated.Y, forward.Y - 1e-6f, forward.Y + 1e-6f);
        Assert.InRange(negated.Z, forward.Z - 1e-6f, forward.Z + 1e-6f);
        var a = ReactionControl.Drive.Drive.ToAngles(target);
        var b = ReactionControl.Drive.Drive.ToAngles(-target);
        Assert.InRange(b.Twist, a.Twist - 1e-6f, a.Twist + 1e-6f);
        Assert.InRange(b.Lean1, a.Lean1 - 1e-6f, a.Lean1 + 1e-6f);
        Assert.InRange(b.Lean2, a.Lean2 - 1e-6f, a.Lean2 + 1e-6f);
    }

    [Fact]
    public void BeyondLimitTargetIsPulledBackBounded()
    {
        var clamped = ReactionControl.Drive.Drive.ClampToLimits(Angles(5f, -5f, 0.1f), Limits(), 0.02f);
        Assert.InRange(clamped.Twist, -1.5f, 1.5f);
        Assert.InRange(clamped.Lean1, -0.8f, 0.8f);
        var soft = new SoftLimit { Angle = 0.5f, Direction = 1f, Stiffness = 10f, Damping = 1f, Enabled = 1 };
        var pulled = ReactionControl.Drive.Drive.PullToSoftLimit(1.5f, soft, 0.25f);
        Assert.InRange(pulled, 0.749f, 0.751f);
        Assert.True(Math.Abs(pulled - 0.5f) <= 0.26f * Math.Abs(1.5f - 0.5f));
        var inside = ReactionControl.Drive.Drive.PullToSoftLimit(0.2f, soft, 0.25f);
        Assert.InRange(inside, 0.199f, 0.201f);
    }

    [Fact]
    public void SoftAccelPushesBackOnlyPastLimit()
    {
        var soft = new SoftLimit { Angle = 0.5f, Direction = 1f, Stiffness = 10f, Damping = 1f, Enabled = 1 };
        Assert.Equal(0f, ReactionControl.Drive.Drive.PullToSoftLimit(0.4f, soft, 0.25f) - 0.4f);
        var disabled = soft;
        disabled.Enabled = 0;
        Assert.Equal(0f, SoftLimit.Accel(disabled, 2f, 0f));
        Assert.Equal(0f, SoftLimit.Accel(soft, 0.2f, 1f));
        var push = SoftLimit.Accel(soft, 1f, 0f);
        Assert.True(push < 0f);
        Assert.True(Scalar.IsFinite(push));
        Assert.Equal(0f, SoftLimit.Accel(soft, float.NaN, 0f));
    }

    [Fact]
    public void AngleRoundTripHoldsAwayFromSingularity()
    {
        var cases = new[]
        {
            Angles(0.3f, 0.2f, -0.1f),
            Angles(0f, 0f, 0f),
            Angles(-1.2f, 0.5f, 0.4f),
            Angles(2.9f, -0.6f, 0.6f),
            Angles(-2.5f, 0.05f, -0.05f),
        };
        foreach (var expected in cases)
        {
            var back = ReactionControl.Drive.Drive.ToAngles(ReactionControl.Drive.Drive.FromAngles(expected));
            Assert.InRange(back.Twist, expected.Twist - 1e-4f, expected.Twist + 1e-4f);
            Assert.InRange(back.Lean1, expected.Lean1 - 1e-4f, expected.Lean1 + 1e-4f);
            Assert.InRange(back.Lean2, expected.Lean2 - 1e-4f, expected.Lean2 + 1e-4f);
        }
    }

    [Fact]
    public void SingularityGivesDefinedValue()
    {
        var zero = ReactionControl.Drive.Drive.ToAngles(Quaternion.Identity);
        Assert.True(Scalar.IsFinite(zero.Twist) && Scalar.IsFinite(zero.Lean1) && Scalar.IsFinite(zero.Lean2));
        var folded = ReactionControl.Drive.Drive.FromAngles(Angles(0.5f, (float)Math.PI, 0f));
        Assert.True(folded.IsValidRotation);
        var back = ReactionControl.Drive.Drive.ToAngles(folded);
        Assert.True(Scalar.IsFinite(back.Twist) && Scalar.IsFinite(back.Lean1) && Scalar.IsFinite(back.Lean2));
        Assert.True(ReactionControl.Drive.Drive.TrackingError(Quaternion.Identity, Quaternion.Identity).IsFinite);
    }

    [Fact]
    public void HostileInputsGivePassiveDrive()
    {
        var limits = Limits();
        var badAngles = new[]
        {
            Angles(float.NaN, 0f, 0f),
            Angles(0f, float.PositiveInfinity, 0f),
            Angles(0f, 0f, float.NaN),
        };
        foreach (var bad in badAngles)
            Assert.Equal(0f, ReactionControl.Drive.Drive.ToJointDrive(bad, limits, Params(8f)).Stiffness);
        Assert.Equal(0f, ReactionControl.Drive.Drive.ToJointDrive(Angles(0.1f, 0.1f, 0.1f), limits, Params(float.NaN)).Stiffness);
        Assert.Equal(0f, ReactionControl.Drive.Drive.ToJointDrive(Angles(0.1f, 0.1f, 0.1f), limits, Params(float.PositiveInfinity)).Stiffness);
        Assert.Equal(0f, ReactionControl.Drive.Drive.ToJointDrive(Angles(0.1f, 0.1f, 0.1f), limits, Params(-3f)).Stiffness);
        var hostileLimits = limits;
        hostileLimits.TwistMax = float.NaN;
        Assert.Equal(0f, ReactionControl.Drive.Drive.ToJointDrive(Angles(0.1f, 0.1f, 0.1f), hostileLimits, Params(8f)).Stiffness);
        var hostilePar = Params(8f);
        hostilePar.MaxTorque = float.NaN;
        Assert.Equal(0f, ReactionControl.Drive.Drive.ToJointDrive(Angles(0.1f, 0.1f, 0.1f), limits, hostilePar).MaxTorque);
    }

    [Fact]
    public void HugeFiniteInputsStayBounded()
    {
        var drive = ReactionControl.Drive.Drive.ToJointDrive(Angles(1e30f, -1e30f, 1e30f), Limits(), Params(1e30f));
        Assert.True(Scalar.IsFinite(drive.Stiffness) && Scalar.IsFinite(drive.Damping) && Scalar.IsFinite(drive.MaxTorque));
        Assert.True(drive.Target.IsValidRotation);
        Assert.True(drive.Stiffness <= DriveParams.Standard.StiffnessCap);
        var back = ReactionControl.Drive.Drive.ToAngles(drive.Target);
        Assert.True(Math.Abs(back.Twist) <= 1.5f && Math.Abs(back.Lean1) <= 0.8f && Math.Abs(back.Lean2) <= 0.8f);
    }
}
