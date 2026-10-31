using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Motion.Inertialization
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DecaySample
    {
        public float Value;
        public float Velocity;
        public float Acceleration;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ScalarDecay
    {
        public float Duration;
        public float C0;
        public float C1;
        public float C2;
        public float C3;
        public float C4;
        public float C5;

        public static ScalarDecay Create(float magnitude, float radialVelocity, float seconds)
        {
            if (!Scalar.IsFinite(magnitude) || magnitude <= Scalar.MagnitudeEpsilon || !Scalar.IsFinite(seconds))
                return default;
            var velocity = Scalar.Min(Scalar.Finite(radialVelocity), 0f);
            var duration = velocity < 0f ? Scalar.Min(seconds, -5f * magnitude / velocity) : seconds;
            if (!Scalar.IsFinite(duration) || duration <= Scalar.DurationEpsilon)
                return default;
            var vt = velocity * duration;
            var at2 = Scalar.Max(0f, -8f * vt - 20f * magnitude);
            var result = new ScalarDecay
            {
                Duration = duration,
                C0 = magnitude,
                C1 = vt,
                C2 = at2 * 0.5f,
                C3 = -(3f * at2 + 12f * vt + 20f * magnitude) * 0.5f,
                C4 = (3f * at2 + 16f * vt + 30f * magnitude) * 0.5f,
                C5 = -(at2 + 6f * vt + 12f * magnitude) * 0.5f
            };
            return Scalar.IsFinite(result.C1) && Scalar.IsFinite(result.C2) && Scalar.IsFinite(result.C3) && Scalar.IsFinite(result.C4) && Scalar.IsFinite(result.C5) ? result : default;
        }

        public DecaySample Evaluate(float elapsed)
        {
            if (Duration <= Scalar.DurationEpsilon || elapsed >= Duration)
                return default;
            var t = Scalar.Max(0f, Scalar.Finite(elapsed)) / Duration;
            return new DecaySample
            {
                Value = (((((C5 * t + C4) * t + C3) * t + C2) * t + C1) * t + C0),
                Velocity = ((((5f * C5 * t + 4f * C4) * t + 3f * C3) * t + 2f * C2) * t + C1) / Duration,
                Acceleration = (((20f * C5 * t + 12f * C4) * t + 6f * C3) * t + 2f * C2) / (Duration * Duration)
            };
        }
    }
}
