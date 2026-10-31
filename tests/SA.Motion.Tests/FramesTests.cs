using SA.Motion.Mechanics;
using SA.Motion.Numerics;
using Xunit;
using Quaternion = SA.Motion.Numerics.Quaternion;

namespace SA.Motion.Tests;

public class FramesTests
{
    static void Near(float expected, float actual, float tolerance = 1e-5f) => Assert.InRange(actual, expected - tolerance, expected + tolerance);
    static void Near(Float3 expected, Float3 actual, float tolerance = 1e-5f)
    {
        Near(expected.X, actual.X, tolerance);
        Near(expected.Y, actual.Y, tolerance);
        Near(expected.Z, actual.Z, tolerance);
    }
    static void NearRotation(Quaternion expected, Quaternion actual, float tolerance = 1e-5f) =>
        Near(1f, Math.Abs(Quaternion.Dot(Quaternion.Normalize(expected), Quaternion.Normalize(actual))), tolerance);
    static void NearFrame(Frame expected, Frame actual)
    {
        Near(expected.Position, actual.Position);
        NearRotation(expected.Rotation, actual.Rotation);
    }

    static Float3 RandomVector(Random random, float range)
    {
        return new Float3(
            (float)(random.NextDouble() * 2.0 - 1.0) * range,
            (float)(random.NextDouble() * 2.0 - 1.0) * range,
            (float)(random.NextDouble() * 2.0 - 1.0) * range);
    }

    static Quaternion RandomRotation(Random random)
    {
        while (true)
        {
            var candidate = new Quaternion(
                (float)(random.NextDouble() * 2.0 - 1.0),
                (float)(random.NextDouble() * 2.0 - 1.0),
                (float)(random.NextDouble() * 2.0 - 1.0),
                (float)(random.NextDouble() * 2.0 - 1.0));
            if (candidate.IsValidRotation)
                return Quaternion.Normalize(candidate);
        }
    }

    static Frame RandomFrame(Random random) => new Frame(RandomVector(random, 5f), RandomRotation(random));

    static Float3 MirrorComponent(Float3 value, Axis axis) =>
        axis == Axis.X ? new Float3(-value.X, value.Y, value.Z)
        : axis == Axis.Y ? new Float3(value.X, -value.Y, value.Z)
        : new Float3(value.X, value.Y, -value.Z);

    static bool AllFinite(Float3 value) => value.IsFinite;

    [Fact]
    public void ComposeWithInverseIsIdentity()
    {
        var random = new Random(2101);
        for (var i = 0; i < 200; i++)
        {
            var frame = RandomFrame(random);
            NearFrame(Frame.Identity, Frames.Compose(Frames.Inverse(frame), frame));
            NearFrame(Frame.Identity, Frames.Compose(frame, Frames.Inverse(frame)));
        }
    }

    [Fact]
    public void RelativeReconstructsLocal()
    {
        var random = new Random(2102);
        for (var i = 0; i < 200; i++)
        {
            var parent = RandomFrame(random);
            var local = RandomFrame(random);
            NearFrame(local, Frames.Relative(parent, Frames.Compose(parent, local)));
        }
    }

    [Fact]
    public void CompositionIsAssociative()
    {
        var random = new Random(2103);
        for (var i = 0; i < 200; i++)
        {
            var a = RandomFrame(random);
            var b = RandomFrame(random);
            var c = RandomFrame(random);
            NearFrame(Frames.Compose(Frames.Compose(a, b), c), Frames.Compose(a, Frames.Compose(b, c)));
        }
    }

    [Fact]
    public void TransformPointDistributesOverComposition()
    {
        var random = new Random(2104);
        for (var i = 0; i < 200; i++)
        {
            var a = RandomFrame(random);
            var b = RandomFrame(random);
            var point = RandomVector(random, 5f);
            Near(Frames.TransformPoint(Frames.Compose(a, b), point), Frames.TransformPoint(a, Frames.TransformPoint(b, point)));
        }
    }

    [Fact]
    public void TransformDirectionIgnoresTranslation()
    {
        var random = new Random(2105);
        for (var i = 0; i < 200; i++)
        {
            var frame = RandomFrame(random);
            var direction = RandomVector(random, 3f);
            var moved = new Frame(frame.Position + new Float3(10f, -7f, 4f), frame.Rotation);
            Near(Frames.TransformDirection(frame, direction), Frames.TransformDirection(moved, direction));
            Near(direction.Length, Frames.TransformDirection(frame, direction).Length, 1e-4f);
        }
    }

    [Theory]
    [InlineData(Axis.X)]
    [InlineData(Axis.Y)]
    [InlineData(Axis.Z)]
    public void MirrorCommutesWithTransform(Axis axis)
    {
        var random = new Random(2106 + (int)axis);
        for (var i = 0; i < 200; i++)
        {
            var frame = RandomFrame(random);
            var point = RandomVector(random, 5f);
            var mirrored = Frames.Mirror(frame, axis);
            Near(MirrorComponent(Frames.TransformPoint(frame, point), axis), Frames.TransformPoint(mirrored, MirrorComponent(point, axis)));
            NearFrame(frame, Frames.Mirror(mirrored, axis));
        }
    }

    [Fact]
    public void DegenerateFramesReturnDefinedValues()
    {
        var nan = new Float3(float.NaN, float.NaN, float.NaN);
        var inf = new Float3(float.PositiveInfinity, 0f, 0f);
        var badFrames = new[]
        {
            new Frame(nan, Quaternion.Identity),
            new Frame(inf, Quaternion.Identity),
            new Frame(new Float3(1f, 2f, 3f), default),
            new Frame(new Float3(1f, 2f, 3f), new Quaternion(float.NaN, 0f, 0f, 0f)),
            new Frame(nan, default),
        };
        var point = new Float3(1f, 2f, 3f);
        foreach (var frame in badFrames)
        {
            Assert.True(AllFinite(Frames.Inverse(frame).Position));
            Assert.True(Frames.Inverse(frame).Rotation.IsValidRotation);
            Assert.True(AllFinite(Frames.Compose(frame, frame).Position));
            Assert.True(AllFinite(Frames.TransformPoint(frame, point)));
            Assert.True(AllFinite(Frames.TransformDirection(frame, point)));
            Assert.True(AllFinite(Frames.Mirror(frame, Axis.X).Position));
            Assert.True(AllFinite(Frames.Mirror(frame, Axis.Y).Position));
            Assert.True(AllFinite(Frames.Mirror(frame, Axis.Z).Position));
        }
    }

    [Fact]
    public void ZeroQuaternionBehavesAsIdentityRotation()
    {
        var frame = new Frame(new Float3(1f, 2f, 3f), default);
        var point = new Float3(4f, 5f, 6f);
        Near(new Float3(5f, 7f, 9f), Frames.TransformPoint(frame, point));
        Near(point, Frames.TransformDirection(frame, point));
        NearFrame(Frame.Identity, Frames.Compose(Frames.Inverse(new Frame(Float3.Zero, default)), new Frame(Float3.Zero, default)));
    }
}
