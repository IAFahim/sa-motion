using System.Buffers.Binary;
using System.Text;
using SA.Animation.Content;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Animation.Tests;

public class SharedCodecTests
{
    internal static readonly AssetId Family = new(1, 2, 3, 4);
    internal static readonly int[] Parents = [-1, 0, 1, 2];
    internal static Pose[] Reference() => [Pose.Identity, At(new Float3(0, 1, 0)), At(new Float3(.5f, .5f, 0)), At(new Float3(.6f, 0, 0))];
    internal static Pose At(Float3 position) => new() { Position = position, Rotation = Quaternion.Identity, Scale = Float3.One };

    internal static (Pose[] Samples, RigidPose[] Root) Motion(int count)
    {
        var reference = Reference();
        var samples = new Pose[count * 4];
        var root = new RigidPose[count];
        for (var i = 0; i < count; i++)
        {
            var t = count == 1 ? 0 : i / (float)(count - 1);
            reference.CopyTo(samples, i * 4);
            samples[i * 4 + 1].Position.Y += .03f * MathF.Sin(t * 4);
            samples[i * 4 + 2].Rotation = RotationVector.Exp(new Float3(0, 0, -.4f + .5f * MathF.Sin(t * 4)));
            samples[i * 4 + 3].Rotation = RotationVector.Exp(new Float3(.15f * MathF.Cos(t * 3), 0, 0));
            root[i] = new RigidPose(new Float3(0, 0, 2 * t), RotationVector.Exp(new Float3(0, .4f * t, 0)));
        }
        return (samples, root);
    }

    internal static byte[] Encode(int count = 35, bool packed = true)
    {
        var motion = Motion(count);
        Assert.Equal(AnimationError.None, SharedClipEncoder.Encode(Family, Parents, Reference(), motion.Samples, count, count == 1 ? 0 : 1,
            false, RootPolicy.Planar, motion.Root, [], [], [], packed, CompressionLimits.Default, out var bytes, out _));
        return bytes;
    }

    [Fact]
    public void QuaternionGoldenAndRandomRoundTripsMeetErrorBudget()
    {
        var random = new Random(20261103);
        Span<byte> encoded = stackalloc byte[8];
        Span<byte> flipped = stackalloc byte[8];
        Assert.True(PackedRotation.Encode(Quaternion.Identity, encoded));
        Assert.Equal("0300000000000000", Convert.ToHexString(encoded).ToLowerInvariant());
        for (var i = 0; i < 20000; i++)
        {
            var q = PoseFixtures.RandomRotation(random);
            Assert.True(PackedRotation.Encode(q, encoded));
            Assert.True(PackedRotation.Encode(-q, flipped));
            Assert.True(encoded.SequenceEqual(flipped));
            Assert.True(PackedRotation.Decode(encoded, out var actual));
            var angle = RotationVector.Log(Quaternion.Multiply(q, Quaternion.InverseUnit(actual))).Length;
            Assert.InRange(angle, 0, .01f * Scalar.Pi / 180);
        }
        encoded[0] = 4;
        Assert.False(PackedRotation.Decode(encoded, out _));
        Assert.False(PackedRotation.Encode(default, encoded));
    }

    [Fact]
    public void PositionRangesHandleConstantsAndShortPackedBlocks()
    {
        var min = new Float3(-2, 3, 1);
        var max = new Float3(4, 3, 2);
        Span<byte> data = stackalloc byte[6];
        var expected = new Float3(.12345f, 3, 1.2345f);
        Assert.True(PackedPosition.Encode(expected, min, max, data));
        Assert.True(PackedPosition.Decode(data, min, max, out var actual));
        Assert.InRange(Math.Abs(actual.X - expected.X), 0, 6f / 65535 / 2 + 1e-6f);
        Assert.Equal(3, actual.Y);
        Assert.False(PackedPosition.Decode(data[..5], min, max, out _));
        Assert.False(PackedPosition.Encode(new Float3(10, 3, 1), min, max, data));
    }

    [Fact]
    public void SharedFileRoundTripsRandomAccessAndEverySegmentBoundary()
    {
        var bytes = Encode();
        Assert.Equal(bytes, Encode());
        Assert.Equal(AnimationError.None, SharedClipReader.Load(bytes, Family, Reference(), out var clip));
        Assert.NotNull(clip);
        var raw = Motion(35).Samples;
        var expected = new Pose[4];
        var actual = new Pose[4];
        var scratch = new RigidPose[8];
        for (var step = 0; step <= 136; step++)
        {
            var time = step / 136d;
            Assert.Equal(AnimationError.None, clip.Sample(time, Reference(), actual));
            for (var bone = 0; bone < 4; bone++) expected[bone] = ClipSampling.At(raw, 4, bone, (float)(time * 34));
            Assert.Equal(AnimationError.None, ObjectSpaceError.Measure(Parents, expected, actual, [], scratch, out var error));
            Assert.InRange(error.AngleRadians, 0, CompressionLimits.Default.AngleRadians);
            Assert.InRange(error.PositionMetres, 0, CompressionLimits.Default.PositionMetres);
        }
        Assert.Equal(3, clip.Segments.Length);
        Assert.Equal(35, clip.Root.Length);
        Assert.Equal(AnimationError.FamilyMismatch, SharedClipReader.Load(bytes, new AssetId(9, 9, 9, 9), Reference(), out _));
    }

    [Fact]
    public void StrictPositionalBudgetPromotesAnOffendingSegmentToRaw()
    {
        var reference = Reference();
        var samples = new Pose[12];
        for (var frame = 0; frame < 3; frame++) reference.CopyTo(samples, frame * 4);
        samples[5].Position.X = .1234567f;
        samples[9].Position.X = 10f;
        var root = Enumerable.Repeat(RigidPose.Identity, 3).ToArray();
        Assert.Equal(AnimationError.None, SharedClipEncoder.Encode(Family, Parents, reference, samples, 3, 1, false, RootPolicy.None, root,
            [], [], [], true, new CompressionLimits(0, 0), out var bytes, out var report));
        Assert.Equal(1, report.RawFallbackSegments);
        Assert.Equal(AnimationError.None, SharedClipReader.Load(bytes, Family, reference, out var loaded));
        var actual = new Pose[4];
        Assert.Equal(AnimationError.None, loaded!.Sample(.5, reference, actual));
        Assert.Equal(samples[5].Position, actual[1].Position);
    }

    [Fact]
    public void AdditiveReferenceAndContactsHaveIndependentStorage()
    {
        var motion = Motion(18);
        var reference = Reference();
        var contacts = Enumerable.Range(0, 72).Select(i => i % 4 == 0 ? .75f : 0f).ToArray();
        Assert.Equal(AnimationError.None, SharedClipEncoder.Encode(Family, Parents, reference, motion.Samples, 18, 1, true, RootPolicy.Planar,
            motion.Root, reference, contacts, [], true, CompressionLimits.Default, out var bytes, out _));
        Assert.Equal(AnimationError.None, SharedClipReader.Load(bytes, Family, reference, out var loaded));
        Assert.Equal(4, loaded!.AdditiveReference.Length);
        Assert.Equal(contacts, loaded.ContactWeights);
        Assert.True(loaded.Loop);
    }

    [Fact]
    public void HashCrcTruncationAndOffsetsAreCheckedBeforePublication()
    {
        Assert.Equal(0xcbf43926u, AnimationCrc32.Of(Encoding.ASCII.GetBytes("123456789")));
        var bytes = Encode();
        for (var length = 0; length < bytes.Length; length += Math.Max(1, bytes.Length / 20))
            Assert.NotEqual(AnimationError.None, SharedClipReader.Load(bytes.AsSpan(0, length), Family, Reference(), out _));
        var corrupt = (byte[])bytes.Clone();
        corrupt[^1] ^= 1;
        Assert.Equal(AnimationError.CorruptData, SharedClipReader.Load(corrupt, Family, Reference(), out _));
        corrupt = (byte[])bytes.Clone();
        BinaryPrimitives.WriteUInt64LittleEndian(corrupt.AsSpan(128 + 8), ulong.MaxValue);
        Assert.Equal(AnimationError.InvalidFormat, SharedClipReader.Load(corrupt, Family, Reference(), out _));
        Span<SacaChunk> directory = stackalloc SacaChunk[64];
        Assert.Equal(AnimationError.None, SacaFile.Verify(bytes, directory, out var count, out _));
        Assert.True(SacaContainer.Find(directory[..count], SacaContainer.Data, out var data));
        corrupt = (byte[])bytes.Clone();
        corrupt[data.Offset] ^= 4;
        Stamp(corrupt);
        Assert.Equal(AnimationError.CorruptData, SharedClipReader.Load(corrupt, Family, Reference(), out _));
    }

    [Fact]
    public void HostileBytesAndDecoderMetadataNeverThrow()
    {
        var random = new Random(42);
        var bytes = Encode();
        for (var i = 0; i < 500; i++)
        {
            var corrupt = (byte[])bytes.Clone();
            corrupt[random.Next(corrupt.Length)] ^= (byte)random.Next(1, 256);
            var error = SharedClipReader.Load(corrupt, Family, Reference(), out _);
            Assert.NotEqual(AnimationError.None, error);
        }
        var tracks = new[] { new TrackDescription(0, TrackChannel.Position, TrackStorage.Dynamic, default), new TrackDescription(0, TrackChannel.Rotation, TrackStorage.Default, default) };
        var blocks = new[] { new TrackBlock(int.MaxValue, TrackEncoding.Position16, Float3.Zero, Float3.One), default };
        Assert.Equal(AnimationError.CorruptData, SegmentDecoder.Decode(tracks, blocks, [], 2, 0, 1, .5f, [Pose.Identity], new Pose[1]));
    }

    internal static void Stamp(byte[] bytes)
    {
        var digest = SacaFile.Digest(bytes);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(40), digest.A);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(48), digest.B);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(56), digest.C);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(64), digest.D);
    }
}
