using System;
using System.Runtime.InteropServices;

namespace SA.Motion.Numerics
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Quaternion
    {
        public float X;
        public float Y;
        public float Z;
        public float W;

        public Quaternion(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
        public bool IsValidRotation => Scalar.IsFinite(Dot(this, this)) && Dot(this, this) > Scalar.MagnitudeEpsilon * Scalar.MagnitudeEpsilon;
        public Float3 Vector => new Float3(X, Y, Z);
        public static Quaternion Identity => new Quaternion(0f, 0f, 0f, 1f);
        public static Quaternion operator -(Quaternion q) => new Quaternion(-q.X, -q.Y, -q.Z, -q.W);
        public static float Dot(Quaternion a, Quaternion b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
        public static Quaternion Multiply(Quaternion a, Quaternion b)
        {
            var vector = b.Vector * a.W + a.Vector * b.W + Float3.Cross(a.Vector, b.Vector);
            return new Quaternion(vector.X, vector.Y, vector.Z, a.W * b.W - Float3.Dot(a.Vector, b.Vector));
        }
        public static Quaternion Normalize(Quaternion q)
        {
            var length = Dot(q, q);
            if (!Scalar.IsFinite(length) || length <= Scalar.MagnitudeEpsilon * Scalar.MagnitudeEpsilon)
                return Identity;
            var inverse = 1f / (float)Math.Sqrt(length);
            return new Quaternion(q.X * inverse, q.Y * inverse, q.Z * inverse, q.W * inverse);
        }
        public static Quaternion InverseUnit(Quaternion q) => new Quaternion(-q.X, -q.Y, -q.Z, q.W);
        public static Quaternion Canonical(Quaternion q)
        {
            q = Normalize(q);
            if (q.W < 0f || (q.W == 0f && (q.X < 0f || (q.X == 0f && (q.Y < 0f || (q.Y == 0f && q.Z < 0f))))))
                q = -q;
            return q;
        }
        public static Quaternion AxisAngle(Float3 axis, float angle)
        {
            var half = angle * 0.5f;
            var sine = (float)Math.Sin(half);
            return new Quaternion(axis.X * sine, axis.Y * sine, axis.Z * sine, (float)Math.Cos(half));
        }
        public static Float3 Rotate(Quaternion q, Float3 v)
        {
            var twice = Float3.Cross(q.Vector, v) * 2f;
            return v + twice * q.W + Float3.Cross(q.Vector, twice);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Pose
    {
        public Float3 Position;
        public Quaternion Rotation;
        public Float3 Scale;
        public static Pose Identity => new Pose { Rotation = Quaternion.Identity, Scale = Float3.One };
    }
}
