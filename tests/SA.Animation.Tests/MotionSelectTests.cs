using System.Runtime.InteropServices;
using SA.Animation;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Animation.Tests;

public class MotionSelectTests
{
    static readonly float[] Speeds = { 0f, 1.4f, 3.5f, 6f };

    [Fact]
    public void LinearPicksTheNearestAndHoldsTheCurrentInsideTheMargin()
    {
        Assert.Equal(0, MotionSelect.Linear(Speeds, 0.3f, -1, 0f));
        Assert.Equal(1, MotionSelect.Linear(Speeds, 1.9f, -1, 0f));
        Assert.Equal(2, MotionSelect.Linear(Speeds, 2.5f, -1, 0f));
        Assert.Equal(1, MotionSelect.Linear(Speeds, 2.5f, 1, 0.3f));
        Assert.Equal(2, MotionSelect.Linear(Speeds, 2.8f, 1, 0.3f));
        Assert.Equal(3, MotionSelect.Linear(Speeds, 50f, 0, 0.3f));
    }

    [Fact]
    public void LinearIsTotal()
    {
        Assert.Equal(-1, MotionSelect.Linear(ReadOnlySpan<float>.Empty, 1f, 0, 0f));
        Assert.Equal(2, MotionSelect.Linear(Speeds, float.NaN, 2, 0f));
        Assert.Equal(0, MotionSelect.Linear(Speeds, float.NaN, -1, 0f));
        Assert.Equal(1, MotionSelect.Linear(new[] { float.NaN, 2f }, 0f, 0, 0f));
        Assert.Equal(1, MotionSelect.Linear(Speeds, 1.4f, 9, float.NaN));
    }

    [Fact]
    public void LinearSwitchesOnlyOnceWhileTheValueJittersAtABoundary()
    {
        var current = 1;
        var switches = 0;
        for (var frame = 0; frame < 200; frame++)
        {
            var value = 2.45f + 0.1f * MathF.Sin(frame * 1.7f) + frame * 0.002f;
            var next = MotionSelect.Linear(Speeds, value, current, 0.3f);
            switches += next != current ? 1 : 0;
            current = next;
        }
        Assert.Equal(1, switches);
        Assert.Equal(2, current);
    }

    [Fact]
    public void PlanarPicksTheNearestPointWithHysteresis()
    {
        var points = new[] { new Float2(0f, 0f), new Float2(0f, 2f), new Float2(2f, 0f), new Float2(-2f, 0f) };
        Assert.Equal(1, MotionSelect.Planar(points, new Float2(0.2f, 1.5f), -1, 0f));
        Assert.Equal(0, MotionSelect.Planar(points, new Float2(0.2f, 1.1f), 0, 0.5f));
        Assert.Equal(1, MotionSelect.Planar(points, new Float2(0.2f, 1.6f), 0, 0.5f));
        Assert.Equal(3, MotionSelect.Planar(points, new Float2(float.PositiveInfinity, 0f), 3, 0f));
        Assert.Equal(-1, MotionSelect.Planar(ReadOnlySpan<Float2>.Empty, Float2.Zero, -1, 0f));
    }

    [Fact]
    public void DirectionalMapsHeadingsClockwiseFromForward()
    {
        var all = Enumerable.Range(0, 8).Select(i => new ClipIndex((ushort)i)).ToArray();
        Assert.Equal((int)Heading.Forward, MotionSelect.Directional(all, new Float2(0f, 1f), -1, 0f));
        Assert.Equal((int)Heading.ForwardRight, MotionSelect.Directional(all, new Float2(1f, 1f), -1, 0f));
        Assert.Equal((int)Heading.Right, MotionSelect.Directional(all, new Float2(1f, 0f), -1, 0f));
        Assert.Equal((int)Heading.Back, MotionSelect.Directional(all, new Float2(0f, -1f), -1, 0f));
        Assert.Equal((int)Heading.BackLeft, MotionSelect.Directional(all, new Float2(-1f, -1f), -1, 0f));
        Assert.Equal((int)Heading.ForwardLeft, MotionSelect.Directional(all, new Float2(-1f, 1.1f), -1, 0f));
        Assert.Equal((int)Heading.Forward, MotionSelect.Directional(all, new Float2(0.5f, 1f), 0, 0.2f));
        Assert.Equal((int)Heading.ForwardRight, MotionSelect.Directional(all, new Float2(0.7f, 1f), 0, 0.2f));
        Assert.Equal((int)Heading.Left, MotionSelect.Directional(all, Float2.Zero, (int)Heading.Left, 0f));
    }

    [Fact]
    public void DirectionalFallsBackToTheNearestAuthoredHeading()
    {
        var four = new ClipIndex[8];
        for (var i = 0; i < 8; i++)
            four[i] = i % 2 == 0 ? new ClipIndex((ushort)(10 + i)) : ClipIndex.None;
        Assert.Equal((int)Heading.Right, MotionSelect.Directional(four, new Float2(1f, 0.3f), -1, 0f));
        Assert.Equal((int)Heading.Forward, MotionSelect.Directional(four, new Float2(1f, 1.2f), (int)Heading.ForwardRight, 0f));
        Assert.Equal(-1, MotionSelect.Directional(Enumerable.Repeat(ClipIndex.None, 8).ToArray(), new Float2(1f, 0f), 0, 0f));
        Assert.Equal((int)Heading.Forward, MotionSelect.Directional(four, Float2.Zero, -1, 0f));
    }

    [Fact]
    public void IdleHasAHysteresisBandAboveTheIdleSpeed()
    {
        Assert.True(MotionSelect.Idle(0.1f, false, 0.2f, 0.1f));
        Assert.False(MotionSelect.Idle(0.25f, false, 0.2f, 0.1f));
        Assert.True(MotionSelect.Idle(0.25f, true, 0.2f, 0.1f));
        Assert.False(MotionSelect.Idle(0.31f, true, 0.2f, 0.1f));
        Assert.True(MotionSelect.Idle(float.NaN, true, 0.2f, 0.1f));
    }

    [Fact]
    public void LocalVelocityIsExpressedInTheFacingFrame()
    {
        var facing = Quaternion.AxisAngle(new Float3(0f, 1f, 0f), Scalar.Pi * 0.5f);
        var local = MotionSelect.Planar(new Float3(1f, 0f, 0f), facing);
        Assert.InRange(local.X, -1e-5f, 1e-5f);
        Assert.InRange(local.Y, 0.99999f, 1.00001f);
    }

    [Fact]
    public void PlayRateMatchesTheAuthoredSpeedWithinItsLimits()
    {
        Assert.Equal(1.5f, PlayRate.Match(3f, 2f, 0.5f, 2f));
        Assert.Equal(2f, PlayRate.Match(9f, 2f, 0.5f, 2f));
        Assert.Equal(0.5f, PlayRate.Match(0f, 2f, 0.5f, 2f));
        Assert.Equal(1f, PlayRate.Match(3f, 0f, 0.5f, 2f));
        Assert.Equal(1f, PlayRate.Match(float.NaN, 2f, 0.5f, 2f));
    }

    [Fact]
    public void PlaybackStateMatchesTheUnityLayoutByteForByte()
    {
        Assert.Equal(2, Marshal.SizeOf<ClipIndex>());
        Assert.Equal(2, Marshal.SizeOf<MaskIndex>());
        Assert.Equal(28, Marshal.SizeOf<ClipState>());
        Assert.Equal(8, (int)Marshal.OffsetOf<ClipState>(nameof(ClipState.Phase)));
        Assert.Equal(44, Marshal.SizeOf<PoseSum>());
    }
}

public class PhaseTests
{
    [Fact]
    public void ShapesAreTotalForEveryWrap()
    {
        Assert.Equal(0.25f, Phase.Shape(1.25f, Wrap.Loop), 6);
        Assert.Equal(0.75f, Phase.Shape(-0.25f, Wrap.Loop), 6);
        Assert.Equal(1f, Phase.Shape(3f, Wrap.Clamp), 6);
        Assert.Equal(0f, Phase.Shape(-3f, Wrap.Clamp), 6);
        Assert.Equal(0.5f, Phase.Shape(1.5f, Wrap.PingPong), 6);
        Assert.Equal(1f, Phase.Shape(1f, Wrap.PingPong), 6);
        Assert.Equal(0.5f, Phase.Shape(-0.5f, Wrap.PingPong), 6);
        Assert.Equal(0f, Phase.Shape(float.NaN, Wrap.Loop));
        Assert.Equal(1.5f, Phase.Wrapped(3.5f, Wrap.PingPong), 6);
        Assert.Equal(15f, Phase.Frame(0.5f, Wrap.Loop, 31), 5);
        Assert.Equal(0f, Phase.Frame(0.5f, Wrap.Loop, 0));
    }
}
