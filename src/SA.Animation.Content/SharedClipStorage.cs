using System;

namespace SA.Animation.Content
{
    public static class SharedClipStorage
    {
        public static AnimationError Measure(SharedClip? clip, out int blocks, out int bytes)
        {
            blocks = 0;
            bytes = 0;
            if (clip == null || clip.Tracks.Length == 0 || clip.Segments.Length == 0)
                return AnimationError.InvalidFormat;
            var blockTotal = (long)clip.Segments.Length * clip.Tracks.Length;
            var byteTotal = 0L;
            for (var i = 0; i < clip.Segments.Length; i++)
            {
                var segment = clip.Segments[i];
                if (segment.Bytes == null || segment.Blocks == null || segment.Blocks.Length != clip.Tracks.Length)
                    return AnimationError.InvalidFormat;
                byteTotal += segment.Bytes.Length;
            }
            if (blockTotal > int.MaxValue || byteTotal > int.MaxValue)
                return AnimationError.CapacityExceeded;
            blocks = (int)blockTotal;
            bytes = (int)byteTotal;
            return AnimationError.None;
        }

        public static AnimationError Write(SharedClip? clip, Span<SegmentRange> segments, Span<TrackBlock> blocks, Span<byte> bytes)
        {
            if (clip == null)
                return AnimationError.InvalidFormat;
            var error = Measure(clip, out var blockCount, out var byteCount);
            if (error != AnimationError.None)
                return error;
            if (segments.Length < clip.Segments.Length || blocks.Length < blockCount || bytes.Length < byteCount)
                return AnimationError.CapacityExceeded;
            var tracks = clip.Tracks.Length;
            var cursor = 0;
            for (var i = 0; i < clip.Segments.Length; i++)
            {
                var segment = clip.Segments[i];
                segments[i] = new SegmentRange(segment.FirstSample, segment.SampleCount, cursor, segment.Bytes.Length);
                segment.Blocks.AsSpan().CopyTo(blocks.Slice(i * tracks, tracks));
                segment.Bytes.AsSpan().CopyTo(bytes.Slice(cursor, segment.Bytes.Length));
                cursor += segment.Bytes.Length;
            }
            return AnimationError.None;
        }
    }
}
