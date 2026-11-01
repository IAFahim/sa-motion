using System;
using SA.Motion.Numerics;

namespace SA.Animation
{
    public static class Phase
    {
        public static float Shape(float phase, Wrap wrap)
        {
            var value = Scalar.Finite(phase);
            return wrap switch
            {
                Wrap.Loop => Fraction(value),
                Wrap.Clamp => Unit(value),
                _ => 1f - Math.Abs(2f * Fraction(value * 0.5f) - 1f)
            };
        }

        public static float Wrapped(float phase, Wrap wrap)
        {
            var value = Scalar.Finite(phase);
            return wrap switch
            {
                Wrap.Loop => Fraction(value),
                Wrap.Clamp => Unit(value),
                _ => value - 2f * (float)Math.Floor(value * 0.5f)
            };
        }

        public static float Frame(float phase, Wrap wrap, int frameCount) => Shape(phase, wrap) * Math.Max(0, frameCount - 1);

        static float Fraction(float value) => value - (float)Math.Floor(value);

        static float Unit(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
