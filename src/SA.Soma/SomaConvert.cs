using System;
using SA.Animation;
using SA.Animation.Content;
using SA.Motion.Numerics;

namespace SA.Soma
{
    public static class SomaFrame
    {
        public const int FloatsPerJoint = 7;

        public static Pose Read(ReadOnlySpan<float> poses, int frame, int joint, int jointsPerFrame)
        {
            var index = jointsPerFrame == SomaRig.SkeletonJointCount ? SomaRig.SkeletonIndex(joint) : joint;
            var at = ((long)frame * jointsPerFrame + index) * FloatsPerJoint;
            if (index < 0 || at < 0 || at + FloatsPerJoint > poses.Length) return default;
            return new Pose
            {
                Position = new Float3(poses[(int)at], poses[(int)at + 1], poses[(int)at + 2]),
                Rotation = new Quaternion(poses[(int)at + 3], poses[(int)at + 4], poses[(int)at + 5], poses[(int)at + 6]),
                Scale = Float3.One
            };
        }

        public static AnimationError ToSource(ReadOnlySpan<Pose> model, SomaSpace space, Span<Pose> source, out RigidPose heading)
        {
            heading = RigidPose.Identity;
            if (model.Length != SomaRig.ModelJointCount || source.Length != SomaRig.SourceCount) return AnimationError.InvalidFrame;
            for (var joint = 0; joint < model.Length; joint++)
                if (!model[joint].Position.IsFinite || !RigidMath.UnitRotation(model[joint].Rotation)) return AnimationError.InvalidFrame;
            var hips = SomaMirror.Local(SomaAxes.RootToModel(model[SomaRig.Hips], space));
            hips.Rotation = Quaternion.Normalize(hips.Rotation);
            var yaw = SomaAxes.Yaw(hips.Rotation, SomaMirror.Position(model[SomaRig.LeftLeg].Position), SomaMirror.Position(model[SomaRig.RightLeg].Position), 0f);
            var turn = SomaAxes.YawRotation(yaw);
            heading = new RigidPose(new Float3(hips.Position.X, 0f, hips.Position.Z), turn);
            var inverse = Quaternion.InverseUnit(turn);
            source[0] = Pose.Identity;
            for (var joint = 0; joint < model.Length; joint++)
            {
                var index = SomaRig.Source(joint);
                if (index < 0) continue;
                source[index] = joint == SomaRig.Hips
                    ? new Pose { Position = Quaternion.Rotate(inverse, hips.Position - heading.Position), Rotation = Quaternion.Normalize(Quaternion.Multiply(inverse, hips.Rotation)), Scale = Float3.One }
                    : SomaMirror.Local(model[joint]);
            }
            return AnimationError.None;
        }
    }

    public static class SomaContacts
    {
        public const int SomaChannels = 4;
        public const int SaChannels = 4;

        public static AnimationError Merge(ReadOnlySpan<float> soma, Span<float> sa)
        {
            if (soma.Length % SomaChannels != 0 || sa.Length != soma.Length / SomaChannels * SaChannels) return AnimationError.InvalidFrame;
            for (var frame = 0; frame < soma.Length / SomaChannels; frame++)
            {
                var at = frame * SomaChannels;
                sa[frame * SaChannels] = Math.Max(Clamp(soma[at]), Clamp(soma[at + 1]));
                sa[frame * SaChannels + 1] = Math.Max(Clamp(soma[at + 2]), Clamp(soma[at + 3]));
                sa[frame * SaChannels + 2] = 0f;
                sa[frame * SaChannels + 3] = 0f;
            }
            return AnimationError.None;
        }

        static float Clamp(float value) => Scalar.IsFinite(value) ? Math.Min(1f, Math.Max(0f, value)) : 0f;
    }

    public static class SomaClip
    {
        public static AnimationError Convert(SomaRetarget retarget, ReadOnlySpan<float> poses, int frames, int jointsPerFrame, SomaSpace space,
            Span<Pose> samples, Span<RigidPose> root)
        {
            const int bones = CanonicalHumanoid.BoneCount;
            if (retarget == null || frames <= 0 || (jointsPerFrame != SomaRig.ModelJointCount && jointsPerFrame != SomaRig.SkeletonJointCount)
                || poses.Length != (long)frames * jointsPerFrame * SomaFrame.FloatsPerJoint || samples.Length != (long)frames * bones || root.Length != frames)
                return AnimationError.InvalidFrame;
            var model = new Pose[SomaRig.ModelJointCount];
            var source = new Pose[SomaRig.SourceCount];
            var sourceModel = new RigidPose[SomaRig.SourceCount];
            var targetModel = new RigidPose[bones];
            var first = RigidPose.Identity;
            var firstYaw = 0f;
            for (var frame = 0; frame < frames; frame++)
            {
                for (var joint = 0; joint < model.Length; joint++) model[joint] = SomaFrame.Read(poses, frame, joint, jointsPerFrame);
                var error = SomaFrame.ToSource(model, space, source, out var heading);
                if (error != AnimationError.None) return error;
                var yaw = Yaw(heading.Rotation);
                if (frame == 0) { first = heading; firstYaw = yaw; }
                var travel = Quaternion.Rotate(SomaAxes.YawRotation(-firstYaw), heading.Position - first.Position) * retarget.HeightRatio;
                root[frame] = frame == 0 ? RigidPose.Identity : new RigidPose(new Float3(travel.X, 0f, travel.Z), SomaAxes.YawRotation(yaw - firstYaw));
                error = RetargetTable.Apply(retarget.Family, retarget.Binding, retarget.SourceParents, source, retarget.Rows, sourceModel, targetModel, samples.Slice(frame * bones, bones));
                if (error != AnimationError.None) return error;
            }
            return AnimationError.None;
        }

        public static AnimationError Encode(SomaRetarget retarget, ReadOnlySpan<Pose> samples, ReadOnlySpan<RigidPose> root, ReadOnlySpan<float> saContacts,
            int frames, float framesPerSecond, out byte[] bytes, out CompressionReport report)
        {
            bytes = Array.Empty<byte>();
            report = default;
            if (retarget == null || frames < 2 || !Scalar.IsFinite(framesPerSecond) || framesPerSecond <= 0f) return AnimationError.InvalidFrame;
            return SharedClipEncoder.Encode(retarget.Family, retarget.TargetParents, retarget.TargetReference, samples, frames, (frames - 1) / (double)framesPerSecond,
                false, RootPolicy.Planar, root, ReadOnlySpan<Pose>.Empty, saContacts, FootMarkers, true, CompressionLimits.Default, out bytes, out report);
        }

        public static readonly Float3 SoleOffset = new Float3(0f, -0.1f, 0.08f);

        static readonly PoseMarker[] FootMarkers = { new PoseMarker(50, SoleOffset), new PoseMarker(54, SoleOffset) };

        static float Yaw(Quaternion q) => 2f * (float)Math.Atan2(q.Y, q.W);
    }
}
