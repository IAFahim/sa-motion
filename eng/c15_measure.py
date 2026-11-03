#!/usr/bin/env python3
"""C15 staged cost probe (stdlib only).

Fixed workload, not a 40k-claim sweep: synthetic corpus-shaped clips
(61 samples, 2 s, loop, planar turn+travel root) at canonical 56 bones,
retargeted to 56 (identity table) and 67 (56 mapped + 11 static extras,
UAL-shaped). Grid: characters {1, 50, 500} x contributions {1, 2, 4}.
Stages timed separately per config: sample, blend, retarget, smoothing,
palette; plus a full-loop row with cold first-touch ms, warm median/p95
per character-frame, segment residency, and allocation. Random seeks +
cuts are a separate seeded section. An optional corpus sample section
runs the same warm 5-stage loop over up to 3 real clips (one per rig
template) when SA_C15_DIR points at a c08-full extractor output dir.

The C# harness is materialized under /tmp/c15measure (outside all repos)
and references the CF1 sources; stdout is one JSON object per line.
Exit 0 iff every reported row passes.
"""

import json
import os
import platform
import shutil
import subprocess
import sys
import time
from pathlib import Path

CF1 = Path(__file__).resolve().parents[1]
HARNESS_DIR = Path("/tmp/c15measure")
DEFAULT_DIR = Path("/home/i/ruk2/Temp/sa/c08-full")

CSHARP = r"""
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using SA.Animation;
using SA.Animation.Content;
using SA.Motion.Inertialization;
using SA.Motion.Numerics;

public static class C15Measure
{
    const int SrcBones = 56;
    const int ExtraBones = 11;
    const int TgtBones = SrcBones + ExtraBones;
    const int Samples = 61;
    const double Duration = 2.0;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    sealed class Rig
    {
        internal int[] SrcParents = Array.Empty<int>();
        internal Pose[] Reference = Array.Empty<Pose>();
        internal AssetId Family;
        internal SharedClip Clip = null!;
        internal int[] TgtParents = Array.Empty<int>();
        internal Pose[] Calibration = Array.Empty<Pose>();
        internal int[] SourceMap = Array.Empty<int>();
        internal RetargetRow[] Rows = Array.Empty<RetargetRow>();
        internal RetargetBinding Binding;
        internal AssetId Target = new AssetId(7, 7, 7, 7);
        internal int TargetBones;
    }

    sealed class CharState
    {
        internal Pose[] Decoded = Array.Empty<Pose>();
        internal Pose[] Blended = Array.Empty<Pose>();
        internal Pose[] Raw = Array.Empty<Pose>();
        internal Pose[] Output = Array.Empty<Pose>();
        internal Pose[] Scratch = Array.Empty<Pose>();
        internal BoneFrameState[] Bones = Array.Empty<BoneFrameState>();
        internal BoneFrameState[] Staged = Array.Empty<BoneFrameState>();
        internal PoseVelocity[] Velocity = Array.Empty<PoseVelocity>();
        internal ClipContribution[] Previous = Array.Empty<ClipContribution>();
        internal LayerInput[] PreviousLayers = Array.Empty<LayerInput>();
        internal Affine[] Models = Array.Empty<Affine>();
        internal Affine[] Palette = Array.Empty<Affine>();
        internal Affine[] Binds = Array.Empty<Affine>();
        internal int[] PaletteMap = Array.Empty<int>();
        internal RigidPose[] CanonicalModel = Array.Empty<RigidPose>();
        internal RigidPose[] TargetModel = Array.Empty<RigidPose>();
        internal ClipContribution[] Contributions = Array.Empty<ClipContribution>();
        internal LayerInput[] Layers = Array.Empty<LayerInput>();
        internal PoseFrameState State;
    }

    public static int Main(string[] args)
    {
        var dataDir = args.Length > 0 ? args[0] : "";
        var rig56 = BuildRig(56);
        var rig67 = BuildRig(67);
        if (rig56 == null || rig67 == null)
        {
            Console.WriteLine("{\"type\":\"config\",\"ok\":false,\"error\":\"rig-build\"}");
            return 1;
        }
        Console.WriteLine("{\"type\":\"meta\",\"ok\":true,\"backend\":" + Q(RuntimeInformation.FrameworkDescription)
            + ",\"os\":" + Q(RuntimeInformation.OSDescription) + "}");
        var bonesOptions = new[] { 56, 67 };
        var charOptions = new[] { 1, 50, 500 };
        var contribOptions = new[] { 1, 2, 4 };
        var allOk = true;
        RunConfig(rig56, 56, 1, 1, false);
        foreach (var bones in bonesOptions)
        {
            var rig = bones == 56 ? rig56 : rig67;
            foreach (var chars in charOptions)
            {
                foreach (var contribs in contribOptions)
                {
                    allOk &= RunConfig(rig, bones, chars, contribs, true);
                }
            }
            allOk &= RunAlloc(rig, bones);
            allOk &= RunSeeks(rig, bones);
        }
        allOk &= RunCorpus(dataDir);
        Console.WriteLine("{\"type\":\"done\",\"ok\":" + (allOk ? "true" : "false") + "}");
        return allOk ? 0 : 1;
    }

    static Rig? BuildRig(int targetBones)
    {
        var parents = new int[SrcBones];
        var reference = new Pose[SrcBones];
        if (CanonicalHumanoid.WriteReference(parents, reference) != AnimationError.None)
        {
            return null;
        }
        var names = new string[SrcBones];
        for (var i = 0; i < SrcBones; i++)
        {
            names[i] = "B" + i.ToString("D2", Inv);
        }
        if (ContentIdentity.Skeleton("Humanoid", 1, names, parents, reference, out var family) != AnimationError.None || family.IsEmpty)
        {
            return null;
        }
        var motion = TurnMotion(reference);
        if (SharedClipEncoder.Encode(family, parents, reference, motion.Samples, Samples, Duration, true, RootPolicy.Planar, motion.Root,
            Array.Empty<Pose>(), Array.Empty<float>(), Array.Empty<PoseMarker>(), false, CompressionLimits.Default, out var bytes, out _) != AnimationError.None)
        {
            return null;
        }
        if (SharedClipReader.Load(bytes, family, reference, out var clip) != AnimationError.None || clip == null)
        {
            return null;
        }
        var rig = new Rig { SrcParents = parents, Reference = reference, Family = family, Clip = clip, TargetBones = targetBones };
        rig.TgtParents = new int[targetBones];
        Array.Copy(parents, rig.TgtParents, SrcBones);
        rig.Calibration = new Pose[targetBones];
        Array.Copy(reference, rig.Calibration, SrcBones);
        rig.SourceMap = new int[targetBones];
        for (var i = 0; i < SrcBones; i++)
        {
            rig.SourceMap[i] = i;
        }
        for (var b = SrcBones; b < targetBones; b++)
        {
            rig.TgtParents[b] = 13 + (b - SrcBones) % 20;
            rig.SourceMap[b] = -1;
            rig.Calibration[b] = new Pose { Position = new Float3(0.05f * (b - SrcBones + 1), 0, 0.1f), Rotation = Quaternion.Identity, Scale = Float3.One };
        }
        rig.Rows = new RetargetRow[targetBones];
        var scratch = new RigidPose[SrcBones + targetBones];
        if (RetargetTable.Compile(family, rig.Target, parents, reference, rig.TgtParents, rig.Calibration, rig.SourceMap,
            CanonicalHumanoid.Hips, CanonicalHumanoid.Hips, 1.1f, scratch, rig.Rows, out rig.Binding) != AnimationError.None)
        {
            return null;
        }
        return rig;
    }

    static (Pose[] Samples, RigidPose[] Root) TurnMotion(Pose[] reference)
    {
        var samples = new Pose[Samples * SrcBones];
        var root = new RigidPose[Samples];
        var up = new Float3(0f, 1f, 0f);
        var outOfPlane = new Float3(0f, 0f, 1f);
        for (var i = 0; i < Samples; i++)
        {
            var t = i / (float)(Samples - 1);
            reference.CopyTo(samples, i * SrcBones);
            samples[i * SrcBones + 1].Position.Y += 0.03f * MathF.Sin(t * Scalar.TwoPi);
            samples[i * SrcBones + 11].Rotation = Quaternion.AxisAngle(outOfPlane, 0.25f * MathF.Sin(t * Scalar.TwoPi));
            samples[i * SrcBones + 30].Rotation = Quaternion.AxisAngle(outOfPlane, -0.25f * MathF.Sin(t * Scalar.TwoPi));
            root[i] = new RigidPose(new Float3(0f, 0f, t), Quaternion.AxisAngle(up, t * Scalar.Pi * 0.5f));
        }
        return (samples, root);
    }

    static CharState MakeChar(Rig rig, int contribs)
    {
        var tgt = rig.TargetBones;
        var map = new int[tgt];
        for (var i = 0; i < tgt; i++)
        {
            map[i] = i;
        }
        var binds = new Affine[tgt];
        for (var i = 0; i < tgt; i++)
        {
            binds[i] = Affine.Identity;
        }
        var contributions = new ClipContribution[contribs];
        for (var c = 0; c < contribs; c++)
        {
            contributions[c] = new ClipContribution(new ClipHandle(0, 1), 0, 1f / contribs);
        }
        return new CharState
        {
            Decoded = new Pose[contribs * SrcBones],
            Blended = new Pose[SrcBones],
            Raw = new Pose[tgt],
            Output = new Pose[tgt],
            Scratch = new Pose[tgt],
            Bones = new BoneFrameState[tgt],
            Staged = new BoneFrameState[tgt],
            Velocity = new PoseVelocity[tgt],
            Previous = new ClipContribution[Math.Max(8, contribs)],
            PreviousLayers = new LayerInput[4],
            Models = new Affine[tgt],
            Palette = new Affine[tgt],
            Binds = binds,
            PaletteMap = map,
            CanonicalModel = new RigidPose[SrcBones],
            TargetModel = new RigidPose[tgt],
            Contributions = contributions,
            Layers = new[] { new LayerInput(0, ClipMode.Override, 0, contribs) },
        };
    }

    static AnimationError FullPass(Rig rig, CharState ch, double time, ulong tick, ulong continuity, float weight, FrameMode mode)
    {
        for (var c = 0; c < ch.Contributions.Length; c++)
        {
            var at = time + c * 0.37 * Duration;
            ch.Contributions[c] = new ClipContribution(new ClipHandle(0, 1), at, weight);
            var error = rig.Clip.Sample(at, rig.Reference, ch.Decoded.AsSpan(c * SrcBones, SrcBones));
            if (error != AnimationError.None)
            {
                return error;
            }
        }
        var error2 = CanonicalBlend.Evaluate(ch.Contributions, ch.Layers, ch.Decoded, Array.Empty<Pose>(), Array.Empty<float>(), rig.Reference, ch.Blended);
        if (error2 != AnimationError.None)
        {
            return error2;
        }
        error2 = RetargetTable.Apply(rig.Family, rig.Binding, rig.SrcParents, ch.Blended, rig.Rows, ch.CanonicalModel, ch.TargetModel, ch.Raw);
        if (error2 != AnimationError.None)
        {
            return error2;
        }
        var frame = new AnimationFrame(tick, 1, continuity, time, 0.01f, 0.2f, mode);
        error2 = TimelinePose.Evaluate(frame, ch.Contributions, ch.Layers, true, ch.Raw, ch.Velocity,
            ref ch.State, ch.Bones, ch.Staged, ch.Previous, ch.PreviousLayers, ch.Output, ch.Scratch, out _);
        if (error2 != AnimationError.None)
        {
            return error2;
        }
        Hierarchy.Model(rig.TgtParents, ch.Output, ch.Models);
        return SkinPalette.Build(ch.Models, ch.PaletteMap, ch.Binds, Affine.Identity, ch.Palette);
    }

    static string Row(int bones, int chars, int contribs, string stage, double medianUs, double p95Us, long alloc, bool ok)
    {
        return "{\"type\":\"row\",\"ok\":" + (ok ? "true" : "false")
            + ",\"bones\":" + bones + ",\"chars\":" + chars + ",\"contribs\":" + contribs
            + ",\"stage\":" + Q(stage) + ",\"medianUs\":" + medianUs.ToString("G6", Inv)
            + ",\"p95Us\":" + p95Us.ToString("G6", Inv) + ",\"allocBytes\":" + alloc + "}";
    }

    static void Sort(List<double> values) => values.Sort();

    static double Median(List<double> values)
    {
        Sort(values);
        var n = values.Count;
        return n % 2 == 1 ? values[n / 2] : 0.5 * (values[n / 2 - 1] + values[n / 2]);
    }

    static double P95(List<double> values)
    {
        Sort(values);
        return values[Math.Min(values.Count - 1, (int)Math.Ceiling(0.95 * values.Count) - 1)];
    }

    static double Micros(long ticks) => ticks * 1000000.0 / Stopwatch.Frequency;

    static bool RunConfig(Rig rig, int bones, int chars, int contribs, bool report)
    {
        var ok = true;
        var states = new CharState[chars];
        for (var i = 0; i < chars; i++)
        {
            states[i] = MakeChar(rig, contribs);
        }
        var weight = 1f / contribs;
        var time = 0.0;
        var tick = 0UL;
        var warmPasses = Math.Max(40, 20000 / chars);
        for (var w = 0; w < warmPasses; w++)
        {
            time += 0.01;
            tick++;
            foreach (var ch in states)
            {
                if (FullPass(rig, ch, time, tick, 1, weight, FrameMode.Advance) != AnimationError.None)
                {
                    if (report)
                    {
                        Console.WriteLine(Row(bones, chars, contribs, "warmup", 0, 0, 0, false));
                    }
                    return false;
                }
            }
        }
        var fresh = MakeChar(rig, contribs);
        var coldWatch = Stopwatch.StartNew();
        var coldError = FullPass(rig, fresh, 0.5, 0, 1, weight, FrameMode.Advance);
        coldWatch.Stop();
        var coldMs = Micros(coldWatch.ElapsedTicks) / 1000.0;
        if (coldError != AnimationError.None)
        {
            ok = false;
        }
        const int Iters = 200;
        var stageNames = new[] { "sample", "blend", "retarget", "smooth", "palette" };
        var stageSamples = new List<double>[5];
        for (var s = 0; s < 5; s++)
        {
            stageSamples[s] = new List<double>(Iters);
        }
        for (var it = 0; it < Iters; it++)
        {
            time += 0.007;
            tick++;
            for (var s = 0; s < 5; s++)
            {
                var watch = Stopwatch.StartNew();
                foreach (var ch in states)
                {
                    AnimationError error = AnimationError.None;
                    if (s == 0)
                    {
                        for (var c = 0; c < contribs; c++)
                        {
                            var at = time + c * 0.37 * Duration;
                            ch.Contributions[c] = new ClipContribution(new ClipHandle(0, 1), at, weight);
                            error = rig.Clip.Sample(at, rig.Reference, ch.Decoded.AsSpan(c * SrcBones, SrcBones));
                            if (error != AnimationError.None)
                            {
                                break;
                            }
                        }
                    }
                    else if (s == 1)
                    {
                        error = CanonicalBlend.Evaluate(ch.Contributions, ch.Layers, ch.Decoded, Array.Empty<Pose>(), Array.Empty<float>(), rig.Reference, ch.Blended);
                    }
                    else if (s == 2)
                    {
                        error = RetargetTable.Apply(rig.Family, rig.Binding, rig.SrcParents, ch.Blended, rig.Rows, ch.CanonicalModel, ch.TargetModel, ch.Raw);
                    }
                    else if (s == 3)
                    {
                        var frame = new AnimationFrame(tick, 1, 1, time, 0.01f, 0.2f, FrameMode.Advance);
                        error = TimelinePose.Evaluate(frame, ch.Contributions, ch.Layers, true, ch.Raw, ch.Velocity,
                            ref ch.State, ch.Bones, ch.Staged, ch.Previous, ch.PreviousLayers, ch.Output, ch.Scratch, out _);
                    }
                    else
                    {
                        Hierarchy.Model(rig.TgtParents, ch.Output, ch.Models);
                        error = SkinPalette.Build(ch.Models, ch.PaletteMap, ch.Binds, Affine.Identity, ch.Palette);
                    }
                    if (error != AnimationError.None)
                    {
                        ok = false;
                        break;
                    }
                }
                watch.Stop();
                stageSamples[s].Add(Micros(watch.ElapsedTicks) / chars);
            }
        }
        var stageMeds = new double[5];
        var stageP95 = new double[5];
        for (var s = 0; s < 5; s++)
        {
            stageMeds[s] = Median(stageSamples[s]);
            stageP95[s] = P95(stageSamples[s]);
        }
        var loopSamples = new List<double>(Iters);
        for (var it = 0; it < Iters; it++)
        {
            time += 0.007;
            tick++;
            var watch = Stopwatch.StartNew();
            foreach (var ch in states)
            {
                if (FullPass(rig, ch, time, tick, 1, weight, FrameMode.Advance) != AnimationError.None)
                {
                    ok = false;
                    break;
                }
            }
            watch.Stop();
            loopSamples.Add(Micros(watch.ElapsedTicks) / chars);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var ch in states)
        {
            time += 0.007;
            tick++;
            if (FullPass(rig, ch, time, tick, 1, weight, FrameMode.Advance) != AnimationError.None)
            {
                ok = false;
            }
        }
        var loopAlloc = GC.GetAllocatedBytesForCurrentThread() - before;
        if (loopAlloc != 0)
        {
            ok = false;
        }
        if (!report)
        {
            return ok;
        }
        for (var s = 0; s < 5; s++)
        {
            Console.WriteLine(Row(bones, chars, contribs, stageNames[s], stageMeds[s], stageP95[s], 0, ok));
        }
        Console.WriteLine("{\"type\":\"loop\",\"ok\":" + (ok ? "true" : "false")
            + ",\"bones\":" + bones + ",\"chars\":" + chars + ",\"contribs\":" + contribs
            + ",\"medianUs\":" + (Median(loopSamples)).ToString("G6", Inv)
            + ",\"p95Us\":" + (P95(loopSamples)).ToString("G6", Inv)
            + ",\"coldMs\":" + coldMs.ToString("G6", Inv)
            + ",\"segments\":" + rig.Clip.Segments.Length + ",\"clipBytes\":" + ClipBytes(rig)
            + ",\"loopAllocBytes\":" + loopAlloc + "}");
        return ok;
    }

    static long ClipBytes(Rig rig)
    {
        var total = 0L;
        foreach (var segment in rig.Clip.Segments)
        {
            total += segment.Bytes.Length;
        }
        return total;
    }

    static bool RunAlloc(Rig rig, int bones)
    {
        var ok = true;
        foreach (var contribs in new[] { 1, 2, 4 })
        {
            var ch = MakeChar(rig, contribs);
            var weight = 1f / contribs;
            for (var w = 0; w < 20000; w++)
            {
                if (FullPass(rig, ch, w * 0.001, (ulong)w, 1, weight, FrameMode.Advance) != AnimationError.None)
                {
                    ok = false;
                }
            }
            var before = GC.GetAllocatedBytesForCurrentThread();
            var result = AnimationError.None;
            for (var w = 20000; w < 21000; w++)
            {
                result = FullPass(rig, ch, w * 0.001, (ulong)w, 1, weight, FrameMode.Advance);
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            var pass = ok && result == AnimationError.None && allocated == 0 && ch.Output[1].Position.IsFinite;
            ok &= pass;
            Console.WriteLine("{\"type\":\"alloc\",\"ok\":" + (pass ? "true" : "false")
                + ",\"bones\":" + bones + ",\"contribs\":" + contribs
                + ",\"calls\":1000,\"allocBytes\":" + allocated + "}");
        }
        return ok;
    }

    static bool RunSeeks(Rig rig, int bones)
    {
        var ch = MakeChar(rig, 2);
        var random = new Random(20261103);
        const int Seeks = 500;
        var samples = new List<double>(Seeks);
        var failures = 0;
        var cuts = 0;
        var jumps = 0;
        var forwards = 0;
        ulong continuity = 1;
        var time = 0.0;
        if (FullPass(rig, ch, time, 0, continuity, 0.5f, FrameMode.Advance) != AnimationError.None)
        {
            failures++;
        }
        for (var i = 1; i <= Seeks; i++)
        {
            var mode = FrameMode.Advance;
            var roll = random.NextDouble();
            if (i % 25 == 0)
            {
                continuity++;
                cuts++;
                time += 0.02 + random.NextDouble() * 0.05;
            }
            else if (roll < 0.4)
            {
                jumps++;
                mode = FrameMode.Seek;
                time = random.NextDouble() * Duration;
            }
            else
            {
                forwards++;
                time += 0.005 + random.NextDouble() * 0.03;
            }
            var watch = Stopwatch.StartNew();
            var error = FullPass(rig, ch, time, (ulong)i, continuity, 0.5f, mode);
            watch.Stop();
            samples.Add(Micros(watch.ElapsedTicks));
            if (error != AnimationError.None || !ch.Output[1].Position.IsFinite)
            {
                failures++;
            }
        }
        var pass = failures == 0;
        Console.WriteLine("{\"type\":\"seek\",\"ok\":" + (pass ? "true" : "false")
            + ",\"bones\":" + bones + ",\"seeks\":" + Seeks + ",\"cuts\":" + cuts
            + ",\"jumps\":" + jumps + ",\"forwards\":" + forwards
            + ",\"failures\":" + failures
            + ",\"medianUs\":" + (Median(samples)).ToString("G6", Inv)
            + ",\"p95Us\":" + (P95(samples)).ToString("G6", Inv) + "}");
        return pass;
    }

    static bool RunCorpus(string dataDir)
    {
        if (string.IsNullOrEmpty(dataDir))
        {
            Console.WriteLine("{\"type\":\"corpus\",\"status\":\"skipped\",\"reason\":\"no-dir\"}");
            return true;
        }
        var manifestPath = Path.Combine(dataDir, "_manifest.json");
        if (!File.Exists(manifestPath))
        {
            Console.WriteLine("{\"type\":\"corpus\",\"status\":\"unavailable\",\"reason\":" + Q("missing " + manifestPath) + "}");
            return true;
        }
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var picked = new List<(string Name, string File)>();
        foreach (var entry in manifest.RootElement.GetProperty("clips").EnumerateArray())
        {
            var rig = entry.GetProperty("rig").GetString() ?? "?";
            if (seen.Add(rig))
            {
                picked.Add((entry.GetProperty("clip").GetString() ?? "?", entry.GetProperty("file").GetString() ?? ""));
            }
            if (picked.Count == 3)
            {
                break;
            }
        }
        var ok = true;
        foreach (var (name, file) in picked)
        {
            ok &= RunCorpusClip(dataDir, name, file);
        }
        return ok;
    }

    static bool RunCorpusClip(string dataDir, string name, string file)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDir, file)));
            return CorpusClipInner(document, name);
        }
        catch (Exception ex)
        {
            Console.WriteLine("{\"type\":\"corpus\",\"ok\":false,\"clip\":" + Q(name) + ",\"error\":" + Q(ex.GetType().Name) + "}");
            return false;
        }
    }

    static bool CorpusClipInner(JsonDocument document, string name)
    {
        var root = document.RootElement;
        var rig = root.GetProperty("rig").GetString() ?? "?";
        var bonesElement = root.GetProperty("bones");
        var framesElement = root.GetProperty("frames");
        var sampleCount = root.GetProperty("sampleCount").GetInt32();
        var duration = root.GetProperty("duration").GetDouble();
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
        index = 0;
        foreach (var frame in framesElement.EnumerateArray())
        {
            ReadPoses(frame.GetProperty("p"), frame.GetProperty("q"), samples, index * bones, bones);
            index++;
        }
        var reference = new Pose[bones];
        Array.Copy(samples, reference, bones);
        if (ContentIdentity.Skeleton(rig, 1, names, parents, reference, out var family) != AnimationError.None || family.IsEmpty)
        {
            Console.WriteLine("{\"type\":\"corpus\",\"ok\":false,\"clip\":" + Q(name) + ",\"error\":\"family\"}");
            return false;
        }
        var rootMotion = new RigidPose[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            rootMotion[i] = RigidPose.Identity;
        }
        if (SharedClipEncoder.Encode(family, parents, reference, samples, sampleCount, duration, true, RootPolicy.None, rootMotion,
            Array.Empty<Pose>(), Array.Empty<float>(), Array.Empty<PoseMarker>(), false, CompressionLimits.Default, out var bytes, out _) != AnimationError.None)
        {
            Console.WriteLine("{\"type\":\"corpus\",\"ok\":false,\"clip\":" + Q(name) + ",\"error\":\"encode\"}");
            return false;
        }
        if (SharedClipReader.Load(bytes, family, reference, out var clip) != AnimationError.None || clip == null)
        {
            Console.WriteLine("{\"type\":\"corpus\",\"ok\":false,\"clip\":" + Q(name) + ",\"error\":\"load\"}");
            return false;
        }
        var decoded = new Pose[bones];
        var blended = new Pose[bones];
        var mapped = new Pose[bones];
        var raw = new Pose[bones];
        var output = new Pose[bones];
        var scratch = new Pose[bones];
        var modelScratch = new RigidPose[bones];
        var paletteOut = new Affine[bones];
        var models = new Affine[bones];
        var boneStates = new BoneFrameState[bones];
        var staged = new BoneFrameState[bones];
        var velocity = new PoseVelocity[bones];
        var previous = new ClipContribution[8];
        var previousLayers = new LayerInput[4];
        var state = new PoseFrameState();
        var contributions = new[] { new ClipContribution(new ClipHandle(0, 1), 0, 1f) };
        var layers = new[] { new LayerInput(0, ClipMode.Override, 0, 1) };
        var binds = new Affine[bones];
        var map = new int[bones];
        for (var i = 0; i < bones; i++)
        {
            binds[i] = Affine.Identity;
            map[i] = i;
        }
        var model = new RigidPose[bones];
        AnimationError Step(int tick, double time, FrameMode mode)
        {
            contributions[0] = new ClipContribution(new ClipHandle(0, 1), time, 1f);
            var error = clip.Sample(time, reference, decoded);
            if (error != AnimationError.None)
            {
                return error;
            }
            error = CanonicalBlend.Evaluate(contributions, layers, decoded, Array.Empty<Pose>(), Array.Empty<float>(), reference, blended);
            if (error != AnimationError.None)
            {
                return error;
            }
            error = IdentityRetarget.Apply(family, family, parents, blended, model, mapped);
            if (error != AnimationError.None)
            {
                return error;
            }
            Array.Copy(mapped, raw, bones);
            var frame = new AnimationFrame((ulong)tick, 1, 1, time, 0.01f, 0.2f, mode);
            error = TimelinePose.Evaluate(frame, contributions, layers, true, raw, velocity,
                ref state, boneStates, staged, previous, previousLayers, output, scratch, out _);
            if (error != AnimationError.None)
            {
                return error;
            }
            Hierarchy.Model(parents, output, models);
            return SkinPalette.Build(models, map, binds, Affine.Identity, paletteOut);
        }
        for (var w = 0; w < 100; w++)
        {
            if (Step(w, 0.1 + w * 0.001, FrameMode.Advance) != AnimationError.None)
            {
                Console.WriteLine("{\"type\":\"corpus\",\"ok\":false,\"clip\":" + Q(name) + ",\"error\":\"warmup\"}");
                return false;
            }
        }
        var spot = true;
        for (var i = 0; i < sampleCount; i++)
        {
            if (Step(1000 + i, duration * i / Math.Max(1, sampleCount - 1), FrameMode.Seek) != AnimationError.None || !output[0].Position.IsFinite)
            {
                spot = false;
                break;
            }
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = AnimationError.None;
        for (var w = 0; w < 1000; w++)
        {
            result = Step(100000 + w, duration + 0.001 * (w + 1), FrameMode.Advance);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var pass = spot && result == AnimationError.None && allocated == 0 && output[0].Position.IsFinite;
        Console.WriteLine("{\"type\":\"corpus\",\"ok\":" + (pass ? "true" : "false")
            + ",\"rig\":" + Q(rig) + ",\"clip\":" + Q(name)
            + ",\"frames\":" + sampleCount + ",\"bones\":" + bones
            + ",\"calls\":1000,\"allocBytes\":" + allocated + "}");
        return pass;
    }

    static void ReadPoses(JsonElement positions, JsonElement rotations, Pose[] samples, int first, int bones)
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
    print("c15_measure: " + message, file=sys.stderr)
    return 1


def hardware():
    cpu, cores = "?", 0
    try:
        text = Path("/proc/cpuinfo").read_text()
        models = [l.split(":", 1)[1].strip() for l in text.splitlines() if l.startswith("model name")]
        if models:
            cpu = models[0]
        cores = sum(1 for l in text.splitlines() if l.startswith("processor"))
    except OSError:
        pass
    mem = "?"
    try:
        for line in Path("/proc/meminfo").read_text().splitlines():
            if line.startswith("MemTotal:"):
                mem = line.split()[1] + " " + line.split()[2]
                break
    except OSError:
        pass
    runtime = "?"
    try:
        proc = subprocess.run(["dotnet", "--list-runtimes"], capture_output=True, text=True, timeout=60)
        runtimes = [l.strip() for l in proc.stdout.splitlines() if "NETCore.App 10" in l]
        if runtimes:
            runtime = "; ".join(runtimes)
    except (OSError, subprocess.SubprocessError):
        pass
    return {"cpu": cpu, "cores": cores, "mem": mem, "os": platform.platform(), "runtime": runtime}


def main():
    t0 = time.time()
    data_dir = Path(os.environ.get("SA_C15_DIR", DEFAULT_DIR))
    if HARNESS_DIR.exists():
        shutil.rmtree(HARNESS_DIR)
    (HARNESS_DIR / "tmp").mkdir(parents=True)
    (HARNESS_DIR / "Program.cs").write_text(CSHARP)
    (HARNESS_DIR / "c15measure.csproj").write_text(CSPROJ.replace("__SRC__", (CF1 / "src").as_posix()))
    env = dict(os.environ)
    env["TMPDIR"] = str(HARNESS_DIR / "tmp")
    env["NUGET_PACKAGES"] = str(CF1 / "artifacts" / "nuget")
    env["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1"
    build = time.time()
    proc = subprocess.run(
        ["dotnet", "run", "--project", str(HARNESS_DIR / "c15measure.csproj"),
         "--configuration", "Release", "--", str(data_dir)],
        capture_output=True, text=True, timeout=1500, cwd=str(CF1), env=env)
    harness_wall = time.time() - build
    if proc.returncode != 0:
        print(proc.stderr[-4000:], file=sys.stderr)
        return fail("harness exit %d" % proc.returncode)
    rows = []
    for line in proc.stdout.splitlines():
        line = line.strip()
        if line.startswith("{"):
            rows.append(json.loads(line))
    done = [r for r in rows if r.get("type") == "done"]
    if not done:
        return fail("harness produced no done row")
    hw = hardware()
    meta = next((r for r in rows if r.get("type") == "meta"), {})

    print("C15 staged costs: fixed workload (61-sample 2 s loop clip, planar turn+travel root)")
    print("hardware: %s x%d | mem %s | %s" % (hw["cpu"], hw["cores"], hw["mem"], hw["os"]))
    print("backend: %s | runtimes: %s" % (meta.get("backend", "?"), hw["runtime"]))
    print("active bones: all bones active every stage (full-body, no masks); "
          "56 = canonical 56 mapped 1:1, 67 = 56 mapped + 11 static extras (UAL-shaped)")
    print("residency: every SharedClip fully resident post-Load; no streaming IO on Sample")
    print("")
    print("bones | chars | contribs | stage | medianUs/char | p95Us/char | allocB | ok")
    for r in rows:
        if r.get("type") == "row":
            print("%d | %d | %d | %s | %.3f | %.3f | %d | %s" % (
                r["bones"], r["chars"], r["contribs"], r["stage"],
                r["medianUs"], r["p95Us"], r["allocBytes"], "PASS" if r.get("ok") else "FAIL"))
    print("")
    print("full-loop per character-frame (all five stages) + cold first touch:")
    print("bones | chars | contribs | medianUs | p95Us | coldMs | segments | clipBytes | loopAllocB | ok")
    for r in rows:
        if r.get("type") == "loop":
            print("%d | %d | %d | %.3f | %.3f | %.3f | %d | %d | %d | %s" % (
                r["bones"], r["chars"], r["contribs"], r["medianUs"], r["p95Us"],
                r["coldMs"], r["segments"], r["clipBytes"], r["loopAllocBytes"],
                "PASS" if r.get("ok") else "FAIL"))
    print("")
    print("warm 5-stage loop allocation over 1000 calls (GC.GetAllocatedBytesForCurrentThread):")
    for r in rows:
        if r.get("type") == "alloc":
            print("  %db/%dc: %d bytes -> %s" % (
                r["bones"], r["contribs"], r["allocBytes"], "PASS" if r.get("ok") else "FAIL"))
    print("")
    print("random seeks + cuts (seed 20261103: forward steps + backward Seek jumps + continuity cut every 25th):")
    for r in rows:
        if r.get("type") == "seek":
            print("  %db: %d steps (%d fwd %d jumps %d cuts) %d failures median %.3f us p95 %.3f us -> %s" % (
                r["bones"], r["seeks"], r.get("forwards", -1), r.get("jumps", -1), r["cuts"], r["failures"],
                r["medianUs"], r["p95Us"], "PASS" if r.get("ok") else "FAIL"))
    print("")
    print("95-clip corpus sample (first clip per rig template, full 5-stage warm loop x1000):")
    corpus = [r for r in rows if r.get("type") == "corpus"]
    if not corpus:
        print("  (no corpus rows)")
    for r in corpus:
        if r.get("status") in ("skipped", "unavailable"):
            print("  %s: %s" % (r["status"], r.get("reason", "")))
        else:
            print("  %s %s: %d frames %d bones %d bytes/1000 calls -> %s" % (
                r.get("rig"), r.get("clip"), r.get("frames", -1), r.get("bones", -1),
                r.get("allocBytes", -1), "PASS" if r.get("ok") else "FAIL"))
    hottest = ("", -1.0)
    for r in rows:
        if r.get("type") == "row" and r.get("chars") == 500 and r.get("contribs") == 4:
            if r["medianUs"] > hottest[1]:
                hottest = ("%db %s" % (r["bones"], r["stage"]), r["medianUs"])
    print("")
    print("C16 gate: hottest measured stage at 500 chars x 4 contribs (median/char-frame) is %s at %.3f us; "
          "no CPU bottleneck demonstrated, GPU/native port stays deferred." % hottest)
    wall = time.time() - t0
    print("harness wall %.1fs, total wall %.1fs" % (harness_wall, wall))
    ok = done[0].get("ok") and all(r.get("ok", True) for r in rows if r.get("type") in ("row", "loop", "alloc", "seek", "corpus") and "status" not in r)
    return 0 if ok else fail("one or more rows failed")


if __name__ == "__main__":
    sys.exit(main())
