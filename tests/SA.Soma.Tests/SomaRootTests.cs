using System;
using SA.Animation;
using SA.Motion.Numerics;
using SA.Soma;

namespace SA.Soma.Tests;

public sealed class SomaRootTests
{
    [Fact]
    public void ConstantHeadingKeepsYawAndInteriorPositions()
    {
        const int frames = 30;
        const int window = 5;
        var root = new RigidPose[frames];
        for (var i = 0; i < frames; i++)
            root[i] = new RigidPose(new Float3(0.1f * i, 0f, 0.05f * i), Quaternion.Identity);
        var output = new RigidPose[frames];
        Assert.Equal(AnimationError.None, SomaRoot.Smooth(root, window, output));
        Assert.True(output[0].Position.Length < 1e-6f);
        for (var i = 0; i < frames; i++)
        {
            Assert.True(Math.Abs(YawOf(output[i])) < 1e-4f, i.ToString());
            if (i >= window && i < frames - window)
                Assert.True((output[i].Position - root[i].Position).Length < 1e-4f, i.ToString());
            else
                Assert.True((output[i].Position - root[i].Position).Length < 0.12f, i.ToString());
        }
    }

    [Fact]
    public void TenFrameWindowRemovesFiveDegreeSineWobble()
    {
        const int frames = 60;
        const int window = 10;
        var wobble = 5f * Scalar.Pi / 180f;
        var limit = 0.5f * Scalar.Pi / 180f;
        var root = new RigidPose[frames];
        for (var i = 0; i < frames; i++)
            root[i] = new RigidPose(Float3.Zero, SomaAxes.YawRotation(wobble * MathF.Sin(2f * Scalar.Pi * i / 10f)));
        var output = new RigidPose[frames];
        Assert.Equal(AnimationError.None, SomaRoot.Smooth(root, window, output));
        Assert.True(output[0].Position.Length < 1e-6f);
        Assert.True(Math.Abs(YawOf(output[0])) < 1e-6f);
        for (var i = window; i < frames - window; i++)
        {
            Assert.True(Math.Abs(YawOf(output[i])) < limit, i.ToString());
            Assert.True(output[i].Position.Length < 1e-6f, i.ToString());
        }
    }

    [Fact]
    public void SmoothingUnwrapsYawAcrossTheWrapBoundary()
    {
        var turn = new[] { 170f, 175f, 180f, -175f, -170f };
        var root = new RigidPose[turn.Length];
        for (var i = 0; i < turn.Length; i++)
            root[i] = new RigidPose(Float3.Zero, SomaAxes.YawRotation(turn[i] * Scalar.Pi / 180f));
        var output = new RigidPose[turn.Length];
        Assert.Equal(AnimationError.None, SomaRoot.Smooth(root, 3, output));
        Assert.True(Math.Abs(YawResidual(YawOf(output[1]), 175f * Scalar.Pi / 180f)) < Scalar.Pi / 180f);
        Assert.True(Math.Abs(YawResidual(YawOf(output[2]), Scalar.Pi)) < Scalar.Pi / 180f);
        Assert.True(Math.Abs(YawResidual(YawOf(output[3]), -175f * Scalar.Pi / 180f)) < Scalar.Pi / 180f);
    }

    static float YawOf(RigidPose pose) => 2f * (float)Math.Atan2(pose.Rotation.Y, pose.Rotation.W);

    static float YawResidual(float actual, float expected)
    {
        var residual = actual - expected;
        while (residual > Scalar.Pi)
            residual -= Scalar.TwoPi;
        while (residual < -Scalar.Pi)
            residual += Scalar.TwoPi;
        return residual;
    }
}
