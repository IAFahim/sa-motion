using System;
using SA.Motion.Numerics;

namespace SA.Animation
{
    public static class Hierarchy
    {
        public static void Model(ReadOnlySpan<int> parents, ReadOnlySpan<Pose> local, Span<Affine> model)
        {
            var count = Math.Min(model.Length, Math.Min(parents.Length, local.Length));
            for (var bone = 0; bone < count; bone++)
            {
                var transform = PoseMath.Affine(local[bone]);
                var parent = parents[bone];
                model[bone] = parent >= 0 && parent < bone ? PoseMath.Mul(model[parent], transform) : transform;
            }
        }
    }
}
