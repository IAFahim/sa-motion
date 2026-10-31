using System;
using SA.Motion.Numerics;

namespace SA.Humanoid.Hinge
{
    public static class HingeInference
    {
        public const int MinSamples = 3;
        public const float MinMotion = 0.05f;
        public const float MinFit = 0.8f;

        public static HingeEstimate Estimate(Quaternion rest, ReadOnlySpan<Quaternion> relativeSamples)
        {
            var fallback = new Float3(1f, 0f, 0f);
            if (!rest.IsValidRotation)
                return new HingeEstimate(fallback, 0f, 0f, 0f, false);
            int count = relativeSamples.Length;
            if (count < MinSamples)
                return new HingeEstimate(fallback, 0f, 0f, 0f, false);
            for (int i = 0; i < count; i++)
            {
                if (!relativeSamples[i].IsValidRotation)
                    return new HingeEstimate(fallback, 0f, 0f, 0f, false);
            }
            var inverse = Quaternion.InverseUnit(Quaternion.Normalize(rest));
            float cxx = 0f;
            float cyy = 0f;
            float czz = 0f;
            float cxy = 0f;
            float cxz = 0f;
            float cyz = 0f;
            float energy = 0f;
            for (int i = 0; i < count; i++)
            {
                var r = RotationVector(relativeSamples[i], inverse);
                cxx += r.X * r.X;
                cyy += r.Y * r.Y;
                czz += r.Z * r.Z;
                cxy += r.X * r.Y;
                cxz += r.X * r.Z;
                cyz += r.Y * r.Z;
                energy += r.X * r.X + r.Y * r.Y + r.Z * r.Z;
            }
            if (!Scalar.IsFinite(energy) || energy <= 0f)
                return new HingeEstimate(fallback, 0f, 0f, 0f, false);
            var axis = DominantAxis(cxx, cyy, czz, cxy, cxz, cyz);
            float min = float.MaxValue;
            float max = float.MinValue;
            float aligned = 0f;
            for (int i = 0; i < count; i++)
            {
                float angle = Float3.Dot(RotationVector(relativeSamples[i], inverse), axis);
                if (angle < min)
                    min = angle;
                if (angle > max)
                    max = angle;
                aligned += angle * angle;
            }
            float fit = aligned / energy;
            if (fit < 0f)
                fit = 0f;
            if (fit > 1f)
                fit = 1f;
            float motion = max - min;
            bool valid = Scalar.IsFinite(min)
                && Scalar.IsFinite(max)
                && Scalar.IsFinite(fit)
                && motion >= MinMotion
                && fit >= MinFit;
            return new HingeEstimate(axis, Scalar.Finite(min), Scalar.Finite(max), Scalar.Finite(fit), valid);
        }

        private static Float3 RotationVector(Quaternion sample, Quaternion inverseRest)
        {
            var delta = Quaternion.Multiply(Quaternion.Normalize(sample), inverseRest);
            if (delta.W < 0f)
                delta = -delta;
            float w = delta.W;
            if (w > 1f)
                w = 1f;
            float angle = 2f * (float)Math.Acos(w);
            float spread = (float)Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z);
            if (!(spread > 1e-9f))
                return Float3.Zero;
            float scale = angle / spread;
            return new Float3(delta.X * scale, delta.Y * scale, delta.Z * scale);
        }

        private static Float3 DominantAxis(float cxx, float cyy, float czz, float cxy, float cxz, float cyz)
        {
            Float3 axis;
            if (cxx >= cyy && cxx >= czz)
                axis = new Float3(1f, 0f, 0f);
            else if (cyy >= czz)
                axis = new Float3(0f, 1f, 0f);
            else
                axis = new Float3(0f, 0f, 1f);
            for (int step = 0; step < 32; step++)
            {
                var next = new Float3(
                    cxx * axis.X + cxy * axis.Y + cxz * axis.Z,
                    cxy * axis.X + cyy * axis.Y + cyz * axis.Z,
                    cxz * axis.X + cyz * axis.Y + czz * axis.Z);
                float length = next.Length;
                if (!(length > 1e-12f))
                    break;
                axis = next / length;
            }
            float size = axis.Length;
            if (!(size > 1e-12f))
                return new Float3(1f, 0f, 0f);
            return axis / size;
        }
    }
}
