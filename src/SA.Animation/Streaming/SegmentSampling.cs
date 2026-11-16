using System;
using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Animation
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct SegmentRange
    {
        public readonly int FirstSample;
        public readonly int SampleCount;
        public readonly int ByteOffset;
        public readonly int ByteLength;
        public SegmentRange(int firstSample, int sampleCount, int byteOffset, int byteLength)
        { FirstSample = firstSample; SampleCount = sampleCount; ByteOffset = byteOffset; ByteLength = byteLength; }
    }

    public static class SegmentSampling
    {
        public static AnimationError Sample(double duration, int samples, bool loop, double seconds,
            ReadOnlySpan<TrackDescription> tracks, ReadOnlySpan<SegmentRange> segments, ReadOnlySpan<TrackBlock> blocks, ReadOnlySpan<byte> bytes,
            ReadOnlySpan<Pose> familyReference, Span<Pose> output)
        {
            var error = SampleLocator.Locate(duration, samples, seconds, loop, out var window);
            if (error != AnimationError.None)
                return error;
            if (tracks.Length == 0 || tracks.Length != 2 * familyReference.Length || window.Segment >= segments.Length)
                return AnimationError.InvalidFrame;
            ref readonly var segment = ref segments[window.Segment];
            var firstBlock = (long)window.Segment * tracks.Length;
            if (firstBlock + tracks.Length > blocks.Length || !Wire.Fits(bytes.Length, segment.ByteOffset, segment.ByteLength))
                return AnimationError.CorruptData;
            return SegmentDecoder.Decode(tracks, blocks.Slice((int)firstBlock, tracks.Length), bytes.Slice(segment.ByteOffset, segment.ByteLength),
                segment.SampleCount, window.First - segment.FirstSample, window.Second - segment.FirstSample, window.Alpha, familyReference, output);
        }
    }
}
