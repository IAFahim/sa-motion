using System;
using SA.Motion.Inertialization;
using SA.Motion.Mechanics;
using SA.Motion.Numerics;

namespace SA.Motion.Legs
{
    public static class LegRig
    {
        const float ElapsedCap = 1e6f;

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
            var p = LegParamsSpec.Scaled(LegParamsSpec.Sanitize(parameters), bodyScale);
            var dt = Scalar.IsFinite(deltaTime) && deltaTime > 0f ? deltaTime : 0f;
            var upAxis = Unit(up, new Float3(0f, 1f, 0f));
            var ahead = Unit(forward - upAxis * Float3.Dot(forward, upAxis), Perpendicular(upAxis));
            var level = floor.IsFinite ? floor : Float3.Zero;
            var desired = Scalar.IsFinite(weight) ? Clamp(weight, 0f, 1f) : 0f;
            rig.Weight = rig.Started == 0 ? new SpringState { Value = desired } : CriticalSpring.Step(rig.Weight, desired, p.WeightHalfLife, dt);
            rig.Started = 1;
            p.Weight *= Clamp(rig.Weight.Value, 0f, 1f);
            var count = Math.Min(Math.Min(states.Length, inputs.Length), Math.Min(shapes.Length, poses.Length));
            var demand = float.PositiveInfinity;
            for (var i = 0; i < count; i++)
                demand = Scalar.Min(demand, Plan(p, ref states[i], inputs[i], shapes[i], level, upAxis, dt, ref poses[i]));
            var goal = float.IsPositiveInfinity(demand) ? 0f : Clamp(demand, -p.PelvisDrop, p.PelvisRaise) * p.PelvisWeight * p.Weight;
            rig.Pelvis = CriticalSpring.Step(rig.Pelvis, goal, p.PelvisHalfLife, dt);
            var shift = upAxis * rig.Pelvis.Value;
            for (var i = 0; i < count; i++)
                Resolve(p, ref states[i], inputs[i], shapes[i], shift, upAxis, ahead, dt, ref poses[i]);
            return shift;
        }

        static float Plan(in LegParams p, ref LegState state, in LegInput input, in FootShape shape, Float3 floor, Float3 up, float dt, ref LegPose pose)
        {
            if (!Valid(input, shape))
            {
                state = default;
                pose = Passthrough(input);
                return float.PositiveInfinity;
            }
            var foot = Quaternion.Normalize(input.Foot);
            var heel = input.Ankle + Quaternion.Rotate(foot, shape.Heel);
            var ball = input.Ankle + Quaternion.Rotate(foot, shape.Ball);
            var ground = Planting.Walkable(input.Ground, up, p.MaxWalkableSlope);
            var normal = ground ? Unit(input.Ground.Normal, up) : up;
            var step = ground ? Clamp(Float3.Dot(input.Ground.Point - floor, up), -p.MaxStepHeight, p.MaxStepHeight) * p.TerrainFollow : 0f;
            state.Terrain = CriticalSpring.Step(state.Terrain, step, p.TerrainHalfLife, dt);
            var lift = up * state.Terrain.Value;
            var clearance = Scalar.Min(Float3.Dot(heel - floor, up), Float3.Dot(ball - floor, up));
            var height = ground
                ? Scalar.Min(Float3.Dot(heel + lift - input.Ground.Point, normal), Float3.Dot(ball + lift - input.Ground.Point, normal))
                : float.PositiveInfinity;
            var rise = state.HasPrevious != 0 && dt > 0f ? (clearance - state.PreviousClearance) / dt : 0f;
            var contact = input.Ankle + lift + Quaternion.Rotate(foot, Fulcrum.Pivot(shape, p.TurnPivot));
            var wasPlanted = state.Planted != 0;
            var drift = wasPlanted ? Flat(contact - state.Anchor, state.AnchorNormal).Length : 0f;
            var plant = Planting.Decide(wasPlanted, ground, height, rise, drift, input.ContactAuthored, input.Contact, p);
            var aligned = Fulcrum.AlignToSlope(foot, up, plant && wasPlanted ? state.AnchorNormal : normal, p.SlopeAlign, p.MaxSlopeAlign);
            if (plant && !wasPlanted)
            {
                var current = state.Output.Count != 0 ? state.Output.Current.Position : input.Ankle + lift;
                var anchor = current - Fulcrum.Ankle(shape, Float3.Zero, normal, aligned, p.TurnPivot, p.HeelRoll, p.BallRoll, p.RollBand);
                state.Anchor = anchor - normal * Float3.Dot(anchor - input.Ground.Point, normal);
                state.AnchorNormal = normal;
            }
            if (plant)
            {
                var slack = Flat(contact - state.Anchor, state.AnchorNormal);
                var length = slack.Length;
                if (length > p.PinRadius && length > Scalar.MagnitudeEpsilon)
                    state.Anchor = state.Anchor + slack * ((length - p.PinRadius) / length);
            }
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
            pose.Ankle = Fulcrum.Ankle(shape, state.Anchor, state.AnchorNormal, aligned, p.TurnPivot, p.HeelRoll, p.BallRoll, p.RollBand);
            return Float3.Dot(pose.Ankle - input.Ankle, up);
        }

        static void Resolve(in LegParams p, ref LegState state, in LegInput input, in FootShape shape, Float3 shift, Float3 up, Float3 ahead, float dt, ref LegPose pose)
        {
            if (!Valid(input, shape))
                return;
            var foot = Quaternion.Normalize(input.Foot);
            var hip = input.Hip + shift;
            var knee = input.Knee + shift;
            var ankle = input.Ankle + shift;
            var planted = state.Planted != 0;
            var target = new Pose
            {
                Position = planted ? pose.Ankle : input.Ankle + up * state.Terrain.Value,
                Rotation = planted ? pose.Foot : foot,
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
            var output = PoseTransition.Apply(ref state.Blend, target, state.BlendElapsed);
            state.BlendElapsed = Scalar.Min(state.BlendElapsed + dt, ElapsedCap);
            state.Output.Capture(output, dt);
            var weightedAnkle = ankle + (output.Position - ankle) * p.Weight;
            var weightedFoot = Nlerp(foot, output.Rotation, p.Weight);
            var bend = SoftReach.KneeDirection(hip, knee, ankle, ahead, p.KneeFollow);
            var reach = SoftReach.Solve(hip, knee, ankle, weightedAnkle, bend, p.Softness, p.MaxStretch);
            pose = new LegPose
            {
                Upper = reach.Valid ? Quaternion.Normalize(Quaternion.Multiply(reach.UpperDelta, Quaternion.Normalize(input.Upper))) : Quaternion.Normalize(input.Upper),
                Lower = reach.Valid ? Quaternion.Normalize(Quaternion.Multiply(reach.LowerDelta, Quaternion.Normalize(input.Lower))) : Quaternion.Normalize(input.Lower),
                Foot = weightedFoot,
                Ankle = reach.Valid ? reach.Ankle : ankle,
                Contact = planted ? state.Anchor : weightedAnkle + Quaternion.Rotate(weightedFoot, Fulcrum.Pivot(shape, p.TurnPivot)),
                Stretch = reach.Stretch,
                Planted = state.Planted,
                Solved = (byte)(reach.Valid ? 1 : 0)
            };
        }

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

        static Float3 Flat(Float3 value, Float3 normal) => value - normal * Float3.Dot(value, normal);

        static Quaternion Nlerp(Quaternion a, Quaternion b, float t)
        {
            if (Quaternion.Dot(a, b) < 0f)
                b = -b;
            return Quaternion.Normalize(new Quaternion(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t, a.W + (b.W - a.W) * t));
        }

        static Float3 Unit(Float3 value, Float3 fallback)
        {
            var length = value.Length;
            return value.IsFinite && Scalar.IsFinite(length) && length > Scalar.MagnitudeEpsilon ? value / length : fallback;
        }

        static Float3 Perpendicular(Float3 axis) =>
            Unit(Float3.Cross(axis, Math.Abs(axis.X) < 0.8f ? new Float3(1f, 0f, 0f) : new Float3(0f, 0f, 1f)), new Float3(0f, 0f, 1f));

        static float Clamp(float value, float low, float high) => value < low ? low : value > high ? high : value;
    }
}
