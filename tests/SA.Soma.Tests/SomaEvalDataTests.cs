using System.Text.Json;
using SA.Animation;
using SA.Animation.Content;
using SA.Motion.Numerics;
using SA.Soma;
using Xunit.Abstractions;
using static SA.Soma.Tests.SomaFixtures;

namespace SA.Soma.Tests;

public sealed class EvalDataFactAttribute : FactAttribute
{
    public EvalDataFactAttribute()
    {
        if (SomaEvalDataTests.Folder() == null)
            Skip = "testdata/eval-only/*.json absent: NVIDIA evaluation-derived SOMA clips are never committed; regenerate them as described in docs/soma.md";
    }
}

public sealed class SomaEvalDataTests
{
    static readonly Float3 ToeSole = new(0f, -0.035f, 0f);
    readonly ITestOutputHelper output;
    public SomaEvalDataTests(ITestOutputHelper output) => this.output = output;

    internal static string? Folder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SA.Motion.sln"))) directory = directory.Parent;
        if (directory == null) return null;
        var folder = Path.Combine(directory.FullName, "testdata", "eval-only");
        return Directory.Exists(folder) && Directory.GetFiles(folder, "*.json").Length > 0 ? folder : null;
    }

    [EvalDataFact]
    public void EvalClipsRoundTripWithFeetOnTheGround()
    {
        var folder = Folder()!;
        var retarget = Retarget();
        foreach (var file in Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            var rootElement = json.RootElement;
            var frames = rootElement.GetProperty("frames").GetInt32();
            var fps = rootElement.GetProperty("fps").GetSingle();
            var space = rootElement.GetProperty("space").GetString() == "runtime-zup-mujoco" ? SomaSpace.RuntimeZUp : SomaSpace.ModelYUp;
            var poses = rootElement.GetProperty("poses").EnumerateArray().Select(e => e.GetSingle()).ToArray();
            var soma = rootElement.GetProperty("contacts").EnumerateArray().Select(e => e.GetSingle()).ToArray();
            var (samples, root) = Convert(retarget, poses, frames, space);
            var contacts = new float[frames * 4];
            Assert.Equal(AnimationError.None, SomaContacts.Merge(soma, contacts));

            var worstDirection = 0f;
            var model = new Pose[SomaRig.ModelJointCount];
            for (var f = 0; f < frames; f++)
            {
                for (var j = 0; j < model.Length; j++) model[j] = SomaFrame.Read(poses, f, j, SomaRig.ModelJointCount);
                worstDirection = Math.Max(worstDirection, WorstBoneDirection(retarget, model, samples.AsSpan(f * CanonicalHumanoid.BoneCount, CanonicalHumanoid.BoneCount), space));
            }

            Assert.Equal(AnimationError.None, SomaClip.Encode(retarget, samples, root, contacts, frames, fps, out var bytes, out var report));
            Assert.Equal(AnimationError.None, SharedClipReader.Load(bytes, Family, retarget.TargetReference, out var clip));
            Assert.Equal(contacts, clip!.ContactWeights);

            var decoded = new Pose[CanonicalHumanoid.BoneCount];
            var worstAngle = 0f;
            var worstPosition = 0f;
            var worstContactSole = float.NegativeInfinity;
            var lowestSole = float.PositiveInfinity;
            var planted = 0;
            var worstMidSole = float.NegativeInfinity;
            for (var f = 0; f < frames; f++)
            {
                Assert.Equal(AnimationError.None, clip.Sample(f / (double)fps, retarget.TargetReference, decoded));
                var a = Model(retarget, samples.AsSpan(f * CanonicalHumanoid.BoneCount, CanonicalHumanoid.BoneCount));
                var b = Model(retarget, decoded);
                for (var bone = 0; bone < a.Length; bone++)
                {
                    worstAngle = Math.Max(worstAngle, AngleDegrees(a[bone].Rotation, b[bone].Rotation));
                    worstPosition = Math.Max(worstPosition, (a[bone].Position - b[bone].Position).Length);
                }
                for (var foot = 0; foot < 2; foot++)
                {
                    var bone = foot == 0 ? 50 : 54;
                    var midSole = (b[bone].Position + Quaternion.Rotate(b[bone].Rotation, SomaClip.SoleOffset)).Y;
                    var sole = Math.Min(midSole, (b[bone + 1].Position + Quaternion.Rotate(b[bone + 1].Rotation, ToeSole)).Y);
                    lowestSole = Math.Min(lowestSole, sole);
                    if (contacts[f * 4 + foot] >= 0.5f) worstMidSole = Math.Max(worstMidSole, midSole);
                    if (contacts[f * 4 + foot] < 0.5f) continue;
                    planted++;
                    worstContactSole = Math.Max(worstContactSole, sole);
                }
            }
            output.WriteLine($"{Path.GetFileName(file)}: frames {frames}, bytes {bytes.Length}, encoder error {report.Error.AngleRadians * 180f / MathF.PI:F4} deg / {report.Error.PositionMetres * 1000f:F3} mm, " +
                $"readback {worstAngle:F4} deg / {worstPosition * 1000f:F3} mm, worst bone direction {worstDirection:F4} deg, " +
                $"planted foot-frames {planted}, highest planted support point {worstContactSole * 100f:F2} cm, highest planted family sole point {worstMidSole * 100f:F2} cm, lowest sole {lowestSole * 100f:F2} cm, " +
                $"root travel {root[frames - 1].Position.X:F3},{root[frames - 1].Position.Z:F3} m, height ratio {retarget.HeightRatio:F4}");
            Assert.True(worstAngle <= 0.15f && worstPosition <= 0.002f);
            Assert.True(worstDirection < 2f);
            Assert.True(planted > 0 && worstContactSole < 0.025f);
        }
    }
}
