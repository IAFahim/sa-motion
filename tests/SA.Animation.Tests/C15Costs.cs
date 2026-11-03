using System;
using System.Diagnostics;
using SA.Animation;
using SA.Animation.Content;
using SA.Motion.Inertialization;
using SA.Motion.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace SA.Animation.Tests;

public sealed class C15Costs
{
    const int CanonicalBones = 56;
    const int ExtraBones = 11;
    const int TargetBones = CanonicalBones + ExtraBones;
    const int Samples = 61;
    const double Duration = 2.0;

    readonly ITestOutputHelper output;

    public C15Costs(ITestOutputHelper output)
    {
        this.output = output;
    }

    sealed class Rig
    {
        internal int[] Parents = Array.Empty<int>();
        internal Pose[] Reference = Array.Empty<Pose>();
        internal AssetId Family;
        internal SharedClip Clip = null!;
        internal int[] TargetParents = Array.Empty<int>();
        internal RetargetRow[] Rows = Array.Empty<RetargetRow>();
        internal RetargetBinding Binding;
        internal int TargetBones;
    }

    sealed class Buffers
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

    static Rig BuildRig(int targetBones)
    {
        var parents = new int[CanonicalBones];
        var reference = new Pose[CanonicalBones];
        Assert.Equal(AnimationError.None, CanonicalHumanoid.WriteReference(parents, reference));
        var names = new string[CanonicalBones];
        for (var i = 0; i < CanonicalBones; i++)
        {
            names[i] = "B" + i.ToString("D2");
        }
        Assert.Equal(AnimationError.None, ContentIdentity.Skeleton("Humanoid", 1, names, parents, reference, out var family));
        Assert.False(family.IsEmpty);
        var samples = new Pose[Samples * CanonicalBones];
        var root = new RigidPose[Samples];
        var up = new Float3(0f, 1f, 0f);
        var outOfPlane = new Float3(0f, 0f, 1f);
        for (var i = 0; i < Samples; i++)
        {
            var t = i / (float)(Samples - 1);
            reference.CopyTo(samples, i * CanonicalBones);
            samples[i * CanonicalBones + 1].Position.Y += 0.03f * MathF.Sin(t * Scalar.TwoPi);
            samples[i * CanonicalBones + 11].Rotation = Quaternion.AxisAngle(outOfPlane, 0.25f * MathF.Sin(t * Scalar.TwoPi));
            samples[i * CanonicalBones + 30].Rotation = Quaternion.AxisAngle(outOfPlane, -0.25f * MathF.Sin(t * Scalar.TwoPi));
            root[i] = new RigidPose(new Float3(0f, 0f, t), Quaternion.AxisAngle(up, t * Scalar.Pi * 0.5f));
        }
        Assert.Equal(AnimationError.None, SharedClipEncoder.Encode(family, parents, reference, samples, Samples, Duration, true, RootPolicy.Planar, root,
            Array.Empty<Pose>(), Array.Empty<float>(), Array.Empty<PoseMarker>(), false, CompressionLimits.Default, out var bytes, out _));
        Assert.Equal(AnimationError.None, SharedClipReader.Load(bytes, family, reference, out var clip));
        Assert.NotNull(clip);
        var targetParents = new int[targetBones];
        Array.Copy(parents, targetParents, CanonicalBones);
        var calibration = new Pose[targetBones];
        Array.Copy(reference, calibration, CanonicalBones);
        var sourceMap = new int[targetBones];
        for (var i = 0; i < CanonicalBones; i++)
        {
            sourceMap[i] = i;
        }
        for (var b = CanonicalBones; b < targetBones; b++)
        {
            targetParents[b] = 13 + (b - CanonicalBones) % 20;
            sourceMap[b] = -1;
            calibration[b] = new Pose { Position = new Float3(0.05f * (b - CanonicalBones + 1), 0f, 0.1f), Rotation = Quaternion.Identity, Scale = Float3.One };
        }
        var rows = new RetargetRow[targetBones];
        var scratch = new RigidPose[CanonicalBones + targetBones];
        var target = new AssetId(7, 7, 7, 7);
        Assert.Equal(AnimationError.None, RetargetTable.Compile(family, target, parents, reference, targetParents, calibration, sourceMap,
            CanonicalHumanoid.Hips, CanonicalHumanoid.Hips, 1.1f, scratch, rows, out var binding));
        return new Rig { Parents = parents, Reference = reference, Family = family, Clip = clip!, TargetParents = targetParents, Rows = rows, Binding = binding, TargetBones = targetBones };
    }

    static Buffers MakeBuffers(Rig rig, int contribs)
    {
        var tgt = rig.TargetBones;
        var binds = new Affine[tgt];
        var map = new int[tgt];
        for (var i = 0; i < tgt; i++)
        {
            binds[i] = Affine.Identity;
            map[i] = i;
        }
        var contributions = new ClipContribution[contribs];
        for (var c = 0; c < contribs; c++)
        {
            contributions[c] = new ClipContribution(new ClipHandle(0, 1), 0, 1f / contribs);
        }
        return new Buffers
        {
            Decoded = new Pose[contribs * CanonicalBones],
            Blended = new Pose[CanonicalBones],
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
            CanonicalModel = new RigidPose[CanonicalBones],
            TargetModel = new RigidPose[tgt],
            Contributions = contributions,
            Layers = new[] { new LayerInput(0, ClipMode.Override, 0, contribs) },
        };
    }

    static AnimationError FullPass(Rig rig, Buffers buffers, double time, ulong tick, ulong continuity, float weight, FrameMode mode)
    {
        for (var c = 0; c < buffers.Contributions.Length; c++)
        {
            var at = time + c * 0.37 * Duration;
            buffers.Contributions[c] = new ClipContribution(new ClipHandle(0, 1), at, weight);
            var error = rig.Clip.Sample(at, rig.Reference, buffers.Decoded.AsSpan(c * CanonicalBones, CanonicalBones));
            if (error != AnimationError.None)
            {
                return error;
            }
        }
        var staged = CanonicalBlend.Evaluate(buffers.Contributions, buffers.Layers, buffers.Decoded, Array.Empty<Pose>(), Array.Empty<float>(), rig.Reference, buffers.Blended);
        if (staged != AnimationError.None)
        {
            return staged;
        }
        staged = RetargetTable.Apply(rig.Family, rig.Binding, rig.Parents, buffers.Blended, rig.Rows, buffers.CanonicalModel, buffers.TargetModel, buffers.Raw);
        if (staged != AnimationError.None)
        {
            return staged;
        }
        var frame = new AnimationFrame(tick, 1, continuity, time, 0.01f, 0.2f, mode);
        staged = TimelinePose.Evaluate(frame, buffers.Contributions, buffers.Layers, true, buffers.Raw, buffers.Velocity,
            ref buffers.State, buffers.Bones, buffers.Staged, buffers.Previous, buffers.PreviousLayers, buffers.Output, buffers.Scratch, out _);
        if (staged != AnimationError.None)
        {
            return staged;
        }
        Hierarchy.Model(rig.TargetParents, buffers.Output, buffers.Models);
        return SkinPalette.Build(buffers.Models, buffers.PaletteMap, buffers.Binds, Affine.Identity, buffers.Palette);
    }

    [Theory]
    [InlineData(CanonicalBones)]
    [InlineData(TargetBones)]
    public void WarmFiveStageLoopAllocatesZeroBytesOverOneThousandCalls(int targetBones)
    {
        var rig = BuildRig(targetBones);
        foreach (var contribs in new[] { 1, 2, 4 })
        {
            var buffers = MakeBuffers(rig, contribs);
            var weight = 1f / contribs;
            for (var w = 0; w < 20000; w++)
            {
                Assert.Equal(AnimationError.None, FullPass(rig, buffers, w * 0.001, (ulong)w, 1, weight, FrameMode.Advance));
            }
            var before = GC.GetAllocatedBytesForCurrentThread();
            var result = AnimationError.None;
            for (var w = 20000; w < 21000; w++)
            {
                result = FullPass(rig, buffers, w * 0.001, (ulong)w, 1, weight, FrameMode.Advance);
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(AnimationError.None, result);
            Assert.Equal(0, allocated);
            Assert.True(RigidMath.UnitPose(buffers.Output[1]));
        }
    }

    [Theory]
    [InlineData(CanonicalBones)]
    [InlineData(TargetBones)]
    public void CorpusShapedSeeksAndCutsStayValid(int targetBones)
    {
        var rig = BuildRig(targetBones);
        var buffers = MakeBuffers(rig, 2);
        var random = new Random(20261103);
        Assert.Equal(AnimationError.None, FullPass(rig, buffers, 0, 0, 1, 0.5f, FrameMode.Advance));
        var time = 0.0;
        ulong continuity = 1;
        for (var i = 1; i <= 500; i++)
        {
            var mode = FrameMode.Advance;
            if (i % 25 == 0)
            {
                continuity++;
                time += 0.02 + random.NextDouble() * 0.05;
            }
            else if (random.NextDouble() < 0.4)
            {
                mode = FrameMode.Seek;
                time = random.NextDouble() * Duration;
            }
            else
            {
                time += 0.005 + random.NextDouble() * 0.03;
            }
            Assert.Equal(AnimationError.None, FullPass(rig, buffers, time, (ulong)i, continuity, 0.5f, mode));
            Assert.True(RigidMath.UnitPose(buffers.Output[1]));
        }
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void BlendWeightsCoverOneTwoAndFourContributions(int contribs)
    {
        var rig = BuildRig(TargetBones);
        var buffers = MakeBuffers(rig, contribs);
        var weight = 1f / contribs;
        var direct = new Pose[CanonicalBones];
        Assert.Equal(AnimationError.None, rig.Clip.Sample(0.75, rig.Reference, direct));
        for (var c = 0; c < contribs; c++)
        {
            buffers.Contributions[c] = new ClipContribution(new ClipHandle(0, 1), 0.75, weight);
            Assert.Equal(AnimationError.None, rig.Clip.Sample(0.75, rig.Reference, buffers.Decoded.AsSpan(c * CanonicalBones, CanonicalBones)));
        }
        Assert.Equal(AnimationError.None, CanonicalBlend.Evaluate(buffers.Contributions, buffers.Layers, buffers.Decoded, Array.Empty<Pose>(), Array.Empty<float>(), rig.Reference, buffers.Blended));
        var worst = 0f;
        for (var b = 0; b < CanonicalBones; b++)
        {
            var gap = (buffers.Blended[b].Position - direct[b].Position).Length;
            if (gap > worst)
            {
                worst = gap;
            }
        }
        Assert.InRange(worst, 0, 1e-5f);
    }
    [Fact]
    public void FixedWorkloadStageMediansAreReported()
    {
        var rig = BuildRig(TargetBones);
        var buffers = MakeBuffers(rig, 2);
        var time = 0.0;
        ulong tick = 0;
        for (var w = 0; w < 20000; w++)
        {
            time += 0.01;
            tick++;
            Assert.Equal(AnimationError.None, FullPass(rig, buffers, time, tick, 1, 0.5f, FrameMode.Advance));
        }
        var medians = new double[5];
        var names = new[] { "sample", "blend", "retarget", "smooth", "palette" };
        var watch = new Stopwatch();
        for (var s = 0; s < 5; s++)
        {
            var samples = new double[200];
            for (var it = 0; it < 200; it++)
            {
                time += 0.007;
                tick++;
                watch.Restart();
                if (s == 0)
                {
                    for (var c = 0; c < 2; c++)
                    {
                        var at = time + c * 0.37 * Duration;
                        buffers.Contributions[c] = new ClipContribution(new ClipHandle(0, 1), at, 0.5f);
                        Assert.Equal(AnimationError.None, rig.Clip.Sample(at, rig.Reference, buffers.Decoded.AsSpan(c * CanonicalBones, CanonicalBones)));
                    }
                }
                else if (s == 1)
                {
                    Assert.Equal(AnimationError.None, CanonicalBlend.Evaluate(buffers.Contributions, buffers.Layers, buffers.Decoded, Array.Empty<Pose>(), Array.Empty<float>(), rig.Reference, buffers.Blended));
                }
                else if (s == 2)
                {
                    Assert.Equal(AnimationError.None, RetargetTable.Apply(rig.Family, rig.Binding, rig.Parents, buffers.Blended, rig.Rows, buffers.CanonicalModel, buffers.TargetModel, buffers.Raw));
                }
                else if (s == 3)
                {
                    var frame = new AnimationFrame(tick, 1, 1, time, 0.01f, 0.2f, FrameMode.Advance);
                    Assert.Equal(AnimationError.None, TimelinePose.Evaluate(frame, buffers.Contributions, buffers.Layers, true, buffers.Raw, buffers.Velocity,
                        ref buffers.State, buffers.Bones, buffers.Staged, buffers.Previous, buffers.PreviousLayers, buffers.Output, buffers.Scratch, out _));
                }
                else
                {
                    Hierarchy.Model(rig.TargetParents, buffers.Output, buffers.Models);
                    Assert.Equal(AnimationError.None, SkinPalette.Build(buffers.Models, buffers.PaletteMap, buffers.Binds, Affine.Identity, buffers.Palette));
                }
                watch.Stop();
                samples[it] = watch.ElapsedTicks * 1000000.0 / Stopwatch.Frequency;
            }
            Array.Sort(samples);
            medians[s] = samples[100];
        }
        for (var s = 0; s < 5; s++)
        {
            output.WriteLine(names[s] + " median " + medians[s].ToString("F3") + " us per character frame (67 bones, 1 char, 2 contribs)");
        }
    }
}
