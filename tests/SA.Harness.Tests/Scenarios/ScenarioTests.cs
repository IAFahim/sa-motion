using System;
using SA.Humanoid;
using SA.Motion.Numerics;
using SA.ReactionControl;
using Xunit;

namespace SA.Harness.Tests;

public class ScenarioTests
{
    const float PushSeconds = 0.5f;
    const float WindowSeconds = 5f;
    const float StartPelvisHeight = 1f;

    static PushSpec TorsoPush(float impulse)
    {
        return new PushSpec(Role.Torso, new Float3(1f, 0f, 0f), impulse, PushSeconds);
    }

    static string Describe(string name, ScenarioResult result)
    {
        return string.Concat(
            name,
            " Fell=", result.Fell.ToString(),
            " disp=", result.PeakPelvisDisplacement.ToString(),
            " lean=", result.PeakTorsoLean.ToString(),
            " rec=", result.RecoverySeconds.ToString(),
            " step=", result.MaxFootStep.ToString(),
            " steps=", result.Steps.ToString(),
            " finalY=", result.FinalPelvisHeight.ToString());
    }

    static bool AllFinite(ScenarioResult result)
    {
        return Scalar.IsFinite(result.PeakPelvisDisplacement)
            && Scalar.IsFinite(result.PeakTorsoLean)
            && Scalar.IsFinite(result.RecoverySeconds)
            && Scalar.IsFinite(result.MaxFootStep)
            && Scalar.IsFinite(result.FinalPelvisHeight);
    }

    static void SameResult(ScenarioResult expected, ScenarioResult actual)
    {
        Assert.Equal(expected.Fell, actual.Fell);
        Assert.Equal(expected.PeakPelvisDisplacement, actual.PeakPelvisDisplacement);
        Assert.Equal(expected.PeakTorsoLean, actual.PeakTorsoLean);
        Assert.Equal(expected.RecoverySeconds, actual.RecoverySeconds);
        Assert.Equal(expected.MaxFootStep, actual.MaxFootStep);
        Assert.Equal(expected.Steps, actual.Steps);
        Assert.Equal(expected.FinalPelvisHeight, actual.FinalPelvisHeight);
    }

    [Fact]
    public void PassiveFallsForEveryPushAndForNoPush()
    {
        var impulses = new float[] { 0f, 25f, 100f };
        foreach (float impulse in impulses)
        {
            PushSpec push = TorsoPush(impulse);
            ScenarioResult result = Scenarios.Run(Controllers.Passive, in push, WindowSeconds);
            Assert.True(result.Fell, Describe(impulse.ToString(), result));
            Assert.True(AllFinite(result), Describe(impulse.ToString(), result));
        }
        PushSpec none = TorsoPush(0f);
        ScenarioResult rest = Scenarios.Run(Controllers.Passive, in none, WindowSeconds);
        Assert.True(rest.FinalPelvisHeight < Scenarios.FellPelvisHeight, Describe("nopush", rest));
        Assert.True(rest.PeakPelvisDisplacement > 0.5f, Describe("nopush", rest));
    }

    [Fact]
    public void HoldRecoversSmallPushes()
    {
        var impulses = new float[] { 10f, 20f, 50f };
        foreach (float impulse in impulses)
        {
            PushSpec push = TorsoPush(impulse);
            ScenarioResult result = Scenarios.Run(Controllers.HoldPose(), in push, WindowSeconds);
            Assert.False(result.Fell, Describe(impulse.ToString(), result));
            Assert.True(AllFinite(result), Describe(impulse.ToString(), result));
            Assert.True(
                Scalar.Abs(result.FinalPelvisHeight - StartPelvisHeight) <= 0.1f,
                Describe(impulse.ToString(), result));
            Assert.True(
                result.RecoverySeconds >= 0f && result.RecoverySeconds <= WindowSeconds,
                Describe(impulse.ToString(), result));
        }
    }

    [Fact]
    public void HoldBoundaryLiesBeyondSingleImpulseRange()
    {
        var impulses = new float[] { 200f, 500f, 1000f };
        ScenarioResult previous = default;
        foreach (float impulse in impulses)
        {
            PushSpec push = TorsoPush(impulse);
            ScenarioResult result = Scenarios.Run(Controllers.HoldPose(), in push, WindowSeconds);
            Assert.False(result.Fell, Describe(impulse.ToString(), result));
            Assert.True(AllFinite(result), Describe(impulse.ToString(), result));
            if (impulse > 200f)
            {
                Assert.True(
                    Scalar.Abs(result.PeakPelvisDisplacement - previous.PeakPelvisDisplacement) < 0.05f,
                    Describe(impulse.ToString(), result));
            }
            previous = result;
        }
    }

    [Fact]
    public void RunsAreDeterministic()
    {
        PushSpec push = TorsoPush(50f);
        ScenarioResult firstHold = Scenarios.Run(Controllers.HoldPose(), in push, WindowSeconds);
        ScenarioResult secondHold = Scenarios.Run(Controllers.HoldPose(), in push, WindowSeconds);
        SameResult(firstHold, secondHold);
        PushSpec shove = TorsoPush(25f);
        ScenarioResult firstPassive = Scenarios.Run(Controllers.Passive, in shove, WindowSeconds);
        ScenarioResult secondPassive = Scenarios.Run(Controllers.Passive, in shove, WindowSeconds);
        SameResult(firstPassive, secondPassive);
    }

    [Fact]
    public void SweepReturnsImpulseMajorStableOrder()
    {
        var impulses = new float[] { 20f, 40f };
        const int directions = 4;
        const float sweepSeconds = 3f;
        ScenarioResult[] sweep = Scenarios.Sweep(Controllers.Passive, impulses, directions, sweepSeconds);
        Assert.Equal(impulses.Length * directions, sweep.Length);
        for (int i = 0; i < impulses.Length; i++)
        {
            for (int k = 0; k < directions; k++)
            {
                var spec = new PushSpec(Role.Torso, Scenarios.SweepDirection(k, directions), impulses[i], Scenarios.SweepPushSeconds);
                ScenarioResult single = Scenarios.Run(Controllers.Passive, in spec, sweepSeconds);
                SameResult(single, sweep[i * directions + k]);
            }
        }
        ScenarioResult[] again = Scenarios.Sweep(Controllers.Passive, impulses, directions, sweepSeconds);
        Assert.Equal(sweep.Length, again.Length);
        for (int i = 0; i < sweep.Length; i++)
        {
            SameResult(sweep[i], again[i]);
        }
    }

    [Fact]
    public void HostileInputReturnsFellWithFiniteFields()
    {
        PushSpec push = TorsoPush(10f);
        ScenarioResult nullController = Scenarios.Run(null, in push, WindowSeconds);
        Assert.True(nullController.Fell, Describe("null", nullController));
        Assert.True(AllFinite(nullController), Describe("null", nullController));

        ControllerStep bomb = (HarnessWorld world, float dt, Span<JointDrive> drives) =>
            throw new InvalidOperationException("boom");
        ScenarioResult throwing = Scenarios.Run(bomb, in push, WindowSeconds);
        Assert.True(throwing.Fell, Describe("throw", throwing));
        Assert.True(AllFinite(throwing), Describe("throw", throwing));

        var badRole = new PushSpec((Role)77, new Float3(1f, 0f, 0f), 10f, PushSeconds);
        ScenarioResult role = Scenarios.Run(Controllers.HoldPose(), in badRole, WindowSeconds);
        Assert.True(role.Fell, Describe("role", role));
        Assert.True(AllFinite(role), Describe("role", role));

        var badImpulse = new PushSpec(Role.Torso, new Float3(1f, 0f, 0f), float.NaN, PushSeconds);
        ScenarioResult impulse = Scenarios.Run(Controllers.HoldPose(), in badImpulse, WindowSeconds);
        Assert.True(impulse.Fell, Describe("impulse", impulse));
        Assert.True(AllFinite(impulse), Describe("impulse", impulse));

        var badDirection = new PushSpec(Role.Torso, new Float3(float.NaN, 0f, 0f), 10f, PushSeconds);
        ScenarioResult direction = Scenarios.Run(Controllers.HoldPose(), in badDirection, WindowSeconds);
        Assert.True(direction.Fell, Describe("direction", direction));
        Assert.True(AllFinite(direction), Describe("direction", direction));

        var badTime = new PushSpec(Role.Torso, new Float3(1f, 0f, 0f), 10f, float.NaN);
        ScenarioResult time = Scenarios.Run(Controllers.HoldPose(), in badTime, WindowSeconds);
        Assert.True(time.Fell, Describe("time", time));
        Assert.True(AllFinite(time), Describe("time", time));

        ScenarioResult nanSeconds = Scenarios.Run(Controllers.HoldPose(), in push, float.NaN);
        Assert.True(nanSeconds.Fell, Describe("nansec", nanSeconds));
        Assert.True(AllFinite(nanSeconds), Describe("nansec", nanSeconds));

        ScenarioResult negativeSeconds = Scenarios.Run(Controllers.HoldPose(), in push, -2f);
        Assert.True(negativeSeconds.Fell, Describe("negsec", negativeSeconds));
        Assert.True(AllFinite(negativeSeconds), Describe("negsec", negativeSeconds));

        Assert.Empty(Scenarios.Sweep(Controllers.Passive, new float[] { 10f }, 0, WindowSeconds));
        Assert.Empty(Scenarios.Sweep(Controllers.Passive, Array.Empty<float>(), 4, WindowSeconds));
        Assert.True(Scenarios.SweepDirection(3, 0).IsFinite, "zero-direction fallback must stay finite");
    }
}
