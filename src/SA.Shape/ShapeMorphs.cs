using System;
using System.Runtime.InteropServices;
using SA.Animation;
using SA.Motion.Numerics;

namespace SA.Shape
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct ShapeMorph
    {
        public readonly float Minimum;
        public readonly float Maximum;
        public readonly float Base;
        public ShapeMorph(float minimum, float maximum, float baseValue = 0f) { Minimum = minimum; Maximum = maximum; Base = baseValue; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct MorphEffect
    {
        public readonly int Channel;
        public readonly int Morph;
        public readonly float Delta;
        public MorphEffect(int channel, int morph, float delta) { Channel = channel; Morph = morph; Delta = delta; }
    }

    public static class ShapeMorphs
    {
        public static AnimationError Evaluate(ReadOnlySpan<ShapeChannel> channels, ReadOnlySpan<float> values, ReadOnlySpan<ShapeMorph> morphs,
            ReadOnlySpan<MorphEffect> effects, Span<float> output)
        {
            var error = ShapeParameters.Validate(channels);
            if (error != AnimationError.None) return error;
            if (values.Length != channels.Length || output.Length < morphs.Length || effects.Length > ShapeRig.MaxEffects) return AnimationError.CapacityExceeded;
            if (values.Overlaps(output)) return AnimationError.AliasedBuffers;
            for (var i = 0; i < values.Length; i++)
                if (!Scalar.IsFinite(values[i]) || !Scalar.IsFinite(ShapeParameters.Delta(channels[i], values[i]))) return AnimationError.InvalidFrame;
            for (var i = 0; i < morphs.Length; i++)
                if (!Scalar.IsFinite(morphs[i].Minimum) || !Scalar.IsFinite(morphs[i].Maximum) || !Scalar.IsFinite(morphs[i].Base)
                    || morphs[i].Minimum > morphs[i].Base || morphs[i].Maximum < morphs[i].Base) return AnimationError.InvalidTable;
            for (var i = 0; i < effects.Length; i++)
                if (effects[i].Channel < 0 || effects[i].Channel >= channels.Length || effects[i].Morph < 0 || effects[i].Morph >= morphs.Length
                    || !Scalar.IsFinite(effects[i].Delta)) return AnimationError.InvalidTable;
            for (var i = 0; i < morphs.Length; i++) output[i] = morphs[i].Base;
            for (var i = 0; i < effects.Length; i++)
            {
                var effect = effects[i];
                output[effect.Morph] += effect.Delta * ShapeParameters.Delta(channels[effect.Channel], values[effect.Channel]);
                if (!Scalar.IsFinite(output[effect.Morph])) return AnimationError.InvalidFrame;
            }
            for (var i = 0; i < morphs.Length; i++) output[i] = ShapeParameters.Clamp(output[i], morphs[i].Minimum, morphs[i].Maximum);
            return AnimationError.None;
        }
    }
}
