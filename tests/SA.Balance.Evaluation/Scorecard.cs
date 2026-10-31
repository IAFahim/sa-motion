using SA.Balance.Contract;

namespace SA.Balance.Evaluation;

public sealed record PushRow(float Impulse, int Recovered, int Fell, int WithSteps, float MeanRecoverySeconds, float MeanPeakDisplacement, int LatchedFalling);

public sealed record Scorecard(string Name, bool StandPassed, float StandPeakDisplacement, float StandPeakLean, bool Deterministic, IReadOnlyList<PushRow> Rows, int Score, string Version);
