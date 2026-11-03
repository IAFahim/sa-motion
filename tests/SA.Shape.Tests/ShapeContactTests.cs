using SA.Animation;
using SA.Motion.Numerics;
using Xunit;
using static SA.Shape.Tests.ShapeFixture;

namespace SA.Shape.Tests;

public class ShapeContactTests
{
    [Fact]
    public void PlantUsesTheChangedLengthsAndTheDeformedSoleOffset()
    {
        var f = new ShapeFixture();
        f.Values[0] = 1f; f.Values[1] = 1f;
        Assert.Equal(AnimationError.None, f.Evaluate());
        Assert.Equal(AnimationError.None, f.Retarget());
        var anchor = new Float3(-.15f, .08f, .05f);
        var rotation = RotationVector.Exp(new Float3(.15f, .2f, -.1f));
        Assert.True(ShapeContacts.TryAnkleTarget(f.LeftSole, f.Deformations, anchor, rotation, out var ankle));
        var output = new Pose[17];
        var limb = new ShapeLimb(4, 5, 6);
        Assert.Equal(AnimationError.None, ShapeContacts.Reach(f.Rows, f.Target, limb, new ShapeContactTarget(ankle, new Float3(-.15f, .8f, 1), rotation), f.Models, output, out var result));
        Near(result.Metrics.UpperLength, .5625f);
        Near(result.Metrics.LowerLength, .5625f);
        Assert.InRange(result.Residual, 0, 3e-5f);
        Assert.Equal((byte)0, result.Clamped);
        Assert.True(ShapeContacts.TryMarker(f.Models[6], f.Deformations[6], f.LeftSole.Offset, out var sole));
        Near(sole, anchor, 3e-5f);
        Near(f.Models[6].Rotation, rotation);
        for (var i = 0; i < output.Length; i++) Assert.Equal(f.Target[i].Position, output[i].Position);
        Near(f.Models[4].Position, f.TargetModels[4].Position);
    }

    [Fact]
    public void UnreachableHandReportsResidualWithoutStretchingBones()
    {
        var f = new ShapeFixture();
        f.Values[2] = 1;
        Assert.Equal(AnimationError.None, f.Evaluate());
        Assert.Equal(AnimationError.None, f.Retarget());
        var output = new Pose[17];
        var limb = new ShapeLimb(10, 11, 12);
        var target = new Float3(-5, 2, 1);
        Assert.Equal(AnimationError.None, ShapeContacts.Reach(f.Rows, f.Target, limb, new ShapeContactTarget(target, new Float3(-1, 3, 2), Quaternion.Identity), f.Models, output, out var result));
        Near(result.Metrics.MaximumReach, .975f);
        Assert.Equal((byte)1, result.Clamped);
        Assert.True(result.Residual > 3f);
        Near((f.Models[12].Position - f.Models[10].Position).Length, .975f, 4e-5f);
        for (var i = 0; i < output.Length; i++) Assert.Equal(f.Target[i].Position, output[i].Position);
    }

    [Fact]
    public void RotatedParentsAndInterveningTwistBonesReachTheTarget()
    {
        int[] parents = [-1, 0, 1, 2, 3, 4, 2];
        Pose[] local = [Pose.Identity, At(.3f, 1, .2f), At(.1f, .1f, .1f), At(.5f, .3f, .1f), At(.1f, -.1f, .2f), At(.3f, -.1f, .2f), At(.1f, .2f, .1f)];
        var rows = new RetargetRow[7];
        for (var i = 0; i < local.Length; i++)
        {
            local[i].Rotation = i == 0 ? Quaternion.Identity : RotationVector.Exp(new Float3(i * .03f, -.1f * i, .05f * i));
            rows[i] = new RetargetRow(parents[i], -1, RetargetRowMode.Static, local[i], Quaternion.Identity, Quaternion.Identity);
        }
        var output = new Pose[7];
        var models = new RigidPose[7];
        var target = new Float3(.7f, 1.3f, .3f);
        var orientation = RotationVector.Exp(new Float3(-.5f, .2f, .3f));
        Assert.Equal(AnimationError.None, ShapeContacts.Reach(rows, local, new ShapeLimb(1, 3, 5), new ShapeContactTarget(target, new Float3(0, 2, 2), orientation), models, output, out var result));
        Near(result.Reached, target, 4e-5f);
        Near(models[5].Rotation, orientation);
        Near(output[2].Rotation, local[2].Rotation);
        Near(output[4].Rotation, local[4].Rotation);
        Near(output[6].Rotation, local[6].Rotation);
        for (var i = 0; i < output.Length; i++) Assert.Equal(local[i].Position, output[i].Position);
    }

    [Fact]
    public void CoincidentTargetAndCollinearPoleHaveFiniteDefinedResults()
    {
        var f = new ShapeFixture();
        Assert.Equal(AnimationError.None, f.Retarget());
        var limb = new ShapeLimb(4, 5, 6);
        var start = f.TargetModels[4].Position;
        var output = new Pose[17];
        Assert.Equal(AnimationError.None, ShapeContacts.Reach(f.Rows, f.Target, limb, new ShapeContactTarget(start, start, default, false), f.Models, output, out var result));
        Assert.True(float.IsFinite(result.Residual));
        Assert.Equal((byte)1, result.Clamped);
        foreach (var pose in output) Assert.True(RigidMath.UnitPose(pose));
    }

    [Fact]
    public void AsymmetricLegsOnlyGroundTheLowestSoleBeforeIk()
    {
        var f = new ShapeFixture();
        f.Values[0] = 1;
        var definition = new ShapeDefinition(f.Definition.Profile, f.Channels,
            [new OffsetEffect(0, 5, Float3.Zero, MathF.Log(1.5f)), new OffsetEffect(0, 6, Float3.Zero, MathF.Log(1.5f))], [], []);
        Assert.Equal(AnimationError.None, ShapeRig.Evaluate(definition, f.Binding, f.Rows, f.Values, 1, f.Workspace, out var geometry));
        Near(geometry.GroundY, 0);
        Assert.True(ShapeContacts.TryMarker(f.Models[6], f.Deformations[6], f.LeftSole.Offset, out var left));
        Assert.True(ShapeContacts.TryMarker(f.Models[9], f.Deformations[9], f.RightSole.Offset, out var right));
        Near(left.Y, 0);
        Near(right.Y, .45f);
    }

    [Fact]
    public void BadLimbAndAliasedOutputAreRejected()
    {
        var f = new ShapeFixture();
        Assert.Equal(AnimationError.None, f.Retarget());
        var target = new ShapeContactTarget(Float3.Zero, Float3.One, Quaternion.Identity);
        Assert.Equal(AnimationError.InvalidTable, ShapeContacts.Measure(f.Rows, f.Target, new ShapeLimb(4, 8, 9), f.Models, out _));
        Assert.Equal(AnimationError.InvalidTable, ShapeContacts.Measure(f.Rows, f.Target, new ShapeLimb(0, 1, 2), f.Models, out _));
        Assert.Equal(AnimationError.AliasedBuffers, ShapeContacts.Reach(f.Rows, f.Target, new ShapeLimb(4, 5, 6), target, f.Models, f.Target, out _));
        Assert.False(ShapeContacts.TryAnkleTarget(new ShapeMarker(999, Float3.Zero), f.Deformations, Float3.Zero, Quaternion.Identity, out _));
        Assert.False(ShapeContacts.TryAnkleTarget(f.LeftSole, f.Deformations, Float3.Zero, default, out _));
    }
}
