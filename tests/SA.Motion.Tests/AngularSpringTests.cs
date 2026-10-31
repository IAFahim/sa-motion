using SA.Motion.Mechanics;
using SA.Motion.Numerics;
using Xunit;
using Quaternion = SA.Motion.Numerics.Quaternion;

namespace SA.Motion.Tests;

public class AngularSpringTests
{
    [Fact]
    public void ZeroErrorAndVelocityGiveZeroAcceleration()
    {
        Assert.Equal(Float3.Zero, AngularSpring.Acceleration(Float3.Zero, Float3.Zero, 4f, 1f, 1f / 60f));
    }

    [Fact]
    public void AccelerationPointsAlongTheErrorAndOpposesVelocity()
    {
        var toward = AngularSpring.Acceleration(new Float3(0.5f, 0f, 0f), Float3.Zero, 3f, 1f, 1f / 60f);
        Assert.True(toward.X > 0f && toward.Y == 0f && toward.Z == 0f);
        var braking = AngularSpring.Acceleration(Float3.Zero, new Float3(0f, 2f, 0f), 3f, 1f, 1f / 60f);
        Assert.True(braking.Y < 0f);
    }

    [Theory]
    [InlineData(1f / 30f)]
    [InlineData(1f / 60f)]
    [InlineData(1f / 120f)]
    public void CriticallyDampedErrorDecaysWithoutOvershoot(float step)
    {
        var angle = 1f;
        var velocity = 0f;
        var worst = angle;
        for (var i = 0; i < (int)(3f / step); i++)
        {
            var acceleration = AngularSpring.Acceleration(new Float3(-angle, 0f, 0f), new Float3(velocity, 0f, 0f), 2f, 1f, step).X;
            velocity += acceleration * step;
            angle += velocity * step;
            worst = Scalar.Min(worst, angle);
            Assert.True(angle > -0.01f);
        }
        Assert.True(Scalar.Abs(angle) < 0.01f);
        Assert.True(worst > -0.01f);
    }

    [Fact]
    public void VeryStiffGainsStayBoundedAtALargeTimestep()
    {
        var angle = 1f;
        var velocity = 0f;
        for (var i = 0; i < 600; i++)
        {
            var acceleration = AngularSpring.Acceleration(new Float3(-angle, 0f, 0f), new Float3(velocity, 0f, 0f), 200f, 1f, 1f / 30f).X;
            velocity += acceleration / 30f;
            angle += velocity / 30f;
            Assert.True(Scalar.Abs(angle) <= 1.001f);
        }
    }

    [Fact]
    public void InvalidInputsGiveZero()
    {
        var one = new Float3(1f, 1f, 1f);
        Assert.Equal(Float3.Zero, AngularSpring.Acceleration(new Float3(float.NaN, 0f, 0f), Float3.Zero, 1f, 1f, 0.01f));
        Assert.Equal(Float3.Zero, AngularSpring.Acceleration(one, one, 0f, 1f, 0.01f));
        Assert.Equal(Float3.Zero, AngularSpring.Acceleration(one, one, 1f, -1f, 0.01f));
        Assert.Equal(Float3.Zero, AngularSpring.Acceleration(one, one, 1f, 1f, float.PositiveInfinity));
    }
}

public class RotationErrorTests
{
    [Fact]
    public void IdenticalRotationsHaveNoError() =>
        Assert.Equal(Float3.Zero, RotationError.Between(Quaternion.AxisAngle(new Float3(0, 1, 0), 0.7f), Quaternion.AxisAngle(new Float3(0, 1, 0), 0.7f)));

    [Fact]
    public void TheErrorIsTheRotationVectorFromCurrentToTarget()
    {
        var current = Quaternion.AxisAngle(new Float3(0, 1, 0), 0.2f);
        var target = Quaternion.AxisAngle(new Float3(0, 1, 0), 0.9f);
        var error = RotationError.Between(current, target);
        Assert.InRange(error.Y, 0.7f - 1e-4f, 0.7f + 1e-4f);
        Assert.InRange(error.X, -1e-5f, 1e-5f);
    }

    [Fact]
    public void SignOfTheTargetDoesNotChangeTheError()
    {
        var current = Quaternion.AxisAngle(new Float3(1, 0, 0), 0.3f);
        var target = Quaternion.AxisAngle(new Float3(0, 0, 1), 1.1f);
        var a = RotationError.Between(current, target);
        var b = RotationError.Between(current, -target);
        Assert.InRange((a - b).Length, 0f, 1e-5f);
    }

    [Fact]
    public void TakesTheShortestWayRoundForAnglesPastPi()
    {
        var error = RotationError.Between(Quaternion.Identity, Quaternion.AxisAngle(new Float3(0, 1, 0), 5.5f));
        Assert.InRange(error.Y, -(2f * Scalar.Pi - 5.5f) - 1e-4f, -(2f * Scalar.Pi - 5.5f) + 1e-4f);
    }

    [Fact]
    public void InvalidRotationsGiveZero() =>
        Assert.Equal(Float3.Zero, RotationError.Between(new Quaternion(float.NaN, 0, 0, 1), Quaternion.Identity));
}
