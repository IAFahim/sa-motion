using SA.Motion.Inertialization;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Animation.Tests;

public class TimelineStagingTests
{
    [Fact]
    public void PreparingCanBeDiscardedOrCommittedWithoutChangingTheEvaluationContract()
    {
        var frame = new AnimationFrame(1, 1, 1, .01, .01f);
        ClipContribution[] input = [new(new ClipHandle(0, 1), .1, 1)];
        LayerInput[] layers = [new(0, ClipMode.Override, 0, 1)];
        var state = default(PoseFrameState);
        var bones = new BoneFrameState[1];
        var staged = new BoneFrameState[1];
        var previous = new ClipContribution[1];
        var previousLayers = new LayerInput[1];
        var output = new Pose[1];
        var scratch = new Pose[1];
        Assert.Equal(AnimationError.None, TimelinePose.Prepare(frame, input, layers, true, [Pose.Identity], [default(PoseVelocity)], state, bones, staged,
            previous, previousLayers, output, scratch, out var prepared, out var action));
        Assert.Equal(FrameAction.Initialized, action);
        Assert.Equal(default, state);
        Assert.Equal(default, bones[0]);
        Assert.Equal(default, output[0]);
        Assert.Equal(default, previous[0]);
        Assert.Equal(AnimationError.None, TimelinePose.Commit(prepared, input, layers, staged, scratch, ref state, bones, previous, previousLayers, output));
        Assert.Equal(prepared, state);
        Assert.Equal(Pose.Identity, output[0]);
        Assert.Equal(staged[0], bones[0]);
        Assert.Equal(input, previous);
        Assert.Equal(layers, previousLayers);
    }

    [Fact]
    public void AnInvalidStagedBoneOrHeaderCannotPartlyCommit()
    {
        var frame = new AnimationFrame(1, 1, 1, .01, .01f);
        var state = default(PoseFrameState);
        var bones = new BoneFrameState[2];
        var staged = new BoneFrameState[2];
        var output = new Pose[2];
        var scratch = new Pose[2];
        Assert.Equal(AnimationError.None, TimelinePose.Prepare(frame, [], [], true, [Pose.Identity, Pose.Identity], [], state, bones, staged, [], [], output, scratch, out var prepared, out _));
        var good = prepared;
        prepared.Elapsed = float.NaN;
        Assert.Equal(AnimationError.InvalidFrame, TimelinePose.Commit(prepared, [], [], staged, scratch, ref state, bones, [], [], output));
        staged[1].History.Previous.Position = new Float3(float.PositiveInfinity, 0, 0);
        Assert.Equal(AnimationError.InvalidFrame, TimelinePose.Commit(good, [], [], staged, scratch, ref state, bones, [], [], output));
        Assert.Equal(default, state);
        Assert.All(bones, value => Assert.Equal(default, value));
        Assert.All(output, value => Assert.Equal(default, value));
    }
}
