using SA.Humanoid;
using SA.Harness;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Balance.Tests
{
    public class HoldTests
    {
        static PushSpec NoPush()
        {
            return new PushSpec(Role.Torso, new Float3(1f, 0f, 0f), 0f, 0.5f);
        }

        static string Describe(ScenarioResult result)
        {
            return string.Concat(
                result.Fell.ToString(),
                " disp ",
                result.PeakPelvisDisplacement.ToString(),
                " lean ",
                result.PeakTorsoLean.ToString());
        }

        [Fact]
        public void HoldTenSecondsWithinTolerance()
        {
            var runner = new BalanceRunner(BalanceParams.Standard);
            ScenarioResult result = Scenarios.Run(runner.Step, NoPush(), 10f);
            Assert.False(result.Fell);
            Assert.True(result.PeakPelvisDisplacement <= 0.08f, Describe(result));
            Assert.True(result.PeakTorsoLean <= 10f, Describe(result));
        }

        [Fact]
        public void HoldIsDeterministic()
        {
            var first = new BalanceRunner(BalanceParams.Standard);
            var second = new BalanceRunner(BalanceParams.Standard);
            ScenarioResult a = Scenarios.Run(first.Step, NoPush(), 10f);
            ScenarioResult b = Scenarios.Run(second.Step, NoPush(), 10f);
            Assert.Equal(a.Fell, b.Fell);
            Assert.Equal(a.PeakPelvisDisplacement, b.PeakPelvisDisplacement);
            Assert.Equal(a.PeakTorsoLean, b.PeakTorsoLean);
            Assert.Equal(a.RecoverySeconds, b.RecoverySeconds);
            Assert.Equal(a.Steps, b.Steps);
            Assert.Equal(a.FinalPelvisHeight, b.FinalPelvisHeight);
        }

        [Fact]
        public void HostileInputGivesFinitePassiveOutput()
        {
            var state = new BalanceState();
            var bodies = new BodyState[15];
            for (int i = 0; i < bodies.Length; i++)
            {
                var broken = new BodyState();
                broken.Position = new Float3(float.NaN, float.PositiveInfinity, 0f);
                broken.Rotation = new Quaternion(float.NaN, 0f, 0f, 0f);
                broken.LinearVelocity = new Float3(float.PositiveInfinity, 0f, 0f);
                broken.AngularVelocity = new Float3(0f, float.NaN, 0f);
                broken.InverseMass = float.NaN;
                bodies[i] = broken;
            }
            var feet = new FootContact[2];
            var drives = new BodyDrive[15];
            var input = new BalanceInput(null, float.NaN);
            var brokenParams = new BalanceParams();
            brokenParams.Gravity = float.NaN;
            BalanceReport report = StandingController.Tick(in brokenParams, ref state, bodies, feet, in input, drives);
            Assert.True(report.Com.IsFinite);
            Assert.True(report.CapturePoint.IsFinite);
            Assert.True(Scalar.IsFinite(report.Margin));
            for (int i = 0; i < drives.Length; i++)
            {
                Assert.True(drives[i].TargetWorld.IsValidRotation);
                Assert.True(Scalar.IsFinite(drives[i].Stiffness));
                Assert.True(Scalar.IsFinite(drives[i].Damping));
                Assert.True(Scalar.IsFinite(drives[i].MaxTorque));
            }
        }

        [Fact]
        public void ShortSpansGivePassiveOutput()
        {
            var state = new BalanceState();
            var bodies = new BodyState[2];
            var feet = new FootContact[0];
            var drives = new BodyDrive[15];
            var targets = new Quaternion[15];
            for (int i = 0; i < targets.Length; i++)
            {
                targets[i] = Quaternion.Identity;
            }
            var input = new BalanceInput(targets, 0.01666667f);
            var p = BalanceParams.Standard;
            BalanceReport report = StandingController.Tick(in p, ref state, bodies, feet, in input, drives);
            Assert.True(report.Com.IsFinite);
            for (int i = 0; i < drives.Length; i++)
            {
                Assert.Equal(0f, drives[i].Stiffness);
            }
        }
    }
}
