using SA.Motion.Numerics;
using SA.ReactionControl.Drive;
using Xunit;

namespace SA.ReactionControl.Tests.Totality;

public class DriveTotality
{
    static EffectorAngles Angles(int i)
    {
        return new EffectorAngles { Twist = Fuzz.Pick(i), Lean1 = Fuzz.Pick(i + 1), Lean2 = Fuzz.Pick(i + 2) };
    }

    static DriveLimits Limits(int i)
    {
        return new DriveLimits
        {
            TwistMin = Fuzz.Pick(i),
            TwistMax = Fuzz.Pick(i + 1),
            Lean1Min = Fuzz.Pick(i + 2),
            Lean1Max = Fuzz.Pick(i + 3),
            Lean2Min = Fuzz.Pick(i + 4),
            Lean2Max = Fuzz.Pick(i + 5),
        };
    }

    static DriveParams Params(int i)
    {
        var standard = DriveParams.Standard;
        if ((i & 1) == 0)
        {
            return standard;
        }
        return new DriveParams
        {
            Strength = Fuzz.Pick(i),
            Damping = Fuzz.Pick(i + 1),
            MaxTorque = Fuzz.Pick(i + 2),
            MinDamping = Fuzz.Pick(i + 3),
            StiffnessCap = Fuzz.Pick(i + 4),
            LimitMargin = Fuzz.Pick(i + 5),
            SoftPull = Fuzz.Pick(i + 6),
        };
    }

    static SoftLimit Limit(int i)
    {
        return new SoftLimit
        {
            Angle = Fuzz.Pick(i),
            Direction = Fuzz.Pick(i + 1),
            Stiffness = Fuzz.Pick(i + 2),
            Damping = Fuzz.Pick(i + 3),
            VelocityScaled = (byte)(i & 1),
            Enabled = (byte)((i >> 1) & 1),
        };
    }

    [Fact]
    public void DriveJoints_Totality()
    {
        for (var i = 0; i < Fuzz.Combos; i++)
        {
            var desired = Angles(i * 3);
            var limits = Limits(i * 5);
            var par = Params(i);
            var plain = ReactionControl.Drive.Drive.ToJointDrive(desired, limits, par);
            Fuzz.Drive(plain);
            var soft = new SoftBounds { Twist = Limit(i * 7), Lean1 = Limit(i * 11), Lean2 = Limit(i * 13) };
            var shaped = ReactionControl.Drive.Drive.ToJointDrive(desired, limits, soft, par);
            Fuzz.Drive(shaped);
            var gains = ReactionControl.Drive.Drive.Gains(Fuzz.Pick(i * 17), Fuzz.Pick(i * 19), par);
            Assert.True(float.IsFinite(gains.Stiffness) && gains.Stiffness >= 0f);
            Assert.True(float.IsFinite(gains.Damping) && gains.Damping >= 0f);
            Assert.True(float.IsFinite(gains.MaxTorque) && gains.MaxTorque >= 0f);
            var clamped = ReactionControl.Drive.Drive.ClampToLimits(desired, limits, Fuzz.Pick(i * 23));
            Fuzz.Value(clamped.Twist);
            Fuzz.Value(clamped.Lean1);
            Fuzz.Value(clamped.Lean2);
            var pulled = ReactionControl.Drive.Drive.PullToSoftLimit(Fuzz.Pick(i * 29), Limit(i * 31), Fuzz.Pick(i * 37));
            Fuzz.Value(pulled);
        }
    }

    [Fact]
    public void DriveAngles_Totality()
    {
        for (var i = 0; i < Fuzz.Combos; i++)
        {
            var rotation = ReactionControl.Drive.Drive.FromAngles(Angles(i * 3));
            Fuzz.Value(rotation.X);
            Fuzz.Value(rotation.Y);
            Fuzz.Value(rotation.Z);
            Fuzz.Value(rotation.W);
            var back = ReactionControl.Drive.Drive.ToAngles(Fuzz.Quat(i * 5, i));
            Fuzz.Value(back.Twist);
            Fuzz.Value(back.Lean1);
            Fuzz.Value(back.Lean2);
            var error = ReactionControl.Drive.Drive.TrackingError(Fuzz.Quat(i * 7, i), Fuzz.Quat(i * 11, i + 1));
            Fuzz.Value(error);
            var accel = SoftLimit.Accel(Limit(i * 13), Fuzz.Pick(i * 17), Fuzz.Pick(i * 19));
            Fuzz.Value(accel);
        }
    }
}
