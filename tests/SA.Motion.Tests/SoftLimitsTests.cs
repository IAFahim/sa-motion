using System;
using SA.Motion.Mechanics;
using SA.Motion.Numerics;
using Xunit;
using Quaternion = SA.Motion.Numerics.Quaternion;

namespace SA.Motion.Tests;

public class SoftLimitsTests
{
    static void Near(float expected, float actual, float tolerance = 2e-4f) => Assert.InRange(actual, expected - tolerance, expected + tolerance);
    static void Near(Float3 expected, Float3 actual, float tolerance = 2e-4f)
    {
        Near(expected.X, actual.X, tolerance);
        Near(expected.Y, actual.Y, tolerance);
        Near(expected.Z, actual.Z, tolerance);
    }

    static Float3 Axis(double x, double y, double z)
    {
        var length = Math.Sqrt(x * x + y * y + z * z);
        return new Float3((float)(x / length), (float)(y / length), (float)(z / length));
    }

    static Quaternion FromAxisAngle(Float3 axis, float angle) => Quaternion.Normalize(Quaternion.AxisAngle(axis, angle));

    static Float3 RandomDirection(Random random)
    {
        while (true)
        {
            var candidate = new Float3((float)(random.NextDouble() * 2.0 - 1.0), (float)(random.NextDouble() * 2.0 - 1.0), (float)(random.NextDouble() * 2.0 - 1.0));
            if (candidate.LengthSquared > 1e-4f && candidate.LengthSquared <= 1f)
                return candidate / candidate.Length;
        }
    }

    static Float3 RandomVelocity(Random random, float scale)
    {
        return new Float3((float)(random.NextDouble() * 2.0 - 1.0) * scale, (float)(random.NextDouble() * 2.0 - 1.0) * scale, (float)(random.NextDouble() * 2.0 - 1.0) * scale);
    }

    static Quaternion RandomRotation(Random random)
    {
        return Quaternion.Normalize(new Quaternion((float)(random.NextDouble() * 2.0 - 1.0), (float)(random.NextDouble() * 2.0 - 1.0), (float)(random.NextDouble() * 2.0 - 1.0), (float)(random.NextDouble() * 2.0 - 1.0)));
    }

    static Float3 Perpendicular(Float3 axis, Random random)
    {
        var helper = Math.Abs(axis.Y) < 0.9f ? new Float3(0f, 1f, 0f) : new Float3(1f, 0f, 0f);
        var side = Float3.Cross(axis, helper);
        if (side.LengthSquared <= 1e-8f)
            side = Float3.Cross(axis, RandomDirection(random));
        return side / side.Length;
    }

    static double SwingOf(Quaternion deviation, Float3 axis)
    {
        var along = Float3.Dot(deviation.Vector, axis);
        var twistLength = Math.Sqrt((double)along * along + (double)deviation.W * deviation.W);
        var twist = new Quaternion(axis.X * (float)(along / twistLength), axis.Y * (float)(along / twistLength), axis.Z * (float)(along / twistLength), (float)(deviation.W / twistLength));
        var swing = Quaternion.Multiply(Quaternion.Normalize(deviation), Quaternion.InverseUnit(Quaternion.Normalize(twist)));
        return 2.0 * Math.Atan2(swing.Vector.Length, swing.W);
    }

    static double HingeAngleOf(Quaternion relative, Quaternion rest, Float3 axis)
    {
        var deviation = Quaternion.Canonical(Quaternion.Multiply(Quaternion.Normalize(relative), Quaternion.InverseUnit(Quaternion.Normalize(rest))));
        return 2.0 * Math.Atan2((double)Float3.Dot(deviation.Vector, axis), (double)deviation.W);
    }

    static Quaternion Integrate(Quaternion q, ref Float3 velocity, Float3 torque, float inertia, float dt)
    {
        velocity += torque / inertia * dt;
        var speed = velocity.Length;
        if (speed > 0f)
            q = Quaternion.Normalize(Quaternion.Multiply(Quaternion.AxisAngle(velocity / speed, speed * dt), q));
        return q;
    }

    [Fact]
    public void ConeTwistInsideGivesExactlyZero()
    {
        var random = new Random(71);
        var limit = new ConeTwistLimit { ConeAngle = 0.6f, TwistMin = -1f, TwistMax = 1f };
        var gains = new LimitGains { Stiffness = 80f, Damping = 6f, MaxTorque = 30f };
        for (var i = 0; i < 500; i++)
        {
            var rest = RandomRotation(random);
            var axis = RandomDirection(random);
            var swingAxis = Perpendicular(axis, random);
            var swing = FromAxisAngle(swingAxis, (float)(random.NextDouble() * 0.9 * 0.6));
            var twist = FromAxisAngle(axis, (float)(random.NextDouble() * 1.8 - 0.9));
            var relative = Quaternion.Multiply(swing, twist);
            relative = Quaternion.Multiply(relative, rest);
            Assert.Equal(Float3.Zero, SoftLimits.ConeTwistTorque(relative, rest, axis, RandomVelocity(random, 5f), in limit, in gains));
        }
        for (var i = 0; i < 200; i++)
        {
            var rest = RandomRotation(random);
            var axis = RandomDirection(random);
            var relative = Quaternion.Multiply(FromAxisAngle(RandomDirection(random), (float)(random.NextDouble() * 0.15)), rest);
            Assert.Equal(Float3.Zero, SoftLimits.ConeTwistTorque(relative, rest, axis, RandomVelocity(random, 5f), in limit, in gains));
        }
        Assert.Equal(Float3.Zero, SoftLimits.ConeTwistTorque(Quaternion.Identity, Quaternion.Identity, Axis(0, 1, 0), Float3.Zero, in limit, in gains));
    }

    [Fact]
    public void HingeInsideGivesExactlyZero()
    {
        var random = new Random(73);
        var limit = new HingeLimit { Min = -0.7f, Max = 0.9f };
        var gains = new LimitGains { Stiffness = 80f, Damping = 6f, MaxTorque = 30f };
        for (var i = 0; i < 500; i++)
        {
            var rest = RandomRotation(random);
            var axis = RandomDirection(random);
            var angle = (float)(random.NextDouble() * (0.9 - 0.05 - (-0.7 + 0.05)) + (-0.7 + 0.05));
            var relative = Quaternion.Multiply(FromAxisAngle(axis, angle), rest);
            Assert.Equal(Float3.Zero, SoftLimits.HingeTorque(relative, rest, axis, RandomVelocity(random, 5f), in limit, in gains));
        }
    }

    [Fact]
    public void ConeOutsidePointsBackAndGrowsToCap()
    {
        var axis = Axis(0, 1, 0);
        var swingAxis = Axis(1, 0, 0);
        var limit = new ConeTwistLimit { ConeAngle = 0.5f, TwistMin = -3f, TwistMax = 3f };
        var gains = new LimitGains { Stiffness = 100f, Damping = 0f, MaxTorque = 25f };
        var swings = new float[] { 0.55f, 0.7f, 1f, 1.5f, 2f, 2.5f };
        var previous = 0f;
        foreach (var swing in swings)
        {
            var torque = SoftLimits.ConeTwistTorque(FromAxisAngle(swingAxis, swing), Quaternion.Identity, axis, Float3.Zero, in limit, in gains);
            Assert.True(Float3.Dot(torque, swingAxis) < 0f);
            var expected = Math.Min(100.0 * (swing - 0.5), 25.0);
            Near((float)expected, torque.Length, 1e-3f);
            Assert.True(torque.Length >= previous - 1e-6f);
            previous = torque.Length;
        }
        Near(25f, previous, 1e-3f);
    }

    [Fact]
    public void TwistOutsidePointsBackAndGrowsToCap()
    {
        var axis = Axis(0.3, 0.8, -0.5);
        var limit = new ConeTwistLimit { ConeAngle = (float)Math.PI, TwistMin = -0.4f, TwistMax = 0.6f };
        var gains = new LimitGains { Stiffness = 100f, Damping = 0f, MaxTorque = 25f };
        var twists = new float[] { 0.65f, 0.8f, 1.1f, 1.6f, 2.4f };
        var previous = 0f;
        foreach (var twist in twists)
        {
            var torque = SoftLimits.ConeTwistTorque(FromAxisAngle(axis, twist), Quaternion.Identity, axis, Float3.Zero, in limit, in gains);
            Assert.True(Float3.Dot(torque, axis) < 0f);
            Near((float)Math.Min(100.0 * (twist - 0.6), 25.0), torque.Length, 1e-3f);
            Assert.True(torque.Length >= previous - 1e-6f);
            previous = torque.Length;
        }
        Near(25f, previous, 1e-3f);
        var below = SoftLimits.ConeTwistTorque(FromAxisAngle(axis, -0.6f), Quaternion.Identity, axis, Float3.Zero, in limit, in gains);
        Assert.True(Float3.Dot(below, axis) > 0f);
        Near(20f, below.Length, 1e-3f);
    }

    [Fact]
    public void CapPreservesDirection()
    {
        var axis = Axis(0, 1, 0);
        var swingAxis = Axis(1, 0, 0);
        var limit = new ConeTwistLimit { ConeAngle = 0.5f, TwistMin = -3f, TwistMax = 3f };
        var capped = new LimitGains { Stiffness = 100f, Damping = 0f, MaxTorque = 20f };
        var free = new LimitGains { Stiffness = 100f, Damping = 0f, MaxTorque = float.MaxValue };
        var relative = FromAxisAngle(swingAxis, 2f);
        var limited = SoftLimits.ConeTwistTorque(relative, Quaternion.Identity, axis, Float3.Zero, in limit, in capped);
        var unlimited = SoftLimits.ConeTwistTorque(relative, Quaternion.Identity, axis, Float3.Zero, in limit, in free);
        Near(20f, limited.Length, 1e-3f);
        Near(unlimited / unlimited.Length, limited / limited.Length, 1e-5f);
        var hingeLimit = new HingeLimit { Min = -0.5f, Max = 0.5f };
        var hingeCapped = SoftLimits.HingeTorque(FromAxisAngle(swingAxis, 2f), Quaternion.Identity, swingAxis, Float3.Zero, in hingeLimit, in capped);
        var hingeFree = SoftLimits.HingeTorque(FromAxisAngle(swingAxis, 2f), Quaternion.Identity, swingAxis, Float3.Zero, in hingeLimit, in free);
        Near(20f, hingeCapped.Length, 1e-3f);
        Near(hingeFree / hingeFree.Length, hingeCapped / hingeCapped.Length, 1e-5f);
    }

    [Fact]
    public void HingeOutsidePointsBackAndGrowsToCap()
    {
        var axis = Axis(0, 0, 1);
        var limit = new HingeLimit { Min = -0.5f, Max = 0.5f };
        var gains = new LimitGains { Stiffness = 100f, Damping = 0f, MaxTorque = 25f };
        var angles = new float[] { 0.55f, 0.7f, 1f, 1.5f };
        var previous = 0f;
        foreach (var angle in angles)
        {
            var torque = SoftLimits.HingeTorque(FromAxisAngle(axis, angle), Quaternion.Identity, axis, Float3.Zero, in limit, in gains);
            Assert.True(Float3.Dot(torque, axis) < 0f);
            Near((float)Math.Min(100.0 * (angle - 0.5), 25.0), torque.Length, 1e-3f);
            Assert.True(torque.Length >= previous - 1e-6f);
            previous = torque.Length;
        }
        Near(25f, previous, 1e-3f);
        var below = SoftLimits.HingeTorque(FromAxisAngle(axis, -0.7f), Quaternion.Identity, axis, Float3.Zero, in limit, in gains);
        Assert.True(Float3.Dot(below, axis) > 0f);
        Near(20f, below.Length, 1e-3f);
    }

    [Fact]
    public void BoundaryIsContinuous()
    {
        var axis = Axis(0, 1, 0);
        var swingAxis = Axis(1, 0, 0);
        var coneLimit = new ConeTwistLimit { ConeAngle = 0.5f, TwistMin = -3f, TwistMax = 3f };
        var twistLimit = new ConeTwistLimit { ConeAngle = (float)Math.PI, TwistMin = -0.4f, TwistMax = 0.6f };
        var hingeLimit = new HingeLimit { Min = -0.5f, Max = 0.5f };
        var gains = new LimitGains { Stiffness = 50f, Damping = 4f, MaxTorque = 1000f };
        Assert.Equal(Float3.Zero, SoftLimits.ConeTwistTorque(FromAxisAngle(swingAxis, 0.5f), Quaternion.Identity, axis, Float3.Zero, in coneLimit, in gains));
        Assert.True(SoftLimits.ConeTwistTorque(FromAxisAngle(swingAxis, 0.5f + 1e-4f), Quaternion.Identity, axis, Float3.Zero, in coneLimit, in gains).Length < 1e-2f);
        Assert.Equal(Float3.Zero, SoftLimits.ConeTwistTorque(FromAxisAngle(axis, 0.6f), Quaternion.Identity, axis, Float3.Zero, in twistLimit, in gains));
        Assert.True(SoftLimits.ConeTwistTorque(FromAxisAngle(axis, 0.6f + 1e-4f), Quaternion.Identity, axis, Float3.Zero, in twistLimit, in gains).Length < 1e-2f);
        Assert.Equal(Float3.Zero, SoftLimits.HingeTorque(FromAxisAngle(swingAxis, 0.5f), Quaternion.Identity, swingAxis, Float3.Zero, in hingeLimit, in gains));
        Assert.True(SoftLimits.HingeTorque(FromAxisAngle(swingAxis, 0.5f + 1e-4f), Quaternion.Identity, swingAxis, Float3.Zero, in hingeLimit, in gains).Length < 1e-2f);
        Assert.Equal(Float3.Zero, SoftLimits.HingeTorque(FromAxisAngle(swingAxis, -0.5f), Quaternion.Identity, swingAxis, Float3.Zero, in hingeLimit, in gains));
        Assert.True(SoftLimits.HingeTorque(FromAxisAngle(swingAxis, -0.5f - 1e-4f), Quaternion.Identity, swingAxis, Float3.Zero, in hingeLimit, in gains).Length < 1e-2f);
    }

    [Fact]
    public void DampingActsOnlyWhileWorsening()
    {
        var hingeAxis = Axis(0, 0, 1);
        var hingeLimit = new HingeLimit { Min = -0.5f, Max = 0.5f };
        var gains = new LimitGains { Stiffness = 40f, Damping = 5f, MaxTorque = 1000f };
        var pose = FromAxisAngle(hingeAxis, 0.8f);
        var still = SoftLimits.HingeTorque(pose, Quaternion.Identity, hingeAxis, Float3.Zero, in hingeLimit, in gains);
        Near(new Float3(0f, 0f, -12f), still, 1e-4f);
        var worsening = SoftLimits.HingeTorque(pose, Quaternion.Identity, hingeAxis, hingeAxis * 2f, in hingeLimit, in gains);
        Near(new Float3(0f, 0f, -22f), worsening, 1e-4f);
        Assert.Equal(still, SoftLimits.HingeTorque(pose, Quaternion.Identity, hingeAxis, hingeAxis * -2f, in hingeLimit, in gains));
        Assert.Equal(still, SoftLimits.HingeTorque(pose, Quaternion.Identity, hingeAxis, Axis(1, 0, 0) * 2f, in hingeLimit, in gains));
        var limb = Axis(0, 1, 0);
        var swingAxis = Axis(1, 0, 0);
        var coneLimit = new ConeTwistLimit { ConeAngle = 0.5f, TwistMin = -3f, TwistMax = 3f };
        var swingPose = FromAxisAngle(swingAxis, 0.8f);
        var swingStill = SoftLimits.ConeTwistTorque(swingPose, Quaternion.Identity, limb, Float3.Zero, in coneLimit, in gains);
        Assert.True(swingStill.Length > 1e-3f);
        Assert.Equal(swingStill, SoftLimits.ConeTwistTorque(swingPose, Quaternion.Identity, limb, swingAxis * -2f, in coneLimit, in gains));
        Assert.True(SoftLimits.ConeTwistTorque(swingPose, Quaternion.Identity, limb, swingAxis * 2f, in coneLimit, in gains).Length > swingStill.Length + 1f);
        var twistLimit = new ConeTwistLimit { ConeAngle = (float)Math.PI, TwistMin = -0.4f, TwistMax = 0.6f };
        var twistPose = FromAxisAngle(limb, 1f);
        var twistStill = SoftLimits.ConeTwistTorque(twistPose, Quaternion.Identity, limb, Float3.Zero, in twistLimit, in gains);
        Assert.Equal(twistStill, SoftLimits.ConeTwistTorque(twistPose, Quaternion.Identity, limb, limb * -2f, in twistLimit, in gains));
        Assert.True(SoftLimits.ConeTwistTorque(twistPose, Quaternion.Identity, limb, limb * 2f, in twistLimit, in gains).Length > twistStill.Length + 1f);
    }

    [Fact]
    public void DoubleCoverGivesSameTorque()
    {
        var random = new Random(77);
        var coneLimit = new ConeTwistLimit { ConeAngle = 0.5f, TwistMin = -1f, TwistMax = 1f };
        var hingeLimit = new HingeLimit { Min = -0.5f, Max = 0.8f };
        var gains = new LimitGains { Stiffness = 30f, Damping = 2f, MaxTorque = 50f };
        for (var i = 0; i < 50; i++)
        {
            var relative = RandomRotation(random);
            var rest = RandomRotation(random);
            var axis = RandomDirection(random);
            var velocity = RandomVelocity(random, 3f);
            Near(
                SoftLimits.ConeTwistTorque(relative, rest, axis, velocity, in coneLimit, in gains),
                SoftLimits.ConeTwistTorque(-relative, rest, axis, velocity, in coneLimit, in gains),
                1e-5f);
            Near(
                SoftLimits.ConeTwistTorque(relative, rest, axis, velocity, in coneLimit, in gains),
                SoftLimits.ConeTwistTorque(relative, -rest, axis, velocity, in coneLimit, in gains),
                1e-5f);
            Near(
                SoftLimits.HingeTorque(relative, rest, axis, velocity, in hingeLimit, in gains),
                SoftLimits.HingeTorque(-relative, rest, axis, velocity, in hingeLimit, in gains),
                1e-5f);
        }
    }

    [Fact]
    public void ZeroAndPiLimits()
    {
        var axis = Axis(0, 1, 0);
        var swingAxis = Axis(1, 0, 0);
        var gains = new LimitGains { Stiffness = 60f, Damping = 1f, MaxTorque = 500f };
        var point = new ConeTwistLimit { ConeAngle = 0f, TwistMin = -3f, TwistMax = 3f };
        Assert.Equal(Float3.Zero, SoftLimits.ConeTwistTorque(Quaternion.Identity, Quaternion.Identity, axis, Float3.Zero, in point, in gains));
        var smallSwing = SoftLimits.ConeTwistTorque(FromAxisAngle(swingAxis, 0.1f), Quaternion.Identity, axis, Float3.Zero, in point, in gains);
        Assert.True(Float3.Dot(smallSwing, swingAxis) < 0f);
        Near(6f, smallSwing.Length, 1e-3f);
        var full = new ConeTwistLimit { ConeAngle = (float)Math.PI, TwistMin = -3.2f, TwistMax = 3.2f };
        var random = new Random(79);
        for (var i = 0; i < 100; i++)
            Assert.Equal(Float3.Zero, SoftLimits.ConeTwistTorque(RandomRotation(random), RandomRotation(random), RandomDirection(random), RandomVelocity(random, 3f), in full, in gains));
        var pointHinge = new HingeLimit { Min = 0f, Max = 0f };
        Assert.Equal(Float3.Zero, SoftLimits.HingeTorque(Quaternion.Identity, Quaternion.Identity, swingAxis, Float3.Zero, in pointHinge, in gains));
        var hingeSmall = SoftLimits.HingeTorque(FromAxisAngle(swingAxis, 0.2f), Quaternion.Identity, swingAxis, Float3.Zero, in pointHinge, in gains);
        Assert.True(Float3.Dot(hingeSmall, swingAxis) < 0f);
        Near(12f, hingeSmall.Length, 1e-3f);
        var fullHinge = new HingeLimit { Min = -(float)Math.PI, Max = (float)Math.PI };
        foreach (var angle in new float[] { -3f, -1.5f, 0f, 1.5f, 3f })
            Assert.Equal(Float3.Zero, SoftLimits.HingeTorque(FromAxisAngle(swingAxis, angle), Quaternion.Identity, swingAxis, Float3.Zero, in fullHinge, in gains));
        var pointTwist = new ConeTwistLimit { ConeAngle = (float)Math.PI, TwistMin = 0f, TwistMax = 0f };
        Assert.Equal(Float3.Zero, SoftLimits.ConeTwistTorque(Quaternion.Identity, Quaternion.Identity, axis, Float3.Zero, in pointTwist, in gains));
        var twistSmall = SoftLimits.ConeTwistTorque(FromAxisAngle(axis, 0.2f), Quaternion.Identity, axis, Float3.Zero, in pointTwist, in gains);
        Assert.True(Float3.Dot(twistSmall, axis) < 0f);
    }

    [Fact]
    public void HostileInputsGiveZeroTorque()
    {
        var fine = FromAxisAngle(Axis(0, 0, 1), 0.5f);
        var nanRotation = new Quaternion(float.NaN, 0f, 0f, 1f);
        var infiniteRotation = new Quaternion(float.PositiveInfinity, 0f, 0f, 1f);
        var nanVector = new Float3(float.NaN, 0f, 0f);
        var axis = Axis(0, 1, 0);
        var coneLimit = new ConeTwistLimit { ConeAngle = 0.5f, TwistMin = -1f, TwistMax = 1f };
        var hingeLimit = new HingeLimit { Min = -0.5f, Max = 0.5f };
        var gains = new LimitGains { Stiffness = 10f, Damping = 1f, MaxTorque = 100f };
        Assert.Equal(Float3.Zero, SoftLimits.ConeTwistTorque(nanRotation, fine, axis, Float3.Zero, in coneLimit, in gains));
        Assert.Equal(Float3.Zero, SoftLimits.ConeTwistTorque(fine, nanRotation, axis, Float3.Zero, in coneLimit, in gains));
        Assert.Equal(Float3.Zero, SoftLimits.ConeTwistTorque(infiniteRotation, fine, axis, Float3.Zero, in coneLimit, in gains));
        Assert.Equal(Float3.Zero, SoftLimits.ConeTwistTorque(fine, fine, nanVector, Float3.Zero, in coneLimit, in gains));
        Assert.Equal(Float3.Zero, SoftLimits.ConeTwistTorque(fine, fine, Float3.Zero, Float3.Zero, in coneLimit, in gains));
        Assert.Equal(Float3.Zero, SoftLimits.ConeTwistTorque(fine, fine, axis, nanVector, in coneLimit, in gains));
        Assert.Equal(Float3.Zero, SoftLimits.ConeTwistTorque(fine, fine, axis, new Float3(float.PositiveInfinity, 0f, 0f), in coneLimit, in gains));
        Assert.Equal(Float3.Zero, SoftLimits.HingeTorque(nanRotation, fine, axis, Float3.Zero, in hingeLimit, in gains));
        Assert.Equal(Float3.Zero, SoftLimits.HingeTorque(fine, nanRotation, axis, Float3.Zero, in hingeLimit, in gains));
        Assert.Equal(Float3.Zero, SoftLimits.HingeTorque(fine, fine, nanVector, Float3.Zero, in hingeLimit, in gains));
        Assert.Equal(Float3.Zero, SoftLimits.HingeTorque(fine, fine, axis, nanVector, in hingeLimit, in gains));
        var nanCone = new ConeTwistLimit { ConeAngle = float.NaN, TwistMin = -1f, TwistMax = 1f };
        var negativeCone = new ConeTwistLimit { ConeAngle = -0.1f, TwistMin = -1f, TwistMax = 1f };
        var flippedTwist = new ConeTwistLimit { ConeAngle = 0.5f, TwistMin = 1f, TwistMax = -1f };
        var flippedHinge = new HingeLimit { Min = 0.5f, Max = -0.5f };
        var nanHinge = new HingeLimit { Min = float.NaN, Max = 0.5f };
        var badStiffness = new LimitGains { Stiffness = -1f, Damping = 1f, MaxTorque = 100f };
        var badDamping = new LimitGains { Stiffness = 10f, Damping = -1f, MaxTorque = 100f };
        var zeroCap = new LimitGains { Stiffness = 10f, Damping = 1f, MaxTorque = 0f };
        var infiniteCap = new LimitGains { Stiffness = 10f, Damping = 1f, MaxTorque = float.PositiveInfinity };
        var nanStiffness = new LimitGains { Stiffness = float.NaN, Damping = 1f, MaxTorque = 100f };
        Assert.Equal(Float3.Zero, SoftLimits.ConeTwistTorque(fine, fine, axis, Float3.Zero, in nanCone, in gains));
        Assert.Equal(Float3.Zero, SoftLimits.ConeTwistTorque(fine, fine, axis, Float3.Zero, in negativeCone, in gains));
        Assert.Equal(Float3.Zero, SoftLimits.ConeTwistTorque(fine, fine, axis, Float3.Zero, in flippedTwist, in gains));
        Assert.Equal(Float3.Zero, SoftLimits.HingeTorque(fine, fine, axis, Float3.Zero, in flippedHinge, in gains));
        Assert.Equal(Float3.Zero, SoftLimits.HingeTorque(fine, fine, axis, Float3.Zero, in nanHinge, in gains));
        Assert.Equal(Float3.Zero, SoftLimits.ConeTwistTorque(fine, fine, axis, Float3.Zero, in coneLimit, in badStiffness));
        Assert.Equal(Float3.Zero, SoftLimits.ConeTwistTorque(fine, fine, axis, Float3.Zero, in coneLimit, in badDamping));
        Assert.Equal(Float3.Zero, SoftLimits.ConeTwistTorque(fine, fine, axis, Float3.Zero, in coneLimit, in zeroCap));
        Assert.Equal(Float3.Zero, SoftLimits.ConeTwistTorque(fine, fine, axis, Float3.Zero, in coneLimit, in infiniteCap));
        Assert.Equal(Float3.Zero, SoftLimits.ConeTwistTorque(fine, fine, axis, Float3.Zero, in coneLimit, in nanStiffness));
        Assert.Equal(Float3.Zero, SoftLimits.HingeTorque(fine, fine, axis, Float3.Zero, in hingeLimit, in badStiffness));
        Assert.Equal(Float3.Zero, SoftLimits.HingeTorque(fine, fine, axis, Float3.Zero, in hingeLimit, in zeroCap));
        Assert.Equal(Float3.Zero, SoftLimits.ConeTwistTorque(default, default, axis, Float3.Zero, in coneLimit, in gains));
        Assert.Equal(Float3.Zero, SoftLimits.HingeTorque(default, default, axis, Float3.Zero, in hingeLimit, in gains));
    }

    [Fact]
    public void StepAlongTorqueShrinksTheViolation()
    {
        var axis = Axis(0, 1, 0);
        var swingAxis = Axis(1, 0, 0);
        var coneLimit = new ConeTwistLimit { ConeAngle = 0.5f, TwistMin = -3f, TwistMax = 3f };
        var hingeLimit = new HingeLimit { Min = -0.5f, Max = 0.5f };
        var gains = new LimitGains { Stiffness = 10f, Damping = 0f, MaxTorque = 1000f };
        var conePose = FromAxisAngle(swingAxis, 1.2f);
        var coneTorque = SoftLimits.ConeTwistTorque(conePose, Quaternion.Identity, axis, Float3.Zero, in coneLimit, in gains);
        Assert.True(coneTorque.Length > 1e-3f);
        var coneBefore = SwingOf(Quaternion.Normalize(conePose), axis);
        var coneStepped = Quaternion.Normalize(Quaternion.Multiply(Quaternion.AxisAngle(coneTorque / coneTorque.Length, 1e-3f), conePose));
        Assert.True(SwingOf(coneStepped, axis) < coneBefore);
        var hingePose = FromAxisAngle(swingAxis, 1.2f);
        var hingeTorque = SoftLimits.HingeTorque(hingePose, Quaternion.Identity, swingAxis, Float3.Zero, in hingeLimit, in gains);
        Assert.True(hingeTorque.Length > 1e-3f);
        var hingeBefore = HingeAngleOf(hingePose, Quaternion.Identity, swingAxis);
        var hingeStepped = Quaternion.Normalize(Quaternion.Multiply(Quaternion.AxisAngle(hingeTorque / hingeTorque.Length, 1e-3f), hingePose));
        Assert.True(HingeAngleOf(hingeStepped, Quaternion.Identity, swingAxis) < hingeBefore);
    }

    [Fact]
    public void RotorPushedPastHingeLimitSettlesInside()
    {
        var axis = Axis(0.3, -0.5, 0.8);
        var limit = new HingeLimit { Min = -0.5f, Max = 0.5f };
        var stiffness = 120f;
        var gains = new LimitGains { Stiffness = stiffness, Damping = 2f * (float)Math.Sqrt(stiffness * 2f), MaxTorque = 1000f };
        var startAngle = 0.8f;
        var startExcess = startAngle - limit.Max;
        var quaternion = FromAxisAngle(axis, startAngle);
        var velocity = Float3.Zero;
        var dt = 1f / 60f;
        var steps = (int)(12f / dt);
        var lateStart = (int)(9.6f / dt);
        var worst = 0.0;
        var lateWorst = 0.0;
        var lateBest = double.MaxValue;
        for (var i = 0; i < steps; i++)
        {
            var torque = SoftLimits.HingeTorque(quaternion, Quaternion.Identity, axis, velocity, in limit, in gains);
            Assert.True(torque.Length <= 1000f * (1f + 1e-5f));
            quaternion = Integrate(quaternion, ref velocity, torque, 2f, dt);
            var angle = HingeAngleOf(quaternion, Quaternion.Identity, axis);
            var violation = Math.Max(angle - limit.Max, limit.Min - angle);
            if (violation > worst)
                worst = violation;
            if (i >= lateStart)
            {
                if (violation > lateWorst)
                    lateWorst = violation;
                if (violation < lateBest)
                    lateBest = violation;
            }
        }
        Assert.True(worst <= 1.05 * startExcess);
        Assert.True(lateWorst <= 0.05 * startExcess);
        Assert.True(lateBest <= 0.0);
    }
}
