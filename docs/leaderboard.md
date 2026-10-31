# Balance leaderboard

Scored by `tests/SA.Balance.Evaluation` in the pure .NET harness: stand 10 s (20), pushes of 60, 100, 150, 200 and 300 N·s in eight directions (20, 15, 15, 10, 10), step bonus (10). 450 N·s is reported, not scored.

| Rank | Controller | Score | Stand | Deterministic | Recovered of 8 at 60 / 100 / 150 / 200 / 300 / 450 |
|---|---|---|---|---|---|
| 1 | baseline-hold | 51 | FAIL | True | 8 / 8 / 1 / 2 / 2 / 2 |
