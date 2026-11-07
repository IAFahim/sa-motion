using System;
using SA.Animation;
using SA.Animation.Content;
using SA.Motion.Numerics;

namespace SA.Soma
{
    public sealed class SomaRetarget
    {
        public AssetId Family { get; }
        public RetargetBinding Binding { get; }
        public RetargetRow[] Rows { get; }
        public int[] SourceParents { get; }
        public Pose[] SourceReference { get; }
        public int[] TargetParents { get; }
        public Pose[] TargetReference { get; }
        public Quaternion[] APose { get; }
        public float HeightRatio => Binding.HeightRatio;

        SomaRetarget(AssetId family, RetargetBinding binding, RetargetRow[] rows, int[] sourceParents, Pose[] sourceReference, int[] targetParents, Pose[] targetReference, Quaternion[] aPose)
        { Family = family; Binding = binding; Rows = rows; SourceParents = sourceParents; SourceReference = sourceReference; TargetParents = targetParents; TargetReference = targetReference; APose = aPose; }

        public static AnimationError Create(AssetId family, out SomaRetarget? retarget)
        {
            retarget = null;
            if (family.IsEmpty) return AnimationError.InvalidTable;
            const int bones = CanonicalHumanoid.BoneCount;
            var targetParents = new int[bones];
            var targetReference = new Pose[bones];
            var error = CanonicalHumanoid.WriteReference(targetParents, targetReference);
            if (error != AnimationError.None) return error;
            var targetModel = new RigidPose[bones];
            error = RigidMath.Model(targetParents, targetReference, targetModel);
            if (error != AnimationError.None) return error;
            var aPose = new Quaternion[bones];
            if (!SomaCalibration.APose(targetParents, targetModel, aPose)) return AnimationError.InvalidTable;
            var calibration = new Pose[bones];
            var sourceMap = new int[bones];
            for (var bone = 0; bone < bones; bone++)
            {
                var parent = targetParents[bone];
                var rotation = parent < 0 ? Quaternion.Identity : Quaternion.Normalize(Quaternion.Multiply(Quaternion.InverseUnit(aPose[parent]), aPose[bone]));
                calibration[bone] = new Pose { Position = targetReference[bone].Position, Rotation = rotation, Scale = Float3.One };
                var model = SomaRig.TargetModel(bone);
                sourceMap[bone] = model == -2 ? 0 : model < 0 ? -1 : SomaRig.Source(model);
            }
            var aModel = new RigidPose[bones];
            error = RigidMath.Model(targetParents, calibration, aModel);
            if (error != AnimationError.None) return error;
            var lift = 0.5f * (aModel[50].Position.Y + aModel[54].Position.Y) - 0.5f * (targetModel[50].Position.Y + targetModel[54].Position.Y);
            calibration[CanonicalHumanoid.Hips].Position = new Float3(0f, targetReference[CanonicalHumanoid.Hips].Position.Y - lift, 0f);
            var sourceParents = new int[SomaRig.SourceCount];
            SomaRig.SourceParents(sourceParents);
            var sourceReference = SomaCalibration.SourceBind();
            var ratio = SomaCalibration.HeightRatio(targetModel);
            var rows = new RetargetRow[bones];
            var scratch = new RigidPose[SomaRig.SourceCount + bones];
            error = RetargetTable.Compile(family, family, sourceParents, sourceReference, targetParents, calibration, sourceMap,
                SomaRig.Source(SomaRig.Hips), CanonicalHumanoid.Hips, ratio, scratch, rows, out var binding);
            if (error != AnimationError.None) return error;
            retarget = new SomaRetarget(family, binding, rows, sourceParents, sourceReference, targetParents, targetReference, aPose);
            return AnimationError.None;
        }
    }

    public static class SomaCalibration
    {
        static readonly Float3 Up = new Float3(0f, 1f, 0f);
        static readonly Float3 Right = new Float3(1f, 0f, 0f);
        static readonly Float3 Forward = new Float3(0f, 0f, 1f);

        public static Float3 Bind(int joint) => SomaMirror.Position(SomaRig.BindPosition(joint));

        public static float HeightRatio(ReadOnlySpan<RigidPose> targetModel)
        {
            var target = (targetModel[49].Position - targetModel[48].Position).Length + (targetModel[50].Position - targetModel[49].Position).Length
                + (targetModel[53].Position - targetModel[52].Position).Length + (targetModel[54].Position - targetModel[53].Position).Length;
            var source = SomaRig.RestTranslation(23).Length + SomaRig.RestTranslation(SomaRig.LeftFoot).Length
                + SomaRig.RestTranslation(27).Length + SomaRig.RestTranslation(SomaRig.RightFoot).Length;
            return source > 1e-3f ? target / source : 1f;
        }

        public static Pose[] SourceBind()
        {
            var reference = new Pose[SomaRig.SourceCount];
            reference[0] = Pose.Identity;
            for (var joint = 0; joint < SomaRig.ModelJointCount; joint++)
            {
                var source = SomaRig.Source(joint);
                if (source < 0) continue;
                var local = SomaMirror.Local(SomaRig.BindLocal(joint));
                if (joint == SomaRig.Hips) local.Position = new Float3(0f, local.Position.Y, 0f);
                reference[source] = local;
            }
            return reference;
        }

        public static bool APose(ReadOnlySpan<int> parents, ReadOnlySpan<RigidPose> model, Span<Quaternion> aPose)
        {
            if (parents.Length != CanonicalHumanoid.BoneCount || model.Length != parents.Length || aPose.Length != parents.Length) return false;
            for (var bone = 0; bone < parents.Length; bone++)
            {
                var parent = parents[bone];
                aPose[bone] = parent < 0 ? Quaternion.Identity : aPose[parent];
                if (!Aim(bone, model, out var t, out var ts, out var a, out var aSide)) continue;
                if (!CalibrationFrame.TryRotation(t, ts, a, aSide, out var rotation)) return false;
                aPose[bone] = rotation;
            }
            return true;
        }

        static bool Aim(int bone, ReadOnlySpan<RigidPose> m, out Float3 t, out Float3 ts, out Float3 a, out Float3 aSide)
        {
            var hipsSide = Bind(SomaRig.RightLeg) - Bind(SomaRig.LeftLeg);
            var shoulderSide = Bind(SomaRig.RightShoulder) - Bind(SomaRig.LeftShoulder);
            t = ts = a = aSide = Float3.Zero;
            switch (bone)
            {
                case 1: return Set(m[2].Position - m[1].Position, m[52].Position - m[48].Position, Bind(1) - Bind(0), hipsSide, out t, out ts, out a, out aSide);
                case 2: return Set(m[3].Position - m[2].Position, Right, Bind(2) - Bind(1), hipsSide, out t, out ts, out a, out aSide);
                case 3: return Set(m[4].Position - m[3].Position, Right, Bind(3) - Bind(2), hipsSide, out t, out ts, out a, out aSide);
                case 4: return Set(m[5].Position - m[4].Position, m[29].Position - m[10].Position, Bind(SomaRig.Neck1) - Bind(3), shoulderSide, out t, out ts, out a, out aSide);
                case 5: return Set(m[6].Position - m[5].Position, Right, Bind(SomaRig.Head) - Bind(SomaRig.Neck2), shoulderSide, out t, out ts, out a, out aSide);
                case 6:
                    return Set((m[8].Position + m[9].Position) * 0.5f - m[6].Position, m[9].Position - m[8].Position,
                        (Bind(SomaRig.LeftEye) + Bind(SomaRig.RightEye)) * 0.5f - Bind(SomaRig.Head), Bind(SomaRig.RightEye) - Bind(SomaRig.LeftEye), out t, out ts, out a, out aSide);
                case 10: return Set(m[11].Position - m[10].Position, Up, Bind(SomaRig.LeftArm) - Bind(SomaRig.LeftShoulder), Up, out t, out ts, out a, out aSide);
                case 29: return Set(m[30].Position - m[29].Position, Up, Bind(SomaRig.RightArm) - Bind(SomaRig.RightShoulder), Up, out t, out ts, out a, out aSide);
                case 11: return UpperArm(m, 11, 12, SomaRig.LeftArm, out t, out ts, out a, out aSide);
                case 30: return UpperArm(m, 30, 31, SomaRig.RightArm, out t, out ts, out a, out aSide);
                case 12: return Set(m[13].Position - m[12].Position, Forward, Bind(SomaRig.LeftHand) - Bind(SomaRig.LeftForeArm), Bind(SomaRig.LeftThumbEnd) - Bind(SomaRig.LeftHand), out t, out ts, out a, out aSide);
                case 31: return Set(m[32].Position - m[31].Position, Forward, Bind(SomaRig.RightHand) - Bind(SomaRig.RightForeArm), Bind(SomaRig.RightThumbEnd) - Bind(SomaRig.RightHand), out t, out ts, out a, out aSide);
                case 13: return Set(m[20].Position - m[13].Position, Forward, Bind(SomaRig.LeftMiddleEnd) - Bind(SomaRig.LeftHand), Bind(SomaRig.LeftThumbEnd) - Bind(SomaRig.LeftHand), out t, out ts, out a, out aSide);
                case 32: return Set(m[39].Position - m[32].Position, Forward, Bind(SomaRig.RightMiddleEnd) - Bind(SomaRig.RightHand), Bind(SomaRig.RightThumbEnd) - Bind(SomaRig.RightHand), out t, out ts, out a, out aSide);
                case 48: case 49: case 52: case 53:
                    return Set(m[bone + 1].Position - m[bone].Position, Forward, Bind(SomaRig.TargetModel(bone + 1)) - Bind(SomaRig.TargetModel(bone)), Forward, out t, out ts, out a, out aSide);
                case 50: case 54:
                    return Set(m[bone + 1].Position - m[bone].Position, Up, Bind(SomaRig.TargetModel(bone + 1)) - Bind(SomaRig.TargetModel(bone)), Up, out t, out ts, out a, out aSide);
                default: return false;
            }
        }

        static bool UpperArm(ReadOnlySpan<RigidPose> m, int upper, int lower, int joint, out Float3 t, out Float3 ts, out Float3 a, out Float3 aSide)
        {
            var tDirection = m[lower].Position - m[upper].Position;
            var aUpper = Bind(joint + 1) - Bind(joint);
            var aLower = Bind(joint + 2) - Bind(joint + 1);
            return Set(tDirection, Float3.Cross(tDirection, Forward), aUpper, Float3.Cross(aUpper, aLower), out t, out ts, out a, out aSide);
        }

        static bool Set(Float3 tPrimary, Float3 tSide, Float3 aPrimary, Float3 aSecondary, out Float3 t, out Float3 ts, out Float3 a, out Float3 aSide)
        { t = tPrimary; ts = tSide; a = aPrimary; aSide = aSecondary; return true; }
    }
}
