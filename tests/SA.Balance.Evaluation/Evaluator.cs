using SA.Balance.Contract;
using System.Text.Json;
using SA.Harness;
using SA.Humanoid;
using SA.Motion.Numerics;

namespace SA.Balance.Evaluation;

public static class Evaluator
{
    public const int Directions = 8;
    public const float SweepSeconds = 6f;
    public const float StandSeconds = 10f;
    public static readonly float[] Impulses = { 60f, 100f, 150f, 200f, 300f, 450f };
    static readonly int[] Points = { 20, 15, 15, 10, 10, 0 };

    public static Scorecard Run(string name, Func<IBalanceController> factory)
    {
        var stand = Stand(factory, out var deterministic);
        var rows = new List<PushRow>();
        var score = stand.Fell || stand.PeakPelvisDisplacement > 0.12f || stand.PeakTorsoLean > 15f ? 0 : 20;
        var stepBonus = false;
        for (var k = 0; k < Impulses.Length; k++)
        {
            var results = new ScenarioResultSet(Impulses[k]);
            for (var d = 0; d < Directions; d++)
            {
                var controller = factory();
                var latched = 0;
                var step = HarnessBridge.Step(controller, report => latched = report.Decision == ControllerDecision.Falling ? latched + 1 : 0);
                var push = new PushSpec(Role.Torso, Scenarios.SweepDirection(d, Directions), Impulses[k], 1f);
                var result = Scenarios.Run(step, push, SweepSeconds);
                results.Add(result, latched > 30);
            }
            var row = results.ToRow();
            rows.Add(row);
            score += (int)Math.Round(Points[k] * (row.Recovered / (float)Directions));
            if (Impulses[k] >= 150f && row.WithSteps > 0)
                stepBonus = true;
        }
        if (stepBonus)
            score += 10;
        return new Scorecard(name, !(stand.Fell || stand.PeakPelvisDisplacement > 0.12f || stand.PeakTorsoLean > 15f), stand.PeakPelvisDisplacement, stand.PeakTorsoLean, deterministic, rows, Math.Min(100, score), "1");
    }

    static ScenarioResult Stand(Func<IBalanceController> factory, out bool deterministic)
    {
        var push = new PushSpec(Role.Torso, new Float3(1f, 0f, 0f), 0f, 1f);
        var first = Scenarios.Run(HarnessBridge.Step(factory()), push, StandSeconds);
        var second = Scenarios.Run(HarnessBridge.Step(factory()), push, StandSeconds);
        deterministic = BitConverter.SingleToInt32Bits(first.FinalPelvisHeight) == BitConverter.SingleToInt32Bits(second.FinalPelvisHeight)
            && BitConverter.SingleToInt32Bits(first.PeakPelvisDisplacement) == BitConverter.SingleToInt32Bits(second.PeakPelvisDisplacement);
        return first;
    }

    public static float RunFast(Func<IBalanceController> factory)
    {
        var push = new PushSpec(Role.Torso, new Float3(1f, 0f, 0f), 0f, 1f);
        var stand = Scenarios.Run(HarnessBridge.Step(factory()), push, 6f);
        if (stand.Fell)
            return stand.FinalPelvisHeight;
        var total = 20f - Math.Min(20f, stand.PeakPelvisDisplacement * 100f);
        var impulses = new[] { 100f, 150f, 200f };
        foreach (var impulse in impulses)
            for (var d = 0; d < 4; d++)
            {
                var spec = new PushSpec(Role.Torso, Scenarios.SweepDirection(d * 2, Directions), impulse, 1f);
                var result = Scenarios.Run(HarnessBridge.Step(factory()), spec, 5f);
                total += result.Fell ? result.FinalPelvisHeight : 8f - Math.Min(4f, result.PeakPelvisDisplacement * 2f);
            }
        return total;
    }

    public static string Table(Scorecard card)
    {
        var lines = new List<string>
        {
            $"{card.Name}: score {card.Score}/100, stand {(card.StandPassed ? "pass" : "FAIL")} (drift {card.StandPeakDisplacement:F3} m, lean {card.StandPeakLean:F1} deg), deterministic {card.Deterministic}",
            "impulse  recovered/8  fell  with steps  recovery s  peak m  latched falling"
        };
        foreach (var row in card.Rows)
            lines.Add($"{row.Impulse,7:F0}  {row.Recovered,11}  {row.Fell,4}  {row.WithSteps,10}  {row.MeanRecoverySeconds,10:F2}  {row.MeanPeakDisplacement,6:F2}  {row.LatchedFalling,15}");
        return string.Join("\n", lines);
    }

    public static void Write(Scorecard card, string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, card.Name + ".json"), JsonSerializer.Serialize(card, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(directory, card.Name + ".txt"), Table(card) + "\n");
    }

    sealed class ScenarioResultSet
    {
        readonly float impulse;
        int recovered;
        int fell;
        int withSteps;
        int latched;
        float recovery;
        float peak;
        int count;

        public ScenarioResultSet(float impulse) => this.impulse = impulse;

        public void Add(ScenarioResult result, bool latchedFalling)
        {
            count++;
            if (result.Fell)
                fell++;
            else
            {
                recovered++;
                recovery += result.RecoverySeconds;
                if (result.Steps > 0)
                    withSteps++;
            }
            if (latchedFalling)
                latched++;
            peak += result.PeakPelvisDisplacement;
        }

        public PushRow ToRow() => new PushRow(impulse, recovered, fell, withSteps, recovered > 0 ? recovery / recovered : 0f, count > 0 ? peak / count : 0f, latched);
    }
}
