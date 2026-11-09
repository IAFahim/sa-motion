using System.Text.Json;
using SA.Animation;
using SA.Motion.Numerics;
using SA.Soma;
using Xunit.Abstractions;
using static SA.Soma.Tests.SomaFixtures;

namespace SA.Soma.Tests;

public sealed class UnretargetEvalFactAttribute : FactAttribute
{
    public UnretargetEvalFactAttribute()
    {
        if (SomaUnretargetTests.EvalFolder() == null)
            Skip = "no SOMA eval clips: set SA_SOMA_EVAL to a folder of *.json clips or regenerate testdata/eval-only as described in docs/soma.md; NVIDIA evaluation-derived data is never committed";
    }
}

public sealed class SomaUnretargetTests
{
    const int Bones = CanonicalHumanoid.BoneCount;
    const int Joints = SomaRig.ModelJointCount;
    static readonly int[] NeckChain = { SomaRig.Neck2, SomaRig.Head, 7, SomaRig.LeftEye, SomaRig.RightEye };
    readonly ITestOutputHelper output;
    public SomaUnretargetTests(ITestOutputHelper output) => this.output = output;

    internal static string? EvalFolder()
    {
        var configured = Environment.GetEnvironmentVariable("SA_SOMA_EVAL");
        if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured) && Directory.GetFiles(configured, "*.json").Length > 0) return configured;
        return SomaEvalDataTests.Folder();
    }

    sealed class Report
    {
        public float LocalAngle, WorldAngle, HipsPosition, ModelPosition, Neck1Local, Neck2Local, NeckChainPosition, DroppedTranslation;
        public string Line => $"local rotation {LocalAngle:F5} deg, world rotation {WorldAngle:F5} deg, hips position {HipsPosition * 1000f:F4} mm, " +
            $"model position {ModelPosition * 1000f:F4} mm; lossy: Neck1 local {Neck1Local:F3} deg, Neck2 local {Neck2Local:F3} deg, " +
            $"Neck2/Head/Jaw/eye position {NeckChainPosition * 1000f:F3} mm, dropped data bone translation {DroppedTranslation * 1000f:F3} mm";
    }

    static float Angle(Quaternion a, Quaternion b)
    {
        a = Quaternion.Normalize(a);
        b = Quaternion.Normalize(b);
        double x = a.W * (double)b.X - a.X * (double)b.W - a.Y * (double)b.Z + a.Z * (double)b.Y;
        double y = a.W * (double)b.Y + a.X * (double)b.Z - a.Y * (double)b.W - a.Z * (double)b.X;
        double z = a.W * (double)b.Z - a.X * (double)b.Y + a.Y * (double)b.X - a.Z * (double)b.W;
        double w = a.W * (double)b.W + a.X * (double)b.X + a.Y * (double)b.Y + a.Z * (double)b.Z;
        return (float)(2.0 * Math.Atan2(Math.Sqrt(x * x + y * y + z * z), Math.Abs(w)) * 180.0 / Math.PI);
    }

    static Pose[] Frame(float[] poses, int frame)
    {
        var model = new Pose[Joints];
        for (var j = 0; j < Joints; j++) model[j] = SomaFrame.Read(poses, frame, j, Joints);
        return model;
    }

    static RigidPose Heading(Pose[] model, SomaSpace space)
    {
        var source = new Pose[SomaRig.SourceCount];
        Assert.Equal(AnimationError.None, SomaFrame.ToSource(model, space, source, out var heading));
        return heading;
    }

    static Pose[] Expected(Pose[] model, SomaSpace space, RigidPose first)
    {
        var expected = new Pose[Joints];
        for (var j = 0; j < Joints; j++)
            expected[j] = new Pose { Position = SomaRig.IsMarker(j) ? model[j].Position : SomaRig.RestTranslation(j), Rotation = Quaternion.Normalize(model[j].Rotation), Scale = Float3.One };
        var hips = SomaMirror.Local(SomaAxes.RootToModel(model[SomaRig.Hips], space));
        var inverse = Quaternion.InverseUnit(Quaternion.Normalize(first.Rotation));
        var local = new Pose
        {
            Position = Quaternion.Rotate(inverse, hips.Position - first.Position),
            Rotation = Quaternion.Normalize(Quaternion.Multiply(inverse, Quaternion.Normalize(hips.Rotation))),
            Scale = Float3.One
        };
        expected[SomaRig.Hips] = SomaMirror.Local(local);
        return expected;
    }

    static (Quaternion[] world, Float3[] position) Fk(Pose[] local)
    {
        var world = new Quaternion[Joints];
        var position = new Float3[Joints];
        for (var j = 0; j < Joints; j++)
        {
            var parent = SomaRig.Parent(j);
            if (parent < 0 && j != SomaRig.Hips) { world[j] = Quaternion.Identity; continue; }
            world[j] = parent < 0 ? Quaternion.Normalize(local[j].Rotation) : Quaternion.Normalize(Quaternion.Multiply(world[parent], local[j].Rotation));
            position[j] = parent < 0 ? local[j].Position : position[parent] + Quaternion.Rotate(world[parent], local[j].Position);
        }
        return (world, position);
    }

    static void Compare(Pose[] expected, Pose[] actual, Pose[] data, Report report)
    {
        var (expectedWorld, expectedPosition) = Fk(expected);
        var (actualWorld, actualPosition) = Fk(actual);
        for (var j = 0; j < Joints; j++)
        {
            if (SomaRig.IsMarker(j)) continue;
            var local = Angle(expected[j].Rotation, actual[j].Rotation);
            if (j == SomaRig.Neck1) report.Neck1Local = Math.Max(report.Neck1Local, local);
            else if (j == SomaRig.Neck2) report.Neck2Local = Math.Max(report.Neck2Local, local);
            else report.LocalAngle = Math.Max(report.LocalAngle, local);
            if (j != SomaRig.Neck1) report.WorldAngle = Math.Max(report.WorldAngle, Angle(expectedWorld[j], actualWorld[j]));
            var position = (expectedPosition[j] - actualPosition[j]).Length;
            if (Array.IndexOf(NeckChain, j) >= 0) report.NeckChainPosition = Math.Max(report.NeckChainPosition, position);
            else if (j != SomaRig.Neck1) report.ModelPosition = Math.Max(report.ModelPosition, position);
            if (j != SomaRig.Hips) report.DroppedTranslation = Math.Max(report.DroppedTranslation, (data[j].Position - SomaRig.RestTranslation(j)).Length);
        }
        report.HipsPosition = Math.Max(report.HipsPosition, (expected[SomaRig.Hips].Position - actual[SomaRig.Hips].Position).Length);
    }

    static Report RoundTrip(SomaRetarget retarget, float[] poses, int frames, SomaSpace space)
    {
        var (samples, root) = Convert(retarget, poses, frames, space);
        var first = Heading(Frame(poses, 0), space);
        var report = new Report();
        var soma = new Pose[Joints];
        for (var f = 0; f < frames; f++)
        {
            var model = Frame(poses, f);
            Assert.Equal(AnimationError.None, SomaUnretarget.ToSoma(retarget, samples.AsSpan(f * Bones, Bones), root[f], soma));
            Compare(Expected(model, space, first), soma, model, report);
        }
        return report;
    }

    static Quaternion RandomRotation(Random random, float maxDegrees)
    {
        var axis = new Float3((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f);
        if (axis.Length < 1e-3f) axis = new Float3(0f, 1f, 0f);
        return Quaternion.AxisAngle(axis / axis.Length, ((float)random.NextDouble() * 2f - 1f) * maxDegrees * MathF.PI / 180f);
    }

    static Pose[] RandomSoma(Random random, bool bendNeck1)
    {
        var model = new Pose[Joints];
        for (var j = 0; j < Joints; j++)
        {
            var bind = SomaRig.BindLocal(j);
            var rotation = SomaRig.IsMarker(j) ? Quaternion.Identity
                : j == SomaRig.Neck1 && !bendNeck1 ? bind.Rotation
                : Quaternion.Normalize(Quaternion.Multiply(bind.Rotation, RandomRotation(random, j == SomaRig.Hips ? 180f : 90f)));
            var position = j == SomaRig.Hips
                ? new Float3((float)random.NextDouble() * 4f - 2f, 0.6f + (float)random.NextDouble() * 0.5f, (float)random.NextDouble() * 4f - 2f)
                : SomaRig.RestTranslation(j);
            model[j] = new Pose { Position = position, Rotation = rotation, Scale = Float3.One };
        }
        return model;
    }

    [Fact]
    public void SyntheticSomaRoundTripsThroughSaForEveryMappedJoint()
    {
        var retarget = Retarget();
        var random = new Random(31);
        var frames = new Pose[24][];
        for (var f = 0; f < frames.Length; f++) frames[f] = RandomSoma(random, false);
        var report = RoundTrip(retarget, Pack(frames), frames.Length, SomaSpace.ModelYUp);
        output.WriteLine($"synthetic, Neck1 at bind: {report.Line}");
        Assert.True(report.LocalAngle <= 0.1f && report.Neck1Local <= 0.1f && report.Neck2Local <= 0.1f && report.WorldAngle <= 0.1f, report.Line);
        Assert.True(report.HipsPosition <= 0.001f && report.ModelPosition <= 0.001f && report.NeckChainPosition <= 0.001f, report.Line);
    }

    [Fact]
    public void BentNeck1FoldsIntoNeck2AndKeepsEveryWorldRotationExceptNeck1()
    {
        var retarget = Retarget();
        var random = new Random(53);
        var frames = new Pose[24][];
        for (var f = 0; f < frames.Length; f++) frames[f] = RandomSoma(random, true);
        var report = RoundTrip(retarget, Pack(frames), frames.Length, SomaSpace.ModelYUp);
        output.WriteLine($"synthetic, Neck1 bent up to 90 deg: {report.Line}");
        Assert.True(report.LocalAngle <= 0.1f && report.WorldAngle <= 0.1f, report.Line);
        Assert.True(report.HipsPosition <= 0.001f && report.ModelPosition <= 0.001f, report.Line);
        Assert.True(report.Neck1Local > 1f && report.Neck2Local > 1f);
    }

    [UnretargetEvalFact]
    public void EvalClipsRoundTripThroughSa()
    {
        var retarget = Retarget();
        foreach (var file in Directory.GetFiles(EvalFolder()!, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            var root = json.RootElement;
            var frames = root.GetProperty("frames").GetInt32();
            var space = root.GetProperty("space").GetString() == "runtime-zup-mujoco" ? SomaSpace.RuntimeZUp : SomaSpace.ModelYUp;
            var poses = root.GetProperty("poses").EnumerateArray().Select(e => e.GetSingle()).ToArray();
            var report = RoundTrip(retarget, poses, frames, space);
            output.WriteLine($"{Path.GetFileName(file)}: frames {frames}, {report.Line}");
            Assert.True(report.LocalAngle <= 0.1f && report.WorldAngle <= 0.1f, report.Line);
            Assert.True(report.HipsPosition <= 0.001f && report.ModelPosition <= 0.001f, report.Line);
        }
    }

    [Fact]
    public void SaCalibrationPoseMapsToTheSomaBindPose()
    {
        var retarget = Retarget();
        var aPose = new Pose[Bones];
        for (var bone = 0; bone < Bones; bone++) aPose[bone] = retarget.Rows[bone].Calibration;
        var soma = new Pose[Joints];
        Assert.Equal(AnimationError.None, SomaUnretarget.ToSoma(retarget, aPose, soma));
        var bind = BindModel();
        for (var j = 0; j < Joints; j++)
            if (SomaRig.IsMarker(j)) bind[j] = new Pose { Position = SomaRig.RestTranslation(j), Rotation = Quaternion.Identity, Scale = Float3.One };
        var expected = Expected(bind, SomaSpace.ModelYUp, Heading(bind, SomaSpace.ModelYUp));
        var worst = 0f;
        for (var j = 0; j < Joints; j++)
        {
            worst = Math.Max(worst, Angle(expected[j].Rotation, soma[j].Rotation));
            Assert.True((expected[j].Position - soma[j].Position).Length < 1e-3f, SomaRig.Name(j));
        }
        output.WriteLine($"A-pose to SOMA bind: worst local rotation {worst:F5} deg, hips height {soma[SomaRig.Hips].Position.Y:F5} m, bind hips height {SomaRig.BindPosition(SomaRig.Hips).Y:F5} m");
        Assert.True(worst <= 0.1f);
        Assert.True(Math.Abs(soma[SomaRig.Hips].Position.Y - SomaRig.BindPosition(SomaRig.Hips).Y) < 1e-3f);
    }

    [Fact]
    public void SaReferencePoseSurvivesReverseThenForward()
    {
        var retarget = Retarget();
        var soma = new Pose[Joints];
        Assert.Equal(AnimationError.None, SomaUnretarget.ToSoma(retarget, retarget.TargetReference, soma));
        var (samples, _) = Convert(retarget, Pack(soma), 1);
        var before = Model(retarget, retarget.TargetReference);
        var after = Model(retarget, samples);
        var worstAngle = 0f;
        for (var bone = 1; bone < Bones; bone++)
            if (retarget.Rows[bone].Source > 0) worstAngle = Math.Max(worstAngle, Angle(before[bone].Rotation, after[bone].Rotation));
        var hips = (before[CanonicalHumanoid.Hips].Position - after[CanonicalHumanoid.Hips].Position).Length;
        output.WriteLine($"SA reference to SOMA to SA: worst mapped model rotation {worstAngle:F5} deg, hips {hips * 1000f:F4} mm");
        Assert.True(worstAngle <= 0.1f && hips <= 0.001f);
    }

    [Fact]
    public void InvalidInputReturnsAnErrorAndNeverThrows()
    {
        var retarget = Retarget();
        var good = (Pose[])retarget.TargetReference.Clone();
        var soma = new Pose[Joints];
        Assert.Equal(AnimationError.None, SomaUnretarget.ToSoma(retarget, good, soma));
        Assert.NotEqual(AnimationError.None, SomaUnretarget.ToSoma(null!, good, soma));
        Assert.NotEqual(AnimationError.None, SomaUnretarget.ToSoma(retarget, good.AsSpan(0, Bones - 1), soma));
        Assert.NotEqual(AnimationError.None, SomaUnretarget.ToSoma(retarget, new Pose[Bones + 1], soma));
        Assert.NotEqual(AnimationError.None, SomaUnretarget.ToSoma(retarget, ReadOnlySpan<Pose>.Empty, soma));
        Assert.NotEqual(AnimationError.None, SomaUnretarget.ToSoma(retarget, good, new Pose[Joints - 1]));
        Assert.NotEqual(AnimationError.None, SomaUnretarget.ToSoma(retarget, good, new Pose[SomaRig.SkeletonJointCount]));
        Assert.NotEqual(AnimationError.None, SomaUnretarget.ToSoma(retarget, new Pose[Bones], soma));
        foreach (var bad in new[]
        {
            new Pose { Position = new Float3(float.NaN, 0f, 0f), Rotation = Quaternion.Identity, Scale = Float3.One },
            new Pose { Position = new Float3(0f, float.PositiveInfinity, 0f), Rotation = Quaternion.Identity, Scale = Float3.One },
            new Pose { Position = Float3.Zero, Rotation = new Quaternion(float.NaN, 0f, 0f, 1f), Scale = Float3.One },
            new Pose { Position = Float3.Zero, Rotation = new Quaternion(0f, 0f, 0f, 0f), Scale = Float3.One },
            new Pose { Position = Float3.Zero, Rotation = new Quaternion(0f, 0f, 0f, 2f), Scale = Float3.One }
        })
            foreach (var bone in new[] { 0, 1, 5, 20, 55 })
            {
                var input = (Pose[])good.Clone();
                input[bone] = bad;
                Assert.NotEqual(AnimationError.None, SomaUnretarget.ToSoma(retarget, input, soma));
            }
        Assert.NotEqual(AnimationError.None, SomaUnretarget.ToSoma(retarget, good, new RigidPose(new Float3(float.NaN, 0f, 0f), Quaternion.Identity), soma));
        Assert.NotEqual(AnimationError.None, SomaUnretarget.ToSoma(retarget, good, new RigidPose(Float3.Zero, new Quaternion(0f, 0f, 0f, 0f)), soma));
        var reference = new Pose[SomaRig.SkeletonJointCount];
        for (var j = 0; j < reference.Length; j++) reference[j] = Pose.Identity;
        Assert.NotEqual(AnimationError.None, SomaUnretarget.ToSkeleton(soma, reference.AsSpan(1), new Pose[SomaRig.SkeletonJointCount]));
        Assert.NotEqual(AnimationError.None, SomaUnretarget.ToSkeleton(soma.AsSpan(1), reference, new Pose[SomaRig.SkeletonJointCount]));
        Assert.NotEqual(AnimationError.None, SomaUnretarget.ToSkeleton(soma, reference, new Pose[Joints]));
        Assert.Equal(AnimationError.AliasedBuffers, SomaUnretarget.ToSkeleton(soma, reference, reference));
        var nan = (Pose[])reference.Clone();
        nan[40].Position = new Float3(float.NaN, 0f, 0f);
        Assert.NotEqual(AnimationError.None, SomaUnretarget.ToSkeleton(soma, nan, new Pose[SomaRig.SkeletonJointCount]));
        Assert.NotEqual(AnimationError.None, SomaUnretarget.Write(soma, new float[Joints * 7 - 1]));
        Assert.NotEqual(AnimationError.None, SomaUnretarget.Write(ReadOnlySpan<Pose>.Empty, new float[0]));
    }

    [Fact]
    public void SameInputGivesBitIdenticalOutput()
    {
        var retarget = Retarget();
        var random = new Random(97);
        var frames = new Pose[6][];
        for (var f = 0; f < frames.Length; f++) frames[f] = RandomSoma(random, true);
        var (samples, root) = Convert(retarget, Pack(frames), frames.Length);
        var a = new Pose[Joints];
        var b = new Pose[Joints];
        for (var j = 0; j < Joints; j++) b[j] = new Pose { Position = new Float3(float.NaN, 9f, 9f), Rotation = new Quaternion(1f, 2f, 3f, 4f), Scale = Float3.One };
        var first = new float[Joints * 7];
        var second = new float[Joints * 7];
        for (var f = 0; f < frames.Length; f++)
        {
            Assert.Equal(AnimationError.None, SomaUnretarget.ToSoma(retarget, samples.AsSpan(f * Bones, Bones), root[f], a));
            Assert.Equal(AnimationError.None, SomaUnretarget.ToSoma(Retarget(), samples.AsSpan(f * Bones, Bones), root[f], b));
            Assert.Equal(AnimationError.None, SomaUnretarget.Write(a, first));
            Assert.Equal(AnimationError.None, SomaUnretarget.Write(b, second));
            for (var i = 0; i < first.Length; i++) Assert.Equal(BitConverter.SingleToInt32Bits(first[i]), BitConverter.SingleToInt32Bits(second[i]));
        }
    }

    [Fact]
    public void SkeletonLayoutKeepsReferenceJointsAndTranslations()
    {
        var retarget = Retarget();
        var soma = new Pose[Joints];
        Assert.Equal(AnimationError.None, SomaUnretarget.ToSoma(retarget, retarget.TargetReference, soma));
        var reference = new Pose[SomaRig.SkeletonJointCount];
        for (var j = 0; j < reference.Length; j++)
            reference[j] = new Pose { Position = new Float3(0.01f * j, 0.002f, 0f), Rotation = Quaternion.AxisAngle(new Float3(0f, 0f, 1f), 0.01f * j), Scale = Float3.One };
        var skeleton = new Pose[SomaRig.SkeletonJointCount];
        Assert.Equal(AnimationError.None, SomaUnretarget.ToSkeleton(soma, reference, skeleton));
        var mapped = new bool[SomaRig.SkeletonJointCount];
        for (var j = 0; j < Joints; j++)
        {
            var index = SomaRig.SkeletonIndex(j);
            if (SomaRig.IsMarker(j)) continue;
            mapped[index] = true;
            Assert.True(Angle(soma[j].Rotation, skeleton[index].Rotation) < 1e-3f);
            var position = j == SomaRig.Hips ? soma[j].Position : reference[index].Position;
            Assert.True((position - skeleton[index].Position).Length < 1e-7f);
        }
        for (var k = 0; k < skeleton.Length; k++)
        {
            if (mapped[k]) continue;
            Assert.True(Angle(reference[k].Rotation, skeleton[k].Rotation) < 1e-3f);
            Assert.True((reference[k].Position - skeleton[k].Position).Length < 1e-7f);
        }
        var floats = new float[SomaRig.SkeletonJointCount * SomaFrame.FloatsPerJoint];
        Assert.Equal(AnimationError.None, SomaUnretarget.Write(skeleton, floats));
        for (var k = 0; k < skeleton.Length; k++)
        {
            Assert.Equal(skeleton[k].Position.X, floats[k * 7]);
            Assert.Equal(skeleton[k].Rotation.W, floats[k * 7 + 6]);
        }
    }
}
