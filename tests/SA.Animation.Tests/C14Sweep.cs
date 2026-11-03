using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SA.Animation;
using SA.Animation.Content;
using SA.Motion.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace SA.Animation.Tests;

public sealed class C14Sweep
{
    const int ExpectedRigs = 3;
    const int ExpectedClips = 95;
    const int ExpectedPairs = 258;
    const float RotationAgreement = 1e-5f;
    const float PositionAgreement = 1e-5f;
    const float DecodePosition = 1e-6f;
    const float DecodeRotation = 1e-6f;
    const float ScaleUnit = 1e-6f;

    static readonly string[] CanonicalNames = new[]
    {
        "Root", "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head", "Jaw",
        "LeftEye", "RightEye", "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand",
        "LeftThumbProximal", "LeftThumbIntermediate", "LeftThumbDistal",
        "LeftIndexProximal", "LeftIndexIntermediate", "LeftIndexDistal",
        "LeftMiddleProximal", "LeftMiddleIntermediate", "LeftMiddleDistal",
        "LeftRingProximal", "LeftRingIntermediate", "LeftRingDistal",
        "LeftLittleProximal", "LeftLittleIntermediate", "LeftLittleDistal",
        "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
        "RightThumbProximal", "RightThumbIntermediate", "RightThumbDistal",
        "RightIndexProximal", "RightIndexIntermediate", "RightIndexDistal",
        "RightMiddleProximal", "RightMiddleIntermediate", "RightMiddleDistal",
        "RightRingProximal", "RightRingIntermediate", "RightRingDistal",
        "RightLittleProximal", "RightLittleIntermediate", "RightLittleDistal",
        "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes",
        "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes"
    };

    readonly ITestOutputHelper output;

    public C14Sweep(ITestOutputHelper output)
    {
        this.output = output;
    }

    sealed class RigData
    {
        public string Rig = "";
        public string File = "";
        public string[] Names = Array.Empty<string>();
        public int[] Parents = Array.Empty<int>();
        public Pose[] Reference = Array.Empty<Pose>();
        public AssetId Family;
        public int Hips;
        public float HipHeight;
        public RetargetRow[] ToCanonical = Array.Empty<RetargetRow>();
        public RetargetBinding ToCanonicalBinding;
        public bool ToCanonicalValid;
        public RetargetRow[] FromCanonical = Array.Empty<RetargetRow>();
        public RetargetBinding FromCanonicalBinding;
        public Affine[] BindAffines = Array.Empty<Affine>();
        public int[] PaletteToRig = Array.Empty<int>();
    }

    sealed class ClipData
    {
        public string Clip = "";
        public string File = "";
        public string Rig = "";
        public Pose[] Samples = Array.Empty<Pose>();
        public RigidPose[] Root = Array.Empty<RigidPose>();
        public int Bones;
        public int SampleCount;
        public double Duration;
        public bool Loop;
        public RootPolicy Policy;
        public SharedClip Encoded = null!;
        public byte[] Bytes = Array.Empty<byte>();
        public AssetId MotionId;
    }

    [Fact]
    public void RigTablesCompileWithHeightRatioFromCalibration()
    {
        var directory = FindDirectory();
        if (directory == null)
        {
            output.WriteLine("C14 awaiting extractor JSONs: Temp/sa/c08-full/_manifest.json not present; synthetic checks carry the gate.");
            return;
        }
        var rigs = LoadRigs(directory);
        Assert.Equal(ExpectedRigs, rigs.Count);
        foreach (var rig in rigs)
        {
            Assert.False(rig.Family.IsEmpty);
            Assert.InRange(rig.HipHeight, 0.3f, 1.8f);
            Assert.True(rig.FromCanonicalBinding.HeightRatio > 0f && float.IsFinite(rig.FromCanonicalBinding.HeightRatio));
            if (rig.Rig == "Robot Kyle")
            {
                Assert.False(rig.ToCanonicalValid);
            }
            else
            {
                Assert.True(rig.ToCanonicalValid);
                var roundTrip = rig.ToCanonicalBinding.HeightRatio * rig.FromCanonicalBinding.HeightRatio;
                Assert.InRange(roundTrip, 0.999f, 1.001f);
            }
            output.WriteLine("C14 rig " + rig.Rig + ": bones " + rig.Names.Length + " hipsBone " + rig.Hips
                + " hipHeight " + rig.HipHeight.ToString("F4") + " toCanonicalValid " + rig.ToCanonicalValid
                + " ratioFromCanonical " + rig.FromCanonicalBinding.HeightRatio.ToString("F4"));
        }
    }

    [Fact]
    public void StreamEveryCompatiblePairThroughBothTables()
    {
        var directory = FindDirectory();
        if (directory == null)
        {
            output.WriteLine("C14 awaiting extractor JSONs: Temp/sa/c08-full/_manifest.json not present; synthetic checks carry the gate.");
            return;
        }
        var timer = Stopwatch.StartNew();
        var rigs = LoadRigs(directory);
        Assert.Equal(ExpectedRigs, rigs.Count);
        var byRig = new Dictionary<string, RigData>(StringComparer.Ordinal);
        foreach (var rig in rigs)
        {
            byRig[rig.Rig] = rig;
        }
        var clips = LoadClips(directory, byRig);
        Assert.Equal(ExpectedClips, clips.Count);
        var failures = new List<string>();
        var pairCount = 0;
        var frameTotal = 0L;
        var byteTotal = 0L;
        var worstAngle = 0f;
        var worstPos = 0f;
        var worstDecodeAngle = 0f;
        var worstDecodePos = 0f;
        var pairWorst = new Dictionary<string, (float Angle, float Pos)>();
        var skippedSources = 0;
        foreach (var target in rigs)
        {
            foreach (var clip in clips)
            {
                var source = byRig[clip.Rig];
                if (!source.ToCanonicalValid)
                {
                    skippedSources++;
                    continue;
                }
                pairCount++;
                byteTotal += clip.Bytes.Length;
                var detail = CheckPair(target, source, clip, ref worstAngle, ref worstPos, ref worstDecodeAngle, ref worstDecodePos, ref frameTotal, out var pairAngle, out var pairPos);
                var key = target.Rig + " x " + clip.Rig;
                if (!pairWorst.TryGetValue(key, out var entry))
                {
                    entry = (0f, 0f);
                }
                pairWorst[key] = (Math.Max(entry.Angle, pairAngle), Math.Max(entry.Pos, pairPos));
                if (detail != null)
                {
                    failures.Add(detail);
                }
            }
        }
        timer.Stop();
        output.WriteLine("C14 sweep: pairs " + pairCount + " skippedInvalidSource " + skippedSources + " frames " + frameTotal + " failures " + failures.Count
            + " worstApplyAngleRad " + worstAngle.ToString("G9") + " worstApplyPosM " + worstPos.ToString("G9")
            + " worstDecodeAngle " + worstDecodeAngle.ToString("G9") + " worstDecodePosM " + worstDecodePos.ToString("G9")
            + " rawBytes " + byteTotal + " wallS " + timer.Elapsed.TotalSeconds.ToString("F1"));
        foreach (var key in new List<string>(pairWorst.Keys))
        {
            var entry = pairWorst[key];
            output.WriteLine("C14 pair " + key + ": worstAngleRad " + entry.Angle.ToString("G9") + " worstPosM " + entry.Pos.ToString("G9"));
        }
        var seen = failures.Count > 8 ? failures.GetRange(0, 8) : failures;
        Assert.True(failures.Count == 0, "C14 sweep failures " + failures.Count + ": " + string.Join(" | ", seen));
        Assert.Equal(ExpectedPairs, pairCount);
        Assert.Equal(27, skippedSources);
    }

    [Fact]
    public void CorpusShortfallStatedPlainly()
    {
        var directory = FindDirectory();
        if (directory == null)
        {
            output.WriteLine("C14 awaiting extractor JSONs: Temp/sa/c08-full/_manifest.json not present; synthetic checks carry the gate.");
            return;
        }
        var rigs = LoadRigs(directory);
        var byRig = new Dictionary<string, RigData>(StringComparer.Ordinal);
        foreach (var rig in rigs)
        {
            byRig[rig.Rig] = rig;
        }
        var clips = LoadClips(directory, byRig);
        var groups = new Dictionary<string, List<string>>();
        foreach (var clip in clips)
        {
            var key = clip.Encoded.Artifact.A.ToString("X16") + clip.Encoded.Artifact.B.ToString("X16");
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<string>();
                groups[key] = list;
            }
            list.Add(clip.Rig + ":" + clip.Clip);
        }
        var duplicateGroups = 0;
        foreach (var entry in groups)
        {
            if (entry.Value.Count > 1)
            {
                duplicateGroups++;
                output.WriteLine("C14 duplicate payload " + entry.Key + ": " + string.Join(", ", entry.Value));
            }
        }
        output.WriteLine("C14 shortfall vs plan 16.1: corpus here is " + rigs.Count + " rig templates ("
            + string.Join(", ", rigs.ConvertAll(r => r.Rig)) + "), not 50 characters; "
            + clips.Count + " motions, not 500; swept pairs " + ExpectedPairs + " (Robot Kyle's 9 clips are targets only: its Ribs is a sibling of its Hip, so no valid rig-to-canonical table exists), not 25000. "
            + "Unique payloads " + groups.Count + " of " + clips.Count + " encoded; duplicate groups " + duplicateGroups + ".");
        Assert.Equal(ExpectedRigs, rigs.Count);
        Assert.Equal(ExpectedClips, clips.Count);
    }

    string? CheckPair(RigData target, RigData source, ClipData clip, ref float worstAngle, ref float worstPos, ref float worstDecodeAngle, ref float worstDecodePos, ref long frameTotal, out float pairAngle, out float pairPos)
    {
        pairAngle = 0f;
        pairPos = 0f;
        var sourceBones = source.Names.Length;
        var targetBones = target.Names.Length;
        var canonical = new Pose[CanonicalNames.Length];
        var canonicalReference = new Pose[CanonicalNames.Length];
        var sourceScratch = new RigidPose[sourceBones + CanonicalNames.Length];
        var targetScratch = new RigidPose[CanonicalNames.Length + targetBones];
        var decoded = new Pose[sourceBones];
        var affineModel = new Affine[targetBones];
        var palette = new Affine[targetBones];
        var canonicalParents = CanonicalParents();
        var canonicalFamily = CanonicalFamily();
        var frames = clip.SampleCount;
        for (var frame = 0; frame < frames; frame++)
        {
            var seconds = frames == 1 ? 0.0 : clip.Duration * frame / (frames - 1);
            var where = "pair " + target.Rig + " x " + clip.Rig + ":" + clip.Clip + " frame " + frame;
            if (clip.Encoded.Sample(seconds, source.Reference, decoded) != AnimationError.None)
            {
                return where + ": decode failed; fix: re-encode " + clip.File + " raw and reload before sweeping.";
            }
            for (var bone = 0; bone < sourceBones; bone++)
            {
                var expectedFrame = clip.Loop && frames > 1 && frame == frames - 1 ? 0 : frame;
                var raw = clip.Samples[expectedFrame * sourceBones + bone];
                var positionGap = VectorGap(raw.Position, decoded[bone].Position);
                var rotationGap = RotationGap(raw.Rotation, decoded[bone].Rotation);
                if (positionGap > worstDecodePos)
                {
                    worstDecodePos = positionGap;
                }
                if (rotationGap > worstDecodeAngle)
                {
                    worstDecodeAngle = rotationGap;
                }
                if (positionGap > DecodePosition || rotationGap > DecodeRotation)
                {
                    return where + " bone " + bone + " (" + source.Names[bone] + "): decoded gap pos "
                        + positionGap.ToString("G9") + " rot " + rotationGap.ToString("G9")
                        + " exceeds raw exactness; fix: keep this clip raw (packed off) or re-extract " + clip.File + ".";
                }
                if (!decoded[bone].Position.IsFinite || !decoded[bone].Rotation.IsValidRotation)
                {
                    return where + " bone " + bone + " (" + source.Names[bone] + "): decoded frame not finite; fix: re-extract " + clip.File + " and reject non-finite tracks at bake.";
                }
            }
            if (RetargetTable.Apply(clip.Encoded.Family, source.ToCanonicalBinding, source.Parents, decoded, source.ToCanonical,
                sourceScratch.AsSpan(0, sourceBones), sourceScratch.AsSpan(sourceBones, CanonicalNames.Length), canonical) != AnimationError.None)
            {
                return where + ": source-to-canonical Apply failed; fix: recompile the " + source.Rig + " table against the canonical reference.";
            }
            if (RetargetTable.ApplyReference(clip.Encoded.Family, source.ToCanonicalBinding, source.Parents, decoded, source.ToCanonical,
                sourceScratch.AsSpan(0, sourceBones), sourceScratch.AsSpan(sourceBones, CanonicalNames.Length), canonicalReference) != AnimationError.None)
            {
                return where + ": source-to-canonical ApplyReference failed; fix: recompile the " + source.Rig + " table against the canonical reference.";
            }
            for (var bone = 0; bone < CanonicalNames.Length; bone++)
            {
                if (!canonical[bone].Position.IsFinite || !canonical[bone].Rotation.IsValidRotation)
                {
                    return where + " canonical bone " + bone + " (" + CanonicalNames[bone] + "): retargeted frame not finite; fix: check the " + source.Rig + " calibration for non-finite frame " + frame + " in " + clip.File + ".";
                }
                var rotationGap = RotationGap(canonical[bone].Rotation, canonicalReference[bone].Rotation);
                var positionGap = VectorGap(canonical[bone].Position, canonicalReference[bone].Position);
                if (rotationGap > worstAngle)
                {
                    worstAngle = rotationGap;
                }
                if (rotationGap > pairAngle)
                {
                    pairAngle = rotationGap;
                }
                if (positionGap > worstPos)
                {
                    worstPos = positionGap;
                }
                if (positionGap > pairPos)
                {
                    pairPos = positionGap;
                }
                if (rotationGap > RotationAgreement || positionGap > PositionAgreement)
                {
                    return where + " canonical bone " + bone + " (" + CanonicalNames[bone] + "): general/fast gap rot "
                        + rotationGap.ToString("G9") + " pos " + positionGap.ToString("G9")
                        + " exceeds 1e-5; fix: repair the FastLocal row pre-rotation for this bone in the compiler.";
                }
            }
            var targetLocal = new Pose[targetBones];
            var targetOracle = new Pose[targetBones];
            if (RetargetTable.Apply(canonicalFamily, target.FromCanonicalBinding, canonicalParents, canonical, target.FromCanonical,
                targetScratch.AsSpan(0, CanonicalNames.Length), targetScratch.AsSpan(CanonicalNames.Length, targetBones), targetLocal) != AnimationError.None)
            {
                return where + ": canonical-to-target Apply failed; fix: recompile the " + target.Rig + " table against the canonical reference.";
            }
            if (RetargetTable.ApplyReference(canonicalFamily, target.FromCanonicalBinding, canonicalParents, canonical, target.FromCanonical,
                targetScratch.AsSpan(0, CanonicalNames.Length), targetScratch.AsSpan(CanonicalNames.Length, targetBones), targetOracle) != AnimationError.None)
            {
                return where + ": canonical-to-target ApplyReference failed; fix: recompile the " + target.Rig + " table against the canonical reference.";
            }
            if (target.FromCanonicalBinding.Family != canonicalFamily)
            {
                return where + ": target table family drifted; fix: recompile the " + target.Rig + " table with the canonical family id.";
            }
            for (var bone = 0; bone < targetBones; bone++)
            {
                if (!targetLocal[bone].Position.IsFinite || !targetLocal[bone].Rotation.IsValidRotation)
                {
                    return where + " target bone " + bone + " (" + target.Names[bone] + "): retargeted frame not finite; fix: check the " + target.Rig + " calibration and frame " + frame + " of " + clip.File + ".";
                }
                var rotationGap = RotationGap(targetLocal[bone].Rotation, targetOracle[bone].Rotation);
                var positionGap = VectorGap(targetLocal[bone].Position, targetOracle[bone].Position);
                if (rotationGap > worstAngle)
                {
                    worstAngle = rotationGap;
                }
                if (rotationGap > pairAngle)
                {
                    pairAngle = rotationGap;
                }
                if (positionGap > worstPos)
                {
                    worstPos = positionGap;
                }
                if (positionGap > pairPos)
                {
                    pairPos = positionGap;
                }
                if (rotationGap > RotationAgreement || positionGap > PositionAgreement)
                {
                    return where + " target bone " + bone + " (" + target.Names[bone] + "): general/fast gap rot "
                        + rotationGap.ToString("G9") + " pos " + positionGap.ToString("G9")
                        + " exceeds 1e-5; fix: repair the FastLocal row pre-rotation for this bone in the compiler.";
                }
                if (bone != target.Hips)
                {
                    var translationGap = VectorGap(targetLocal[bone].Position, target.Reference[bone].Position);
                    if (translationGap != 0f)
                    {
                        return where + " target bone " + bone + " (" + target.Names[bone] + "): non-Hips translation moved by "
                            + translationGap.ToString("G9") + " m; fix: keep translation degrees of freedom disabled in the bake profile.";
                    }
                }
                var scaleGap = ScaleGap(targetLocal[bone].Scale);
                if (scaleGap > ScaleUnit)
                {
                    return where + " target bone " + bone + " (" + target.Names[bone] + "): target scale gap "
                        + scaleGap.ToString("G9") + " exceeds 1e-6; fix: re-extract " + target.File + " with baked unit scales.";
                }
            }
            Hierarchy.Model(target.Parents, targetLocal, affineModel);
            if (SkinPalette.Build(affineModel, target.PaletteToRig, target.BindAffines, Affine.Identity, palette) != AnimationError.None)
            {
                return where + ": SkinPalette.Build rejected the retargeted model; fix: check palette order and bind matrices for " + target.Rig + ".";
            }
            for (var bone = 0; bone < targetBones; bone++)
            {
                if (!Finite(palette[bone]))
                {
                    return where + " palette entry " + bone + " (" + target.Names[bone] + "): palette not finite; fix: check bind matrices for " + target.Rig + ".";
                }
            }
            frameTotal++;
        }
        return null;
    }

    List<RigData> LoadRigs(string directory)
    {
        var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "_manifest.json")));
        var firstByRig = new Dictionary<string, string>(StringComparer.Ordinal);
        var poseByRig = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var entry in manifest.RootElement.GetProperty("clips").EnumerateArray())
        {
            var rig = entry.GetProperty("rig").GetString() ?? "";
            var file = entry.GetProperty("file").GetString() ?? "";
            var clip = entry.GetProperty("clip").GetString() ?? "";
            if (!firstByRig.ContainsKey(rig))
            {
                firstByRig[rig] = file;
                order.Add(rig);
            }
            if (clip.IndexOf("TPose", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                poseByRig[rig] = file;
            }
            else if (clip.IndexOf("Idle", StringComparison.OrdinalIgnoreCase) >= 0 && !poseByRig.ContainsKey(rig))
            {
                poseByRig[rig] = file;
            }
        }
        foreach (var rig in poseByRig.Keys)
        {
            firstByRig[rig] = poseByRig[rig];
        }
        order.Sort(StringComparer.Ordinal);
        var rigs = new List<RigData>();
        foreach (var rig in order)
        {
            rigs.Add(LoadRig(directory, rig, firstByRig[rig]));
        }
        return rigs;
    }

    RigData LoadRig(string directory, string rig, string file)
    {
        var loaded = LoadClipFile(directory, file, "calibration");
        var data = new RigData { Rig = rig, File = file, Names = loaded.Names, Parents = loaded.Parents, Reference = loaded.Reference };
        if (ContentIdentity.Skeleton(rig, 1, data.Names, data.Parents, data.Reference, out var family) != AnimationError.None || family.IsEmpty)
        {
            throw new InvalidOperationException("C14 rig " + rig + ": skeleton hash failed; fix: check bone names and parents in " + file + ".");
        }
        data.Family = family;
        data.Hips = FindHips(data.Names);
        if (data.Hips <= 0)
        {
            throw new InvalidOperationException("C14 rig " + rig + ": no Hips leaf found; fix: extend the leaf map for " + file + ".");
        }
        var models = new RigidPose[data.Names.Length];
        if (RigidMath.Model(data.Parents, data.Reference, models) != AnimationError.None)
        {
            throw new InvalidOperationException("C14 rig " + rig + ": calibration FK failed; fix: re-extract " + file + " with a valid frame 0.");
        }
        data.HipHeight = HipHeightAboveGround(data.Names, models, data.Hips, rig, file);
        var canonicalParents = CanonicalParents();
        var canonicalReference = CanonicalReference();
        var canonicalFamily = CanonicalFamily();
        var sourceMap = new int[CanonicalNames.Length];
        var seen = new HashSet<int>();
        for (var role = 0; role < CanonicalNames.Length; role++)
        {
            sourceMap[role] = -1;
        }
        sourceMap[0] = 0;
        seen.Add(0);
        for (var bone = 1; bone < data.Names.Length; bone++)
        {
            var role = LeafRole(Leaf(data.Names[bone]));
            if (role <= 0)
            {
                continue;
            }
            if (seen.Contains(role))
            {
                throw new InvalidOperationException("C14 rig " + rig + ": bones map twice to role " + role + " (" + CanonicalNames[role] + "); fix: disambiguate leaves in " + file + ".");
            }
            seen.Add(role);
            sourceMap[role] = bone;
        }
        var scratchUp = new RigidPose[data.Names.Length + CanonicalNames.Length];
        var rowsUp = new RetargetRow[CanonicalNames.Length];
        data.ToCanonicalValid = RetargetTable.Compile(data.Family, canonicalFamily, data.Parents, data.Reference, canonicalParents, canonicalReference,
            sourceMap, data.Hips, CanonicalHumanoid.Hips, 1f / data.HipHeight, scratchUp, rowsUp, out var up) == AnimationError.None;
        if (data.ToCanonicalValid)
        {
            data.ToCanonical = rowsUp;
            data.ToCanonicalBinding = up;
        }
        var downMap = new int[data.Names.Length];
        downMap[0] = 0;
        for (var bone = 1; bone < data.Names.Length; bone++)
        {
            downMap[bone] = LeafRole(Leaf(data.Names[bone]));
        }
        var scratchDown = new RigidPose[CanonicalNames.Length + data.Names.Length];
        var rowsDown = new RetargetRow[data.Names.Length];
        if (RetargetTable.Compile(canonicalFamily, data.Family, canonicalParents, canonicalReference, data.Parents, data.Reference,
            downMap, CanonicalHumanoid.Hips, data.Hips, data.HipHeight, scratchDown, rowsDown, out var down) != AnimationError.None)
        {
            throw new InvalidOperationException("C14 rig " + rig + ": canonical-to-target Compile failed; fix: check topology of " + file + " against the canonical parent chain.");
        }
        data.FromCanonical = rowsDown;
        data.FromCanonicalBinding = down;
        data.BindAffines = new Affine[data.Names.Length];
        for (var bone = 0; bone < data.Names.Length; bone++)
        {
            data.BindAffines[bone] = PoseMath.Affine(data.Reference[bone]);
        }
        data.PaletteToRig = new int[data.Names.Length];
        for (var bone = 0; bone < data.Names.Length; bone++)
        {
            data.PaletteToRig[bone] = bone;
        }
        return data;
    }

    List<ClipData> LoadClips(string directory, Dictionary<string, RigData> byRig)
    {
        var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "_manifest.json")));
        var clips = new List<ClipData>();
        foreach (var entry in manifest.RootElement.GetProperty("clips").EnumerateArray())
        {
            var name = entry.GetProperty("clip").GetString() ?? "";
            var file = entry.GetProperty("file").GetString() ?? "";
            var rig = entry.GetProperty("rig").GetString() ?? "";
            if (!byRig.TryGetValue(rig, out var rigData))
            {
                throw new InvalidOperationException("C14 clip " + name + ": unknown rig " + rig + "; fix: add a calibration entry for this rig.");
            }
            var loaded = LoadClipFile(directory, file, name);
            if (!SameSkeleton(rigData.Names, rigData.Parents, loaded.Names, loaded.Parents))
            {
                throw new InvalidOperationException("C14 clip " + name + ": skeleton drift vs " + rigData.File + "; fix: bake all " + rig + " takes from one rig.");
            }
            var clip = new ClipData
            {
                Clip = name,
                File = file,
                Rig = rig,
                Samples = loaded.Samples,
                Root = loaded.Root,
                Bones = loaded.Names.Length,
                SampleCount = loaded.SampleCount,
                Duration = loaded.Duration,
                Loop = loaded.Loop,
                Policy = loaded.Policy
            };
            if (ContentIdentity.Motion(rigData.Family, clip.Samples, clip.Bones, clip.SampleCount, clip.Duration, clip.Loop,
                clip.Policy, clip.Root, rigData.Reference, Array.Empty<float>(), out var motionId) != AnimationError.None || motionId.IsEmpty)
            {
                throw new InvalidOperationException("C14 clip " + name + ": motion hash failed; fix: check frames and root tracks in " + file + ".");
            }
            clip.MotionId = motionId;
            if (SharedClipEncoder.Encode(rigData.Family, rigData.Parents, rigData.Reference, clip.Samples, clip.SampleCount,
                clip.Duration, clip.Loop, clip.Policy, clip.Root, Array.Empty<Pose>(), Array.Empty<float>(),
                Array.Empty<PoseMarker>(), false, CompressionLimits.Default, out var bytes, out _) != AnimationError.None)
            {
                throw new InvalidOperationException("C14 clip " + name + ": raw encode failed; fix: re-extract " + file + " inside CompressionLimits.Default.");
            }
            clip.Bytes = bytes;
            if (SharedClipReader.Load(bytes, rigData.Family, rigData.Reference, out var encoded) != AnimationError.None || encoded == null)
            {
                throw new InvalidOperationException("C14 clip " + name + ": raw reload failed; fix: re-extract " + file + " inside CompressionLimits.Default.");
            }
            clip.Encoded = encoded;
            if (clip.Encoded.Family != rigData.Family || clip.Encoded.Artifact != SacaFile.Digest(bytes))
            {
                throw new InvalidOperationException("C14 clip " + name + ": family or artifact hash mismatch; fix: re-encode " + file + " without renaming the family.");
            }
            if (ContentIdentity.Motion(rigData.Family, clip.Samples, clip.Bones, clip.SampleCount, clip.Duration, clip.Loop,
                clip.Policy, clip.Root, rigData.Reference, Array.Empty<float>(), out var again) != AnimationError.None || again != motionId)
            {
                throw new InvalidOperationException("C14 clip " + name + ": motion hash unstable; fix: check for non-deterministic tracks in " + file + ".");
            }
            clips.Add(clip);
        }
        return clips;
    }

    sealed class LoadedFile
    {
        public string[] Names = Array.Empty<string>();
        public int[] Parents = Array.Empty<int>();
        public Pose[] Reference = Array.Empty<Pose>();
        public Pose[] Samples = Array.Empty<Pose>();
        public RigidPose[] Root = Array.Empty<RigidPose>();
        public int SampleCount;
        public double Duration;
        public bool Loop;
        public RootPolicy Policy;
    }

    LoadedFile LoadClipFile(string directory, string file, string what)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, file)));
        var root = document.RootElement;
        var bonesElement = root.GetProperty("bones");
        var framesElement = root.GetProperty("frames");
        var sampleCount = root.GetProperty("sampleCount").GetInt32();
        var duration = root.GetProperty("duration").GetDouble();
        var loop = root.GetProperty("loop").GetBoolean();
        var bones = bonesElement.GetArrayLength();
        if (bones <= 0 || bones > SampleLocator.MaxBones || sampleCount != framesElement.GetArrayLength())
        {
            throw new InvalidOperationException("C14 " + what + " " + file + ": bad bone or frame count; fix: re-run the extractor into Temp/sa/c08-full.");
        }
        var loaded = new LoadedFile { SampleCount = sampleCount, Duration = duration, Loop = loop };
        loaded.Names = new string[bones];
        loaded.Parents = new int[bones];
        var index = 0;
        foreach (var bone in bonesElement.EnumerateArray())
        {
            loaded.Names[index] = bone.GetProperty("path").GetString() ?? ("b" + index);
            var parent = bone.GetProperty("parent").GetInt32();
            loaded.Parents[index] = index == 0 && parent == 0 ? -1 : parent;
            if (loaded.Parents[index] >= index || (index > 0 && loaded.Parents[index] < 0))
            {
                throw new InvalidOperationException("C14 " + what + " " + file + " bone " + index + " (" + loaded.Names[index] + "): parent order broken; fix: re-run the extractor into Temp/sa/c08-full.");
            }
            index++;
        }
        loaded.Samples = new Pose[bones * sampleCount];
        loaded.Root = new RigidPose[sampleCount];
        index = 0;
        foreach (var frame in framesElement.EnumerateArray())
        {
            var positions = frame.GetProperty("p");
            var rotations = frame.GetProperty("q");
            var scales = frame.GetProperty("s");
            if (positions.GetArrayLength() != bones || rotations.GetArrayLength() != bones || scales.GetArrayLength() != bones)
            {
                throw new InvalidOperationException("C14 " + what + " " + file + " frame " + index + ": track count mismatch; fix: re-run the extractor into Temp/sa/c08-full.");
            }
            for (var bone = 0; bone < bones; bone++)
            {
                var p = positions[bone];
                var q = rotations[bone];
                var s = scales[bone];
                var scaleGap = Math.Max(Math.Abs(s[0].GetSingle() - 1f), Math.Max(Math.Abs(s[1].GetSingle() - 1f), Math.Abs(s[2].GetSingle() - 1f)));
                if (scaleGap > 1e-5f)
                {
                    throw new InvalidOperationException("C14 " + what + " " + file + " frame " + index + " bone " + bone
                        + " (" + loaded.Names[bone] + "): non-unit scale gap " + scaleGap.ToString("G9")
                        + "; fix: re-extract " + file + " with baked unit scales.");
                }
                loaded.Samples[index * bones + bone] = new Pose
                {
                    Position = new Float3(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle()),
                    Rotation = new Quaternion(q[0].GetSingle(), q[1].GetSingle(), q[2].GetSingle(), q[3].GetSingle()),
                    Scale = Float3.One
                };
            }
            var rp = frame.GetProperty("rootP");
            var rq = frame.GetProperty("rootQ");
            loaded.Root[index] = new RigidPose(
                new Float3(rp[0].GetSingle(), rp[1].GetSingle(), rp[2].GetSingle()),
                new Quaternion(rq[0].GetSingle(), rq[1].GetSingle(), rq[2].GetSingle(), rq[3].GetSingle()));
            index++;
        }
        loaded.Reference = new Pose[bones];
        Array.Copy(loaded.Samples, loaded.Reference, bones);
        var policy = RootPolicy.None;
        foreach (var pose in loaded.Root)
        {
            if (RootIsIdentity(pose))
            {
                continue;
            }
            policy = RootIsPlanar(pose) && policy != RootPolicy.FullRigid ? RootPolicy.Planar : RootPolicy.FullRigid;
        }
        loaded.Policy = policy;
        return loaded;
    }

    static bool SameSkeleton(string[] aNames, int[] aParents, string[] bNames, int[] bParents)
    {
        if (aNames.Length != bNames.Length)
        {
            return false;
        }
        for (var i = 0; i < aNames.Length; i++)
        {
            if (!string.Equals(aNames[i], bNames[i], StringComparison.Ordinal) || aParents[i] != bParents[i])
            {
                return false;
            }
        }
        return true;
    }

    static int FindHips(string[] names)
    {
        for (var i = 1; i < names.Length; i++)
        {
            if (LeafRole(Leaf(names[i])) == CanonicalHumanoid.Hips)
            {
                return i;
            }
        }
        return -1;
    }
    static float HipHeightAboveGround(string[] names, RigidPose[] models, int hips, string rig, string file)
    {
        var ankle = float.MaxValue;
        for (var bone = 1; bone < names.Length; bone++)
        {
            var role = LeafRole(Leaf(names[bone]));
            if (role != 50 && role != 54)
            {
                continue;
            }
            if (models[bone].Position.Y < ankle)
            {
                ankle = models[bone].Position.Y;
            }
        }
        if (ankle == float.MaxValue)
        {
            throw new InvalidOperationException("C14 rig " + rig + ": no foot leaves mapped; fix: extend the leaf map for " + file + ".");
        }
        return (models[hips].Position.Y - ankle) / 0.9f;
    }

    static string Leaf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash >= 0 ? path.Substring(slash + 1) : path;
    }

    static int LeafRole(string leaf)
    {
        switch (leaf.ToLowerInvariant())
        {
            case "pelvis":
            case "hip": return 1;
            case "spine_01":
            case "ribs": return 2;
            case "spine_02": return 3;
            case "spine_03": return 4;
            case "neck_01":
            case "neck": return 5;
            case "head": return 6;
            case "clavicle_l":
            case "left_shoulder_joint_01": return 10;
            case "upperarm_l":
            case "left_upper_arm_joint_01": return 11;
            case "lowerarm_l":
            case "left_forearm_joint_01": return 12;
            case "hand_l":
            case "left_wrist_joint_01": return 13;
            case "thumb_01_l":
            case "left_thumb_joint_01a": return 14;
            case "thumb_02_l":
            case "left_thumb_joint_01b": return 15;
            case "thumb_03_l": return 16;
            case "index_01_l":
            case "left_index_finger_joint_01a": return 17;
            case "index_02_l":
            case "left_index_finger_joint_01b": return 18;
            case "index_03_l":
            case "left_index_finger_joint_01c": return 19;
            case "middle_01_l":
            case "left_middle_finger_joint_01a": return 20;
            case "middle_02_l":
            case "left_middle_finger_joint_01b": return 21;
            case "middle_03_l":
            case "left_middle_finger_joint_01c": return 22;
            case "ring_01_l":
            case "left_ring_finger_joint_01a": return 23;
            case "ring_02_l":
            case "left_ring_finger_joint_01b": return 24;
            case "ring_03_l":
            case "left_ring_finger_joint_01c": return 25;
            case "pinky_01_l":
            case "left_pinky_finger_joint_01a": return 26;
            case "pinky_02_l":
            case "left_pinky_finger_joint_01b": return 27;
            case "pinky_03_l":
            case "left_pinky_finger_joint_01c": return 28;
            case "clavicle_r":
            case "right_shoulder_joint_01": return 29;
            case "upperarm_r":
            case "right_upper_arm_joint_01": return 30;
            case "lowerarm_r":
            case "right_forearm_joint_01": return 31;
            case "hand_r":
            case "right_wrist_joint_01": return 32;
            case "thumb_01_r":
            case "right_thumb_joint_01a": return 33;
            case "thumb_02_r":
            case "right_thumb_joint_01b": return 34;
            case "thumb_03_r": return 35;
            case "index_01_r":
            case "right_index_finger_joint_01a": return 36;
            case "index_02_r":
            case "right_index_finger_joint_01b": return 37;
            case "index_03_r":
            case "right_index_finger_joint_01c": return 38;
            case "middle_01_r":
            case "right_middle_finger_joint_01a": return 39;
            case "middle_02_r":
            case "right_middle_finger_joint_01b": return 40;
            case "middle_03_r":
            case "right_middle_finger_joint_01c": return 41;
            case "ring_01_r":
            case "right_ring_finger_joint_01a": return 42;
            case "ring_02_r":
            case "right_ring_finger_joint_01b": return 43;
            case "ring_03_r":
            case "right_ring_finger_joint_01c": return 44;
            case "pinky_01_r":
            case "right_pinky_finger_joint_01a": return 45;
            case "pinky_02_r":
            case "right_pinky_finger_joint_01b": return 46;
            case "pinky_03_r":
            case "right_pinky_finger_joint_01c": return 47;
            case "thigh_l":
            case "left_thigh_joint_01": return 48;
            case "calf_l":
            case "left_knee_joint_01": return 49;
            case "foot_l":
            case "left_ankle_joint_01": return 50;
            case "ball_l":
            case "left_toe_joint_01": return 51;
            case "thigh_r":
            case "right_thigh_joint_01": return 52;
            case "calf_r":
            case "right_knee_joint_01": return 53;
            case "foot_r":
            case "right_ankle_joint_01": return 54;
            case "ball_r":
            case "right_toe_joint_01": return 55;
            default: return -1;
        }
    }

    static int[] CanonicalParents()
    {
        var parents = new int[CanonicalNames.Length];
        var reference = new Pose[CanonicalNames.Length];
        if (CanonicalHumanoid.WriteReference(parents, reference) != AnimationError.None)
        {
            throw new InvalidOperationException("C14: canonical reference failed; fix: repair CanonicalHumanoid.WriteReference.");
        }
        return parents;
    }

    static Pose[] CanonicalReference()
    {
        var parents = new int[CanonicalNames.Length];
        var reference = new Pose[CanonicalNames.Length];
        if (CanonicalHumanoid.WriteReference(parents, reference) != AnimationError.None)
        {
            throw new InvalidOperationException("C14: canonical reference failed; fix: repair CanonicalHumanoid.WriteReference.");
        }
        return reference;
    }

    static AssetId CanonicalFamily()
    {
        if (ContentIdentity.Skeleton("Humanoid", 1, CanonicalNames, CanonicalParents(), CanonicalReference(), out var family) != AnimationError.None || family.IsEmpty)
        {
            throw new InvalidOperationException("C14: canonical family hash failed; fix: check the 56 canonical names and parents.");
        }
        return family;
    }

    static string? FindDirectory()
    {
        var scan = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && scan != null; i++)
        {
            var candidate = Path.Combine(scan, "Temp", "sa", "c08-full", "_manifest.json");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate);
            }
            scan = Path.GetDirectoryName(scan);
        }
        const string absolute = "/home/i/ruk2/Temp/sa/c08-full/_manifest.json";
        if (File.Exists(absolute))
        {
            return Path.GetDirectoryName(absolute);
        }
        return null;
    }

    static float VectorGap(Float3 a, Float3 b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Max(Math.Abs(a.Y - b.Y), Math.Abs(a.Z - b.Z)));

    static float RotationGap(Quaternion a, Quaternion b)
    {
        var sign = Quaternion.Dot(a, b) < 0f ? -1f : 1f;
        return Math.Max(Math.Max(Math.Abs(a.X - sign * b.X), Math.Abs(a.Y - sign * b.Y)), Math.Max(Math.Abs(a.Z - sign * b.Z), Math.Abs(a.W - sign * b.W)));
    }

    static float ScaleGap(Float3 scale) =>
        Math.Max(Math.Abs(scale.X - 1f), Math.Max(Math.Abs(scale.Y - 1f), Math.Abs(scale.Z - 1f)));

    static bool Finite(in Affine value) =>
        value.C0.IsFinite && value.C1.IsFinite && value.C2.IsFinite && value.C3.IsFinite;

    static bool RootIsIdentity(in RigidPose pose) =>
        pose.Position.LengthSquared <= 1e-12f && Math.Abs(pose.Rotation.W) >= 0.999999f;

    static bool RootIsPlanar(in RigidPose pose) =>
        Math.Abs(pose.Position.Y) <= 1e-6f && Math.Abs(pose.Rotation.X) <= 1e-6f && Math.Abs(pose.Rotation.Z) <= 1e-6f;
}
