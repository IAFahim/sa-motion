using SA.Motion.Inertialization;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Motion.Tests;

public class PoseDecayTests
{
    static Pose At(Float3 p, Quaternion q) => new() { Position = p, Rotation = q, Scale = Float3.One };
    static void Near(Float3 a, Float3 b, float tolerance = 1e-5f) => Assert.InRange((a - b).Length, 0, tolerance);
    static void Near(Quaternion a, Quaternion b, float tolerance = 1e-5f) => Assert.InRange(RotationVector.Log(Quaternion.Multiply(a, Quaternion.InverseUnit(b))).Length, 0, tolerance);

    [Fact]
    public void ExpLogHaveTheSpecifiedAxisAndShortestSignConventions()
    {
        var q = RotationVector.Exp(new Float3(0, Scalar.Pi / 2, 0));
        Near(new Float3(1, 0, 0), Quaternion.Rotate(q, new Float3(0, 0, 1)));
        Near(new Float3(0, Scalar.Pi / 2, 0), RotationVector.Log(q));
        Near(RotationVector.Log(q), RotationVector.Log(-q));
        foreach (var angle in new[] { 0f, 1e-8f, 0.5f, Scalar.Pi - 1e-6f, Scalar.Pi })
        {
            var rotation = Quaternion.AxisAngle(new Float3(1, 0, 0), angle);
            Near(rotation, RotationVector.Exp(RotationVector.Log(rotation)));
        }
    }

    [Fact]
    public void TranslationMatchesMovingDestinationVelocityAndFiniteEndpoint()
    {
        var outgoing = At(new Float3(1, 2, 3), Quaternion.Identity);
        var incoming = At(new Float3(4, -2, 1), Quaternion.Identity);
        var outVelocity = new PoseVelocity(new Float3(2, 1, -1), Float3.Zero);
        var inVelocity = new PoseVelocity(new Float3(-1, 2, 3), Float3.Zero);
        Assert.True(PoseDecay.TryCreate(outgoing, outVelocity, incoming, inVelocity, out var decay));
        Assert.True(decay.TryApply(incoming, 0, 0.5f, out var start));
        Near(outgoing.Position, start.Position);
        const float h = 0.001f;
        var next = incoming;
        next.Position += inVelocity.Linear * h;
        Assert.True(decay.TryApply(next, h, 0.5f, out var first));
        next.Position = incoming.Position + inVelocity.Linear * (h * 0.5f);
        Assert.True(decay.TryApply(next, h * 0.5f, 0.5f, out var half));
        var slope = (half.Position - start.Position) * (4 / h) - (first.Position - start.Position) / h;
        Near(outVelocity.Linear, slope, 0.002f);
        next.Position = incoming.Position + inVelocity.Linear * 0.5f;
        Assert.True(decay.TryApply(next, 0.5f, 0.5f, out var end));
        Assert.Equal(next, end);
        Assert.True(decay.TryApply(next, 10, 0.5f, out end));
        Assert.Equal(next, end);
    }

    [Fact]
    public void NoncommutingRotationMatchesOutgoingAngularVelocity()
    {
        var random = new Random(20261103);
        for (var i = 0; i < 200; i++)
        {
            var axis = new Float3((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
            axis /= axis.Length;
            var incoming = At(Float3.Zero, Quaternion.AxisAngle(new Float3(0, 0, 1), 0.7f));
            var offset = Quaternion.AxisAngle(axis, i == 0 ? 1e-8f : i == 1 ? Scalar.Pi - 1e-5f : (float)random.NextDouble() * 3f);
            var outgoing = At(Float3.Zero, Quaternion.Multiply(offset, incoming.Rotation));
            var wOut = new Float3(1.2f, -0.8f, 0.3f);
            var wIn = new Float3(-0.4f, 1.7f, -0.6f);
            Assert.True(PoseDecay.TryCreate(outgoing, new PoseVelocity(Float3.Zero, wOut), incoming, new PoseVelocity(Float3.Zero, wIn), out var decay));
            Assert.True(decay.TryApply(incoming, 0, 0.5f, out var start));
            Near(outgoing.Rotation, start.Rotation);
            const float h = 0.001f;
            var firstTarget = incoming;
            firstTarget.Rotation = Quaternion.Multiply(RotationVector.Exp(wIn * h), incoming.Rotation);
            var halfTarget = incoming;
            halfTarget.Rotation = Quaternion.Multiply(RotationVector.Exp(wIn * (h * 0.5f)), incoming.Rotation);
            Assert.True(decay.TryApply(firstTarget, h, 0.5f, out var first));
            Assert.True(decay.TryApply(halfTarget, h * 0.5f, 0.5f, out var half));
            var firstSlope = RotationVector.Log(Quaternion.Multiply(first.Rotation, Quaternion.InverseUnit(start.Rotation))) / h;
            var halfSlope = RotationVector.Log(Quaternion.Multiply(half.Rotation, Quaternion.InverseUnit(start.Rotation))) / (h * 0.5f);
            Near(wOut, halfSlope * 2f - firstSlope, 0.003f);
            Assert.True(decay.TryApply(firstTarget, 0.5f, 0.5f, out var end));
            Assert.Equal(firstTarget, end);
        }
    }

    [Fact]
    public void ASecondInterruptionStartsFromTheCorrectedPose()
    {
        var a = At(new Float3(2, 1, 0), RotationVector.Exp(new Float3(.5f, .3f, -.2f)));
        var b = At(new Float3(-2, 0, 3), RotationVector.Exp(new Float3(-.1f, .8f, .4f)));
        Assert.True(PoseDecay.TryCreate(a, default, b, default, out var first));
        Assert.True(first.TryApply(b, .15f, .5f, out var corrected));
        var c = At(new Float3(1, 3, 2), RotationVector.Exp(new Float3(.4f, -.3f, .2f)));
        Assert.True(PoseDecay.TryCreate(corrected, default, c, default, out var second));
        Assert.True(second.TryApply(c, 0, .5f, out var switchPose));
        Near(corrected.Position, switchPose.Position);
        Near(corrected.Rotation, switchPose.Rotation);
    }

    [Fact]
    public void InvalidInputsFailWithoutInventingStateAndOutputCanAliasDestination()
    {
        Assert.False(PoseDecay.TryCreate(default, default, Pose.Identity, default, out _));
        Assert.False(PoseDecay.TryCreate(Pose.Identity, new PoseVelocity(new Float3(float.NaN, 0, 0), Float3.Zero), Pose.Identity, default, out _));
        Assert.False(default(PoseDecay).TryApply(Pose.Identity, float.NaN, 1, out _));
        Assert.False(default(PoseDecay).TryApply(Pose.Identity, -1, 1, out _));
        Assert.False(RotationVector.TryLeftJacobianInverse(new Float3(10, 0, 0), Float3.Zero, out _));
        var destination = At(new Float3(3, 2, 1), Quaternion.Identity);
        Assert.True(default(PoseDecay).TryApply(destination, 0, 0, out destination));
        Assert.Equal(new Float3(3, 2, 1), destination.Position);
        var corrupt = new PoseDecay(new Float3(float.MaxValue, 0, 0), new Float3(float.MaxValue, 0, 0), Float3.Zero, Float3.Zero);
        Assert.False(corrupt.TryApply(destination, .2f, 10, out _));
    }

    [Fact]
    public void WarmDecayDoesNotAllocate()
    {
        var pose = At(new Float3(2, 1, 0), RotationVector.Exp(new Float3(.5f, .3f, -.2f)));
        Assert.True(PoseDecay.TryCreate(pose, default, Pose.Identity, default, out var decay));
        for (var i = 0; i < 1000; i++) decay.TryApply(Pose.Identity, .2f, .5f, out pose);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) decay.TryApply(Pose.Identity, .2f, .5f, out pose);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(pose.Position.X > 0);
    }
}
