using System;
using System.Runtime.InteropServices;

namespace SA.Motion.Numerics
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Float2
    {
        public float X;
        public float Y;

        public Float2(float x, float y) { X = x; Y = y; }
        public static Float2 Zero => default;
        public static Float2 operator +(Float2 a, Float2 b) => new Float2(a.X + b.X, a.Y + b.Y);
        public static Float2 operator -(Float2 a, Float2 b) => new Float2(a.X - b.X, a.Y - b.Y);
        public static Float2 operator *(Float2 a, float b) => new Float2(a.X * b, a.Y * b);
        public static float Dot(Float2 a, Float2 b) => a.X * b.X + a.Y * b.Y;
        public float LengthSquared => Dot(this, this);
        public float Length => (float)Math.Sqrt(LengthSquared);
        public bool IsFinite => Scalar.IsFinite(X) && Scalar.IsFinite(Y);
    }
}
