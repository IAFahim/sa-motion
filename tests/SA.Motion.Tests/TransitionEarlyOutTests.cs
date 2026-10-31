using System;
using SA.Motion.Inertialization;
using SA.Motion.Numerics;
using Xunit;
using Quaternion = SA.Motion.Numerics.Quaternion;

namespace SA.Motion.Tests;

public class TransitionEarlyOutTests
{
    static Float3 ReferenceVector(VectorOffset offset, Float3 destination, float elapsed)
    {
        return destination + offset.Direction * offset.Decay.Evaluate(elapsed).Value;
    }

    static Quaternion ReferenceRotation(RotationOffset offset, Quaternion destination, float elapsed)
    {
        return Quaternion.Normalize(Quaternion.Multiply(Quaternion.AxisAngle(offset.Axis, offset.Decay.Evaluate(elapsed).Value), Quaternion.Normalize(destination)));
    }

    static void Near(float expected, float actual)
    {
        Assert.InRange(actual, expected - 1e-6f, expected + 1e-6f);
    }

    static void Near3(Float3 expected, Float3 actual)
    {
        Near(expected.X, actual.X);
        Near(expected.Y, actual.Y);
        Near(expected.Z, actual.Z);
    }

    static void Near4(Quaternion expected, Quaternion actual)
    {
        Near(expected.X, actual.X);
        Near(expected.Y, actual.Y);
        Near(expected.Z, actual.Z);
        Near(expected.W, actual.W);
    }

    static float Coordinate(Random random, float scale)
    {
        return (float)(random.NextDouble() * 2.0 - 1.0) * scale;
    }

    static Float3 Vector(Random random, float scale)
    {
        return new Float3(Coordinate(random, scale), Coordinate(random, scale), Coordinate(random, scale));
    }

    static Float3 Unit(Random random)
    {
        var candidate = Vector(random, 1f);
        var length = candidate.Length;
        return length > 1e-6f ? candidate / length : new Float3(1f, 0f, 0f);
    }

    static Quaternion Rotation(Random random)
    {
        return Quaternion.AxisAngle(Unit(random), (float)(random.NextDouble() * 6.2831853));
    }

    static void CheckVector(VectorOffset offset, Float3 destination, float elapsed)
    {
        Near3(ReferenceVector(offset, destination, elapsed), offset.Apply(destination, elapsed));
    }

    static void CheckRotation(RotationOffset offset, Quaternion destination, float elapsed)
    {
        Near4(ReferenceRotation(offset, destination, elapsed), offset.Apply(destination, elapsed));
    }

    static void SweepVector(VectorOffset offset, Float3 destination)
    {
        var duration = offset.Decay.Duration;
        for (var i = 0; i <= 10; i++)
            CheckVector(offset, destination, -1f + i * (2f * duration + 1f) / 10f);
        CheckVector(offset, destination, duration);
        CheckVector(offset, destination, duration + 0.5f);
        CheckVector(offset, destination, duration * 2f + 1f);
        CheckVector(offset, destination, float.NaN);
    }

    static void SweepRotation(RotationOffset offset, Quaternion destination)
    {
        var duration = offset.Decay.Duration;
        for (var i = 0; i <= 10; i++)
            CheckRotation(offset, destination, -1f + i * (2f * duration + 1f) / 10f);
        CheckRotation(offset, destination, duration);
        CheckRotation(offset, destination, duration + 0.5f);
        CheckRotation(offset, destination, duration * 2f + 1f);
        CheckRotation(offset, destination, float.NaN);
    }

    static void ExactPose(Pose expected, Pose actual)
    {
        var expectedRotation = Quaternion.Normalize(expected.Rotation);
        Assert.True(actual.Position.X == expected.Position.X);
        Assert.True(actual.Position.Y == expected.Position.Y);
        Assert.True(actual.Position.Z == expected.Position.Z);
        Assert.True(actual.Rotation.X == expectedRotation.X);
        Assert.True(actual.Rotation.Y == expectedRotation.Y);
        Assert.True(actual.Rotation.Z == expectedRotation.Z);
        Assert.True(actual.Rotation.W == expectedRotation.W);
        Assert.True(actual.Scale.X == expected.Scale.X);
        Assert.True(actual.Scale.Y == expected.Scale.Y);
        Assert.True(actual.Scale.Z == expected.Scale.Z);
    }

    [Fact]
    public void VectorApplyMatchesReferenceFromNegativeToTwiceDuration()
    {
        var random = new Random(4177);
        for (var i = 0; i < 200; i++)
        {
            var offset = VectorOffset.Create(Vector(random, 5f), Vector(random, 5f), Vector(random, 5f), 0.02f + (float)random.NextDouble() * 0.08f, 0.1f + (float)random.NextDouble() * 1.4f);
            SweepVector(offset, Vector(random, 5f));
        }
    }

    [Fact]
    public void RotationApplyMatchesReferenceFromNegativeToTwiceDuration()
    {
        var random = new Random(9021);
        for (var i = 0; i < 200; i++)
        {
            var offset = RotationOffset.Create(Rotation(random), Rotation(random), Rotation(random), 0.02f + (float)random.NextDouble() * 0.08f, 0.1f + (float)random.NextDouble() * 1.4f);
            SweepRotation(offset, Rotation(random));
        }
    }

    [Fact]
    public void ClearedOffsetsReturnDestination()
    {
        SweepVector(default, new Float3(1f, 2f, 3f));
        SweepRotation(default, Quaternion.AxisAngle(new Float3(0f, 0f, 1f), 0.7f));
    }

    [Fact]
    public void FinishedPoseTransitionReturnsDestinationExactly()
    {
        var source = Pose.Identity;
        source.Position = new Float3(3f, -2f, 1f);
        source.Rotation = Quaternion.AxisAngle(new Float3(0f, 1f, 0f), 1f);
        source.Scale = new Float3(2f, 0.5f, 3f);
        var history = new PoseHistory();
        history.Reset(source);
        var destination = Pose.Identity;
        destination.Position = new Float3(-1f, 4f, 0.5f);
        var unit = Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.4f);
        destination.Rotation = new Quaternion(unit.X * 2f, unit.Y * 2f, unit.Z * 2f, unit.W * 2f);
        destination.Scale = new Float3(0.5f, 2f, 1.5f);
        var transition = PoseTransition.Create(history, destination, 0.5f);
        var duration = Scalar.Max(transition.Position.Decay.Duration, Scalar.Max(transition.Rotation.Decay.Duration, transition.LogScale.Decay.Duration));
        Assert.True(duration > 0f);
        var exact = PoseTransition.Apply(ref transition, destination, duration);
        ExactPose(destination, exact);
        var late = PoseTransition.Apply(ref transition, destination, duration + 5f);
        ExactPose(destination, late);
    }

    [Fact]
    public void EmptyHistoryTransitionReturnsDestinationExactly()
    {
        var destination = Pose.Identity;
        destination.Position = new Float3(-1f, 4f, 0.5f);
        destination.Rotation = Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.4f);
        destination.Scale = new Float3(0.5f, 2f, 1.5f);
        var transition = PoseTransition.Create(new PoseHistory(), destination, 0.5f);
        var output = PoseTransition.Apply(ref transition, destination, 0f);
        ExactPose(destination, output);
    }
}
