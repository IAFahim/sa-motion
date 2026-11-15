using System.Runtime.InteropServices;
using SA.Animation.Content;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Animation.Tests;

public class SegmentSamplingTests
{
    sealed record Flat(SharedClip Clip, SegmentRange[] Segments, TrackBlock[] Blocks, byte[] Bytes);

    static Flat Load(int count, bool packed, bool loop)
    {
        var motion = SharedCodecTests.Motion(count);
        Assert.Equal(AnimationError.None, SharedClipEncoder.Encode(SharedCodecTests.Family, SharedCodecTests.Parents, SharedCodecTests.Reference(),
            motion.Samples, count, count == 1 ? 0 : 1, loop, RootPolicy.Planar, motion.Root, [], [], [], packed, CompressionLimits.Default, out var encoded, out _));
        Assert.Equal(AnimationError.None, SharedClipReader.Load(encoded, SharedCodecTests.Family, SharedCodecTests.Reference(), out var clip));
        Assert.Equal(AnimationError.None, SharedClipStorage.Measure(clip, out var blockCount, out var byteCount));
        var segments = new SegmentRange[clip!.Segments.Length];
        var blocks = new TrackBlock[blockCount];
        var bytes = new byte[byteCount];
        Assert.Equal(AnimationError.None, SharedClipStorage.Write(clip, segments, blocks, bytes));
        return new Flat(clip, segments, blocks, bytes);
    }

    static AnimationError Sample(Flat flat, double seconds, ReadOnlySpan<Pose> reference, Span<Pose> output) =>
        SegmentSampling.Sample(flat.Clip.Duration, flat.Clip.SampleCount, flat.Clip.Loop, seconds, flat.Clip.Tracks, flat.Segments, flat.Blocks, flat.Bytes, reference, output);

    static bool SameBits(ReadOnlySpan<Pose> a, ReadOnlySpan<Pose> b) => MemoryMarshal.AsBytes(a).SequenceEqual(MemoryMarshal.AsBytes(b));

    [Theory]
    [InlineData(1, true, false)]
    [InlineData(2, true, false)]
    [InlineData(17, true, true)]
    [InlineData(18, false, false)]
    [InlineData(35, true, true)]
    [InlineData(35, false, false)]
    [InlineData(100, true, true)]
    [InlineData(100, false, true)]
    public void FlatStorageSamplesTheSameBitsAsTheLoadedClip(int count, bool packed, bool loop)
    {
        var flat = Load(count, packed, loop);
        var reference = SharedCodecTests.Reference();
        var expected = new Pose[4];
        var actual = new Pose[4];
        var random = new Random(20261116);
        var grid = 4 * count;
        for (var step = 0; step < grid + 2000; step++)
        {
            var seconds = step < grid ? step / (double)Math.Max(1, 2 * (count - 1)) - .5 : random.NextDouble() * 6 - 3;
            var loaded = flat.Clip.Sample(seconds, reference, expected);
            var sampled = Sample(flat, seconds, reference, actual);
            Assert.Equal(AnimationError.None, loaded);
            Assert.Equal(loaded, sampled);
            Assert.True(SameBits(expected, actual), $"time {seconds} differs");
        }
    }

    [Fact]
    public void StorageIsContiguousRepeatableAndRejectsShortBuffers()
    {
        var flat = Load(35, true, false);
        Assert.Equal(3, flat.Segments.Length);
        Assert.Equal(flat.Clip.Segments.Length * flat.Clip.Tracks.Length, flat.Blocks.Length);
        var cursor = 0;
        for (var i = 0; i < flat.Segments.Length; i++)
        {
            Assert.Equal(i * SampleLocator.SegmentIntervals, flat.Segments[i].FirstSample);
            Assert.Equal(flat.Clip.Segments[i].SampleCount, flat.Segments[i].SampleCount);
            Assert.Equal(cursor, flat.Segments[i].ByteOffset);
            Assert.Equal(flat.Clip.Segments[i].Bytes.Length, flat.Segments[i].ByteLength);
            cursor += flat.Segments[i].ByteLength;
        }
        Assert.Equal(flat.Bytes.Length, cursor);
        var again = Load(35, true, false);
        Assert.Equal(flat.Bytes, again.Bytes);
        Assert.Equal(flat.Segments, again.Segments);
        for (var i = 0; i < flat.Blocks.Length; i++)
        {
            Assert.Equal(flat.Blocks[i].Offset, again.Blocks[i].Offset);
            Assert.Equal(flat.Blocks[i].Encoding, again.Blocks[i].Encoding);
            Assert.Equal(flat.Blocks[i].Min, again.Blocks[i].Min);
            Assert.Equal(flat.Blocks[i].Max, again.Blocks[i].Max);
            var source = flat.Clip.Segments[i / flat.Clip.Tracks.Length].Blocks[i % flat.Clip.Tracks.Length];
            Assert.Equal(source.Offset, flat.Blocks[i].Offset);
            Assert.Equal(source.Encoding, flat.Blocks[i].Encoding);
        }
        var segments = new SegmentRange[3];
        var blocks = new TrackBlock[flat.Blocks.Length];
        var bytes = new byte[flat.Bytes.Length];
        Assert.Equal(AnimationError.CapacityExceeded, SharedClipStorage.Write(flat.Clip, segments.AsSpan(0, 2), blocks, bytes));
        Assert.Equal(AnimationError.CapacityExceeded, SharedClipStorage.Write(flat.Clip, segments, blocks.AsSpan(0, blocks.Length - 1), bytes));
        Assert.Equal(AnimationError.CapacityExceeded, SharedClipStorage.Write(flat.Clip, segments, blocks, bytes.AsSpan(0, bytes.Length - 1)));
        Assert.All(segments, range => Assert.Equal(default, range));
        Assert.All(bytes, value => Assert.Equal(0, value));
        Assert.Equal(AnimationError.InvalidFormat, SharedClipStorage.Write(null, segments, blocks, bytes));
        Assert.Equal(AnimationError.InvalidFormat, SharedClipStorage.Measure(null, out var blockCount, out var byteCount));
        Assert.Equal(0, blockCount);
        Assert.Equal(0, byteCount);
    }

    [Fact]
    public void BrokenLayoutsReturnAnErrorAndNeverThrow()
    {
        var flat = Load(35, true, false);
        var reference = SharedCodecTests.Reference();
        var output = new Pose[4];
        AnimationError Run(double seconds, ReadOnlySpan<TrackDescription> tracks, ReadOnlySpan<SegmentRange> segments, ReadOnlySpan<TrackBlock> blocks, ReadOnlySpan<byte> bytes, ReadOnlySpan<Pose> family) =>
            SegmentSampling.Sample(1, 35, false, seconds, tracks, segments, blocks, bytes, family, output);
        Assert.Equal(AnimationError.None, Run(1, flat.Clip.Tracks, flat.Segments, flat.Blocks, flat.Bytes, reference));
        Assert.Equal(AnimationError.InvalidFrame, Run(.5, flat.Clip.Tracks, flat.Segments.AsSpan(0, 1), flat.Blocks, flat.Bytes, reference));
        Assert.Equal(AnimationError.CorruptData, Run(1, flat.Clip.Tracks, flat.Segments, flat.Blocks.AsSpan(0, flat.Blocks.Length - 1), flat.Bytes, reference));
        Assert.Equal(AnimationError.CorruptData, Run(1, flat.Clip.Tracks, flat.Segments, flat.Blocks, flat.Bytes.AsSpan(0, flat.Bytes.Length - 1), reference));
        Assert.Equal(AnimationError.InvalidFrame, Run(1, flat.Clip.Tracks.AsSpan(0, 6), flat.Segments, flat.Blocks, flat.Bytes, reference));
        Assert.Equal(AnimationError.InvalidFrame, Run(1, [], flat.Segments, flat.Blocks, flat.Bytes, []));
        Assert.Equal(AnimationError.InvalidFrame, SegmentSampling.Sample(double.NaN, 35, false, 0, flat.Clip.Tracks, flat.Segments, flat.Blocks, flat.Bytes, reference, output));
        Assert.Equal(AnimationError.AliasedBuffers, SegmentSampling.Sample(1, 35, false, 0, flat.Clip.Tracks, flat.Segments, flat.Blocks, flat.Bytes, reference, reference));
        var hostile = new[]
        {
            new SegmentRange(int.MinValue, 17, 0, flat.Bytes.Length), new SegmentRange(int.MaxValue, 17, 0, flat.Bytes.Length),
            new SegmentRange(0, 0, 0, flat.Bytes.Length), new SegmentRange(0, 18, 0, flat.Bytes.Length), new SegmentRange(0, int.MinValue, 0, 0),
            new SegmentRange(0, 17, -1, 8), new SegmentRange(0, 17, 0, -1), new SegmentRange(0, 17, int.MaxValue, int.MaxValue),
            new SegmentRange(0, 17, flat.Bytes.Length, 1), new SegmentRange(0, 17, int.MinValue, int.MinValue)
        };
        foreach (var range in hostile)
            Assert.NotEqual(AnimationError.None, Run(0, flat.Clip.Tracks, [range, range, range], flat.Blocks, flat.Bytes, reference));
        var random = new Random(42);
        var segments = (SegmentRange[])flat.Segments.Clone();
        var blocks = (TrackBlock[])flat.Blocks.Clone();
        var bytes = (byte[])flat.Bytes.Clone();
        for (var i = 0; i < 20000; i++)
        {
            flat.Segments.CopyTo(segments, 0);
            flat.Blocks.CopyTo(blocks, 0);
            flat.Bytes.CopyTo(bytes, 0);
            var choice = random.Next(4);
            if (choice == 0)
                segments[random.Next(segments.Length)] = new SegmentRange(random.Next(-40, 40), random.Next(-2, 20), random.Next(-8, bytes.Length + 8), random.Next(-8, bytes.Length + 8));
            else if (choice == 1)
                blocks[random.Next(blocks.Length)] = new TrackBlock(random.Next(int.MinValue, int.MaxValue), (TrackEncoding)random.Next(0, 8), new Float3(-1, -1, -1), new Float3(1, 1, 1));
            else if (choice == 2)
                bytes[random.Next(bytes.Length)] ^= (byte)random.Next(1, 256);
            else
                segments[random.Next(segments.Length)] = new SegmentRange(random.Next(), random.Next(), random.Next(int.MinValue, int.MaxValue), random.Next(int.MinValue, int.MaxValue));
            var result = Run(random.NextDouble() * 3 - 1, flat.Clip.Tracks, segments, blocks, bytes, reference);
            Assert.True(Enum.IsDefined(result));
        }
    }

    [Fact]
    public void ManyCharactersSampleOneStorageInParallelWithTheSameBits()
    {
        var flat = Load(100, true, true);
        var reference = SharedCodecTests.Reference();
        const int characters = 256;
        var serial = new Pose[characters * 4];
        var parallel = new Pose[characters * 4];
        for (var character = 0; character < characters; character++)
            Assert.Equal(AnimationError.None, Sample(flat, character * .0371 - 2, reference, serial.AsSpan(character * 4, 4)));
        var errors = new AnimationError[characters];
        Parallel.For(0, characters, character =>
            errors[character] = Sample(flat, character * .0371 - 2, reference, parallel.AsSpan(character * 4, 4)));
        Assert.All(errors, error => Assert.Equal(AnimationError.None, error));
        Assert.True(SameBits(serial, parallel));
    }

    [Fact]
    public void SamplingFlatStorageAllocatesNothing()
    {
        var flat = Load(100, true, true);
        var reference = SharedCodecTests.Reference();
        var output = new Pose[4];
        for (var i = 0; i < 20000; i++)
            Assert.Equal(AnimationError.None, Sample(flat, i * .0137, reference, output));
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = AnimationError.None;
        for (var i = 20000; i < 21000; i++)
            result = Sample(flat, i * .0137, reference, output);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(AnimationError.None, result);
        Assert.Equal(0, allocated);
    }
}
