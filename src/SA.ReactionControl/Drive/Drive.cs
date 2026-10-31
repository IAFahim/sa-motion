using System;
using SA.Motion.Mechanics;
using SA.Motion.Numerics;

namespace SA.ReactionControl.Drive
{
    public static class Drive
    {
        public static JointDrive ToJointDrive(EffectorAngles desired, DriveLimits limits, DriveParams par)
        {
            return ToJointDrive(desired, limits, default(SoftBounds), par);
        }

        public static JointDrive ToJointDrive(EffectorAngles desired, DriveLimits limits, SoftBounds soft, DriveParams par)
        {
            if (!Finite(desired) || !Finite(limits))
                return JointDrive.Passive;
            var gains = Gains(par.Strength, par.Damping, par);
            if (gains.Stiffness <= 0f || gains.MaxTorque <= 0f)
                return JointDrive.Passive;
            var clamped = ClampToLimits(desired, limits, par.LimitMargin);
            var pull = SanitizePull(par.SoftPull);
            var shaped = new EffectorAngles
            {
                Twist = PullToSoftLimit(clamped.Twist, soft.Twist, pull),
                Lean1 = PullToSoftLimit(clamped.Lean1, soft.Lean1, pull),
                Lean2 = PullToSoftLimit(clamped.Lean2, soft.Lean2, pull),
            };
            if (!Finite(shaped))
                return JointDrive.Passive;
            return new JointDrive
            {
                Target = FromAngles(shaped),
                TargetAngularVelocity = Float3.Zero,
                Stiffness = gains.Stiffness,
                Damping = gains.Damping,
                MaxTorque = gains.MaxTorque,
            };
        }

        public static DriveGains Gains(float strength, float damping, DriveParams par)
        {
            var result = default(DriveGains);
            if (!Scalar.IsFinite(strength) || !Scalar.IsFinite(damping))
                return result;
            if (!Scalar.IsFinite(par.MaxTorque) || par.MaxTorque <= 0f)
                return result;
            if (!Scalar.IsFinite(par.StiffnessCap) || par.StiffnessCap <= 0f)
                return result;
            if (strength <= 0f)
                return result;
            var floor = Scalar.IsFinite(par.MinDamping) && par.MinDamping > 0f ? par.MinDamping : 0f;
            result.Stiffness = strength < par.StiffnessCap ? strength : par.StiffnessCap;
            result.Damping = damping > floor ? damping : floor;
            result.MaxTorque = par.MaxTorque;
            return result;
        }

        public static EffectorAngles ClampToLimits(EffectorAngles desired, DriveLimits limits, float margin)
        {
            var safe = Scalar.IsFinite(margin) && margin > 0f ? margin : 0f;
            Range(limits.Lean1Min, limits.Lean1Max, safe, out var mid1, out var half1);
            Range(limits.Lean2Min, limits.Lean2Max, safe, out var mid2, out var half2);
            var lean1 = ClampAxis(desired.Lean1, mid1, half1);
            var lean2 = ClampAxis(desired.Lean2, mid2, half2);
            if (half1 > 0f && half2 > 0f && Scalar.IsFinite(lean1) && Scalar.IsFinite(lean2))
            {
                var u = (lean1 - mid1) / half1;
                var v = (lean2 - mid2) / half2;
                var radiusSquared = u * u + v * v;
                if (radiusSquared > 1f)
                {
                    var scale = 1f / (float)Math.Sqrt(radiusSquared);
                    lean1 = mid1 + u * scale * half1;
                    lean2 = mid2 + v * scale * half2;
                }
            }
            Range(limits.TwistMin, limits.TwistMax, safe, out var twistMid, out var twistHalf);
            return new EffectorAngles
            {
                Twist = ClampAxis(desired.Twist, twistMid, twistHalf),
                Lean1 = lean1,
                Lean2 = lean2,
            };
        }

        public static float PullToSoftLimit(float desired, SoftLimit limit, float pull)
        {
            var kept = Scalar.IsFinite(pull) ? pull : 0f;
            if (kept < 0f)
                kept = 0f;
            if (kept > 1f)
                kept = 1f;
            if (limit.Enabled == 0)
                return Scalar.IsFinite(desired) ? desired : 0f;
            if (!Scalar.IsFinite(limit.Angle) || !Scalar.IsFinite(limit.Direction))
                return Scalar.IsFinite(desired) ? desired : 0f;
            if (!Scalar.IsFinite(desired))
                return limit.Angle;
            var excess = desired - limit.Angle;
            if (excess * limit.Direction <= 0f)
                return desired;
            return limit.Angle + excess * kept;
        }

        public static Quaternion FromAngles(EffectorAngles angles)
        {
            var twist = Scalar.IsFinite(angles.Twist) ? Wrap(angles.Twist) : 0f;
            var lean1 = Scalar.IsFinite(angles.Lean1) ? angles.Lean1 : 0f;
            var lean2 = Scalar.IsFinite(angles.Lean2) ? angles.Lean2 : 0f;
            var magnitude = (float)Math.Sqrt(lean1 * lean1 + lean2 * lean2);
            var swing = Quaternion.Identity;
            if (magnitude >= 1e-9f)
            {
                var inverse = 1f / magnitude;
                swing = Quaternion.AxisAngle(new Float3(lean1 * inverse, lean2 * inverse, 0f), magnitude);
            }
            var spin = Quaternion.AxisAngle(new Float3(0f, 0f, 1f), twist);
            return Quaternion.Canonical(Quaternion.Multiply(swing, spin));
        }

        public static EffectorAngles ToAngles(Quaternion rotation)
        {
            var result = default(EffectorAngles);
            if (!rotation.IsValidRotation)
                return result;
            var shaped = Quaternion.Canonical(Quaternion.Normalize(rotation));
            var twist = 2f * (float)Math.Atan2(shaped.Z, shaped.W);
            var spin = Quaternion.AxisAngle(new Float3(0f, 0f, 1f), twist);
            var swing = Quaternion.Normalize(Quaternion.Multiply(shaped, Quaternion.InverseUnit(spin)));
            var vector = swing.Vector;
            var length = vector.Length;
            if (length < 1e-9f || !Scalar.IsFinite(length))
            {
                result.Twist = twist;
                return result;
            }
            var magnitude = 2f * (float)Math.Atan2(length, swing.W);
            var scale = magnitude / length;
            result.Twist = twist;
            result.Lean1 = vector.X * scale;
            result.Lean2 = vector.Y * scale;
            return result;
        }

        public static Float3 TrackingError(Quaternion current, Quaternion target)
        {
            return RotationError.Between(current, target);
        }

        static float Wrap(float angle)
        {
            var turned = angle % Scalar.TwoPi;
            if (turned > Scalar.Pi)
                return turned - Scalar.TwoPi;
            if (turned < -Scalar.Pi)
                return turned + Scalar.TwoPi;
            return turned;
        }

        static void Range(float min, float max, float margin, out float mid, out float half)
        {
            var low = Scalar.IsFinite(min) ? min : 0f;
            var high = Scalar.IsFinite(max) ? max : 0f;
            if (high < low)
            {
                var swap = low;
                low = high;
                high = swap;
            }
            low += margin;
            high -= margin;
            if (high < low)
            {
                var center = (low + high) * 0.5f;
                low = center;
                high = center;
            }
            mid = (low + high) * 0.5f;
            half = (high - low) * 0.5f;
        }

        static float ClampAxis(float value, float mid, float half)
        {
            if (!Scalar.IsFinite(value))
                return mid;
            if (half <= 0f)
                return mid;
            if (value < mid - half)
                return mid - half;
            if (value > mid + half)
                return mid + half;
            return value;
        }

        static float SanitizePull(float pull)
        {
            if (!Scalar.IsFinite(pull))
                return 0f;
            if (pull < 0f)
                return 0f;
            if (pull > 1f)
                return 1f;
            return pull;
        }

        static bool Finite(EffectorAngles angles)
        {
            return Scalar.IsFinite(angles.Twist) && Scalar.IsFinite(angles.Lean1) && Scalar.IsFinite(angles.Lean2);
        }

        static bool Finite(DriveLimits limits)
        {
            return Scalar.IsFinite(limits.TwistMin) && Scalar.IsFinite(limits.TwistMax)
                && Scalar.IsFinite(limits.Lean1Min) && Scalar.IsFinite(limits.Lean1Max)
                && Scalar.IsFinite(limits.Lean2Min) && Scalar.IsFinite(limits.Lean2Max);
        }
    }
}
