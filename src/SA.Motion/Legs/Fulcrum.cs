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
            return Ankle(anchor, normal, Quaternion.Rotate(foot, shape.Heel), Quaternion.Rotate(foot, shape.Ball), turnPivot, heelRoll, ballRoll, rollBand);
        }

        internal static Float3 Ankle(Float3 anchor, Float3 normal, Float3 heelArm, Float3 ballArm, float turnPivot, float heelRoll, float ballRoll, float rollBand)
        {
            var along = Unit(turnPivot);
            var sole = ballArm - heelArm;
            var turned = anchor - (heelArm + sole * along);
            var soleSquared = Float3.Dot(sole, sole);
            if (!(soleSquared > Scalar.MagnitudeEpsilon * Scalar.MagnitudeEpsilon))
                return turned;
            var soleHeight = Float3.Dot(sole, normal);
            var flat = sole - normal * soleHeight;
            var flatSquared = Float3.Dot(flat, flat);
            if (!(flatSquared > Scalar.MagnitudeEpsilon * Scalar.MagnitudeEpsilon))
                return turned;
            var soleLength = MathF.Sqrt(soleSquared);
            var pitch = (float)Math.Asin(Scalar.Max(-1f, Scalar.Min(1f, soleHeight / soleLength)));
            var heelShare = ToeUp(pitch, rollBand);
            var rolled = (1f - heelShare) * Unit(ballRoll) * (1f - along) - heelShare * Unit(heelRoll) * along;
            var level = flat * (soleLength / MathF.Sqrt(flatSquared));
            var ankle = turned + (level - sole) * rolled;
            return ankle.IsFinite ? ankle : turned;
        }

        public static Quaternion AlignToSlope(Quaternion foot, Float3 up, Float3 normal, float weight, float maxAngle) =>
            Tilted(Quaternion.Normalize(foot), up, normal, weight, maxAngle);

        internal static Quaternion Tilted(Quaternion foot, Float3 up, Float3 normal, float weight, float maxAngle)
        {
            if (normal.X == up.X && normal.Y == up.Y && normal.Z == up.Z)
                return foot;
            var tilt = Quaternion.Canonical(TwoBoneIK.FromTo(up, normal));
            var sine = LegMath.Length(tilt.Vector);
            if (!Scalar.IsFinite(sine) || sine <= Scalar.MagnitudeEpsilon)
                return foot;
            var limit = Scalar.IsFinite(maxAngle) ? Scalar.Max(0f, maxAngle) : 0f;
            var angle = Scalar.Min(2f * (float)Math.Atan2(sine, tilt.W), limit) * Unit(weight);
            return LegMath.Unit(Quaternion.Multiply(Quaternion.AxisAngle(tilt.Vector / sine, angle), foot));
        }

        static float Unit(float value) => !Scalar.IsFinite(value) ? 0f : value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
