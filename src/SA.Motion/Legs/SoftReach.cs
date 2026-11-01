using System;
using System.Runtime.CompilerServices;
using SA.Motion.Numerics;

namespace SA.Motion.Legs
{
    public static class SoftReach
    {
        const float BoneEpsilon = 1e-5f;
        const float AntipodeDot = -0.99999f;

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

        public static Float3 KneeDirection(Float3 hip, Float3 knee, Float3 ankle, Float3 forward, float follow) =>
            Bend(Measure(hip, knee, ankle), forward, follow);

        public static LegReach Solve(Float3 hip, Float3 knee, Float3 ankle, Float3 goal, Float3 bend, float softness, float maxStretch)
        {
            if (!hip.IsFinite || !knee.IsFinite || !ankle.IsFinite || !goal.IsFinite || !bend.IsFinite)
                return new LegReach(false, knee, ankle, Quaternion.Identity, Quaternion.Identity, 1f);
            var length = LegMath.Length(bend);
            var unitBend = length > Scalar.MagnitudeEpsilon ? bend / length : Float3.Zero;
            return Reach(Measure(hip, knee, ankle), goal, unitBend, softness, maxStretch);
        }

        internal static LegReach Leg(Float3 hip, Float3 knee, Float3 ankle, Float3 goal, Float3 forward, float follow, float softness, float maxStretch)
        {
            if (!hip.IsFinite || !knee.IsFinite || !ankle.IsFinite || !goal.IsFinite)
                return new LegReach(false, knee, ankle, Quaternion.Identity, Quaternion.Identity, 1f);
            var limb = Measure(hip, knee, ankle);
            return Reach(limb, goal, Bend(limb, forward, follow), softness, maxStretch);
        }

        static Limb Measure(Float3 hip, Float3 knee, Float3 ankle)
        {
            var upper = knee - hip;
            var lower = ankle - knee;
            var axis = ankle - hip;
            var axisLength = LegMath.Length(axis);
            return new Limb(
                hip,
                knee,
                ankle,
                upper,
                lower,
                LegMath.Length(upper),
                LegMath.Length(lower),
                Scalar.IsFinite(axisLength) && axisLength > Scalar.MagnitudeEpsilon ? axis / axisLength : new Float3(0f, -1f, 0f),
                axisLength);
        }

        static Float3 Bend(in Limb limb, Float3 forward, float follow)
        {
            var bent = limb.Upper - limb.Axis * Float3.Dot(limb.Upper, limb.Axis);
            var bentLength = LegMath.Length(bent);
            var total = limb.UpperLength + limb.LowerLength;
            var bends = Scalar.IsFinite(total) && bentLength > 1e-3f * total;
            var weight = Unit(follow);
            if (bends && weight >= 1f)
                return bent / bentLength;
            var ahead = forward - limb.Axis * Float3.Dot(forward, limb.Axis);
            var fallback = Normalize(ahead, Normalize(bent, Perpendicular(limb.Axis, new Float3(0f, 0f, 1f))));
            var animated = bends ? bent / bentLength : fallback;
            return Normalize(fallback + (animated - fallback) * weight, animated);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static LegReach Reach(in Limb limb, Float3 goal, Float3 bend, float softness, float maxStretch)
        {
            var length = limb.UpperLength + limb.LowerLength;
            if (!Scalar.IsFinite(length) || limb.UpperLength <= BoneEpsilon || limb.LowerLength <= BoneEpsilon)
                return Unchanged(limb);
            var toGoal = goal - limb.Hip;
            var distance = LegMath.Length(toGoal);
            var direction = distance > BoneEpsilon ? toGoal / distance : limb.Axis;
            var cap = Scalar.IsFinite(maxStretch) ? Scalar.Max(1f, Scalar.Min(maxStretch, 2f)) : 1f;
            var stretch = Scalar.Max(1f, Scalar.Min(distance / length, cap));
            var reach = Distance(distance, length * stretch, softness, limb.AxisLength * stretch);
            var upper = limb.UpperLength * stretch;
            var lower = limb.LowerLength * stretch;
            if (!Scalar.IsFinite(upper + lower + reach))
                return Unchanged(limb);
            var axis = reach > BoneEpsilon ? direction : limb.Axis;
            var span = Scalar.Max(Scalar.Abs(upper - lower) + BoneEpsilon, Scalar.Min(reach, upper + lower - BoneEpsilon));
            var preferred = bend.LengthSquared > 0f ? bend : Perpendicular(direction, new Float3(0f, 0f, 1f));
            var side = Sideways(limb, preferred, axis, stretch);
            var along = (upper * upper + span * span - lower * lower) / (2f * span);
            var height = MathF.Sqrt(Scalar.Max(0f, upper * upper - along * along));
            var middle = axis * along + side * height;
            var end = axis * span;
            var upperTurn = Turn(limb.Upper / limb.UpperLength, middle);
            var lowerTurn = Turn(Quaternion.Rotate(upperTurn, limb.Lower) / limb.LowerLength, end - middle);
            return new LegReach(true, limb.Hip + middle, limb.Hip + end, upperTurn, LegMath.Unit(Quaternion.Multiply(lowerTurn, upperTurn)), stretch);
        }

        static Float3 Sideways(in Limb limb, Float3 preferred, Float3 axis, float stretch)
        {
            var side = preferred - axis * Float3.Dot(preferred, axis);
            if (side.LengthSquared < 1e-10f)
            {
                var bent = limb.Upper * stretch;
                side = bent - axis * Float3.Dot(bent, axis);
            }
            return side.LengthSquared > 1e-12f ? side / LegMath.Length(side) : Perpendicular(axis, new Float3(0f, 1f, 0f));
        }

        static Quaternion Turn(Float3 from, Float3 to)
        {
            var length = LegMath.Length(to);
            if (length < Scalar.MagnitudeEpsilon)
                return Quaternion.Identity;
            var direction = to / length;
            var dot = Float3.Dot(from, direction);
            if (dot < AntipodeDot)
                return Quaternion.AxisAngle(Perpendicular(from, new Float3(0f, 1f, 0f)), Scalar.Pi);
            var cross = Float3.Cross(from, direction);
            return LegMath.Unit(new Quaternion(cross.X, cross.Y, cross.Z, 1f + dot));
        }

        static LegReach Unchanged(in Limb limb) =>
            new LegReach(false, limb.Knee, limb.Ankle, Quaternion.Identity, Quaternion.Identity, 1f);

        static Float3 Normalize(Float3 value, Float3 fallback)
        {
            var length = LegMath.Length(value);
            return Scalar.IsFinite(length) && length > Scalar.MagnitudeEpsilon ? value / length : fallback;
        }

        static Float3 Perpendicular(Float3 axis, Float3 alternative) =>
            Normalize(Float3.Cross(axis, Math.Abs(axis.X) < 0.8f ? new Float3(1f, 0f, 0f) : alternative), new Float3(0f, 0f, 1f));

        static float Unit(float value) => !Scalar.IsFinite(value) ? 0f : value < 0f ? 0f : value > 1f ? 1f : value;

        readonly struct Limb
        {
            public readonly Float3 Hip;
            public readonly Float3 Knee;
            public readonly Float3 Ankle;
            public readonly Float3 Upper;
            public readonly Float3 Lower;
            public readonly float UpperLength;
            public readonly float LowerLength;
            public readonly Float3 Axis;
            public readonly float AxisLength;

            public Limb(Float3 hip, Float3 knee, Float3 ankle, Float3 upper, Float3 lower, float upperLength, float lowerLength, Float3 axis, float axisLength)
            {
                Hip = hip;
                Knee = knee;
                Ankle = ankle;
                Upper = upper;
                Lower = lower;
                UpperLength = upperLength;
                LowerLength = lowerLength;
                Axis = axis;
                AxisLength = axisLength;
            }
        }
    }
}
