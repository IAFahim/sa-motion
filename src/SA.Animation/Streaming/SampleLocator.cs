using System;
using System.Runtime.InteropServices;

namespace SA.Animation
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct SampleWindow
    {
        public readonly int First;
        public readonly int Second;
        public readonly float Alpha;
        public readonly int Segment;
        public readonly long Cycle;
        public SampleWindow(int first, int second, float alpha, int segment, long cycle)
        { First = first; Second = second; Alpha = alpha; Segment = segment; Cycle = cycle; }
    }

    public static class SampleLocator
    {
        public const int SegmentIntervals = 16;
        public const int MaxSamples = 1000000;
        public const int MaxBones = 1024;
        public static int SegmentCount(int samples) => samples > 0 && samples <= MaxSamples ? Math.Max(1, (samples - 2 + SegmentIntervals) / SegmentIntervals) : 0;

        public static AnimationError Locate(double duration, int samples, double seconds, bool loop, out SampleWindow window)
        {
            window = default;
            if (double.IsNaN(duration) || double.IsInfinity(duration) || duration < 0 || duration > 3600
                || double.IsNaN(seconds) || double.IsInfinity(seconds) || samples <= 0 || samples > MaxSamples
                || (duration == 0 && samples != 1) || (duration > 0 && samples < 2))
                return AnimationError.InvalidFrame;
            if (duration == 0)
                return AnimationError.None;
            var cycle = 0L;
            var time = Math.Min(duration, Math.Max(0, seconds));
            if (loop)
            {
                var count = Math.Floor(seconds / duration);
                if (count < -int.MaxValue || count > int.MaxValue)
                    return AnimationError.TimeOutOfRange;
                cycle = (long)count;
                time = Math.Max(0, Math.Min(duration, seconds - count * duration));
            }
            var frame = Math.Min(samples - 1, time / duration * (samples - 1));
            var first = (int)frame;
            window = new SampleWindow(first, Math.Min(first + 1, samples - 1), (float)(frame - first), Math.Min(first / SegmentIntervals, SegmentCount(samples) - 1), cycle);
            return AnimationError.None;
        }
    }
}
