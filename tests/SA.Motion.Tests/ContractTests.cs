using SA.Motion.Inertialization;
using SA.Motion.Numerics;
using Xunit;
using Quaternion = SA.Motion.Numerics.Quaternion;

namespace SA.Motion.Tests;

public class ContractTests
{
    static void Near(float expected, float actual, float tolerance = 2e-4f) => Assert.InRange(actual, expected - tolerance, expected + tolerance);
    static void Near(Float3 expected, Float3 actual, float tolerance = 2e-4f)
    {
        Near(expected.X, actual.X, tolerance);
        Near(expected.Y, actual.Y, tolerance);
        Near(expected.Z, actual.Z, tolerance);
    }
    static void Near(Quaternion expected, Quaternion actual) => Near(1f, Math.Abs(Quaternion.Dot(Quaternion.Normalize(expected), Quaternion.Normalize(actual))), 2e-5f);

    [Fact]
    public void PerpendicularVelocity_IsProjectedAway_NoFullVectorC1()
    {
        var current = new Float3(1, 0, 0);
        var previous = new Float3(1, 0.2f, 0);
        var offset = VectorOffset.Create(current, previous, Float3.Zero, 0.1f, 0.5f);
        var sourceVelocity = (current - previous) / 0.1f;
        Near(-2f, sourceVelocity.Y, 1e-3f);
        var start = offset.Direction * offset.Decay.Evaluate(0f).Velocity;
        Near(0f, start.X, 1e-6f);
        Near(0f, start.Y, 1e-6f);
        Near(0f, start.Z, 1e-6f);
    }

    [Fact]
    public void StartAcceleration_FollowsPolynomialWithoutOutgoingMatching()
    {
        var decay = ScalarDecay.Create(1.2f, -4f, 1f);
        Near(-4f, decay.Evaluate(0f).Velocity, 1e-4f);
        Near(8f, decay.Evaluate(0f).Acceleration, 1e-2f);
    }

    [Fact]
    public void EndpointAndBeyond_ReturnDestinationWithoutResidual()
    {
        var offset = VectorOffset.Create(new Float3(1, 0, 0), new Float3(1.2f, 0, 0), Float3.Zero, 0.1f, 0.5f);
        Assert.True(offset.Decay.Duration > 0f);
        Assert.Equal(Float3.Zero, offset.Apply(Float3.Zero, offset.Decay.Duration));
        Assert.Equal(Float3.Zero, offset.Apply(Float3.Zero, offset.Decay.Duration * 10f));
        Assert.Equal(Float3.Zero, offset.Apply(Float3.Zero, float.MaxValue));
        var target = Quaternion.AxisAngle(new Float3(0, 0, 1), 0.7f);
        var current = Quaternion.Multiply(Quaternion.AxisAngle(new Float3(0, 0, 1), 1.2f), target);
        var rotation = RotationOffset.Create(current, current, target, 0.1f, 0.5f);
        Assert.True(rotation.Decay.Duration > 0f);
        Near(target, rotation.Apply(target, rotation.Decay.Duration));
        Near(target, rotation.Apply(target, rotation.Decay.Duration * 10f));
    }

    [Fact]
    public void TerminalJerk_IsNotGuaranteedAfterA0Clamp()
    {
        var decay = ScalarDecay.Create(1.2f, 0f, 0.5f);
        double jerk = 6.0 * decay.C3 + 24.0 * decay.C4 + 60.0 * decay.C5;
        Assert.True(Math.Abs(jerk) > 1.0);
        var end = decay.Evaluate(decay.Duration);
        Near(0f, end.Value, 0f);
        Near(0f, end.Velocity, 0f);
        Near(0f, end.Acceleration, 0f);
    }

    [Fact]
    public void TransitionDependsOnlyOnLatestCorrectedHistory()
    {
        var first = Pose.Identity;
        first.Position = new Float3(5, 0, 0);
        var middle = Pose.Identity;
        middle.Position = new Float3(2.1f, 0, 0);
        var latest = Pose.Identity;
        latest.Position = new Float3(2, 0, 0);
        var longPath = new PoseHistory();
        longPath.Reset(first);
        longPath.Capture(middle, 0.02f);
        longPath.Capture(latest, 0.05f);
        var shortPath = new PoseHistory();
        shortPath.Reset(middle);
        shortPath.Capture(latest, 0.05f);
        var a = PoseTransition.Create(longPath, Pose.Identity, 0.5f);
        var b = PoseTransition.Create(shortPath, Pose.Identity, 0.5f);
        Assert.Equal(a.Position.Decay.C0, b.Position.Decay.C0);
        Assert.Equal(a.Position.Decay.C1, b.Position.Decay.C1);
        Assert.Equal(a.Position.Decay.Duration, b.Position.Decay.Duration);
        Assert.Equal(a.Rotation.Decay.C0, b.Rotation.Decay.C0);
        Assert.Equal(a.Rotation.Decay.Duration, b.Rotation.Decay.Duration);
    }

    [Fact]
    public void HistoryRetainsFullOutputKinematicsAndInterval()
    {
        var first = Pose.Identity;
        first.Position = new Float3(1, 2, 3);
        first.Rotation = Quaternion.AxisAngle(new Float3(0, 1, 0), 0.6f);
        first.Scale = new Float3(2, 2, 2);
        var second = Pose.Identity;
        second.Position = new Float3(4, 5, 6);
        second.Rotation = Quaternion.AxisAngle(new Float3(1, 0, 0), 0.3f);
        second.Scale = new Float3(3, 3, 3);
        var history = new PoseHistory();
        history.Reset(first);
        history.Capture(second, 0.02f);
        Assert.Equal(2u, history.Count);
        Near(0.02f, history.Interval, 0f);
        Near(second.Position, history.Current.Position);
        Near(second.Rotation, history.Current.Rotation);
        Near(second.Scale, history.Current.Scale);
        Near(first.Position, history.Previous.Position);
        Near(first.Rotation, history.Previous.Rotation);
    }

    [Fact]
    public void MissingOrInvalidHistoryInterval_UsesZeroInitialVelocity()
    {
        var current = new Float3(2, 0, 0);
        var previous = new Float3(3, 0, 0);
        foreach (var interval in new float[] { 0f, -0.1f, float.NaN })
        {
            var offset = VectorOffset.Create(current, previous, Float3.Zero, interval, 0.5f);
            Near(0f, offset.Decay.Evaluate(0f).Velocity, 0f);
            Near(current, offset.Apply(Float3.Zero, 0f));
        }
        var rotation = RotationOffset.Create(
            Quaternion.AxisAngle(new Float3(0, 0, 1), 1f),
            Quaternion.AxisAngle(new Float3(0, 0, 1), 1.2f),
            Quaternion.Identity, 0f, 0.5f);
        Near(0f, rotation.Decay.Evaluate(0f).Velocity, 0f);
    }

    [Fact]
    public void InvalidRequestedDuration_UsesDestinationDirectly()
    {
        var source = Pose.Identity;
        source.Position = new Float3(2, 0, 0);
        source.Rotation = Quaternion.AxisAngle(new Float3(0, 1, 0), 0.8f);
        var history = new PoseHistory();
        history.Reset(source);
        foreach (var seconds in new float[] { float.NaN, -1f, 0f })
        {
            var transition = PoseTransition.Create(history, Pose.Identity, seconds);
            Assert.Equal(0f, transition.Position.Decay.Duration);
            Assert.Equal(0f, transition.Rotation.Decay.Duration);
            var output = PoseTransition.Apply(ref transition, Pose.Identity, 0f);
            Near(Pose.Identity.Position, output.Position);
            Near(Pose.Identity.Rotation, output.Rotation);
        }
    }

    [Fact]
    public void EpsilonSmallEffectiveDuration_ChoosesDestinationDirectly()
    {
        var decay = ScalarDecay.Create(1f, -1e6f, 1f);
        Assert.Equal(0f, decay.Duration);
        Assert.Equal(0f, decay.Evaluate(0f).Value);
        var offset = VectorOffset.Create(new Float3(1, 0, 0), new Float3(2e6f, 0, 0), Float3.Zero, 0.1f, 0.5f);
        Assert.Equal(0f, offset.Decay.Duration);
        Assert.Equal(Float3.Zero, offset.Apply(Float3.Zero, 0f));
    }

    [Fact]
    public void DirectTargetFallback_SnapsWithoutContinuityPromise()
    {
        var start = new Float3(1, 0, 0);
        var offset = VectorOffset.Create(start, new Float3(2e6f, 0, 0), Float3.Zero, 0.1f, 0.5f);
        Assert.Equal(0f, offset.Decay.Duration);
        Near(Float3.Zero, offset.Apply(Float3.Zero, 0f));
        Assert.True((start - offset.Apply(Float3.Zero, 0f)).Length > 0.5f);
    }

    [Fact]
    public void NearZeroOffset_ClearsCorrection()
    {
        var position = VectorOffset.Create(new Float3(1e-7f, 0, 0), new Float3(1, 0, 0), Float3.Zero, 0.1f, 0.5f);
        Assert.Equal(0f, position.Decay.Duration);
        var rotation = RotationOffset.Create(
            Quaternion.AxisAngle(new Float3(0, 0, 1), 1e-7f),
            Quaternion.AxisAngle(new Float3(0, 0, 1), 1f),
            Quaternion.Identity, 0.1f, 0.5f);
        Assert.Equal(0f, rotation.Decay.Duration);
    }

    [Fact]
    public void NonUnitCurrentQuaternion_IsNormalizedNotRejected()
    {
        var current = Quaternion.AxisAngle(new Float3(0, 0, 1), 0.5f);
        var scaled = new Quaternion(current.X * 3f, current.Y * 3f, current.Z * 3f, current.W * 3f);
        var offset = RotationOffset.Create(scaled, Quaternion.Identity, Quaternion.Identity, 0.1f, 0.5f);
        Assert.True(offset.Decay.Duration > 0f);
        Near(Quaternion.Normalize(scaled), offset.Apply(Quaternion.Identity, 0f));
    }

    [Fact]
    public void WrappedHistoricalTwist_IsUnwrappedNearCurrentAngle()
    {
        var axis = new Float3(0, 0, 1);
        var current = Quaternion.AxisAngle(axis, 0.5f);
        var previous = Quaternion.AxisAngle(axis, 0.7f);
        var plain = RotationOffset.Create(current, previous, Quaternion.Identity, 0.1f, 0.5f);
        var flipped = RotationOffset.Create(current, -previous, Quaternion.Identity, 0.1f, 0.5f);
        Near(-2f, plain.Decay.Evaluate(0f).Velocity, 1e-3f);
        Near(-2f, flipped.Decay.Evaluate(0f).Velocity, 1e-3f);
    }

    [Fact]
    public void HornerEvaluation_MatchesDirectPolynomialForm()
    {
        var decay = ScalarDecay.Create(2.5f, -1.5f, 0.7f);
        Assert.True(decay.Duration > 0f);
        for (var i = 0; i <= 10; i++)
        {
            var u = i / 10.0;
            var expected = decay.C0 + decay.C1 * u + decay.C2 * u * u + decay.C3 * u * u * u + decay.C4 * u * u * u * u + decay.C5 * u * u * u * u * u;
            Near((float)expected, decay.Evaluate((float)(u * decay.Duration)).Value, 1e-4f);
        }
    }

    [Fact]
    public void SmallDurations_RemainFiniteAndTerminateAtDestination()
    {
        foreach (var seconds in new float[] { 1e-4f, 5e-5f, 2e-5f })
        {
            var decay = ScalarDecay.Create(1f, 0f, seconds);
            Assert.True(decay.Duration > 0f);
            Near(1f, decay.Evaluate(0f).Value, 1e-6f);
            var mid = decay.Evaluate(decay.Duration * 0.5f);
            Assert.True(float.IsFinite(mid.Value));
            Assert.True(float.IsFinite(mid.Velocity));
            Assert.True(float.IsFinite(mid.Acceleration));
            Assert.Equal(0f, decay.Evaluate(decay.Duration).Value);
        }
    }

    [Fact]
    public void Reset_SeedsHistoryFromGivenOutput()
    {
        var history = new PoseHistory();
        var pose = Pose.Identity;
        pose.Position = new Float3(9, 9, 9);
        history.Reset(pose);
        history.Capture(Pose.Identity, 0.02f);
        var teleport = Pose.Identity;
        teleport.Position = new Float3(-4, 1, 2);
        history.Reset(teleport);
        Assert.Equal(1u, history.Count);
        Near(0f, history.Interval, 0f);
        Near(teleport.Position, history.Current.Position);
        Near(teleport.Position, history.Previous.Position);
        var transition = PoseTransition.Create(history, Pose.Identity, 0.5f);
        Near(0f, transition.Position.Decay.Evaluate(0f).Velocity, 0f);
    }

    [Fact]
    public void UniformTranslationOfHistoryAndDestination_LeavesCorrectionUnchanged()
    {
        var current = new Float3(1, 2, 3);
        var previous = new Float3(1.5f, 2, 3);
        var destination = new Float3(0.5f, 2, 3);
        var frame = new Float3(100, -50, 25);
        var a = VectorOffset.Create(current, previous, destination, 0.1f, 0.5f);
        var b = VectorOffset.Create(current + frame, previous + frame, destination + frame, 0.1f, 0.5f);
        Assert.True(a.Decay.Duration > 0f);
        Near(a.Direction, b.Direction);
        Near(a.Decay.C0, b.Decay.C0);
        Near(a.Decay.Evaluate(0f).Velocity, b.Decay.Evaluate(0f).Velocity);
    }

    [Fact]
    public void ValidOffsets_OutputAtZeroEqualsCapturedCorrectedOutput()
    {
        var previous = Pose.Identity;
        previous.Position = new Float3(2.1f, 0, 0);
        previous.Rotation = Quaternion.AxisAngle(new Float3(0, 0, 1), 0.6f);
        var current = Pose.Identity;
        current.Position = new Float3(2, 0, 0);
        current.Rotation = Quaternion.AxisAngle(new Float3(0, 0, 1), 0.5f);
        var history = new PoseHistory();
        history.Reset(previous);
        history.Capture(current, 0.1f);
        var transition = PoseTransition.Create(history, Pose.Identity, 0.5f);
        var output = PoseTransition.Apply(ref transition, Pose.Identity, 0f);
        Near(current.Position, output.Position);
        Near(current.Rotation, output.Rotation);
        var twisted = RotationOffset.Create(current.Rotation, new Quaternion(1, 0, 0, 0), Quaternion.Identity, 0.1f, 0.5f);
        Assert.True(twisted.Decay.Duration > 0f);
        Near(current.Rotation, twisted.Apply(Quaternion.Identity, 0f));
    }

    [Fact]
    public void StationaryParallelApproach_PreservesProjectedStartingDerivative()
    {
        var offset = VectorOffset.Create(new Float3(1, 0, 0), new Float3(1.2f, 0, 0), Float3.Zero, 0.1f, 0.5f);
        Near(-2f, offset.Decay.Evaluate(0f).Velocity, 1e-3f);
        Near(new Float3(1, 0, 0), offset.Apply(Float3.Zero, 0f));
    }
}
