using System;
using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Animation
{
    public enum RetargetRowMode : byte { Static, ModelMapped, FastLocal }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct RetargetRow
    {
        public readonly int Parent;
        public readonly int Source;
        public readonly RetargetRowMode Mode;
        public readonly Pose Calibration;
        public readonly Quaternion Correction;
        public readonly Quaternion PreRotation;

        public RetargetRow(int parent, int source, RetargetRowMode mode, Pose calibration, Quaternion correction, Quaternion preRotation)
        { Parent = parent; Source = source; Mode = mode; Calibration = calibration; Correction = correction; PreRotation = preRotation; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct RetargetGeometry
    {
        public readonly AssetId Target;
        public readonly Float3 HipReference;
        public readonly float HeightRatio;

        public RetargetGeometry(AssetId target, Float3 hipReference, float heightRatio)
        { Target = target; HipReference = hipReference; HeightRatio = heightRatio; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct RetargetBinding
    {
        public readonly AssetId Family;
        public readonly AssetId Target;
        public readonly int SourceHips;
        public readonly int TargetHips;
        public readonly float HeightRatio;
        public readonly Float3 SourceHipReference;
        public readonly Float3 TargetHipReference;

        public RetargetBinding(AssetId family, AssetId target, int sourceHips, int targetHips, float heightRatio, Float3 sourceHipReference, Float3 targetHipReference)
        { Family = family; Target = target; SourceHips = sourceHips; TargetHips = targetHips; HeightRatio = heightRatio; SourceHipReference = sourceHipReference; TargetHipReference = targetHipReference; }
    }

    public static class RetargetTable
    {
        public static AnimationError Compile(AssetId family, AssetId target, ReadOnlySpan<int> canonicalParents, ReadOnlySpan<Pose> canonicalReference,
            ReadOnlySpan<int> targetParents, ReadOnlySpan<Pose> targetCalibration, ReadOnlySpan<int> sourceMap,
            int sourceHips, int targetHips, float heightRatio, Span<RigidPose> modelScratch, Span<RetargetRow> rows, out RetargetBinding binding)
        {
            binding = default;
            var sourceCount = canonicalParents.Length;
            var count = targetParents.Length;
            if (family.IsEmpty || target.IsEmpty || sourceCount == 0 || sourceCount > SampleLocator.MaxBones || count == 0 || count > SampleLocator.MaxBones
                || canonicalReference.Length != sourceCount || targetCalibration.Length != count || sourceMap.Length != count
                || rows.Length < count || modelScratch.Length < sourceCount + count || !Scalar.IsFinite(heightRatio) || heightRatio <= 0f
                || canonicalParents[0] != -1 || targetParents[0] != -1 || sourceMap[0] != 0
                || !Root(canonicalReference[0]) || !Root(targetCalibration[0])
                || (sourceHips == -1 ? targetHips != -1 : sourceHips <= 0 || sourceHips >= sourceCount || targetHips <= 0 || targetHips >= count))
                return AnimationError.InvalidTable;
            var sourceModels = modelScratch.Slice(0, sourceCount);
            var targetModels = modelScratch.Slice(sourceCount, count);
            if (RigidMath.Model(canonicalParents, canonicalReference, sourceModels) != AnimationError.None
                || RigidMath.Model(targetParents, targetCalibration, targetModels) != AnimationError.None)
                return AnimationError.InvalidTable;
            for (var i = 1; i < sourceCount; i++)
                if (canonicalParents[i] < 0)
                    return AnimationError.InvalidTable;
            for (var i = 1; i < count; i++)
            {
                var source = sourceMap[i];
                if (targetParents[i] < 0 || source < -1 || source == 0 || source >= sourceCount)
                    return AnimationError.InvalidTable;
                if (source < 0)
                    continue;
                for (var previous = 0; previous < i; previous++)
                    if (sourceMap[previous] == source)
                        return AnimationError.InvalidTable;
                var ancestor = targetParents[i];
                while (ancestor > 0 && sourceMap[ancestor] < 0)
                    ancestor = targetParents[ancestor];
                var required = sourceMap[ancestor];
                var parent = canonicalParents[source];
                while (parent > 0 && parent != required)
                    parent = canonicalParents[parent];
                if (parent != required)
                    return AnimationError.InvalidTable;
            }
            if (sourceHips >= 0 && sourceMap[targetHips] != sourceHips)
                return AnimationError.InvalidTable;
            for (var i = 0; i < count; i++)
            {
                var source = sourceMap[i];
                var parent = targetParents[i];
                var correction = source < 0 ? Quaternion.Identity : Quaternion.Normalize(Quaternion.Multiply(
                    Quaternion.InverseUnit(sourceModels[source].Rotation), targetModels[i].Rotation));
                var mode = source < 0 ? RetargetRowMode.Static : RetargetRowMode.ModelMapped;
                var pre = Quaternion.Identity;
                if (i > 0 && source >= 0 && sourceMap[parent] == canonicalParents[source])
                {
                    mode = RetargetRowMode.FastLocal;
                    pre = Quaternion.Normalize(Quaternion.Multiply(Quaternion.InverseUnit(targetModels[parent].Rotation), sourceModels[canonicalParents[source]].Rotation));
                }
                rows[i] = new RetargetRow(parent, source, mode, targetCalibration[i], correction, pre);
            }
            binding = new RetargetBinding(family, target, sourceHips, targetHips, heightRatio,
                sourceHips < 0 ? Float3.Zero : sourceModels[sourceHips].Position,
                targetHips < 0 ? Float3.Zero : targetModels[targetHips].Position);
            return AnimationError.None;
        }

        public static AnimationError Apply(AssetId clipFamily, in RetargetBinding binding, ReadOnlySpan<int> canonicalParents, ReadOnlySpan<Pose> canonicalLocal,
            ReadOnlySpan<RetargetRow> rows, Span<RigidPose> canonicalModel, Span<RigidPose> targetModel, Span<Pose> targetLocal) =>
            Evaluate(clipFamily, binding, canonicalParents, canonicalLocal, rows, ReadOnlySpan<Float3>.Empty, canonicalModel, targetModel, targetLocal, false);

        public static AnimationError ApplyReference(AssetId clipFamily, in RetargetBinding binding, ReadOnlySpan<int> canonicalParents, ReadOnlySpan<Pose> canonicalLocal,
            ReadOnlySpan<RetargetRow> rows, Span<RigidPose> canonicalModel, Span<RigidPose> targetModel, Span<Pose> targetLocal) =>
            Evaluate(clipFamily, binding, canonicalParents, canonicalLocal, rows, ReadOnlySpan<Float3>.Empty, canonicalModel, targetModel, targetLocal, true);

        public static AnimationError ApplyGeometry(AssetId clipFamily, in RetargetBinding binding, in RetargetGeometry geometry,
            ReadOnlySpan<int> canonicalParents, ReadOnlySpan<Pose> canonicalLocal, ReadOnlySpan<RetargetRow> rows, ReadOnlySpan<Float3> localOffsets,
            Span<RigidPose> canonicalModel, Span<RigidPose> targetModel, Span<Pose> targetLocal) =>
            WithGeometry(clipFamily, binding, geometry, canonicalParents, canonicalLocal, rows, localOffsets, canonicalModel, targetModel, targetLocal, false);

        public static AnimationError ApplyReferenceGeometry(AssetId clipFamily, in RetargetBinding binding, in RetargetGeometry geometry,
            ReadOnlySpan<int> canonicalParents, ReadOnlySpan<Pose> canonicalLocal, ReadOnlySpan<RetargetRow> rows, ReadOnlySpan<Float3> localOffsets,
            Span<RigidPose> canonicalModel, Span<RigidPose> targetModel, Span<Pose> targetLocal) =>
            WithGeometry(clipFamily, binding, geometry, canonicalParents, canonicalLocal, rows, localOffsets, canonicalModel, targetModel, targetLocal, true);

        static AnimationError WithGeometry(AssetId clipFamily, in RetargetBinding binding, in RetargetGeometry geometry,
            ReadOnlySpan<int> canonicalParents, ReadOnlySpan<Pose> canonicalLocal, ReadOnlySpan<RetargetRow> rows, ReadOnlySpan<Float3> localOffsets,
            Span<RigidPose> canonicalModel, Span<RigidPose> targetModel, Span<Pose> targetLocal, bool reference)
        {
            if (geometry.Target != binding.Target || geometry.Target.IsEmpty || localOffsets.Length == 0 || localOffsets.Length != rows.Length
                || !geometry.HipReference.IsFinite || !Scalar.IsFinite(geometry.HeightRatio) || geometry.HeightRatio <= 0f
                || !Scalar.IsFinite(binding.HeightRatio) || binding.HeightRatio <= 0f)
                return AnimationError.InvalidTable;
            for (var i = 0; i < localOffsets.Length; i++) if (!localOffsets[i].IsFinite) return AnimationError.InvalidTable;
            if (localOffsets[0].LengthSquared > 1e-12f) return AnimationError.InvalidTable;
            var shaped = new RetargetBinding(binding.Family, binding.Target, binding.SourceHips, binding.TargetHips, geometry.HeightRatio, binding.SourceHipReference, geometry.HipReference);
            return Evaluate(clipFamily, shaped, canonicalParents, canonicalLocal, rows, localOffsets, canonicalModel, targetModel, targetLocal, reference);
        }

        static AnimationError Evaluate(AssetId clipFamily, in RetargetBinding binding, ReadOnlySpan<int> canonicalParents, ReadOnlySpan<Pose> canonicalLocal,
            ReadOnlySpan<RetargetRow> rows, ReadOnlySpan<Float3> localOffsets, Span<RigidPose> canonicalModel, Span<RigidPose> targetModel, Span<Pose> targetLocal, bool reference)
        {
            if (clipFamily != binding.Family || clipFamily.IsEmpty)
                return AnimationError.FamilyMismatch;
            if (rows.Length == 0 || rows.Length > SampleLocator.MaxBones || targetLocal.Length < rows.Length || targetModel.Length < rows.Length
                || canonicalParents.Length == 0 || canonicalParents.Length > SampleLocator.MaxBones || canonicalParents.Length != canonicalLocal.Length
                || canonicalModel.Length < canonicalLocal.Length || !Scalar.IsFinite(binding.HeightRatio) || binding.HeightRatio <= 0f
                || !binding.SourceHipReference.IsFinite || !binding.TargetHipReference.IsFinite || binding.SourceHips < -1
                || (binding.SourceHips == -1 ? binding.TargetHips != -1 : binding.SourceHips >= canonicalLocal.Length || binding.TargetHips <= 0 || binding.TargetHips >= rows.Length))
                return AnimationError.InvalidTable;
            if (canonicalLocal.Overlaps(targetLocal) || canonicalModel.Overlaps(targetModel))
                return AnimationError.AliasedBuffers;
            for (var i = 0; i < rows.Length; i++)
            {
                ref readonly var row = ref rows[i];
                if (row.Parent >= i || row.Parent < (i == 0 ? -1 : 0) || row.Source < -1 || row.Source >= canonicalLocal.Length
                    || !RigidMath.UnitPose(row.Calibration) || !RigidMath.UnitRotation(row.Correction) || !RigidMath.UnitRotation(row.PreRotation)
                    || row.Mode > RetargetRowMode.FastLocal || (row.Mode == RetargetRowMode.Static) != (row.Source == -1)
                    || (i == 0 && (row.Parent != -1 || row.Source != 0 || !Root(row.Calibration)))
                    || (row.Mode == RetargetRowMode.FastLocal && (row.Parent < 0 || canonicalParents[row.Source] != rows[row.Parent].Source)))
                    return AnimationError.InvalidTable;
            }
            if (binding.TargetHips >= 0 && rows[binding.TargetHips].Source != binding.SourceHips)
                return AnimationError.InvalidTable;
            var error = RigidMath.Model(canonicalParents, canonicalLocal, canonicalModel);
            if (error != AnimationError.None || canonicalParents[0] != -1 || !Root(canonicalLocal[0]))
                return AnimationError.InvalidTable;
            for (var i = 0; i < rows.Length; i++)
            {
                ref readonly var row = ref rows[i];
                var parent = row.Parent < 0 ? RigidPose.Identity : targetModel[row.Parent];
                var local = row.Calibration;
                if (localOffsets.Length != 0) local.Position = localOffsets[i];
                if (row.Source >= 0)
                {
                    local.Rotation = !reference && row.Mode == RetargetRowMode.FastLocal
                        ? Quaternion.Normalize(Quaternion.Multiply(Quaternion.Multiply(row.PreRotation, canonicalLocal[row.Source].Rotation), row.Correction))
                        : Quaternion.Normalize(Quaternion.Multiply(Quaternion.InverseUnit(parent.Rotation), Quaternion.Multiply(canonicalModel[row.Source].Rotation, row.Correction)));
                }
                if (i == binding.TargetHips)
                {
                    var desired = binding.TargetHipReference + (canonicalModel[binding.SourceHips].Position - binding.SourceHipReference) * binding.HeightRatio;
                    local.Position = Quaternion.Rotate(Quaternion.InverseUnit(parent.Rotation), desired - parent.Position);
                }
                if (!RigidMath.UnitPose(local) || !RigidMath.TryCompose(parent, new RigidPose(local.Position, local.Rotation), out var model))
                    return AnimationError.InvalidTable;
                targetLocal[i] = local;
                targetModel[i] = model;
            }
            return AnimationError.None;
        }

        static bool Root(in Pose pose) => RigidMath.UnitPose(pose) && pose.Position.LengthSquared <= 1e-12f && Math.Abs(pose.Rotation.W) >= 0.999999f;
    }
}
