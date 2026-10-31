using System;
using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Motion.Mechanics
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ConeTwistLimit
    {
        public float ConeAngle;
        public float TwistMin;
        public float TwistMax;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HingeLimit
    {
        public float Min;
        public float Max;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LimitGains
    {
        public float Stiffness;
        public float Damping;
        public float MaxTorque;
    }

    public static class SoftLimits
    {
        public static Float3 ConeTwistTorque(Quaternion relative, Quaternion rest, Float3 limbAxis, Float3 relativeAngularVelocity, in ConeTwistLimit limit, in LimitGains gains)
        {
            if (!GainsValid(gains))
                return Float3.Zero;
            if (!Scalar.IsFinite(limit.ConeAngle) || !Scalar.IsFinite(limit.TwistMin) || !Scalar.IsFinite(limit.TwistMax))
                return Float3.Zero;
            if (limit.ConeAngle < 0f || limit.TwistMin > limit.TwistMax)
                return Float3.Zero;
            if (!limbAxis.IsFinite || !relativeAngularVelocity.IsFinite)
                return Float3.Zero;
            if (!IsFinite(relative) || !IsFinite(rest))
                return Float3.Zero;
            if (limbAxis.LengthSquared <= Scalar.MagnitudeEpsilon * Scalar.MagnitudeEpsilon)
                return Float3.Zero;
            var axis = limbAxis / limbAxis.Length;
            var deviation = Deviation(relative, rest);
            var along = Float3.Dot(deviation.Vector, axis);
            var twistAngle = 2f * (float)Math.Atan2(along, deviation.W);
            var twistExcess = 0f;
            if (twistAngle > limit.TwistMax)
                twistExcess = twistAngle - limit.TwistMax;
            else if (twistAngle < limit.TwistMin)
                twistExcess = twistAngle - limit.TwistMin;
            var twistLengthSquared = along * along + deviation.W * deviation.W;
            Float3 swingAxis = Float3.Zero;
            var swingAngle = 0f;
            if (twistLengthSquared > Scalar.MagnitudeEpsilon * Scalar.MagnitudeEpsilon)
            {
                var inverse = 1f / (float)Math.Sqrt(twistLengthSquared);
                var twist = new Quaternion(axis.X * along * inverse, axis.Y * along * inverse, axis.Z * along * inverse, deviation.W * inverse);
                var swing = Quaternion.Multiply(deviation, Quaternion.InverseUnit(twist));
                var swingLength = swing.Vector.Length;
                swingAngle = 2f * (float)Math.Atan2(swingLength, swing.W);
                if (swingLength > 1e-6f)
                    swingAxis = swing.Vector / swingLength;
            }
            var coneExcess = swingAngle > limit.ConeAngle ? swingAngle - limit.ConeAngle : 0f;
            if (coneExcess <= 0f && twistExcess == 0f)
                return Float3.Zero;
            var torque = Float3.Zero;
            if (coneExcess > 0f && swingAxis.LengthSquared > 0f)
                torque = torque + swingAxis * (-gains.Stiffness * coneExcess);
            if (twistExcess != 0f)
                torque = torque + axis * (-gains.Stiffness * twistExcess);
            if (coneExcess > 0f && swingAxis.LengthSquared > 0f)
            {
                var swingRate = Float3.Dot(swingAxis, relativeAngularVelocity);
                if (swingRate > 0f)
                    torque = torque + swingAxis * (-gains.Damping * swingRate);
            }
            if (twistExcess != 0f)
            {
                var twistRate = Float3.Dot(axis, relativeAngularVelocity);
                if (twistRate * twistExcess > 0f)
                    torque = torque + axis * (-gains.Damping * twistRate);
            }
            if (!torque.IsFinite)
                return Float3.Zero;
            var magnitude = torque.Length;
            if (magnitude > gains.MaxTorque)
                torque = torque * (gains.MaxTorque / magnitude);
            return torque;
        }

        public static Float3 HingeTorque(Quaternion relative, Quaternion rest, Float3 hingeAxis, Float3 relativeAngularVelocity, in HingeLimit limit, in LimitGains gains)
        {
            if (!GainsValid(gains))
                return Float3.Zero;
            if (!Scalar.IsFinite(limit.Min) || !Scalar.IsFinite(limit.Max))
                return Float3.Zero;
            if (limit.Min > limit.Max)
                return Float3.Zero;
            if (!hingeAxis.IsFinite || !relativeAngularVelocity.IsFinite)
                return Float3.Zero;
            if (!IsFinite(relative) || !IsFinite(rest))
                return Float3.Zero;
            if (hingeAxis.LengthSquared <= Scalar.MagnitudeEpsilon * Scalar.MagnitudeEpsilon)
                return Float3.Zero;
            var axis = hingeAxis / hingeAxis.Length;
            var deviation = Deviation(relative, rest);
            var along = Float3.Dot(deviation.Vector, axis);
            var angle = 2f * (float)Math.Atan2(along, deviation.W);
            float excess;
            if (angle > limit.Max)
                excess = angle - limit.Max;
            else if (angle < limit.Min)
                excess = angle - limit.Min;
            else
                return Float3.Zero;
            var rate = Float3.Dot(axis, relativeAngularVelocity);
            var torque = axis * (-gains.Stiffness * excess);
            if (rate * excess > 0f)
                torque = torque + axis * (-gains.Damping * rate);
            if (!torque.IsFinite)
                return Float3.Zero;
            var magnitude = torque.Length;
            if (magnitude > gains.MaxTorque)
                torque = torque * (gains.MaxTorque / magnitude);
            return torque;
        }

        static Quaternion Deviation(Quaternion relative, Quaternion rest)
        {
            var from = Quaternion.Normalize(relative);
            var to = Quaternion.Normalize(rest);
            return Quaternion.Canonical(Quaternion.Multiply(from, Quaternion.InverseUnit(to)));
        }

        static bool GainsValid(in LimitGains gains)
        {
            if (!Scalar.IsFinite(gains.Stiffness) || !Scalar.IsFinite(gains.Damping) || !Scalar.IsFinite(gains.MaxTorque))
                return false;
            return gains.Stiffness >= 0f && gains.Damping >= 0f && gains.MaxTorque > 0f;
        }

        static bool IsFinite(Quaternion value)
        {
            return Scalar.IsFinite(value.X) && Scalar.IsFinite(value.Y) && Scalar.IsFinite(value.Z) && Scalar.IsFinite(value.W);
        }
    }
}
