using System;
using SA.Humanoid;
using SA.Motion.Numerics;

namespace SA.ReactionControl
{
    public static class GetUp
    {
        public static RestingPose Classify(ReadOnlySpan<RoleBody> bodies, in GetUpParams parameters)
        {
            if (bodies.Length != Roles.Count)
                return RestingPose.Unknown;
            var pelvis = bodies[(int)Role.Pelvis];
            var torso = bodies[(int)Role.Torso];
            var head = bodies[(int)Role.Head];
            if (!pelvis.Position.IsFinite || !torso.Position.IsFinite || !head.Position.IsFinite)
                return RestingPose.Unknown;
            if (!pelvis.Rotation.IsValidRotation || !torso.Rotation.IsValidRotation)
                return RestingPose.Unknown;
            var headRise = head.Position.Y - pelvis.Position.Y;
            if (!Scalar.IsFinite(headRise))
                return RestingPose.Unknown;
            var dot = SanitizeDot(parameters.PoseDot);
            var torsoRotation = Quaternion.Normalize(torso.Rotation);
            var up = Quaternion.Rotate(torsoRotation, new Float3(0f, 1f, 0f));
            var right = Quaternion.Rotate(torsoRotation, new Float3(1f, 0f, 0f));
            if (headRise >= SanitizeRise(parameters.UprightHeadRise) && up.Y >= dot)
                return RestingPose.Upright;
            if (headRise < SanitizeDrop(parameters.LyingHeadDrop))
            {
                if (up.Y >= dot)
                    return RestingPose.Supine;
                if (up.Y <= -dot)
                    return RestingPose.Prone;
                if (Scalar.Abs(right.Y) >= dot)
                    return RestingPose.Side;
            }
            return RestingPose.Unknown;
        }

        public static GetUpChoice ChooseClip(RestingPose pose, ReadOnlySpan<GetUpClipEntry> clips)
        {
            for (var i = 0; i < clips.Length; i++)
            {
                if (clips[i].StartPose == pose)
                    return new GetUpChoice { HasClip = 1, ClipId = clips[i].ClipId };
            }
            return new GetUpChoice { HasClip = 0, ClipId = -1 };
        }

        public static GetUpAlignment ComputeAlignment(ReadOnlySpan<RoleBody> bodies, in GetUpParams parameters)
        {
            var bound = SanitizeBound(parameters.MaxAlignOffset);
            if (bodies.Length != Roles.Count)
                return new GetUpAlignment { Valid = 0, Position = Float3.Zero, Yaw = 0f };
            var pelvis = bodies[(int)Role.Pelvis];
            if (!pelvis.Position.IsFinite || !pelvis.Rotation.IsValidRotation)
                return new GetUpAlignment { Valid = 0, Position = Float3.Zero, Yaw = 0f };
            var forward = Quaternion.Rotate(Quaternion.Normalize(pelvis.Rotation), new Float3(0f, 0f, 1f));
            var ground = forward.X * forward.X + forward.Z * forward.Z;
            var target = ClampGround(pelvis.Position, bound);
            if (!Scalar.IsFinite(ground) || ground <= Scalar.MagnitudeEpsilon * Scalar.MagnitudeEpsilon)
                return new GetUpAlignment { Valid = 0, Position = target, Yaw = 0f };
            var yaw = (float)Math.Atan2(forward.X, forward.Z);
            if (!Scalar.IsFinite(yaw))
                return new GetUpAlignment { Valid = 0, Position = target, Yaw = 0f };
            return new GetUpAlignment { Valid = 1, Position = target, Yaw = yaw };
        }

        public static GetUpClearance CheckClearance(ReadOnlySpan<SupportProbe> probes, in GetUpParams parameters)
        {
            var clear = parameters.ClearDistance;
            if (!Scalar.IsFinite(clear) || clear < 0f)
                clear = GetUpParams.Standard.ClearDistance;
            for (var i = 0; i < probes.Length; i++)
            {
                var probe = probes[i];
                if (probe.Hit == 0)
                    continue;
                if (!Scalar.IsFinite(probe.Distance) || probe.Distance < 0f || probe.Distance > clear)
                    continue;
                return GetUpClearance.Blocked;
            }
            return GetUpClearance.Go;
        }

        static float SanitizeDot(float dot)
        {
            if (!Scalar.IsFinite(dot))
                return GetUpParams.Standard.PoseDot;
            if (dot > 1f)
                return 1f;
            if (dot < 0f)
                return 0f;
            return dot;
        }

        static float SanitizeDrop(float drop)
        {
            if (!Scalar.IsFinite(drop))
                return GetUpParams.Standard.LyingHeadDrop;
            return drop;
        }

        static float SanitizeRise(float rise)
        {
            if (!Scalar.IsFinite(rise) || rise < 0f)
                return GetUpParams.Standard.UprightHeadRise;
            return rise;
        }

        static float SanitizeBound(float bound)
        {
            if (!Scalar.IsFinite(bound) || bound <= 0f)
                return GetUpParams.Standard.MaxAlignOffset;
            return bound;
        }

        static Float3 ClampGround(Float3 position, float bound)
        {
            return new Float3(ClampAxis(position.X, bound), 0f, ClampAxis(position.Z, bound));
        }

        static float ClampAxis(float value, float bound)
        {
            if (!Scalar.IsFinite(value))
                return 0f;
            if (value > bound)
                return bound;
            if (value < -bound)
                return -bound;
            return value;
        }
    }
}
