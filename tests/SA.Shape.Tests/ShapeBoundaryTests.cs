using SA.Animation;
using SA.Motion.Numerics;
using Xunit;
using static SA.Shape.Tests.ShapeFixture;

namespace SA.Shape.Tests;

public class ShapeBoundaryTests
{
    [Fact]
    public void PreparedShapeFrameCanBeDiscardedAndInvalidFinalOutputCannotCommit()
    {
        var f = new ShapeFixture();
        var frames = new ShapeFrames();
        Assert.Equal(AnimationError.None, f.Retarget());
        var frame = new AnimationFrame(1, 1, 1, .01, .01f);
        Assert.Equal(AnimationError.None, ShapeTimelinePose.Prepare(frame, frames.Contributions, frames.Layers, true, f.Target, frames.Incoming,
            f.Geometry, f.Binding, f.Rows, f.Offsets, f.Scales, frames.State, frames.Bones, frames.BoneScratch, frames.Previous, frames.PreviousLayers,
            frames.Workspace, out var prepared, out var action));
        Assert.Equal(FrameAction.Initialized, action);
        Assert.Equal(default, frames.State);
        Assert.All(frames.Displayed, value => Assert.Equal(default, value));
        Assert.All(frames.Neutral, value => Assert.Equal(default, value));
        Assert.All(frames.Bones, value => Assert.Equal(default, value));
        var good = frames.DisplayScratch[6];
        frames.DisplayScratch[6].Scale = new Float3(1, 2, 1);
        Assert.Equal(AnimationError.InvalidFrame, ShapeTimelinePose.Commit(prepared, frames.Contributions, frames.Layers, frames.BoneScratch, frames.Workspace,
            ref frames.State, frames.Bones, frames.Previous, frames.PreviousLayers));
        Assert.Equal(default, frames.State);
        Assert.All(frames.Neutral, value => Assert.Equal(default, value));
        Assert.All(frames.Bones, value => Assert.Equal(default, value));
        frames.DisplayScratch[6] = good;
        Assert.Equal(AnimationError.None, ShapeTimelinePose.Commit(prepared, frames.Contributions, frames.Layers, frames.BoneScratch, frames.Workspace,
            ref frames.State, frames.Bones, frames.Previous, frames.PreviousLayers));
        Assert.Equal(prepared, frames.State);
        Assert.Equal(frames.DisplayScratch, frames.Displayed);
    }

    [Fact]
    public void StagedMissingDataOnlyProposesTheWaitingFlag()
    {
        var f = new ShapeFixture();
        var frames = new ShapeFrames();
        var frame = new AnimationFrame(1, 1, 1, .01, .01f);
        Assert.Equal(AnimationError.NeedsData, ShapeTimelinePose.Prepare(frame, frames.Contributions, frames.Layers, false, [], [],
            f.Geometry, f.Binding, f.Rows, f.Offsets, f.Scales, frames.State, frames.Bones, frames.BoneScratch, frames.Previous, frames.PreviousLayers,
            frames.Workspace, out var prepared, out var action));
        Assert.Equal(FrameAction.Waiting, action);
        Assert.Equal((byte)1, prepared.Motion.Waiting);
        Assert.Equal(default, frames.State);
        Assert.All(frames.Bones, value => Assert.Equal(default, value));
        Assert.All(frames.Displayed, value => Assert.Equal(default, value));
    }

    [Fact]
    public void UniqueCreaturePathKeepsNonHipTranslationsWhileChangingItsBody()
    {
        int[] parents = [-1, 0, 1, 1];
        Pose[] rest = [Pose.Identity, At(0, 1, 0), At(.5f, .1f, 0), At(0, .2f, .1f)];
        var rows = new RetargetRow[4];
        Assert.Equal(AnimationError.None, RetargetTable.Compile(FamilyId, TargetId, parents, rest, parents, rest, [0, 1, 2, 3], -1, -1, 1,
            new RigidPose[8], rows, out var binding));
        var definition = new ShapeDefinition(new ShapeProfile(DefinitionId, TargetId, default, default, ShapeGrounding.Free),
            [new ShapeChannel(1, 0, 1, .5f)], [new OffsetEffect(0, 2, Float3.Zero, MathF.Log(1.6f) * 2)], [], []);
        var offsets = new Float3[4]; var lengths = new float[4]; var scales = new float[4]; var skins = new Float3[4]; var deformations = new Affine[4]; var models = new RigidPose[4];
        var workspace = new ShapeWorkspace(offsets, lengths, scales, skins, deformations, models);
        Assert.Equal(AnimationError.None, ShapeRig.Evaluate(definition, binding, rows, [.5f], 1, workspace, out var baseGeometry));
        var animated = (Pose[])rest.Clone();
        animated[2].Position += new Float3(.2f, -.03f, .1f);
        animated[3].Position += new Float3(-.1f, .2f, .02f);
        animated[1].Rotation = RotationVector.Exp(new Float3(.4f, -.2f, .1f));
        var sampled = new Pose[4];
        Assert.Equal(AnimationError.None, IdentityRetarget.Apply(FamilyId, FamilyId, parents, animated, models, sampled));
        var neutral = new Pose[4];
        Assert.Equal(AnimationError.None, ShapeMotionSpace.Normalize(baseGeometry, binding, rows, offsets, scales, sampled, models, neutral));
        Assert.Equal(AnimationError.None, ShapeRig.Evaluate(definition, binding, rows, [1], 2, workspace, out var shapedGeometry));
        var output = new Pose[4];
        Assert.Equal(AnimationError.None, ShapeMotionSpace.Compose(shapedGeometry, binding, rows, offsets, scales, neutral, models, output));
        Near(output[2].Position, animated[2].Position * 1.6f);
        Near(output[3].Position, animated[3].Position);
        Near(output[1].Rotation, animated[1].Rotation);
        Assert.Equal(0f, shapedGeometry.HipHeight);
    }

    [Fact]
    public void RandomShapeAndAnimationRoundTripsStayFiniteAndPreserveNeutralHipMotion()
    {
        var random = new Random(11032026);
        var f = new ShapeFixture();
        var input = new Pose[17];
        var neutral = new Pose[17];
        var composed = new Pose[17];
        for (var trial = 0; trial < 300; trial++)
        {
            for (var i = 0; i < f.Values.Length; i++) f.Values[i] = (float)random.NextDouble() * 2 - 1;
            Assert.Equal(AnimationError.None, f.Evaluate((ulong)trial));
            for (var i = 0; i < 17; i++)
            {
                input[i] = f.Base[i];
                if (i > 0) input[i].Rotation = RotationVector.Exp(new Float3((float)random.NextDouble() * 2, (float)random.NextDouble() * 2, (float)random.NextDouble() * 2));
            }
            input[1].Position += new Float3((float)random.NextDouble(), (float)random.NextDouble() - .5f, (float)random.NextDouble());
            Assert.Equal(AnimationError.None, f.Retarget(input));
            Assert.Equal(AnimationError.None, ShapeMotionSpace.Normalize(f.Geometry, f.Binding, f.Rows, f.Offsets, f.Scales, f.Target, f.Models, neutral));
            Assert.Equal(AnimationError.None, ShapeMotionSpace.Compose(f.Geometry, f.Binding, f.Rows, f.Offsets, f.Scales, neutral, f.Models, composed));
            Near(neutral[1].Position, input[1].Position - f.Base[1].Position);
            for (var i = 0; i < 17; i++)
            {
                Assert.True(RigidMath.UnitPose(composed[i]));
                Near(composed[i].Position, f.Target[i].Position);
                Near(composed[i].Rotation, f.Target[i].Rotation);
            }
        }
    }

    [Fact]
    public void GeometryAliasesAndOutOfRangeIndicesCannotAffectCommittedState()
    {
        var f = new ShapeFixture();
        var alias = new ShapeWorkspace(f.Offsets, f.Lengths, f.Lengths, f.SkinScales, f.Deformations, f.Models);
        Assert.Equal(AnimationError.AliasedBuffers, ShapeRig.Evaluate(f.Definition, f.Binding, f.Rows, f.Values, 1, alias, out _));
        var frames = new ShapeFrames();
        var poseAlias = new ShapePoseWorkspace(frames.Neutral, frames.Neutral, frames.Scratch, frames.Displayed, frames.DisplayScratch, frames.Models);
        var frame = new AnimationFrame(1, 1, 1, .01, .01f);
        Assert.Equal(AnimationError.AliasedBuffers, ShapeTimelinePose.Evaluate(frame, frames.Contributions, frames.Layers, false, [], [], f.Geometry, f.Binding,
            f.Rows, f.Offsets, f.Scales, ref frames.State, frames.Bones, frames.BoneScratch, frames.Previous, frames.PreviousLayers, poseAlias, out _));
        Assert.Equal(default, frames.State);
        var invalid = new ShapeDefinition(f.Definition.Profile, f.Channels, [new OffsetEffect(int.MaxValue, 1, Float3.Zero)], [], []);
        Assert.Equal(AnimationError.InvalidTable, ShapeRig.Evaluate(invalid, f.Binding, f.Rows, f.Values, 1, f.Workspace, out _));
        invalid = new ShapeDefinition(f.Definition.Profile, f.Channels, [], [new SkinEffect(1, int.MinValue, Float3.Zero)], []);
        Assert.Equal(AnimationError.InvalidTable, ShapeRig.Evaluate(invalid, f.Binding, f.Rows, f.Values, 1, f.Workspace, out _));
    }
}
