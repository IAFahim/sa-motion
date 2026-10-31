import json
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[1]
rows = []
for path in sorted((root / "docs/leaderboard").glob("*.json")):
    card = json.loads(path.read_text())
    detail = {int(r["Impulse"]): r for r in card["Rows"]}
    rows.append((card["Score"], card["Name"], card["StandPassed"], card["Deterministic"], {k: v["Recovered"] for k, v in detail.items()}))
rows.sort(key=lambda r: -r[0])
lines = ["# Balance leaderboard", "", "Scored by `tests/SA.Balance.Evaluation` in the pure .NET harness: stand 10 s (20), pushes of 60, 100, 150, 200 and 300 N·s in eight directions (20, 15, 15, 10, 10), step bonus (10). 450 N·s is reported, not scored.", "", "| Rank | Controller | Score | Stand | Deterministic | Recovered of 8 at 60 / 100 / 150 / 200 / 300 / 450 |", "|---|---|---|---|---|---|"]
for rank, (score, name, stand, det, rec) in enumerate(rows, 1):
    lines.append(f"| {rank} | {name} | {score} | {'pass' if stand else 'FAIL'} | {det} | {' / '.join(str(rec.get(i, '-')) for i in (60, 100, 150, 200, 300, 450))} |")
(root / "docs/leaderboard.md").write_text("\n".join(lines) + "\n")
print("\n".join(lines))
