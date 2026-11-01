using SA.Motion.Legs;
using SA.Motion.Mechanics;
using SA.Motion.Numerics;
using SA.Motion.Tuning;
using Xunit;

namespace SA.Motion.Tests;

public class LegTests
{
    const float Frame = 1f / 60f;
    static readonly Float3 Up = new(0f, 1f, 0f);
    static readonly Float3 Forward = new(0f, 0f, 1f);
    static readonly FootShape Shape = new() { Heel = new Float3(0f, -0.09f, -0.05f), Ball = new Float3(0f, -0.09f, 0.14f) };

    static Quaternion Pitched(float toeUp) => Quaternion.AxisAngle(new Float3(1f, 0f, 0f), -toeUp);

    static GroundHit Ground(float height = 0f) => new() { Hit = 1, Point = new Float3(0f, height, 0f), Normal = Up };

    static float Distance(Float3 a, Float3 b) => (a - b).Length;

    static LegInput Leg(Float3 offset, Quaternion foot, GroundHit ground) => new()
    {
        Hip = new Float3(0f, 0.95f, 0f) + offset,
        Knee = new Float3(0f, 0.52f, 0.12f) + offset,
        Ankle = new Float3(0f, 0.09f, 0f) + offset,
        Upper = Quaternion.Identity,
        Lower = Quaternion.Identity,
        Foot = foot,
        Ground = ground
    };

    static LegInput Raised(float lift)
    {
        var bone = MathF.Sqrt(0.43f * 0.43f + 0.12f * 0.12f);
        var span = 0.86f - lift;
        var input = Leg(Float3.Zero, Quaternion.Identity, Ground());
        input.Knee = new Float3(0f, (1.04f + lift) * 0.5f, MathF.Sqrt(bone * bone - span * span * 0.25f));
        input.Ankle = new Float3(0f, 0.09f + lift, 0f);
        return input;
    }

    static LegInput Gait(Float3 ankle, Quaternion foot) => new()
    {
        Hip = ankle + new Float3(0f, 0.8f, 0f),
        Knee = ankle + new Float3(0f, 0.4f, 0.15f),
        Ankle = ankle,
        Upper = Quaternion.Identity,
        Lower = Quaternion.Identity,
        Foot = foot,
        Ground = Ground()
    };

    sealed class Rig
    {
        public LegParams Params = LegParams.Standard;
        public LegRigState State;
        public float Weight = 1f;
        public SpringState Pelvis => State.Pelvis;
        public readonly LegState[] States;
        public readonly LegPose[] Poses;
        readonly FootShape[] shapes;

        public Rig(int legs)
        {
            States = new LegState[legs];
            Poses = new LegPose[legs];
            shapes = Enumerable.Repeat(Shape, legs).ToArray();
        }

        public Float3 Step(params LegInput[] inputs) =>
            LegRig.Step(Params, 1f, Weight, ref State, States, inputs, shapes, Float3.Zero, Up, Forward, Frame, Poses);
    }

    [Fact]
    public void ToeUpFootRollsOnItsHeelAndTheHeelStaysPinned()
    {
        var anchor = new Float3(0.3f, 0f, 0.2f);
        var expected = anchor + (Shape.Heel - Fulcrum.Pivot(Shape, 0.7f));
        for (var pitch = 0.08f; pitch < 0.9f; pitch += 0.05f)
        {
            var foot = Pitched(pitch);
            Assert.InRange(Fulcrum.Pitch(Shape, foot, Up), pitch - 1e-4f, pitch + 1e-4f);
            var ankle = Fulcrum.Ankle(Shape, anchor, Up, foot, 0.7f, 1f, 1f, 0.07f);
            Assert.InRange(Distance(ankle + Quaternion.Rotate(foot, Shape.Heel), expected), 0f, 1e-5f);
        }
    }

    [Fact]
    public void HeelUpFootRollsOnItsBallAndTheBallStaysPinned()
    {
        var anchor = new Float3(-0.1f, 0.2f, 0.4f);
        var expected = anchor + (Shape.Ball - Fulcrum.Pivot(Shape, 0.7f));
        for (var pitch = -0.08f; pitch > -0.9f; pitch -= 0.05f)
        {
            var foot = Pitched(pitch);
            var ankle = Fulcrum.Ankle(Shape, anchor, Up, foot, 0.7f, 1f, 1f, 0.07f);
            Assert.InRange(Distance(ankle + Quaternion.Rotate(foot, Shape.Ball), expected), 0f, 1e-5f);
        }
    }

    [Fact]
    public void RollIsContinuousThroughFlat()
    {
        var anchor = new Float3(0f, 0f, 0.5f);
        var previous = Fulcrum.Ankle(Shape, anchor, Up, Pitched(-0.5f), 0.7f, 1f, 1f, 0.07f);
        var largest = 0f;
        for (var pitch = -0.4995f; pitch <= 0.5f; pitch += 0.0005f)
        {
            var ankle = Fulcrum.Ankle(Shape, anchor, Up, Pitched(pitch), 0.7f, 1f, 1f, 0.07f);
            largest = Math.Max(largest, Distance(ankle, previous));
            previous = ankle;
        }
        Assert.InRange(largest, 0f, 3e-4f);
    }

    [Fact]
    public void TurningAFlatFootPivotsAboutTheTurnPoint()
    {
        var anchor = new Float3(1f, 0f, -2f);
        foreach (var along in new[] { 0f, 0.4f, 1f })
            for (var yaw = -1.5f; yaw <= 1.5f; yaw += 0.25f)
            {
                var foot = Quaternion.AxisAngle(Up, yaw);
                var ankle = Fulcrum.Ankle(Shape, anchor, Up, foot, along, 1f, 1f, 0.07f);
                Assert.InRange(Distance(ankle + Quaternion.Rotate(foot, Fulcrum.Pivot(Shape, along)), anchor), 0f, 1e-5f);
            }
    }

    [Fact]
    public void SlopeAlignmentKeepsTheAnimatedPitchAndHonorsItsLimit()
    {
        var normal = Quaternion.Rotate(Quaternion.AxisAngle(new Float3(1f, 0f, 0f), -0.3f), Up);
        var aligned = Fulcrum.AlignToSlope(Pitched(0.2f), Up, normal, 1f, 0.6f);
        Assert.InRange(Fulcrum.Pitch(Shape, aligned, normal), 0.2f - 1e-4f, 0.2f + 1e-4f);
        static float Turned(Quaternion q) => 2f * MathF.Acos(Math.Min(1f, Math.Abs(q.W)));
        Assert.InRange(Turned(Fulcrum.AlignToSlope(Quaternion.Identity, Up, normal, 1f, 0.1f)), 0.1f - 1e-4f, 0.1f + 1e-4f);
        Assert.InRange(Turned(Fulcrum.AlignToSlope(Quaternion.Identity, Up, normal, 0.5f, 0.6f)), 0.15f - 1e-4f, 0.15f + 1e-4f);
        Assert.Equal(Quaternion.Identity, Fulcrum.AlignToSlope(Quaternion.Identity, Up, normal, 0f, 0.6f));
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(0.95f)]
    [InlineData(1f)]
    public void SoftDistanceIsExactBelowTheAnimatedReachMonotoneAndBounded(float animated)
    {
        var previous = 0f;
        for (var distance = 0f; distance <= 2f; distance += 0.001f)
        {
            var soft = SoftReach.Distance(distance, 1f, 0.1f, animated);
            Assert.True(soft <= 1f);
            Assert.True(soft >= previous - 1e-6f);
            Assert.True(soft - previous <= 0.001f + 1e-5f);
            if (distance <= Math.Max(animated, 0.9f))
                Assert.Equal(distance, soft);
            previous = soft;
        }
        Assert.InRange(SoftReach.Distance(3f, 1f, 0.1f, 0.5f), 0.999f, 1f);
        Assert.Equal(0f, SoftReach.Distance(float.NaN, 1f, 0.1f, 0.5f));
    }

    [Fact]
    public void SoftReachLeavesTheAnimatedLegUntouchedAndKeepsTheKneePlane()
    {
        var hip = new Float3(0f, 0.95f, 0f);
        var knee = new Float3(0.03f, 0.52f, 0.12f);
        var ankle = new Float3(0f, 0.09f, 0f);
        var bend = SoftReach.KneeDirection(hip, knee, ankle, Forward, 1f);
        var same = SoftReach.Solve(hip, knee, ankle, ankle, bend, 0.04f, 1f);
        Assert.True(same.Valid);
        Assert.InRange(Distance(same.Knee, knee), 0f, 1e-4f);
        Assert.InRange(Distance(same.Ankle, ankle), 0f, 1e-4f);
        var goal = ankle + new Float3(0.1f, 0.12f, 0.05f);
        var moved = SoftReach.Solve(hip, knee, ankle, goal, bend, 0.04f, 1f);
        var plane = Float3.Cross(goal - hip, bend);
        Assert.InRange(Math.Abs(Float3.Dot(moved.Knee - hip, plane / plane.Length)), 0f, 1e-4f);
        Assert.True(Float3.Dot(moved.Knee - hip, bend) > 0f);
        Assert.InRange(Distance(moved.Ankle, goal), 0f, 1e-4f);
        Assert.InRange(Distance(Quaternion.Rotate(moved.UpperDelta, knee - hip), moved.Knee - hip), 0f, 1e-4f);
    }

    [Fact]
    public void StretchOnlyHappensBeyondFullLengthAndNeverPassesItsCap()
    {
        var hip = new Float3(0f, 1f, 0f);
        var knee = new Float3(0f, 0.5f, 0.1f);
        var ankle = new Float3(0f, 0f, 0f);
        var bend = SoftReach.KneeDirection(hip, knee, ankle, Forward, 1f);
        Assert.Equal(1f, SoftReach.Solve(hip, knee, ankle, ankle, bend, 0.04f, 1.1f).Stretch);
        var far = SoftReach.Solve(hip, knee, ankle, new Float3(0f, -2f, 0f), bend, 0.04f, 1.1f);
        Assert.Equal(1.1f, far.Stretch, 5);
        Assert.True((far.Ankle - hip).Length <= 1.1f * ((knee - hip).Length + (ankle - knee).Length));
    }

    [Fact]
    public void CriticalSpringIsExactForAnyStepAndNeverOvershootsFromRest()
    {
        var once = CriticalSpring.Step(new SpringState { Value = 1f }, 0f, 0.1f, 0.1f);
        var half = CriticalSpring.Step(new SpringState { Value = 1f }, 0f, 0.1f, 0.05f);
        var twice = CriticalSpring.Step(half, 0f, 0.1f, 0.05f);
        Assert.InRange(Math.Abs(once.Value - twice.Value), 0f, 1e-5f);
        Assert.InRange(Math.Abs(once.Velocity - twice.Velocity), 0f, 1e-4f);
        var state = new SpringState { Value = 1f };
        var previous = 1f;
        for (var i = 0; i < 300; i++)
        {
            state = CriticalSpring.Step(state, 0f, 0.08f, Frame);
            Assert.True(state.Value >= 0f && state.Value <= previous);
            previous = state.Value;
        }
        Assert.InRange(state.Value, 0f, 1e-4f);
        Assert.Equal(2f, CriticalSpring.Step(new SpringState { Value = 1f }, 2f, 0f, Frame).Value);
        Assert.Equal(1f, CriticalSpring.Step(new SpringState { Value = 1f }, 2f, 0.1f, float.NaN).Value);
    }

    [Fact]
    public void PlantedFootStaysPinnedWhileTheBodyDrifts()
    {
        var rig = new Rig(1);
        for (var frame = 0; frame < 8; frame++)
            rig.Step(Leg(Float3.Zero, Quaternion.Identity, Ground()));
        Assert.Equal(1, rig.States[0].Planted);
        var pinned = rig.Poses[0].Ankle;
        for (var frame = 1; frame <= 8; frame++)
        {
            rig.Step(Leg(new Float3(0.015f * frame, 0f, 0f), Quaternion.Identity, Ground()));
            Assert.Equal(1, rig.Poses[0].Planted);
            Assert.InRange(Distance(rig.Poses[0].Ankle, pinned), 0f, 2e-3f);
        }
    }

    [Fact]
    public void ReleaseAndReplantAreContinuous()
    {
        var rig = new Rig(1);
        var lift = 0f;
        for (var frame = 0; frame < 8; frame++)
            rig.Step(Leg(Float3.Zero, Quaternion.Identity, Ground()));
        var previous = rig.Poses[0].Ankle;
        var released = -1;
        var replanted = -1;
        var largest = 0f;
        for (var frame = 0; frame < 90; frame++)
        {
            lift += frame < 30 ? 0.008f : frame < 60 ? -0.008f : 0f;
            rig.Step(Raised(lift));
            var step = Distance(rig.Poses[0].Ankle, previous);
            largest = Math.Max(largest, step);
            if (released < 0 && rig.Poses[0].Planted == 0)
            {
                released = frame;
                Assert.InRange(step, 0f, 1e-3f);
            }
            if (released >= 0 && replanted < 0 && rig.Poses[0].Planted == 1)
                replanted = frame;
            if (frame == 29)
                Assert.InRange(Distance(rig.Poses[0].Ankle, new Float3(0f, 0.09f + lift, 0f)), 0f, 2e-3f);
            previous = rig.Poses[0].Ankle;
        }
        Assert.InRange(released, 8, 14);
        Assert.InRange(replanted, 50, 62);
        Assert.InRange(largest, 0f, 0.035f);
        Assert.InRange(Distance(rig.Poses[0].Ankle, new Float3(0f, 0.09f, 0f)), 0f, 2e-3f);
    }

    [Fact]
    public void HeelStrikeRollsOnTheHeelAndToeOffRollsOnTheBallWhileTheAnimationSlides()
    {
        var rig = new Rig(1);
        var heelStart = new Float3(0f, 0f, 0.3f);
        var sole = Shape.Ball - Shape.Heel;
        for (var frame = 0; frame < 35; frame++)
        {
            var slide = new Float3(0f, 0f, 0.003f * frame);
            var pitch = frame < 10 ? 0.3f * (1f - frame / 10f) : frame < 20 ? 0f : -0.5f * (frame - 20) / 14f;
            var foot = Pitched(pitch);
            var ankle = frame < 20
                ? heelStart + slide - Quaternion.Rotate(foot, Shape.Heel)
                : heelStart + sole + slide - Quaternion.Rotate(foot, Shape.Ball);
            rig.Step(Gait(ankle, foot));
            var pose = rig.Poses[0];
            Assert.Equal(1, pose.Planted);
            var heel = pose.Ankle + Quaternion.Rotate(pose.Foot, Shape.Heel);
            var ball = pose.Ankle + Quaternion.Rotate(pose.Foot, Shape.Ball);
            if (frame < 7 || (frame >= 10 && frame < 20))
                Assert.InRange(Distance(heel, heelStart), 0f, 1e-3f);
            if (frame >= 10 && frame < 20 || frame >= 22)
                Assert.InRange(Distance(ball, heelStart + sole), 0f, 1e-3f);
        }
    }

    [Fact]
    public void PelvisLowersSoAFootReachesLowerGroundAndReturnsOnceFree()
    {
        var rig = new Rig(2);
        var left = new Float3(-0.12f, 0f, 0f);
        var right = new Float3(0.12f, 0f, 0f);
        for (var frame = 0; frame < 90; frame++)
            rig.Step(Leg(left, Quaternion.Identity, Ground()), Leg(right, Quaternion.Identity, Ground(-0.12f)));
        Assert.Equal(1, rig.States[0].Planted);
        Assert.Equal(1, rig.States[1].Planted);
        Assert.InRange(rig.Pelvis.Value, -0.121f, -0.119f);
        Assert.InRange(rig.Poses[1].Ankle.Y, 0.09f - 0.12f - 2e-3f, 0.09f - 0.12f + 2e-3f);
        Assert.InRange(rig.Poses[0].Ankle.Y, 0.09f - 2e-3f, 0.09f + 2e-3f);
        var high = new Float3(0f, 0.3f, 0f);
        for (var frame = 0; frame < 90; frame++)
            rig.Step(Leg(left + high, Quaternion.Identity, Ground()), Leg(right + high, Quaternion.Identity, Ground(-0.12f)));
        Assert.Equal(0, rig.States[0].Planted);
        Assert.InRange(rig.Pelvis.Value, -1e-3f, 1e-3f);
    }

    [Fact]
    public void FreeFootKeepsItsClearanceAboveRaisedGround()
    {
        var rig = new Rig(1);
        for (var frame = 0; frame < 60; frame++)
            rig.Step(Leg(new Float3(0f, 0.2f, 0f), Quaternion.Identity, Ground(0.1f)));
        Assert.Equal(0, rig.Poses[0].Planted);
        Assert.InRange(rig.Poses[0].Ankle.Y, 0.39f - 2e-3f, 0.39f + 2e-3f);
    }

    [Fact]
    public void NonFiniteInputsPassThroughAndCorruptParamsFallBackToStandard()
    {
        var rig = new Rig(1);
        rig.Step(Leg(Float3.Zero, Quaternion.Identity, Ground()));
        var broken = Leg(Float3.Zero, Quaternion.Identity, Ground());
        broken.Knee = new Float3(float.NaN, 0f, 0f);
        rig.Step(broken);
        Assert.Equal(default, rig.States[0]);
        Assert.Equal(0, rig.Poses[0].Solved);
        Assert.Equal(Quaternion.Identity, rig.Poses[0].Upper);
        var clean = new Rig(1);
        var corrupt = new Rig(1);
        for (var i = 0; i < LegParamsSpec.Count; i++)
            corrupt.Params = LegParamsSpec.With(corrupt.Params, (LegParam)i, float.NaN);
        for (var frame = 0; frame < 20; frame++)
        {
            var input = Leg(new Float3(0f, 0.004f * frame, 0f), Pitched(0.01f * frame), Ground());
            clean.Step(input);
            corrupt.Step(input);
        }
        Assert.Equal(clean.States[0], corrupt.States[0]);
        Assert.Equal(clean.Poses[0], corrupt.Poses[0]);
    }

    [Fact]
    public void SameInputsGiveBitIdenticalResults()
    {
        (LegState, LegPose, SpringState) Run()
        {
            var rig = new Rig(1);
            for (var frame = 0; frame < 120; frame++)
                rig.Step(Leg(new Float3(MathF.Sin(frame * 0.1f) * 0.05f, Math.Max(0f, MathF.Sin(frame * 0.2f)) * 0.2f, 0f), Pitched(MathF.Sin(frame * 0.15f) * 0.4f), Ground()));
            return (rig.States[0], rig.Poses[0], rig.Pelvis);
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void LegsDoNotInfluenceEachOtherWithoutThePelvis()
    {
        LegPose[] Run(float perturbation)
        {
            var rig = new Rig(4);
            rig.Params.PelvisWeight = 0f;
            for (var frame = 0; frame < 30; frame++)
                rig.Step(
                    Leg(new Float3(-0.3f, 0f, 0f), Quaternion.Identity, Ground()),
                    Leg(new Float3(-0.1f, 0.05f, 0f), Pitched(0.2f), Ground()),
                    Leg(new Float3(0.1f, perturbation * frame, 0f), Pitched(-0.3f), Ground(perturbation)),
                    Leg(new Float3(0.3f, 0f, 0.2f), Quaternion.AxisAngle(Up, 0.4f), Ground()));
            return rig.Poses;
        }
        var calm = Run(0f);
        var shaken = Run(0.01f);
        Assert.Equal(calm[0], shaken[0]);
        Assert.Equal(calm[1], shaken[1]);
        Assert.Equal(calm[3], shaken[3]);
        Assert.NotEqual(calm[2], shaken[2]);
    }

    [Fact]
    public void TurningFootIKOffFadesBackToTheAnimationWithoutAJump()
    {
        var rig = new Rig(1);
        for (var frame = 0; frame < 30; frame++)
            rig.Step(Leg(new Float3(0f, 0f, 0.003f * frame), Quaternion.Identity, Ground(-0.06f)));
        var animated = new Float3(0f, 0.09f, 0.09f);
        Assert.True(Distance(rig.Poses[0].Ankle, animated) > 0.05f);
        rig.Weight = 0f;
        var previous = rig.Poses[0].Ankle;
        var largest = 0f;
        for (var frame = 30; frame < 90; frame++)
        {
            rig.Step(Leg(new Float3(0f, 0f, 0.003f * frame), Quaternion.Identity, Ground(-0.06f)));
            largest = Math.Max(largest, Distance(rig.Poses[0].Ankle, previous) - 0.003f);
            previous = rig.Poses[0].Ankle;
        }
        Assert.InRange(largest, 0f, 0.012f);
        Assert.InRange(rig.State.Weight.Value, 0f, 1e-3f);
        Assert.InRange(Distance(rig.Poses[0].Ankle, new Float3(0f, 0.09f, 0.003f * 89)), 0f, 2e-3f);
    }

    [Fact]
    public void EverySettingFallsBackOnItsOwnAndPreparingEqualsSanitizingThenScaling()
    {
        var standard = LegParams.Standard;
        for (var i = 0; i < LegParamsSpec.Count; i++)
            Assert.Equal(standard, LegParamsSpec.Sanitize(LegParamsSpec.With(standard, (LegParam)i, float.NaN)));
        var random = new Random(3);
        foreach (var preset in Enum.GetValues<LegPreset>())
            for (var trial = 0; trial < 50; trial++)
            {
                var p = LegParams.Preset(preset);
                for (var i = 0; i < LegParamsSpec.Count; i++)
                {
                    var bounds = LegParamsSpec.Bounds((LegParam)i);
                    if (random.Next(3) == 0)
                        p = LegParamsSpec.With(p, (LegParam)i, bounds.Minimum - 1f + (float)random.NextDouble() * (bounds.Maximum - bounds.Minimum + 2f));
                }
                var factor = 0.5f + (float)random.NextDouble() * 2f;
                Assert.Equal(LegParamsSpec.Scaled(LegParamsSpec.Sanitize(p), factor), LegParamsSpec.Prepared(p, factor));
            }
    }

    [Fact]
    public void ParameterSchemaDescribesEveryFieldAndRoundTrips()
    {
        var fields = typeof(LegParams).GetFields().Where(f => !f.IsStatic).ToArray();
        Assert.Equal(LegParamsSpec.Count, fields.Length);
        Assert.Equal(LegParamsSpec.Count, Enum.GetValues<LegParam>().Length);
        var names = new HashSet<string>();
        var standard = LegParams.Standard;
        for (var i = 0; i < LegParamsSpec.Count; i++)
        {
            var field = (LegParam)i;
            var text = LegParamsSpec.Text(field);
            Assert.Equal(field.ToString(), text.Name);
            Assert.True(names.Add(text.Name));
            Assert.False(string.IsNullOrWhiteSpace(text.Group));
            Assert.True(text.Description.Length > 20);
            var info = fields.Single(f => f.Name == text.Name);
            Assert.Equal((float)info.GetValue(standard)!, LegParamsSpec.Get(standard, field));
            var changed = LegParamsSpec.With(standard, field, 0.123f);
            Assert.Equal(0.123f, LegParamsSpec.Get(changed, field));
            foreach (var other in fields.Where(f => f.Name != text.Name))
                Assert.Equal(other.GetValue(standard), other.GetValue(changed));
            var bounds = LegParamsSpec.Bounds(field);
            Assert.True(bounds.Minimum < bounds.Maximum);
            var scaled = LegParamsSpec.Get(LegParamsSpec.Scaled(standard, 2f), field);
            Assert.Equal(bounds.ScalesWithBody ? 2f * LegParamsSpec.Get(standard, field) : LegParamsSpec.Get(standard, field), scaled);
        }
        foreach (var preset in Enum.GetValues<LegPreset>())
            Assert.Equal(LegParams.Preset(preset), LegParamsSpec.Sanitize(LegParams.Preset(preset)));
        var inverted = standard;
        inverted.PlantHeight = 0.2f;
        inverted.ReleaseHeight = 0.1f;
        inverted.PinRadius = 0.9f;
        inverted.ReleaseDistance = 0.2f;
        var fixedUp = LegParamsSpec.Sanitize(inverted);
        Assert.Equal(0.2f, fixedUp.ReleaseHeight);
        Assert.Equal(0.9f, fixedUp.ReleaseDistance);
        Assert.Equal(ParamUnit.Meters, LegParamsSpec.Bounds(LegParam.PinRadius).Unit);
    }
}

public class LegDocumentationTests
{
    [Fact]
    public void TheDesignerGuideNamesEverySetting()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SA.Motion.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var guide = File.ReadAllText(Path.Combine(directory!.FullName, "docs", "legs.md"));
        foreach (var field in Enum.GetValues<LegParam>())
            Assert.Contains("`" + field + "`", guide);
    }
}
