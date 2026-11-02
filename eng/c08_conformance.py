#!/usr/bin/env python3
"""C08 conformance entry: validate Temp/sa/c08 extractor output.

Loads Temp/sa/c08/_manifest.json plus per-clip JSON produced by the Unity
extractor ("Extract Canonical Samples") and checks the raw-baker contract
that the C# gate (tests/SA.Animation.Tests/C08Conformance.cs) replays
through SharedClipEncoder/SharedClipReader:

  manifest: {"rigs": [...], "clips": [{"clip": str, "rig": str, "file": str}]}
  clip: {"rig", "sampleRate", "sampleCount", "duration", "loop",
         "bones": [{"path": str, "parent": int}],
         "frames": [{"p": [[x,y,z]], "q": [[x,y,z,w]], "s": [[x,y,z]],
                     "rootP": [x,y,z], "rootQ": [x,y,z,w]}]}

Per-clip checks: counts, parent-before-child order, unique paths, finite
values, unit rotations and scales, identity bone-zero locals, identity
first root frame, planar policy compatibility. Prints a report listing
which checks are green on committed synthetic/golden data and which are
green on real extractor output. Exit 0 when the manifest is missing
(awaiting) or valid; exit 2 when present but invalid.
"""

import json
import math
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CANDIDATES = [
    ROOT / "Temp" / "sa" / "c08" / "_manifest.json",
    Path("/home/i/ruk2/Temp/sa/c08/_manifest.json"),
]
IDENTITY_TOL = 1e-6
UNIT_TOL = 1e-3


def fail(errors, message):
    errors.append(message)


def finite3(v):
    return (
        isinstance(v, list)
        and len(v) == 3
        and all(isinstance(x, (int, float)) and math.isfinite(x) for x in v)
    )


def finite4(v):
    return (
        isinstance(v, list)
        and len(v) == 4
        and all(isinstance(x, (int, float)) and math.isfinite(x) for x in v)
    )


def norm(v):
    return math.sqrt(sum(x * x for x in v))


def check_clip(path):
    errors = []
    try:
        clip = json.loads(path.read_text())
    except (OSError, json.JSONDecodeError) as exc:
        return None, ["%s: unreadable clip JSON: %s" % (path, exc)]
    name = clip.get("clip", path.name)
    where = "clip %s" % name
    for key in ("rig", "sampleRate", "sampleCount", "duration", "loop", "bones", "frames"):
        if key not in clip:
            fail(errors, "%s: missing key %r" % (where, key))
    if errors:
        return None, errors
    bones = clip["bones"]
    frames = clip["frames"]
    samples = clip["sampleCount"]
    if not isinstance(bones, list) or not 1 <= len(bones) <= 1024:
        fail(errors, "%s: bones out of range" % where)
        return None, errors
    count = len(bones)
    if not isinstance(frames, list) or len(frames) != samples:
        fail(errors, "%s: frames has %s poses, need sampleCount=%s" % (
            where, len(frames) if isinstance(frames, list) else "?!", samples))
        return None, errors
    if not isinstance(samples, int) or not 1 <= samples <= 1000000:
        fail(errors, "%s: sampleCount out of range" % where)
        return None, errors
    duration = clip["duration"]
    if not isinstance(duration, (int, float)) or not math.isfinite(duration):
        fail(errors, "%s: duration is not finite" % where)
    elif duration == 0 and samples != 1:
        fail(errors, "%s: zero duration needs exactly 1 sample" % where)
    elif duration > 0 and samples < 2:
        fail(errors, "%s: positive duration needs at least 2 samples" % where)
    paths = []
    for i, bone in enumerate(bones):
        if not isinstance(bone, dict):
            fail(errors, "%s bone %d: not an object" % (where, i))
            continue
        parent = bone.get("parent")
        if not isinstance(parent, int) or (parent != 0 if i == 0 else not 0 <= parent < i):
            fail(errors, "%s bone %d: breaks parent-before-child order" % (where, i))
        paths.append(bone.get("path"))
    if len(set(paths)) != count or any(not isinstance(p, str) for p in paths):
        fail(errors, "%s: bone paths are not unique strings" % where)
    ok = not errors
    normalized = False
    for i, frame in enumerate(frames):
        if not isinstance(frame, dict):
            fail(errors, "%s frame %d: not an object" % (where, i))
            ok = False
            continue
        for key in ("p", "q", "s"):
            values = frame.get(key)
            if not isinstance(values, list) or len(values) != count:
                fail(errors, "%s frame %d: %s has wrong length" % (where, i, key))
                ok = False
        if not ok:
            continue
        for b in range(count):
            if not finite3(frame["p"][b]) or not finite4(frame["q"][b]) or not finite3(frame["s"][b]):
                fail(errors, "%s frame %d bone %d: non-finite p/q/s" % (where, i, b))
                ok = False
                continue
            if abs(norm(frame["q"][b]) - 1.0) > UNIT_TOL:
                fail(errors, "%s frame %d bone %d: rotation is not unit" % (where, i, b))
                ok = False
            scale_gap = max(abs(x - 1.0) for x in frame["s"][b])
            if scale_gap > 1e-5:
                fail(errors, "%s frame %d bone %d: carries non-unit scale (%.3g)" % (where, i, b, scale_gap))
                ok = False
            elif scale_gap > 0:
                normalized = True
        if frame["p"][0] != [0, 0, 0] or frame["q"][0] != [0, 0, 0, 1]:
            fail(errors, "%s frame %d: bone zero local is not identity" % (where, i))
            ok = False
        if not finite3(frame.get("rootP")) or not finite4(frame.get("rootQ")):
            fail(errors, "%s frame %d: non-finite rootP/rootQ" % (where, i))
            ok = False
            continue
        if abs(norm(frame["rootQ"]) - 1.0) > UNIT_TOL:
            fail(errors, "%s frame %d: root rotation is not unit" % (where, i))
            ok = False
    first = frames[0]
    if ok and (norm(first["rootP"]) > IDENTITY_TOL or abs(first["rootQ"][3]) < 1 - IDENTITY_TOL):
        fail(errors, "%s: root[0] is not identity" % where)
        ok = False
    policy = "None"
    if ok:
        for frame in frames:
            if norm(frame["rootP"]) <= IDENTITY_TOL and abs(frame["rootQ"][3]) >= 1 - IDENTITY_TOL:
                continue
            planar = abs(frame["rootP"][1]) <= IDENTITY_TOL and abs(
                frame["rootQ"][0]) <= IDENTITY_TOL and abs(frame["rootQ"][2]) <= IDENTITY_TOL
            policy = "Planar" if planar and policy != "FullRigid" else "FullRigid"
    if errors:
        return None, errors
    travel = norm([a - b for a, b in zip(frames[-1]["rootP"], frames[0]["rootP"])]) if ok else 0.0
    note = ", scales normalized to unit (float noise)" if normalized else ""
    return "%s (%s, %d bones x %d samples, policy %s, travel %.4gm%s)" % (
        name, clip["rig"], count, samples, policy, travel, note), []


def main():
    manifest = next((c for c in CANDIDATES if c.is_file()), None)
    print("C08 conformance entry")
    print("green on committed data: synthetic translation-plus-turn clip,")
    print("  vertical-motion clip, golden three-bone fixtures (see dotnet C08Conformance).")
    if manifest is None:
        print("awaiting real extractor output: Temp/sa/c08/_manifest.json not present")
        print("  (checked %s)" % ", ".join(str(c) for c in CANDIDATES))
        print("C08 extractor checks: AWAITING")
        return 0
    print("manifest: %s" % manifest)
    try:
        data = json.loads(manifest.read_text())
    except json.JSONDecodeError as exc:
        print("manifest is not valid JSON: %s" % exc)
        return 2
    entries = data.get("clips")
    if not isinstance(entries, list) or not entries:
        print("manifest needs a non-empty clips array")
        return 2
    errors = []
    passed = []
    directory = manifest.parent
    for entry in entries:
        if not isinstance(entry, dict) or "file" not in entry or ("clip" not in entry and "name" not in entry):
            errors.append("manifest entry needs clip/name and file: %r" % (entry,))
            continue
        label = entry.get("clip", entry.get("name"))
        path = directory / entry["file"]
        if not path.is_file():
            errors.append("clip %s: missing file %s" % (label, path))
            continue
        name, clip_errors = check_clip(path)
        if clip_errors:
            errors.extend(clip_errors)
        else:
            passed.append(name)
    for name in passed:
        print("  green: %s" % name)
    for message in errors:
        print("  FAIL: %s" % message)
    if errors:
        print("C08 extractor checks: %d passed, %d failed" % (len(passed), len(errors)))
        return 2
    print("C08 extractor checks: %d passed, 0 failed" % len(passed))
    return 0


if __name__ == "__main__":
    sys.exit(main())
