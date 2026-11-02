using System.Text.Json;
using SA.Animation.Content;
using SA.Motion.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace SA.Animation.Tests;

public class C08Conformance
{
    const int Bones = 56;
    static readonly AssetId OtherFamily = new(91, 92, 93, 94);
    static readonly AssetId OtherTarget = new(95, 96, 97, 98);

    readonly ITestOutputHelper output;

    public C08Conformance(ITestOutputHelper output)
    {
        this.output = output;
    }

    static void Canonical(out int[] parents, out Pose[] reference)
    {
        parents = new int[Bones];
        reference = new Pose[Bones];
        Assert.Equal(AnimationError.None, CanonicalHumanoid.WriteReference(parents, reference));
    }

    static AssetId FamilyId(int[] parents, Pose[] reference)
    {
        var names = new string[Bones];
        for (var i = 0; i < Bones; i++)
        {
            names[i] = "B" + i.ToString("D2");
        }
        Assert.Equal(AnimationError.None, ContentIdentity.Skeleton("Humanoid", 1, names, parents, reference, out var family));
        Assert.False(family.IsEmpty);
        return family;
    }
    static AssetId FamilyId(string rig, string[] names, int[] parents, Pose[] reference)
    {
        Assert.Equal(AnimationError.None, ContentIdentity.Skeleton(rig, 1, names, parents, reference, out var family));
        Assert.False(family.IsEmpty);
        return family;
    }

    static (Pose[] Samples, RigidPose[] Root) TurnClip(Pose[] reference, int count, double duration)
    {
        var samples = new Pose[count * Bones];
        var root = new RigidPose[count];
        var up = new Float3(0f, 1f, 0f);
        var outOfPlane = new Float3(0f, 0f, 1f);
        for (var i = 0; i < count; i++)
        {
            var t = count == 1 ? 0f : i / (float)(count - 1);
            reference.CopyTo(samples, i * Bones);
            samples[i * Bones + 1].Position.Y += 0.03f * MathF.Sin(t * Scalar.TwoPi);
            samples[i * Bones + 11].Rotation = Quaternion.AxisAngle(outOfPlane, 0.25f * MathF.Sin(t * Scalar.TwoPi));
            samples[i * Bones + 30].Rotation = Quaternion.AxisAngle(outOfPlane, -0.25f * MathF.Sin(t * Scalar.TwoPi));
            root[i] = new RigidPose(new Float3(0f, 0f, t), Quaternion.AxisAngle(up, t * Scalar.Pi * 0.5f));
        }
        return (samples, root);
    }

    static (Pose[] Samples, RigidPose[] Root) VerticalClip(Pose[] reference, int count, double duration)
    {
        var samples = new Pose[count * Bones];
        var root = new RigidPose[count];
        var sideways = new Float3(1f, 0f, 0f);
        for (var i = 0; i < count; i++)
        {
            var t = count == 1 ? 0f : i / (float)(count - 1);
            reference.CopyTo(samples, i * Bones);
            samples[i * Bones + 1].Position.Y -= 0.08f * MathF.Sin(t * Scalar.Pi);
            samples[i * Bones + 2].Rotation = Quaternion.AxisAngle(sideways, 0.15f * MathF.Sin(t * Scalar.Pi));
            root[i] = new RigidPose(new Float3(0f, 0.5f * t, 0f), Quaternion.Identity);
        }
        return (samples, root);
    }

    static SharedClip CheckRaw(AssetId family, int[] parents, Pose[] reference, Pose[] samples, int count, double duration, bool loop, RootPolicy policy, RigidPose[] root, int bones)
    {
        Assert.Equal(AnimationError.None, SharedClipEncoder.Encode(family, parents, reference, samples, count, duration, loop, policy, root,
            [], [], [], false, CompressionLimits.Default, out var bytes, out var report));
        Assert.Equal(AnimationError.None, SharedClipReader.Load(bytes, family, reference, out var clip));
        Assert.NotNull(clip);
        var actual = new Pose[bones];
        var expected = new Pose[bones];
        var scratch = new RigidPose[bones * 2];
        var budget = CompressionLimits.Default;
        void Check(double seconds)
        {
            Assert.Equal(AnimationError.None, clip!.Sample(seconds, reference, actual));
            var span = seconds;
            if (loop && duration > 0)
            {
                span = seconds - duration * Math.Floor(seconds / duration);
            }
            var frame = (float)(span / duration * (count - 1));
            for (var b = 0; b < bones; b++)
            {
                expected[b] = ClipSampling.At(samples, bones, b, frame);
            }
            Assert.Equal(AnimationError.None, ObjectSpaceError.Measure(parents, expected, actual, [], scratch, out var error));
            Assert.InRange(error.AngleRadians, 0, budget.AngleRadians);
            Assert.InRange(error.PositionMetres, 0, budget.PositionMetres);
        }
        for (var i = 0; i < count; i++)
        {
            var seconds = duration * i / (count - 1);
            Check(seconds);
            var span = seconds;
            if (loop && duration > 0)
            {
                span = seconds - duration * Math.Floor(seconds / duration);
            }
            var landed = (int)Math.Round(span / duration * (count - 1));
            Assert.Equal(AnimationError.None, clip!.Sample(seconds, reference, actual));
            for (var b = 0; b < bones; b++)
            {
                Assert.InRange(PoseFixtures.VectorGap(samples[landed * bones + b].Position, actual[b].Position), 0, 1e-6f);
                Assert.InRange(PoseFixtures.RotationGap(samples[landed * bones + b].Rotation, actual[b].Rotation), 0, 1e-6f);
            }
        }
        for (var k = 1; k < 4; k++)
        {
            Check(duration * k / 4);
        }
        for (var i = 0; i + 1 < count; i++)
        {
            Check(duration * (i + 0.5) / (count - 1));
        }
        if (loop && count > 1)
        {
            Check(duration * (1 - 1e-7));
        }
        return clip!;
    }

    static void Near(RigidPose a, RigidPose b, float positionTolerance = 1e-5f, float angleTolerance = 1e-5f)
    {
        Assert.InRange((a.Position - b.Position).Length, 0, positionTolerance);
        Assert.InRange(RotationVector.Log(Quaternion.Multiply(a.Rotation, Quaternion.InverseUnit(b.Rotation))).Length, 0, angleTolerance);
    }

    [Fact]
    public void SyntheticTurnClipRoundTripsRawAtSamplesQuartersAndMidpoints()
    {
        Canonical(out var parents, out var reference);
        var family = FamilyId(parents, reference);
        var motion = TurnClip(reference, 33, 2.0);
        var clip = CheckRaw(family, parents, reference, motion.Samples, 33, 2.0, true, RootPolicy.Planar, motion.Root, Bones);
        Assert.Equal(33, clip.SampleCount);
        Assert.Equal(2.0, clip.Duration);
        Assert.Equal(RootPolicy.Planar, clip.RootPolicy);
    }

    [Fact]
    public void VerticalMotionClipRoundTripsRawRootAndSamples()
    {
        Canonical(out var parents, out var reference);
        var family = FamilyId(parents, reference);
        var motion = VerticalClip(reference, 17, 1.0);
        var clip = CheckRaw(family, parents, reference, motion.Samples, 17, 1.0, false, RootPolicy.FullRigid, motion.Root, Bones);
        for (var i = 0; i < motion.Root.Length; i++)
        {
            Assert.Equal(motion.Root[i].Position, clip.Root[i].Position);
            Assert.InRange(PoseFixtures.RotationGap(motion.Root[i].Rotation, clip.Root[i].Rotation), 0, 1e-6f);
        }
        Assert.Equal(AnimationError.None, RootMotion.Delta(clip.Root, 1.0, 0, 1.0, false, 1f, out var rise));
        Assert.InRange((rise.Position - new Float3(0f, 0.5f, 0f)).Length, 0, 1e-4f);
        Assert.InRange(RotationVector.Log(rise.Rotation).Length, 0, 1e-4f);
    }

    [Fact]
    public void RootEndpointsProveTranslationTurnCyclesAndSeeking()
    {
        Canonical(out _, out var reference);
        var motion = TurnClip(reference, 33, 2.0);
        var root = motion.Root;
        Assert.Equal(AnimationError.None, RootMotion.Delta(root, 2.0, 0, 2.0, true, 1f, out var end));
        Assert.InRange((end.Position - new Float3(0f, 0f, 1f)).Length, 0, 1e-4f);
        var turn = RotationVector.Log(end.Rotation);
        Assert.InRange(turn.Y, Scalar.Pi * 0.5f - 1e-3f, Scalar.Pi * 0.5f + 1e-3f);
        Assert.InRange(Math.Abs(turn.X) + Math.Abs(turn.Z), 0, 1e-3f);
        Assert.Equal(AnimationError.None, RootMotion.Delta(root, 2.0, 0, 2.0, true, 2f, out var scaled));
        Assert.InRange((scaled.Position - new Float3(0f, 0f, 2f)).Length, 0, 1e-4f);
        Assert.Equal(AnimationError.None, RootMotion.Delta(root, 2.0, 0, 8.0, true, 1f, out var cycles));
        Assert.InRange(cycles.Position.Length, 0, 1e-3f);
        Assert.InRange(RotationVector.Log(cycles.Rotation).Length, 0, 1e-3f);
        Assert.Equal(AnimationError.None, RootMotion.Delta(root, 2.0, -2.0, 0, true, 1f, out var negative));
        Near(negative, end);
        Assert.Equal(AnimationError.None, RootMotion.Delta(root, 2.0, 0.25, 1.5, true, 1f, out var forward));
        Assert.Equal(AnimationError.None, RootMotion.Delta(root, 2.0, 1.5, 0.25, true, 1f, out var backward));
        Assert.True(RigidMath.TryCompose(forward, backward, out var cancelled));
        Assert.InRange(cancelled.Position.Length, 0, 1e-4f);
        Assert.InRange(RotationVector.Log(cancelled.Rotation).Length, 0, 1e-3f);
        Assert.Equal(AnimationError.None, RootMotion.Delta(root, 2.0, 1.0, 1.0, true, 1f, out var seek));
        Assert.InRange(seek.Position.Length, 0, 1e-5f);
        Assert.InRange(RotationVector.Log(seek.Rotation).Length, 0, 1e-4f);
    }

    [Fact]
    public void CrossSkeletonMappingCompilesAppliesAndReloadsWithHeightRatio()
    {
        Canonical(out var parents, out var reference);
        var family = FamilyId(parents, reference);
        var target = new AssetId(7, 7, 7, 7);
        var count = Bones + 2;
        var targetParents = new int[count];
        Array.Copy(parents, targetParents, Bones);
        targetParents[Bones] = 13;
        targetParents[Bones + 1] = 32;
        var calibration = new Pose[count];
        Array.Copy(reference, calibration, Bones);
        for (var i = 1; i < Bones; i++)
        {
            calibration[i].Position = calibration[i].Position * 1.1f;
        }
        var twist = new Float3(0f, 0f, 1f);
        calibration[11].Rotation = Quaternion.AxisAngle(twist, 0.17f);
        calibration[30].Rotation = Quaternion.AxisAngle(twist, -0.17f);
        calibration[Bones] = new Pose { Position = new Float3(-0.05f, 0f, 0.1f), Rotation = Quaternion.Identity, Scale = Float3.One };
        calibration[Bones + 1] = new Pose { Position = new Float3(0.05f, 0f, 0.1f), Rotation = Quaternion.Identity, Scale = Float3.One };
        var scores = new int[Bones * count];
        for (var r = 0; r < Bones; r++)
        {
            for (var b = 0; b < count; b++)
            {
                scores[r * count + b] = b == r ? 100 : (r * 31 + b * 17) % 5;
            }
        }
        Assert.Equal(AnimationError.None, MappingAssignment.Solve(scores, Bones, count, 15, out var assignment));
        Assert.NotNull(assignment);
        for (var r = 0; r < Bones; r++)
        {
            Assert.Equal(r, assignment!.BoneOfRole[r]);
        }
        var sourceMap = new int[count];
        for (var b = 0; b < count; b++)
        {
            sourceMap[b] = -1;
        }
        for (var r = 0; r < Bones; r++)
        {
            sourceMap[assignment!.BoneOfRole[r]] = r;
        }
        Assert.True(CalibrationFrame.TryRotation(new Float3(1f, 0f, 0f), new Float3(0f, 1f, 0f), new Float3(1f, 0f, 0f), new Float3(0f, 1f, 0f), out var aligned));
        Assert.InRange(RotationVector.Log(aligned).Length, 0, 1e-5f);
        var scratch = new RigidPose[Bones + count];
        var rows = new RetargetRow[count];
        Assert.Equal(AnimationError.None, RetargetTable.Compile(family, target, parents, reference, targetParents, calibration, sourceMap, 1, 1, 1.1f, scratch, rows, out var binding));
        Assert.Equal(1.1f, binding.HeightRatio);
        Assert.Equal(RetargetRowMode.Static, rows[Bones].Mode);
        Assert.Equal(RetargetRowMode.Static, rows[Bones + 1].Mode);
        var input = (Pose[])reference.Clone();
        input[1].Position += new Float3(0.2f, -0.1f, 0.3f);
        input[11].Rotation = Quaternion.AxisAngle(new Float3(0f, 1f, 0f), 0.5f);
        var canonicalModel = new RigidPose[Bones];
        var targetModel = new RigidPose[count];
        var result = new Pose[count];
        var oracle = new Pose[count];
        Assert.Equal(AnimationError.None, RetargetTable.Apply(family, binding, parents, input, rows, canonicalModel, targetModel, result));
        Assert.Equal(AnimationError.None, RetargetTable.ApplyReference(family, binding, parents, input, rows, canonicalModel, targetModel, oracle));
        for (var i = 0; i < count; i++)
        {
            if (i != 1)
            {
                Assert.Equal(calibration[i].Position, result[i].Position);
            }
            Assert.InRange(PoseFixtures.RotationGap(result[i].Rotation, oracle[i].Rotation), 0, 1e-5f);
            Assert.InRange(PoseFixtures.VectorGap(result[i].Position, oracle[i].Position), 0, 1e-5f);
        }
        Assert.InRange((result[1].Position - calibration[1].Position).Length, 1e-3f, 1e6f);
        Assert.Equal(AnimationError.None, RetargetFile.Encode(binding, parents, reference, rows, out var tableBytes));
        Assert.Equal(AnimationError.None, RetargetFile.Load(tableBytes, family, target, parents, reference, out var reloaded, out var reloadedRows));
        Assert.Equal(count, reloadedRows.Length);
        Assert.Equal(binding.HeightRatio, reloaded.HeightRatio);
        var replay = new Pose[count];
        Assert.Equal(AnimationError.None, RetargetTable.Apply(family, reloaded, parents, input, reloadedRows, canonicalModel, targetModel, replay));
        for (var i = 0; i < count; i++)
        {
            Assert.InRange(PoseFixtures.Gap(result[i], replay[i]), 0, 1e-5f);
        }
        Assert.Equal(AnimationError.FamilyMismatch, RetargetTable.Apply(OtherFamily, binding, parents, input, rows, canonicalModel, targetModel, replay));
    }

    [Fact]
    public void IdentityRetargetPreservesAnimatedTranslationsExactly()
    {
        Canonical(out var parents, out var reference);
        var family = FamilyId(parents, reference);
        var sampled = (Pose[])reference.Clone();
        sampled[1].Position += new Float3(0.1f, -0.2f, 0.4f);
        sampled[13].Position += new Float3(-0.05f, 0.02f, 0.01f);
        sampled[5].Rotation = Quaternion.AxisAngle(new Float3(0f, 1f, 0f), 0.3f);
        var scratch = new RigidPose[Bones];
        var output = new Pose[Bones];
        Assert.Equal(AnimationError.None, IdentityRetarget.Apply(family, family, parents, sampled, scratch, output));
        for (var i = 0; i < Bones; i++)
        {
            Assert.Equal(sampled[i], output[i]);
        }
        Assert.Equal(AnimationError.FamilyMismatch, IdentityRetarget.Apply(OtherFamily, family, parents, sampled, scratch, output));
    }

    [Fact]
    public void ErrorCodesOnFamilyMismatchCorruptAndTruncated()
    {
        Canonical(out var parents, out var reference);
        var family = FamilyId(parents, reference);
        var motion = TurnClip(reference, 33, 2.0);
        Assert.Equal(AnimationError.None, SharedClipEncoder.Encode(family, parents, reference, motion.Samples, 33, 2.0, true, RootPolicy.Planar, motion.Root,
            [], [], [], false, CompressionLimits.Default, out var bytes, out _));
        Assert.Equal(AnimationError.FamilyMismatch, SharedClipReader.Load(bytes, OtherFamily, reference, out _));
        var corrupt = (byte[])bytes.Clone();
        corrupt[corrupt.Length - 16] = (byte)(corrupt[corrupt.Length - 16] ^ 0xFF);
        corrupt[corrupt.Length - 17] = (byte)(corrupt[corrupt.Length - 17] ^ 0x01);
        Assert.Equal(AnimationError.CorruptData, SharedClipReader.Load(corrupt, family, reference, out _));
        Assert.Equal(AnimationError.InvalidFormat, SharedClipReader.Load(bytes.AsSpan(0, bytes.Length / 2), family, reference, out _));
        Assert.Equal(AnimationError.InvalidFormat, RetargetFile.Load(bytes, family, OtherTarget, parents, reference, out _, out _));
    }

    [Fact]
    public void GoldenFixturesDecodeAtDocumentedValues()
    {
        var goldenFamily = new AssetId(1, 2, 3, 4);
        var goldenReference = new[] { Pose.Identity, At(new Float3(0, 1, 0)), At(new Float3(1, 0, 0)) };
        foreach (var name in new[] { "three-bone-raw.saclip", "three-bone-packed.saclip" })
        {
            var bytes = Fixture(name);
            Assert.Equal(AnimationError.None, SharedClipReader.Load(bytes, goldenFamily, goldenReference, out var clip));
            Assert.NotNull(clip);
            Assert.Equal(3, clip!.SampleCount);
            Assert.Equal(1, clip.Duration);
            var pose = new Pose[3];
            Assert.Equal(AnimationError.None, clip.Sample(0.5, goldenReference, pose));
            Assert.InRange(pose[1].Position.Y, 1.04999f, 1.05001f);
            Assert.InRange(RotationVector.Log(pose[2].Rotation).Z, 0.2999f, 0.3001f);
        }
        var tableBytes = Fixture("three-bone-target.saret");
        Assert.Equal(AnimationError.None, RetargetFile.Load(tableBytes, goldenFamily, new AssetId(10, 0, 0, 0), new[] { -1, 0, 1 }, goldenReference, out var binding, out var rows));
        Assert.Equal(2, binding.HeightRatio);
        Assert.Equal(3, rows.Length);
        Assert.Equal(new Float3(2, 0, 0), rows[2].Calibration.Position);
    }

    [Fact]
    public void ExtractorManifestDrivesRawReloadWhenPresent()
    {
        var manifest = FindManifest();
        if (manifest == null)
        {
            output.WriteLine("C08 awaiting extractor: Temp/sa/c08/_manifest.json not present; synthetic and golden checks carry the gate.");
            return;
        }
        output.WriteLine("C08 extractor manifest: " + manifest);
        using var manifestDocument = JsonDocument.Parse(File.ReadAllText(manifest));
        var directory = Path.GetDirectoryName(manifest);
        Assert.NotNull(directory);
        Assert.True(manifestDocument.RootElement.TryGetProperty("clips", out var clips));
        var checkedClips = 0;
        foreach (var entry in clips.EnumerateArray())
        {
            var name = entry.TryGetProperty("clip", out var clipName) ? clipName.GetString() : entry.GetProperty("name").GetString();
            var file = entry.GetProperty("file").GetString();
            Assert.False(string.IsNullOrEmpty(name));
            Assert.False(string.IsNullOrEmpty(file));
            CheckExtractorClip(directory!, name!, file!);
            checkedClips++;
        }
        Assert.True(checkedClips > 0);
        output.WriteLine("C08 extractor clips reloaded raw: " + checkedClips);
    }

    static Pose At(Float3 position) => new() { Position = position, Rotation = Quaternion.Identity, Scale = Float3.One };

    static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    static string? FindManifest()
    {
        var candidates = new List<string>();
        var scan = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && scan != null; i++)
        {
            candidates.Add(Path.Combine(scan, "Temp", "sa", "c08", "_manifest.json"));
            scan = Path.GetDirectoryName(scan);
        }
        candidates.Add("/home/i/ruk2/Temp/sa/c08/_manifest.json");
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    void CheckExtractorClip(string directory, string name, string file)
    {
        var path = Path.Combine(directory, file);
        output.WriteLine("C08 checking " + name + " in " + path);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var rig = root.GetProperty("rig").GetString();
        var bonesElement = root.GetProperty("bones");
        var framesElement = root.GetProperty("frames");
        var sampleCount = root.GetProperty("sampleCount").GetInt32();
        var duration = root.GetProperty("duration").GetDouble();
        var loop = root.GetProperty("loop").GetBoolean();
        var bones = bonesElement.GetArrayLength();
        Assert.False(string.IsNullOrEmpty(rig));
        Assert.InRange(bones, 1, SampleLocator.MaxBones);
        Assert.Equal(sampleCount, framesElement.GetArrayLength());
        var names = new string[bones];
        var parents = new int[bones];
        var index = 0;
        foreach (var bone in bonesElement.EnumerateArray())
        {
            names[index] = bone.GetProperty("path").GetString()!;
            var parent = bone.GetProperty("parent").GetInt32();
            parents[index] = index == 0 && parent == 0 ? -1 : parent;
            Assert.True(parents[index] < index, "C08 " + name + ": bone " + index + " breaks parent-before-child order; re-run the extractor into Temp/sa/c08.");
            index++;
        }
        Assert.Equal(bones, new HashSet<string>(names).Count);
        var samples = new Pose[bones * sampleCount];
        var rootMotion = new RigidPose[sampleCount];
        index = 0;
        foreach (var frame in framesElement.EnumerateArray())
        {
            ReadFramePoses(frame.GetProperty("p"), frame.GetProperty("q"), frame.GetProperty("s"), samples, index * bones, bones, name, path, index);
            var rp = frame.GetProperty("rootP");
            var rq = frame.GetProperty("rootQ");
            rootMotion[index] = new RigidPose(
                new Float3(rp[0].GetSingle(), rp[1].GetSingle(), rp[2].GetSingle()),
                new Quaternion(rq[0].GetSingle(), rq[1].GetSingle(), rq[2].GetSingle(), rq[3].GetSingle()));
            index++;
        }
        var reference = new Pose[bones];
        Array.Copy(samples, reference, bones);
        var family = FamilyId(rig!, names, parents, reference);
        var policy = RootPolicy.None;
        foreach (var pose in rootMotion)
        {
            if (RootIsIdentity(pose))
            {
                continue;
            }
            policy = RootIsPlanar(pose) && policy != RootPolicy.FullRigid ? RootPolicy.Planar : RootPolicy.FullRigid;
        }
        var clip = CheckRaw(family, parents, reference, samples, sampleCount, duration, loop, policy, rootMotion, bones);
        Assert.Equal(sampleCount, clip.SampleCount);
        Assert.Equal(policy, clip.RootPolicy);
        Assert.Equal(AnimationError.None, RootMotion.Delta(clip.Root, duration, 0, duration, loop, 1f, out var end));
        Assert.True(RigidMath.TryInverse(rootMotion[0], out var inverse));
        Assert.True(RigidMath.TryCompose(inverse, rootMotion[sampleCount - 1], out var span));
        if (loop)
        {
            Assert.True(RigidMath.TryCompose(span, rootMotion[0], out span));
        }
        Near(end, span, 1e-4f, 1e-3f);
        var spin = RotationVector.Log(end.Rotation);
        output.WriteLine("C08 " + name + ": endpoint travel " + end.Position.Length + "m turn " + spin.Length + "rad policy " + policy);
    }

    static bool RootIsIdentity(in RigidPose pose) =>
        pose.Position.LengthSquared <= 1e-12f && Math.Abs(pose.Rotation.W) >= 0.999999f;

    static bool RootIsPlanar(in RigidPose pose) =>
        Math.Abs(pose.Position.Y) <= 1e-6f && Math.Abs(pose.Rotation.X) <= 1e-6f && Math.Abs(pose.Rotation.Z) <= 1e-6f;

    static void ReadFramePoses(JsonElement positions, JsonElement rotations, JsonElement scales, Pose[] samples, int first, int bones, string name, string path, int frame)
    {
        Assert.Equal(bones, positions.GetArrayLength());
        Assert.Equal(bones, rotations.GetArrayLength());
        Assert.Equal(bones, scales.GetArrayLength());
        for (var i = 0; i < bones; i++)
        {
            var p = positions[i];
            var q = rotations[i];
            var s = scales[i];
            var scaleGap = Math.Max(Math.Abs(s[0].GetSingle() - 1f), Math.Max(Math.Abs(s[1].GetSingle() - 1f), Math.Abs(s[2].GetSingle() - 1f)));
            Assert.True(scaleGap <= 1e-5f,
                "C08 " + name + ": frame " + frame + " bone " + i + " carries non-unit scale; re-extract " + path + " with baked unit scales.");
            samples[first + i] = new Pose
            {
                Position = new Float3(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle()),
                Rotation = new Quaternion(q[0].GetSingle(), q[1].GetSingle(), q[2].GetSingle(), q[3].GetSingle()),
                Scale = Float3.One
            };
        }
    }
}
