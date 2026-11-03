#!/usr/bin/env python3
"""Pilot corpus inventory for sa-motion residual gate (stdlib only).

Counts every real rig and clip on disk without downloads, copies or
renames: sa-motion tests/Fixtures (*.saclip/*.saret), the published
families/humanoid-v1.safamily descriptor, the com.sa.unity Samples~
showcase (UAL/Kyle clip references), and the sa-demo Synty FBX set at
/home/os/TestingAssets/Assets/Synty (counted only, never modified).

Merges tests/SA.PilotSweep.Tests output (artifacts/pilot-sweep-report.json)
when present and prints one machine-readable JSON object to stdout.
Exit 0 when the report is produced; exit 1 only on unreadable inputs.
Plan shortfalls (50 rigs / 500 clips / 25000 pairs) are reported as data,
never as failures: the sweep covers COMPATIBLE portable pairs only.
"""

import hashlib
import json
import os
import subprocess
import sys
from pathlib import Path

SA_MOTION = Path(__file__).resolve().parents[1]
COM_SA_UNITY = Path("/home/os/GitHub/com.sa.unity")
SYNTY = Path("/home/os/TestingAssets/Assets/Synty")
SA_DEMO = Path("/home/os/GitHub/sa-demo")
BUDGET = 64 * 1024 * 1024

FROZEN = {
    "three-bone-packed.saclip": (
        756, "a85a73b5f115cbdf478ef10b1f995d8f3016a8a3fc4ef44c5a5eee79abf59ef8"),
    "three-bone-raw.saclip": (
        772, "7ba9609c2f715bf1445ca634c8d1c751817cfdfbddfbed4660e790c1bf071a2a"),
    "three-bone-target.saret": (
        608, "dca879c5183cf898ab1bb18ba1090138f5658301de65144e69d752747027ccc0"),
}

EXPECTED_SYNTH_FBX = 721


def sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(65536), b""):
            digest.update(chunk)
    return digest.hexdigest()


def git_branch(repo):
    try:
        branch = subprocess.run(
            ["git", "-C", str(repo), "branch", "--show-current"],
            capture_output=True, text=True, timeout=30)
        tip = subprocess.run(
            ["git", "-C", str(repo), "log", "--oneline", "-1"],
            capture_output=True, text=True, timeout=30)
    except (OSError, subprocess.SubprocessError):
        return {"present": False}
    if branch.returncode != 0:
        return {"present": False}
    return {"present": True,
            "branch": branch.stdout.strip(),
            "tip": tip.stdout.strip() if tip.returncode == 0 else ""}


def main():
    errors = []
    fixtures_dir = SA_MOTION / "tests" / "SA.Animation.Tests" / "Fixtures"
    fixtures = []
    for name in sorted(FROZEN):
        path = fixtures_dir / name
        try:
            data = {"name": name, "bytes": path.stat().st_size,
                    "sha256": sha256(path)}
        except OSError as exc:
            errors.append("fixture %s unreadable: %s" % (name, exc))
            continue
        frozen_bytes, frozen_hash = FROZEN[name]
        data["frozen"] = data["bytes"] == frozen_bytes and data["sha256"] == frozen_hash
        fixtures.append(data)

    family_path = SA_MOTION / "families" / "humanoid-v1.safamily"
    try:
        family_doc = json.loads(family_path.read_text())
        family = {
            "bytes": family_path.stat().st_size,
            "sha256": sha256(family_path),
            "family": family_doc.get("family"),
            "familyVersion": family_doc.get("familyVersion"),
            "bones": len(family_doc.get("bones", [])),
            "requiredBones": len(family_doc.get("requiredBones", [])),
            "hipBone": family_doc.get("hipBone"),
        }
    except (OSError, ValueError) as exc:
        errors.append("safamily unreadable: %s" % exc)
        family = {}

    showcase = COM_SA_UNITY / "Samples~" / "Showcase"
    try:
        showcase_files = sorted(p.name for p in showcase.iterdir() if p.is_file())
    except OSError as exc:
        errors.append("showcase unreadable: %s" % exc)
        showcase_files = []
    showcase_clips = 0
    for suffix in (".anim", ".fbx", ".controller", ".overrideController"):
        showcase_clips += len(list(showcase.rglob("*" + suffix))) if showcase.exists() else 0

    referenced = ["Assets/UAL/UAL1_Standard.fbx", "Assets/UAL/UAL2_Standard.fbx",
                  "Robot Kyle.fbx", "Idle.anim", "Kyle_Walk.anim"]
    missing_samples = []
    for ref in referenced:
        found = False
        for base in (COM_SA_UNITY, Path("/home/os/TestingAssets")):
            matches = list(base.rglob(os.path.basename(ref))) if base.exists() else []
            matches = [m for m in matches if "Library/" not in m.as_posix()
                       and ".git/" not in m.as_posix()]
            if matches:
                found = True
                break
        if not found:
            missing_samples.append(ref)

    synty = {"present": SYNTY.exists(), "fbx": 0, "bytes": 0, "takes": 0,
             "feminine": 0, "masculine": 0, "neutral": 0}
    if synty["present"]:
        try:
            fbx_files = [p for p in SYNTY.rglob("*.fbx") if p.is_file()]
            synty["fbx"] = len(fbx_files)
            synty["bytes"] = sum(p.stat().st_size for p in fbx_files)
            takes = set()
            for path in fbx_files:
                stem = path.stem
                for tag in ("_Femn", "_Masc"):
                    stem = stem.replace(tag, "")
                takes.add(stem)
                lowered = path.as_posix().lower()
                if "/feminine/" in lowered:
                    synty["feminine"] += 1
                elif "/masculine/" in lowered:
                    synty["masculine"] += 1
                elif "/neutral/" in lowered:
                    synty["neutral"] += 1
            synty["takes"] = len(takes)
        except OSError as exc:
            errors.append("synty unreadable: %s" % exc)
    synty["matchesExpected"] = synty["fbx"] == EXPECTED_SYNTH_FBX

    sweep_report = None
    sweep_path = SA_MOTION / "artifacts" / "pilot-sweep-report.json"
    if sweep_path.exists():
        try:
            sweep_report = json.loads(sweep_path.read_text())
        except (OSError, ValueError) as exc:
            errors.append("sweep report unreadable: %s" % exc)

    report = {
        "schema": 1,
        "budgetBytes": BUDGET,
        "plan": {"rigs": 50, "clips": 500, "pairs": 25000},
        "saMotion": git_branch(SA_MOTION),
        "comSaUnity": git_branch(COM_SA_UNITY),
        "saDemo": {"present": SA_DEMO.exists()},
        "fixtures": fixtures,
        "allFixturesFrozen": all(f.get("frozen", False) for f in fixtures) and len(fixtures) == 3,
        "family": family,
        "showcase": {"files": showcase_files, "clipAssets": showcase_clips},
        "missingSampleAssets": missing_samples,
        "synty": synty,
        "sweep": sweep_report,
        "shortfall": {
            "rigs": "3 portable target rigs inventoried, not 50",
            "clips": "2 portable clips inventoried, not 500",
            "pairs": "4 compatible streamed pairs, not 25000",
            "note": "Synty FBX set is Unity-importer content counted only; "
                    "UAL/Kyle sample assets are referenced by Editor code but absent "
                    "from the portable repos; no corpus was fabricated, renamed or copied",
        },
        "errors": errors,
    }
    json.dump(report, sys.stdout, indent=2)
    sys.stdout.write("\n")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
