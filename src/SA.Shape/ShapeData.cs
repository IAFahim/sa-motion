using System;
using System.Runtime.InteropServices;
using SA.Animation;
using SA.Motion.Numerics;

namespace SA.Shape
{
    public enum ShapeGrounding : byte { Free, KeepBase }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct ShapeChannel
    {
        public readonly uint Id;
        public readonly float Minimum;
        public readonly float Maximum;
        public readonly float Default;
        public readonly float HalfLife;
        public ShapeChannel(uint id, float minimum, float maximum, float defaultValue = 0f, float halfLife = .1f)
        { Id = id; Minimum = minimum; Maximum = maximum; Default = defaultValue; HalfLife = halfLife; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ShapeParameter
    {
        public float Value;
        public float Velocity;
        public ShapeParameter(float value, float velocity = 0f) { Value = value; Velocity = velocity; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct OffsetEffect
    {
        public readonly int Channel;
        public readonly int Bone;
        public readonly Float3 Delta;
        public readonly float LogLength;
        public OffsetEffect(int channel, int bone, Float3 delta, float logLength = 0f)
        { Channel = channel; Bone = bone; Delta = delta; LogLength = logLength; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct SkinEffect
    {
        public readonly int Channel;
        public readonly int Bone;
        public readonly Float3 LogScale;
        public SkinEffect(int channel, int bone, Float3 logScale) { Channel = channel; Bone = bone; LogScale = logScale; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct SkinFrame
    {
        public readonly Quaternion Basis;
        public readonly Float3 Pivot;
        public SkinFrame(Quaternion basis, Float3 pivot) { Basis = basis; Pivot = pivot; }
        public static SkinFrame Identity => new SkinFrame(Quaternion.Identity, Float3.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct ShapeMarker
    {
        public readonly int Bone;
        public readonly Float3 Offset;
        public ShapeMarker(int bone, Float3 offset) { Bone = bone; Offset = offset; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct ShapeProfile
    {
        public readonly AssetId Definition;
        public readonly AssetId Target;
        public readonly ShapeGrounding Grounding;
        public readonly ShapeMarker SupportA;
        public readonly ShapeMarker SupportB;
        public readonly float MinimumLength;
        public readonly float MaximumLength;
        public readonly float MinimumSkinScale;
        public readonly float MaximumSkinScale;
        public ShapeProfile(AssetId definition, AssetId target, ShapeMarker supportA, ShapeMarker supportB, ShapeGrounding grounding = ShapeGrounding.KeepBase,
            float minimumLength = .25f, float maximumLength = 4f, float minimumSkinScale = .125f, float maximumSkinScale = 8f)
        { Definition = definition; Target = target; SupportA = supportA; SupportB = supportB; Grounding = grounding;
            MinimumLength = minimumLength; MaximumLength = maximumLength; MinimumSkinScale = minimumSkinScale; MaximumSkinScale = maximumSkinScale; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct ShapeGeometry
    {
        public readonly AssetId Definition;
        public readonly ulong Revision;
        public readonly RetargetGeometry Retarget;
        public readonly float BaseHipHeight;
        public readonly float HipHeight;
        public readonly float GroundY;
        public readonly float GroundShift;
        public ShapeGeometry(AssetId definition, ulong revision, RetargetGeometry retarget, float baseHipHeight, float hipHeight, float groundY, float groundShift)
        { Definition = definition; Revision = revision; Retarget = retarget; BaseHipHeight = baseHipHeight; HipHeight = hipHeight; GroundY = groundY; GroundShift = groundShift; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly ref struct ShapeDefinition
    {
        public readonly ShapeProfile Profile;
        public readonly ReadOnlySpan<ShapeChannel> Channels;
        public readonly ReadOnlySpan<OffsetEffect> Offsets;
        public readonly ReadOnlySpan<SkinEffect> Skin;
        public readonly ReadOnlySpan<SkinFrame> SkinFrames;
        public ShapeDefinition(ShapeProfile profile, ReadOnlySpan<ShapeChannel> channels, ReadOnlySpan<OffsetEffect> offsets,
            ReadOnlySpan<SkinEffect> skin, ReadOnlySpan<SkinFrame> skinFrames)
        { Profile = profile; Channels = channels; Offsets = offsets; Skin = skin; SkinFrames = skinFrames; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly ref struct ShapeWorkspace
    {
        public readonly Span<Float3> Offsets;
        public readonly Span<float> LengthScales;
        public readonly Span<float> MotionScales;
        public readonly Span<Float3> SkinScales;
        public readonly Span<Affine> Deformations;
        public readonly Span<RigidPose> Models;
        public ShapeWorkspace(Span<Float3> offsets, Span<float> lengthScales, Span<float> motionScales, Span<Float3> skinScales, Span<Affine> deformations, Span<RigidPose> models)
        { Offsets = offsets; LengthScales = lengthScales; MotionScales = motionScales; SkinScales = skinScales; Deformations = deformations; Models = models; }
    }
}
