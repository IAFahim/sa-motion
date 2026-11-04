using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using SA.Animation;
using SA.Animation.Content;
using SA.Motion.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace SA.PilotSweep.Tests
{
    public sealed class RealCorpusSweep
    {
        const string CorpusDir = "/home/os/TestingAssets/Temp/sa/c08";
        const int ExpectedRigs = 8;
        const int ExpectedClips = 8;
        const int ExpectedPairs = 64;
        const int ExpectedFrames = 3176;
        const long BudgetBytes = 67108864;
        const float RotationAgreement = 1e-5f;
        const float PositionAgreement = 1e-5f;
        const float DecodePosition = 1e-6f;
        const float DecodeRotation = 1e-6f;
        const float ScaleUnit = 1e-6f;
        const int PoseBytes = 40;
        const int RigidPoseBytes = 28;
        const int AffineBytes = 64;

        static readonly string[] CanonicalNames = new string[]
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

        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        readonly ITestOutputHelper output;

        public RealCorpusSweep(ITestOutputHelper output)
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
            public bool ToCanonicalValid = true;
            public string ToCanonicalError = "";
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

        sealed class SweepResult
        {
            public int Compatible;
            public int Incompatible;
            public List<string> IncompatibleNames = new List<string>();
            public long Frames;
            public long ExpectedFrames;
            public long Failures;
            public long PeakResident;
            public long EncodedBytes;
            public long CorpusJsonBytes;
            public float WorstAngle;
            public float WorstPos;
            public float WorstDecodeAngle;
            public float WorstDecodePos;
            public long ValidPass;
            public long FinitePass;
            public long ParityPass;
            public long TranslationPass;
            public long PalettePass;
            public long DecodePass;
            public List<string> FailureNotes = new List<string>();
            public List<string> Matrix = new List<string>();
            public List<string> RigLines = new List<string>();
        }

        [Fact]
        public void RealCorpusInventory()
        {
            if (!File.Exists(Path.Combine(CorpusDir, "_manifest.json")))
            {
                output.WriteLine("real corpus awaiting extractor JSONs: " + CorpusDir + "/_manifest.json not present.");
                return;
            }
            var rigs = LoadRigs(CorpusDir);
            var byRig = new Dictionary<string, RigData>(StringComparer.Ordinal);
            foreach (var rig in rigs)
            {
                byRig[rig.Rig] = rig;
            }
            var clips = LoadClips(CorpusDir, byRig, false);
            Assert.Equal(ExpectedRigs, rigs.Count);
            Assert.Equal(ExpectedClips, clips.Count);
            foreach (var rig in rigs)
            {
                Assert.True(rig.ToCanonicalValid);
                Assert.True(rig.HipHeight > 0f && float.IsFinite(rig.HipHeight));
                output.WriteLine("real rig " + rig.Rig + ": bones " + rig.Names.Length
                    + " hipsBone " + rig.Hips + " hipHeight " + rig.HipHeight.ToString("F4", Invariant)
                    + " ratio " + rig.FromCanonicalBinding.HeightRatio.ToString("F4", Invariant));
            }
            var expectedSamples = new Dictionary<string, int>(StringComparer.Ordinal);
            expectedSamples["Dual_Blade@Idle|Idle"] = 41;
            expectedSamples["Dual_Blade@Walk_Fwd|Walk_Fwd"] = 33;
            expectedSamples["Dual_Blade@Walk_Fwd_Root|Walk_Fwd_Root"] = 33;
            expectedSamples["Dual_Blade@Run|Run"] = 17;
            expectedSamples["Dual_Blade@Run_Root|Run_Root"] = 17;
            expectedSamples["Stabbing|Scene"] = 73;
            expectedSamples["Stable Sword Outward Slash|Scene"] = 64;
            expectedSamples["Frisbee Throw|Scene"] = 102;
            foreach (var clip in clips)
            {
                Assert.True(clip.Loop);
                Assert.Equal(expectedSamples[clip.Rig + "|" + clip.Clip], clip.SampleCount);
                var travel = RootTravel(clip.Root);
                var expected = ExpectedTravel(clip.Rig, clip.Clip);
                if (expected >= 0)
                {
                    Assert.InRange(travel, expected - 0.01, expected + 0.01);
                }
                else
                {
                    Assert.InRange(travel, 0, 0.01);
                }
                output.WriteLine("real clip " + clip.Rig + ":" + clip.Clip + ": bones " + clip.Bones
                    + " samples " + clip.SampleCount + " duration " + clip.Duration.ToString("F4", Invariant)
                    + " travel " + travel.ToString("F4", Invariant));
            }
        }

        [Fact]
        public void StreamedSweepRealCorpus()
        {
            if (!File.Exists(Path.Combine(CorpusDir, "_manifest.json")))
            {
                output.WriteLine("real corpus awaiting extractor JSONs: " + CorpusDir + "/_manifest.json not present.");
                return;
            }
            var result = Sweep(CorpusDir);
            output.WriteLine("real sweep: pairs " + result.Compatible + " incompatible " + result.Incompatible
                + " frames " + result.Frames + " failures " + result.Failures
                + " worstApplyAngle " + result.WorstAngle.ToString("G9", Invariant)
                + " worstApplyPos " + result.WorstPos.ToString("G9", Invariant)
                + " worstDecodeAngle " + result.WorstDecodeAngle.ToString("G9", Invariant)
                + " worstDecodePos " + result.WorstDecodePos.ToString("G9", Invariant)
                + " encodedBytes " + result.EncodedBytes + " corpusJsonBytes " + result.CorpusJsonBytes
                + " peakResident " + result.PeakResident + " budget " + BudgetBytes);
            foreach (var line in result.RigLines)
            {
                output.WriteLine(line);
            }
            foreach (var line in result.Matrix)
            {
                output.WriteLine(line);
            }
            var shown = result.FailureNotes.Count > 8 ? 8 : result.FailureNotes.Count;
            for (var i = 0; i < shown; i++)
            {
                output.WriteLine(result.FailureNotes[i]);
            }
            WriteReport(result);
            Assert.Equal(ExpectedPairs, result.Compatible);
            Assert.Equal(0, result.Incompatible);
            Assert.Equal(result.ExpectedFrames, result.Frames);
            Assert.Equal((long)ExpectedFrames, result.ExpectedFrames);
            Assert.True(result.PeakResident < BudgetBytes);
            Assert.True(result.Failures == 0);
        }

        [Fact]
        public void RealCorpusShortfall()
        {
            if (!File.Exists(Path.Combine(CorpusDir, "_manifest.json")))
            {
                output.WriteLine("real corpus awaiting extractor JSONs: " + CorpusDir + "/_manifest.json not present.");
                return;
            }
            var result = Sweep(CorpusDir);
            var families = new HashSet<string>();
            var rigs = LoadRigs(CorpusDir);
            foreach (var rig in rigs)
            {
                families.Add(rig.Family.A.ToString("X16", Invariant) + rig.Family.B.ToString("X16", Invariant)
                    + rig.Family.C.ToString("X16", Invariant) + rig.Family.D.ToString("X16", Invariant));
            }
            output.WriteLine("real shortfall vs plan 16.1: rigs " + rigs.Count + " not 50 (distinct families "
                + families.Count + "); clips 8 not 500; swept pairs " + result.Compatible
                + " not 25000; encoded " + result.EncodedBytes + " bytes resident peak " + result.PeakResident + ".");
            Assert.Equal(ExpectedRigs, rigs.Count);
            Assert.Equal(ExpectedPairs, result.Compatible);
        }

        SweepResult Sweep(string directory)
        {
            var result = new SweepResult();
            var rigs = LoadRigs(directory);
            var byRig = new Dictionary<string, RigData>(StringComparer.Ordinal);
            foreach (var rig in rigs)
            {
                byRig[rig.Rig] = rig;
                result.RigLines.Add("real rig " + rig.Rig + ": bones " + rig.Names.Length
                    + " family " + rig.Family.A.ToString("X16", Invariant)
                    + " valid " + (rig.ToCanonicalValid ? "yes" : "no " + rig.ToCanonicalError));
            }
            var clips = LoadClips(directory, byRig, true);
            foreach (var clip in clips)
            {
                result.EncodedBytes += clip.Bytes.Length;
                result.CorpusJsonBytes += new FileInfo(Path.Combine(directory, clip.File)).Length;
                result.ExpectedFrames += clip.SampleCount + clip.Encoded.Segments.Length - 1;
            }
            result.ExpectedFrames *= rigs.Count;
            var maxBones = 0;
            foreach (var rig in rigs)
            {
                if (rig.Names.Length > maxBones)
                {
                    maxBones = rig.Names.Length;
                }
            }
            var decoded = new Pose[maxBones];
            var local = new Pose[maxBones];
            var oracle = new Pose[maxBones];
            var canonicalModel = new RigidPose[maxBones];
            var targetModel = new RigidPose[maxBones];
            var oracleModel = new RigidPose[maxBones];
            var affineModel = new Affine[maxBones];
            var palette = new Affine[maxBones];
            foreach (var target in rigs)
            {
                foreach (var clip in clips)
                {
                    var source = byRig[clip.Rig];
                    if (!source.ToCanonicalValid)
                    {
                        result.Incompatible++;
                        result.IncompatibleNames.Add(target.Rig + " x " + clip.Rig + ":" + clip.Clip);
                        result.Matrix.Add("real pair " + target.Rig + " x " + clip.Rig + ":" + clip.Clip + ": incompatible source");
                        continue;
                    }
                    result.Compatible++;
                    var detail = CheckPair(target, source, clip, decoded, local, oracle,
                        canonicalModel, targetModel, oracleModel, affineModel, palette, result);
                    if (detail != null)
                    {
                        result.Failures++;
                        if (result.FailureNotes.Count < 16)
                        {
                            result.FailureNotes.Add(detail);
                        }
                    }
                    else
                    {
                        result.Matrix.Add("real pair " + target.Rig + " x " + clip.Rig + ":" + clip.Clip + ": swept " + clip.SampleCount + " frames");
                    }
                }
            }
            return result;
        }

        string? CheckPair(RigData target, RigData source, ClipData clip,
            Pose[] decoded, Pose[] local, Pose[] oracle,
            RigidPose[] canonicalModel, RigidPose[] targetModel, RigidPose[] oracleModel,
            Affine[] affineModel, Affine[] palette, SweepResult result)
        {
            var sourceBones = source.Names.Length;
            var targetBones = target.Names.Length;
            var frames = clip.SampleCount;
            var bind = new Affine[targetBones];
            var map = new int[targetBones];
            for (var i = 0; i < targetBones; i++)
            {
                map[i] = i;
                bind[i] = PoseMath.Affine(target.Reference[i]);
            }
            var scratchBytes = (long)(sourceBones + 2 * targetBones) * PoseBytes
                + (long)(sourceBones + 2 * targetBones) * RigidPoseBytes
                + (long)(2 * targetBones) * AffineBytes
                + (long)targetBones * 4;
            var rootResident = (long)clip.Root.Length * RigidPoseBytes;
            var trackMeta = (long)clip.Encoded.Tracks.Length * 32;
            var segmentTable = (long)clip.Encoded.Segments.Length * 32;
            var uploadCopy = (long)targetBones * AffineBytes;
            if (clip.Encoded.Family != source.Family || clip.Encoded.Family != source.ToCanonicalBinding.Family)
            {
                return "pair " + target.Rig + " x " + clip.Rig + ":" + clip.Clip + ": family agreement failed";
            }
            result.ValidPass++;
            if (SacaFile.Digest(clip.Bytes) != clip.Encoded.Artifact)
            {
                return "pair " + target.Rig + " x " + clip.Rig + ":" + clip.Clip + ": artifact digest drifted";
            }
            for (var segment = 0; segment < clip.Encoded.Segments.Length; segment++)
            {
                var page = clip.Encoded.Segments[segment];
                var resident = rootResident + trackMeta + segmentTable + page.Bytes.Length + scratchBytes + uploadCopy;
                if (resident > result.PeakResident)
                {
                    result.PeakResident = resident;
                }
                var last = page.FirstSample + page.SampleCount;
                for (var frame = page.FirstSample; frame < last; frame++)
                {
                    var seconds = frames == 1 ? 0.0 : clip.Duration * frame / (frames - 1);
                    var where = "pair " + target.Rig + " x " + clip.Rig + ":" + clip.Clip + " frame " + frame;
                    var slice = new Span<Pose>(decoded, 0, sourceBones);
                    if (clip.Encoded.Sample(seconds, source.Reference, slice) != AnimationError.None)
                    {
                        return where + ": decode failed";
                    }
                    for (var bone = 0; bone < sourceBones; bone++)
                    {
                        var expectedFrame = clip.Loop && frames > 1 && frame == frames - 1 ? 0 : frame;
                        var raw = clip.Samples[expectedFrame * sourceBones + bone];
                        var positionGap = VectorGap(raw.Position, decoded[bone].Position);
                        var rotationGap = RotationGap(raw.Rotation, decoded[bone].Rotation);
                        if (positionGap > result.WorstDecodePos)
                        {
                            result.WorstDecodePos = positionGap;
                        }
                        if (rotationGap > result.WorstDecodeAngle)
                        {
                            result.WorstDecodeAngle = rotationGap;
                        }
                        if (positionGap > DecodePosition || rotationGap > DecodeRotation)
                        {
                            return where + " bone " + bone + ": raw decode gap pos "
                                + positionGap.ToString("G9", Invariant) + " rot " + rotationGap.ToString("G9", Invariant);
                        }
                        if (!decoded[bone].Position.IsFinite || !decoded[bone].Rotation.IsValidRotation)
                        {
                            return where + " bone " + bone + ": decoded not finite";
                        }
                    }
                    result.DecodePass++;
                    result.FinitePass++;
                    var canonicalSlice = new Span<Pose>(decoded, 0, sourceBones);
                    var canonicalModels = new Span<RigidPose>(canonicalModel, 0, sourceBones);
                    var targetLocals = new Span<Pose>(local, 0, targetBones);
                    var targetModels = new Span<RigidPose>(targetModel, 0, targetBones);
                    var oracleLocals = new Span<Pose>(oracle, 0, targetBones);
                    var oracleModels = new Span<RigidPose>(oracleModel, 0, targetBones);
                    if (RetargetTable.Apply(clip.Encoded.Family, source.ToCanonicalBinding, source.Parents, canonicalSlice, source.ToCanonical,
                        canonicalModels, targetModels, targetLocals) != AnimationError.None)
                    {
                        return where + ": source-to-canonical Apply failed";
                    }
                    var canonicalOut = new Pose[CanonicalNames.Length];
                    for (var bone = 0; bone < CanonicalNames.Length; bone++)
                    {
                        canonicalOut[bone] = targetLocals[bone];
                    }
                    var affines = new Span<Affine>(affineModel, 0, targetBones);
                    var palettes = new Span<Affine>(palette, 0, targetBones);
                    var canonicalParents = CanonicalParents();
                    if (RetargetTable.Apply(target.FromCanonicalBinding.Family, target.FromCanonicalBinding, canonicalParents, canonicalOut, target.FromCanonical,
                        canonicalModels, targetModels, targetLocals) != AnimationError.None)
                    {
                        return where + ": canonical-to-target Apply failed";
                    }
                    if (RetargetTable.ApplyReference(target.FromCanonicalBinding.Family, target.FromCanonicalBinding, canonicalParents, canonicalOut, target.FromCanonical,
                        canonicalModels, oracleModels, oracleLocals) != AnimationError.None)
                    {
                        return where + ": canonical-to-target ApplyReference failed";
                    }
                    for (var bone = 0; bone < targetBones; bone++)
                    {
                        if (!targetLocals[bone].Position.IsFinite || !targetLocals[bone].Rotation.IsValidRotation)
                        {
                            return where + " target bone " + bone + ": retarget not finite";
                        }
                        var rotationGap = RotationGap(targetLocals[bone].Rotation, oracleLocals[bone].Rotation);
                        var positionGap = VectorGap(targetLocals[bone].Position, oracleLocals[bone].Position);
                        if (rotationGap > result.WorstAngle)
                        {
                            result.WorstAngle = rotationGap;
                        }
                        if (positionGap > result.WorstPos)
                        {
                            result.WorstPos = positionGap;
                        }
                        if (rotationGap > RotationAgreement || positionGap > PositionAgreement)
                        {
                            return where + " target bone " + bone + ": reference/fast gap rot "
                                + rotationGap.ToString("G9", Invariant) + " pos " + positionGap.ToString("G9", Invariant);
                        }
                        if (bone != target.Hips)
                        {
                            var drift = VectorGap(targetLocals[bone].Position, target.Reference[bone].Position);
                            if (drift != 0f)
                            {
                                return where + " target bone " + bone + ": non-Hips moved " + drift.ToString("G9", Invariant);
                            }
                        }
                        if (ScaleGap(targetLocals[bone].Scale) > ScaleUnit)
                        {
                            return where + " target bone " + bone + ": scale gap";
                        }
                    }
                    result.ParityPass++;
                    result.TranslationPass++;
                    Hierarchy.Model(target.Parents, targetLocals, affines);
                    if (SkinPalette.Build(affines, map, bind, Affine.Identity, palettes) != AnimationError.None)
                    {
                        return where + ": palette build rejected";
                    }
                    for (var bone = 0; bone < targetBones; bone++)
                    {
                        if (!Finite(palettes[bone]))
                        {
                            return where + " palette " + bone + ": not finite";
                        }
                    }
                    result.PalettePass++;
                    result.Frames++;
                }
            }
            return null;
        }

        List<RigData> LoadRigs(string directory)
        {
            var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "_manifest.json")));
            var firstByRig = new Dictionary<string, string>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var entry in manifest.RootElement.GetProperty("clips").EnumerateArray())
            {
                var rig = entry.GetProperty("rig").GetString() ?? "";
                var file = entry.GetProperty("file").GetString() ?? "";
                if (!firstByRig.ContainsKey(rig))
                {
                    firstByRig[rig] = file;
                    order.Add(rig);
                }
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
                throw new InvalidOperationException("real rig " + rig + ": skeleton hash failed in " + file);
            }
            data.Family = family;
            data.Hips = FindHips(data.Names);
            if (data.Hips <= 0)
            {
                throw new InvalidOperationException("real rig " + rig + ": no Hips role in " + file);
            }
            var models = new RigidPose[data.Names.Length];
            if (RigidMath.Model(data.Parents, data.Reference, models) != AnimationError.None)
            {
                throw new InvalidOperationException("real rig " + rig + ": calibration FK failed in " + file);
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
                    throw new InvalidOperationException("real rig " + rig + ": bones map twice to role " + role + " in " + file);
                }
                seen.Add(role);
                sourceMap[role] = bone;
            }
            var scratchUp = new RigidPose[data.Names.Length + CanonicalNames.Length];
            var rowsUp = new RetargetRow[CanonicalNames.Length];
            if (RetargetTable.Compile(data.Family, canonicalFamily, data.Parents, data.Reference, canonicalParents, canonicalReference,
                sourceMap, data.Hips, CanonicalHumanoid.Hips, 1f / data.HipHeight, scratchUp, rowsUp, out var up) == AnimationError.None)
            {
                data.ToCanonical = rowsUp;
                data.ToCanonicalBinding = up;
                data.ToCanonicalValid = true;
            }
            else
            {
                data.ToCanonicalValid = false;
                data.ToCanonicalError = "source-to-canonical Compile rejected";
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
                throw new InvalidOperationException("real rig " + rig + ": canonical-to-target Compile failed in " + file);
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

        List<ClipData> LoadClips(string directory, Dictionary<string, RigData> byRig, bool encode)
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
                    throw new InvalidOperationException("real clip " + name + ": unknown rig " + rig);
                }
                var loaded = LoadClipFile(directory, file, name);
                if (!SameSkeleton(rigData.Names, rigData.Parents, loaded.Names, loaded.Parents))
                {
                    throw new InvalidOperationException("real clip " + name + ": skeleton drift vs " + rigData.File);
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
                if (encode)
                {
                    if (ContentIdentity.Motion(rigData.Family, clip.Samples, clip.Bones, clip.SampleCount, clip.Duration, clip.Loop,
                        clip.Policy, clip.Root, rigData.Reference, Array.Empty<float>(), out var motionId) != AnimationError.None || motionId.IsEmpty)
                    {
                        throw new InvalidOperationException("real clip " + name + ": motion hash failed in " + file);
                    }
                    clip.MotionId = motionId;
                    if (SharedClipEncoder.Encode(rigData.Family, rigData.Parents, rigData.Reference, clip.Samples, clip.SampleCount,
                        clip.Duration, clip.Loop, clip.Policy, clip.Root, Array.Empty<Pose>(), Array.Empty<float>(),
                        Array.Empty<PoseMarker>(), false, CompressionLimits.Default, out var bytes, out _) != AnimationError.None)
                    {
                        throw new InvalidOperationException("real clip " + name + ": raw encode failed in " + file);
                    }
                    clip.Bytes = bytes;
                    if (SharedClipReader.Load(bytes, rigData.Family, rigData.Reference, out var encoded) != AnimationError.None || encoded == null)
                    {
                        throw new InvalidOperationException("real clip " + name + ": raw reload failed in " + file);
                    }
                    clip.Encoded = encoded;
                    if (clip.Encoded.Family != rigData.Family || clip.Encoded.Artifact != SacaFile.Digest(bytes))
                    {
                        throw new InvalidOperationException("real clip " + name + ": family or artifact mismatch in " + file);
                    }
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
                throw new InvalidOperationException("real " + what + " " + file + ": bad bone or frame count");
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
                    throw new InvalidOperationException("real " + what + " " + file + " bone " + index + ": parent order broken");
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
                    throw new InvalidOperationException("real " + what + " " + file + " frame " + index + ": track count mismatch");
                }
                for (var bone = 0; bone < bones; bone++)
                {
                    var p = positions[bone];
                    var q = rotations[bone];
                    var s = scales[bone];
                    var scaleGap = Math.Max(Math.Abs(s[0].GetSingle() - 1f), Math.Max(Math.Abs(s[1].GetSingle() - 1f), Math.Abs(s[2].GetSingle() - 1f)));
                    if (scaleGap > 1e-5f)
                    {
                        throw new InvalidOperationException("real " + what + " " + file + " frame " + index + " bone " + bone + ": non-unit scale");
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

        void WriteReport(SweepResult result)
        {
            var root = FindRepoRoot();
            if (root == null)
            {
                output.WriteLine("real report: repo root not found");
                return;
            }
            var builder = new StringBuilder(2048);
            builder.Append("{\"schema\":1,\"corpus\":\"c08\",\"compatible\":").Append(result.Compatible);
            builder.Append(",\"incompatible\":").Append(result.Incompatible);
            builder.Append(",\"frames\":").Append(result.Frames);
            builder.Append(",\"failures\":").Append(result.Failures);
            builder.Append(",\"peakResident\":").Append(result.PeakResident);
            builder.Append(",\"budget\":").Append(BudgetBytes);
            builder.Append(",\"encodedBytes\":").Append(result.EncodedBytes);
            builder.Append(",\"corpusJsonBytes\":").Append(result.CorpusJsonBytes);
            builder.Append(",\"worstAngle\":").Append(result.WorstAngle.ToString("G9", Invariant));
            builder.Append(",\"worstPos\":").Append(result.WorstPos.ToString("G9", Invariant));
            builder.Append(",\"worstDecodeAngle\":").Append(result.WorstDecodeAngle.ToString("G9", Invariant));
            builder.Append(",\"worstDecodePos\":").Append(result.WorstDecodePos.ToString("G9", Invariant));
            builder.Append(",\"plan\":{\"rigs\":50,\"clips\":500,\"pairs\":25000}");
            builder.Append(",\"checks\":{\"valid\":").Append(result.ValidPass);
            builder.Append(",\"finite\":").Append(result.FinitePass);
            builder.Append(",\"parity\":").Append(result.ParityPass);
            builder.Append(",\"translation\":").Append(result.TranslationPass);
            builder.Append(",\"palette\":").Append(result.PalettePass);
            builder.Append(",\"decode\":").Append(result.DecodePass).Append("}");
            builder.Append(",\"ok\":").Append(result.Failures == 0 ? "true" : "false").Append("}");
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "artifacts"));
                File.WriteAllText(Path.Combine(root, "artifacts", "real-corpus-sweep-report.json"), builder.ToString());
                output.WriteLine("real report: artifacts/real-corpus-sweep-report.json");
            }
            catch (Exception ex)
            {
                output.WriteLine("real report unwritten: " + ex.GetType().Name);
            }
        }

        static double ExpectedTravel(string rig, string clip)
        {
            if (rig == "Dual_Blade@Idle" && clip == "Idle")
            {
                return 0;
            }
            if (rig == "Dual_Blade@Walk_Fwd" && clip == "Walk_Fwd")
            {
                return 0;
            }
            if (rig == "Dual_Blade@Walk_Fwd_Root" && clip == "Walk_Fwd_Root")
            {
                return 1.8717;
            }
            if (rig == "Dual_Blade@Run" && clip == "Run")
            {
                return 0;
            }
            if (rig == "Dual_Blade@Run_Root" && clip == "Run_Root")
            {
                return 2.853;
            }
            return -1;
        }

        static double RootTravel(RigidPose[] root)
        {
            if (root.Length == 0)
            {
                return 0;
            }
            var first = root[0].Position;
            var last = root[root.Length - 1].Position;
            var dx = last.X - first.X;
            var dz = last.Z - first.Z;
            return Math.Sqrt(dx * dx + dz * dz);
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
                throw new InvalidOperationException("real rig " + rig + ": no foot leaves in " + file);
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
                case "bip01 pelvis": return 1;
                case "bip01 spine": return 2;
                case "bip01 spine1": return 3;
                case "bip01 spine2": return 4;
                case "bip01 neck": return 5;
                case "bip01 head": return 6;
                case "bip01 l clavicle": return 10;
                case "bip01 l upperarm": return 11;
                case "bip01 l forearm": return 12;
                case "bip01 l hand": return 13;
                case "bip01 l finger0": return 14;
                case "bip01 l finger01": return 15;
                case "bip01 l finger02": return 16;
                case "bip01 l finger1": return 17;
                case "bip01 l finger11": return 18;
                case "bip01 l finger12": return 19;
                case "bip01 l finger2": return 20;
                case "bip01 l finger21": return 21;
                case "bip01 l finger22": return 22;
                case "bip01 l finger3": return 23;
                case "bip01 l finger31": return 24;
                case "bip01 l finger32": return 25;
                case "bip01 l finger4": return 26;
                case "bip01 l finger41": return 27;
                case "bip01 l finger42": return 28;
                case "bip01 r clavicle": return 29;
                case "bip01 r upperarm": return 30;
                case "bip01 r forearm": return 31;
                case "bip01 r hand": return 32;
                case "bip01 r finger0": return 33;
                case "bip01 r finger01": return 34;
                case "bip01 r finger02": return 35;
                case "bip01 r finger1": return 36;
                case "bip01 r finger11": return 37;
                case "bip01 r finger12": return 38;
                case "bip01 r finger2": return 39;
                case "bip01 r finger21": return 40;
                case "bip01 r finger22": return 41;
                case "bip01 r finger3": return 42;
                case "bip01 r finger31": return 43;
                case "bip01 r finger32": return 44;
                case "bip01 r finger4": return 45;
                case "bip01 r finger41": return 46;
                case "bip01 r finger42": return 47;
                case "bip01 l thigh": return 48;
                case "bip01 l calf": return 49;
                case "bip01 l foot": return 50;
                case "bip01 l toe0": return 51;
                case "bip01 r thigh": return 52;
                case "bip01 r calf": return 53;
                case "bip01 r foot": return 54;
                case "bip01 r toe0": return 55;
                case "mixamorig:hips": return 1;
                case "mixamorig:spine": return 2;
                case "mixamorig:spine1": return 3;
                case "mixamorig:spine2": return 4;
                case "mixamorig:neck": return 5;
                case "mixamorig:head": return 6;
                case "mixamorig:leftshoulder": return 10;
                case "mixamorig:leftarm": return 11;
                case "mixamorig:leftforearm": return 12;
                case "mixamorig:lefthand": return 13;
                case "mixamorig:lefthandthumb1": return 14;
                case "mixamorig:lefthandthumb2": return 15;
                case "mixamorig:lefthandthumb3": return 16;
                case "mixamorig:lefthandindex1": return 17;
                case "mixamorig:lefthandindex2": return 18;
                case "mixamorig:lefthandindex3": return 19;
                case "mixamorig:lefthandmiddle1": return 20;
                case "mixamorig:lefthandmiddle2": return 21;
                case "mixamorig:lefthandmiddle3": return 22;
                case "mixamorig:lefthandring1": return 23;
                case "mixamorig:lefthandring2": return 24;
                case "mixamorig:lefthandring3": return 25;
                case "mixamorig:lefthandpinky1": return 26;
                case "mixamorig:lefthandpinky2": return 27;
                case "mixamorig:lefthandpinky3": return 28;
                case "mixamorig:rightshoulder": return 29;
                case "mixamorig:rightarm": return 30;
                case "mixamorig:rightforearm": return 31;
                case "mixamorig:righthand": return 32;
                case "mixamorig:righthandthumb1": return 33;
                case "mixamorig:righthandthumb2": return 34;
                case "mixamorig:righthandthumb3": return 35;
                case "mixamorig:righthandindex1": return 36;
                case "mixamorig:righthandindex2": return 37;
                case "mixamorig:righthandindex3": return 38;
                case "mixamorig:righthandmiddle1": return 39;
                case "mixamorig:righthandmiddle2": return 40;
                case "mixamorig:righthandmiddle3": return 41;
                case "mixamorig:righthandring1": return 42;
                case "mixamorig:righthandring2": return 43;
                case "mixamorig:righthandring3": return 44;
                case "mixamorig:righthandpinky1": return 45;
                case "mixamorig:righthandpinky2": return 46;
                case "mixamorig:righthandpinky3": return 47;
                case "mixamorig:leftupleg": return 48;
                case "mixamorig:leftleg": return 49;
                case "mixamorig:leftfoot": return 50;
                case "mixamorig:lefttoebase": return 51;
                case "mixamorig:rightupleg": return 52;
                case "mixamorig:rightleg": return 53;
                case "mixamorig:rightfoot": return 54;
                case "mixamorig:righttoebase": return 55;
                default: return -1;
            }
        }

        static int[] CanonicalParents()
        {
            var parents = new int[CanonicalNames.Length];
            var reference = new Pose[CanonicalNames.Length];
            if (CanonicalHumanoid.WriteReference(parents, reference) != AnimationError.None)
            {
                throw new InvalidOperationException("real: canonical reference failed");
            }
            return parents;
        }

        static Pose[] CanonicalReference()
        {
            var parents = new int[CanonicalNames.Length];
            var reference = new Pose[CanonicalNames.Length];
            if (CanonicalHumanoid.WriteReference(parents, reference) != AnimationError.None)
            {
                throw new InvalidOperationException("real: canonical reference failed");
            }
            return reference;
        }

        static AssetId CanonicalFamily()
        {
            if (ContentIdentity.Skeleton("Humanoid", 1, CanonicalNames, CanonicalParents(), CanonicalReference(), out var family) != AnimationError.None || family.IsEmpty)
            {
                throw new InvalidOperationException("real: canonical family hash failed");
            }
            return family;
        }

        static string? FindRepoRoot()
        {
            var scan = AppContext.BaseDirectory;
            for (var i = 0; i < 12 && scan != null; i++)
            {
                if (File.Exists(Path.Combine(scan, "SA.Motion.sln")))
                {
                    return scan;
                }
                scan = Path.GetDirectoryName(scan);
            }
            return null;
        }

        static float VectorGap(Float3 a, Float3 b)
        {
            return Math.Max(Math.Abs(a.X - b.X), Math.Max(Math.Abs(a.Y - b.Y), Math.Abs(a.Z - b.Z)));
        }

        static float RotationGap(Quaternion a, Quaternion b)
        {
            var sign = Quaternion.Dot(a, b) < 0f ? -1f : 1f;
            return Math.Max(Math.Max(Math.Abs(a.X - sign * b.X), Math.Abs(a.Y - sign * b.Y)), Math.Max(Math.Abs(a.Z - sign * b.Z), Math.Abs(a.W - sign * b.W)));
        }

        static float ScaleGap(Float3 scale)
        {
            return Math.Max(Math.Abs(scale.X - 1f), Math.Max(Math.Abs(scale.Y - 1f), Math.Abs(scale.Z - 1f)));
        }

        static bool Finite(in Affine value)
        {
            return value.C0.IsFinite && value.C1.IsFinite && value.C2.IsFinite && value.C3.IsFinite;
        }

        static bool RootIsIdentity(in RigidPose pose)
        {
            return pose.Position.LengthSquared <= 1e-12f && Math.Abs(pose.Rotation.W) >= 0.999999f;
        }

        static bool RootIsPlanar(in RigidPose pose)
        {
            return Math.Abs(pose.Position.Y) <= 1e-6f && Math.Abs(pose.Rotation.X) <= 1e-6f && Math.Abs(pose.Rotation.Z) <= 1e-6f;
        }
    }
}
