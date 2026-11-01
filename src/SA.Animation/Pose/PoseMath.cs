using SA.Motion.Numerics;

namespace SA.Animation
{
    public static class PoseMath
    {
        public static Quaternion Align(Quaternion q, Quaternion reference) =>
            Finite(Quaternion.Dot(q, reference) < 0f ? -q : q, Quaternion.Identity);

        public static Quaternion Nlerp(Quaternion a, Quaternion b, float x)
        {
            var target = Align(b, a);
            var t = Scalar.Finite(x);
            return Quaternion.Normalize(new Quaternion(
                a.X + (target.X - a.X) * t,
                a.Y + (target.Y - a.Y) * t,
                a.Z + (target.Z - a.Z) * t,
                a.W + (target.W - a.W) * t));
        }

        public static Pose Lerp(in Pose a, in Pose b, float x)
        {
            var t = Scalar.Finite(x);
            return new Pose
            {
                Position = Finite(a.Position + (b.Position - a.Position) * t, Float3.Zero),
                Rotation = Nlerp(a.Rotation, b.Rotation, t),
                Scale = Finite(a.Scale + (b.Scale - a.Scale) * t, Float3.One)
            };
        }

        public static Pose Delta(in Pose pose, in Pose reference) => new Pose
        {
            Position = Finite(pose.Position - reference.Position, Float3.Zero),
            Rotation = Quaternion.Normalize(Quaternion.Multiply(Quaternion.InverseUnit(reference.Rotation), pose.Rotation)),
            Scale = Finite(pose.Scale - reference.Scale, Float3.Zero)
        };

        public static Pose Accumulate(in Pose pose, in Pose delta, float weight)
        {
            var w = Scalar.Finite(weight);
            return new Pose
            {
                Position = Finite(pose.Position + delta.Position * w, Float3.Zero),
                Rotation = Quaternion.Normalize(Quaternion.Multiply(pose.Rotation, Nlerp(Quaternion.Identity, delta.Rotation, w))),
                Scale = Finite(pose.Scale + delta.Scale * w, Float3.One)
            };
        }

        public static PoseSum Add(in PoseSum sum, in Pose pose, float weight)
        {
            var w = Scalar.Finite(weight);
            var anchor = sum.Weight > 0f ? sum.Rotation : pose.Rotation;
            var rotation = Quaternion.Dot(pose.Rotation, anchor) < 0f ? -pose.Rotation : pose.Rotation;
            return new PoseSum
            {
                Position = Finite(sum.Position + pose.Position * w, Float3.Zero),
                Rotation = Finite(new Quaternion(
                    sum.Rotation.X + rotation.X * w,
                    sum.Rotation.Y + rotation.Y * w,
                    sum.Rotation.Z + rotation.Z * w,
                    sum.Rotation.W + rotation.W * w), default),
                Scale = Finite(sum.Scale + pose.Scale * w, Float3.Zero),
                Weight = Scalar.Finite(sum.Weight + w)
            };
        }

        public static Pose Mean(in PoseSum sum)
        {
            var inverse = sum.Weight > 0f ? 1f / sum.Weight : 0f;
            return new Pose
            {
                Position = Finite(sum.Position * inverse, Float3.Zero),
                Rotation = Quaternion.Normalize(sum.Rotation),
                Scale = Finite(sum.Scale * inverse, Float3.One)
            };
        }

        public static Affine Affine(in Pose pose)
        {
            var q = Finite(pose.Rotation, Quaternion.Identity);
            var scale = Finite(pose.Scale, Float3.One);
            var x2 = q.X + q.X;
            var y2 = q.Y + q.Y;
            var z2 = q.Z + q.Z;
            return Guarded(new Affine(
                new Float3(1f - q.Y * y2 - q.Z * z2, q.X * y2 + q.W * z2, q.X * z2 - q.W * y2) * scale.X,
                new Float3(q.X * y2 - q.W * z2, 1f - q.X * x2 - q.Z * z2, q.Y * z2 + q.W * x2) * scale.Y,
                new Float3(q.X * z2 + q.W * y2, q.Y * z2 - q.W * x2, 1f - q.X * x2 - q.Y * y2) * scale.Z,
                Finite(pose.Position, Float3.Zero)));
        }

        public static Affine Mul(in Affine a, in Affine b) => Guarded(new Affine(
            Linear(a, b.C0),
            Linear(a, b.C1),
            Linear(a, b.C2),
            Linear(a, b.C3) + a.C3));

        internal static bool Positive(float value) => value > 0f && value <= float.MaxValue;

        static Float3 Linear(in Affine matrix, Float3 vector) =>
            matrix.C0 * vector.X + matrix.C1 * vector.Y + matrix.C2 * vector.Z;

        static Affine Guarded(Affine matrix) => new Affine(
            Finite(matrix.C0, Float3.Zero),
            Finite(matrix.C1, Float3.Zero),
            Finite(matrix.C2, Float3.Zero),
            Finite(matrix.C3, Float3.Zero));

        static Float3 Finite(Float3 value, Float3 fallback) => value.IsFinite ? value : fallback;

        static Quaternion Finite(Quaternion value, Quaternion fallback) =>
            Scalar.IsFinite(Quaternion.Dot(value, value)) ? value : fallback;
    }
}
