# Rules shared by every B packet (the balance race)

Read `AGENTS.md`, `docs/harness.md`, `docs/harness-scenarios.md`, this file, then your packet. They are your whole task. You have **60 minutes of wall-clock time**. Push your branch at least every 15 minutes and after every improvement in score, so the integrator can see partial results.

## The race

Many independent controllers are being built at once. Each one is scored by the same evaluator in the pure .NET harness, and the best one ships. Your job is to get the highest score you honestly can.

- Implement `SA.Balance.IBalanceController` (`src/SA.Balance.Abstractions/Contract.cs`). It receives world-space body states, two foot contacts, the target pose to hold (world orientations) and `dt`, and writes one `BodyDrive` per role (world-orientation target, stiffness, damping, torque cap). It returns a `ControllerReport`. Any state lives inside the controller object; `Reset()` clears it.
- Your code goes in **your own** new project `src/SA.Balance.<Name>/` (netstandard2.1, C# 9, `Nullable` enabled, MIT, references `SA.Balance.Abstractions`, `SA.Motion` and `SA.Humanoid` only) and your tests in `tests/SA.Balance.<Name>.Tests/` (net10.0, xunit, may reference your project and `SA.Balance.Evaluation`). Copy the csproj style of `tests/SA.Balance.Evaluation.Tests`. Restore once without a lock file and commit the generated `packages.lock.json`.
- Your test must run `Evaluator.Run("<name>", () => new YourController())`, write the scorecard with `Evaluator.Write(card, <repo>/artifacts/balance)`, and also copy `<name>.json` and `<name>.txt` into `docs/leaderboard/` (tracked). Use `Evaluator.RunFast` inside any optimizer loop; it takes about a second.
- Score: stand 10 s (20 points), pushes of 60, 100, 150, 200 and 300 N·s in eight directions (20, 15, 15, 10, 10), step bonus (10). Baseline `baseline-hold` scores 51: it recovers every 60 and 100 N·s push but fails the 10 s stand (it drifts and tips after about five seconds). Beat it.

## Hard rules

- Independent MIT code. **Do not read Delirium or `src/SA.ReactionControl`.** Derive from published control theory and ordinary mechanics.
- Never edit `src/SA.Balance.Abstractions`, `tests/SA.Balance.Evaluation*`, `tests/SA.Harness*`, or another agent's project. If you find a bug in them, put it in your handoff.
- No comments in code. Total functions: NaN, infinity, short spans give passive drives and a finite report. Deterministic: two runs bit-identical. No allocation per tick.
- Do not game the evaluator: no reading the push direction, no detecting the harness, no hard-coded schedules keyed on time. The controller sees only what the interface gives it.
- Run `eng/dotnet_test.sh tests/SA.Balance.<Name>.Tests/SA.Balance.<Name>.Tests.csproj` and `python3 eng/check_source_rules.py` before each commit. Push `packet/B<n>`, never main.

## Finish

Write `docs/handoff/B<n>.md`: final scorecard table, a log of what you tried (one line per experiment: change, fast score before and after), what limited the score, and what you did not verify. Stop early only if you hit 100.
