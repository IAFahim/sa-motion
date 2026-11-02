using SA.Animation.Content;
using SA.Motion.Inertialization;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Animation.Tests;

public class TimelineFoundationTests
{
    sealed class FrameFixture
    {
        internal PoseFrameState State;
        internal readonly BoneFrameState[] Bones = new BoneFrameState[1];
        internal readonly BoneFrameState[] BoneScratch = new BoneFrameState[1];
        internal readonly ClipContribution[] Previous = new ClipContribution[8];
        internal readonly LayerInput[] PreviousLayers = new LayerInput[4];
        internal readonly Pose[] Output = new Pose[1];
        internal readonly Pose[] Scratch = new Pose[1];
        internal FrameAction Action;
        internal AnimationError Step(int tick, ulong continuity, float x, uint clip = 0, bool ready = true, FrameMode mode = FrameMode.Advance, uint epoch = 1, float weight = 1, bool reverse = false)
        {
            var frame = new AnimationFrame((ulong)tick, epoch, continuity, tick * .01, .01f, .2f, mode);
            var contributions = new[] { new ClipContribution(new ClipHandle(clip, 1), (reverse ? -1 : 1) * tick * .01, weight) };
            return TimelinePose.Evaluate(frame, contributions, [new LayerInput(0, ClipMode.Override, 0, 1)], ready,
                ready ? [SharedCodecTests.At(new Float3(x, 0, 0))] : [], [default(PoseVelocity)], ref State, Bones, BoneScratch, Previous, PreviousLayers, Output, Scratch, out Action);
        }
    }

    [Fact]
    public void RepeatedContinuityFinishesAndInterruptionUsesCorrectedOutput()
    {
        var fixture = new FrameFixture();
        Assert.Equal(AnimationError.None, fixture.Step(0, 1, 0));
        Assert.Equal(FrameAction.Initialized, fixture.Action);
        Assert.Equal(AnimationError.None, fixture.Step(1, 1, .1f));
        Assert.Equal(AnimationError.None, fixture.Step(2, 2, 10, 1));
        Assert.Equal(FrameAction.Transitioned, fixture.Action);
        Assert.InRange(fixture.Output[0].Position.X, .09999f, .10001f);
        Assert.Equal(AnimationError.None, fixture.Step(3, 2, 10, 1));
        var corrected = fixture.Output[0];
        Assert.Equal(AnimationError.None, fixture.Step(4, 3, -5, 2));
        Assert.InRange((corrected.Position - fixture.Output[0].Position).Length, 0, 1e-5f);
        for (var tick = 5; tick <= 26; tick++) Assert.Equal(AnimationError.None, fixture.Step(tick, 3, -5, 2));
        Assert.Equal(new Float3(-5, 0, 0), fixture.Output[0].Position);
        Assert.Equal(.2f, fixture.State.Elapsed);
        Assert.Equal(FrameAction.Advanced, fixture.Action);
    }

    [Fact]
    public void MissingIncomingDataDoesNotConsumeTheBoundaryAndMayRetryTheSameTick()
    {
        var fixture = new FrameFixture();
        Assert.Equal(AnimationError.None, fixture.Step(0, 1, 0));
        Assert.Equal(AnimationError.None, fixture.Step(1, 1, .1f));
        var before = fixture.Output[0];
        Assert.Equal(AnimationError.NeedsData, fixture.Step(2, 2, 10, 1, false));
        Assert.Equal((ulong)1, fixture.State.LastFrame.Tick);
        Assert.Equal((byte)1, fixture.State.Waiting);
        Assert.Equal(before, fixture.Output[0]);
        Assert.Equal(AnimationError.None, fixture.Step(2, 2, 10, 1));
        Assert.Equal(FrameAction.Transitioned, fixture.Action);
        Assert.Equal((byte)0, fixture.State.Waiting);
        Assert.InRange((before.Position - fixture.Output[0].Position).Length, 0, 1e-5f);
        var state = fixture.State;
        Assert.Equal(AnimationError.None, fixture.Step(2, 2, 10, 1));
        Assert.Equal(FrameAction.Reused, fixture.Action);
        Assert.Equal(state, fixture.State);
    }

    [Fact]
    public void TickConflictAndMembershipMisusePreserveCommittedOutput()
    {
        var fixture = new FrameFixture();
        Assert.Equal(AnimationError.None, fixture.Step(0, 1, 0));
        Assert.Equal(AnimationError.None, fixture.Step(1, 1, 1));
        var before = fixture.Output[0];
        var state = fixture.State;
        Assert.Equal(AnimationError.TickConflict, fixture.Step(1, 1, 1, weight: .5f));
        Assert.Equal(state, fixture.State);
        Assert.Equal(before, fixture.Output[0]);
        Assert.Equal(AnimationError.ContinuityMisuse, fixture.Step(2, 1, 10, 2));
        Assert.Equal(state, fixture.State);
        Assert.Equal(before, fixture.Output[0]);
    }

    [Fact]
    public void SeekAndPoolEpochResetHistoryWhileReversePlaybackStillAdvancesDecay()
    {
        var fixture = new FrameFixture();
        Assert.Equal(AnimationError.None, fixture.Step(0, 1, 0, reverse: true));
        Assert.Equal(AnimationError.None, fixture.Step(1, 2, 10, 1, reverse: true));
        Assert.Equal(AnimationError.None, fixture.Step(2, 2, 10, 1, reverse: true));
        Assert.True(fixture.State.Elapsed > 0);
        Assert.Equal(AnimationError.None, fixture.Step(0, 2, -8, 1, mode: FrameMode.Seek));
        Assert.Equal(FrameAction.Sought, fixture.Action);
        Assert.Equal(-8, fixture.Output[0].Position.X);
        Assert.Equal((uint)1, fixture.Bones[0].History.Count);
        Assert.Equal(AnimationError.None, fixture.Step(0, 9, 5, 2, epoch: 2));
        Assert.Equal(FrameAction.Initialized, fixture.Action);
        Assert.Equal(5, fixture.Output[0].Position.X);
        Assert.Equal(0, fixture.State.Duration);
    }

    [Fact]
    public void CanonicalLayerMasksAndLogAdditiveRespectStableOrder()
    {
        var reference = new[] { Pose.Identity, SharedCodecTests.At(new Float3(0, 1, 0)) };
        var decoded = new[] { Pose.Identity, SharedCodecTests.At(new Float3(2, 1, 0)), Pose.Identity, SharedCodecTests.At(new Float3(6, 1, 0)), Pose.Identity, SharedCodecTests.At(new Float3(0, 1, 0)) };
        decoded[5].Rotation = RotationVector.Exp(new Float3(0, 0, Scalar.Pi / 2));
        var contributions = new[] { new ClipContribution(new ClipHandle(0, 1), 0, .25f), new ClipContribution(new ClipHandle(1, 1), 0, .75f), new ClipContribution(new ClipHandle(2, 1), 0, .5f, 1, 0) };
        var layers = new[] { new LayerInput(0, ClipMode.Override, 0, 2), new LayerInput(1, ClipMode.Additive, 2, 1) };
        var additive = new Pose[6];
        for (var i = 0; i < 6; i++) additive[i] = reference[i % 2];
        var output = new Pose[2];
        Assert.Equal(AnimationError.None, CanonicalBlend.Evaluate(contributions, layers, decoded, additive, [0, 1], reference, output));
        Assert.Equal(5, output[1].Position.X);
        var angle = RotationVector.Log(output[1].Rotation).Z;
        Assert.InRange(angle, Scalar.Pi / 4 - 1e-5f, Scalar.Pi / 4 + 1e-5f);
        Assert.Equal(Pose.Identity, output[0]);
    }

    [Fact]
    public void EvictionHonorsPinsGenerationsAndGenerationOverflowRetiresTheSlot()
    {
        var slots = new ResidencySlot[2];
        Assert.Equal(AnimationError.None, Residency.Publish(slots, 0, new AssetId(1, 0, 0, 0), 0, 100, 1, out var a));
        Assert.Equal(AnimationError.None, Residency.Publish(slots, 1, new AssetId(2, 0, 0, 0), 0, 100, 2, out _));
        Assert.Equal(AnimationError.None, Residency.Pin(slots, a, 3));
        Assert.Equal(1, Residency.EvictionCandidate(slots));
        Assert.Equal(AnimationError.BudgetExceeded, Residency.Evict(slots, 0, out _));
        Assert.Equal(AnimationError.None, Residency.Release(slots, a));
        Assert.Equal(AnimationError.InvalidHandle, Residency.Release(slots, a));
        Assert.Equal(AnimationError.None, Residency.Evict(slots, 0, out _));
        Assert.Equal(AnimationError.None, Residency.Publish(slots, 0, new AssetId(3, 0, 0, 0), 0, 100, 4, out var replacement));
        Assert.NotEqual(a.Generation, replacement.Generation);
        Assert.Equal(AnimationError.InvalidHandle, Residency.Pin(slots, a, 5));
        slots[0].Generation = uint.MaxValue;
        Assert.Equal(AnimationError.None, Residency.Evict(slots, 0, out _));
        Assert.Equal(AnimationError.BudgetExceeded, Residency.Publish(slots, 0, new AssetId(4, 0, 0, 0), 0, 100, 5, out _));
    }

    [Fact]
    public void AssignmentIsGlobalOneToOneAndReportsTiesAndMissingMatches()
    {
        Assert.Equal(AnimationError.None, MappingAssignment.Solve([100, 99, 98, 1], 2, 2, 15, out var mapped));
        Assert.Equal(new[] { 1, 0 }, mapped!.BoneOfRole);
        Assert.Equal(197, mapped.Score);
        Assert.Equal(AnimationError.None, MappingAssignment.Solve([80, 80, 80, 80, 0, 0], 3, 2, 15, out mapped));
        Assert.Equal(-1, mapped!.BoneOfRole[2]);
        Assert.Equal((byte)1, mapped.Ambiguous[0]);
        Assert.Equal((byte)1, mapped.Ambiguous[1]);
    }

    [Fact]
    public void CalibrationUsesTheSecondaryHintToResolveRoll()
    {
        Assert.True(CalibrationFrame.TryRotation(new Float3(1, 0, 0), new Float3(0, 1, 0), new Float3(0, 0, 1), new Float3(0, -1, 0), out var q));
        Assert.InRange((Quaternion.Rotate(q, new Float3(1, 0, 0)) - new Float3(0, 0, 1)).Length, 0, 1e-5f);
        Assert.InRange((Quaternion.Rotate(q, new Float3(0, 1, 0)) - new Float3(0, -1, 0)).Length, 0, 1e-5f);
        Assert.False(CalibrationFrame.TryRotation(new Float3(1, 0, 0), new Float3(2, 0, 0), new Float3(1, 0, 0), new Float3(0, 1, 0), out _));
    }
}
