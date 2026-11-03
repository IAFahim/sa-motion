using SA.Animation;
using SA.Motion.Inertialization;
using SA.Motion.Numerics;
using Xunit;
using static SA.Shape.Tests.ShapeFixture;

namespace SA.Shape.Tests;

public class ShapeTimelineTests
{
    [Fact]
    public void ChangingProportionsAndInterruptingCutsKeepTheSameNeutralHistory()
    {
        var fixedShape = new ShapeFixture();
        var growing = new ShapeFixture();
        var fixedFrames = new ShapeFrames();
        var growingFrames = new ShapeFrames();
        for (var tick = 0; tick < 80; tick++)
        {
            growing.Values[0] = .6f * MathF.Sin(tick * .1f);
            growing.Values[1] = -.5f * MathF.Sin(tick * .13f);
            growing.Values[2] = .8f * MathF.Sin(tick * .08f);
            Assert.Equal(AnimationError.None, growing.Evaluate((ulong)tick + 2));
            var input = (Pose[])fixedShape.Base.Clone();
            var continuity = tick < 10 ? 1UL : tick < 18 ? 2UL : 3UL;
            var clip = (uint)continuity;
            input[1].Position += new Float3(continuity == 1 ? .01f * tick : continuity == 2 ? 2f : -1f, -.1f, .1f);
            for (var i = 1; i < input.Length; i++) input[i].Rotation = RotationVector.Exp(new Float3(.02f * i * (float)continuity, .01f * tick, -.04f * i));
            Assert.Equal(AnimationError.None, fixedShape.Retarget(input));
            Assert.Equal(AnimationError.None, growing.Retarget(input));
            Assert.Equal(AnimationError.None, fixedFrames.Step(fixedShape, tick, continuity, clip));
            Assert.Equal(AnimationError.None, growingFrames.Step(growing, tick, continuity, clip));
            Assert.Equal(fixedFrames.Action, growingFrames.Action);
            for (var i = 0; i < growing.Rows.Length; i++)
            {
                Near(growingFrames.Neutral[i].Position, fixedFrames.Neutral[i].Position, 5e-5f);
                Near(growingFrames.Neutral[i].Rotation, fixedFrames.Neutral[i].Rotation, 5e-5f);
                if (i != 1) Assert.Equal(growing.Offsets[i], growingFrames.Displayed[i].Position);
                Assert.Equal(Float3.One, growingFrames.Displayed[i].Scale);
            }
        }
        Near(growingFrames.Bones[1].History.Current.Position, fixedFrames.Bones[1].History.Current.Position);
        Near(growingFrames.State.Motion.Elapsed, .2f);
    }

    [Fact]
    public void GeometryChangeDoesNotMasqueradeAsMotionOnAnOtherwiseStationaryRig()
    {
        var f = new ShapeFixture();
        var frames = new ShapeFrames();
        for (var tick = 0; tick < 12; tick++)
        {
            f.Values[0] = tick / 12f;
            Assert.Equal(AnimationError.None, f.Evaluate((ulong)tick + 1));
            Assert.Equal(AnimationError.None, f.Retarget());
            Assert.Equal(AnimationError.None, frames.Step(f, tick));
        }
        foreach (var bone in frames.Bones)
        {
            Near(bone.History.Current.Position, Float3.Zero);
            Assert.True(PoseVelocity.TryBetween(bone.History.Previous, bone.History.Current, bone.History.Interval, out var velocity));
            Near(velocity.Linear, Float3.Zero);
        }
        f.Target[1].Position.X = 5;
        Assert.Equal(AnimationError.None, frames.Step(f, 12, 2, 1));
        Near(frames.Bones[1].Decay.PositionVelocity, Float3.Zero);
    }

    [Fact]
    public void SameTickAndPausedShapeEditsRequireAnExplicitSeek()
    {
        var f = new ShapeFixture();
        var frames = new ShapeFrames();
        Assert.Equal(AnimationError.None, f.Retarget());
        Assert.Equal(AnimationError.None, frames.Step(f, 0));
        var before = (Pose[])frames.Displayed.Clone();
        var state = frames.State;
        Assert.Equal(AnimationError.None, frames.Step(f, 0));
        Assert.Equal(FrameAction.Reused, frames.Action);
        f.Values[0] = 1f;
        Assert.Equal(AnimationError.None, f.Evaluate(2));
        Assert.Equal(AnimationError.None, f.Retarget());
        Assert.Equal(AnimationError.TickConflict, frames.Step(f, 0));
        Assert.Equal(AnimationError.TickConflict, frames.Step(f, 1, mode: FrameMode.Pause));
        Assert.Equal(state, frames.State);
        Assert.Equal(before, frames.Displayed);
        Assert.Equal(AnimationError.None, frames.Step(f, 1, mode: FrameMode.Seek));
        Assert.Equal(FrameAction.Sought, frames.Action);
        Near(frames.Displayed[1].Position, f.Target[1].Position);
        Assert.Equal((uint)1, frames.Bones[1].History.Count);
    }

    [Fact]
    public void MissingDataHoldsGeometryAndRetriesTheSameBoundary()
    {
        var f = new ShapeFixture();
        var frames = new ShapeFrames();
        Assert.Equal(AnimationError.None, f.Retarget());
        Assert.Equal(AnimationError.None, frames.Step(f, 0));
        var before = (Pose[])frames.Displayed.Clone();
        f.Values[2] = 1;
        Assert.Equal(AnimationError.None, f.Evaluate(2));
        Assert.Equal(AnimationError.NeedsData, frames.Step(f, 1, 2, 1, ready: false));
        Assert.Equal((ulong)0, frames.State.Motion.LastFrame.Tick);
        Assert.Equal((ulong)1, frames.State.Revision);
        Assert.Equal((byte)1, frames.State.Motion.Waiting);
        Assert.Equal(before, frames.Displayed);
        Assert.Equal(AnimationError.None, f.Retarget());
        Assert.Equal(AnimationError.None, frames.Step(f, 1, 2, 1));
        Assert.Equal(FrameAction.Transitioned, frames.Action);
        Assert.Equal((ulong)2, frames.State.Revision);
        Assert.Equal((byte)0, frames.State.Motion.Waiting);
        Assert.Equal(AnimationError.None, frames.Step(f, 1, 2, 1));
        Assert.Equal(FrameAction.Reused, frames.Action);
    }

    [Fact]
    public void FailedFinalCompositionDoesNotCommitHistorySnapshotsOrDisplay()
    {
        var f = new ShapeFixture();
        var frames = new ShapeFrames();
        Assert.Equal(AnimationError.None, f.Retarget());
        f.Target[1].Position.Y = 1e38f;
        Assert.Equal(AnimationError.None, frames.Step(f, 0));
        var state = frames.State;
        var bones = (BoneFrameState[])frames.Bones.Clone();
        var display = (Pose[])frames.Displayed.Clone();
        var neutral = (Pose[])frames.Neutral.Clone();
        var previous = (ClipContribution[])frames.Previous.Clone();
        var previousLayers = (LayerInput[])frames.PreviousLayers.Clone();
        f.Geometry = new ShapeGeometry(DefinitionId, 2, new RetargetGeometry(TargetId, new Float3(0, 3e38f, 0), 4f), 1, 1, 0, 0);
        f.Offsets[1] = f.Geometry.Retarget.HipReference;
        f.Scales[1] = 4f;
        f.Target[1].Position = f.Offsets[1];
        Assert.Equal(AnimationError.InvalidFrame, frames.Step(f, 1, 2, 1));
        Assert.Equal(state, frames.State);
        Assert.Equal(bones, frames.Bones);
        Assert.Equal(display, frames.Displayed);
        Assert.Equal(neutral, frames.Neutral);
        Assert.Equal(previous, frames.Previous);
        Assert.Equal(previousLayers, frames.PreviousLayers);
    }

    [Fact]
    public void NewDefinitionRequiresAnEpochOrSeekAndDishonestRevisionsAreDetected()
    {
        var f = new ShapeFixture();
        var frames = new ShapeFrames();
        Assert.Equal(AnimationError.None, f.Retarget());
        Assert.Equal(AnimationError.None, frames.Step(f, 0));
        f.Offsets[11].X -= .1f;
        Assert.Equal(AnimationError.TickConflict, frames.Step(f, 0));
        f.Offsets[11] = f.Base[11].Position;
        var g = f.Geometry;
        f.Geometry = new ShapeGeometry(FamilyId, g.Revision, g.Retarget, g.BaseHipHeight, g.HipHeight, g.GroundY, g.GroundShift);
        Assert.Equal(AnimationError.InvalidTable, frames.Step(f, 1));
        Assert.Equal(AnimationError.None, frames.Step(f, 0, epoch: 2));
        Assert.Equal(FrameAction.Initialized, frames.Action);
        Assert.Equal(FamilyId, frames.State.Definition);
    }

    [Fact]
    public void FrozenGeometryVelocityRemainsInSourceMetresPerSimulationSecond()
    {
        var f = new ShapeFixture();
        f.Values[0] = .8f; f.Values[2] = .4f;
        Assert.Equal(AnimationError.None, f.Evaluate());
        var previous = new Pose[17];
        var current = new Pose[17];
        var velocities = new PoseVelocity[17];
        var input = (Pose[])f.Base.Clone();
        Assert.Equal(AnimationError.None, f.Retarget(input));
        Assert.Equal(AnimationError.None, ShapeMotionSpace.Normalize(f.Geometry, f.Binding, f.Rows, f.Offsets, f.Scales, f.Target, f.Models, previous));
        input[1].Position += new Float3(-.2f, .1f, .3f);
        Assert.Equal(AnimationError.None, f.Retarget(input));
        Assert.Equal(AnimationError.None, ShapeMotionSpace.Normalize(f.Geometry, f.Binding, f.Rows, f.Offsets, f.Scales, f.Target, f.Models, current));
        Assert.Equal(AnimationError.None, ShapeMotionSpace.Differentiate(previous, current, .01f, velocities));
        Near(velocities[1].Linear, new Float3(-20, 10, 30), 5e-5f);
        var output = new Pose[17];
        Assert.Equal(AnimationError.None, ShapeMotionSpace.Compose(f.Geometry, f.Binding, f.Rows, f.Offsets, f.Scales, current, f.Models, output));
        for (var i = 0; i < output.Length; i++) Near(output[i].Position, f.Target[i].Position);
    }

    [Fact]
    public void HipResidualUsesModelSpaceEvenWithAnimatedAndShapedAncestors()
    {
        int[] parents = [-1, 0, 1, 2, 3];
        Pose[] rest = [Pose.Identity, At(.2f, .1f, 0), At(0, 1, 0), At(0, -.45f, 0), At(0, -.45f, 0)];
        rest[1].Rotation = RotationVector.Exp(new Float3(.3f, -.2f, .4f));
        var rows = new RetargetRow[5];
        Assert.Equal(AnimationError.None, RetargetTable.Compile(FamilyId, TargetId, parents, rest, parents, rest, [0, 1, 2, 3, 4], 2, 2, 1,
            new RigidPose[10], rows, out var binding));
        var offsets = new Float3[5]; var lengths = new float[5]; var scales = new float[5]; var skins = new Float3[5]; var deformations = new Affine[5]; var models = new RigidPose[5];
        var definition = new ShapeDefinition(new ShapeProfile(DefinitionId, TargetId, new ShapeMarker(4, new Float3(0, -.1f, 0)), new ShapeMarker(4, new Float3(0, -.1f, 0))),
            [new ShapeChannel(1, -1, 1)], [new OffsetEffect(0, 1, new Float3(.4f, .1f, .2f)), new OffsetEffect(0, 3, Float3.Zero, MathF.Log(1.3f)), new OffsetEffect(0, 4, Float3.Zero, MathF.Log(1.3f))], [], []);
        var input = (Pose[])rest.Clone();
        input[1].Rotation = RotationVector.Exp(new Float3(-.2f, .4f, .5f));
        input[2].Position += new Float3(.2f, .1f, -.2f);
        var neutral = new[] { new Pose[5], new Pose[5] };
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.Equal(AnimationError.None, ShapeRig.Evaluate(definition, binding, rows, [pass], (ulong)pass, new ShapeWorkspace(offsets, lengths, scales, skins, deformations, models), out var geometry));
            var local = new Pose[5];
            Assert.Equal(AnimationError.None, RetargetTable.ApplyGeometry(FamilyId, binding, geometry.Retarget, parents, input, rows, offsets, new RigidPose[5], models, local));
            Assert.Equal(AnimationError.None, ShapeMotionSpace.Normalize(geometry, binding, rows, offsets, scales, local, models, neutral[pass]));
            var composed = new Pose[5];
            Assert.Equal(AnimationError.None, ShapeMotionSpace.Compose(geometry, binding, rows, offsets, scales, neutral[pass], models, composed));
            for (var i = 0; i < 5; i++) Near(composed[i].Position, local[i].Position);
        }
        for (var i = 0; i < 5; i++) Near(neutral[0][i].Position, neutral[1][i].Position);
    }
}
