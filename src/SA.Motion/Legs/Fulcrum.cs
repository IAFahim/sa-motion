using System;
using SA.Motion.Kinematics;
using SA.Motion.Numerics;

namespace SA.Motion.Legs
{
    public static class Fulcrum
    {
        public static Float3 Pivot(in FootShape shape, float along) =>
            shape.Heel + (shape.Ball - shape.Heel) * Unit(along);

        public static float Pitch(in FootShape shape, Quaternion foot, Float3 normal)
        {
            var sole = Quaternion.Rotate(Quaternion.Normalize(foot), shape.Ball - shape.Heel);
            var length = sole.Length;
            if (!Scalar.IsFinite(length) || length <= Scalar.MagnitudeEpsilon || !normal.IsFinite)
                return 0f;
            var sine = Float3.Dot(sole / length, normal);
            return (float)Math.Asin(sine < -1f ? -1f : sine > 1f ? 1f : sine);
        }

        public static float ToeUp(float pitch, float band)
        {
            if (!Scalar.IsFinite(pitch))
                return 0.5f;
            if (!Scalar.IsFinite(band) || band <= Scalar.MagnitudeEpsilon)
                return pitch > 0f ? 1f : pitch < 0f ? 0f : 0.5f;
            var t = Unit((pitch + band) / (2f * band));
            return t * t * (3f - 2f * t);
        }

        public static Float3 Ankle(in FootShape shape, Float3 anchor, Float3 normal, Quaternion foot, float turnPivot, float heelRoll, float ballRoll, float rollBand)
        {
            foot = Quaternion.Normalize(foot);
            var pivot = Pivot(shape, turnPivot);
            var turned = anchor - Quaternion.Rotate(foot, pivot);
            var sole = Quaternion.Rotate(foot, shape.Ball - shape.Heel);
            if (!sole.IsFinite || !normal.IsFinite || !anchor.IsFinite || sole.LengthSquared <= Scalar.MagnitudeEpsilon * Scalar.MagnitudeEpsilon)
                return turned;
            var flat = sole - normal * Float3.Dot(sole, normal);
            if (flat.LengthSquared <= Scalar.MagnitudeEpsilon * Scalar.MagnitudeEpsilon)
                return turned;
            var level = Quaternion.Normalize(Quaternion.Multiply(TwoBoneIK.FromTo(sole, flat), foot));
            var heelOnGround = anchor + Quaternion.Rotate(level, shape.Heel - pivot);
            var ballOnGround = anchor + Quaternion.Rotate(level, shape.Ball - pivot);
            var onHeel = Lerp(turned, heelOnGround - Quaternion.Rotate(foot, shape.Heel), Unit(heelRoll));
            var onBall = Lerp(turned, ballOnGround - Quaternion.Rotate(foot, shape.Ball), Unit(ballRoll));
            var ankle = Lerp(onBall, onHeel, ToeUp(Pitch(shape, foot, normal), rollBand));
            return ankle.IsFinite ? ankle : turned;
        }

        public static Quaternion AlignToSlope(Quaternion foot, Float3 up, Float3 normal, float weight, float maxAngle) =>
            Tilted(Quaternion.Normalize(foot), up, normal, weight, maxAngle);

        internal static Quaternion Tilted(Quaternion foot, Float3 up, Float3 normal, float weight, float maxAngle)
        {
            if (normal.X == up.X && normal.Y == up.Y && normal.Z == up.Z)
                return foot;
            var tilt = Quaternion.Canonical(TwoBoneIK.FromTo(up, normal));
            var sine = tilt.Vector.Length;
            if (!Scalar.IsFinite(sine) || sine <= Scalar.MagnitudeEpsilon)
                return foot;
            var limit = Scalar.IsFinite(maxAngle) ? Scalar.Max(0f, maxAngle) : 0f;
            var angle = Scalar.Min(2f * (float)Math.Atan2(sine, tilt.W), limit) * Unit(weight);
            return Quaternion.Normalize(Quaternion.Multiply(Quaternion.AxisAngle(tilt.Vector / sine, angle), foot));
        }

        static Float3 Lerp(Float3 a, Float3 b, float t) => a + (b - a) * t;

        static float Unit(float value) => !Scalar.IsFinite(value) ? 0f : value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
