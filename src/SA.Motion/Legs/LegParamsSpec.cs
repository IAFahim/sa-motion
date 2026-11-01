using SA.Motion.Numerics;
using SA.Motion.Tuning;

namespace SA.Motion.Legs
{
    public enum LegParam : byte
    {
        Weight,
        WeightHalfLife,
        PlantHeight,
        ReleaseHeight,
        PlantRise,
        ReleaseRise,
        PlantSpeed,
        PinRadius,
        ReleaseDistance,
        BlendSeconds,
        TurnPivot,
        HeelRoll,
        BallRoll,
        RollBand,
        SlopeAlign,
        MaxSlopeAlign,
        MaxWalkableSlope,
        TerrainFollow,
        TerrainHalfLife,
        MaxStepHeight,
        Softness,
        MaxStretch,
        KneeFollow,
        PelvisWeight,
        PelvisDrop,
        PelvisRaise,
        PelvisHalfLife
    }

    public static class LegParamsSpec
    {
        public const int Count = (int)LegParam.PelvisHalfLife + 1;

        public static ParamBounds Bounds(LegParam field) => field switch
        {
            LegParam.Weight => new ParamBounds(ParamUnit.Ratio, 0f, 1f),
            LegParam.WeightHalfLife => new ParamBounds(ParamUnit.Seconds, 0f, 1f),
            LegParam.PlantHeight => new ParamBounds(ParamUnit.Meters, 0f, 0.3f),
            LegParam.ReleaseHeight => new ParamBounds(ParamUnit.Meters, 0f, 0.5f),
            LegParam.PlantRise => new ParamBounds(ParamUnit.MetersPerSecond, 0f, 5f),
            LegParam.ReleaseRise => new ParamBounds(ParamUnit.MetersPerSecond, 0f, 10f),
            LegParam.PlantSpeed => new ParamBounds(ParamUnit.MetersPerSecond, 0f, 20f),
            LegParam.PinRadius => new ParamBounds(ParamUnit.Meters, 0f, 1f),
            LegParam.ReleaseDistance => new ParamBounds(ParamUnit.Meters, 0f, 2f),
            LegParam.BlendSeconds => new ParamBounds(ParamUnit.Seconds, 0f, 1f),
            LegParam.TurnPivot => new ParamBounds(ParamUnit.Ratio, 0f, 1f),
            LegParam.HeelRoll => new ParamBounds(ParamUnit.Ratio, 0f, 1f),
            LegParam.BallRoll => new ParamBounds(ParamUnit.Ratio, 0f, 1f),
            LegParam.RollBand => new ParamBounds(ParamUnit.Radians, 0f, 0.5f),
            LegParam.SlopeAlign => new ParamBounds(ParamUnit.Ratio, 0f, 1f),
            LegParam.MaxSlopeAlign => new ParamBounds(ParamUnit.Radians, 0f, 1.2f),
            LegParam.MaxWalkableSlope => new ParamBounds(ParamUnit.Radians, 0f, 1.5f),
            LegParam.TerrainFollow => new ParamBounds(ParamUnit.Ratio, 0f, 1f),
            LegParam.TerrainHalfLife => new ParamBounds(ParamUnit.Seconds, 0f, 1f),
            LegParam.MaxStepHeight => new ParamBounds(ParamUnit.Meters, 0f, 1f),
            LegParam.Softness => new ParamBounds(ParamUnit.Ratio, 0f, 0.3f),
            LegParam.MaxStretch => new ParamBounds(ParamUnit.Ratio, 1f, 1.25f),
            LegParam.KneeFollow => new ParamBounds(ParamUnit.Ratio, 0f, 1f),
            LegParam.PelvisWeight => new ParamBounds(ParamUnit.Ratio, 0f, 1f),
            LegParam.PelvisDrop => new ParamBounds(ParamUnit.Meters, 0f, 1f),
            LegParam.PelvisRaise => new ParamBounds(ParamUnit.Meters, 0f, 0.5f),
            _ => new ParamBounds(ParamUnit.Seconds, 0f, 1f)
        };

        public static ParamText Text(LegParam field) => field switch
        {
            LegParam.Weight => new ParamText("Weight", "Overall", "How much the leg solver changes the animated legs. 0 leaves the animation untouched."),
            LegParam.WeightHalfLife => new ParamText("WeightHalfLife", "Overall", "How quickly the leg solver fades in or out when a clip or timeline turns foot IK on or off."),
            LegParam.PlantHeight => new ParamText("PlantHeight", "Planting", "A foot can plant once the lowest point of its sole is closer to the ground than this."),
            LegParam.ReleaseHeight => new ParamText("ReleaseHeight", "Planting", "A planted foot lets go once the animation lifts the lowest point of its sole above this."),
            LegParam.PlantRise => new ParamText("PlantRise", "Planting", "A foot only plants while the animation raises its sole slower than this. Falling feet always may plant."),
            LegParam.ReleaseRise => new ParamText("ReleaseRise", "Planting", "A planted foot lets go as soon as the animation raises its sole faster than this."),
            LegParam.PlantSpeed => new ParamText("PlantSpeed", "Planting", "A foot only plants once it moves slower than this over the ground, so a foot still sliding forward at touchdown waits instead of planting and tearing loose. 0 turns the check off."),
            LegParam.PinRadius => new ParamText("PinRadius", "Planting", "A planted foot stays pinned until the animated foot drifts this far from the pin. Beyond it the pin is dragged along, so the foot slides instead of overstretching."),
            LegParam.ReleaseDistance => new ParamText("ReleaseDistance", "Planting", "A planted foot lets go once the animated foot drifts this far from the pin."),
            LegParam.BlendSeconds => new ParamText("BlendSeconds", "Planting", "Every plant and release is inertialized over this time, so neither pops."),
            LegParam.TurnPivot => new ParamText("TurnPivot", "Roll", "Where on the sole a planted foot pivots when the animation turns it. 0 is the heel, 1 is the ball."),
            LegParam.HeelRoll => new ParamText("HeelRoll", "Roll", "How much a toe-up foot rolls on its heel. 0 rotates it about the turn pivot instead."),
            LegParam.BallRoll => new ParamText("BallRoll", "Roll", "How much a heel-up foot rolls on its ball. 0 rotates it about the turn pivot instead."),
            LegParam.RollBand => new ParamText("RollBand", "Roll", "Foot pitch range around flat over which the contact moves smoothly between heel and ball. Smaller is crisper, larger is softer."),
            LegParam.SlopeAlign => new ParamText("SlopeAlign", "Ground", "How much a planted foot tilts with the ground. The animated heel and toe motion is kept on top."),
            LegParam.MaxSlopeAlign => new ParamText("MaxSlopeAlign", "Ground", "The largest tilt applied to a planted foot to follow the ground."),
            LegParam.MaxWalkableSlope => new ParamText("MaxWalkableSlope", "Ground", "Ground steeper than this is never planted on."),
            LegParam.TerrainFollow => new ParamText("TerrainFollow", "Ground", "How much a foot follows ground that is higher or lower than the floor the animation was made on."),
            LegParam.TerrainHalfLife => new ParamText("TerrainHalfLife", "Ground", "How quickly a foot adapts to a new ground height, for example at a stair edge."),
            LegParam.MaxStepHeight => new ParamText("MaxStepHeight", "Ground", "The largest ground height difference under a foot that the leg adapts to."),
            LegParam.Softness => new ParamText("Softness", "Reach", "Fraction of the leg length over which the knee eases into full extension, so it never snaps straight."),
            LegParam.MaxStretch => new ParamText("MaxStretch", "Reach", "How far the leg may stretch to reach a pin. 1 never stretches."),
            LegParam.KneeFollow => new ParamText("KneeFollow", "Reach", "1 keeps the knee bending the way the animation bends it. 0 points it along the character forward."),
            LegParam.PelvisWeight => new ParamText("PelvisWeight", "Pelvis", "How much the pelvis moves so that planted feet can reach the ground."),
            LegParam.PelvisDrop => new ParamText("PelvisDrop", "Pelvis", "The furthest the pelvis lowers."),
            LegParam.PelvisRaise => new ParamText("PelvisRaise", "Pelvis", "The furthest the pelvis rises."),
            _ => new ParamText("PelvisHalfLife", "Pelvis", "How quickly the pelvis settles. Smaller follows the ground faster.")
        };

        public static float Get(in LegParams p, LegParam field) => field switch
        {
            LegParam.Weight => p.Weight,
            LegParam.WeightHalfLife => p.WeightHalfLife,
            LegParam.PlantHeight => p.PlantHeight,
            LegParam.ReleaseHeight => p.ReleaseHeight,
            LegParam.PlantRise => p.PlantRise,
            LegParam.ReleaseRise => p.ReleaseRise,
            LegParam.PlantSpeed => p.PlantSpeed,
            LegParam.PinRadius => p.PinRadius,
            LegParam.ReleaseDistance => p.ReleaseDistance,
            LegParam.BlendSeconds => p.BlendSeconds,
            LegParam.TurnPivot => p.TurnPivot,
            LegParam.HeelRoll => p.HeelRoll,
            LegParam.BallRoll => p.BallRoll,
            LegParam.RollBand => p.RollBand,
            LegParam.SlopeAlign => p.SlopeAlign,
            LegParam.MaxSlopeAlign => p.MaxSlopeAlign,
            LegParam.MaxWalkableSlope => p.MaxWalkableSlope,
            LegParam.TerrainFollow => p.TerrainFollow,
            LegParam.TerrainHalfLife => p.TerrainHalfLife,
            LegParam.MaxStepHeight => p.MaxStepHeight,
            LegParam.Softness => p.Softness,
            LegParam.MaxStretch => p.MaxStretch,
            LegParam.KneeFollow => p.KneeFollow,
            LegParam.PelvisWeight => p.PelvisWeight,
            LegParam.PelvisDrop => p.PelvisDrop,
            LegParam.PelvisRaise => p.PelvisRaise,
            _ => p.PelvisHalfLife
        };

        public static LegParams With(in LegParams p, LegParam field, float value)
        {
            var q = p;
            switch (field)
            {
                case LegParam.Weight: q.Weight = value; break;
                case LegParam.WeightHalfLife: q.WeightHalfLife = value; break;
                case LegParam.PlantHeight: q.PlantHeight = value; break;
                case LegParam.ReleaseHeight: q.ReleaseHeight = value; break;
                case LegParam.PlantRise: q.PlantRise = value; break;
                case LegParam.ReleaseRise: q.ReleaseRise = value; break;
                case LegParam.PlantSpeed: q.PlantSpeed = value; break;
                case LegParam.PinRadius: q.PinRadius = value; break;
                case LegParam.ReleaseDistance: q.ReleaseDistance = value; break;
                case LegParam.BlendSeconds: q.BlendSeconds = value; break;
                case LegParam.TurnPivot: q.TurnPivot = value; break;
                case LegParam.HeelRoll: q.HeelRoll = value; break;
                case LegParam.BallRoll: q.BallRoll = value; break;
                case LegParam.RollBand: q.RollBand = value; break;
                case LegParam.SlopeAlign: q.SlopeAlign = value; break;
                case LegParam.MaxSlopeAlign: q.MaxSlopeAlign = value; break;
                case LegParam.MaxWalkableSlope: q.MaxWalkableSlope = value; break;
                case LegParam.TerrainFollow: q.TerrainFollow = value; break;
                case LegParam.TerrainHalfLife: q.TerrainHalfLife = value; break;
                case LegParam.MaxStepHeight: q.MaxStepHeight = value; break;
                case LegParam.Softness: q.Softness = value; break;
                case LegParam.MaxStretch: q.MaxStretch = value; break;
                case LegParam.KneeFollow: q.KneeFollow = value; break;
                case LegParam.PelvisWeight: q.PelvisWeight = value; break;
                case LegParam.PelvisDrop: q.PelvisDrop = value; break;
                case LegParam.PelvisRaise: q.PelvisRaise = value; break;
                case LegParam.PelvisHalfLife: q.PelvisHalfLife = value; break;
            }
            return q;
        }

        public static LegParams Sanitize(in LegParams p) => Prepared(p, 1f);

        public static LegParams Prepared(in LegParams parameters, float bodyScale)
        {
            var standard = LegParams.Standard;
            var factor = Scalar.IsFinite(bodyScale) && bodyScale > 0f ? bodyScale : 1f;
            var p = parameters;
            p.Weight = Bounded(LegParam.Weight, parameters.Weight, standard.Weight, factor);
            p.WeightHalfLife = Bounded(LegParam.WeightHalfLife, parameters.WeightHalfLife, standard.WeightHalfLife, factor);
            p.PlantHeight = Bounded(LegParam.PlantHeight, parameters.PlantHeight, standard.PlantHeight, factor);
            p.ReleaseHeight = Bounded(LegParam.ReleaseHeight, parameters.ReleaseHeight, standard.ReleaseHeight, factor);
            p.PlantRise = Bounded(LegParam.PlantRise, parameters.PlantRise, standard.PlantRise, factor);
            p.ReleaseRise = Bounded(LegParam.ReleaseRise, parameters.ReleaseRise, standard.ReleaseRise, factor);
            p.PlantSpeed = Bounded(LegParam.PlantSpeed, parameters.PlantSpeed, standard.PlantSpeed, factor);
            p.PinRadius = Bounded(LegParam.PinRadius, parameters.PinRadius, standard.PinRadius, factor);
            p.ReleaseDistance = Bounded(LegParam.ReleaseDistance, parameters.ReleaseDistance, standard.ReleaseDistance, factor);
            p.BlendSeconds = Bounded(LegParam.BlendSeconds, parameters.BlendSeconds, standard.BlendSeconds, factor);
            p.TurnPivot = Bounded(LegParam.TurnPivot, parameters.TurnPivot, standard.TurnPivot, factor);
            p.HeelRoll = Bounded(LegParam.HeelRoll, parameters.HeelRoll, standard.HeelRoll, factor);
            p.BallRoll = Bounded(LegParam.BallRoll, parameters.BallRoll, standard.BallRoll, factor);
            p.RollBand = Bounded(LegParam.RollBand, parameters.RollBand, standard.RollBand, factor);
            p.SlopeAlign = Bounded(LegParam.SlopeAlign, parameters.SlopeAlign, standard.SlopeAlign, factor);
            p.MaxSlopeAlign = Bounded(LegParam.MaxSlopeAlign, parameters.MaxSlopeAlign, standard.MaxSlopeAlign, factor);
            p.MaxWalkableSlope = Bounded(LegParam.MaxWalkableSlope, parameters.MaxWalkableSlope, standard.MaxWalkableSlope, factor);
            p.TerrainFollow = Bounded(LegParam.TerrainFollow, parameters.TerrainFollow, standard.TerrainFollow, factor);
            p.TerrainHalfLife = Bounded(LegParam.TerrainHalfLife, parameters.TerrainHalfLife, standard.TerrainHalfLife, factor);
            p.MaxStepHeight = Bounded(LegParam.MaxStepHeight, parameters.MaxStepHeight, standard.MaxStepHeight, factor);
            p.Softness = Bounded(LegParam.Softness, parameters.Softness, standard.Softness, factor);
            p.MaxStretch = Bounded(LegParam.MaxStretch, parameters.MaxStretch, standard.MaxStretch, factor);
            p.KneeFollow = Bounded(LegParam.KneeFollow, parameters.KneeFollow, standard.KneeFollow, factor);
            p.PelvisWeight = Bounded(LegParam.PelvisWeight, parameters.PelvisWeight, standard.PelvisWeight, factor);
            p.PelvisDrop = Bounded(LegParam.PelvisDrop, parameters.PelvisDrop, standard.PelvisDrop, factor);
            p.PelvisRaise = Bounded(LegParam.PelvisRaise, parameters.PelvisRaise, standard.PelvisRaise, factor);
            p.PelvisHalfLife = Bounded(LegParam.PelvisHalfLife, parameters.PelvisHalfLife, standard.PelvisHalfLife, factor);
            p.ReleaseHeight = Scalar.Max(p.ReleaseHeight, p.PlantHeight);
            p.ReleaseRise = Scalar.Max(p.ReleaseRise, p.PlantRise);
            p.ReleaseDistance = Scalar.Max(p.ReleaseDistance, p.PinRadius);
            return p;
        }

        static float Bounded(LegParam field, float value, float fallback, float factor)
        {
            var bounds = Bounds(field);
            var clamped = bounds.Clamp(value, fallback);
            return bounds.ScalesWithBody ? clamped * factor : clamped;
        }

        public static LegParams Scaled(in LegParams p, float factor)
        {
            if (!Scalar.IsFinite(factor) || factor <= 0f)
                return p;
            var q = p;
            for (var i = 0; i < Count; i++)
            {
                var field = (LegParam)i;
                if (Bounds(field).ScalesWithBody)
                    q = With(q, field, Get(q, field) * factor);
            }
            return q;
        }
    }
}
