using System;
using SA.Animation;
using SA.Motion.Mechanics;
using SA.Motion.Numerics;

namespace SA.Shape
{
    public static class ShapeParameters
    {
        public const int MaxChannels = 128;

        public static AnimationError Validate(ReadOnlySpan<ShapeChannel> channels)
        {
            if (channels.Length > MaxChannels) return AnimationError.CapacityExceeded;
            for (var i = 0; i < channels.Length; i++)
            {
                var channel = channels[i];
                if (!Scalar.IsFinite(channel.Minimum) || !Scalar.IsFinite(channel.Maximum) || !Scalar.IsFinite(channel.Default) || !Scalar.IsFinite(channel.HalfLife)
                    || channel.Minimum > channel.Maximum || channel.Default < channel.Minimum || channel.Default > channel.Maximum || channel.HalfLife < 0f)
                    return AnimationError.InvalidFrame;
                for (var previous = 0; previous < i; previous++) if (channels[previous].Id == channel.Id) return AnimationError.InvalidFrame;
            }
            return AnimationError.None;
        }

        public static AnimationError Reset(ReadOnlySpan<ShapeChannel> channels, Span<ShapeParameter> output)
        {
            var error = Validate(channels);
            if (error != AnimationError.None) return error;
            if (output.Length < channels.Length) return AnimationError.CapacityExceeded;
            for (var i = 0; i < channels.Length; i++) output[i] = new ShapeParameter(channels[i].Default);
            return AnimationError.None;
        }

        public static AnimationError Step(ReadOnlySpan<ShapeChannel> channels, ReadOnlySpan<float> requested, ReadOnlySpan<ShapeParameter> current,
            float deltaSeconds, Span<ShapeParameter> output)
        {
            var error = Validate(channels);
            if (error != AnimationError.None) return error;
            if (requested.Length != channels.Length || current.Length != channels.Length || output.Length < channels.Length || !Scalar.IsFinite(deltaSeconds) || deltaSeconds < 0f)
                return AnimationError.InvalidFrame;
            if (current.Overlaps(output, out var offset) && offset != 0) return AnimationError.AliasedBuffers;
            for (var i = 0; i < channels.Length; i++)
                if (!Scalar.IsFinite(requested[i]) || !Scalar.IsFinite(current[i].Value) || !Scalar.IsFinite(current[i].Velocity)
                    || current[i].Value < channels[i].Minimum || current[i].Value > channels[i].Maximum)
                    return AnimationError.InvalidFrame;
            for (var i = 0; i < channels.Length; i++)
            {
                var channel = channels[i];
                var goal = Clamp(requested[i], channel.Minimum, channel.Maximum);
                var previous = current[i];
                if (deltaSeconds == 0f) { output[i] = previous; continue; }
                var spring = CriticalSpring.Step(new SpringState { Value = previous.Value, Velocity = previous.Velocity }, goal, channel.HalfLife, deltaSeconds);
                var value = Clamp(spring.Value, channel.Minimum, channel.Maximum);
                var velocity = value == spring.Value ? spring.Velocity : 0f;
                if (Math.Abs(value - goal) <= 1e-5f && Math.Abs(velocity) <= 1e-4f) { value = goal; velocity = 0f; }
                output[i] = new ShapeParameter(value, velocity);
            }
            return AnimationError.None;
        }

        internal static float Clamp(float value, float minimum, float maximum) => Math.Max(minimum, Math.Min(maximum, value));
        internal static float Delta(in ShapeChannel channel, float value) => Clamp(value, channel.Minimum, channel.Maximum) - channel.Default;
    }
}
