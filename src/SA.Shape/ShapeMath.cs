using System;
using SA.Animation;
using SA.Motion.Mechanics;
using SA.Motion.Numerics;

namespace SA.Shape
{
    internal static class ShapeMath
    {
        internal static bool Finite(in Affine value) => value.C0.IsFinite && value.C1.IsFinite && value.C2.IsFinite && value.C3.IsFinite;
        internal static Float3 Vector(in Affine value, Float3 point) => value.C0 * point.X + value.C1 * point.Y + value.C2 * point.Z;
        internal static Float3 Point(in Affine value, Float3 point) => Vector(value, point) + value.C3;
        internal static Affine Multiply(in Affine a, in Affine b) => new Affine(Vector(a, b.C0), Vector(a, b.C1), Vector(a, b.C2), Vector(a, b.C3) + a.C3);
        internal static bool Identity(in Affine value) => value.C0.X == 1f && value.C0.Y == 0f && value.C0.Z == 0f
            && value.C1.X == 0f && value.C1.Y == 1f && value.C1.Z == 0f && value.C2.X == 0f && value.C2.Y == 0f && value.C2.Z == 1f
            && value.C3.X == 0f && value.C3.Y == 0f && value.C3.Z == 0f;

        internal static bool Deform(in SkinFrame frame, Float3 scale, out Affine deformation)
        {
            deformation = Affine.Identity;
            if (!RigidMath.UnitRotation(frame.Basis) || !frame.Pivot.IsFinite || !scale.IsPositive) return false;
            if (scale.X == 1f && scale.Y == 1f && scale.Z == 1f) return true;
            var inverse = Quaternion.InverseUnit(Quaternion.Normalize(frame.Basis));
            var q = Quaternion.Normalize(frame.Basis);
            Float3 Stretch(Float3 direction)
            {
                var local = Quaternion.Rotate(inverse, direction);
                return Quaternion.Rotate(q, new Float3(local.X * scale.X, local.Y * scale.Y, local.Z * scale.Z));
            }
            var value = new Affine(Stretch(new Float3(1f, 0f, 0f)), Stretch(new Float3(0f, 1f, 0f)), Stretch(new Float3(0f, 0f, 1f)), Float3.Zero);
            value.C3 = frame.Pivot - Vector(value, frame.Pivot);
            if (!Finite(value)) return false;
            deformation = value;
            return true;
        }

        internal static bool Normal(in Affine matrix, out Float3x3 normal)
        {
            normal = default;
            var x = Float3.Cross(matrix.C1, matrix.C2);
            var y = Float3.Cross(matrix.C2, matrix.C0);
            var z = Float3.Cross(matrix.C0, matrix.C1);
            var determinant = Float3.Dot(matrix.C0, x);
            if (!Scalar.IsFinite(determinant) || determinant == 0f) return false;
            x /= determinant; y /= determinant; z /= determinant;
            if (!x.IsFinite || !y.IsFinite || !z.IsFinite) return false;
            normal = new Float3x3(x, y, z);
            return true;
        }

        internal static AnimationError Model(ReadOnlySpan<RetargetRow> rows, ReadOnlySpan<Float3> offsets, Span<RigidPose> models)
        {
            if (models.Length < rows.Length || offsets.Length != 0 && offsets.Length != rows.Length) return AnimationError.CapacityExceeded;
            for (var i = 0; i < rows.Length; i++)
            {
                var parent = rows[i].Parent;
                if (parent >= i || parent < (i == 0 ? -1 : 0) || !RigidMath.UnitPose(rows[i].Calibration)) return AnimationError.InvalidTable;
                var local = new RigidPose(offsets.Length == 0 ? rows[i].Calibration.Position : offsets[i], Quaternion.Normalize(rows[i].Calibration.Rotation));
                if (!local.IsValid) return AnimationError.InvalidTable;
                if (parent >= 0 && !RigidMath.TryCompose(models[parent], local, out local)) return AnimationError.InvalidTable;
                models[i] = local;
            }
            return AnimationError.None;
        }

        internal static AnimationError Model(ReadOnlySpan<RetargetRow> rows, ReadOnlySpan<Pose> local, Span<RigidPose> models)
        {
            if (rows.Length != local.Length || models.Length < rows.Length) return AnimationError.CapacityExceeded;
            for (var i = 0; i < rows.Length; i++)
            {
                var parent = rows[i].Parent;
                if (parent >= i || parent < (i == 0 ? -1 : 0) || !RigidMath.UnitPose(local[i])) return AnimationError.InvalidTable;
                var pose = new RigidPose(local[i].Position, Quaternion.Normalize(local[i].Rotation));
                if (parent >= 0 && !RigidMath.TryCompose(models[parent], pose, out pose)) return AnimationError.InvalidTable;
                models[i] = pose;
            }
            return AnimationError.None;
        }

        internal static bool Descendant(ReadOnlySpan<RetargetRow> rows, int bone, int ancestor)
        {
            if (bone < 0 || bone >= rows.Length || ancestor < 0 || ancestor >= rows.Length) return false;
            for (var i = bone; i >= 0; i = rows[i].Parent)
            {
                if (i == ancestor) return true;
                if (rows[i].Parent >= i || rows[i].Parent < -1) return false;
            }
            return false;
        }
    }
}
