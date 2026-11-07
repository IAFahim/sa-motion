using System;
using SA.Animation;
using SA.Motion.Numerics;

namespace SA.Soma
{
    public static class SomaRoot
    {
        public static AnimationError Smooth(ReadOnlySpan<RigidPose> root, int window, Span<RigidPose> output)
        {
            if (window <= 0 || root.Length == 0 || output.Length != root.Length)
                return AnimationError.InvalidFrame;
            for (var i = 0; i < root.Length; i++)
                if (!root[i].IsValid)
                    return AnimationError.InvalidFrame;
            var count = root.Length;
            var yaw = new float[count];
            var positions = new Float3[count];
            for (var i = 0; i < count; i++)
            {
                yaw[i] = 2f * (float)Math.Atan2(root[i].Rotation.Y, root[i].Rotation.W);
                positions[i] = root[i].Position;
            }
            for (var i = 1; i < count; i++)
            {
                while (yaw[i] - yaw[i - 1] > Scalar.Pi)
                    yaw[i] -= Scalar.TwoPi;
                while (yaw[i] - yaw[i - 1] < -Scalar.Pi)
                    yaw[i] += Scalar.TwoPi;
            }
            var before = (window - 1) / 2;
            var after = window / 2;
            for (var i = 0; i < count; i++)
            {
                if (i == 0)
                {
                    output[0] = RigidPose.Identity;
                    continue;
                }
                var lo = i - before;
                if (lo < 0)
                    lo = 0;
                var hi = i + after;
                if (hi > count - 1)
                    hi = count - 1;
                var yawSum = 0f;
                var positionSum = Float3.Zero;
                for (var j = lo; j <= hi; j++)
                {
                    yawSum += yaw[j];
                    positionSum += positions[j];
                }
                var samples = (float)(hi - lo + 1);
                output[i] = new RigidPose(positionSum / samples, SomaAxes.YawRotation(yawSum / samples));
            }
            return AnimationError.None;
        }
    }
}
