using System;
using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Motion.Kinematics
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct TwoBoneResult
    {
        public readonly bool Valid;
        public readonly Float3 Middle;
        public readonly Float3 End;
        public readonly Quaternion UpperDelta;
        public readonly Quaternion LowerDelta;

        public TwoBoneResult(bool valid, Float3 middle, Float3 end, Quaternion upper, Quaternion lower)
        { Valid = valid; Middle = middle; End = end; UpperDelta = upper; LowerDelta = lower; }
    }

    public static class TwoBoneIK
    {
        public static TwoBoneResult Solve(Float3 start, Float3 middle, Float3 end, Float3 target, Float3 pole)
        {
            var unchanged = new TwoBoneResult(false, middle, end, Quaternion.Identity, Quaternion.Identity);
            if (!start.IsFinite || !middle.IsFinite || !end.IsFinite || !target.IsFinite || !pole.IsFinite)
                return unchanged;
            var upperLength = (middle - start).Length;
            var lowerLength = (end - middle).Length;
            var displacement = target - start;
            var distance = displacement.Length;
            if (!Scalar.IsFinite(upperLength + lowerLength + distance) || upperLength < 1e-5f || lowerLength < 1e-5f)
                return unchanged;
            var direction = distance > 1e-5f ? displacement / distance : Normalize(end - start, new Float3(0, -1, 0));
            distance = Scalar.Max(Math.Abs(upperLength - lowerLength) + 1e-5f, Scalar.Min(distance, upperLength + lowerLength - 1e-5f));
            var bend = pole - start;
            bend -= direction * Float3.Dot(bend, direction);
            if (bend.LengthSquared < 1e-10f)
            {
                bend = middle - start;
                bend -= direction * Float3.Dot(bend, direction);
            }
            bend = Normalize(bend, Perpendicular(direction));
            var along = (upperLength * upperLength + distance * distance - lowerLength * lowerLength) / (2f * distance);
            var height = (float)Math.Sqrt(Scalar.Max(0f, upperLength * upperLength - along * along));
            var solvedMiddle = start + direction * along + bend * height;
            var solvedEnd = start + direction * distance;
            var upper = FromTo(middle - start, solvedMiddle - start);
            var lower = FromTo(Quaternion.Rotate(upper, end - middle), solvedEnd - solvedMiddle);
            return new TwoBoneResult(true, solvedMiddle, solvedEnd, upper, Quaternion.Normalize(Quaternion.Multiply(lower, upper)));
        }

        public static Quaternion FromTo(Float3 from, Float3 to)
        {
            if (!from.IsFinite || !to.IsFinite || from.LengthSquared < 1e-12f || to.LengthSquared < 1e-12f)
                return Quaternion.Identity;
            from = Normalize(from, new Float3(0, 1, 0));
            to = Normalize(to, from);
            var dot = Float3.Dot(from, to);
            if (dot < -0.99999f)
                return Quaternion.AxisAngle(Perpendicular(from), Scalar.Pi);
            var cross = Float3.Cross(from, to);
            return Quaternion.Normalize(new Quaternion(cross.X, cross.Y, cross.Z, 1f + dot));
        }

        static Float3 Normalize(Float3 value, Float3 fallback) => value.LengthSquared > 1e-12f ? value / value.Length : fallback;
        static Float3 Perpendicular(Float3 direction) => Normalize(Float3.Cross(direction, Math.Abs(direction.X) < 0.8f ? new Float3(1, 0, 0) : new Float3(0, 1, 0)), new Float3(0, 0, 1));
    }
}
