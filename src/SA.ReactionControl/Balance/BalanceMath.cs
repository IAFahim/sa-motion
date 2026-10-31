using System;
using SA.Motion.Mechanics;
using SA.Motion.Numerics;

namespace SA.ReactionControl.Balance
{
    internal static class BalanceMath
    {
        public static float Clamp(float value, float low, float high)
        {
            if (!Scalar.IsFinite(value))
                return 0f;
            if (value < low)
                return low;
            if (value > high)
                return high;
            return value;
        }

        public static float ClampMagnitude(float value, float limit)
        {
            if (!Scalar.IsFinite(value))
                return 0f;
            if (value > limit)
                return limit;
            if (value < -limit)
                return -limit;
            return value;
        }

        public static Float3 Sanitize(Float3 value)
        {
            if (!value.IsFinite)
                return Float3.Zero;
            return new Float3(
                ClampMagnitude(value.X, 10000f),
                ClampMagnitude(value.Y, 10000f),
                ClampMagnitude(value.Z, 10000f));
        }

        public static Float3 SanitizeUnit(Float3 value, Float3 fallback)
        {
            if (!value.IsFinite)
                return fallback;
            var length = value.Length;
            if (!(length > 1e-6f))
                return fallback;
            return value / length;
        }

        public static Quaternion SanitizeRotation(Quaternion value)
        {
            if (!value.IsValidRotation)
                return Quaternion.Identity;
            return Quaternion.Normalize(value);
        }

        public static Float3 Horizontal(Float3 value, Float3 up)
        {
            return Sanitize(value) - Sanitize(up) * Float3.Dot(Sanitize(value), Sanitize(up));
        }

        public static Float3 SetHeight(Float3 value, Float3 up, float height)
        {
            var clean = Sanitize(value);
            var axis = Sanitize(up);
            if (!Scalar.IsFinite(height))
                height = 0f;
            return clean + axis * (height - Float3.Dot(clean, axis));
        }

        public static Float3 NormalizeOrZero(Float3 value, float epsilon)
        {
            if (!value.IsFinite)
                return Float3.Zero;
            var squared = Float3.Dot(value, value);
            if (!(squared > epsilon * epsilon))
                return Float3.Zero;
            return value / (float)Math.Sqrt(squared);
        }

        public static float Atan2(float y, float x)
        {
            if (!Scalar.IsFinite(y) || !Scalar.IsFinite(x))
                return 0f;
            if (x == 0f && y == 0f)
                return 0f;
            return (float)Math.Atan2(y, x);
        }

        public static float Acos(float value)
        {
            if (!Scalar.IsFinite(value))
                return 0f;
            if (value <= -1f)
                return Scalar.Pi;
            if (value >= 1f)
                return 0f;
            return (float)Math.Acos(value);
        }

        public static float Asin(float value)
        {
            if (!Scalar.IsFinite(value))
                return 0f;
            if (value <= -1f)
                return -Scalar.Pi * 0.5f;
            if (value >= 1f)
                return Scalar.Pi * 0.5f;
            return (float)Math.Asin(value);
        }

        public static void Separate(ref Float3 a, ref Float3 b, float min)
        {
            if (!Scalar.IsFinite(min) || min <= 0f)
                return;
            var delta = Sanitize(b) - Sanitize(a);
            var length = delta.Length;
            if (!(length >= 0f) || length >= min)
                return;
            var direction = NormalizeOrZero(delta, 1e-6f);
            a = Sanitize(a) - direction * ((length - min) * 0.5f);
            b = Sanitize(b) + direction * ((min - length) * 0.5f);
        }

        public static Float3 ClampLength(Float3 value, float low, float high)
        {
            var clean = Sanitize(value);
            if (!(high > 0f))
                return Float3.Zero;
            if (low < 0f)
                low = 0f;
            if (low > high)
                low = high;
            var squared = Float3.Dot(clean, clean);
            if (!(squared >= 0f))
                return Float3.Zero;
            var length = (float)Math.Sqrt(squared);
            if (length > high)
                return clean * (high / length);
            if (length < low)
            {
                if (!(length > 1e-9f))
                    return Float3.Zero;
                return clean * (low / length);
            }
            return clean;
        }

        public static Quaternion RotVecToQuat(Float3 packed)
        {
            if (!packed.IsFinite)
                return Quaternion.Identity;
            var squared = Float3.Dot(packed, packed);
            if (!(squared > 0f))
                return Quaternion.Identity;
            var length = (float)Math.Sqrt(squared);
            return Quaternion.AxisAngle(packed / length, length);
        }

        public static void QuatRows(Quaternion q, out Float3 x, out Float3 y, out Float3 z)
        {
            var clean = SanitizeRotation(q);
            x = Quaternion.Rotate(clean, new Float3(1f, 0f, 0f));
            y = Quaternion.Rotate(clean, new Float3(0f, 1f, 0f));
            z = Quaternion.Rotate(clean, new Float3(0f, 0f, 1f));
        }

        public static Quaternion Arc(Float3 a, Float3 b, float epsilon)
        {
            var na = NormalizeOrZero(a, epsilon);
            var nb = NormalizeOrZero(b, epsilon);
            var dot = Float3.Dot(na, nb);
            if (!Scalar.IsFinite(dot))
                return Quaternion.Identity;
            if (Math.Abs(dot - 1f) < epsilon)
                return Quaternion.Identity;
            var sum = dot + 1f;
            Float3 use = nb;
            if (Math.Abs(sum) < epsilon)
            {
                if (Math.Abs(a.X) < Math.Abs(a.Y) && Math.Abs(a.X) < Math.Abs(a.Z))
                    use = new Float3(1f, 0f, 0f);
                else if (Math.Abs(a.Y) < Math.Abs(a.Z))
                    use = new Float3(0f, 1f, 0f);
                else
                    use = new Float3(0f, 0f, 1f);
                var cross = Float3.Cross(a, use);
                return Quaternion.AxisAngle(NormalizeOrZero(cross, epsilon), Scalar.Pi);
            }
            var axis = Float3.Cross(na, use);
            var squared = sum * sum + Float3.Dot(axis, axis);
            if (!(squared > 0f))
                return Quaternion.Identity;
            var inverse = 1f / (float)Math.Sqrt(squared);
            return new Quaternion(axis.X * inverse, axis.Y * inverse, axis.Z * inverse, sum * inverse);
        }

        public static Quaternion QMul(Quaternion a, Quaternion b)
        {
            return Quaternion.Multiply(a, b);
        }

        public static Float3 ToLocal(Frame frame, Float3 value)
        {
            var rotation = SanitizeRotation(frame.Rotation);
            return Quaternion.Rotate(Quaternion.InverseUnit(rotation), Sanitize(value));
        }

        public static Float3 ToWorld(Frame frame, Float3 value)
        {
            var rotation = SanitizeRotation(frame.Rotation);
            return Quaternion.Rotate(rotation, Sanitize(value));
        }

        public static Float3 AxisX(Frame frame)
        {
            return ToWorld(frame, new Float3(1f, 0f, 0f));
        }

        public static Float3 AxisY(Frame frame)
        {
            return ToWorld(frame, new Float3(0f, 1f, 0f));
        }

        public static Float3 AxisZ(Frame frame)
        {
            return ToWorld(frame, new Float3(0f, 0f, 1f));
        }

        public static Quaternion JointRotation(float lean1, float lean2, float twist)
        {
            if (!Scalar.IsFinite(lean1))
                lean1 = 0f;
            if (!Scalar.IsFinite(lean2))
                lean2 = 0f;
            if (!Scalar.IsFinite(twist))
                twist = 0f;
            var tilt = (float)Math.Sqrt(lean1 * lean1 + lean2 * lean2);
            Quaternion lean = Quaternion.Identity;
            if (tilt > 1e-6f)
                lean = Quaternion.AxisAngle(new Float3(-lean2 / tilt, lean1 / tilt, 0f), tilt);
            var spin = Quaternion.AxisAngle(new Float3(0f, 0f, 1f), twist);
            return Quaternion.Normalize(Quaternion.Multiply(lean, spin));
        }

        public static void SplitRotation(Quaternion relative, out float lean1, out float lean2, out float twist)
        {
            var clean = SanitizeRotation(relative);
            var w = clean.W;
            var z = clean.Z;
            twist = 2f * Atan2(z, w);
            if (twist > Scalar.Pi)
                twist -= Scalar.TwoPi;
            if (twist < -Scalar.Pi)
                twist += Scalar.TwoPi;
            var undo = Quaternion.AxisAngle(new Float3(0f, 0f, 1f), -twist);
            var flat = Quaternion.Multiply(clean, undo);
            var direction = Quaternion.Rotate(Quaternion.Normalize(flat), new Float3(0f, 0f, 1f));
            var angle = Acos(Clamp(direction.Z, -1f, 1f));
            var radius = (float)Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            if (!(radius > 1e-6f))
            {
                lean1 = 0f;
                lean2 = 0f;
                return;
            }
            var gain = angle / radius;
            lean1 = direction.X * gain;
            lean2 = direction.Y * gain;
        }

        public static float SegmentLength(Float3 a, Float3 b)
        {
            var delta = Sanitize(a) - Sanitize(b);
            var length = delta.Length;
            if (!Scalar.IsFinite(length))
                return 0.45f;
            if (length < 0.1f)
                return 0.1f;
            if (length > 1.0f)
                return 1.0f;
            return length;
        }
    }
}
