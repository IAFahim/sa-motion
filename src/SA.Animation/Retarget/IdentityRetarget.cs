using System;
using SA.Motion.Numerics;

namespace SA.Animation
{
    public static class IdentityRetarget
    {
        public static AnimationError Apply(AssetId clipFamily, AssetId rigFamily, ReadOnlySpan<int> parents, ReadOnlySpan<Pose> sampled,
            Span<RigidPose> modelScratch, Span<Pose> output)
        {
            if (clipFamily.IsEmpty || clipFamily != rigFamily) return AnimationError.FamilyMismatch;
            if (parents.Length == 0 || parents.Length > SampleLocator.MaxBones || parents.Length != sampled.Length || output.Length < sampled.Length)
                return AnimationError.InvalidTable;
            if (sampled.Overlaps(output)) return AnimationError.AliasedBuffers;
            var error = RigidMath.Model(parents, sampled, modelScratch);
            if (error != AnimationError.None) return error;
            sampled.CopyTo(output);
            return AnimationError.None;
        }
    }
}
