using System;
using SA.Motion.Numerics;

namespace SA.Motion.Kinematics
{
    public static class Aim
    {
        public static Quaternion Solve(Quaternion rotation, Float3 localAxis, Float3 direction, float weight, float maxRadians)
        {
            rotation = Quaternion.Normalize(rotation);
            if (!direction.IsFinite || !localAxis.IsFinite || direction.LengthSquared < 1e-10f || localAxis.LengthSquared < 1e-10f)
                return rotation;
            weight = Scalar.Max(0f, Scalar.Min(1f, Scalar.Finite(weight)));
            maxRadians = Scalar.Max(0f, Scalar.Min(Scalar.Pi, Scalar.Finite(maxRadians)));
            var delta = Quaternion.Canonical(TwoBoneIK.FromTo(Quaternion.Rotate(rotation, localAxis), direction));
            var sine = delta.Vector.Length;
            if (sine < 1e-6f)
                return rotation;
            var angle = Scalar.Min(2f * (float)Math.Atan2(sine, delta.W), maxRadians) * weight;
            return Quaternion.Normalize(Quaternion.Multiply(Quaternion.AxisAngle(delta.Vector / sine, angle), rotation));
        }
    }
}
