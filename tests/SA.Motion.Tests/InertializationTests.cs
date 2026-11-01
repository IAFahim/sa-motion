using System.Runtime.InteropServices;
using SA.Motion.Inertialization;
using SA.Motion.Numerics;
using Xunit;
using Quaternion = SA.Motion.Numerics.Quaternion;

namespace SA.Motion.Tests;

public class InertializationTests
{
    static void Near(float expected, float actual, float tolerance = 2e-4f) => Assert.InRange(actual, expected - tolerance, expected + tolerance);
    static void Near(Float3 expected, Float3 actual, float tolerance = 2e-4f)
    {
        Near(expected.X, actual.X, tolerance);
        Near(expected.Y, actual.Y, tolerance);
        Near(expected.Z, actual.Z, tolerance);
    }
    static void Near(Quaternion expected, Quaternion actual) => Near(1f, Math.Abs(Quaternion.Dot(Quaternion.Normalize(expected), Quaternion.Normalize(actual))), 2e-5f);

    [Theory]
    [InlineData(0.01f)]
    [InlineData(0.1f)]
    [InlineData(0.5f)]
    [InlineData(2f)]
    public void ZeroVelocityMatchesMinimumJerkClosedForm(float duration)
    {
        var decay = ScalarDecay.Create(3f, 0f, duration);
        for (var i = 0; i <= 100; i++)
        {
            var u = i / 100d;
            var expected = 3d * (1d - 10d * u * u * u + 15d * Math.Pow(u, 4) - 6d * Math.Pow(u, 5));
            Near((float)expected, decay.Evaluate((float)(u * duration)).Value, 1e-4f);
        }
    }

    [Theory]
    [InlineData(-0.2f, 1f)]
    [InlineData(-4f, 1f)]
    [InlineData(-20f, 0.5f)]
    [InlineData(5f, 0.5f)]
    public void InitialAndTerminalDerivativesFollowTheDeclaredRules(float velocity, float duration)
    {
        var decay = ScalarDecay.Create(1.2f, velocity, duration);
        var start = decay.Evaluate(0f);
        Near(1.2f, start.Value);
        Near(Math.Min(0f, velocity), start.Velocity);
        var end = decay.Evaluate(decay.Duration);
        Near(0f, end.Value, 0f);
        Near(0f, end.Velocity, 0f);
        Near(0f, end.Acceleration, 0f);
        var before = decay.Evaluate(decay.Duration * 0.999f);
        Near(0f, before.Value, 1e-4f);
        Near(0f, before.Velocity, 0.005f);
        Near(0f, before.Acceleration, 0.4f);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(-0.2f, 1f)]
    [InlineData(-4f, 1f)]
    [InlineData(-20f, 0.5f)]
    [InlineData(5f, 0.5f)]
    public void RawNormalizedCoefficientsSatisfyTerminalConditions(float velocity, float duration)
    {
        var p = ScalarDecay.Create(1.2f, velocity, duration);
        var value = (double)p.C0 + p.C1 + p.C2 + p.C3 + p.C4 + p.C5;
        var derivative = (double)p.C1 + 2d * p.C2 + 3d * p.C3 + 4d * p.C4 + 5d * p.C5;
        var acceleration = 2d * p.C2 + 6d * p.C3 + 12d * p.C4 + 20d * p.C5;
        Assert.InRange(Math.Abs(value), 0, 2e-5);
        Assert.InRange(Math.Abs(derivative), 0, 2e-4);
        Assert.InRange(Math.Abs(acceleration), 0, 1e-3);
    }

    [Fact]
    public void ShortenedDurationIsCheckedBeforeAnyDivision()
    {
        var decay = ScalarDecay.Create(1f, -1e20f, 1f);
        Assert.Equal(0f, decay.Duration);
        Assert.Equal(0f, decay.Evaluate(0f).Value);
        Near(0.3f, ScalarDecay.Create(1.2f, -20f, 0.5f).Duration);
    }

    [Fact]
    public void CanonicalCorrectionsStayMonotoneAndDoNotOvershoot()
    {
        var random = new Random(6819);
        for (var caseIndex = 0; caseIndex < 200; caseIndex++)
        {
            var magnitude = (float)(0.01 + 10 * random.NextDouble());
            var velocity = -(float)(20 * random.NextDouble());
            var decay = ScalarDecay.Create(magnitude, velocity, 0.1f + (float)random.NextDouble());
            var previous = magnitude;
            for (var i = 0; i <= 100; i++)
            {
                var sample = decay.Evaluate(decay.Duration * i / 100f);
                Assert.InRange(sample.Value, -2e-4f, previous + 2e-4f);
                Assert.True(float.IsFinite(sample.Velocity));
                Assert.True(float.IsFinite(sample.Acceleration));
                previous = sample.Value;
            }
        }
    }

    [Theory]
    [InlineData(float.NaN, 0f, 1f)]
    [InlineData(float.PositiveInfinity, 0f, 1f)]
    [InlineData(1f, 0f, float.NaN)]
    [InlineData(1f, 0f, -1f)]
    [InlineData(1f, 0f, 0f)]
    [InlineData(float.MaxValue, -1f, 1f)]
    public void InvalidOrUnrepresentableInputsChooseDirectDestination(float magnitude, float velocity, float seconds)
    {
        var decay = ScalarDecay.Create(magnitude, velocity, seconds);
        Assert.Equal(0f, decay.Duration);
        Assert.Equal(0f, decay.Evaluate(1f).Value);
    }

    [Fact]
    public void OriginalProjectionPreservesApproachingRadialButDiscardsPerpendicularVelocity()
    {
        var offset = VectorOffset.Create(new Float3(1, 0, 0), new Float3(1.1f, 1, 0), Float3.Zero, 0.1f, 0.5f);
        Near(-1f, offset.Decay.Evaluate(0f).Velocity);
        Near(new Float3(1, 0, 0), offset.Apply(Float3.Zero, 0f));
        Near(0f, offset.Apply(Float3.Zero, 0.01f).Y);
        var away = VectorOffset.Create(new Float3(1, 0, 0), new Float3(0.9f, 0, 0), Float3.Zero, 0.1f, 0.5f);
        Near(0f, away.Decay.Evaluate(0).Velocity);
    }

    [Fact]
    public void MovingDestinationIsAddedRatherThanVelocitySubtracted()
    {
        var offset = VectorOffset.Create(new Float3(1, 0, 0), new Float3(1, 0, 0), Float3.Zero, 0.1f, 1f);
        var stationary = offset.Apply(Float3.Zero, 0.1f);
        var moving = offset.Apply(new Float3(0.5f, 0, 0), 0.1f);
        Near(0.5f, moving.X - stationary.X);
    }

    [Fact]
    public void RotationUsesLeftCorrectionWithNonIdentityDestinationAndSignEquivalentHistory()
    {
        var target = Quaternion.AxisAngle(new Float3(1, 0, 0), 0.7f);
        var current = Quaternion.Multiply(Quaternion.AxisAngle(new Float3(0, 0, 1), 1.2f), target);
        var previous = Quaternion.Multiply(Quaternion.AxisAngle(new Float3(0, 0, 1), 1.3f), target);
        var offset = RotationOffset.Create(current, previous, target, 0.1f, 0.5f);
        var flipped = RotationOffset.Create(-current, -previous, -target, 0.1f, 0.5f);
        Near(current, offset.Apply(target, 0));
        Near(offset.Apply(target, 0.2f), flipped.Apply(-target, 0.2f));
        Near(target, offset.Apply(target, 1));
        Near(-1f, offset.Decay.Evaluate(0).Velocity);
    }

    [Fact]
    public void DegenerateHistoricalTwistKeepsValidCurrentRotationCorrection()
    {
        var current = Quaternion.AxisAngle(new Float3(0, 0, 1), 1f);
        var offset = RotationOffset.Create(current, new Quaternion(1, 0, 0, 0), Quaternion.Identity, 0.1f, 0.5f);
        Assert.True(offset.Decay.Duration > 0);
        Near(0f, offset.Decay.Evaluate(0).Velocity);
        Near(current, offset.Apply(Quaternion.Identity, 0));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.00000001f)]
    [InlineData(3.14158f)]
    [InlineData(3.1415927f)]
    public void QuaternionBranchesRemainFiniteAndSignEquivalent(float angle)
    {
        var current = Quaternion.AxisAngle(new Float3(0, 1, 0), angle);
        var a = RotationOffset.Create(current, current, Quaternion.Identity, 0.02f, 0.3f);
        var b = RotationOffset.Create(-current, -current, Quaternion.Identity, 0.02f, 0.3f);
        var qa = a.Apply(Quaternion.Identity, 0.1f);
        var qb = b.Apply(Quaternion.Identity, 0.1f);
        Near(1f, Quaternion.Dot(qa, qa));
        Near(qa, qb);
    }

    [Fact]
    public void InterruptedTransitionStartsFromTheCorrectedOutput()
    {
        var source = Pose.Identity;
        source.Position = new Float3(3, 2, 0);
        source.Rotation = Quaternion.AxisAngle(new Float3(0, 1, 0), 1f);
        var history = new PoseHistory();
        history.Reset(source);
        var firstTarget = Pose.Identity;
        var first = PoseTransition.Create(history, firstTarget, 0.8f);
        var previous = PoseTransition.Apply(ref first, firstTarget, 0.28f);
        var current = PoseTransition.Apply(ref first, firstTarget, 0.3f);
        history.Reset(previous);
        history.Capture(current, 0.02f);
        var next = Pose.Identity;
        next.Position = new Float3(-2, 0, 1);
        next.Rotation = Quaternion.AxisAngle(new Float3(1, 0, 0), 0.4f);
        var second = PoseTransition.Create(history, next, 0.5f);
        var output = PoseTransition.Apply(ref second, next, 0);
        Near(current.Position, output.Position);
        Near(current.Rotation, output.Rotation);
        Near(next.Position, PoseTransition.Apply(ref second, next, 1).Position);
    }

    [Fact]
    public void ZeroDeltaDoesNotReplaceHistoryAndHistoricalIntervalOwnsVelocity()
    {
        var history = new PoseHistory();
        var previous = Pose.Identity;
        previous.Position = new Float3(2.1f, 0, 0);
        var current = previous;
        current.Position = new Float3(2, 0, 0);
        history.Reset(previous);
        history.Capture(current, 0.1f);
        history.Capture(Pose.Identity, 0f);
        Near(current.Position, history.Current.Position);
        Near(0.1f, history.Interval);
        var transition = PoseTransition.Create(history, Pose.Identity, 1f);
        Near(-1f, transition.Position.Decay.Evaluate(0f).Velocity);
    }

    [Fact]
    public void InvalidSignedScaleResetsOnlyScaleCorrection()
    {
        var source = Pose.Identity;
        source.Position = new Float3(2, 0, 0);
        source.Scale = new Float3(-1, 2, 1);
        var history = new PoseHistory();
        history.Reset(source);
        var transition = PoseTransition.Create(history, Pose.Identity, 0.5f);
        var output = PoseTransition.Apply(ref transition, Pose.Identity, 0f);
        Near(source.Position, output.Position);
        Near(Float3.One, output.Scale);
    }

    [Fact]
    public void PositiveScaleUsesLogSpaceAndReturnsToDestination()
    {
        var source = Pose.Identity;
        source.Scale = new Float3(2, 0.5f, 3);
        var history = new PoseHistory();
        history.Reset(source);
        var transition = PoseTransition.Create(history, Pose.Identity, 0.5f);
        Near(source.Scale, PoseTransition.Apply(ref transition, Pose.Identity, 0).Scale);
        Assert.True(PoseTransition.Apply(ref transition, Pose.Identity, 0.25f).Scale.IsPositive);
        Near(Float3.One, PoseTransition.Apply(ref transition, Pose.Identity, 1).Scale);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(float.NaN)]
    public void InvalidRotationHistoryIsNotNormalizedIntoInventedVelocity(float invalid)
    {
        var destination = Quaternion.AxisAngle(new Float3(0, 0, 1), -2f);
        var current = Quaternion.AxisAngle(new Float3(0, 0, 1), -1.5f);
        var previous = new Quaternion(invalid, 0, 0, 0);
        var offset = RotationOffset.Create(current, previous, destination, 0.02f, 0.5f);
        Near(0f, offset.Decay.Evaluate(0).Velocity);
        Near(current, offset.Apply(destination, 0));
        var badCurrent = RotationOffset.Create(previous, current, destination, 0.02f, 0.5f);
        Assert.Equal(0f, badCurrent.Decay.Duration);
    }

    [Fact]
    public void SignedDestinationInvalidatesOnlyScaleStateAndDoesNotReviveIt()
    {
        var source = Pose.Identity;
        source.Position = new Float3(2, 0, 0);
        source.Scale = new Float3(2, 2, 2);
        var history = new PoseHistory();
        history.Reset(source);
        var transition = PoseTransition.Create(history, Pose.Identity, 1f);
        var signed = Pose.Identity;
        signed.Scale = new Float3(-1, 1, 1);
        Near(signed.Scale, PoseTransition.Apply(ref transition, signed, 0.1f).Scale);
        var resumed = PoseTransition.Apply(ref transition, Pose.Identity, 0.2f);
        Near(Float3.One, resumed.Scale);
        Assert.True(resumed.Position.X > 0);
        history.Capture(signed, 0.02f);
        history.Capture(source, 0.02f);
        Near(source.Scale, history.Previous.Scale);
        Near(signed.Position, history.Previous.Position);
    }

    [Fact]
    public void KernelLoopsAllocateNoManagedMemoryAfterWarmup()
    {
        var history = new PoseHistory();
        var pose = Pose.Identity;
        pose.Position = new Float3(1, 2, 3);
        pose.Scale = new Float3(2, 3, 4);
        history.Reset(pose);
        var transition = PoseTransition.Create(history, Pose.Identity, 1f);
        var sum = 0f;
        for (var i = 0; i < 1000; i++) sum += PoseTransition.Apply(ref transition, Pose.Identity, 0.2f).Position.X;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100000; i++) sum += PoseTransition.Apply(ref transition, Pose.Identity, i % 100 * 0.01f).Position.X;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.True(sum > 0f);
    }

    [Fact]
    public void PublicAbiIsUnmanagedAndEngineFree()
    {
        Assert.Equal(12, Marshal.SizeOf<Float3>());
        Assert.Equal(16, Marshal.SizeOf<Quaternion>());
        Assert.Equal(40, Marshal.SizeOf<Pose>());
        Assert.Equal(28, Marshal.SizeOf<ScalarDecay>());
        Assert.Equal(88, Marshal.SizeOf<PoseHistory>());
        Assert.Equal(120, Marshal.SizeOf<PoseTransition>());
        Assert.DoesNotContain(typeof(ScalarDecay).Assembly.GetReferencedAssemblies(), a => a.Name!.StartsWith("Unity", StringComparison.Ordinal));
        Assert.Equal(LibraryUnderTest.Framework, typeof(ScalarDecay).Assembly.GetCustomAttributes(typeof(System.Runtime.Versioning.TargetFrameworkAttribute), false).Cast<System.Runtime.Versioning.TargetFrameworkAttribute>().Single().FrameworkName);
    }
}
