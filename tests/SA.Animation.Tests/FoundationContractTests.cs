using System.Security.Cryptography;
using System.Text.Json;
using SA.Animation.Content;
using SA.Motion.Inertialization;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Animation.Tests;

public class FoundationContractTests
{
    static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Theory]
    [InlineData("three-bone-packed.saclip", "a85a73b5f115cbdf478ef10b1f995d8f3016a8a3fc4ef44c5a5eee79abf59ef8")]
    [InlineData("three-bone-raw.saclip", "7ba9609c2f715bf1445ca634c8d1c751817cfdfbddfbed4660e790c1bf071a2a")]
    public void CommittedGoldenMotionBytesDecodeIndependentlyOfTheWriter(string name, string sha)
    {
        var bytes = Fixture(name);
        Assert.Equal(sha, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        var reference = new[] { Pose.Identity, SharedCodecTests.At(new Float3(0, 1, 0)), SharedCodecTests.At(new Float3(1, 0, 0)) };
        Assert.Equal(AnimationError.None, SharedClipReader.Load(bytes, SharedCodecTests.Family, reference, out var clip));
        var pose = new Pose[3];
        Assert.Equal(AnimationError.None, clip!.Sample(.5, reference, pose));
        Assert.InRange(pose[1].Position.Y, 1.04999f, 1.05001f);
        Assert.InRange(RotationVector.Log(pose[2].Rotation).Z, .2999f, .3001f);
        Assert.Equal(3, clip.SampleCount);
        Assert.Equal(1, clip.Duration);
    }

    [Fact]
    public void CommittedGoldenTableUsesItsOriginalCompatibilityIds()
    {
        var bytes = Fixture("three-bone-target.saret");
        Assert.Equal("dca879c5183cf898ab1bb18ba1090138f5658301de65144e69d752747027ccc0", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        var reference = new[] { Pose.Identity, SharedCodecTests.At(new Float3(0, 1, 0)), SharedCodecTests.At(new Float3(1, 0, 0)) };
        Assert.Equal(AnimationError.None, RetargetFile.Load(bytes, SharedCodecTests.Family, new AssetId(10, 0, 0, 0), [-1, 0, 1], reference, out var binding, out var rows));
        Assert.Equal(2, binding.HeightRatio);
        Assert.Equal(3, rows.Length);
        Assert.Equal(new Float3(2, 0, 0), rows[2].Calibration.Position);
    }

    [Fact]
    public void ExpandedDraftDescriptorMatchesTheCompiledReferenceExactly()
    {
        using var doc = JsonDocument.Parse(Fixture("humanoid-v1.safamily"));
        Assert.Equal("draft-awaiting-Unity-Avatar-conformance", doc.RootElement.GetProperty("status").GetString());
        var bones = doc.RootElement.GetProperty("bones");
        var parents = new int[56];
        var poses = new Pose[56];
        Assert.Equal(AnimationError.None, CanonicalHumanoid.WriteReference(parents, poses));
        Assert.Equal(56, bones.GetArrayLength());
        Assert.Equal(15, doc.RootElement.GetProperty("requiredBones").GetArrayLength());
        for (var i = 0; i < 56; i++)
        {
            Assert.Equal(parents[i], bones[i].GetProperty("parent").GetInt32());
            var xyz = bones[i].GetProperty("referenceLocal").GetProperty("position");
            Assert.Equal(new Float3(xyz[0].GetSingle(), xyz[1].GetSingle(), xyz[2].GetSingle()), poses[i].Position);
        }
    }

    [Fact]
    public void AssignmentMatchesExhaustiveOptimaOnSmallConflictingMaps()
    {
        var random = new Random(2303);
        for (var trial = 0; trial < 100; trial++)
        {
            var roles = random.Next(1, 6);
            var bones = random.Next(1, 7);
            var scores = Enumerable.Range(0, roles * bones).Select(_ => random.Next(0, 101)).ToArray();
            long Best(int role, int used)
            {
                if (role == roles) return 0;
                var best = Best(role + 1, used);
                for (var bone = 0; bone < bones; bone++)
                    if ((used & 1 << bone) == 0 && scores[role * bones + bone] > 0)
                        best = Math.Max(best, scores[role * bones + bone] + Best(role + 1, used | 1 << bone));
                return best;
            }
            Assert.Equal(AnimationError.None, MappingAssignment.Solve(scores, roles, bones, 15, out var result));
            Assert.Equal(Best(0, 0), result!.Score);
            var assigned = result.BoneOfRole.Where(x => x >= 0).ToArray();
            Assert.Equal(assigned.Length, assigned.Distinct().Count());
        }
    }

    [Fact]
    public void LateSmoothingFailureDoesNotCommitEarlierBonesOrHistory()
    {
        var state = new PoseFrameState();
        var bones = new BoneFrameState[2];
        var staged = new BoneFrameState[2];
        var previous = new ClipContribution[1];
        var previousLayers = new LayerInput[1];
        var output = new Pose[2];
        var scratch = new Pose[2];
        var contribution = new[] { new ClipContribution(new ClipHandle(0, 1), 0, 1) };
        var layers = new[] { new LayerInput(0, ClipMode.Override, 0, 1) };
        var destination = new[] { Pose.Identity, Pose.Identity };
        Assert.Equal(AnimationError.None, TimelinePose.Evaluate(new AnimationFrame(0, 1, 1, 0, .01f), contribution, layers, true, destination, [], ref state, bones, staged, previous, previousLayers, output, scratch, out _));
        state.Duration = 1;
        bones[1].Decay = new PoseDecay(new Float3(float.NaN, 0, 0), Float3.Zero, Float3.Zero, Float3.Zero);
        var beforeState = state;
        var beforeHistory = bones[0].History;
        var beforeOutput = output.ToArray();
        contribution[0] = new ClipContribution(new ClipHandle(0, 1), .01, 1);
        Assert.Equal(AnimationError.InvalidFrame, TimelinePose.Evaluate(new AnimationFrame(1, 1, 1, .01, .01f), contribution, layers, true, destination, [], ref state, bones, staged, previous, previousLayers, output, scratch, out _));
        Assert.Equal(beforeState, state);
        Assert.Equal(beforeHistory, bones[0].History);
        Assert.Equal(beforeOutput, output);
        Assert.Equal((uint)2, staged[0].History.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(17)]
    [InlineData(18)]
    public void PoseAndShortClipFilesSupportEmptyOrOverlappingSegments(int count)
    {
        var reference = SharedCodecTests.Reference();
        var bytes = SharedCodecTests.Encode(count);
        Assert.Equal(AnimationError.None, SharedClipReader.Load(bytes, SharedCodecTests.Family, reference, out var clip));
        Assert.Equal(AnimationError.None, clip!.Sample(count == 1 ? 0 : 1, reference, new Pose[4]));
        Assert.Equal(SampleLocator.SegmentCount(count), clip.Segments.Length);
    }

    [Fact]
    public void BlendArithmeticOverflowReturnsADiagnostic()
    {
        var extreme = SharedCodecTests.At(new Float3(float.MaxValue, 0, 0));
        var contributions = new[] { new ClipContribution(new ClipHandle(0, 1), 0, 1), new ClipContribution(new ClipHandle(1, 1), 0, 1) };
        Assert.Equal(AnimationError.InvalidFrame, CanonicalBlend.Evaluate(contributions, [new LayerInput(0, ClipMode.Override, 0, 2)], [extreme, extreme], [], [], [Pose.Identity], new Pose[1]));
    }

    [Fact]
    public void ReusedTicksStillRejectInputSnapshotAliasing()
    {
        var state = new PoseFrameState();
        var bones = new BoneFrameState[1];
        var staged = new BoneFrameState[1];
        var previous = new ClipContribution[1];
        var previousLayers = new LayerInput[1];
        var output = new Pose[1];
        var scratch = new Pose[1];
        var contribution = new[] { new ClipContribution(new ClipHandle(0, 1), 0, 1) };
        var layers = new[] { new LayerInput(0, ClipMode.Override, 0, 1) };
        var frame = new AnimationFrame(0, 1, 1, 0, .01f);
        Assert.Equal(AnimationError.None, TimelinePose.Evaluate(frame, contribution, layers, true, [Pose.Identity], [], ref state, bones, staged, previous, previousLayers, output, scratch, out _));
        Assert.Equal(AnimationError.AliasedBuffers, TimelinePose.Evaluate(frame, previous, layers, true, [Pose.Identity], [], ref state, bones, staged, previous, previousLayers, output, scratch, out _));
    }
}
