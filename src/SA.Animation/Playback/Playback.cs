using System;
using SA.Motion.Numerics;

namespace SA.Animation
{
    public static class Playback
    {
        public const int MaxStates = 1024;

        public static void Play(Span<ClipState> states, ref int count, ClipIndex clip, in PlayOptions options) =>
            CrossFade(states, ref count, clip, 0f, options);

        public static void CrossFade(Span<ClipState> states, ref int count, ClipIndex clip, float seconds, in PlayOptions options)
        {
            Span<ClipIndex> clips = stackalloc ClipIndex[1];
            Span<float> weights = stackalloc float[1];
            clips[0] = clip;
            weights[0] = 1f;
            Converge(states, ref count, options.Layer, clips, weights, seconds, false, options);
        }

        public static void Blend(Span<ClipState> states, ref int count, ReadOnlySpan<ClipIndex> clips, ReadOnlySpan<float> weights, float seconds, in PlayOptions options) =>
            Converge(states, ref count, options.Layer, clips, weights, seconds, true, options);

        public static void Stop(Span<ClipState> states, ref int count, byte layer, float seconds) =>
            Converge(states, ref count, layer, default, default, seconds, false, PlayOptions.Default.WithLayer(layer));

        public static int Find(ReadOnlySpan<ClipState> states, int count, byte layer, ClipIndex clip)
        {
            var live = Live(count, states.Length);
            var ending = -1;
            for (var i = 0; i < live; i++)
            {
                var state = states[i];
                if (state.Layer != layer || !state.Clip.Is(clip))
                    continue;
                if (state.FadeRate >= 0f)
                    return i;
                ending = ending < 0 ? i : ending;
            }
            return ending;
        }

        public static bool IsPlaying(ReadOnlySpan<ClipState> states, int count, byte layer, ClipIndex clip)
        {
            var index = Find(states, count, layer, clip);
            return index >= 0 && states[index].FadeRate >= 0f;
        }

        public static float NormalizedTime(ReadOnlySpan<ClipState> states, int count, byte layer, ClipIndex clip)
        {
            var index = Find(states, count, layer, clip);
            return index < 0 ? 0f : Phase.Shape(states[index].Phase, states[index].Wrap);
        }

        public static bool HasFinished(ReadOnlySpan<ClipState> states, int count, byte layer, ClipIndex clip)
        {
            var index = Find(states, count, layer, clip);
            if (index < 0)
                return true;
            var state = states[index];
            return state.Wrap == Wrap.Clamp && (state.Speed >= 0f ? state.Phase >= 1f : state.Phase <= 0f);
        }

        public static bool Seek(Span<ClipState> states, int count, byte layer, ClipIndex clip, float phase)
        {
            var index = Find(states, count, layer, clip);
            if (index < 0)
                return false;
            states[index].Phase = Phase.Wrapped(phase, states[index].Wrap);
            return true;
        }

        public static float Rate(ReadOnlySpan<float> durations, in ClipState state)
        {
            var index = state.Clip.Value;
            if (index >= durations.Length)
                return 0f;
            var duration = durations[index];
            return duration > 0f ? Scalar.Finite(Scalar.Finite(state.Speed) / duration) : 0f;
        }

        public static float SyncedRate(ReadOnlySpan<float> durations, ReadOnlySpan<ClipState> states, int count, byte layer)
        {
            float weighted = 0f, weights = 0f, plain = 0f;
            var members = 0;
            var live = Live(count, states.Length);
            for (var i = 0; i < live; i++)
            {
                var state = states[i];
                if (state.Synced == 0 || state.Layer != layer)
                    continue;
                var rate = Rate(durations, state);
                var weight = NonNegative(state.Weight * state.Fade);
                weighted += weight * rate;
                weights += weight;
                plain += rate;
                members++;
            }
            return Scalar.Finite(weights > 0f ? weighted / weights : members > 0 ? plain / members : 0f);
        }

        public static void Step(Span<ClipState> states, ref int count, ReadOnlySpan<float> durations, float deltaTime)
        {
            var live = Live(count, states.Length);
            var dt = Scalar.Finite(deltaTime);
            for (var i = 0; i < live; i++)
            {
                var rate = states[i].Synced != 0 ? SyncedRate(durations, states, live, states[i].Layer) : Rate(durations, states[i]);
                states[i].Phase = Phase.Wrapped(states[i].Phase + rate * dt, states[i].Wrap);
            }
            var kept = 0;
            for (var i = 0; i < live; i++)
            {
                var state = states[i];
                state.Fade = Unit(state.Fade + state.FadeRate * dt);
                if (state.FadeRate < 0f && state.Fade <= 0f)
                    continue;
                states[kept++] = state;
            }
            states.Slice(kept, live - kept).Clear();
            count = kept;
        }

        internal static int Live(int count, int length)
        {
            var capacity = Capacity(length);
            return count < 0 ? 0 : count < capacity ? count : capacity;
        }

        static int Capacity(int length) => length < MaxStates ? length : MaxStates;

        static void Converge(Span<ClipState> states, ref int count, byte layer, ReadOnlySpan<ClipIndex> clips, ReadOnlySpan<float> weights, float seconds, bool synced, in PlayOptions options)
        {
            var capacity = Capacity(states.Length);
            var live = Live(count, states.Length);
            seconds = NonNegative(seconds);
            var requested = Math.Min(clips.Length, weights.Length);
            Span<bool> keep = stackalloc bool[capacity];
            var groupPhase = 0f;
            for (var i = 0; i < live; i++)
                if (states[i].Layer == layer && states[i].Synced != 0)
                    groupPhase = states[i].Phase;
            for (var k = 0; k < requested; k++)
            {
                if (clips[k].IsNone)
                    continue;
                var weight = NonNegative(weights[k]);
                var pick = Pick(states, live, layer, clips[k], synced, options, keep);
                if (pick >= 0 && keep[pick])
                {
                    states[pick].Weight = Scalar.Min(states[pick].Weight + weight, float.MaxValue);
                    continue;
                }
                if (pick < 0)
                {
                    pick = live < capacity ? live++ : Weakest(states, live, keep);
                    if (pick < 0)
                        continue;
                    states[pick] = Fresh(clips[k], layer, synced, groupPhase, options);
                }
                ref var state = ref states[pick];
                state.Weight = weight;
                state.Speed = Scalar.Finite(options.Speed);
                state.Wrap = synced ? Wrap.Loop : options.Wrap;
                state.FadeRate = FadeSpeed(seconds);
                state.Fade = seconds > 0f ? state.Fade : 1f;
                keep[pick] = true;
            }
            var kept = 0;
            for (var i = 0; i < live; i++)
            {
                var state = states[i];
                if (state.Layer == layer && !keep[i])
                {
                    if (seconds <= 0f)
                        continue;
                    state.FadeRate = -FadeSpeed(seconds);
                }
                states[kept++] = state;
            }
            states.Slice(kept, live - kept).Clear();
            count = kept;
        }

        static int Pick(ReadOnlySpan<ClipState> states, int live, byte layer, ClipIndex clip, bool synced, in PlayOptions options, ReadOnlySpan<bool> keep)
        {
            var ending = -1;
            for (var i = 0; i < live; i++)
            {
                var state = states[i];
                var match = state.Layer == layer && state.Clip.Is(clip) && state.Mode == options.Mode && state.Mask.Is(options.Mask) && (state.Synced != 0) == synced;
                if (!match || (options.Restart && !keep[i]))
                    continue;
                if (keep[i] || state.FadeRate >= 0f)
                    return i;
                ending = ending < 0 ? i : ending;
            }
            return ending;
        }

        static int Weakest(ReadOnlySpan<ClipState> states, int live, ReadOnlySpan<bool> keep)
        {
            var weakest = -1;
            var lowest = 0f;
            for (var i = 0; i < live; i++)
            {
                if (keep[i])
                    continue;
                var strength = Scalar.Finite(states[i].Weight * states[i].Fade);
                if (weakest < 0 || strength < lowest)
                {
                    weakest = i;
                    lowest = strength;
                }
            }
            return weakest;
        }

        static ClipState Fresh(ClipIndex clip, byte layer, bool synced, float groupPhase, in PlayOptions options) => new ClipState
        {
            Clip = clip, Layer = layer, Mode = options.Mode, Mask = options.Mask, Synced = synced ? (byte)1 : (byte)0,
            Wrap = synced ? Wrap.Loop : options.Wrap, Phase = synced ? groupPhase : 0f, Fade = 0f
        };

        static float FadeSpeed(float seconds) => seconds > 0f ? Scalar.Min(1f / seconds, float.MaxValue) : 0f;

        static float NonNegative(float value) => value > 0f && Scalar.IsFinite(value) ? value : 0f;

        static float Unit(float value) => value > 0f ? (value < 1f ? value : 1f) : 0f;
    }
}
