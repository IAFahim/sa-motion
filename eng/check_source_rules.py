import os
import re
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[1]
project = Path(os.environ.get("SA_PROJECT", "/home/i/ruk2"))
demo_folders = ["Assets/ReactionsDemo", "Assets/SA Demo"]
harness_name = re.compile(r"Probe|Harness|Telemetry")
hidden_read = re.compile(r"File\.(?:Exists|ReadAll\w+|OpenRead)\(([^)]*)\)")
temp_constant = re.compile(r'string\s+(\w+)\s*=\s*"[^"]*Temp/')
scene_harness = re.compile(r"m_EditorClassIdentifier:\s*\S*(?:Probe|Harness|Telemetry)\w*")
skipped = {"bin", "obj", "artifacts", "Library", "Temp", ".git"}
strings = re.compile(r'@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])\'')
forbidden = {
    "comment": lambda code: "//" in code or "/*" in code or "#region" in code,
    "reflection": lambda code: "System.Reflection" in code,
    "legacy input": lambda code: "Input.Get" in code,
}

def ships_to_people(relative):
    parts = relative.parts
    return parts[0] == "packages" and "Tests" not in parts and ("Runtime" in parts or any(part.startswith("Samples~") for part in parts))


def demo_sample(relative):
    parts = relative.parts
    for index, part in enumerate(parts[:-1]):
        if part.startswith("Samples~"):
            return index + 1 < len(parts) - 1 and bool(re.search(r"Demo|Showcase", parts[index + 1]))
    return False


offenders = []
for path in root.rglob("*.cs"):
    relative = path.relative_to(root)
    if skipped & set(relative.parts):
        continue
    raw = path.read_text()
    code = strings.sub('""', raw)
    offenders += [f"{relative}: {name}" for name, found in forbidden.items() if found(code)]
    if not ships_to_people(relative):
        continue
    names = set(temp_constant.findall(raw))
    for argument in hidden_read.findall(raw):
        if "Temp/" in argument or any(re.search(rf"\b{name}\b", argument) for name in names):
            offenders.append(f"{relative}: hidden switch, reads a file in Temp/. Use an inspector field or a menu item")
    if "GetEnvironmentVariable" in raw:
        offenders.append(f"{relative}: hidden switch, reads the environment. Use an inspector field or a menu item")
    if demo_sample(relative) and harness_name.search(path.stem):
        offenders.append(f"{relative}: test harness in a demo sample. Run it from an Editor menu item or a test")

for folder in demo_folders:
    for path in sorted((project / folder).rglob("*")):
        if path.suffix in (".unity", ".prefab"):
            for found in scene_harness.findall(path.read_text(errors="ignore")):
                offenders.append(f"{path.relative_to(project)}: test harness in a demo scene ({found.split()[-1]})")
        if path.suffix == ".cs" and harness_name.search(path.stem):
            offenders.append(f"{path.relative_to(project)}: test harness in a demo folder")

print("\n".join(offenders) if offenders else "source rules: ok")
sys.exit(1 if offenders else 0)
