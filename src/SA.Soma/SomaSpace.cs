using System;
using SA.Animation;
using SA.Motion.Numerics;

namespace SA.Soma
{
    public enum SomaSpace : byte { ModelYUp, RuntimeZUp }

    public static class SomaMirror
    {
        public static Float3 Position(Float3 p) => new Float3(-p.X, p.Y, p.Z);
        public static Quaternion Rotation(Quaternion q) => new Quaternion(q.X, -q.Y, -q.Z, q.W);
        public static Pose Local(in Pose pose) => new Pose { Position = Position(pose.Position), Rotation = Rotation(pose.Rotation), Scale = Float3.One };
    }

    public static class SomaAxes
    {
        public static Quaternion RuntimeZUpToModel => Quaternion.AxisAngle(new Float3(1f, 0f, 0f), -0.5f * Scalar.Pi);

        public static Pose RootToModel(in Pose root, SomaSpace space)
        {
            if (space != SomaSpace.RuntimeZUp) return root;
            var turn = RuntimeZUpToModel;
            return new Pose { Position = Quaternion.Rotate(turn, root.Position), Rotation = Quaternion.Normalize(Quaternion.Multiply(turn, root.Rotation)), Scale = Float3.One };
        }

        public static float Yaw(Quaternion hipsRotation, Float3 leftLeg, Float3 rightLeg, float fallback)
        {
            var right = Quaternion.Rotate(hipsRotation, rightLeg - leftLeg);
            var forward = Float3.Cross(right, new Float3(0f, 1f, 0f));
            if (!forward.IsFinite || forward.X * forward.X + forward.Z * forward.Z < 1e-10f) return fallback;
            return (float)Math.Atan2(forward.X, forward.Z);
        }

        public static Quaternion YawRotation(float yaw) => Quaternion.AxisAngle(new Float3(0f, 1f, 0f), yaw);
    }
}
