using System;
using SA.Animation;
using SA.Motion.Numerics;

namespace SA.Shape
{
    public static class ShapeRig
    {
        public const int MaxEffects = 16384;

        public static AnimationError Evaluate(in ShapeDefinition definition, in RetargetBinding binding, ReadOnlySpan<RetargetRow> rows,
            ReadOnlySpan<float> values, ulong revision, ShapeWorkspace workspace, out ShapeGeometry geometry)
        {
            geometry = default;
            var count = rows.Length;
            var profile = definition.Profile;
            var error = ShapeParameters.Validate(definition.Channels);
            if (error != AnimationError.None) return error;
            if (count <= 0 || count > SampleLocator.MaxBones || values.Length != definition.Channels.Length || profile.Definition.IsEmpty || profile.Target.IsEmpty
                || profile.Target != binding.Target || binding.Family.IsEmpty || definition.Offsets.Length > MaxEffects || definition.Skin.Length > MaxEffects
                || definition.SkinFrames.Length != 0 && definition.SkinFrames.Length != count || profile.Grounding > ShapeGrounding.KeepBase
                || !ScaleRange(profile.MinimumLength, profile.MaximumLength) || !ScaleRange(profile.MinimumSkinScale, profile.MaximumSkinScale)
                || !Scalar.IsFinite(binding.HeightRatio) || binding.HeightRatio <= 0f || binding.SourceHips < -1 || binding.SourceHips == 0
                || (binding.SourceHips == -1 ? binding.TargetHips != -1 || profile.Grounding != ShapeGrounding.Free : binding.TargetHips <= 0 || binding.TargetHips >= count))
                return AnimationError.InvalidTable;
            if (workspace.Offsets.Length < count || workspace.LengthScales.Length < count || workspace.MotionScales.Length < count
                || workspace.SkinScales.Length < count || workspace.Deformations.Length < count || workspace.Models.Length < count)
                return AnimationError.CapacityExceeded;
            if (workspace.Offsets.Overlaps(workspace.SkinScales) || workspace.LengthScales.Overlaps(workspace.MotionScales)
                || values.Overlaps(workspace.LengthScales) || values.Overlaps(workspace.MotionScales))
                return AnimationError.AliasedBuffers;
            for (var i = 0; i < values.Length; i++)
                if (!Scalar.IsFinite(values[i]) || !Scalar.IsFinite(ShapeParameters.Delta(definition.Channels[i], values[i])))
                    return AnimationError.InvalidFrame;
            for (var i = 0; i < definition.SkinFrames.Length; i++)
                if (!RigidMath.UnitRotation(definition.SkinFrames[i].Basis) || !definition.SkinFrames[i].Pivot.IsFinite)
                    return AnimationError.InvalidTable;
            for (var i = 0; i < definition.Offsets.Length; i++)
            {
                var effect = definition.Offsets[i];
                if (effect.Channel < 0 || effect.Channel >= values.Length || effect.Bone <= 0 || effect.Bone >= count || !effect.Delta.IsFinite || !Scalar.IsFinite(effect.LogLength))
                    return AnimationError.InvalidTable;
            }
            for (var i = 0; i < definition.Skin.Length; i++)
            {
                var effect = definition.Skin[i];
                if (effect.Channel < 0 || effect.Channel >= values.Length || effect.Bone <= 0 || effect.Bone >= count || !effect.LogScale.IsFinite)
                    return AnimationError.InvalidTable;
            }
            error = ShapeMath.Model(rows, ReadOnlySpan<Float3>.Empty, workspace.Models);
            if (error != AnimationError.None) return error;
            if (workspace.Models[0].Position.LengthSquared > 1e-12f || Math.Abs(workspace.Models[0].Rotation.W) < .999999f)
                return AnimationError.InvalidTable;
            var hips = binding.TargetHips;
            var baseGround = 0f;
            var baseHeight = 0f;
            if (hips >= 0)
            {
                if (rows[hips].Source != binding.SourceHips || !Marker(profile.SupportA, rows, hips) || !Marker(profile.SupportB, rows, hips)
                    || (workspace.Models[hips].Position - binding.TargetHipReference).Length > 1e-4f)
                    return AnimationError.InvalidTable;
                baseGround = Math.Min(Point(workspace.Models, ReadOnlySpan<Affine>.Empty, profile.SupportA).Y, Point(workspace.Models, ReadOnlySpan<Affine>.Empty, profile.SupportB).Y);
                baseHeight = workspace.Models[hips].Position.Y - baseGround;
                if (!Scalar.IsFinite(baseGround) || !Scalar.IsFinite(baseHeight) || baseHeight <= 1e-5f) return AnimationError.InvalidTable;
            }
            for (var i = 0; i < count; i++)
            {
                workspace.Offsets[i] = Float3.Zero;
                workspace.LengthScales[i] = 0f;
                workspace.SkinScales[i] = Float3.Zero;
            }
            for (var i = 0; i < definition.Offsets.Length; i++)
            {
                var effect = definition.Offsets[i];
                var amount = ShapeParameters.Delta(definition.Channels[effect.Channel], values[effect.Channel]);
                workspace.Offsets[effect.Bone] += effect.Delta * amount;
                workspace.LengthScales[effect.Bone] += effect.LogLength * amount;
            }
            for (var i = 0; i < definition.Skin.Length; i++)
            {
                var effect = definition.Skin[i];
                workspace.SkinScales[effect.Bone] += effect.LogScale * ShapeParameters.Delta(definition.Channels[effect.Channel], values[effect.Channel]);
            }
            var isBase = true;
            for (var i = 0; i < count; i++)
            {
                var offset = workspace.Offsets[i];
                var lengthLog = workspace.LengthScales[i];
                var skinLog = workspace.SkinScales[i];
                if (!offset.IsFinite || !Scalar.IsFinite(lengthLog) || !skinLog.IsFinite) return AnimationError.InvalidFrame;
                var length = lengthLog == 0f ? 1f : (float)Math.Exp(lengthLog);
                var scale = skinLog.LengthSquared == 0f ? Float3.One : Float3.Exp(skinLog);
                if (!InRange(length, profile.MinimumLength, profile.MaximumLength) || !scale.IsPositive
                    || !InRange(scale.X, profile.MinimumSkinScale, profile.MaximumSkinScale) || !InRange(scale.Y, profile.MinimumSkinScale, profile.MaximumSkinScale)
                    || !InRange(scale.Z, profile.MinimumSkinScale, profile.MaximumSkinScale))
                    return AnimationError.InvalidFrame;
                var neutral = lengthLog == 0f && offset.X == 0f && offset.Y == 0f && offset.Z == 0f;
                var position = neutral ? rows[i].Calibration.Position : rows[i].Calibration.Position * length + offset;
                if (!position.IsFinite) return AnimationError.InvalidFrame;
                var skinFrame = definition.SkinFrames.Length == 0 ? SkinFrame.Identity : definition.SkinFrames[i];
                if (!ShapeMath.Deform(skinFrame, scale, out var deformation)) return AnimationError.InvalidFrame;
                workspace.Offsets[i] = position;
                workspace.LengthScales[i] = length;
                workspace.MotionScales[i] = length;
                workspace.SkinScales[i] = scale;
                workspace.Deformations[i] = deformation;
                isBase &= neutral && scale.X == 1f && scale.Y == 1f && scale.Z == 1f;
            }
            error = ShapeMath.Model(rows, workspace.Offsets.Slice(0, count), workspace.Models);
            if (error != AnimationError.None) return error;
            var ground = baseGround;
            var height = baseHeight;
            var shift = 0f;
            var hipReference = binding.TargetHipReference;
            var ratio = binding.HeightRatio;
            if (hips >= 0 && !isBase)
            {
                ground = Math.Min(Point(workspace.Models, workspace.Deformations, profile.SupportA).Y, Point(workspace.Models, workspace.Deformations, profile.SupportB).Y);
                if (!Scalar.IsFinite(ground)) return AnimationError.InvalidFrame;
                if (profile.Grounding == ShapeGrounding.KeepBase)
                {
                    shift = baseGround - ground;
                    var parent = rows[hips].Parent;
                    workspace.Offsets[hips] += Quaternion.Rotate(Quaternion.InverseUnit(workspace.Models[parent].Rotation), new Float3(0f, shift, 0f));
                    error = ShapeMath.Model(rows, workspace.Offsets.Slice(0, count), workspace.Models);
                    if (error != AnimationError.None) return error;
                    ground = Math.Min(Point(workspace.Models, workspace.Deformations, profile.SupportA).Y, Point(workspace.Models, workspace.Deformations, profile.SupportB).Y);
                }
                hipReference = workspace.Models[hips].Position;
                height = hipReference.Y - ground;
                if (!Scalar.IsFinite(height) || height <= 1e-5f) return AnimationError.InvalidFrame;
                ratio = height == baseHeight ? binding.HeightRatio : (float)(binding.HeightRatio * ((double)height / baseHeight));
            }
            if (!Scalar.IsFinite(ratio) || ratio <= 0f || !Scalar.IsFinite(shift)) return AnimationError.InvalidFrame;
            if (hips >= 0) workspace.MotionScales[hips] = ratio;
            geometry = new ShapeGeometry(profile.Definition, revision, new RetargetGeometry(binding.Target, hipReference, ratio), baseHeight, height, ground, shift);
            return AnimationError.None;
        }

        static bool ScaleRange(float minimum, float maximum) => Scalar.IsFinite(minimum) && Scalar.IsFinite(maximum) && minimum > 0f && minimum <= 1f && maximum >= 1f;
        static bool InRange(float value, float minimum, float maximum) => Scalar.IsFinite(value) && value >= minimum && value <= maximum;
        static bool Marker(in ShapeMarker marker, ReadOnlySpan<RetargetRow> rows, int hips) => marker.Offset.IsFinite && ShapeMath.Descendant(rows, marker.Bone, hips);
        static Float3 Point(ReadOnlySpan<RigidPose> models, ReadOnlySpan<Affine> deformations, in ShapeMarker marker)
        {
            var offset = deformations.Length == 0 ? marker.Offset : ShapeMath.Point(deformations[marker.Bone], marker.Offset);
            var model = models[marker.Bone];
            return model.Position + Quaternion.Rotate(model.Rotation, offset);
        }
    }
}
