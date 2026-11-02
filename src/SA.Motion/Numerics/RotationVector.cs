using System;

namespace SA.Motion.Numerics
{
    public static class RotationVector
    {
        public static Quaternion Exp(Float3 vector)
        {
            if (!vector.IsFinite)
                return Quaternion.Identity;
            var square = (double)vector.X * vector.X + (double)vector.Y * vector.Y + (double)vector.Z * vector.Z;
            var angle = Math.Sqrt(square);
            var factor = angle < 1e-8 ? 0.5 - square / 48.0 : Math.Sin(angle * 0.5) / angle;
            return Quaternion.Normalize(new Quaternion((float)(vector.X * factor), (float)(vector.Y * factor), (float)(vector.Z * factor), (float)Math.Cos(angle * 0.5)));
        }

        public static Float3 Log(Quaternion rotation)
        {
            if (!rotation.IsValidRotation)
                return Float3.Zero;
            var q = Quaternion.Canonical(rotation);
            var length = Math.Sqrt((double)q.X * q.X + (double)q.Y * q.Y + (double)q.Z * q.Z);
            var factor = length < 1e-8 ? 2.0 : 2.0 * Math.Atan2(length, q.W) / length;
            return new Float3((float)(q.X * factor), (float)(q.Y * factor), (float)(q.Z * factor));
        }

        public static bool TryLeftJacobianInverse(Float3 rotation, Float3 velocity, out Float3 result)
        {
            result = Float3.Zero;
            if (!rotation.IsFinite || !velocity.IsFinite)
                return false;
            var square = (double)rotation.X * rotation.X + (double)rotation.Y * rotation.Y + (double)rotation.Z * rotation.Z;
            var angle = Math.Sqrt(square);
            if (angle > Math.PI + 1e-5)
                return false;
            var factor = angle < 1e-3 ? 1.0 / 12.0 + square / 720.0 + square * square / 30240.0
                : (1.0 - angle * 0.5 / Math.Tan(angle * 0.5)) / square;
            var cross = Float3.Cross(rotation, velocity);
            var twice = Float3.Cross(rotation, cross);
            var value = velocity - cross * 0.5f + twice * (float)factor;
            if (!value.IsFinite)
                return false;
            result = value;
            return true;
        }
    }
}
