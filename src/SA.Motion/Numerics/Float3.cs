using System;
using System.Runtime.InteropServices;

namespace SA.Motion.Numerics
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Float3
    {
        public float X;
        public float Y;
        public float Z;

        public Float3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static Float3 Zero => default;
        public static Float3 One => new Float3(1f, 1f, 1f);
        public static Float3 operator +(Float3 a, Float3 b) => new Float3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Float3 operator -(Float3 a, Float3 b) => new Float3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Float3 operator *(Float3 a, float b) => new Float3(a.X * b, a.Y * b, a.Z * b);
        public static Float3 operator /(Float3 a, float b) => new Float3(a.X / b, a.Y / b, a.Z / b);
        public static float Dot(Float3 a, Float3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static Float3 Cross(Float3 a, Float3 b) => new Float3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public float LengthSquared => Dot(this, this);
        public float Length => (float)Math.Sqrt(LengthSquared);
        public bool IsFinite => Scalar.IsFinite(X) && Scalar.IsFinite(Y) && Scalar.IsFinite(Z);
        public bool IsPositive => IsFinite && X > 0f && Y > 0f && Z > 0f;
        public static Float3 Log(Float3 value) => new Float3((float)Math.Log(value.X), (float)Math.Log(value.Y), (float)Math.Log(value.Z));
        public static Float3 Exp(Float3 value) => new Float3((float)Math.Exp(value.X), (float)Math.Exp(value.Y), (float)Math.Exp(value.Z));
    }

    public static class Scalar
    {
        public const float MagnitudeEpsilon = 1e-6f;
        public const float DurationEpsilon = 1e-5f;
        public const float Pi = (float)Math.PI;
        public const float TwoPi = 2f * Pi;
        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static float Finite(float value) => IsFinite(value) ? value : 0f;
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Abs(float value) => value < 0f ? -value : value;
    }
}
