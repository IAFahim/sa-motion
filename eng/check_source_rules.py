import re
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[1]
skipped = {"bin", "obj", "artifacts", "Library", "Temp", ".git"}
strings = re.compile(r'@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])\'')
forbidden = {
    "comment": lambda code: "//" in code or "/*" in code or "#region" in code,
    "reflection": lambda code: "System.Reflection" in code,
    "legacy input": lambda code: "Input.Get" in code,
}

offenders = []
for path in root.rglob("*.cs"):
    if skipped & set(path.relative_to(root).parts):
        continue
    code = strings.sub('""', path.read_text())
    offenders += [f"{path.relative_to(root)}: {name}" for name, found in forbidden.items() if found(code)]

print("\n".join(offenders) if offenders else "source rules: ok")
sys.exit(1 if offenders else 0)
