using SA.Motion.Numerics;

namespace SA.Motion.Mechanics
{
    public static class Inertia
    {
        public static Float3x3 ToWorld(Float3 principal, Quaternion orientation) =>
            Sandwich(RotationMatrix(SanitizeRotation(orientation)), SanitizePrincipal(principal));

        public static Float3x3 InverseToWorld(Float3 principal, Quaternion orientation) =>
            Sandwich(
                RotationMatrix(SanitizeRotation(orientation)),
                new Float3(Inverse(principal.X), Inverse(principal.Y), Inverse(principal.Z)));

        static Float3x3 Sandwich(Float3x3 basis, Float3 diagonal)
        {
            var a = basis.C0 * diagonal.X;
            var b = basis.C1 * diagonal.Y;
            var c = basis.C2 * diagonal.Z;
            return new Float3x3(
                a * basis.C0.X + b * basis.C1.X + c * basis.C2.X,
                a * basis.C0.Y + b * basis.C1.Y + c * basis.C2.Y,
                a * basis.C0.Z + b * basis.C1.Z + c * basis.C2.Z);
        }

        static float Inverse(float principal) =>
            Scalar.IsFinite(principal) && principal > Scalar.MagnitudeEpsilon ? 1f / principal : 0f;

        static Float3 SanitizePrincipal(Float3 principal) => new Float3(
            Scalar.IsFinite(principal.X) ? principal.X : 0f,
            Scalar.IsFinite(principal.Y) ? principal.Y : 0f,
            Scalar.IsFinite(principal.Z) ? principal.Z : 0f);

        static Quaternion SanitizeRotation(Quaternion rotation) =>
            rotation.IsValidRotation ? Quaternion.Normalize(rotation) : Quaternion.Identity;

        static Float3x3 RotationMatrix(Quaternion q)
        {
            var xx = q.X * q.X;
            var xy = q.X * q.Y;
            var xz = q.X * q.Z;
            var xw = q.X * q.W;
            var yy = q.Y * q.Y;
            var yz = q.Y * q.Z;
            var yw = q.Y * q.W;
            var zz = q.Z * q.Z;
            var zw = q.Z * q.W;
            return new Float3x3(
                new Float3(1f - 2f * (yy + zz), 2f * (xy + zw), 2f * (xz - yw)),
                new Float3(2f * (xy - zw), 1f - 2f * (xx + zz), 2f * (yz + xw)),
                new Float3(2f * (xz + yw), 2f * (yz - xw), 1f - 2f * (xx + yy)));
        }
    }
}
