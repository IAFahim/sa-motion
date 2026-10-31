using SA.Balance.Evaluation;
using SA.Harness;
using SA.Humanoid;
using Xunit;
using Xunit.Abstractions;

namespace SA.Balance.Tuning;

public class TunedScoreTests
{
    readonly ITestOutputHelper output;

    public TunedScoreTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void TheTunedParamsPublishTheirScorecard()
    {
        var card = Evaluator.Run("c1-tuned", () => new TunedController());
        output.WriteLine(Evaluator.Table(card));
        Evaluator.Write(card, Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/balance"));
        Assert.True(card.StandPassed, Evaluator.Table(card));
        Assert.True(card.Deterministic);
        Assert.True(card.Score >= 80, Evaluator.Table(card));
    }

    [Fact]
    public void TheTunedParamsHoldUntrainedDirections()
    {
        var tuned = Holdout(TunedParams.Tuned);
        var standard = Holdout(BalanceParams.Standard);
        output.WriteLine($"tuned {tuned} of 32, standard {standard} of 32");
        Assert.True(tuned >= standard, $"tuned {tuned} standard {standard}");
    }

    public static int Holdout(BalanceParams p)
    {
        var recovered = 0;
        foreach (var impulse in new[] { 100f, 150f })
        {
            for (var k = 0; k < 16; k++)
            {
                var direction = Scenarios.SweepDirection(2 * k + 1, 32);
                var spec = new PushSpec(Role.Torso, direction, impulse, 1f);
                var result = Scenarios.Run(HarnessBridge.Step(new TunedController(p)), in spec, 5f);
                if (!result.Fell)
                    recovered++;
            }
        }
        return recovered;
    }
}

public class ParamSpaceTests
{
    [Fact]
    public void EncodeThenDecodeReturnsTheStandardParams()
    {
        var back = ParamSpace.Decode(ParamSpace.Encode(BalanceParams.Standard));
        for (var i = 0; i < ParamSpace.Count; i++)
            Assert.Equal(ParamSpace.Get(BalanceParams.Standard, i), ParamSpace.Get(in back, i), 5);
    }

    [Fact]
    public void TheSeededSequenceIsDeterministic()
    {
        var a = new Optimizer.XorShift(12345UL);
        var b = new Optimizer.XorShift(12345UL);
        for (var i = 0; i < 64; i++)
            Assert.Equal(a.NextDouble(), b.NextDouble());
    }
}

public class TuneRunTests
{
    readonly ITestOutputHelper output;
    public TuneRunTests(ITestOutputHelper output) => this.output = output;
    [Fact]
    public void TuneReportsTheEvolutionLog()
    {
        if (Environment.GetEnvironmentVariable("C1_TUNE") != "1")
            return;
        var started = DateTime.UtcNow;
        var baseline = Optimizer.Objective(BalanceParams.Standard);
        output.WriteLine($"baseline fast {baseline:F3}");
        var seed = ParamSpace.Encode(BalanceParams.Standard);
        for (var i = 0; i < ParamSpace.Count; i++)
        {
            foreach (var sign in new[] { 1.0, -1.0 })
            {
                var trial = (double[])seed.Clone();
                trial[i] += sign * 0.1;
                if (trial[i] < 0 || trial[i] > 1)
                    continue;
                var score = Optimizer.Objective(ParamSpace.Decode(trial));
                output.WriteLine($"sense {ParamSpace.Name(i)} {(sign > 0 ? "+" : "-")} {score:F3}");
            }
        }
        var evolved = Optimizer.Evolve(seed, 24, 8, 20261031UL, 0.12, 0.04, line => output.WriteLine(line), (g, genes, score) =>
        {
            if (g % 6 == 5)
            {
                var card = Evaluator.Run("c1-probe", () => new TunedController(ParamSpace.Decode(genes)));
                output.WriteLine($"probe gen {g} score {card.Score} r200 {card.Rows[3].Recovered} r300 {card.Rows[4].Recovered}");
            }
            return true;
        });
        var refined = Optimizer.Refine(evolved.Genes, evolved.Fast, 0.05, 2, line => output.WriteLine(line));
        var best = ParamSpace.Decode(refined.Genes);
        output.WriteLine($"evals {evolved.Evals + refined.Evals} wall {(DateTime.UtcNow - started).TotalSeconds:F0}s fast {refined.Fast:F3}");
        output.WriteLine(TuningReport.Format(best));
        var final = Evaluator.Run("c1-tuned", () => new TunedController(best));
        output.WriteLine(Evaluator.Table(final));
    }
}
