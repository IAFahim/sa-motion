using System;
using SA.Motion.Numerics;

namespace SA.Animation
{
    public enum Heading : byte
    {
        Forward,
        ForwardRight,
        Right,
        BackRight,
        Back,
        BackLeft,
        Left,
        ForwardLeft
    }

    public static class MotionSelect
    {
        public const int Headings = 8;
        const float SectorAngle = Scalar.Pi / 4f;

        public static int Linear(ReadOnlySpan<float> thresholds, float value, int current, float hysteresis)
        {
            var best = -1;
            var bestDistance = float.PositiveInfinity;
            for (var i = 0; i < thresholds.Length; i++)
            {
                if (!Scalar.IsFinite(thresholds[i]))
                    continue;
                var distance = Scalar.IsFinite(value) ? Math.Abs(value - thresholds[i]) : 0f;
                if (distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }
            if (!Usable(current, thresholds.Length) || !Scalar.IsFinite(thresholds[current]))
                return best;
            if (!Scalar.IsFinite(value))
                return current;
            return Math.Abs(value - thresholds[current]) <= bestDistance + Margin(hysteresis) ? current : best;
        }

        public static int Planar(ReadOnlySpan<Float2> points, Float2 value, int current, float hysteresis)
        {
            var best = -1;
            var bestDistance = float.PositiveInfinity;
            for (var i = 0; i < points.Length; i++)
            {
                if (!points[i].IsFinite)
                    continue;
                var distance = value.IsFinite ? (value - points[i]).Length : 0f;
                if (distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }
            if (!Usable(current, points.Length) || !points[current].IsFinite)
                return best;
            if (!value.IsFinite)
                return current;
            return (value - points[current]).Length <= bestDistance + Margin(hysteresis) ? current : best;
        }

        public static int Directional(ReadOnlySpan<ClipIndex> headings, Float2 direction, int current, float hysteresis)
        {
            var count = Math.Min(headings.Length, Headings);
            var known = direction.IsFinite && direction.LengthSquared > Scalar.MagnitudeEpsilon * Scalar.MagnitudeEpsilon;
            var angle = known ? (float)Math.Atan2(direction.X, direction.Y) : 0f;
            var best = -1;
            var bestDistance = float.PositiveInfinity;
            for (var i = 0; i < count; i++)
            {
                if (headings[i].IsNone)
                    continue;
                var distance = known ? Separation(angle, i * SectorAngle) : i;
                if (distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }
            if (!Usable(current, count) || headings[current].IsNone)
                return best;
            if (!known)
                return current;
            return Separation(angle, current * SectorAngle) <= bestDistance + Margin(hysteresis) ? current : best;
        }

        public static bool Idle(float speed, bool wasIdle, float idleSpeed, float hysteresis)
        {
            if (!Scalar.IsFinite(speed))
                return wasIdle;
            var limit = Scalar.IsFinite(idleSpeed) ? idleSpeed : 0f;
            return wasIdle ? speed < limit + Margin(hysteresis) : speed < limit;
        }

        public static Float2 Planar(Float3 velocity, Quaternion facing)
        {
            var local = Quaternion.Rotate(Quaternion.InverseUnit(Quaternion.Normalize(facing)), velocity);
            return local.IsFinite ? new Float2(local.X, local.Z) : Float2.Zero;
        }

        static float Separation(float a, float b)
        {
            var difference = (a - b) % Scalar.TwoPi;
            if (difference > Scalar.Pi)
                difference -= Scalar.TwoPi;
            if (difference < -Scalar.Pi)
                difference += Scalar.TwoPi;
            return Math.Abs(difference);
        }

        static bool Usable(int index, int count) => index >= 0 && index < count;

        static float Margin(float hysteresis) => Scalar.IsFinite(hysteresis) && hysteresis > 0f ? hysteresis : 0f;
    }

    public static class PlayRate
    {
        public static float Match(float speed, float referenceSpeed, float minimum, float maximum)
        {
            var low = Scalar.IsFinite(minimum) ? minimum : 1f;
            var high = Scalar.IsFinite(maximum) ? maximum : 1f;
            if (high < low)
                high = low;
            var rate = Scalar.IsFinite(speed) && Scalar.IsFinite(referenceSpeed) && referenceSpeed > Scalar.MagnitudeEpsilon ? speed / referenceSpeed : 1f;
            return rate < low ? low : rate > high ? high : rate;
        }
    }
}
