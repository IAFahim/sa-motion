using System;
using SA.Motion.Numerics;

namespace SA.Animation
{
    public static class CanonicalBlend
    {
        public static AnimationError Evaluate(ReadOnlySpan<ClipContribution> contributions, ReadOnlySpan<LayerInput> layers, ReadOnlySpan<Pose> decoded,
            ReadOnlySpan<Pose> additiveReferences, ReadOnlySpan<float> masks, ReadOnlySpan<Pose> familyReference, Span<Pose> output)
        {
            var count = familyReference.Length;
            if (count <= 0 || count > SampleLocator.MaxBones || output.Length < count || decoded.Length != (long)count * contributions.Length)
                return AnimationError.InvalidFrame;
            if (decoded.Overlaps(output) || familyReference.Overlaps(output) || additiveReferences.Overlaps(output))
                return AnimationError.AliasedBuffers;
            var error = TimelineInput.Validate(contributions, layers);
            if (error != AnimationError.None) return error;
            for (var i = 0; i < count; i++) if (!RigidMath.UnitPose(familyReference[i])) return AnimationError.InvalidFrame;
            for (var i = 0; i < decoded.Length; i++) if (!RigidMath.UnitPose(decoded[i])) return AnimationError.InvalidFrame;
            for (var i = 0; i < masks.Length; i++) if (!Scalar.IsFinite(masks[i]) || masks[i] < 0 || masks[i] > 1) return AnimationError.InvalidFrame;
            for (var i = 0; i < contributions.Length; i++)
                if (contributions[i].MaskIndex != ushort.MaxValue && ((long)contributions[i].MaskIndex + 1) * count > masks.Length)
                    return AnimationError.InvalidFrame;
            for (var i = 0; i < layers.Length; i++)
                if (layers[i].Mode == ClipMode.Additive)
                    for (var j = layers[i].First; j < layers[i].First + layers[i].Count; j++)
                    {
                        if ((long)(j + 1) * count > additiveReferences.Length) return AnimationError.InvalidFrame;
                        for (var b = 0; b < count; b++) if (!RigidMath.UnitPose(additiveReferences[j * count + b])) return AnimationError.InvalidFrame;
                    }
            for (var bone = 0; bone < count; bone++)
            {
                var pose = familyReference[bone];
                for (var l = 0; l < layers.Length; l++)
                {
                    var layer = layers[l];
                    if (layer.Mode == ClipMode.Override)
                    {
                        var anchor = -1;
                        var largest = -1f;
                        for (var i = layer.First; i < layer.First + layer.Count; i++)
                        {
                            var weight = Weight(contributions[i], masks, count, bone);
                            if (weight > largest) { largest = weight; anchor = i; }
                        }
                        var sum = default(PoseSum);
                        if (anchor >= 0)
                        {
                            var referenceQ = decoded[anchor * count + bone].Rotation;
                            for (var i = layer.First; i < layer.First + layer.Count; i++)
                            {
                                var weight = Weight(contributions[i], masks, count, bone);
                                if (weight <= 0) continue;
                                var sample = decoded[i * count + bone];
                                var q = PoseMath.Align(sample.Rotation, referenceQ);
                                sum.Position += sample.Position * weight;
                                sum.Rotation = new Quaternion(sum.Rotation.X + q.X * weight, sum.Rotation.Y + q.Y * weight, sum.Rotation.Z + q.Z * weight, sum.Rotation.W + q.W * weight);
                                sum.Weight += weight;
                            }
                        }
                        if (sum.Weight > 0)
                        {
                            if (!sum.Position.IsFinite || !Scalar.IsFinite(sum.Weight)) return AnimationError.InvalidFrame;
                            var mean = new Pose { Position = sum.Position / sum.Weight, Rotation = Quaternion.Normalize(sum.Rotation), Scale = Float3.One };
                            if (!RigidMath.UnitPose(mean) || !(mean.Position - pose.Position).IsFinite) return AnimationError.InvalidFrame;
                            pose = PoseMath.Lerp(pose, mean, Math.Min(1f, sum.Weight) * layer.Weight);
                        }
                    }
                    else
                        for (var i = layer.First; i < layer.First + layer.Count; i++)
                        {
                            var weight = Weight(contributions[i], masks, count, bone) * layer.Weight;
                            var sample = decoded[i * count + bone];
                            var reference = additiveReferences[i * count + bone];
                            pose.Position += (sample.Position - reference.Position) * weight;
                            var delta = Quaternion.Multiply(Quaternion.InverseUnit(reference.Rotation), sample.Rotation);
                            pose.Rotation = Quaternion.Normalize(Quaternion.Multiply(pose.Rotation, RotationVector.Exp(RotationVector.Log(delta) * weight)));
                        }
                    if (!RigidMath.UnitPose(pose)) return AnimationError.InvalidFrame;
                }
                if (!RigidMath.UnitPose(pose)) return AnimationError.InvalidFrame;
                output[bone] = pose;
            }
            return AnimationError.None;
        }

        static float Weight(in ClipContribution contribution, ReadOnlySpan<float> masks, int bones, int bone) =>
            contribution.Weight * (contribution.MaskIndex == ushort.MaxValue ? 1f : masks[contribution.MaskIndex * bones + bone]);
    }
}
