using System;
using SA.Animation;
using SA.Motion.Inertialization;
using SA.Motion.Numerics;

namespace SA.Shape
{
    public static class ShapeMotionSpace
    {
        public static AnimationError Normalize(in ShapeGeometry geometry, in RetargetBinding binding, ReadOnlySpan<RetargetRow> rows,
            ReadOnlySpan<Float3> offsets, ReadOnlySpan<float> motionScales, ReadOnlySpan<Pose> local, Span<RigidPose> modelScratch, Span<Pose> output)
        {
            var error = Validate(geometry, binding, rows, offsets, motionScales, local, modelScratch, output);
            if (error != AnimationError.None) return error;
            error = ShapeMath.Model(rows, local, modelScratch);
            if (error != AnimationError.None) return error;
            for (var i = 0; i < rows.Length; i++)
            {
                var residual = i == binding.TargetHips ? modelScratch[i].Position - geometry.Retarget.HipReference : local[i].Position - offsets[i];
                var position = residual / motionScales[i];
                if (!position.IsFinite) return AnimationError.InvalidFrame;
                output[i] = new Pose { Position = position, Rotation = local[i].Rotation, Scale = Float3.One };
            }
            return AnimationError.None;
        }

        public static AnimationError Compose(in ShapeGeometry geometry, in RetargetBinding binding, ReadOnlySpan<RetargetRow> rows,
            ReadOnlySpan<Float3> offsets, ReadOnlySpan<float> motionScales, ReadOnlySpan<Pose> neutral, Span<RigidPose> modelScratch, Span<Pose> output)
        {
            var error = Validate(geometry, binding, rows, offsets, motionScales, neutral, modelScratch, output);
            if (error != AnimationError.None) return error;
            for (var i = 0; i < rows.Length; i++)
            {
                var parent = rows[i].Parent < 0 ? RigidPose.Identity : modelScratch[rows[i].Parent];
                var position = offsets[i] + neutral[i].Position * motionScales[i];
                if (i == binding.TargetHips)
                {
                    var desired = geometry.Retarget.HipReference + neutral[i].Position * motionScales[i];
                    position = Quaternion.Rotate(Quaternion.InverseUnit(parent.Rotation), desired - parent.Position);
                }
                var local = new RigidPose(position, neutral[i].Rotation);
                if (!RigidMath.TryCompose(parent, local, out var model)) return AnimationError.InvalidFrame;
                output[i] = local.ToPose();
                modelScratch[i] = model;
            }
            return AnimationError.None;
        }

        public static AnimationError Differentiate(ReadOnlySpan<Pose> previousNeutral, ReadOnlySpan<Pose> currentNeutral,
            float simulationInterval, Span<PoseVelocity> output)
        {
            if (previousNeutral.Length == 0 || previousNeutral.Length != currentNeutral.Length || output.Length < currentNeutral.Length)
                return AnimationError.CapacityExceeded;
            if (!Scalar.IsFinite(simulationInterval) || simulationInterval <= Scalar.DurationEpsilon) return AnimationError.InvalidFrame;
            for (var i = 0; i < currentNeutral.Length; i++)
                if (!RigidMath.UnitPose(previousNeutral[i]) || !RigidMath.UnitPose(currentNeutral[i])) return AnimationError.InvalidFrame;
            for (var i = 0; i < currentNeutral.Length; i++)
            {
                if (!PoseVelocity.TryBetween(previousNeutral[i], currentNeutral[i], simulationInterval, out var velocity)) return AnimationError.InvalidFrame;
                output[i] = velocity;
            }
            return AnimationError.None;
        }

        internal static AnimationError Validate(in ShapeGeometry geometry, in RetargetBinding binding, ReadOnlySpan<RetargetRow> rows,
            ReadOnlySpan<Float3> offsets, ReadOnlySpan<float> motionScales, ReadOnlySpan<Pose> input, Span<RigidPose> models, Span<Pose> output)
        {
            var count = rows.Length;
            var error = ValidateGeometry(geometry, binding, rows, offsets, motionScales);
            if (error != AnimationError.None) return error;
            if (input.Length != count || models.Length < count || output.Length < count) return AnimationError.CapacityExceeded;
            if (input.Overlaps(output)) return AnimationError.AliasedBuffers;
            for (var i = 0; i < count; i++) if (!RigidMath.UnitPose(input[i])) return AnimationError.InvalidFrame;
            return AnimationError.None;
        }

        internal static AnimationError ValidateGeometry(in ShapeGeometry geometry, in RetargetBinding binding, ReadOnlySpan<RetargetRow> rows,
            ReadOnlySpan<Float3> offsets, ReadOnlySpan<float> motionScales)
        {
            var count = rows.Length;
            if (count == 0 || count > SampleLocator.MaxBones || offsets.Length != count || motionScales.Length != count
                || geometry.Definition.IsEmpty || geometry.Retarget.Target.IsEmpty || geometry.Retarget.Target != binding.Target || binding.Family.IsEmpty
                || !geometry.Retarget.HipReference.IsFinite || !Scalar.IsFinite(geometry.Retarget.HeightRatio) || geometry.Retarget.HeightRatio <= 0f
                || binding.TargetHips < -1 || binding.TargetHips >= count || binding.TargetHips == 0 || binding.SourceHips < -1 || binding.SourceHips == 0
                || (binding.SourceHips == -1) != (binding.TargetHips == -1))
                return AnimationError.InvalidTable;
            for (var i = 0; i < count; i++)
                if (rows[i].Parent >= i || rows[i].Parent < (i == 0 ? -1 : 0) || !offsets[i].IsFinite
                    || !Scalar.IsFinite(motionScales[i]) || motionScales[i] <= 0f)
                    return AnimationError.InvalidTable;
            if (binding.TargetHips >= 0 && motionScales[binding.TargetHips] != geometry.Retarget.HeightRatio) return AnimationError.InvalidTable;
            return AnimationError.None;
        }
    }
}
