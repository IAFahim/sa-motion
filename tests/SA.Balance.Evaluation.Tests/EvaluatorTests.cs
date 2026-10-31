using SA.Balance.Contract;
using SA.Balance.Evaluation;
using SA.Humanoid;
using SA.Motion.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace SA.Balance.Evaluation.Tests;

sealed class Limp : SA.Balance.Contract.IBalanceController
{
    public SA.Balance.Contract.ControllerReport Tick(ReadOnlySpan<SA.Balance.Contract.BodyState> bodies, ReadOnlySpan<SA.Balance.Contract.FootContact> feet, ReadOnlySpan<Quaternion> targetWorld, float dt, Span<SA.Balance.Contract.BodyDrive> drives)
    {
        for (var i = 0; i < drives.Length; i++)
            drives[i] = SA.Balance.Contract.BodyDrive.Passive;
        return new SA.Balance.Contract.ControllerReport { Decision = SA.Balance.Contract.ControllerDecision.Falling };
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

public class CapturePointScoreTests
{
    readonly ITestOutputHelper output;

    public CapturePointScoreTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void TheCapturePointControllerPublishesItsScorecard()
    {
        var card = Evaluator.Run("capture-point", () => new CapturePointController());
        output.WriteLine(Evaluator.Table(card));
        Evaluator.Write(card, Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/balance"));
        Assert.True(card.StandPassed, Evaluator.Table(card));
        Assert.True(card.Deterministic);
        Assert.True(card.Score > 51, Evaluator.Table(card));
    }
}
