using System;
using SA.Motion.Numerics;

namespace SA.Animation
{
    public static class PoseBlend
    {
        public static void Override(Span<PoseSum> sums, ReadOnlySpan<Pose> samples, int frameCount, float frame, ReadOnlySpan<float> mask, float weight)
        {
            if (!PoseMath.Positive(weight))
                return;
            var stride = ClipSampling.Stride(samples.Length, frameCount);
            var count = Math.Min(Math.Min(sums.Length, mask.Length), stride);
            ClipSampling.Locate(frameCount, frame, out var first, out var next, out var blend);
            for (var bone = 0; bone < count; bone++)
            {
                var contribution = mask[bone] * weight;
                if (PoseMath.Positive(contribution))
                    sums[bone] = PoseMath.Add(sums[bone], ClipSampling.Sample(samples, stride, bone, first, next, blend), contribution);
            }
        }

        public static void Resolve(Span<Pose> pose, Span<PoseSum> sums)
        {
            var count = Math.Min(pose.Length, sums.Length);
            for (var bone = 0; bone < count; bone++)
            {
                ref var sum = ref sums[bone];
                if (PoseMath.Positive(sum.Weight))
                    pose[bone] = PoseMath.Lerp(pose[bone], PoseMath.Mean(sum), Math.Min(1f, sum.Weight));
            }
            sums.Clear();
        }

        public static void Additive(Span<Pose> pose, ReadOnlySpan<Pose> samples, ReadOnlySpan<Pose> reference, int frameCount, float frame, ReadOnlySpan<float> mask, float weight)
        {
            if (!PoseMath.Positive(weight))
                return;
            var stride = ClipSampling.Stride(samples.Length, frameCount);
            var count = Math.Min(Math.Min(pose.Length, reference.Length), Math.Min(mask.Length, stride));
            ClipSampling.Locate(frameCount, frame, out var first, out var next, out var blend);
            for (var bone = 0; bone < count; bone++)
            {
                var contribution = mask[bone] * weight;
                if (PoseMath.Positive(contribution))
                    pose[bone] = PoseMath.Accumulate(pose[bone], PoseMath.Delta(ClipSampling.Sample(samples, stride, bone, first, next, blend), reference[bone]), contribution);
            }
        }

        public static int NextLayer(ReadOnlySpan<ClipState> states, int after)
        {
            var next = int.MaxValue;
            for (var i = 0; i < states.Length; i++)
            {
                var layer = states[i].Layer;
                if (layer > after && layer < next)
                    next = layer;
            }
            return next == int.MaxValue ? -1 : next;
        }
    }
}
