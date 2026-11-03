using System;
using SA.Animation;
using SA.Motion.Mechanics;
using SA.Motion.Numerics;

namespace SA.Shape
{
    public enum ShapeMassPolicy : byte
    {
        FixedMass = 0,
        FixedDensity = 1
    }

    public static class ShapeInertia
    {
        public static AnimationError Transform(float mass, Float3 center, in Float3x3 inertia, in Affine deformation, ShapeMassPolicy policy,
            out float massOut, out Float3 centerOut, out Float3x3 inertiaOut)
        {
            massOut = 0f;
            centerOut = default;
            inertiaOut = default;
            if (!Scalar.IsFinite(mass) || mass <= 0f || !center.IsFinite || !Finite(inertia) || !Symmetric(inertia))
                return AnimationError.InvalidTable;
            if (policy != ShapeMassPolicy.FixedMass && policy != ShapeMassPolicy.FixedDensity)
                return AnimationError.InvalidTable;
            if (!ShapeMath.Finite(deformation))
                return AnimationError.InvalidTable;
            double c0x = deformation.C0.X;
            double c0y = deformation.C0.Y;
            double c0z = deformation.C0.Z;
            double c1x = deformation.C1.X;
            double c1y = deformation.C1.Y;
            double c1z = deformation.C1.Z;
            double c2x = deformation.C2.X;
            double c2y = deformation.C2.Y;
            double c2z = deformation.C2.Z;
            double ex = c1y * c2z - c1z * c2y;
            double ey = c1z * c2x - c1x * c2z;
            double ez = c1x * c2y - c1y * c2x;
            double determinant = c0x * ex + c0y * ey + c0z * ez;
            if (!DFinite(determinant) || determinant == 0.0)
                return AnimationError.UnsupportedScale;
            double scaled = mass;
            if (policy == ShapeMassPolicy.FixedDensity)
            {
                if (determinant <= 0.0)
                    return AnimationError.UnsupportedScale;
                scaled = (double)mass * determinant;
            }
            if (!DFinite(scaled) || scaled <= 0.0 || scaled > (double)float.MaxValue)
                return AnimationError.UnsupportedScale;
            double i00 = inertia.C0.X;
            double i11 = inertia.C1.Y;
            double i22 = inertia.C2.Z;
            double i01 = 0.5 * ((double)inertia.C0.Y + (double)inertia.C1.X);
            double i02 = 0.5 * ((double)inertia.C0.Z + (double)inertia.C2.X);
            double i12 = 0.5 * ((double)inertia.C1.Z + (double)inertia.C2.Y);
            double trace = i00 + i11 + i22;
            double half = 0.5 * trace;
            double inverse = 1.0 / (double)mass;
            double q00 = (half - i00) * inverse;
            double q11 = (half - i11) * inverse;
            double q22 = (half - i22) * inverse;
            double q01 = -i01 * inverse;
            double q02 = -i02 * inverse;
            double q12 = -i12 * inverse;
            double m00 = c0x * q00 + c1x * q01 + c2x * q02;
            double m01 = c0x * q01 + c1x * q11 + c2x * q12;
            double m02 = c0x * q02 + c1x * q12 + c2x * q22;
            double m10 = c0y * q00 + c1y * q01 + c2y * q02;
            double m11 = c0y * q01 + c1y * q11 + c2y * q12;
            double m12 = c0y * q02 + c1y * q12 + c2y * q22;
            double m20 = c0z * q00 + c1z * q01 + c2z * q02;
            double m21 = c0z * q01 + c1z * q11 + c2z * q12;
            double m22 = c0z * q02 + c1z * q12 + c2z * q22;
            double p00 = m00 * c0x + m01 * c1x + m02 * c2x;
            double p01 = m00 * c0y + m01 * c1y + m02 * c2y;
            double p02 = m00 * c0z + m01 * c1z + m02 * c2z;
            double p11 = m10 * c0y + m11 * c1y + m12 * c2y;
            double p12 = m10 * c0z + m11 * c1z + m12 * c2z;
            double p22 = m20 * c0z + m21 * c1z + m22 * c2z;
            double traceP = p00 + p11 + p22;
            double d00 = scaled * (traceP - p00);
            double d11 = scaled * (traceP - p11);
            double d22 = scaled * (traceP - p22);
            double d01 = -scaled * p01;
            double d02 = -scaled * p02;
            double d12 = -scaled * p12;
            double cx = c0x * center.X + c1x * center.Y + c2x * center.Z + deformation.C3.X;
            double cy = c0y * center.X + c1y * center.Y + c2y * center.Z + deformation.C3.Y;
            double cz = c0z * center.X + c1z * center.Y + c2z * center.Z + deformation.C3.Z;
            if (!DFinite(cx) || !DFinite(cy) || !DFinite(cz) || Math.Abs(cx) > (double)float.MaxValue || Math.Abs(cy) > (double)float.MaxValue || Math.Abs(cz) > (double)float.MaxValue)
                return AnimationError.UnsupportedScale;
            if (!DFinite(d00) || !DFinite(d11) || !DFinite(d22) || !DFinite(d01) || !DFinite(d02) || !DFinite(d12)
                || Math.Abs(d00) > (double)float.MaxValue || Math.Abs(d11) > (double)float.MaxValue || Math.Abs(d22) > (double)float.MaxValue
                || Math.Abs(d01) > (double)float.MaxValue || Math.Abs(d02) > (double)float.MaxValue || Math.Abs(d12) > (double)float.MaxValue)
                return AnimationError.UnsupportedScale;
            massOut = (float)scaled;
            centerOut = new Float3((float)cx, (float)cy, (float)cz);
            inertiaOut = new Float3x3(
                new Float3((float)d00, (float)d01, (float)d02),
                new Float3((float)d01, (float)d11, (float)d12),
                new Float3((float)d02, (float)d12, (float)d22));
            return AnimationError.None;
        }

        public static AnimationError PrincipalAxes(in Float3x3 inertia, out Float3 moments, out Quaternion rotation)
        {
            moments = default;
            rotation = Quaternion.Identity;
            if (!Finite(inertia) || !Symmetric(inertia))
                return AnimationError.InvalidTable;
            Span<double> m = stackalloc double[9];
            Span<double> v = stackalloc double[9];
            m[0] = inertia.C0.X;
            m[4] = inertia.C1.Y;
            m[8] = inertia.C2.Z;
            m[1] = 0.5 * ((double)inertia.C0.Y + (double)inertia.C1.X);
            m[3] = m[1];
            m[2] = 0.5 * ((double)inertia.C0.Z + (double)inertia.C2.X);
            m[6] = m[2];
            m[5] = 0.5 * ((double)inertia.C1.Z + (double)inertia.C2.Y);
            m[7] = m[5];
            v[0] = 1.0;
            v[4] = 1.0;
            v[8] = 1.0;
            for (var sweep = 0; sweep < 12; sweep++)
            {
                Pair(m, v, 0, 1);
                Pair(m, v, 0, 2);
                Pair(m, v, 1, 2);
            }
            double off = Math.Abs(m[1]) + Math.Abs(m[2]) + Math.Abs(m[5]);
            double width = 1.0 + Math.Abs(m[0]) + Math.Abs(m[4]) + Math.Abs(m[8]);
            if (!DFinite(m[0]) || !DFinite(m[4]) || !DFinite(m[8]) || off > 1e-10 * width)
                return AnimationError.InvalidTable;
            if (m[4] > m[0])
                Swap(m, v, 0, 1);
            if (m[8] > m[0])
                Swap(m, v, 0, 2);
            if (m[8] > m[4])
                Swap(m, v, 1, 2);
            double e0x = v[0];
            double e0y = v[3];
            double e0z = v[6];
            double e1x = v[1];
            double e1y = v[4];
            double e1z = v[7];
            double e2x = v[2];
            double e2y = v[5];
            double e2z = v[8];
            double handedness = e0x * (e1y * e2z - e1z * e2y) + e0y * (e1z * e2x - e1x * e2z) + e0z * (e1x * e2y - e1y * e2x);
            if (!DFinite(handedness) || handedness == 0.0)
                return AnimationError.InvalidTable;
            if (handedness < 0.0)
            {
                v[2] = -v[2];
                v[5] = -v[5];
                v[8] = -v[8];
            }
            double d0 = m[0];
            double d1 = m[4];
            double d2 = m[8];
            if (d2 < -1e-6 * (1.0 + Math.Abs(d0)))
                return AnimationError.InvalidTable;
            if (!DFinite(d0) || !DFinite(d1) || !DFinite(d2)
                || Math.Abs(d0) > (double)float.MaxValue || Math.Abs(d1) > (double)float.MaxValue || Math.Abs(d2) > (double)float.MaxValue)
                return AnimationError.InvalidTable;
            double r00 = v[0];
            double r01 = v[1];
            double r02 = v[2];
            double r10 = v[3];
            double r11 = v[4];
            double r12 = v[5];
            double r20 = v[6];
            double r21 = v[7];
            double r22 = v[8];
            double qx;
            double qy;
            double qz;
            double qw;
            double trace = r00 + r11 + r22;
            if (trace > 0.0)
            {
                double s = Math.Sqrt(trace + 1.0) * 2.0;
                qw = 0.25 * s;
                qx = (r21 - r12) / s;
                qy = (r02 - r20) / s;
                qz = (r10 - r01) / s;
            }
            else if (r00 > r11 && r00 > r22)
            {
                double s = Math.Sqrt(1.0 + r00 - r11 - r22) * 2.0;
                qw = (r21 - r12) / s;
                qx = 0.25 * s;
                qy = (r01 + r10) / s;
                qz = (r02 + r20) / s;
            }
            else if (r11 > r22)
            {
                double s = Math.Sqrt(1.0 + r11 - r00 - r22) * 2.0;
                qw = (r02 - r20) / s;
                qx = (r01 + r10) / s;
                qy = 0.25 * s;
                qz = (r12 + r21) / s;
            }
            else
            {
                double s = Math.Sqrt(1.0 + r22 - r00 - r11) * 2.0;
                qw = (r10 - r01) / s;
                qx = (r02 + r20) / s;
                qy = (r12 + r21) / s;
                qz = 0.25 * s;
            }
            var candidate = new Quaternion((float)qx, (float)qy, (float)qz, (float)qw);
            if (!candidate.IsValidRotation)
                return AnimationError.InvalidTable;
            rotation = Quaternion.Canonical(candidate);
            moments = new Float3((float)d0, (float)d1, (float)d2);
            return AnimationError.None;
        }

        static void Pair(Span<double> m, Span<double> v, int p, int q)
        {
            double apq = m[p * 3 + q];
            if (apq == 0.0)
                return;
            double app = m[p * 3 + p];
            double aqq = m[q * 3 + q];
            double tau = (aqq - app) / (2.0 * apq);
            double t = (tau >= 0.0 ? 1.0 : -1.0) / (Math.Abs(tau) + Math.Sqrt(1.0 + tau * tau));
            double c = 1.0 / Math.Sqrt(1.0 + t * t);
            double s = c * t;
            m[p * 3 + p] = c * c * app - 2.0 * s * c * apq + s * s * aqq;
            m[q * 3 + q] = s * s * app + 2.0 * s * c * apq + c * c * aqq;
            m[p * 3 + q] = 0.0;
            m[q * 3 + p] = 0.0;
            for (var r = 0; r < 3; r++)
            {
                if (r == p || r == q)
                    continue;
                double arp = m[r * 3 + p];
                double arq = m[r * 3 + q];
                m[r * 3 + p] = c * arp - s * arq;
                m[p * 3 + r] = c * arp - s * arq;
                m[r * 3 + q] = s * arp + c * arq;
                m[q * 3 + r] = s * arp + c * arq;
            }
            for (var r = 0; r < 3; r++)
            {
                double vrp = v[r * 3 + p];
                double vrq = v[r * 3 + q];
                v[r * 3 + p] = c * vrp - s * vrq;
                v[r * 3 + q] = s * vrp + c * vrq;
            }
        }

        static void Swap(Span<double> m, Span<double> v, int a, int b)
        {
            double d = m[a * 3 + a];
            m[a * 3 + a] = m[b * 3 + b];
            m[b * 3 + b] = d;
            for (var r = 0; r < 3; r++)
            {
                double x = v[r * 3 + a];
                v[r * 3 + a] = v[r * 3 + b];
                v[r * 3 + b] = x;
            }
        }

        static bool Finite(in Float3x3 value) => value.C0.IsFinite && value.C1.IsFinite && value.C2.IsFinite;

        static bool Symmetric(in Float3x3 value)
        {
            double width = Math.Abs((double)value.C0.X) + Math.Abs((double)value.C1.Y) + Math.Abs((double)value.C2.Z);
            double tolerance = 1e-5 * (1.0 + width);
            return Math.Abs((double)value.C0.Y - (double)value.C1.X) <= tolerance
                && Math.Abs((double)value.C0.Z - (double)value.C2.X) <= tolerance
                && Math.Abs((double)value.C1.Z - (double)value.C2.Y) <= tolerance;
        }

        static bool DFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
