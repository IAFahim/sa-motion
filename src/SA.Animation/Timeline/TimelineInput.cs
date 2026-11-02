using System;
using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Animation
{
    public enum FrameMode : byte { Advance, Seek, Teleport, Pause }
    public enum FrameAction : byte { Initialized, Advanced, Transitioned, Sought, Reused, Waiting }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct ClipHandle : IEquatable<ClipHandle>
    {
        public readonly uint Slot;
        public readonly uint Generation;
        public ClipHandle(uint slot, uint generation) { Slot = slot; Generation = generation; }
        public bool Equals(ClipHandle other) => Slot == other.Slot && Generation == other.Generation;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct ClipContribution
    {
        public readonly ClipHandle Clip;
        public readonly double SampleSeconds;
        public readonly float Weight;
        public readonly ushort Layer;
        public readonly ushort MaskIndex;
        public ClipContribution(ClipHandle clip, double sampleSeconds, float weight, ushort layer = 0, ushort maskIndex = ushort.MaxValue)
        { Clip = clip; SampleSeconds = sampleSeconds; Weight = weight; Layer = layer; MaskIndex = maskIndex; }
        public bool SameMember(in ClipContribution other) => Clip.Equals(other.Clip) && Layer == other.Layer && MaskIndex == other.MaskIndex;
        public bool SameInput(in ClipContribution other) => SameMember(other) && SampleSeconds == other.SampleSeconds && Weight == other.Weight;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct LayerInput
    {
        public readonly ushort Layer;
        public readonly ClipMode Mode;
        public readonly int First;
        public readonly int Count;
        public readonly float Weight;
        public LayerInput(ushort layer, ClipMode mode, int first, int count, float weight = 1f)
        { Layer = layer; Mode = mode; First = first; Count = count; Weight = weight; }
        public bool SameMember(in LayerInput other) => Layer == other.Layer && Mode == other.Mode && First == other.First && Count == other.Count;
        public bool SameInput(in LayerInput other) => SameMember(other) && Weight == other.Weight;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct AnimationFrame
    {
        public readonly ulong Tick;
        public readonly uint RigEpoch;
        public readonly ulong Continuity;
        public readonly double SimulationSeconds;
        public readonly float DeltaSeconds;
        public readonly float TransitionSeconds;
        public readonly float MaxHistoryGapSeconds;
        public readonly FrameMode Mode;
        public AnimationFrame(ulong tick, uint rigEpoch, ulong continuity, double simulationSeconds, float deltaSeconds,
            float transitionSeconds = .2f, FrameMode mode = FrameMode.Advance, float maxHistoryGapSeconds = .1f)
        { Tick = tick; RigEpoch = rigEpoch; Continuity = continuity; SimulationSeconds = simulationSeconds; DeltaSeconds = deltaSeconds;
            TransitionSeconds = transitionSeconds; Mode = mode; MaxHistoryGapSeconds = maxHistoryGapSeconds; }
        public bool SameInput(in AnimationFrame other) => Tick == other.Tick && RigEpoch == other.RigEpoch && Continuity == other.Continuity
            && SimulationSeconds == other.SimulationSeconds && DeltaSeconds == other.DeltaSeconds && TransitionSeconds == other.TransitionSeconds
            && MaxHistoryGapSeconds == other.MaxHistoryGapSeconds && Mode == other.Mode;
    }

    public static class TimelineInput
    {
        public static AnimationError Validate(ReadOnlySpan<ClipContribution> contributions, ReadOnlySpan<LayerInput> layers)
        {
            var cursor = 0;
            for (var i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                if (layer.First != cursor || layer.Count < 0 || layer.Count > contributions.Length - cursor || layer.Mode > ClipMode.Additive
                    || !Scalar.IsFinite(layer.Weight) || layer.Weight < 0 || layer.Weight > 1 || (i > 0 && layers[i - 1].Layer >= layer.Layer))
                    return AnimationError.InvalidFrame;
                for (var j = cursor; j < cursor + layer.Count; j++)
                    if (contributions[j].Layer != layer.Layer || contributions[j].Clip.Generation == 0 || double.IsNaN(contributions[j].SampleSeconds)
                        || double.IsInfinity(contributions[j].SampleSeconds) || !Scalar.IsFinite(contributions[j].Weight) || contributions[j].Weight < 0 || contributions[j].Weight > 1)
                        return AnimationError.InvalidFrame;
                cursor += layer.Count;
            }
            return cursor == contributions.Length ? AnimationError.None : AnimationError.InvalidFrame;
        }
    }
}
