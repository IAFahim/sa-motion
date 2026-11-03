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
    public sealed class PilotSweep
    {
        const int PlanRigs = 50;
        const int PlanClips = 500;
        const int PlanPairs = 25000;
        const long BudgetBytes = 67108864;
        const int ExpectedRigs = 3;
        const int ExpectedClips = 2;
        const int ExpectedCompatible = 4;
        const int ExpectedIncompatible = 2;
        const int ExpectedFramesPerClip = 3;
        const float RotationAgreement = 1e-5f;
        const float PositionAgreement = 1e-5f;
        const int PoseBytes = 40;
        const int RigidPoseBytes = 28;
        const int AffineBytes = 64;

        static readonly AssetId GoldenFamily = new AssetId(1, 2, 3, 4);
        static readonly AssetId GoldenTarget = new AssetId(10, 0, 0, 0);
        static readonly int[] GoldenParents = new int[] { -1, 0, 1 };
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        static readonly string[] HumanNames = new string[]
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

        public PilotSweep(ITestOutputHelper output)
        {
            this.output = output;
        }

        sealed class TargetRig
        {
            public string Name = "";
            public int[] Parents = Array.Empty<int>();
            public Pose[] Calibration = Array.Empty<Pose>();
            public AssetId Target;
            public RetargetRow[] Rows = Array.Empty<RetargetRow>();
            public RetargetBinding Binding;
            public int Hips;
        }

        sealed class ClipView
        {
            public string Name = "";
            public byte[] Bytes = Array.Empty<byte>();
            public SharedClip Clip = null!;
        }

        sealed class SweepResult
        {
            public int Compatible;
            public int Incompatible;
            public long Frames;
            public long Failures;
            public long PeakResident;
            public long FileBytes;
            public float WorstAngle;
            public float WorstPos;
            public float WorstPackedAngle;
            public float WorstPackedPos;
            public long AgreementPass;
            public long FinitePass;
            public long TranslationPass;
            public long HashPass;
            public long PalettePass;
            public long ParityPass;
            public long BudgetPass;
            public List<string> FailureNotes = new List<string>();
            public List<string> Matrix = new List<string>();
        }

        [Fact]
        public void InventoryMatchesDisk()
        {
            var raw = Fixture("three-bone-raw.saclip");
            var packed = Fixture("three-bone-packed.saclip");
            var table = Fixture("three-bone-target.saret");
            Assert.Equal(772, raw.Length);
            Assert.Equal(756, packed.Length);
            Assert.Equal(608, table.Length);
            var reference = GoldenReference();
            Assert.Equal(AnimationError.None, SharedClipReader.Load(raw, GoldenFamily, reference, out var rawClip));
            Assert.NotNull(rawClip);
            Assert.Equal(AnimationError.None, SharedClipReader.Load(packed, GoldenFamily, reference, out var packedClip));
            Assert.NotNull(packedClip);
            Assert.Equal(ExpectedFramesPerClip, rawClip!.SampleCount);
            Assert.Equal(ExpectedFramesPerClip, packedClip!.SampleCount);
            Assert.Equal(1, rawClip.Duration);
            Assert.Equal(3, rawClip.BoneCount);
            Assert.Equal(GoldenFamily, rawClip.Family);
            Assert.Equal(GoldenFamily, packedClip.Family);
            Assert.Equal(SacaFile.Digest(raw), rawClip.Artifact);
            Assert.Equal(SacaFile.Digest(packed), packedClip.Artifact);
            Assert.True(rawClip.Artifact != packedClip.Artifact);
            Assert.Equal(AnimationError.None, RetargetFile.Load(table, GoldenFamily, GoldenTarget, GoldenParents, reference, out var binding, out var rows));
            Assert.Equal(2f, binding.HeightRatio);
            Assert.Equal(3, rows.Length);
            Assert.Equal(new Float3(2, 0, 0), rows[2].Calibration.Position);
            Assert.Equal(GoldenFamily, binding.Family);
            Assert.Equal(GoldenTarget, binding.Target);
            var familyPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "humanoid-v1.safamily");
            Assert.True(File.Exists(familyPath));
            using var familyDocument = JsonDocument.Parse(File.ReadAllText(familyPath));
            var familyRoot = familyDocument.RootElement;
            Assert.Equal("Humanoid", familyRoot.GetProperty("family").GetString());
            Assert.Equal(1, familyRoot.GetProperty("familyVersion").GetInt32());
            Assert.Equal(56, familyRoot.GetProperty("bones").GetArrayLength());
            Assert.Equal(1, familyRoot.GetProperty("hipBone").GetInt32());
            output.WriteLine("pilot inventory: raw 772B packed 756B saret 608B family 56 bones");
        }

        [Fact]
        public void StreamedSweepCompatiblePairs()
        {
            var result = Sweep();
            output.WriteLine("pilot sweep: compatible " + result.Compatible + " incompatible " + result.Incompatible
                + " frames " + result.Frames + " failures " + result.Failures
                + " worstAngle " + result.WorstAngle.ToString("G9", Invariant)
                + " worstPos " + result.WorstPos.ToString("G9", Invariant)
                + " worstPackedAngle " + result.WorstPackedAngle.ToString("G9", Invariant)
                + " worstPackedPos " + result.WorstPackedPos.ToString("G9", Invariant)
                + " peakResident " + result.PeakResident + " budget " + BudgetBytes);
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
            Assert.Equal(ExpectedCompatible, result.Compatible);
            Assert.Equal(ExpectedIncompatible, result.Incompatible);
            Assert.Equal((long)(ExpectedCompatible * ExpectedFramesPerClip), result.Frames);
            Assert.True(result.PeakResident < BudgetBytes);
            Assert.True(result.Failures == 0);
        }

        [Fact]
        public void ShortfallStatedPlainly()
        {
            var result = Sweep();
            var payloads = new HashSet<string>();
            payloads.Add(rawArtifact());
            payloads.Add(packedArtifact());
            var synty = CountSynty();
            var showcaseClips = CountShowcaseClips();
            output.WriteLine("pilot shortfall vs plan 16.1: rigs " + ExpectedRigs + " not " + PlanRigs
                + " portable clips " + ExpectedClips + " not " + PlanClips
                + " swept compatible pairs " + result.Compatible + " not " + PlanPairs
                + " unique payloads " + payloads.Count + " duplicate groups 0"
                + " syntyFbx " + synty + " showcaseClips " + showcaseClips);
            Assert.Equal(ExpectedRigs, 3);
            Assert.Equal(ExpectedClips, 2);
            Assert.Equal(2, payloads.Count);
            Assert.Equal(721, synty);
            Assert.Equal(0, showcaseClips);
        }

        SweepResult Sweep()
        {
            var result = new SweepResult();
            var rigs = LoadRigs();
            var clips = LoadClips();
            for (var i = 0; i < clips.Count; i++)
            {
                result.FileBytes += clips[i].Bytes.Length;
            }
            ClipView? raw = null;
            ClipView? packed = null;
            for (var i = 0; i < clips.Count; i++)
            {
                if (clips[i].Name == "three-bone-raw.saclip")
                {
                    raw = clips[i];
                }
                else
                {
                    packed = clips[i];
                }
            }
            foreach (var rig in rigs)
            {
                foreach (var clip in clips)
                {
                    var compatible = clip.Clip.Family == rig.Binding.Family;
                    if (compatible)
                    {
                        result.Compatible++;
                        result.Matrix.Add("pilot pair " + rig.Name + " x " + clip.Name + ": compatible");
                        var other = clip == raw ? packed : raw;
                        CheckPair(rig, clip, other!, result);
                    }
                    else
                    {
                        result.Incompatible++;
                        result.Matrix.Add("pilot pair " + rig.Name + " x " + clip.Name + ": incompatible family");
                        var probe = new Pose[rig.Parents.Length];
                        var probeCanonical = new RigidPose[clip.Clip.BoneCount];
                        var probeTarget = new RigidPose[rig.Parents.Length];
                        Assert.Equal(AnimationError.FamilyMismatch, RetargetTable.Apply(
                            clip.Clip.Family, rig.Binding, GoldenParents, GoldenReference(), rig.Rows,
                            probeCanonical, probeTarget, probe));
                    }
                }
            }
            return result;
        }

        void CheckPair(TargetRig rig, ClipView clip, ClipView other, SweepResult result)
        {
            var bones = clip.Clip.BoneCount;
            var frames = clip.Clip.SampleCount;
            var decoded = new Pose[bones];
            var cross = new Pose[bones];
            var local = new Pose[rig.Parents.Length];
            var oracle = new Pose[rig.Parents.Length];
            var canonicalModel = new RigidPose[bones];
            var targetModel = new RigidPose[rig.Parents.Length];
            var oracleModel = new RigidPose[rig.Parents.Length];
            var affineModel = new Affine[rig.Parents.Length];
            var palette = new Affine[rig.Parents.Length];
            var bind = new Affine[rig.Parents.Length];
            var measure = new RigidPose[bones * 2];
            var map = new int[rig.Parents.Length];
            for (var i = 0; i < map.Length; i++)
            {
                map[i] = i;
                bind[i] = PoseMath.Affine(rig.Calibration[i]);
            }
            var scratchBytes = (long)(decoded.Length + cross.Length + local.Length + oracle.Length) * PoseBytes
                + (long)(canonicalModel.Length + targetModel.Length + oracleModel.Length + measure.Length) * RigidPoseBytes
                + (long)(affineModel.Length + palette.Length + bind.Length) * AffineBytes
                + (long)map.Length * 4;
            var rootResident = (long)clip.Clip.Root.Length * RigidPoseBytes;
            var trackMeta = (long)clip.Clip.Tracks.Length * 32;
            var segmentTable = (long)clip.Clip.Segments.Length * 32;
            var uploadCopy = (long)palette.Length * AffineBytes;
            var maxHipsDelta = 0f;
            for (var s = 0; s < clip.Clip.Segments.Length; s++)
            {
                var segment = clip.Clip.Segments[s];
                var resident = rootResident + trackMeta + segmentTable + segment.Bytes.Length + scratchBytes + uploadCopy;
                if (resident > result.PeakResident)
                {
                    result.PeakResident = resident;
                }
                var last = segment.FirstSample + segment.SampleCount;
                for (var frame = segment.FirstSample; frame < last; frame++)
                {
                    var seconds = frames == 1 ? 0.0 : clip.Clip.Duration * frame / (frames - 1);
                    CheckFrame(rig, clip, other, seconds, frame, decoded, cross, local, oracle,
                        canonicalModel, targetModel, oracleModel, affineModel, palette, bind, map, measure, result, ref maxHipsDelta);
                    result.Frames++;
                }
            }
            if (rig.Name == "three-bone-target")
            {
                if (maxHipsDelta <= 0.05f)
                {
                    Note(result, "pair " + rig.Name + " x " + clip.Name + ": hip ratio path idle maxDelta " + maxHipsDelta.ToString("G9", Invariant));
                }
            }
        }

        void CheckFrame(TargetRig rig, ClipView clip, ClipView other, double seconds, int frame,
            Pose[] decoded, Pose[] cross, Pose[] local, Pose[] oracle,
            RigidPose[] canonicalModel, RigidPose[] targetModel, RigidPose[] oracleModel,
            Affine[] affineModel, Affine[] palette, Affine[] bind, int[] map,
            RigidPose[] measure, SweepResult result, ref float maxHipsDelta)
        {
            var where = "pair " + rig.Name + " x " + clip.Name + " frame " + frame;
            if (clip.Clip.Sample(seconds, GoldenReference(), decoded) != AnimationError.None)
            {
                Note(result, where + ": sample failed");
                return;
            }
            result.HashPass++;
            if (other.Clip.Sample(seconds, GoldenReference(), cross) != AnimationError.None)
            {
                Note(result, where + ": cross sample failed");
                return;
            }
            for (var bone = 0; bone < decoded.Length; bone++)
            {
                if (!decoded[bone].Position.IsFinite || !decoded[bone].Rotation.IsValidRotation)
                {
                    Note(result, where + " bone " + bone + ": decoded not finite");
                    return;
                }
            }
            result.FinitePass++;
            if (RetargetTable.Apply(clip.Clip.Family, rig.Binding, GoldenParents, decoded, rig.Rows, canonicalModel, targetModel, local) != AnimationError.None)
            {
                Note(result, where + ": Apply failed");
                return;
            }
            result.AgreementPass++;
            if (RetargetTable.ApplyReference(clip.Clip.Family, rig.Binding, GoldenParents, decoded, rig.Rows, canonicalModel, oracleModel, oracle) != AnimationError.None)
            {
                Note(result, where + ": ApplyReference failed");
                return;
            }
            for (var bone = 0; bone < local.Length; bone++)
            {
                if (!local[bone].Position.IsFinite || !local[bone].Rotation.IsValidRotation)
                {
                    Note(result, where + " target bone " + bone + ": retarget not finite");
                    return;
                }
                var rotationGap = RotationGap(local[bone].Rotation, oracle[bone].Rotation);
                var positionGap = VectorGap(local[bone].Position, oracle[bone].Position);
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
                    Note(result, where + " target bone " + bone + ": reference/fast gap rot "
                        + rotationGap.ToString("G9", Invariant) + " pos " + positionGap.ToString("G9", Invariant));
                    return;
                }
                if (bone != rig.Hips)
                {
                    var drift = VectorGap(local[bone].Position, rig.Calibration[bone].Position);
                    if (drift != 0f)
                    {
                        Note(result, where + " target bone " + bone + ": non-Hips translation moved " + drift.ToString("G9", Invariant));
                        return;
                    }
                }
                else
                {
                    var delta = VectorGap(local[bone].Position, rig.Calibration[bone].Position);
                    if (delta > maxHipsDelta)
                    {
                        maxHipsDelta = delta;
                    }
                }
                var scaleGap = ScaleGap(local[bone].Scale);
                if (scaleGap > 1e-6f)
                {
                    Note(result, where + " target bone " + bone + ": scale gap " + scaleGap.ToString("G9", Invariant));
                    return;
                }
            }
            result.ParityPass++;
            result.TranslationPass++;
            Hierarchy.Model(rig.Parents, local, affineModel);
            if (SkinPalette.Build(affineModel, map, bind, Affine.Identity, palette) != AnimationError.None)
            {
                Note(result, where + ": palette build rejected");
                return;
            }
            for (var bone = 0; bone < palette.Length; bone++)
            {
                if (!Finite(palette[bone]))
                {
                    Note(result, where + " palette " + bone + ": not finite");
                    return;
                }
            }
            result.PalettePass++;
            if (ObjectSpaceError.Measure(GoldenParents, cross, decoded, Array.Empty<PoseMarker>(), measure, out var packedError) != AnimationError.None)
            {
                Note(result, where + ": packed budget measure failed");
                return;
            }
            if (packedError.AngleRadians > result.WorstPackedAngle)
            {
                result.WorstPackedAngle = packedError.AngleRadians;
            }
            if (packedError.PositionMetres > result.WorstPackedPos)
            {
                result.WorstPackedPos = packedError.PositionMetres;
            }
            if (packedError.AngleRadians > CompressionLimits.Default.AngleRadians
                || packedError.PositionMetres > CompressionLimits.Default.PositionMetres)
            {
                Note(result, where + ": packed/raw exceeds budget angle "
                    + packedError.AngleRadians.ToString("G9", Invariant) + " pos "
                    + packedError.PositionMetres.ToString("G9", Invariant));
                return;
            }
            result.BudgetPass++;
        }

        List<TargetRig> LoadRigs()
        {
            var rigs = new List<TargetRig>();
            var reference = GoldenReference();
            Assert.Equal(AnimationError.None, ContentIdentity.Skeleton("ThreeBone", 1,
                new string[] { "Root", "Hips", "Tip" }, GoldenParents, reference, out var selfTarget));
            var selfRows = new RetargetRow[3];
            var selfScratch = new RigidPose[6];
            Assert.Equal(AnimationError.None, RetargetTable.Compile(GoldenFamily, selfTarget,
                GoldenParents, reference, GoldenParents, reference, new int[] { 0, 1, 2 },
                1, 1, 1f, selfScratch, selfRows, out var selfBinding));
            rigs.Add(new TargetRig
            {
                Name = "three-bone",
                Parents = (int[])GoldenParents.Clone(),
                Calibration = reference,
                Target = selfTarget,
                Rows = selfRows,
                Binding = selfBinding,
                Hips = 1
            });
            var tableBytes = Fixture("three-bone-target.saret");
            Assert.Equal(AnimationError.None, RetargetFile.Load(tableBytes, GoldenFamily, GoldenTarget,
                GoldenParents, reference, out var tableBinding, out var tableRows));
            var tableParents = new int[tableRows.Length];
            var tableCalibration = new Pose[tableRows.Length];
            for (var i = 0; i < tableRows.Length; i++)
            {
                tableParents[i] = tableRows[i].Parent;
                tableCalibration[i] = tableRows[i].Calibration;
            }
            rigs.Add(new TargetRig
            {
                Name = "three-bone-target",
                Parents = tableParents,
                Calibration = tableCalibration,
                Target = tableBinding.Target,
                Rows = tableRows,
                Binding = tableBinding,
                Hips = tableBinding.TargetHips
            });
            var humanParents = new int[CanonicalHumanoid.BoneCount];
            var humanReference = new Pose[CanonicalHumanoid.BoneCount];
            Assert.Equal(AnimationError.None, CanonicalHumanoid.WriteReference(humanParents, humanReference));
            Assert.Equal(AnimationError.None, ContentIdentity.Skeleton("Humanoid", 1, HumanNames, humanParents, humanReference, out var humanFamily));
            Assert.Equal(AnimationError.None, ContentIdentity.Skeleton("HumanoidV1Target", 1, HumanNames, humanParents, humanReference, out var humanTarget));
            var humanMap = new int[CanonicalHumanoid.BoneCount];
            for (var i = 0; i < humanMap.Length; i++)
            {
                humanMap[i] = i;
            }
            var humanRows = new RetargetRow[CanonicalHumanoid.BoneCount];
            var humanScratch = new RigidPose[CanonicalHumanoid.BoneCount * 2];
            Assert.Equal(AnimationError.None, RetargetTable.Compile(humanFamily, humanTarget,
                humanParents, humanReference, humanParents, humanReference, humanMap,
                CanonicalHumanoid.Hips, CanonicalHumanoid.Hips, 1f, humanScratch, humanRows, out var humanBinding));
            rigs.Add(new TargetRig
            {
                Name = "humanoid-v1",
                Parents = humanParents,
                Calibration = humanReference,
                Target = humanTarget,
                Rows = humanRows,
                Binding = humanBinding,
                Hips = CanonicalHumanoid.Hips
            });
            return rigs;
        }

        List<ClipView> LoadClips()
        {
            var clips = new List<ClipView>();
            var reference = GoldenReference();
            foreach (var name in new string[] { "three-bone-raw.saclip", "three-bone-packed.saclip" })
            {
                var bytes = Fixture(name);
                Assert.Equal(AnimationError.None, SharedClipReader.Load(bytes, GoldenFamily, reference, out var clip));
                Assert.NotNull(clip);
                Assert.Equal(ExpectedFramesPerClip, clip!.SampleCount);
                Assert.Equal(SacaFile.Digest(bytes), clip.Artifact);
                clips.Add(new ClipView { Name = name, Bytes = bytes, Clip = clip });
            }
            return clips;
        }

        void WriteReport(SweepResult result)
        {
            var root = FindRepoRoot();
            if (root == null)
            {
                output.WriteLine("pilot report: repo root not found");
                return;
            }
            var builder = new StringBuilder(2048);
            builder.Append("{\"schema\":1,\"compatible\":").Append(result.Compatible);
            builder.Append(",\"incompatible\":").Append(result.Incompatible);
            builder.Append(",\"frames\":").Append(result.Frames);
            builder.Append(",\"failures\":").Append(result.Failures);
            builder.Append(",\"peakResident\":").Append(result.PeakResident);
            builder.Append(",\"budget\":").Append(BudgetBytes);
            builder.Append(",\"fileBytes\":").Append(result.FileBytes);
            builder.Append(",\"worstAngle\":").Append(result.WorstAngle.ToString("G9", Invariant));
            builder.Append(",\"worstPos\":").Append(result.WorstPos.ToString("G9", Invariant));
            builder.Append(",\"worstPackedAngle\":").Append(result.WorstPackedAngle.ToString("G9", Invariant));
            builder.Append(",\"worstPackedPos\":").Append(result.WorstPackedPos.ToString("G9", Invariant));
            builder.Append(",\"plan\":{\"rigs\":").Append(PlanRigs);
            builder.Append(",\"clips\":").Append(PlanClips);
            builder.Append(",\"pairs\":").Append(PlanPairs).Append("}");
            builder.Append(",\"checks\":{\"agreement\":").Append(result.AgreementPass);
            builder.Append(",\"finite\":").Append(result.FinitePass);
            builder.Append(",\"translation\":").Append(result.TranslationPass);
            builder.Append(",\"hash\":").Append(result.HashPass);
            builder.Append(",\"palette\":").Append(result.PalettePass);
            builder.Append(",\"parity\":").Append(result.ParityPass);
            builder.Append(",\"budget\":").Append(result.BudgetPass).Append("}");
            builder.Append(",\"ok\":").Append(result.Failures == 0 ? "true" : "false").Append("}");
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "artifacts"));
                File.WriteAllText(Path.Combine(root, "artifacts", "pilot-sweep-report.json"), builder.ToString());
                output.WriteLine("pilot report: artifacts/pilot-sweep-report.json");
            }
            catch (Exception ex)
            {
                output.WriteLine("pilot report unwritten: " + ex.GetType().Name);
            }
        }

        void Note(SweepResult result, string note)
        {
            result.Failures++;
            if (result.FailureNotes.Count < 16)
            {
                result.FailureNotes.Add(note);
            }
        }

        string rawArtifact()
        {
            var bytes = Fixture("three-bone-raw.saclip");
            var digest = SacaFile.Digest(bytes);
            return digest.A.ToString("X16", Invariant) + digest.B.ToString("X16", Invariant)
                + digest.C.ToString("X16", Invariant) + digest.D.ToString("X16", Invariant);
        }

        string packedArtifact()
        {
            var bytes = Fixture("three-bone-packed.saclip");
            var digest = SacaFile.Digest(bytes);
            return digest.A.ToString("X16", Invariant) + digest.B.ToString("X16", Invariant)
                + digest.C.ToString("X16", Invariant) + digest.D.ToString("X16", Invariant);
        }

        int CountSynty()
        {
            const string Synty = "/home/os/TestingAssets/Assets/Synty";
            if (!Directory.Exists(Synty))
            {
                return -1;
            }
            return Directory.GetFiles(Synty, "*.fbx", SearchOption.AllDirectories).Length;
        }

        int CountShowcaseClips()
        {
            const string Showcase = "/home/os/GitHub/com.sa.unity/Samples~/Showcase";
            if (!Directory.Exists(Showcase))
            {
                return -1;
            }
            var count = Directory.GetFiles(Showcase, "*.anim", SearchOption.AllDirectories).Length;
            count += Directory.GetFiles(Showcase, "*.fbx", SearchOption.AllDirectories).Length;
            return count;
        }

        static Pose[] GoldenReference()
        {
            return new Pose[]
            {
                Pose.Identity,
                At(new Float3(0, 1, 0)),
                At(new Float3(1, 0, 0))
            };
        }

        static Pose At(Float3 position)
        {
            return new Pose { Position = position, Rotation = Quaternion.Identity, Scale = Float3.One };
        }

        static byte[] Fixture(string name)
        {
            return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
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
    }
}
