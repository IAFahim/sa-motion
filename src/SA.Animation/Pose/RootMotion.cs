using System;
using SA.Motion.Numerics;

namespace SA.Animation
{
    public static class RootMotion
    {
        public static bool TryPower(RigidPose value, long exponent, out RigidPose result)
        {
            result = RigidPose.Identity;
            if (!value.IsValid || exponent < -4294967294L || exponent > 4294967294L)
                return false;
            var factor = value;
            if (exponent < 0)
            {
                if (!RigidMath.TryInverse(factor, out factor))
                    return false;
                exponent = -exponent;
            }
            while (exponent != 0)
            {
                if ((exponent & 1) != 0 && !RigidMath.TryCompose(result, factor, out result))
                    return false;
                exponent >>= 1;
                if (exponent != 0 && !RigidMath.TryCompose(factor, factor, out factor))
                    return false;
            }
            return true;
        }

        public static AnimationError Delta(ReadOnlySpan<RigidPose> root, double duration, double previousSeconds, double currentSeconds, bool loop, float heightRatio, out RigidPose delta)
        {
            delta = RigidPose.Identity;
            if (!Scalar.IsFinite(heightRatio) || heightRatio <= 0f || root.Length == 0)
                return AnimationError.InvalidFrame;
            var error = SampleLocator.Locate(duration, root.Length, previousSeconds, loop, out var previous);
            if (error != AnimationError.None)
                return error;
            error = SampleLocator.Locate(duration, root.Length, currentSeconds, loop, out var current);
            if (error != AnimationError.None)
                return error;
            if (!IsIdentity(root[0]) || !Valid(root[previous.First]) || !Valid(root[previous.Second]) || !Valid(root[current.First]) || !Valid(root[current.Second]) || !Valid(root[root.Length - 1]))
                return AnimationError.InvalidFrame;
            var before = Sample(root, previous);
            var after = Sample(root, current);
            if (!RigidMath.TryInverse(before, out var inverse) || !TryPower(root[root.Length - 1], current.Cycle - previous.Cycle, out var cycles)
                || !RigidMath.TryCompose(inverse, cycles, out var interval) || !RigidMath.TryCompose(interval, after, out var movement))
                return AnimationError.TimeOutOfRange;
            var position = movement.Position * heightRatio;
            if (!position.IsFinite)
                return AnimationError.TimeOutOfRange;
            delta = new RigidPose(position, movement.Rotation);
            return AnimationError.None;
        }

        static bool Valid(in RigidPose pose) => pose.Position.IsFinite && RigidMath.UnitRotation(pose.Rotation);
        static bool IsIdentity(in RigidPose pose) => Valid(pose) && pose.Position.LengthSquared <= 1e-12f && Math.Abs(pose.Rotation.W) >= 0.999999f;
        static RigidPose Sample(ReadOnlySpan<RigidPose> root, in SampleWindow frame) => new RigidPose(
            root[frame.First].Position + (root[frame.Second].Position - root[frame.First].Position) * frame.Alpha,
            PoseMath.Nlerp(root[frame.First].Rotation, root[frame.Second].Rotation, frame.Alpha));
    }
}
