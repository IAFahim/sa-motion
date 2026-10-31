using System;
using SA.Motion.Numerics;

namespace SA.Motion.Mechanics
{
    public static class QuaternionDrive
    {
        public static Float3 Torque(Quaternion current, Quaternion target, Float3 angularVelocity, Float3 targetAngularVelocity, DriveGains gains)
        {
            if (!IsFinite(current) || !IsFinite(target) || !angularVelocity.IsFinite || !targetAngularVelocity.IsFinite)
                return Float3.Zero;
            if (!Scalar.IsFinite(gains.Stiffness) || !Scalar.IsFinite(gains.Damping) || !Scalar.IsFinite(gains.MaxTorque))
                return Float3.Zero;
            if (gains.Stiffness < 0f || gains.Damping < 0f || gains.MaxTorque <= 0f)
                return Float3.Zero;
            var error = Quaternion.Multiply(Quaternion.Normalize(target), Quaternion.InverseUnit(Quaternion.Normalize(current)));
            if (error.W < 0f)
                error = -error;
            var vector = error.Vector;
            var vectorLength = vector.Length;
            Float3 rotation;
            if (vectorLength < 1e-6f)
                rotation = vector * 2f;
            else
                rotation = vector * (2f * (float)Math.Atan2(vectorLength, error.W) / vectorLength);
            var output = rotation * gains.Stiffness + (targetAngularVelocity - angularVelocity) * gains.Damping;
            if (!output.IsFinite)
                return Float3.Zero;
            var magnitude = output.Length;
            if (magnitude > gains.MaxTorque)
                output = output * (gains.MaxTorque / magnitude);
            return output;
        }

        static bool IsFinite(Quaternion value)
        {
            return Scalar.IsFinite(value.X) && Scalar.IsFinite(value.Y) && Scalar.IsFinite(value.Z) && Scalar.IsFinite(value.W);
        }
    }
}
