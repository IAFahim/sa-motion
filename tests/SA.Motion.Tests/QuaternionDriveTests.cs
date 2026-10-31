using SA.Motion.Mechanics;
using SA.Motion.Numerics;
using Xunit;
using Quaternion = SA.Motion.Numerics.Quaternion;

namespace SA.Motion.Tests;

public class QuaternionDriveTests
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

    static double AngleBetween(Quaternion a, Quaternion b)
    {
        var error = Quaternion.Multiply(Quaternion.Normalize(b), Quaternion.InverseUnit(Quaternion.Normalize(a)));
        return 2.0 * Math.Atan2(Math.Abs((double)error.Vector.Length), Math.Abs((double)error.W));
    }

    static Quaternion Integrate(Quaternion q, ref Float3 velocity, Float3 torque, float inertia, float dt)
    {
        velocity += torque / inertia * dt;
        var speed = velocity.Length;
        if (speed > 0f)
            q = Quaternion.Normalize(Quaternion.Multiply(Quaternion.AxisAngle(velocity / speed, speed * dt), q));
        return q;
    }

    static void RunRotor(float startAngle, float dt, float seconds, float stiffness, float damping, float maxTorque)
    {
        var axis = Axis(0.3, -0.5, 0.8);
        var q = FromAxisAngle(axis, startAngle);
        var velocity = Float3.Zero;
        var gains = new DriveGains { Stiffness = stiffness, Damping = damping, MaxTorque = maxTorque };
        var minimum = (double)startAngle;
        var steps = (int)(seconds / dt);
        for (var i = 0; i < steps; i++)
        {
            var torque = QuaternionDrive.Torque(q, Quaternion.Identity, velocity, Float3.Zero, gains);
            q = Integrate(q, ref velocity, torque, 2f, dt);
            var angle = AngleBetween(q, Quaternion.Identity);
            Assert.True(angle <= minimum + 0.01 * startAngle, $"angle {angle} grew past {minimum} at step {i}");
            if (angle < minimum)
                minimum = angle;
        }
        Assert.True(minimum < 1e-3, $"final angle {minimum} did not converge");
    }

    [Fact]
    public void DoubleCoverGivesSameTorque()
    {
        var current = FromAxisAngle(Axis(1, 0, 0), 0.7f);
        var target = FromAxisAngle(Axis(0, 1, 0), 1.9f);
        var velocity = new Float3(0.5f, -1f, 0.25f);
        var desired = new Float3(-0.2f, 0.4f, 0.1f);
        var gains = new DriveGains { Stiffness = 12f, Damping = 3f, MaxTorque = 50f };
        Near(
            QuaternionDrive.Torque(current, target, velocity, desired, gains),
            QuaternionDrive.Torque(current, -target, velocity, desired, gains),
            1e-5f);
    }

    [Fact]
    public void ZeroErrorWithMatchingVelocitiesGivesZeroTorque()
    {
        var q = FromAxisAngle(Axis(1, 2, 3), 1.1f);
        var velocity = new Float3(1f, -2f, 0.5f);
        var gains = new DriveGains { Stiffness = 10f, Damping = 2f, MaxTorque = 100f };
        Near(Float3.Zero, QuaternionDrive.Torque(q, q, velocity, velocity, gains), 0f);
        Near(Float3.Zero, QuaternionDrive.Torque(Quaternion.Identity, Quaternion.Identity, Float3.Zero, Float3.Zero, gains), 0f);
    }

    [Fact]
    public void SwappedPosesNegateThePositionTerm()
    {
        var current = FromAxisAngle(Axis(1, 0, 0), 0.4f);
        var target = FromAxisAngle(Axis(0, 1, 1), 1.6f);
        var gains = new DriveGains { Stiffness = 8f, Damping = 5f, MaxTorque = 1000f };
        var forward = QuaternionDrive.Torque(current, target, Float3.Zero, Float3.Zero, gains);
        var backward = QuaternionDrive.Torque(target, current, Float3.Zero, Float3.Zero, gains);
        Near(forward * -1f, backward, 1e-5f);
    }

    [Fact]
    public void CapHoldsAndPreservesDirection()
    {
        var current = Quaternion.Identity;
        var target = FromAxisAngle(Axis(0, 0, 1), 2.5f);
        var capped = new DriveGains { Stiffness = 100f, Damping = 0f, MaxTorque = 20f };
        var free = new DriveGains { Stiffness = 100f, Damping = 0f, MaxTorque = float.MaxValue };
        var limited = QuaternionDrive.Torque(current, target, Float3.Zero, Float3.Zero, capped);
        var unlimited = QuaternionDrive.Torque(current, target, Float3.Zero, Float3.Zero, free);
        Near(20f, limited.Length, 1e-3f);
        Near(unlimited / unlimited.Length, limited / limited.Length, 1e-5f);
    }

    [Fact]
    public void ZeroGainsGiveZeroTorque()
    {
        var current = FromAxisAngle(Axis(1, 0, 0), 0.9f);
        var target = FromAxisAngle(Axis(0, 1, 0), 0.3f);
        var gains = new DriveGains { Stiffness = 0f, Damping = 0f, MaxTorque = 100f };
        Near(Float3.Zero, QuaternionDrive.Torque(current, target, new Float3(1f, 2f, 3f), new Float3(-1f, 0f, 2f), gains), 0f);
    }

    [Theory]
    [InlineData(float.NaN, 1f, 10f)]
    [InlineData(1f, float.PositiveInfinity, 10f)]
    [InlineData(1f, 1f, float.NaN)]
    [InlineData(-1f, 1f, 10f)]
    [InlineData(1f, -2f, 10f)]
    [InlineData(1f, 1f, 0f)]
    [InlineData(1f, 1f, -5f)]
    [InlineData(1f, 1f, float.PositiveInfinity)]
    public void BadGainsGiveZeroTorque(float stiffness, float damping, float maxTorque)
    {
        var current = FromAxisAngle(Axis(1, 0, 0), 0.6f);
        var target = FromAxisAngle(Axis(0, 1, 0), 1.2f);
        var gains = new DriveGains { Stiffness = stiffness, Damping = damping, MaxTorque = maxTorque };
        Assert.Equal(Float3.Zero, QuaternionDrive.Torque(current, target, Float3.Zero, Float3.Zero, gains));
    }

    [Fact]
    public void NonFiniteInputsGiveZeroTorque()
    {
        var fine = FromAxisAngle(Axis(0, 0, 1), 0.5f);
        var nan = new Quaternion(float.NaN, 0f, 0f, 1f);
        var badVelocity = new Float3(float.PositiveInfinity, 0f, 0f);
        var gains = new DriveGains { Stiffness = 10f, Damping = 1f, MaxTorque = 100f };
        Assert.Equal(Float3.Zero, QuaternionDrive.Torque(nan, fine, Float3.Zero, Float3.Zero, gains));
        Assert.Equal(Float3.Zero, QuaternionDrive.Torque(fine, nan, Float3.Zero, Float3.Zero, gains));
        Assert.Equal(Float3.Zero, QuaternionDrive.Torque(fine, fine, badVelocity, Float3.Zero, gains));
        Assert.Equal(Float3.Zero, QuaternionDrive.Torque(fine, fine, Float3.Zero, badVelocity, gains));
    }

    [Fact]
    public void NonUnitQuaternionsAreNormalizedFirst()
    {
        var current = FromAxisAngle(Axis(1, 0, 0), 0.7f);
        var target = FromAxisAngle(Axis(0, 1, 0), 1.9f);
        var gains = new DriveGains { Stiffness = 12f, Damping = 3f, MaxTorque = 50f };
        var scaledCurrent = new Quaternion(current.X * 3f, current.Y * 3f, current.Z * 3f, current.W * 3f);
        var scaledTarget = new Quaternion(target.X * 0.25f, target.Y * 0.25f, target.Z * 0.25f, target.W * 0.25f);
        Near(
            QuaternionDrive.Torque(current, target, Float3.Zero, Float3.Zero, gains),
            QuaternionDrive.Torque(scaledCurrent, scaledTarget, Float3.Zero, Float3.Zero, gains),
            1e-5f);
    }

    [Fact]
    public void ZeroQuaternionCountsAsIdentity()
    {
        var target = FromAxisAngle(Axis(0, 1, 0), 1.3f);
        var gains = new DriveGains { Stiffness = 9f, Damping = 1f, MaxTorque = 60f };
        Near(
            QuaternionDrive.Torque(Quaternion.Identity, target, Float3.Zero, Float3.Zero, gains),
            QuaternionDrive.Torque(default, target, Float3.Zero, Float3.Zero, gains),
            1e-6f);
        Near(
            QuaternionDrive.Torque(target, Quaternion.Identity, Float3.Zero, Float3.Zero, gains),
            QuaternionDrive.Torque(target, default, Float3.Zero, Float3.Zero, gains),
            1e-6f);
    }

    [Fact]
    public void TinyStepAlongTorqueShrinksTheErrorAngle()
    {
        var current = FromAxisAngle(Axis(1, 2, 3), 1.4f);
        var target = FromAxisAngle(Axis(-2, 1, 0), 0.5f);
        var gains = new DriveGains { Stiffness = 10f, Damping = 0f, MaxTorque = 1000f };
        var torque = QuaternionDrive.Torque(current, target, Float3.Zero, Float3.Zero, gains);
        Assert.True(torque.Length > 1e-3f);
        var before = AngleBetween(current, target);
        var stepped = Quaternion.Normalize(Quaternion.Multiply(Quaternion.AxisAngle(torque / torque.Length, 1e-3f), current));
        Assert.True(AngleBetween(stepped, target) < before);
    }

    [Theory]
    [InlineData(0.1f, 1f / 60f)]
    [InlineData(1.0f, 1f / 60f)]
    [InlineData(3.0f, 1f / 60f)]
    [InlineData(0.1f, 1f / 240f)]
    [InlineData(1.0f, 1f / 240f)]
    [InlineData(3.0f, 1f / 240f)]
    public void RotorSettlesWithoutRegrowth(float startAngle, float dt)
    {
        var stiffness = 100f;
        RunRotor(startAngle, dt, 5f, stiffness, 2f * (float)Math.Sqrt(stiffness * 2f), float.MaxValue);
    }

    [Theory]
    [InlineData(1f / 60f)]
    [InlineData(1f / 240f)]
    public void CappedRotorStillConverges(float dt)
    {
        var stiffness = 100f;
        var q = FromAxisAngle(Axis(0.3, -0.5, 0.8), 3.0f);
        var velocity = Float3.Zero;
        var gains = new DriveGains { Stiffness = stiffness, Damping = 2f * (float)Math.Sqrt(stiffness * 2f), MaxTorque = 20f };
        var steps = (int)(10f / dt);
        for (var i = 0; i < steps; i++)
        {
            var torque = QuaternionDrive.Torque(q, Quaternion.Identity, velocity, Float3.Zero, gains);
            Assert.True(torque.Length <= 20f * (1f + 1e-5f));
            q = Integrate(q, ref velocity, torque, 2f, dt);
        }
        Assert.True(AngleBetween(q, Quaternion.Identity) < 1e-3f);
    }
}
