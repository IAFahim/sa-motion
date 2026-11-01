using System;
using System.Runtime.CompilerServices;
using SA.Motion.Inertialization;
using SA.Motion.Mechanics;
using SA.Motion.Numerics;

namespace SA.Motion.Legs
{
    public static class LegRig
    {
        const float ElapsedCap = 1e6f;
        const int PreparedParameterCount = 26;
        const int EveryParameterIsPrepared = 1 / (LegParamsSpec.Count == PreparedParameterCount ? 1 : 0);

        public static Float3 Step(
            in LegParams parameters,
            float bodyScale,
            float weight,
            ref LegRigState rig,
            Span<LegState> states,
            ReadOnlySpan<LegInput> inputs,
            ReadOnlySpan<FootShape> shapes,
            Float3 floor,
            Float3 up,
            Float3 forward,
            float deltaTime,
            Span<LegPose> poses)
        {
            var p = Prepared(parameters, bodyScale);
            var dt = Scalar.IsFinite(deltaTime) && deltaTime > 0f ? deltaTime : 0f;
            var upAxis = Unit(up, new Float3(0f, 1f, 0f));
            var ahead = Ahead(forward, upAxis);
            var level = floor.IsFinite ? floor : Float3.Zero;
            var desired = Scalar.IsFinite(weight) ? Clamp(weight, 0f, 1f) : 0f;
            rig.Weight = rig.Started == 0 ? new SpringState { Value = desired } : CriticalSpring.Step(rig.Weight, desired, p.WeightHalfLife, dt);
            rig.Started = 1;
            p.Weight *= Clamp(rig.Weight.Value, 0f, 1f);
            var count = Math.Min(Math.Min(states.Length, inputs.Length), Math.Min(shapes.Length, poses.Length));
            var slopeLimit = Planting.SlopeLimit(p.MaxWalkableSlope);
            var demand = float.PositiveInfinity;
            for (var i = 0; i < count; i++)
                demand = Scalar.Min(demand, Plan(p, ref states[i], inputs[i], shapes[i], level, upAxis, slopeLimit, dt, ref poses[i]));
            var goal = float.IsPositiveInfinity(demand) ? 0f : Clamp(demand, -p.PelvisDrop, p.PelvisRaise) * p.PelvisWeight * p.Weight;
            rig.Pelvis = CriticalSpring.Step(rig.Pelvis, goal, p.PelvisHalfLife, dt);
            var shift = upAxis * rig.Pelvis.Value;
            for (var i = 0; i < count; i++)
                Resolve(p, ref states[i], inputs[i], shapes[i], shift, upAxis, ahead, dt, ref poses[i]);
            return shift;
        }

        static LegParams Prepared(in LegParams parameters, float bodyScale)
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
            var bounds = LegParamsSpec.Bounds(field);
            var clamped = bounds.Clamp(value, fallback);
            return bounds.ScalesWithBody ? clamped * factor : clamped;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static float Plan(in LegParams p, ref LegState state, in LegInput input, in FootShape shape, Float3 floor, Float3 up, float slopeLimit, float dt, ref LegPose pose)
        {
            if (!Valid(input, shape))
            {
                state = default;
                pose = Passthrough(input);
                return float.PositiveInfinity;
            }
            var foot = LegMath.Unit(input.Foot);
            var heelArm = Quaternion.Rotate(foot, shape.Heel);
            var ballArm = Quaternion.Rotate(foot, shape.Ball);
            var heel = input.Ankle + heelArm;
            var ball = input.Ankle + ballArm;
            var ground = Planting.WalkableWithin(input.Ground, up, slopeLimit);
            var normal = ground ? Unit(input.Ground.Normal, up) : up;
            var step = ground ? Clamp(Float3.Dot(input.Ground.Point - floor, up), -p.MaxStepHeight, p.MaxStepHeight) * p.TerrainFollow : 0f;
            state.Terrain = CriticalSpring.Step(state.Terrain, step, p.TerrainHalfLife, dt);
            var lift = up * state.Terrain.Value;
            var clearance = Scalar.Min(Float3.Dot(heel - floor, up), Float3.Dot(ball - floor, up));
            var height = ground
                ? Scalar.Min(Float3.Dot(heel + lift - input.Ground.Point, normal), Float3.Dot(ball + lift - input.Ground.Point, normal))
                : float.PositiveInfinity;
            var rise = state.HasPrevious != 0 && dt > 0f ? (clearance - state.PreviousClearance) / dt : 0f;
            var contact = input.Ankle + lift + (heelArm + (ballArm - heelArm) * Unit(p.TurnPivot));
            var wasPlanted = state.Planted != 0;
            var slack = wasPlanted ? Flat(contact - state.Anchor, state.AnchorNormal) : Float3.Zero;
            var drift = wasPlanted ? LegMath.Length(slack) : 0f;
            var plant = Planting.Decide(wasPlanted, ground, height, rise, drift, input.ContactAuthored, input.Contact, p);
            var aligned = plant ? Fulcrum.Tilted(foot, up, wasPlanted ? state.AnchorNormal : normal, p.SlopeAlign, p.MaxSlopeAlign) : foot;
            var tilted = plant && !Same(aligned, foot);
            var plantedHeel = tilted ? Quaternion.Rotate(aligned, shape.Heel) : heelArm;
            var plantedBall = tilted ? Quaternion.Rotate(aligned, shape.Ball) : ballArm;
            if (plant && !wasPlanted)
            {
                var current = state.Output.Count != 0 ? state.Output.Current.Position : input.Ankle + lift;
                var anchor = current - Fulcrum.Ankle(Float3.Zero, normal, plantedHeel, plantedBall, p.TurnPivot, p.HeelRoll, p.BallRoll, p.RollBand);
                state.Anchor = anchor - normal * Float3.Dot(anchor - input.Ground.Point, normal);
                state.AnchorNormal = normal;
                slack = Flat(contact - state.Anchor, state.AnchorNormal);
                drift = LegMath.Length(slack);
            }
            if (plant && drift > p.PinRadius && drift > Scalar.MagnitudeEpsilon)
                state.Anchor = state.Anchor + slack * ((drift - p.PinRadius) / drift);
            state.Changed = (byte)(plant != wasPlanted ? 1 : 0);
            state.Planted = (byte)(plant ? 1 : 0);
            state.HasPrevious = 1;
            state.PreviousClearance = Scalar.IsFinite(clearance) ? clearance : 0f;
            pose.Planted = state.Planted;
            if (!plant)
            {
                pose.Foot = foot;
                pose.Ankle = input.Ankle;
                return ground && height <= p.ReleaseHeight ? state.Terrain.Value : float.PositiveInfinity;
            }
            pose.Foot = aligned;
            pose.Ankle = Fulcrum.Ankle(state.Anchor, state.AnchorNormal, plantedHeel, plantedBall, p.TurnPivot, p.HeelRoll, p.BallRoll, p.RollBand);
            return Float3.Dot(pose.Ankle - input.Ankle, up);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Resolve(in LegParams p, ref LegState state, in LegInput input, in FootShape shape, Float3 shift, Float3 up, Float3 ahead, float dt, ref LegPose pose)
        {
            if (state.HasPrevious == 0)
                return;
            var hip = input.Hip + shift;
            var knee = input.Knee + shift;
            var ankle = input.Ankle + shift;
            var planted = state.Planted != 0;
            var target = new Pose
            {
                Position = planted ? pose.Ankle : input.Ankle + up * state.Terrain.Value,
                Rotation = pose.Foot,
                Scale = Float3.One
            };
            if (state.Output.Count == 0)
            {
                state.Blend = default;
                state.BlendElapsed = 0f;
            }
            else if (state.Changed != 0)
            {
                state.Blend = PoseTransition.Create(state.Output, target, p.BlendSeconds);
                state.BlendElapsed = 0f;
            }
            var output = Settled(state.Blend, state.BlendElapsed) ? target : PoseTransition.Apply(ref state.Blend, target, state.BlendElapsed);
            state.BlendElapsed = Scalar.Min(state.BlendElapsed + dt, ElapsedCap);
            state.Output.Capture(output, dt);
            var weightedAnkle = ankle + (output.Position - ankle) * p.Weight;
            var weightedFoot = Nlerp(input.Foot, output.Rotation, p.Weight);
            var reach = SoftReach.Leg(hip, knee, ankle, weightedAnkle, ahead, p.KneeFollow, p.Softness, p.MaxStretch);
            pose = new LegPose
            {
                Upper = LegMath.Unit(reach.Valid ? Quaternion.Multiply(reach.UpperDelta, input.Upper) : input.Upper),
                Lower = LegMath.Unit(reach.Valid ? Quaternion.Multiply(reach.LowerDelta, input.Lower) : input.Lower),
                Foot = weightedFoot,
                Ankle = reach.Valid ? reach.Ankle : ankle,
                Contact = planted ? state.Anchor : weightedAnkle + Quaternion.Rotate(weightedFoot, Fulcrum.Pivot(shape, p.TurnPivot)),
                Stretch = reach.Stretch,
                Planted = state.Planted,
                Solved = (byte)(reach.Valid ? 1 : 0)
            };
        }

        static bool Settled(in PoseTransition blend, float elapsed) =>
            Finished(blend.Position.Decay.Duration, elapsed) && Finished(blend.Rotation.Decay.Duration, elapsed) && Finished(blend.LogScale.Decay.Duration, elapsed);

        static bool Finished(float duration, float elapsed) => duration <= Scalar.DurationEpsilon || elapsed >= duration;

        static bool Valid(in LegInput input, in FootShape shape) =>
            input.Hip.IsFinite && input.Knee.IsFinite && input.Ankle.IsFinite && shape.Heel.IsFinite && shape.Ball.IsFinite
            && input.Upper.IsValidRotation && input.Lower.IsValidRotation && input.Foot.IsValidRotation;

        static LegPose Passthrough(in LegInput input)
        {
            var ankle = input.Ankle.IsFinite ? input.Ankle : Float3.Zero;
            return new LegPose
            {
                Upper = Quaternion.Normalize(input.Upper),
                Lower = Quaternion.Normalize(input.Lower),
                Foot = Quaternion.Normalize(input.Foot),
                Ankle = ankle,
                Contact = ankle,
                Stretch = 1f
            };
        }

        static bool Same(Quaternion a, Quaternion b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.W == b.W;

        static Float3 Flat(Float3 value, Float3 normal) => value - normal * Float3.Dot(value, normal);

        static Quaternion Nlerp(Quaternion a, Quaternion b, float t)
        {
            if (Quaternion.Dot(a, b) < 0f)
                b = -b;
            if (t >= 1f)
                return b;
            a = LegMath.Unit(a);
            return LegMath.Unit(new Quaternion(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t, a.W + (b.W - a.W) * t));
        }

        static Float3 Ahead(Float3 forward, Float3 up)
        {
            var flat = forward - up * Float3.Dot(forward, up);
            var length = LegMath.Length(flat);
            return flat.IsFinite && Scalar.IsFinite(length) && length > Scalar.MagnitudeEpsilon ? flat / length : Perpendicular(up);
        }

        static Float3 Unit(Float3 value, Float3 fallback)
        {
            var length = LegMath.Length(value);
            return value.IsFinite && Scalar.IsFinite(length) && length > Scalar.MagnitudeEpsilon ? value / length : fallback;
        }

        static Float3 Perpendicular(Float3 axis) =>
            Unit(Float3.Cross(axis, Math.Abs(axis.X) < 0.8f ? new Float3(1f, 0f, 0f) : new Float3(0f, 0f, 1f)), new Float3(0f, 0f, 1f));

        static float Unit(float value) => !Scalar.IsFinite(value) ? 0f : value < 0f ? 0f : value > 1f ? 1f : value;

        static float Clamp(float value, float low, float high) => value < low ? low : value > high ? high : value;
    }

    internal static class LegMath
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Length(Float3 value) => MathF.Sqrt(Float3.Dot(value, value));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Unit(Quaternion q)
        {
            var squared = Quaternion.Dot(q, q);
            if (!Scalar.IsFinite(squared) || squared <= Scalar.MagnitudeEpsilon * Scalar.MagnitudeEpsilon)
                return Quaternion.Identity;
            var inverse = 1f / MathF.Sqrt(squared);
            return new Quaternion(q.X * inverse, q.Y * inverse, q.Z * inverse, q.W * inverse);
        }
    }
}
