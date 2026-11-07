using System;
using SA.Animation;
using SA.Motion.Numerics;
using SA.Soma;
using static SA.Soma.Tests.SomaFixtures;

namespace SA.Soma.Tests;

public sealed class SomaClipTests
{
    [Fact]
    public void SeventySevenJointInputMatchesThirtyJointInputExactly()
    {
        var retarget = Retarget();
        var world = World(BindModel());
        var swing = Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.3f);
        for (var j = SomaRig.LeftLeg; j <= 25; j++) world[j] = Quaternion.Multiply(swing, world[j]);
        var second = FromWorld(world, SomaRig.BindPosition(SomaRig.Hips) + new Float3(0f, 0f, 0.1f));
        var narrow = Pack(BindModel(), second);
        const int frames = 2;
        var wide = new float[frames * SomaRig.SkeletonJointCount * SomaFrame.FloatsPerJoint];
        for (var f = 0; f < frames; f++)
            for (var j = 0; j < SomaRig.ModelJointCount; j++)
            {
                var from = (f * SomaRig.ModelJointCount + j) * SomaFrame.FloatsPerJoint;
                var to = (f * SomaRig.SkeletonJointCount + SomaRig.SkeletonIndex(j)) * SomaFrame.FloatsPerJoint;
                for (var k = 0; k < SomaFrame.FloatsPerJoint; k++)
                    wide[to + k] = narrow[from + k];
            }
        var used = new bool[SomaRig.SkeletonJointCount];
        for (var j = 0; j < SomaRig.ModelJointCount; j++)
            used[SomaRig.SkeletonIndex(j)] = true;
        for (var f = 0; f < frames; f++)
            for (var s = 0; s < SomaRig.SkeletonJointCount; s++)
            {
                if (used[s])
                    continue;
                wide[(f * SomaRig.SkeletonJointCount + s) * SomaFrame.FloatsPerJoint + 6] = 1f;
            }
        var narrowSamples = new Pose[frames * CanonicalHumanoid.BoneCount];
        var narrowRoot = new RigidPose[frames];
        Assert.Equal(AnimationError.None, SomaClip.Convert(retarget, narrow, frames, SomaRig.ModelJointCount, SomaSpace.ModelYUp, narrowSamples, narrowRoot));
        var wideSamples = new Pose[frames * CanonicalHumanoid.BoneCount];
        var wideRoot = new RigidPose[frames];
        Assert.Equal(AnimationError.None, SomaClip.Convert(retarget, wide, frames, SomaRig.SkeletonJointCount, SomaSpace.ModelYUp, wideSamples, wideRoot));
        Assert.Equal(narrowSamples, wideSamples);
        Assert.Equal(narrowRoot, wideRoot);
    }

    [Fact]
    public void NanPositionReturnsInvalidFrame()
    {
        var retarget = Retarget();
        var poses = Pack(BindModel());
        poses[0] = float.NaN;
        Assert.Equal(AnimationError.InvalidFrame, SomaClip.Convert(retarget, poses, 1, SomaRig.ModelJointCount, SomaSpace.ModelYUp, new Pose[CanonicalHumanoid.BoneCount], new RigidPose[1]));
    }

    [Fact]
    public void ZeroQuaternionReturnsInvalidFrame()
    {
        var retarget = Retarget();
        var poses = Pack(BindModel());
        poses[3] = 0f;
        poses[4] = 0f;
        poses[5] = 0f;
        poses[6] = 0f;
        Assert.Equal(AnimationError.InvalidFrame, SomaClip.Convert(retarget, poses, 1, SomaRig.ModelJointCount, SomaSpace.ModelYUp, new Pose[CanonicalHumanoid.BoneCount], new RigidPose[1]));
    }

    [Fact]
    public void ZeroFramesReturnsInvalidFrame()
    {
        var retarget = Retarget();
        Assert.Equal(AnimationError.InvalidFrame, SomaClip.Convert(retarget, ReadOnlySpan<float>.Empty, 0, SomaRig.ModelJointCount, SomaSpace.ModelYUp, Span<Pose>.Empty, Span<RigidPose>.Empty));
    }

    [Fact]
    public void WrongSpanLengthsReturnInvalidFrame()
    {
        var retarget = Retarget();
        var poses = Pack(BindModel());
        Assert.Equal(AnimationError.InvalidFrame, SomaClip.Convert(retarget, new float[poses.Length - 1], 1, SomaRig.ModelJointCount, SomaSpace.ModelYUp, new Pose[CanonicalHumanoid.BoneCount], new RigidPose[1]));
        Assert.Equal(AnimationError.InvalidFrame, SomaClip.Convert(retarget, poses, 1, SomaRig.ModelJointCount, SomaSpace.ModelYUp, new Pose[CanonicalHumanoid.BoneCount - 1], new RigidPose[1]));
        Assert.Equal(AnimationError.InvalidFrame, SomaClip.Convert(retarget, poses, 1, SomaRig.ModelJointCount, SomaSpace.ModelYUp, new Pose[CanonicalHumanoid.BoneCount], new RigidPose[2]));
    }

    [Fact]
    public void UnknownJointsPerFrameReturnsInvalidFrame()
    {
        var retarget = Retarget();
        Assert.Equal(AnimationError.InvalidFrame, SomaClip.Convert(retarget, new float[SomaFrame.FloatsPerJoint * 31], 1, 31, SomaSpace.ModelYUp, new Pose[CanonicalHumanoid.BoneCount], new RigidPose[1]));
    }

    [Fact]
    public void EncodeWithOneFrameReturnsInvalidFrame()
    {
        var retarget = Retarget();
        Assert.Equal(AnimationError.InvalidFrame, SomaClip.Encode(retarget, new Pose[CanonicalHumanoid.BoneCount], new RigidPose[1], new float[4], 1, 30f, out _, out _));
    }
}
