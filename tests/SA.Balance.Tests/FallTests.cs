using SA.Humanoid;
using SA.Harness;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Balance.Tests
{
    public class FallTests
    {
        [Fact]
        public void ExtremePushesLatchFallingWithoutSteps()
        {
            var impulses = new float[] { 450f };
            for (int k = 0; k < 8; k++)
            {
                var runner = new BalanceRunner(BalanceParams.Standard);
                Float3 direction = Scenarios.SweepDirection(k, 8);
                var push = new PushSpec(Role.Torso, direction, impulses[0], 0.5f);
                ScenarioResult result = Scenarios.Run(runner.Step, push, 5f);
                Assert.True(result.Fell, string.Concat("450 d", k.ToString(), " stood"));
                Assert.Equal(Decision.Falling, runner.LastReport.Decision);
                Assert.False(runner.SawStepping, string.Concat("450 d", k.ToString(), " stepped"));
            }
        }

        [Fact]
        public void FallSweepIsDeterministic()
        {
            var first = new BalanceRunner(BalanceParams.Standard);
            var second = new BalanceRunner(BalanceParams.Standard);
            var push = new PushSpec(Role.Torso, new Float3(1f, 0f, 0f), 450f, 0.5f);
            ScenarioResult a = Scenarios.Run(first.Step, push, 5f);
            ScenarioResult b = Scenarios.Run(second.Step, push, 5f);
            Assert.Equal(a.Fell, b.Fell);
            Assert.Equal(a.FinalPelvisHeight, b.FinalPelvisHeight);
            Assert.Equal(first.LastReport.Decision, second.LastReport.Decision);
        }
    }
}
