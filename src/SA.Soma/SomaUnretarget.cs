using System;
using SA.Animation;
using SA.Motion.Numerics;

namespace SA.Soma
{
    public static class SomaUnretarget
    {
        public static AnimationError ToSoma(SomaRetarget retarget, ReadOnlySpan<Pose> saLocal, Span<Pose> somaLocal) =>
            ToSoma(retarget, saLocal, RigidPose.Identity, somaLocal);

        public static AnimationError ToSoma(SomaRetarget retarget, ReadOnlySpan<Pose> saLocal, in RigidPose saRoot, Span<Pose> somaLocal)
        {
            const int bones = CanonicalHumanoid.BoneCount;
            const int joints = SomaRig.ModelJointCount;
            if (retarget == null || saLocal.Length != bones || somaLocal.Length != joints || !saRoot.IsValid || !RigidMath.UnitRotation(saRoot.Rotation))
                return AnimationError.InvalidFrame;
            var binding = retarget.Binding;
            var rows = retarget.Rows;
            var parents = retarget.TargetParents;
            if (rows == null || parents == null || rows.Length != bones || parents.Length != bones || !Scalar.IsFinite(binding.HeightRatio) || binding.HeightRatio <= 0f
                || binding.TargetHips != CanonicalHumanoid.Hips || binding.SourceHips != SomaRig.Source(SomaRig.Hips)
                || !binding.SourceHipReference.IsFinite || !binding.TargetHipReference.IsFinite)
                return AnimationError.InvalidTable;
            for (var bone = 0; bone < bones; bone++)
                if (!saLocal[bone].Position.IsFinite || !RigidMath.UnitRotation(saLocal[bone].Rotation))
                    return AnimationError.InvalidFrame;
            Span<RigidPose> saModel = stackalloc RigidPose[bones];
            for (var bone = 0; bone < bones; bone++)
            {
                var parent = parents[bone];
                if (parent >= bone || (parent < 0 && bone != 0)) return AnimationError.InvalidTable;
                var local = new RigidPose(saLocal[bone].Position, Quaternion.Normalize(saLocal[bone].Rotation));
                if (!RigidMath.TryCompose(parent < 0 ? saRoot : saModel[parent], local, out var model)) return AnimationError.InvalidFrame;
                saModel[bone] = model;
            }
            Span<Quaternion> world = stackalloc Quaternion[joints];
            Span<bool> known = stackalloc bool[joints];
            for (var bone = 1; bone < bones; bone++)
            {
                var source = rows[bone].Source;
                if (source <= 0) continue;
                var joint = SomaRig.ModelOfSource(source);
                if (joint < 0 || !RigidMath.UnitRotation(rows[bone].Correction)) return AnimationError.InvalidTable;
                world[joint] = SomaMirror.Rotation(Quaternion.Normalize(Quaternion.Multiply(Quaternion.Normalize(saModel[bone].Rotation), Quaternion.InverseUnit(Quaternion.Normalize(rows[bone].Correction)))));
                known[joint] = true;
            }
            if (!known[SomaRig.Hips]) return AnimationError.InvalidTable;
            var hips = SomaMirror.Position(binding.SourceHipReference + (saModel[binding.TargetHips].Position - binding.TargetHipReference) / binding.HeightRatio);
            if (!hips.IsFinite) return AnimationError.InvalidFrame;
            for (var joint = 0; joint < joints; joint++)
            {
                var parent = SomaRig.Parent(joint);
                if (joint == SomaRig.Hips)
                    somaLocal[joint] = new Pose { Position = hips, Rotation = world[joint], Scale = Float3.One };
                else if (SomaRig.IsMarker(joint) || parent < 0)
                    somaLocal[joint] = new Pose { Position = SomaRig.RestTranslation(joint), Rotation = Quaternion.Identity, Scale = Float3.One };
                else if (!known[joint])
                {
                    var bind = SomaRig.BindLocal(joint);
                    somaLocal[joint] = bind;
                    world[joint] = Quaternion.Normalize(Quaternion.Multiply(world[parent], bind.Rotation));
                    known[joint] = true;
                }
                else
                    somaLocal[joint] = new Pose
                    {
                        Position = SomaRig.RestTranslation(joint),
                        Rotation = Quaternion.Normalize(Quaternion.Multiply(Quaternion.InverseUnit(world[parent]), world[joint])),
                        Scale = Float3.One
                    };
            }
            return AnimationError.None;
        }

        public static AnimationError ToSkeleton(ReadOnlySpan<Pose> somaLocal, ReadOnlySpan<Pose> skeletonReference, Span<Pose> skeletonLocal)
        {
            if (somaLocal.Length != SomaRig.ModelJointCount || skeletonReference.Length != SomaRig.SkeletonJointCount || skeletonLocal.Length != SomaRig.SkeletonJointCount)
                return AnimationError.InvalidFrame;
            if (somaLocal.Overlaps(skeletonLocal) || skeletonReference.Overlaps(skeletonLocal)) return AnimationError.AliasedBuffers;
            for (var joint = 0; joint < somaLocal.Length; joint++)
                if (!somaLocal[joint].Position.IsFinite || !RigidMath.UnitRotation(somaLocal[joint].Rotation)) return AnimationError.InvalidFrame;
            for (var joint = 0; joint < skeletonReference.Length; joint++)
                if (!skeletonReference[joint].Position.IsFinite || !RigidMath.UnitRotation(skeletonReference[joint].Rotation)) return AnimationError.InvalidFrame;
            for (var joint = 0; joint < skeletonReference.Length; joint++)
                skeletonLocal[joint] = new Pose { Position = skeletonReference[joint].Position, Rotation = Quaternion.Normalize(skeletonReference[joint].Rotation), Scale = Float3.One };
            for (var joint = 0; joint < somaLocal.Length; joint++)
            {
                if (SomaRig.IsMarker(joint)) continue;
                var index = SomaRig.SkeletonIndex(joint);
                skeletonLocal[index] = new Pose
                {
                    Position = joint == SomaRig.Hips ? somaLocal[joint].Position : skeletonReference[index].Position,
                    Rotation = Quaternion.Normalize(somaLocal[joint].Rotation),
                    Scale = Float3.One
                };
            }
            return AnimationError.None;
        }

        public static AnimationError Write(ReadOnlySpan<Pose> joints, Span<float> floats)
        {
            if (joints.Length == 0 || floats.Length != (long)joints.Length * SomaFrame.FloatsPerJoint) return AnimationError.InvalidFrame;
            for (var joint = 0; joint < joints.Length; joint++)
            {
                var at = joint * SomaFrame.FloatsPerJoint;
                var pose = joints[joint];
                floats[at] = pose.Position.X;
                floats[at + 1] = pose.Position.Y;
                floats[at + 2] = pose.Position.Z;
                floats[at + 3] = pose.Rotation.X;
                floats[at + 4] = pose.Rotation.Y;
                floats[at + 5] = pose.Rotation.Z;
                floats[at + 6] = pose.Rotation.W;
            }
            return AnimationError.None;
        }
    }
}
