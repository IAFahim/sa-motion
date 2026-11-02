#!/usr/bin/env python3
"""C08 full-corpus batch encode/reload report (stdlib only).

Reads the Unity extractor output at Temp/sa/c08-full/_manifest.json
(UAL1 + UAL2 full takes + 9 Kyle .anims), then drives a one-shot C#
harness (materialized under /tmp, referencing the CF1 sources) that
performs, per clip, the same raw Encode->Load->Sample contract the
xunit C08Conformance gate replays:

  raw Encode (CompressionLimits.Default) -> SharedClipReader.Load ->
  Sample at all samples / quarters / midpoints (+ loop end) with
  ObjectSpaceError.Measure vs ClipSampling.At expectations +
  root-endpoint recompose (RootMotion.Delta vs first/last root span).

The harness prints one JSON object per line on stdout; this script
renders the per-clip table, packed-vs-raw byte ratio on a 6-clip
subset, dedup groups by exact payload hash, totals + wall time.

Exit 0 when every clip round-trips inside the gate; 1 otherwise.
"""

import json
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

CF1 = Path(__file__).resolve().parents[1]
DEFAULT_DIR = Path("/home/i/ruk2/Temp/sa/c08-full")
HARNESS_DIR = Path("/tmp/c08batch")

# 6-clip packed-vs-raw subset (base take names, without the Armature| prefix).
SUBSET = ["Idle_Loop", "Walk_Loop", "Sprint_Loop",
          "Walk_Carry_Loop", "Idle", "Kyle_Walk"]

CSHARP = r"""
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SA.Animation;
using SA.Animation.Content;
using SA.Motion.Numerics;

public static class C08Batch
{
    public static int Main(string[] args)
    {
        var dir = args[0];
        var subset = new HashSet<string>(args[1].Split(',', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "_manifest.json")));
        foreach (var entry in manifest.RootElement.GetProperty("clips").EnumerateArray())
        {
            var name = entry.GetProperty("clip").GetString() ?? "?";
            var file = entry.GetProperty("file").GetString() ?? "";
            try
            {
                Console.WriteLine(Run(dir, name, file, subset));
            }
            catch (Exception ex)
            {
                Console.WriteLine("{\"clip\":" + Q(name) + ",\"ok\":false,\"error\":" + Q(ex.GetType().Name + ": " + ex.Message) + "}");
            }
        }
        return 0;
    }

    static string Run(string dir, string name, string file, HashSet<string> subset)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, file)));
        var root = document.RootElement;
        var rig = root.GetProperty("rig").GetString() ?? "?";
        var bonesElement = root.GetProperty("bones");
        var framesElement = root.GetProperty("frames");
        var sampleCount = root.GetProperty("sampleCount").GetInt32();
        var duration = root.GetProperty("duration").GetDouble();
        var loop = root.GetProperty("loop").GetBoolean();
        var bones = bonesElement.GetArrayLength();
        var names = new string[bones];
        var parents = new int[bones];
        var index = 0;
        foreach (var bone in bonesElement.EnumerateArray())
        {
            names[index] = bone.GetProperty("path").GetString() ?? ("b" + index);
            var parent = bone.GetProperty("parent").GetInt32();
            parents[index] = index == 0 && parent == 0 ? -1 : parent;
            index++;
        }
        var samples = new Pose[bones * sampleCount];
        var rootMotion = new RigidPose[sampleCount];
        index = 0;
        foreach (var frame in framesElement.EnumerateArray())
        {
            ReadPoses(frame.GetProperty("p"), frame.GetProperty("q"), frame.GetProperty("s"), samples, index * bones, bones);
            var rp = frame.GetProperty("rootP");
            var rq = frame.GetProperty("rootQ");
            rootMotion[index] = new RigidPose(
                new Float3(rp[0].GetSingle(), rp[1].GetSingle(), rp[2].GetSingle()),
                new Quaternion(rq[0].GetSingle(), rq[1].GetSingle(), rq[2].GetSingle(), rq[3].GetSingle()));
            index++;
        }
        var reference = new Pose[bones];
        Array.Copy(samples, reference, bones);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder(512);
        sb.Append("{\"clip\":").Append(Q(name)).Append(",\"rig\":").Append(Q(rig)).Append(",\"file\":").Append(Q(file));
        sb.Append(",\"frames\":").Append(sampleCount).Append(",\"bones\":").Append(bones);
        sb.Append(",\"duration\":").Append(duration.ToString("G9", inv));
        sb.Append(",\"loop\":").Append(loop ? "true" : "false");
        if (ContentIdentity.Skeleton(rig, 1, names, parents, reference, out var family) != AnimationError.None || family.IsEmpty)
        {
            sb.Append(",\"ok\":false,\"error\":\"family\"}");
            return sb.ToString();
        }
        var policy = RootPolicy.None;
        foreach (var pose in rootMotion)
        {
            if (RootIsIdentity(pose))
            {
                continue;
            }
            policy = RootIsPlanar(pose) && policy != RootPolicy.FullRigid ? RootPolicy.Planar : RootPolicy.FullRigid;
        }
        sb.Append(",\"policy\":\"").Append(policy.ToString()).Append('"');
        var limits = CompressionLimits.Default;
        var encodeError = SharedClipEncoder.Encode(family, parents, reference, samples, sampleCount, duration, loop, policy,
            rootMotion, Array.Empty<Pose>(), Array.Empty<float>(), Array.Empty<PoseMarker>(), false, limits, out var bytes, out var report);
        sb.Append(",\"encode\":\"").Append(encodeError.ToString()).Append('"');
        if (encodeError != AnimationError.None)
        {
            sb.Append(",\"ok\":false}");
            return sb.ToString();
        }
        sb.Append(",\"rawBytes\":").Append(bytes.Length);
        sb.Append(",\"rawSegments\":").Append(report.Segments).Append(",\"rawFallback\":").Append(report.RawFallbackSegments);
        sb.Append(",\"sha256\":\"").Append(Convert.ToHexString(SHA256.HashData(bytes))).Append('"');
        var loadError = SharedClipReader.Load(bytes, family, reference, out var clip);
        sb.Append(",\"load\":\"").Append(loadError.ToString()).Append('"');
        if (loadError != AnimationError.None || clip == null)
        {
            sb.Append(",\"ok\":false}");
            return sb.ToString();
        }
        var points = new List<double>();
        for (var i = 0; i < sampleCount; i++)
        {
            points.Add(duration * i / (sampleCount - 1));
        }
        for (var k = 1; k < 4; k++)
        {
            points.Add(duration * k / 4);
        }
        for (var i = 0; i + 1 < sampleCount; i++)
        {
            points.Add(duration * (i + 0.5) / (sampleCount - 1));
        }
        if (loop && sampleCount > 1)
        {
            points.Add(duration * (1 - 1e-7));
        }
        var actual = new Pose[bones];
        var expected = new Pose[bones];
        var scratch = new RigidPose[bones * 2];
        var maxAngle = 0f;
        var maxPos = 0f;
        var sampleError = AnimationError.None;
        foreach (var seconds in points)
        {
            if (clip.Sample(seconds, reference, actual) != AnimationError.None)
            {
                sampleError = AnimationError.TimeOutOfRange;
                break;
            }
            var span = seconds;
            if (loop && duration > 0)
            {
                span = seconds - duration * Math.Floor(seconds / duration);
            }
            var frame = (float)(span / duration * (sampleCount - 1));
            for (var b = 0; b < bones; b++)
            {
                expected[b] = ClipSampling.At(samples, bones, b, frame);
            }
            if (ObjectSpaceError.Measure(parents, expected, actual, Array.Empty<PoseMarker>(), scratch, out var e) != AnimationError.None)
            {
                sampleError = AnimationError.InvalidFrame;
                break;
            }
            if (e.AngleRadians > maxAngle)
            {
                maxAngle = e.AngleRadians;
            }
            if (e.PositionMetres > maxPos)
            {
                maxPos = e.PositionMetres;
            }
        }
        sb.Append(",\"sample\":\"").Append(sampleError.ToString()).Append('"');
        sb.Append(",\"maxAngle\":").Append(maxAngle.ToString("G9", inv));
        sb.Append(",\"maxPos\":").Append(maxPos.ToString("G9", inv));
        var endError = RootMotion.Delta(clip.Root, duration, 0, duration, loop, 1f, out var end);
        RigidPose spanPose = rootMotion[sampleCount - 1];
        var composeOk = RigidMath.TryInverse(rootMotion[0], out var inverse0);
        RigidPose composed = default;
        if (composeOk)
        {
            composeOk = RigidMath.TryCompose(inverse0, spanPose, out composed);
        }
        if (composeOk)
        {
            spanPose = composed;
        }
        if (loop && composeOk)
        {
            RigidPose wrapped = default;
            composeOk = RigidMath.TryCompose(spanPose, rootMotion[0], out wrapped);
            if (composeOk)
            {
                spanPose = wrapped;
            }
        }
        var dPos = -1f;
        var dAng = -1f;
        if (composeOk)
        {
            dPos = (end.Position - spanPose.Position).Length;
            dAng = RotationVector.Log(Quaternion.Multiply(end.Rotation, Quaternion.InverseUnit(spanPose.Rotation))).Length;
        }
        sb.Append(",\"endpoint\":\"").Append(endError.ToString()).Append('"');
        sb.Append(",\"dPos\":").Append(dPos.ToString("G9", inv));
        sb.Append(",\"dAng\":").Append(dAng.ToString("G9", inv));
        sb.Append(",\"travel\":").Append(end.Position.Length.ToString("G9", inv));
        sb.Append(",\"turn\":").Append(RotationVector.Log(end.Rotation).Length.ToString("G9", inv));
        var packedBytes = -1;
        var bar = name.LastIndexOf('|');
        var baseName = bar >= 0 ? name.Substring(bar + 1) : name;
        if (subset.Contains(baseName))
        {
            var packedError = SharedClipEncoder.Encode(family, parents, reference, samples, sampleCount, duration, loop, policy,
                rootMotion, Array.Empty<Pose>(), Array.Empty<float>(), Array.Empty<PoseMarker>(), true, limits, out var packed, out var _);
            if (packedError == AnimationError.None
                && SharedClipReader.Load(packed, family, reference, out var packedClip) == AnimationError.None
                && packedClip != null
                && packedClip.Sample(duration / 2, reference, actual) == AnimationError.None)
            {
                packedBytes = packed.Length;
            }
        }
        sb.Append(",\"packedBytes\":").Append(packedBytes);
        var ok = sampleError == AnimationError.None && endError == AnimationError.None && composeOk
            && maxAngle <= limits.AngleRadians && maxPos <= limits.PositionMetres
            && dPos >= 0 && dPos <= 1e-4f && dAng >= 0 && dAng <= 1e-3f;
        sb.Append(",\"ok\":").Append(ok ? "true" : "false").Append('}');
        return sb.ToString();
    }

    static bool RootIsIdentity(in RigidPose pose)
    {
        return pose.Position.LengthSquared <= 1e-12f && Math.Abs(pose.Rotation.W) >= 0.999999f;
    }

    static bool RootIsPlanar(in RigidPose pose)
    {
        return Math.Abs(pose.Position.Y) <= 1e-6f && Math.Abs(pose.Rotation.X) <= 1e-6f && Math.Abs(pose.Rotation.Z) <= 1e-6f;
    }

    static void ReadPoses(JsonElement positions, JsonElement rotations, JsonElement scales, Pose[] samples, int first, int bones)
    {
        for (var i = 0; i < bones; i++)
        {
            var p = positions[i];
            var q = rotations[i];
            samples[first + i] = new Pose
            {
                Position = new Float3(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle()),
                Rotation = new Quaternion(q[0].GetSingle(), q[1].GetSingle(), q[2].GetSingle(), q[3].GetSingle()),
                Scale = Float3.One
            };
        }
    }

    static string Q(string text)
    {
        return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
"""

CSPROJ = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="__SRC__/SA.Animation/SA.Animation.csproj" />
    <ProjectReference Include="__SRC__/SA.Animation.Content/SA.Animation.Content.csproj" />
  </ItemGroup>
</Project>
"""


def fail(message):
    print("c08_batch: " + message, file=sys.stderr)
    return 1


def main():
    t0 = time.time()
    data_dir = Path(os.environ.get("SA_C08_DIR", DEFAULT_DIR))
    manifest_path = data_dir / "_manifest.json"
    if not manifest_path.is_file():
        return fail("manifest missing: " + str(manifest_path))
    manifest = json.loads(manifest_path.read_text())
    entries = manifest["clips"]
    print("c08_batch: %d clips listed in %s" % (len(entries), manifest_path))

    if HARNESS_DIR.exists():
        shutil.rmtree(HARNESS_DIR)
    (HARNESS_DIR / "tmp").mkdir(parents=True)
    (HARNESS_DIR / "Program.cs").write_text(CSHARP)
    (HARNESS_DIR / "c08batch.csproj").write_text(
        CSPROJ.replace("__SRC__", (CF1 / "src").as_posix()))

    env = dict(os.environ)
    env["TMPDIR"] = str(HARNESS_DIR / "tmp")
    env["NUGET_PACKAGES"] = str(CF1 / "artifacts" / "nuget")
    env["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1"
    build = time.time()
    proc = subprocess.run(
        ["dotnet", "run", "--project", str(HARNESS_DIR / "c08batch.csproj"),
         "--configuration", "Release", "--", str(data_dir), ",".join(SUBSET)],
        capture_output=True, text=True, timeout=1200, cwd=str(CF1), env=env)
    encode_wall = time.time() - build
    if proc.returncode != 0:
        print(proc.stderr[-4000:], file=sys.stderr)
        return fail("harness exit %d" % proc.returncode)
    rows = []
    for line in proc.stdout.splitlines():
        line = line.strip()
        if line.startswith("{"):
            rows.append(json.loads(line))
    if len(rows) != len(entries):
        return fail("harness returned %d rows for %d clips" % (len(rows), len(entries)))

    deg = 180.0 / 3.141592653589793
    print("")
    print("rig | clip | frames | bones | rawBytes | maxAngDeg | maxPosMm | dPosMm | dAngDeg | travelM | turnRad | policy | ok")
    total_bytes = 0
    ok_count = 0
    for r in rows:
        if r.get("ok"):
            ok_count += 1
            total_bytes += r.get("rawBytes", 0)
        else:
            total_bytes += r.get("rawBytes", 0) or 0
        num = (int, float)
        print("%s | %s | %d | %d | %s | %.4f | %.3f | %s | %s | %s | %s | %s | %s" % (
            r.get("rig"), r.get("clip"), r.get("frames", -1), r.get("bones", -1),
            r.get("rawBytes", r.get("encode", r.get("error", "?"))),
            deg * (r.get("maxAngle") or 0.0), 1000.0 * (r.get("maxPos") or 0.0),
            ("%.4f" % (1000.0 * r["dPos"]) if isinstance(r.get("dPos"), num) and r["dPos"] >= 0 else "?"),
            ("%.4f" % (deg * r["dAng"]) if isinstance(r.get("dAng"), num) and r["dAng"] >= 0 else "?"),
            r.get("travel", "?"), r.get("turn", "?"), r.get("policy", "?"),
            "PASS" if r.get("ok") else "FAIL(" + str(r.get("encode", r.get("load", r.get("sample", r.get("error", "?"))))) + ")"))

    print("")
    print("packed-vs-raw byte ratio (6-clip subset):")
    for r in rows:
        if isinstance(r.get("packedBytes"), int) and r["packedBytes"] > 0:
            print("  %s %s: packed %d / raw %d = %.3f" % (
                r["rig"], r["clip"], r["packedBytes"], r["rawBytes"],
                r["packedBytes"] / r["rawBytes"]))

    print("")
    print("dedup groups by exact payload sha256:")
    groups = {}
    for r in rows:
        if r.get("sha256"):
            groups.setdefault(r["sha256"][:16], []).append(
                "%s:%s" % (r["rig"], r["clip"]))
    dupes = {k: v for k, v in groups.items() if len(v) > 1}
    print("  unique payloads: %d of %d encoded; duplicate groups: %d" % (
        len(groups), sum(1 for r in rows if r.get("sha256")), len(dupes)))
    for k in sorted(dupes):
        print("  %s... x%d: %s" % (k, len(dupes[k]), ", ".join(sorted(dupes[k]))))

    wall = time.time() - t0
    print("")
    print("totals: clips %d/%d PASS, raw bytes %d, harness wall %.1fs, total wall %.1fs" % (
        ok_count, len(rows), total_bytes, encode_wall, wall))
    print("shortfall vs plan 16.1: corpus here is 3 rig templates "
          "(UAL1_Standard, UAL2_Standard, Robot Kyle), not 50 characters; "
          "the UAL FBXs carry 43 takes each (meta + Editor agree), so the "
          "full corpus is 43+43+9 = 95 clips, not 97. All 95 extractor "
          "root tracks are constant identity (no Animator on the preview "
          "rig), so endpoint travel/turn are trivially zero and root "
          "coverage stays synthetic-only per the C08 handoff.")
    report = {"tool": "c08_batch", "clips": len(rows), "passed": ok_count,
              "rawBytes": total_bytes, "harnessWall": encode_wall,
              "wall": wall, "rows": rows}
    (data_dir / "_batch.json").write_text(json.dumps(report, indent=1))
    print("report: " + str(data_dir / "_batch.json"))
    return 0 if ok_count == len(rows) else 1


if __name__ == "__main__":
    sys.exit(main())
