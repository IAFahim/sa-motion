using SA.Humanoid;
using SA.Harness;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Balance.Tests
{
    public class PushTests
    {
        static string Describe(float impulse, int direction, ScenarioResult result)
        {
            return string.Concat(
                impulse.ToString("F0"),
                " d",
                direction.ToString(),
                " fell ",
                result.Fell.ToString(),
                " disp ",
                result.PeakPelvisDisplacement.ToString("F2"),
                " lean ",
                result.PeakTorsoLean.ToString("F1"),
                " rec ",
                result.RecoverySeconds.ToString("F2"));
        }

        [Fact]
        public void SixtyNewtonSecondsRecoverWithoutFalling()
        {
            var impulses = new float[] { 60f };
            ScenarioResult[] results = Sweep(impulses, 8, 5f);
            for (int k = 0; k < 8; k++)
            {
                ScenarioResult result = results[k];
                Assert.False(result.Fell, Describe(60f, k, result));
                Assert.True(result.RecoverySeconds < 3f, Describe(60f, k, result));
            }
        }

        [Fact]
        public void PushSweepIsDeterministic()
        {
            var impulses = new float[] { 60f };
            ScenarioResult[] first = Sweep(impulses, 8, 5f);
            ScenarioResult[] second = Sweep(impulses, 8, 5f);
            for (int k = 0; k < 8; k++)
            {
                Assert.Equal(first[k].Fell, second[k].Fell);
                Assert.Equal(first[k].PeakPelvisDisplacement, second[k].PeakPelvisDisplacement);
                Assert.Equal(first[k].PeakTorsoLean, second[k].PeakTorsoLean);
                Assert.Equal(first[k].RecoverySeconds, second[k].RecoverySeconds);
            }
        }

        static ScenarioResult[] Sweep(float[] impulses, int directions, float seconds)
        {
            var results = new ScenarioResult[impulses.Length * directions];
            for (int i = 0; i < impulses.Length; i++)
            {
                for (int k = 0; k < directions; k++)
                {
                    var runner = new BalanceRunner(BalanceParams.Standard);
                    Float3 direction = Scenarios.SweepDirection(k, directions);
                    var push = new PushSpec(Role.Torso, direction, impulses[i], 0.5f);
                    results[i * directions + k] = Scenarios.Run(runner.Step, push, seconds);
                }
            }
            return results;
        }
    }
}
