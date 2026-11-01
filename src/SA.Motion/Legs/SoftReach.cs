using System;
using SA.Motion.Kinematics;
using SA.Motion.Numerics;

namespace SA.Motion.Legs
{
    public static class SoftReach
    {
        public static float Distance(float distance, float length, float softness, float animated)
        {
            if (!Scalar.IsFinite(length) || length <= Scalar.MagnitudeEpsilon)
                return 0f;
            if (!Scalar.IsFinite(distance) || distance <= 0f)
                return 0f;
            var soft = Unit(softness) * length;
            var start = Scalar.IsFinite(animated) ? animated : 0f;
            start = start < length - soft ? length - soft : start > length ? length : start;
            if (distance <= start)
                return distance;
            var room = length - start;
            if (room <= Scalar.MagnitudeEpsilon)
                return start;
            return start + room * (1f - (float)Math.Exp(-(distance - start) / room));
        }

        public static Float3 KneeDirection(Float3 hip, Float3 knee, Float3 ankle, Float3 forward, float follow)
        {
            var axis = Normalize(ankle - hip, new Float3(0f, -1f, 0f));
            var offset = knee - hip;
            var bent = offset - axis * Float3.Dot(offset, axis);
            var ahead = forward - axis * Float3.Dot(forward, axis);
            var fallback = Normalize(ahead, Normalize(bent, Perpendicular(axis)));
            var limb = offset.Length + (ankle - knee).Length;
            var animated = Scalar.IsFinite(limb) && bent.Length > 1e-3f * limb ? bent / bent.Length : fallback;
            var weight = Unit(follow);
            return Normalize(fallback + (animated - fallback) * weight, animated);
        }

        public static LegReach Solve(Float3 hip, Float3 knee, Float3 ankle, Float3 goal, Float3 bend, float softness, float maxStretch)
        {
            var unchanged = new LegReach(false, knee, ankle, Quaternion.Identity, Quaternion.Identity, 1f);
            if (!hip.IsFinite || !knee.IsFinite || !ankle.IsFinite || !goal.IsFinite || !bend.IsFinite)
                return unchanged;
            var upper = (knee - hip).Length;
            var lower = (ankle - knee).Length;
            var length = upper + lower;
            if (!Scalar.IsFinite(length) || upper <= 1e-5f || lower <= 1e-5f)
                return unchanged;
            var toGoal = goal - hip;
            var distance = toGoal.Length;
            var direction = distance > 1e-5f ? toGoal / distance : Normalize(ankle - hip, new Float3(0f, -1f, 0f));
            var cap = Scalar.IsFinite(maxStretch) ? Scalar.Max(1f, Scalar.Min(maxStretch, 2f)) : 1f;
            var stretch = Scalar.Max(1f, Scalar.Min(distance / length, cap));
            var reach = Distance(distance, length * stretch, softness, (ankle - hip).Length * stretch);
            var stretchedKnee = hip + (knee - hip) * stretch;
            var stretchedAnkle = stretchedKnee + (ankle - knee) * stretch;
            var solved = TwoBoneIK.Solve(hip, stretchedKnee, stretchedAnkle, hip + direction * reach, hip + Normalize(bend, Perpendicular(direction)));
            return solved.Valid ? new LegReach(true, solved.Middle, solved.End, solved.UpperDelta, solved.LowerDelta, stretch) : unchanged;
        }

        static Float3 Normalize(Float3 value, Float3 fallback)
        {
            var length = value.Length;
            return Scalar.IsFinite(length) && length > Scalar.MagnitudeEpsilon ? value / length : fallback;
        }

        static Float3 Perpendicular(Float3 axis) =>
            Normalize(Float3.Cross(axis, Math.Abs(axis.X) < 0.8f ? new Float3(1f, 0f, 0f) : new Float3(0f, 0f, 1f)), new Float3(0f, 0f, 1f));

        static float Unit(float value) => !Scalar.IsFinite(value) ? 0f : value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
