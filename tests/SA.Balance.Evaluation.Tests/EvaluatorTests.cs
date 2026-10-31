using SA.Balance;
using SA.Balance.Evaluation;
using SA.Humanoid;
using SA.Motion.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace SA.Balance.Evaluation.Tests;

sealed class Limp : IBalanceController
{
    public ControllerReport Tick(ReadOnlySpan<BodyState> bodies, ReadOnlySpan<FootContact> feet, ReadOnlySpan<Quaternion> targetWorld, float dt, Span<BodyDrive> drives)
    {
        for (var i = 0; i < drives.Length; i++)
            drives[i] = BodyDrive.Passive;
        return new ControllerReport { Decision = ControllerDecision.Falling };
    }

    public void Reset()
    {
    }
}

public class EvaluatorTests
{
    readonly ITestOutputHelper output;

    public EvaluatorTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void ALimpControllerScoresZeroAndFallsEverywhere()
    {
        var card = Evaluator.Run("limp", () => new Limp());
        output.WriteLine(Evaluator.Table(card));
        Assert.False(card.StandPassed);
        Assert.Equal(0, card.Score);
        Assert.All(card.Rows, row => Assert.Equal(0, row.Recovered));
        Assert.True(card.Deterministic);
    }

    [Fact]
    public void TheHoldBaselineStandsAndPublishesItsScorecard()
    {
        var card = Evaluator.Run("baseline-hold", () => new BaselineHold());
        output.WriteLine(Evaluator.Table(card));
        Evaluator.Write(card, Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/balance"));
        Assert.Equal(8, card.Rows[0].Recovered);
        Assert.True(card.Deterministic);
        Assert.Equal(Evaluator.Impulses.Length, card.Rows.Count);
        Assert.InRange(card.Score, 20, 100);
    }
}

public class FastScoreTests
{
    [Fact]
    public void TheFastScoreOrdersABetterControllerAboveAWorseOne()
    {
        var hold = Evaluator.RunFast(() => new BaselineHold());
        var limp = Evaluator.RunFast(() => new Limp());
        Assert.True(hold > limp, $"hold {hold} limp {limp}");
    }
}
