using System;
using SA.Motion.Inertialization;
using SA.Motion.Numerics;

namespace SA.Animation
{
    public static class TransitionPlayback
    {
        public static RequestDecision PlaySynced(Span<ClipState> states, ref int count, ref TransitionIntent intent, uint key, ClipIndex clip, float seconds, in PlayOptions options)
        {
            var phase = 0f;
            var weight = -1f;
            var live = Playback.Live(count, states.Length);
            for (var i = 0; i < live; i++)
            {
                var state = states[i];
                var strength = state.Weight * state.Fade;
                if (state.Layer == options.Layer && state.Wrap == Wrap.Loop && strength > weight)
                {
                    phase = Scalar.Finite(state.Phase);
                    weight = strength;
                }
            }
            var decision = Play(states, ref count, ref intent, key, clip, seconds, options);
            if (decision == RequestDecision.Start && !options.Restart && options.Wrap == Wrap.Loop)
            {
                var index = Playback.Find(states, count, options.Layer, clip);
                if (index >= 0)
                    states[index].Phase = phase;
            }
            return decision;
        }

        public static RequestDecision Play(Span<ClipState> states, ref int count, ref TransitionIntent intent, uint key, ClipIndex clip, float seconds, in PlayOptions options)
        {
            Span<ClipIndex> clips = stackalloc ClipIndex[1];
            clips[0] = clip;
            var decision = Decide(ref intent, key, clips, seconds, options);
            if (decision != RequestDecision.Misuse)
                Playback.Play(states, ref count, clip, Started(decision, options));
            return decision;
        }

        public static RequestDecision Blend(Span<ClipState> states, ref int count, ref TransitionIntent intent, uint key, ReadOnlySpan<ClipIndex> clips, ReadOnlySpan<float> weights, float seconds, in PlayOptions options)
        {
            var decision = Decide(ref intent, key, clips, seconds, options);
            if (decision != RequestDecision.Misuse)
                Playback.Blend(states, ref count, clips, weights, 0f, Started(decision, options));
            return decision;
        }

        public static uint Membership(ReadOnlySpan<ClipIndex> clips, in PlayOptions options)
        {
            unchecked
            {
                var hash = 2166136261u;
                hash = (hash ^ options.Layer) * 16777619u;
                hash = (hash ^ (uint)options.Mode) * 16777619u;
                hash = (hash ^ options.Mask.Value) * 16777619u;
                for (var i = 0; i < clips.Length; i++)
                    hash = (hash ^ clips[i].Value) * 16777619u;
                return hash;
            }
        }

        static PlayOptions Started(RequestDecision decision, in PlayOptions options) =>
            decision == RequestDecision.Start ? options.WithRestart(true) : options;

        static RequestDecision Decide(ref TransitionIntent intent, uint key, ReadOnlySpan<ClipIndex> clips, float seconds, in PlayOptions options)
        {
            var decision = TransitionRequests.Decide(intent.Request, key, Membership(clips, options), options.Restart, out var next);
            if (decision != RequestDecision.Start)
                return decision;
            intent.Request = next;
            intent.Seconds = Scalar.Max(0f, Scalar.Finite(seconds));
            intent.Pending = 1;
            return decision;
        }
    }
}
