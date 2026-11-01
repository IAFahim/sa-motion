using System;
using SA.Motion.Numerics;

namespace SA.Animation
{
    public static class ClipSampling
    {
        public static Pose At(ReadOnlySpan<Pose> samples, int boneCount, int bone, float frame)
        {
            if (boneCount <= 0 || bone < 0 || bone >= boneCount)
                return Pose.Identity;
            var frames = samples.Length / boneCount;
            if (frames <= 0)
                return Pose.Identity;
            Locate(frames, frame, out var first, out var next, out var blend);
            return Sample(samples, boneCount, bone, first, next, blend);
        }

        internal static int Stride(int sampleCount, int frameCount) => frameCount > 0 ? sampleCount / frameCount : 0;

        internal static void Locate(int frames, float frame, out int first, out int next, out float blend)
        {
            var last = Math.Max(0, frames - 1);
            var position = frame > 0f ? (frame < last ? frame : last) : 0f;
            first = Math.Min(Math.Max((int)position, 0), last);
            next = Math.Min(first + 1, last);
            var fraction = position - first;
            blend = fraction > 0f ? (fraction < 1f ? fraction : 1f) : 0f;
        }

        internal static Pose Sample(ReadOnlySpan<Pose> samples, int stride, int bone, int first, int next, float blend) =>
            PoseMath.Lerp(samples[first * stride + bone], samples[next * stride + bone], blend);
    }
}
