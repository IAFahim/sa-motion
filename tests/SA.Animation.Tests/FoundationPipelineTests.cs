using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SA.Animation.Content;
using SA.Motion.Inertialization;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Animation.Tests;

public class FoundationPipelineTests
{
    sealed class Character
    {
        internal readonly RetargetBinding Binding;
        internal readonly RetargetRow[] Rows;
        internal readonly RigidPose[] CanonicalModel = new RigidPose[4];
        internal readonly RigidPose[] TargetModel;
        internal readonly Pose[] Raw;
        internal readonly Pose[] Output;
        internal readonly Pose[] Scratch;
        internal readonly BoneFrameState[] Bones;
        internal readonly BoneFrameState[] Staged;
        internal readonly PoseVelocity[] Velocity;
        internal readonly ClipContribution[] Previous = new ClipContribution[8];
        internal readonly LayerInput[] PreviousLayers = new LayerInput[4];
        internal PoseFrameState State;

        internal Character(int[] parents, Pose[] calibration, int[] map, float ratio, AssetId target)
        {
            Rows = new RetargetRow[parents.Length];
            var scratch = new RigidPose[parents.Length + 4];
            Assert.Equal(AnimationError.None, RetargetTable.Compile(SharedCodecTests.Family, target, SharedCodecTests.Parents, SharedCodecTests.Reference(), parents, calibration, map,
                1, 1, ratio, scratch, Rows, out Binding));
            TargetModel = new RigidPose[parents.Length]; Raw = new Pose[parents.Length]; Output = new Pose[parents.Length]; Scratch = new Pose[parents.Length];
            Bones = new BoneFrameState[parents.Length]; Staged = new BoneFrameState[parents.Length]; Velocity = new PoseVelocity[parents.Length];
        }
    }

    [Fact]
    public void EncodedSharedMotionsRetargetBlendInterruptAndSkinOnDifferentHierarchies()
    {
        var reference = SharedCodecTests.Reference();
        var bytes = SharedCodecTests.Encode();
        Assert.Equal(AnimationError.None, SharedClipReader.Load(bytes, SharedCodecTests.Family, reference, out var clip));
        var targetA = reference.Select(p => { p.Position *= 1.5f; return p; }).ToArray();
        var targetB = new[] { Pose.Identity, SharedCodecTests.At(new Float3(0, 1.2f, 0)), SharedCodecTests.At(new Float3(.15f, .2f, 0)),
            SharedCodecTests.At(new Float3(.3f, .4f, 0)), SharedCodecTests.At(new Float3(.8f, 0, 0)) };
        targetB[2].Rotation = RotationVector.Exp(new Float3(.2f, .3f, -.1f));
        targetB[3].Rotation = RotationVector.Exp(new Float3(-.3f, .1f, .4f));
        var characters = new[]
        {
            new Character(SharedCodecTests.Parents, targetA, [0, 1, 2, 3], 1.5f, new AssetId(10, 0, 0, 0)),
            new Character([-1, 0, 1, 2, 3], targetB, [0, 1, -1, 2, 3], 1.2f, new AssetId(20, 0, 0, 0))
        };
        var decoded = new Pose[4];
        var blended = new Pose[4];
        var contributions = new ClipContribution[1];
        var layers = new[] { new LayerInput(0, ClipMode.Override, 0, 1) };
        var rawBefore = new Pose[5];
        var rawAfter = new Pose[5];
        foreach (var character in characters)
        {
            Assert.Equal(AnimationError.None, RetargetFile.Encode(character.Binding, SharedCodecTests.Parents, reference, character.Rows, out var tableFile));
            Assert.Equal(AnimationError.None, RetargetFile.Load(tableFile, SharedCodecTests.Family, character.Binding.Target, SharedCodecTests.Parents, reference, out var binding, out var tableRows));
            Assert.Equal(character.Rows.Length, tableRows.Length);
            Assert.Equal(character.Binding.HeightRatio, binding.HeightRatio);
            for (var tick = 0; tick <= 60; tick++)
            {
                var time = tick * .01 + (tick < 15 ? 0 : .4);
                var frame = new AnimationFrame((ulong)tick, 1, tick < 15 ? 1u : 2u, tick * .01, .01f, .2f);
                contributions[0] = new ClipContribution(new ClipHandle(0, 1), time, 1);
                Assert.Equal(AnimationError.None, clip!.Sample(time, reference, decoded));
                Assert.Equal(AnimationError.None, CanonicalBlend.Evaluate(contributions, layers, decoded, [], [], reference, blended));
                Assert.Equal(AnimationError.None, RetargetTable.Apply(SharedCodecTests.Family, binding, SharedCodecTests.Parents, blended, tableRows, character.CanonicalModel, character.TargetModel, character.Raw));
                if (tick == 15)
                {
                    Assert.Equal(AnimationError.None, clip.Sample(time - .001, reference, decoded));
                    Assert.Equal(AnimationError.None, RetargetTable.Apply(SharedCodecTests.Family, binding, SharedCodecTests.Parents, decoded, tableRows, character.CanonicalModel, character.TargetModel, rawBefore));
                    Assert.Equal(AnimationError.None, clip.Sample(time + .001, reference, decoded));
                    Assert.Equal(AnimationError.None, RetargetTable.Apply(SharedCodecTests.Family, binding, SharedCodecTests.Parents, decoded, tableRows, character.CanonicalModel, character.TargetModel, rawAfter));
                    for (var i = 0; i < character.Raw.Length; i++) Assert.True(PoseVelocity.TryBetween(rawBefore[i], rawAfter[i], .002f, out character.Velocity[i]));
                }
                var switchPose = character.Output.ToArray();
                Assert.Equal(AnimationError.None, TimelinePose.Evaluate(frame, contributions, layers, true, character.Raw, character.Velocity,
                    ref character.State, character.Bones, character.Staged, character.Previous, character.PreviousLayers, character.Output, character.Scratch, out var action));
                if (tick == 15)
                {
                    Assert.Equal(FrameAction.Transitioned, action);
                    for (var i = 0; i < character.Output.Length; i++) Assert.InRange(PoseFixtures.Gap(switchPose[i], character.Output[i]), 0, 1e-5f);
                }
                for (var i = 2; i < character.Output.Length; i++) Assert.Equal(tableRows[i].Calibration.Position, character.Output[i].Position);
                if (tick >= 36) Assert.Equal(character.Raw, character.Output);
                var parents = tableRows.Select(x => x.Parent).ToArray();
                var models = new Affine[parents.Length];
                Hierarchy.Model(parents, character.Output, models);
                var palette = new Affine[parents.Length];
                Assert.Equal(AnimationError.None, SkinPalette.Build(models, Enumerable.Range(0, models.Length).ToArray(), Enumerable.Repeat(Affine.Identity, models.Length).ToArray(), Affine.Identity, palette));
                Assert.All(palette, p => Assert.True(p.C3.IsFinite));
            }
        }
        Assert.NotEqual(characters[0].Binding.Target, characters[1].Binding.Target);
        Assert.Equal(characters[0].Binding.Family, characters[1].Binding.Family);
    }

    [Fact]
    public void MeshFromModelAndOriginalBindAreBothNecessaryForBindReconstruction()
    {
        var model = PoseMath.Affine(SharedCodecTests.At(new Float3(10, 0, 0)));
        var bind = PoseMath.Affine(SharedCodecTests.At(new Float3(-8, 0, 0)));
        var meshFromModel = PoseMath.Affine(SharedCodecTests.At(new Float3(-2, 0, 0)));
        var palette = new Affine[1];
        Assert.Equal(AnimationError.None, SkinPalette.Build([model], [0], [bind], meshFromModel, palette));
        Assert.Equal(Affine.Identity, palette[0]);
    }

    [Fact]
    public void TamperedTableCorrectionsFailEvenWithARecomputedFileHash()
    {
        var reference = SharedCodecTests.Reference();
        var rows = new RetargetRow[4];
        var target = new AssetId(10, 0, 0, 0);
        Assert.Equal(AnimationError.None, RetargetTable.Compile(SharedCodecTests.Family, target, SharedCodecTests.Parents, reference, SharedCodecTests.Parents, reference,
            [0, 1, 2, 3], 1, 1, 1, new RigidPose[8], rows, out var binding));
        Assert.Equal(AnimationError.None, RetargetFile.Encode(binding, SharedCodecTests.Parents, reference, rows, out var bytes));
        Span<SacaChunk> directory = stackalloc SacaChunk[64];
        Assert.Equal(AnimationError.None, SacaFile.Verify(bytes, directory, out var count, out _));
        Assert.True(SacaContainer.Find(directory[..count], SacaContainer.Corrections, out var correction));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(correction.Offset + 16), BitConverter.SingleToInt32Bits(.5f));
        SharedCodecTests.Stamp(bytes);
        Assert.Equal(AnimationError.InvalidTable, RetargetFile.Load(bytes, SharedCodecTests.Family, target, SharedCodecTests.Parents, reference, out _, out _));
    }

    [Fact]
    public void PayloadIdentityIgnoresQuaternionSignsButIncludesRootAndContactSemantics()
    {
        var motion = SharedCodecTests.Motion(18);
        Assert.Equal(AnimationError.None, ContentIdentity.Motion(SharedCodecTests.Family, motion.Samples, 4, 18, 1, false, RootPolicy.Planar, motion.Root, [], [], out var first));
        var flipped = motion.Samples.Select(p => { p.Rotation = -p.Rotation; return p; }).ToArray();
        Assert.Equal(AnimationError.None, ContentIdentity.Motion(SharedCodecTests.Family, flipped, 4, 18, 1, false, RootPolicy.Planar, motion.Root, [], [], out var second));
        Assert.Equal(first, second);
        Assert.Equal(AnimationError.None, ContentIdentity.Motion(SharedCodecTests.Family, motion.Samples, 4, 18, 1, true, RootPolicy.Planar, motion.Root, [], [], out second));
        Assert.NotEqual(first, second);
        Assert.Equal(AnimationError.None, ContentIdentity.Motion(SharedCodecTests.Family, motion.Samples, 4, 18, 1, false, RootPolicy.Planar, motion.Root, [], new float[72], out second));
        Assert.NotEqual(first, second);
        var expected = Convert.FromHexString("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        Assert.Equal(new AssetId(BinaryPrimitives.ReadUInt64LittleEndian(expected), BinaryPrimitives.ReadUInt64LittleEndian(expected.AsSpan(8)), BinaryPrimitives.ReadUInt64LittleEndian(expected.AsSpan(16)), BinaryPrimitives.ReadUInt64LittleEndian(expected.AsSpan(24))), ContentIdentity.Hash([]));
    }

    [Fact]
    public void PublicHotDataIsUnmanagedAndCoreHasNoUnityOrContentDependency()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<RetargetRow>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<RetargetBinding>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<ClipContribution>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<BoneFrameState>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<PoseFrameState>());
        Assert.Equal(32, Marshal.SizeOf<AssetId>());
        Assert.Equal(28, Marshal.SizeOf<RigidPose>());
        Assert.Equal(48, Marshal.SizeOf<PoseDecay>());
        Assert.DoesNotContain(typeof(RetargetTable).Assembly.GetReferencedAssemblies(), a => a.Name!.StartsWith("Unity") || a.Name == "SA.Animation.Content");
    }

    [Fact]
    public void UniqueRigIdentityPathPreservesAnimatedNonHipTranslations()
    {
        var poses = SharedCodecTests.Reference();
        poses[2].Position.X = 3;
        var output = new Pose[4];
        Assert.Equal(AnimationError.None, IdentityRetarget.Apply(SharedCodecTests.Family, SharedCodecTests.Family, SharedCodecTests.Parents, poses, new RigidPose[4], output));
        Assert.Equal(poses, output);
        Assert.Equal(AnimationError.FamilyMismatch, IdentityRetarget.Apply(SharedCodecTests.Family, new AssetId(8, 8, 8, 8), SharedCodecTests.Parents, poses, new RigidPose[4], output));
    }

    [Fact]
    public void CompleteWarmEvaluationAllocatesZeroBytes()
    {
        var reference = SharedCodecTests.Reference();
        Assert.Equal(AnimationError.None, SharedClipReader.Load(SharedCodecTests.Encode(), SharedCodecTests.Family, reference, out var clip));
        var character = new Character(SharedCodecTests.Parents, reference, [0, 1, 2, 3], 1, new AssetId(10, 0, 0, 0));
        var decoded = new Pose[4];
        var canonical = new Pose[4];
        var contributions = new ClipContribution[1];
        var layers = new[] { new LayerInput(0, ClipMode.Override, 0, 1) };
        AnimationError Step(int tick)
        {
            var time = tick * .001;
            contributions[0] = new ClipContribution(new ClipHandle(0, 1), time, 1);
            var error = clip!.Sample(time, reference, decoded);
            if (error != AnimationError.None) return error;
            error = CanonicalBlend.Evaluate(contributions, layers, decoded, [], [], reference, canonical);
            if (error != AnimationError.None) return error;
            error = RetargetTable.Apply(SharedCodecTests.Family, character.Binding, SharedCodecTests.Parents, canonical, character.Rows, character.CanonicalModel, character.TargetModel, character.Raw);
            if (error != AnimationError.None) return error;
            return TimelinePose.Evaluate(new AnimationFrame((ulong)tick, 1, 1, time, .001f), contributions, layers, true, character.Raw, character.Velocity,
                ref character.State, character.Bones, character.Staged, character.Previous, character.PreviousLayers, character.Output, character.Scratch, out _);
        }
        for (var tick = 0; tick < 100; tick++) Assert.Equal(AnimationError.None, Step(tick));
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = AnimationError.None;
        for (var tick = 100; tick < 1100; tick++) result = Step(tick);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(AnimationError.None, result);
        Assert.Equal(0, allocated);
        Assert.True(character.Output[1].Position.IsFinite);
    }
}
