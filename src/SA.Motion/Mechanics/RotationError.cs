using System;
using SA.Motion.Numerics;

namespace SA.Motion.Mechanics
{
    public static class RotationError
    {
        public static Float3 Between(Quaternion current, Quaternion target)
        {
            if (!current.IsValidRotation || !target.IsValidRotation)
                return Float3.Zero;
            var delta = Quaternion.Canonical(Quaternion.Multiply(Quaternion.Normalize(target), Quaternion.InverseUnit(Quaternion.Normalize(current))));
            var vector = delta.Vector;
            var length = vector.Length;
            if (length < 1e-6f)
                return vector * 2f;
            return vector * (2f * (float)Math.Atan2(length, delta.W) / length);
        }
    }
}
