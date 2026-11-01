using System.Runtime.InteropServices;
using SA.Animation;
using SA.Motion.Inertialization;
using Xunit;

namespace SA.Animation.Tests;

internal interface IPlaybackSurface
{
    int Count { get; }
    ClipState this[int index] { get; }
    TransitionIntent Intent { get; }
    void Play(ClipIndex clip, PlayOptions options);
    void CrossFade(ClipIndex clip, float seconds, PlayOptions options);
    void Blend(ClipIndex[] clips, float[] weights, float seconds, PlayOptions options);
    void Stop(byte layer, float seconds);
    void Step(float deltaTime);
    int Find(byte layer, ClipIndex clip);
    bool IsPlaying(byte layer, ClipIndex clip);
    float NormalizedTime(byte layer, ClipIndex clip);
    bool HasFinished(byte layer, ClipIndex clip);
    float SyncedRate(byte layer);
    float Rate(int index);
    RequestDecision TransitionPlay(uint key, ClipIndex clip, float seconds, PlayOptions options);
    RequestDecision TransitionPlaySynced(uint key, ClipIndex clip, float seconds, PlayOptions options);
    RequestDecision TransitionBlend(uint key, ClipIndex[] clips, float[] weights, float seconds, PlayOptions options);
}

internal sealed class PlaybackTrack : IPlaybackSurface
{
    public static readonly float[] ClipDurations = { 1f, 2f, 0.5f };

    public readonly ClipState[] Slots;
    readonly float[] durations;
    int count;
    TransitionIntent intent;

    public PlaybackTrack(int capacity, float[]? durations = null)
    {
        Slots = new ClipState[capacity];
        this.durations = durations ?? ClipDurations;
    }

    public int Count => count;
    public ClipState this[int index] => Slots[index];
    public TransitionIntent Intent => intent;

    public void ClearPending() => intent.Pending = 0;
    public void Put(int index, ClipState state) => Slots[index] = state;
    public void SetCount(int value) => count = value;
    public void Play(ClipIndex clip, PlayOptions options) => Playback.Play(Slots, ref count, clip, options);
    public void CrossFade(ClipIndex clip, float seconds, PlayOptions options) => Playback.CrossFade(Slots, ref count, clip, seconds, options);
    public void Blend(ClipIndex[] clips, float[] weights, float seconds, PlayOptions options) => Playback.Blend(Slots, ref count, clips, weights, seconds, options);
    public void Stop(byte layer, float seconds) => Playback.Stop(Slots, ref count, layer, seconds);
    public void Step(float deltaTime) => Playback.Step(Slots, ref count, durations, deltaTime);
    public int Find(byte layer, ClipIndex clip) => Playback.Find(Slots, count, layer, clip);
    public bool IsPlaying(byte layer, ClipIndex clip) => Playback.IsPlaying(Slots, count, layer, clip);
    public float NormalizedTime(byte layer, ClipIndex clip) => Playback.NormalizedTime(Slots, count, layer, clip);
    public bool HasFinished(byte layer, ClipIndex clip) => Playback.HasFinished(Slots, count, layer, clip);
    public bool Seek(byte layer, ClipIndex clip, float phase) => Playback.Seek(Slots, count, layer, clip, phase);
    public float SyncedRate(byte layer) => Playback.SyncedRate(durations, Slots, count, layer);
    public float Rate(int index) => Playback.Rate(durations, Slots[index]);
    public RequestDecision TransitionPlay(uint key, ClipIndex clip, float seconds, PlayOptions options) => TransitionPlayback.Play(Slots, ref count, ref intent, key, clip, seconds, options);
    public RequestDecision TransitionPlaySynced(uint key, ClipIndex clip, float seconds, PlayOptions options) => TransitionPlayback.PlaySynced(Slots, ref count, ref intent, key, clip, seconds, options);
    public RequestDecision TransitionBlend(uint key, ClipIndex[] clips, float[] weights, float seconds, PlayOptions options) => TransitionPlayback.Blend(Slots, ref count, ref intent, key, clips, weights, seconds, options);
}

internal sealed class PlaybackReference : IPlaybackSurface
{
    readonly List<ClipState> states = new List<ClipState>();
    readonly float[] durations;
    TransitionIntent intent;

    public PlaybackReference(float[] durations) => this.durations = durations;

    public int Count => states.Count;
    public ClipState this[int index] => states[index];
    public TransitionIntent Intent => intent;

    static float Finite(float value) => float.IsFinite(value) ? value : 0f;

    static float Saturate(float value) => Math.Min(1f, Math.Max(0f, value));

    static float Frac(float value) => value - MathF.Floor(value);

    static float Shape(float phase, Wrap wrap) => wrap switch
    {
        Wrap.Loop => Frac(phase),
        Wrap.Clamp => Saturate(phase),
        _ => 1f - MathF.Abs(2f * Frac(phase * 0.5f) - 1f)
    };

    static float Wrapped(float phase, Wrap wrap) => wrap switch
    {
        Wrap.Loop => Frac(phase),
        Wrap.Clamp => Saturate(phase),
        _ => phase - 2f * MathF.Floor(phase * 0.5f)
    };

    public void Play(ClipIndex clip, PlayOptions options) => CrossFade(clip, 0f, options);

    public void CrossFade(ClipIndex clip, float seconds, PlayOptions options) =>
        Converge(options.Layer, new[] { clip }, new[] { 1f }, seconds, false, options);

    public void Blend(ClipIndex[] clips, float[] weights, float seconds, PlayOptions options) =>
        Converge(options.Layer, clips, weights, seconds, true, options);

    public void Stop(byte layer, float seconds) =>
        Converge(layer, Array.Empty<ClipIndex>(), Array.Empty<float>(), seconds, false, PlayOptions.Default.WithLayer(layer));

    public int Find(byte layer, ClipIndex clip)
    {
        var ending = -1;
        for (var i = 0; i < states.Count; i++)
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

    public bool IsPlaying(byte layer, ClipIndex clip)
    {
        var index = Find(layer, clip);
        return index >= 0 && states[index].FadeRate >= 0f;
    }

    public float NormalizedTime(byte layer, ClipIndex clip)
    {
        var index = Find(layer, clip);
        return index < 0 ? 0f : Shape(states[index].Phase, states[index].Wrap);
    }

    public bool HasFinished(byte layer, ClipIndex clip)
    {
        var index = Find(layer, clip);
        if (index < 0)
            return true;
        var state = states[index];
        return state.Wrap == Wrap.Clamp && (state.Speed >= 0f ? state.Phase >= 1f : state.Phase <= 0f);
    }

    public float Rate(int index) => Rate(states[index]);

    float Rate(ClipState state)
    {
        if (state.Clip.Value >= durations.Length)
            return 0f;
        var duration = durations[state.Clip.Value];
        return duration > 0f ? Finite(state.Speed) / duration : 0f;
    }

    public float SyncedRate(byte layer) => SyncedRate(states.ToArray(), layer);

    float SyncedRate(ClipState[] view, byte layer)
    {
        float weighted = 0f, weights = 0f, plain = 0f;
        var count = 0;
        for (var i = 0; i < view.Length; i++)
        {
            var state = view[i];
            if (state.Synced == 0 || state.Layer != layer)
                continue;
            var rate = Rate(state);
            var weight = Math.Max(0f, state.Weight * state.Fade);
            weighted += weight * rate;
            weights += weight;
            plain += rate;
            count++;
        }
        return weights > 0f ? weighted / weights : count > 0 ? plain / count : 0f;
    }

    public void Step(float deltaTime)
    {
        var dt = Finite(deltaTime);
        var view = states.ToArray();
        var rates = new float[view.Length];
        for (var i = 0; i < view.Length; i++)
            rates[i] = view[i].Synced != 0 ? SyncedRate(view, view[i].Layer) : Rate(view[i]);
        for (var i = states.Count - 1; i >= 0; i--)
        {
            var state = states[i];
            state.Phase = Wrapped(state.Phase + rates[i] * dt, state.Wrap);
            state.Fade = Saturate(state.Fade + state.FadeRate * dt);
            if (state.FadeRate < 0f && state.Fade <= 0f)
                states.RemoveAt(i);
            else
                states[i] = state;
        }
    }

    void Converge(byte layer, ClipIndex[] clips, float[] weights, float seconds, bool synced, PlayOptions options)
    {
        seconds = Math.Max(0f, Finite(seconds));
        var count = Math.Min(clips.Length, weights.Length);
        var existing = states.Count;
        var keep = new bool[existing + count];
        var groupPhase = 0f;
        for (var i = 0; i < existing; i++)
            if (states[i].Layer == layer && states[i].Synced != 0)
                groupPhase = states[i].Phase;
        for (var k = 0; k < count; k++)
        {
            if (clips[k].IsNone)
                continue;
            var weight = Math.Max(0f, Finite(weights[k]));
            var pick = Pick(layer, clips[k], synced, options, existing, keep);
            if (pick >= 0 && keep[pick])
            {
                var merged = states[pick];
                merged.Weight += weight;
                states[pick] = merged;
                continue;
            }
            if (pick < 0)
            {
                states.Add(new ClipState
                {
                    Clip = clips[k], Layer = layer, Mode = options.Mode, Mask = options.Mask, Synced = synced ? (byte)1 : (byte)0,
                    Wrap = synced ? Wrap.Loop : options.Wrap, Phase = synced ? groupPhase : 0f, Fade = 0f
                });
                pick = states.Count - 1;
            }
            var state = states[pick];
            state.Weight = weight;
            state.Speed = Finite(options.Speed);
            state.Wrap = synced ? Wrap.Loop : options.Wrap;
            state.FadeRate = seconds > 0f ? 1f / seconds : 0f;
            state.Fade = seconds > 0f ? state.Fade : 1f;
            states[pick] = state;
            keep[pick] = true;
        }
        for (var i = states.Count - 1; i >= 0; i--)
        {
            if (states[i].Layer != layer || keep[i])
                continue;
            if (seconds <= 0f)
            {
                states.RemoveAt(i);
                continue;
            }
            var state = states[i];
            state.FadeRate = -1f / seconds;
            states[i] = state;
        }
    }

    int Pick(byte layer, ClipIndex clip, bool synced, PlayOptions options, int existing, bool[] keep)
    {
        var ending = -1;
        for (var i = 0; i < states.Count; i++)
        {
            var state = states[i];
            var match = state.Layer == layer && state.Clip.Is(clip) && state.Mode == options.Mode && state.Mask.Is(options.Mask) && (state.Synced != 0) == synced;
            if (!match || (options.Restart && i < existing && !keep[i]))
                continue;
            if (keep[i] || state.FadeRate >= 0f)
                return i;
            ending = ending < 0 ? i : ending;
        }
        return ending;
    }

    public RequestDecision TransitionPlaySynced(uint key, ClipIndex clip, float seconds, PlayOptions options)
    {
        var phase = 0f;
        var weight = -1f;
        foreach (var state in states)
            if (state.Layer == options.Layer && state.Wrap == Wrap.Loop && state.Weight * state.Fade > weight)
            {
                phase = state.Phase;
                weight = state.Weight * state.Fade;
            }
        var decision = TransitionPlay(key, clip, seconds, options);
        if (decision == RequestDecision.Start && !options.Restart && options.Wrap == Wrap.Loop)
        {
            var index = Find(options.Layer, clip);
            if (index >= 0)
            {
                var state = states[index];
                state.Phase = phase;
                states[index] = state;
            }
        }
        return decision;
    }

    public RequestDecision TransitionPlay(uint key, ClipIndex clip, float seconds, PlayOptions options)
    {
        var clips = new[] { clip };
        var decision = Decide(key, clips, seconds, options);
        if (decision != RequestDecision.Misuse)
            Play(clip, Started(decision, options));
        return decision;
    }

    public RequestDecision TransitionBlend(uint key, ClipIndex[] clips, float[] weights, float seconds, PlayOptions options)
    {
        var decision = Decide(key, clips, seconds, options);
        if (decision != RequestDecision.Misuse)
            Blend(clips, weights, 0f, Started(decision, options));
        return decision;
    }

    static PlayOptions Started(RequestDecision decision, PlayOptions options) =>
        decision == RequestDecision.Start ? options.WithRestart(true) : options;

    RequestDecision Decide(uint key, ClipIndex[] clips, float seconds, PlayOptions options)
    {
        var decision = TransitionRequests.Decide(intent.Request, key, Membership(clips, options), options.Restart, out var next);
        if (decision != RequestDecision.Start)
            return decision;
        intent.Request = next;
        intent.Seconds = Math.Max(0f, Finite(seconds));
        intent.Pending = 1;
        return decision;
    }

    static uint Membership(ClipIndex[] clips, PlayOptions options)
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

internal static class PlaybackChecks
{
    public static int Bits(float value) => BitConverter.SingleToInt32Bits(value);

    public static bool Same(ClipState a, ClipState b) =>
        a.Clip.Value == b.Clip.Value && a.Layer == b.Layer && a.Mode == b.Mode && a.Mask.Value == b.Mask.Value && a.Wrap == b.Wrap && a.Synced == b.Synced &&
        Bits(a.Phase) == Bits(b.Phase) && Bits(a.Speed) == Bits(b.Speed) && Bits(a.Weight) == Bits(b.Weight) && Bits(a.Fade) == Bits(b.Fade) && Bits(a.FadeRate) == Bits(b.FadeRate);

    public static bool SameRequest(RequestState a, RequestState b) =>
        a.Key == b.Key && a.Membership == b.Membership && a.Revision == b.Revision && a.Active == b.Active;

    public static bool SameIntent(TransitionIntent a, TransitionIntent b) =>
        SameRequest(a.Request, b.Request) && Bits(a.Seconds) == Bits(b.Seconds) && Bits(a.Elapsed) == Bits(b.Elapsed) && a.Pending == b.Pending;

    public static string Describe(ClipState s) =>
        $"clip {s.Clip.Value} layer {s.Layer} mode {s.Mode} mask {s.Mask.Value} wrap {s.Wrap} synced {s.Synced} phase {s.Phase:R} speed {s.Speed:R} weight {s.Weight:R} fade {s.Fade:R} fadeRate {s.FadeRate:R}";

    public static string Describe(IPlaybackSurface surface)
    {
        var lines = new List<string>();
        for (var i = 0; i < surface.Count; i++)
            lines.Add(Describe(surface[i]));
        return string.Join(" | ", lines);
    }

    public static void SameStates(IPlaybackSurface expected, IPlaybackSurface actual, string context)
    {
        Assert.True(expected.Count == actual.Count, $"{context}: count {expected.Count} versus {actual.Count}. expected [{Describe(expected)}] actual [{Describe(actual)}]");
        for (var i = 0; i < expected.Count; i++)
            Assert.True(Same(expected[i], actual[i]), $"{context}: state {i} expected [{Describe(expected[i])}] actual [{Describe(actual[i])}]");
    }

    public static void FiniteAndBounded(PlaybackTrack track)
    {
        Assert.InRange(track.Count, 0, track.Slots.Length);
        for (var i = 0; i < track.Count; i++)
        {
            var s = track[i];
            var text = Describe(s);
            Assert.True(float.IsFinite(s.Phase) && float.IsFinite(s.Speed) && float.IsFinite(s.Weight) && float.IsFinite(s.Fade) && float.IsFinite(s.FadeRate), $"non-finite state {i}: {text}");
            Assert.True(s.Fade >= 0f && s.Fade <= 1f && s.Weight >= 0f, $"out of range state {i}: {text}");
        }
        for (var i = track.Count; i < track.Slots.Length; i++)
            Assert.True(Same(default, track.Slots[i]), $"slot {i} past the count is not clear: {Describe(track.Slots[i])}");
    }
}

public class PlaybackTests
{
    static readonly ClipIndex First = new ClipIndex(0);
    static readonly ClipIndex Second = new ClipIndex(1);
    static readonly ClipIndex Third = new ClipIndex(2);

    static PlayOptions Plain => PlayOptions.Default;

    static float Next(Random random, float limit) => (float)(random.NextDouble() * limit);

    [Fact]
    public void CommandsAreIdempotent()
    {
        var once = new PlaybackTrack(16);
        var twice = new PlaybackTrack(16);
        var clips = new[] { First, Second };
        var weights = new[] { 0.25f, 0.75f };
        once.Play(First, Plain);
        twice.Play(First, Plain);
        once.Step(0.2f);
        twice.Step(0.2f);
        once.CrossFade(Second, 0.3f, Plain);
        twice.CrossFade(Second, 0.3f, Plain);
        twice.CrossFade(Second, 0.3f, Plain);
        once.Blend(clips, weights, 0.2f, Plain.WithLayer(1));
        twice.Blend(clips, weights, 0.2f, Plain.WithLayer(1));
        twice.Blend(clips, weights, 0.2f, Plain.WithLayer(1));
        once.Stop(3, 0.1f);
        twice.Stop(3, 0.1f);
        PlaybackChecks.SameStates(once, twice, "idempotent");
        Assert.Equal(4, once.Count);
    }

    [Fact]
    public void CrossFadeEveryFrameStillAdvancesAndConverges()
    {
        var track = new PlaybackTrack(16);
        track.Play(First, Plain);
        for (var frame = 0; frame < 60; frame++)
        {
            track.CrossFade(Second, 0.25f, Plain);
            track.Step(1f / 60f);
        }
        Assert.Equal(1, track.Count);
        Assert.Equal(1f, track[0].Fade, 6);
        Assert.True(track[0].Phase > 0.4f);
    }

    [Fact]
    public void CrossFadeBackRevivesInsteadOfDuplicating()
    {
        var track = new PlaybackTrack(16);
        track.Play(First, Plain);
        track.CrossFade(Second, 1f, Plain);
        track.Step(0.3f);
        track.CrossFade(First, 1f, Plain);
        Assert.Equal(2, track.Count);
        Assert.True(track.IsPlaying(0, First));
        Assert.False(track.IsPlaying(0, Second));
    }

    [Fact]
    public void RestartCreatesAFreshState()
    {
        var track = new PlaybackTrack(16);
        track.Play(First, Plain);
        track.Step(0.4f);
        track.CrossFade(First, 0.2f, Plain.WithRestart(true));
        Assert.Equal(2, track.Count);
        Assert.Equal(0f, track[1].Phase);
        Assert.True(track[0].FadeRate < 0f);
    }

    [Fact]
    public void StopRemovesAfterTheFadeAndZeroSpeedPauses()
    {
        var track = new PlaybackTrack(16);
        track.Play(First, Plain.WithSpeed(0f));
        track.Step(0.5f);
        Assert.Equal(0f, track[0].Phase);
        track.Stop(0, 0.5f);
        track.Step(0.25f);
        Assert.Equal(1, track.Count);
        track.Step(0.25f);
        Assert.Equal(0, track.Count);
        track.Play(ClipIndex.None, Plain);
        Assert.Equal(0, track.Count);
    }

    [Fact]
    public void SyncedStatesShareOnePhaseUnderRandomCommands()
    {
        var track = new PlaybackTrack(16);
        var random = new Random(42);
        var clips = new[] { First, Second };
        for (var step = 0; step < 1000; step++)
        {
            var w = Next(random, 1f);
            if (random.Next(4) == 0)
                track.CrossFade(clips[random.Next(2)], Next(random, 0.3f), Plain);
            else
                track.Blend(clips, new[] { w, 1f - w }, Next(random, 0.3f), Plain);
            track.Step(Next(random, 0.05f));
            var phase = float.NaN;
            for (var i = 0; i < track.Count; i++)
            {
                if (track[i].Synced == 0)
                    continue;
                if (float.IsNaN(phase))
                    phase = track[i].Phase;
                Assert.Equal(phase, track[i].Phase);
            }
        }
    }

    [Fact]
    public void SameSequenceIsBitIdentical()
    {
        PlaybackTrack Run(int capacity)
        {
            var track = new PlaybackTrack(capacity);
            var random = new Random(9);
            for (var i = 0; i < 300; i++)
            {
                track.CrossFade(random.Next(2) == 0 ? First : Second, Next(random, 0.5f), Plain.WithLayer((byte)random.Next(2)));
                track.Step(Next(random, 0.1f));
            }
            return track;
        }
        var a = Run(16);
        var b = Run(16);
        var roomier = Run(64);
        PlaybackChecks.SameStates(a, b, "same sequence");
        PlaybackChecks.SameStates(a, roomier, "same sequence with spare capacity");
        Assert.True(a.Count > 0);
    }

    [Fact]
    public void ClipsAdvanceByTheirOwnDuration()
    {
        var track = new PlaybackTrack(4);
        track.Play(First, Plain);
        track.Play(Second, Plain.WithLayer(1));
        track.Play(Third, Plain.WithLayer(2).WithSpeed(2f));
        track.Step(0.25f);
        Assert.Equal(0.25f, track[0].Phase, 6);
        Assert.Equal(0.125f, track[1].Phase, 6);
        Assert.Equal(0f, track[2].Phase, 6);
        track.Step(0.1f);
        Assert.Equal(0.4f, track[2].Phase, 5);
    }

    [Fact]
    public void MissingAndNonPositiveDurationsMeanRateZero()
    {
        var odd = new[] { 0f, -1f, float.NaN, float.PositiveInfinity };
        var track = new PlaybackTrack(8, odd);
        for (var i = 0; i < 5; i++)
            track.Play(new ClipIndex((ushort)i), Plain.WithLayer((byte)i));
        for (var i = 0; i < 5; i++)
            Assert.Equal(0f, track.Rate(i));
        track.Step(0.5f);
        for (var i = 0; i < 5; i++)
            Assert.Equal(0f, track[i].Phase);
        var empty = new PlaybackTrack(2, Array.Empty<float>());
        empty.Play(First, Plain);
        empty.Step(1f);
        Assert.Equal(0f, empty[0].Phase);
    }

    [Fact]
    public void SyncedRateWeighsByWeightTimesFadeAndFallsBackToTheMean()
    {
        var track = new PlaybackTrack(8);
        track.Blend(new[] { First, Second }, new[] { 0.25f, 0.75f }, 0f, Plain);
        Assert.Equal(0.625f, track.SyncedRate(0), 6);
        Assert.Equal(0f, track.SyncedRate(1));
        track.Play(Third, Plain.WithLayer(1));
        Assert.Equal(0f, track.SyncedRate(1));
        track.Blend(new[] { First, Second }, new[] { 0f, 0f }, 0f, Plain.WithLayer(2));
        Assert.Equal(0.75f, track.SyncedRate(2), 6);
        track.Step(0.2f);
        Assert.Equal(track[0].Phase, track[1].Phase);
        Assert.Equal(0.125f, track[0].Phase, 6);
    }

    [Fact]
    public void QueriesReportPlayingFinishedAndNormalizedTime()
    {
        var track = new PlaybackTrack(8);
        Assert.True(track.HasFinished(0, First));
        Assert.False(track.IsPlaying(0, First));
        Assert.Equal(0f, track.NormalizedTime(0, First));
        Assert.Equal(-1, track.Find(0, First));
        track.Play(First, Plain.WithWrap(Wrap.Clamp));
        track.Play(Second, Plain.WithLayer(1).WithWrap(Wrap.PingPong));
        track.Play(Third, Plain.WithLayer(2).WithWrap(Wrap.Clamp).WithSpeed(-1f));
        Assert.False(track.HasFinished(0, First));
        Assert.True(track.HasFinished(2, Third));
        track.Step(0.5f);
        Assert.Equal(0.5f, track.NormalizedTime(0, First), 6);
        Assert.False(track.HasFinished(0, First));
        track.Step(0.5f);
        Assert.True(track.HasFinished(0, First));
        Assert.False(track.HasFinished(1, Second));
        track.Seek(1, Second, 1.5f);
        Assert.Equal(0.5f, track.NormalizedTime(1, Second), 6);
        Assert.True(track.IsPlaying(0, First));
        track.Stop(0, 1f);
        Assert.False(track.IsPlaying(0, First));
        Assert.Equal(0, track.Find(0, First));
    }

    [Fact]
    public void FindPrefersAPlayingStateOverAFadingOne()
    {
        var track = new PlaybackTrack(8);
        track.Play(First, Plain);
        track.CrossFade(First, 0.5f, Plain.WithRestart(true));
        Assert.Equal(2, track.Count);
        Assert.True(track[0].FadeRate < 0f);
        Assert.True(track[1].FadeRate >= 0f);
        Assert.Equal(1, track.Find(0, First));
        Assert.True(track.IsPlaying(0, First));
        track.Stop(0, 0.5f);
        Assert.Equal(0, track.Find(0, First));
        Assert.False(track.IsPlaying(0, First));
    }

    [Fact]
    public void FindTreatsASteadyStateAsPlayingNotAsFading()
    {
        var track = new PlaybackTrack(4);
        track.Put(0, new ClipState { Clip = First, Layer = 0, Weight = 1f, Fade = 0.5f, FadeRate = -1f });
        track.Put(1, new ClipState { Clip = First, Layer = 0, Weight = 1f, Fade = 1f, FadeRate = 0f });
        track.SetCount(2);
        Assert.Equal(1, track.Find(0, First));
        Assert.True(track.IsPlaying(0, First));
        track.Put(1, new ClipState { Clip = First, Layer = 0, Weight = 1f, Fade = 1f, FadeRate = -0.5f });
        Assert.Equal(0, track.Find(0, First));
        Assert.False(track.IsPlaying(0, First));
    }

    [Fact]
    public void LayersAreIndependent()
    {
        var track = new PlaybackTrack(8);
        track.Play(First, Plain);
        track.Play(Second, Plain.WithLayer(1));
        track.Play(Third, Plain);
        Assert.Equal(2, track.Count);
        Assert.Equal(Second.Value, track[0].Clip.Value);
        Assert.Equal(Third.Value, track[1].Clip.Value);
        track.Stop(1, 0f);
        Assert.Equal(1, track.Count);
        Assert.Equal(Third.Value, track[0].Clip.Value);
    }

    [Fact]
    public void BlendWithMismatchedOrNoneEntriesUsesTheCommonPrefix()
    {
        var track = new PlaybackTrack(8);
        track.Blend(new[] { First, ClipIndex.None, Second }, new[] { 0.5f, 0.5f }, 0f, Plain);
        Assert.Equal(1, track.Count);
        Assert.Equal(First.Value, track[0].Clip.Value);
        track.Blend(new[] { First, First }, new[] { 0.25f, 0.5f }, 0f, Plain);
        Assert.Equal(1, track.Count);
        Assert.Equal(0.75f, track[0].Weight, 6);
        track.Blend(Array.Empty<ClipIndex>(), new[] { 1f }, 0f, Plain);
        Assert.Equal(0, track.Count);
    }

    [Fact]
    public void SeekWrapsThePhaseByTheWrapOfTheState()
    {
        var track = new PlaybackTrack(8);
        track.Play(First, Plain);
        track.Play(Second, Plain.WithLayer(1).WithWrap(Wrap.Clamp));
        track.Play(Third, Plain.WithLayer(2).WithWrap(Wrap.PingPong));
        Assert.True(track.Seek(0, First, 1.25f));
        Assert.Equal(0.25f, track[0].Phase, 6);
        Assert.True(track.Seek(0, First, -0.25f));
        Assert.Equal(0.75f, track[0].Phase, 6);
        Assert.True(track.Seek(1, Second, 3f));
        Assert.Equal(1f, track[1].Phase);
        Assert.True(track.Seek(1, Second, -3f));
        Assert.Equal(0f, track[1].Phase);
        Assert.True(track.Seek(1, Second, 0.4f));
        Assert.Equal(0.4f, track[1].Phase, 6);
        Assert.True(track.Seek(2, Third, 3.5f));
        Assert.Equal(1.5f, track[2].Phase, 6);
        Assert.True(track.Seek(2, Third, -0.5f));
        Assert.Equal(1.5f, track[2].Phase, 6);
        Assert.True(track.Seek(2, Third, 2f));
        Assert.Equal(0f, track[2].Phase, 6);
        Assert.True(track.Seek(2, Third, 0.75f));
        Assert.Equal(0.75f, track[2].Phase, 6);
    }

    [Fact]
    public void SeekFindsTheSameStateAsFindAndReportsMisses()
    {
        var track = new PlaybackTrack(8);
        track.Play(First, Plain);
        track.CrossFade(First, 0.5f, Plain.WithRestart(true));
        var before0 = track[0];
        Assert.True(track.Seek(0, First, 0.6f));
        Assert.Equal(0.6f, track[1].Phase, 6);
        Assert.True(PlaybackChecks.Same(before0, track[0]));
        Assert.False(track.Seek(0, Second, 0.6f));
        Assert.False(track.Seek(1, First, 0.6f));
        Assert.True(PlaybackChecks.Same(before0, track[0]));
        track.Stop(0, 1f);
        Assert.True(track.Seek(0, First, 0.25f));
        Assert.Equal(0.25f, track[0].Phase, 6);
        Assert.Equal(0.6f, track[1].Phase, 6);
    }

    [Fact]
    public void SeekIgnoresNonFinitePhasesAndUnusedSlots()
    {
        var track = new PlaybackTrack(4);
        track.Play(First, Plain);
        track.Step(0.3f);
        Assert.True(track.Seek(0, First, float.NaN));
        Assert.Equal(0f, track[0].Phase);
        Assert.True(track.Seek(0, First, float.PositiveInfinity));
        Assert.Equal(0f, track[0].Phase);
        track.Put(1, new ClipState { Clip = Second, Layer = 0, Fade = 1f, Weight = 1f });
        Assert.False(track.Seek(0, Second, 0.5f));
        Assert.Equal(0f, track[1].Phase);
    }

    [Fact]
    public void RateIsSpeedOverDurationAndZeroWhenUnusable()
    {
        var durations = new[] { 2f, 0f };
        var state = new ClipState { Clip = First, Speed = 3f };
        Assert.Equal(1.5f, Playback.Rate(durations, state));
        state.Speed = float.NaN;
        Assert.Equal(0f, Playback.Rate(durations, state));
        state.Speed = -1f;
        Assert.Equal(-0.5f, Playback.Rate(durations, state));
        state.Clip = Second;
        Assert.Equal(0f, Playback.Rate(durations, state));
        state.Clip = Third;
        Assert.Equal(0f, Playback.Rate(durations, state));
        state.Clip = ClipIndex.None;
        Assert.Equal(0f, Playback.Rate(durations, state));
    }
}

public class PlaybackEvictionTests
{
    static readonly ClipIndex First = new ClipIndex(0);
    static readonly ClipIndex Second = new ClipIndex(1);
    static readonly ClipIndex Third = new ClipIndex(2);

    static PlayOptions Plain => PlayOptions.Default;

    static PlaybackTrack Full(params float[] strengths)
    {
        var track = new PlaybackTrack(strengths.Length);
        for (var i = 0; i < strengths.Length; i++)
            track.Blend(new[] { i % 2 == 0 ? First : Second }, new[] { strengths[i] }, 0f, Plain.WithLayer((byte)i));
        Assert.Equal(strengths.Length, track.Count);
        return track;
    }

    [Fact]
    public void AFullBufferEvictsTheWeakestStateAndKeepsTheOrderOfTheRest()
    {
        var track = Full(1f, 0.3f, 0.6f);
        var kept0 = track[0];
        var kept2 = track[2];
        track.Play(Third, Plain.WithLayer(7));
        Assert.Equal(3, track.Count);
        Assert.True(PlaybackChecks.Same(kept0, track[0]));
        Assert.True(PlaybackChecks.Same(kept2, track[2]));
        Assert.Equal(Third.Value, track[1].Clip.Value);
        Assert.Equal(7, track[1].Layer);
        Assert.Equal(0, track[1].Synced);
        Assert.Equal(1f, track[1].Weight);
        Assert.Equal(1f, track[1].Fade);
        Assert.Equal(0f, track[1].Phase);
    }

    [Fact]
    public void TiesGoToTheLowestIndex()
    {
        var track = Full(0.5f, 0.5f, 0.5f, 0.5f);
        var kept = new[] { track[1], track[2], track[3] };
        track.Play(Third, Plain.WithLayer(9));
        Assert.Equal(4, track.Count);
        Assert.Equal(Third.Value, track[0].Clip.Value);
        for (var i = 0; i < 3; i++)
            Assert.True(PlaybackChecks.Same(kept[i], track[i + 1]));
    }

    [Fact]
    public void StrengthIsWeightTimesFade()
    {
        var track = Full(1f, 0.5f);
        var faded = track[0];
        faded.Fade = 0.2f;
        track.Put(0, faded);
        track.Play(Third, Plain.WithLayer(5));
        Assert.Equal(Third.Value, track[0].Clip.Value);
        Assert.Equal(Second.Value, track[1].Clip.Value);
    }

    [Fact]
    public void ANonFiniteStrengthCountsAsZero()
    {
        var track = Full(1f, 1f, 1f);
        var broken = track[2];
        broken.Weight = float.NaN;
        track.Put(2, broken);
        track.Play(Third, Plain.WithLayer(5));
        Assert.Equal(Third.Value, track[2].Clip.Value);
        Assert.Equal(First.Value, track[0].Clip.Value);
        Assert.Equal(Second.Value, track[1].Clip.Value);
    }

    [Fact]
    public void NothingIsEvictedWhenTheCommandReusesAnExistingState()
    {
        var track = Full(1f, 0.3f);
        var before0 = track[0];
        track.Blend(new[] { Second }, new[] { 0.9f }, 0.5f, Plain.WithLayer(1));
        Assert.Equal(2, track.Count);
        Assert.True(PlaybackChecks.Same(before0, track[0]));
        Assert.Equal(Second.Value, track[1].Clip.Value);
        Assert.Equal(1, track[1].Layer);
        Assert.Equal(0.9f, track[1].Weight, 6);
        track.Stop(1, 0.5f);
        Assert.False(track.IsPlaying(1, Second));
        track.Blend(new[] { Second }, new[] { 0.9f }, 0.5f, Plain.WithLayer(1));
        Assert.Equal(2, track.Count);
        Assert.True(track.IsPlaying(1, Second));
        Assert.True(PlaybackChecks.Same(before0, track[0]));
    }

    [Fact]
    public void ACommandNeverEvictsTheStatesItJustPlaced()
    {
        var track = Full(0.5f, 0.4f, 0.6f);
        track.Blend(new[] { First, Second, Third }, new[] { 0.2f, 0.3f, 0.5f }, 0.25f, Plain.WithLayer(9));
        Assert.Equal(3, track.Count);
        Assert.Equal(Second.Value, track[0].Clip.Value);
        Assert.Equal(First.Value, track[1].Clip.Value);
        Assert.Equal(Third.Value, track[2].Clip.Value);
        Assert.Equal(0.3f, track[0].Weight, 6);
        Assert.Equal(0.2f, track[1].Weight, 6);
        Assert.Equal(0.5f, track[2].Weight, 6);
        for (var i = 0; i < 3; i++)
            Assert.Equal(9, track[i].Layer);
    }

    [Fact]
    public void ClipsBeyondTheCapacityAreSkipped()
    {
        var track = new PlaybackTrack(2);
        track.Blend(new[] { First, Second, Third }, new[] { 0.2f, 0.3f, 0.5f }, 0f, Plain);
        Assert.Equal(2, track.Count);
        Assert.Equal(First.Value, track[0].Clip.Value);
        Assert.Equal(Second.Value, track[1].Clip.Value);
        Assert.Equal(0.2f, track[0].Weight, 6);
        Assert.Equal(0.3f, track[1].Weight, 6);
    }

    [Fact]
    public void ARestartMayReplaceTheStateItIsRestarting()
    {
        var track = new PlaybackTrack(1);
        track.Play(First, Plain);
        track.Step(0.4f);
        track.CrossFade(First, 0.2f, Plain.WithRestart(true));
        Assert.Equal(1, track.Count);
        Assert.Equal(0f, track[0].Phase);
        Assert.True(track.IsPlaying(0, First));
    }

    [Fact]
    public void SpansLongerThanTheLimitAreCutAtTheLimit()
    {
        var track = new PlaybackTrack(Playback.MaxStates + 8);
        for (var i = 0; i < Playback.MaxStates; i++)
            track.Put(i, new ClipState { Clip = new ClipIndex((ushort)(i / 255)), Layer = (byte)(1 + i % 255), Weight = 1f, Fade = 1f, Wrap = Wrap.Loop });
        track.SetCount(Playback.MaxStates);
        var second = track[1];
        track.Play(new ClipIndex(100), Plain.WithLayer(0));
        Assert.Equal(Playback.MaxStates, track.Count);
        Assert.Equal(100, track[0].Clip.Value);
        Assert.True(PlaybackChecks.Same(second, track[1]));
        for (var i = Playback.MaxStates; i < track.Slots.Length; i++)
            Assert.True(PlaybackChecks.Same(default, track.Slots[i]));
        track.SetCount(Playback.MaxStates + 5);
        track.Step(0.1f);
        Assert.Equal(Playback.MaxStates, track.Count);
    }
}

public class PlaybackTotalityTests
{
    static readonly ClipIndex First = new ClipIndex(0);
    static readonly ClipIndex Second = new ClipIndex(1);
    static readonly ClipIndex Third = new ClipIndex(2);

    static readonly float[] Hostile =
    {
        float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue, float.MinValue, float.Epsilon, 1e-40f, 1e-30f, -0f, 0f, -1f, 0.5f, 1f, 1e30f
    };

    static PlayOptions Plain => PlayOptions.Default;

    [Fact]
    public void NonFiniteSpeedWeightAndSecondsNeverProduceANonFiniteState()
    {
        var clips = new[] { First, Second, First };
        foreach (var speed in Hostile)
            foreach (var weight in Hostile)
                foreach (var seconds in Hostile)
                    foreach (var dt in Hostile)
                    {
                        var track = new PlaybackTrack(8);
                        var options = Plain.WithSpeed(speed);
                        track.Play(First, options);
                        track.CrossFade(Second, seconds, options);
                        track.Blend(clips, new[] { weight, weight, weight }, seconds, options.WithLayer(1));
                        track.Step(dt);
                        track.Blend(clips, new[] { weight, 0.5f, weight }, seconds, options.WithLayer(1));
                        track.Seek(1, First, speed);
                        track.Step(dt);
                        track.Stop(1, seconds);
                        track.Step(dt);
                        track.CrossFade(Third, seconds, options.WithRestart(true));
                        track.Step(dt);
                        PlaybackChecks.FiniteAndBounded(track);
                    }
    }

    [Fact]
    public void HostileCommandsInAnyOrderKeepEveryStateFiniteAndInRange()
    {
        var random = new Random(77);
        var track = new PlaybackTrack(6);
        float Pick() => random.Next(3) == 0 ? Hostile[random.Next(Hostile.Length)] : (float)random.NextDouble();
        for (var step = 0; step < 4000; step++)
        {
            var options = new PlayOptions((byte)random.Next(3), random.Next(3) == 0 ? ClipMode.Additive : ClipMode.Override, new MaskIndex((ushort)random.Next(2)), (Wrap)random.Next(3), Pick(), random.Next(3) == 0);
            var clip = new ClipIndex((ushort)random.Next(4));
            switch (random.Next(8))
            {
                case 0:
                    track.Play(clip, options);
                    break;
                case 1:
                    track.CrossFade(clip, Pick(), options);
                    break;
                case 2:
                    track.Blend(new[] { clip, new ClipIndex((ushort)random.Next(4)), ClipIndex.None }, new[] { Pick(), Pick(), Pick() }, Pick(), options);
                    break;
                case 3:
                    track.Stop((byte)random.Next(3), Pick());
                    break;
                case 4:
                case 5:
                    track.Step(Pick());
                    break;
                case 6:
                    track.Seek((byte)random.Next(3), clip, Pick());
                    break;
                default:
                    track.TransitionPlaySynced((uint)random.Next(3), clip, Pick(), options);
                    break;
            }
            PlaybackChecks.FiniteAndBounded(track);
            var intent = track.Intent;
            Assert.True(float.IsFinite(intent.Seconds) && intent.Seconds >= 0f);
            for (var layer = 0; layer < 3; layer++)
            {
                Assert.True(float.IsFinite(track.SyncedRate((byte)layer)));
                Assert.True(float.IsFinite(track.NormalizedTime((byte)layer, clip)));
            }
        }
    }

    [Fact]
    public void AnEmptySpanNeverThrows()
    {
        var none = Span<ClipState>.Empty;
        var count = 0;
        var clips = new[] { First, Second };
        var weights = new[] { 0.5f, 0.5f };
        var intent = new TransitionIntent();
        Playback.Play(none, ref count, First, Plain);
        Playback.CrossFade(none, ref count, First, 0.3f, Plain);
        Playback.Blend(none, ref count, clips, weights, 0.3f, Plain);
        Playback.Stop(none, ref count, 0, 0.3f);
        Playback.Step(none, ref count, PlaybackTrack.ClipDurations, 0.1f);
        Assert.Equal(RequestDecision.Start, TransitionPlayback.Play(none, ref count, ref intent, 1u, First, 0.3f, Plain));
        TransitionPlayback.PlaySynced(none, ref count, ref intent, 2u, Second, 0.3f, Plain);
        TransitionPlayback.Blend(none, ref count, ref intent, 3u, clips, weights, 0.3f, Plain);
        Assert.Equal(0, count);
        Assert.Equal(-1, Playback.Find(none, count, 0, First));
        Assert.False(Playback.IsPlaying(none, count, 0, First));
        Assert.Equal(0f, Playback.NormalizedTime(none, count, 0, First));
        Assert.True(Playback.HasFinished(none, count, 0, First));
        Assert.False(Playback.Seek(none, count, 0, First, 0.5f));
        Assert.Equal(0f, Playback.SyncedRate(PlaybackTrack.ClipDurations, none, count, 0));
        count = 7;
        Playback.Play(none, ref count, First, Plain);
        Assert.Equal(0, count);
        count = -3;
        Playback.Step(none, ref count, ReadOnlySpan<float>.Empty, 0.1f);
        Assert.Equal(0, count);
        Assert.Equal(-1, Playback.Find(none, 9, 0, First));
        Assert.Equal(0f, Playback.SyncedRate(ReadOnlySpan<float>.Empty, none, -2, 0));
    }

    [Fact]
    public void ACountOutsideTheSpanIsClampedNotTrusted()
    {
        var slots = new ClipState[3];
        var count = -5;
        Playback.Play(slots, ref count, First, Plain);
        Assert.Equal(1, count);
        Assert.Equal(First.Value, slots[0].Clip.Value);
        count = 50;
        Playback.Play(slots, ref count, Second, Plain.WithLayer(1));
        Assert.Equal(3, count);
        Assert.Equal(-1, Playback.Find(slots, -1, 0, First));
        Assert.Equal(0, Playback.Find(slots, 50, 0, First));
        Assert.Equal(0, Playback.Find(slots, 3, 0, First));
        Assert.False(Playback.Seek(slots, 0, 0, First, 0.5f));
        count = 50;
        Playback.Step(slots, ref count, PlaybackTrack.ClipDurations, 0.1f);
        Assert.Equal(3, count);
    }

    [Fact]
    public void EmptyAndMismatchedInputSpansAreHandled()
    {
        var track = new PlaybackTrack(4);
        track.Blend(Array.Empty<ClipIndex>(), Array.Empty<float>(), 0.5f, Plain);
        track.Blend(new[] { First }, Array.Empty<float>(), 0.5f, Plain);
        track.Blend(Array.Empty<ClipIndex>(), new[] { 1f }, 0.5f, Plain);
        Assert.Equal(0, track.Count);
        track.Play(First, Plain);
        track.Blend(Array.Empty<ClipIndex>(), Array.Empty<float>(), 0f, Plain);
        Assert.Equal(0, track.Count);
        track.Step(0.1f);
        Assert.Equal(0, track.Count);
    }

    [Fact]
    public void GarbageStatesAndCountsNeverThrow()
    {
        var random = new Random(5);
        var floats = new float[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, 1f, -2f, 3.5f, float.MaxValue };
        for (var round = 0; round < 300; round++)
        {
            var track = new PlaybackTrack(1 + random.Next(6));
            for (var i = 0; i < track.Slots.Length; i++)
            {
                track.Put(i, new ClipState
                {
                    Clip = new ClipIndex((ushort)random.Next(5)),
                    Layer = (byte)random.Next(3),
                    Mode = (ClipMode)random.Next(4),
                    Mask = new MaskIndex((ushort)random.Next(3)),
                    Wrap = (Wrap)random.Next(5),
                    Synced = (byte)random.Next(3),
                    Phase = floats[random.Next(floats.Length)],
                    Speed = floats[random.Next(floats.Length)],
                    Weight = floats[random.Next(floats.Length)],
                    Fade = floats[random.Next(floats.Length)],
                    FadeRate = floats[random.Next(floats.Length)]
                });
            }
            track.SetCount(random.Next(-2, track.Slots.Length + 3));
            var layer = (byte)random.Next(3);
            var clip = new ClipIndex((ushort)random.Next(5));
            _ = track.Find(layer, clip);
            _ = track.IsPlaying(layer, clip);
            _ = track.NormalizedTime(layer, clip);
            _ = track.HasFinished(layer, clip);
            _ = track.SyncedRate(layer);
            track.Seek(layer, clip, floats[random.Next(floats.Length)]);
            track.Step(floats[random.Next(floats.Length)]);
            track.CrossFade(clip, floats[random.Next(floats.Length)], Plain.WithLayer(layer));
            track.Blend(new[] { clip, First }, new[] { 0.5f, 0.5f }, 0.2f, Plain.WithLayer(layer));
            track.TransitionPlaySynced(1u, clip, 0.2f, Plain.WithLayer(layer));
            track.Stop(layer, 0.2f);
            Assert.InRange(track.Count, 0, track.Slots.Length);
        }
    }

    [Fact]
    public void ADenormalFadeTimeIsStillAFiniteFade()
    {
        var track = new PlaybackTrack(4);
        track.Play(First, Plain);
        track.CrossFade(Second, float.Epsilon, Plain);
        Assert.True(float.IsFinite(track[1].FadeRate) && track[1].FadeRate > 0f);
        Assert.True(float.IsFinite(track[0].FadeRate) && track[0].FadeRate < 0f);
        track.Step(0f);
        PlaybackChecks.FiniteAndBounded(track);
        track.Step(0.001f);
        PlaybackChecks.FiniteAndBounded(track);
        Assert.Equal(1, track.Count);
        Assert.Equal(1f, track[0].Fade);
    }

    [Fact]
    public void NonFiniteTimeStepsLeaveTheClockWhereItWas()
    {
        var track = new PlaybackTrack(4);
        track.Play(First, Plain);
        track.Step(0.25f);
        var before = track[0];
        track.Step(float.NaN);
        Assert.True(PlaybackChecks.Same(before, track[0]));
        track.Step(float.PositiveInfinity);
        Assert.True(PlaybackChecks.Same(before, track[0]));
        track.Step(float.NegativeInfinity);
        Assert.True(PlaybackChecks.Same(before, track[0]));
    }

    static PlaybackTrack Scenario(float seconds, float weight, float speed, float dt)
    {
        var track = new PlaybackTrack(8);
        var clips = new[] { First, Second };
        var weights = new[] { weight, 0.5f };
        var options = Plain.WithSpeed(speed);
        track.Play(First, options);
        track.Step(0.1f);
        track.CrossFade(Second, seconds, options);
        track.Step(dt);
        track.Blend(clips, weights, seconds, options.WithLayer(1));
        track.Step(dt);
        track.Stop(1, seconds);
        track.Step(dt);
        track.TransitionPlay(3u, Third, seconds, options.WithLayer(2));
        track.TransitionBlend(4u, clips, weights, seconds, options.WithLayer(3));
        track.Step(dt);
        return track;
    }

    static void SameScenario(PlaybackTrack expected, PlaybackTrack actual, string context)
    {
        PlaybackChecks.SameStates(expected, actual, context);
        Assert.True(PlaybackChecks.SameIntent(expected.Intent, actual.Intent), $"{context}: intent");
    }

    [Fact]
    public void NonFiniteAndNegativeInputsActLikeZero()
    {
        foreach (var bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            SameScenario(Scenario(0f, 0.5f, 1f, 0.05f), Scenario(bad, 0.5f, 1f, 0.05f), $"seconds {bad}");
            SameScenario(Scenario(0.3f, 0f, 1f, 0.05f), Scenario(0.3f, bad, 1f, 0.05f), $"weight {bad}");
            SameScenario(Scenario(0.3f, 0.5f, 0f, 0.05f), Scenario(0.3f, 0.5f, bad, 0.05f), $"speed {bad}");
            SameScenario(Scenario(0.3f, 0.5f, 1f, 0f), Scenario(0.3f, 0.5f, 1f, bad), $"step {bad}");
        }
        SameScenario(Scenario(0f, 0.5f, 1f, 0.05f), Scenario(-2f, 0.5f, 1f, 0.05f), "negative seconds");
        SameScenario(Scenario(0.3f, 0f, 1f, 0.05f), Scenario(0.3f, -2f, 1f, 0.05f), "negative weight");
        Assert.NotEqual(Scenario(0.3f, 0.5f, 1f, 0.05f).Count, Scenario(0f, 0.5f, 1f, 0.05f).Count);
    }

    [Fact]
    public void NoCommandOrStepAllocates()
    {
        var track = new PlaybackTrack(16);
        var clips = new[] { First, Second };
        var weights = new[] { 0.3f, 0.7f };
        var options = Plain.WithLayer(1);
        void Cycle(int round)
        {
            track.Play(First, Plain);
            track.CrossFade(Second, 0.2f, Plain);
            track.Blend(clips, weights, 0.2f, options);
            track.Blend(clips, weights, 0.2f, options.WithRestart(true));
            track.TransitionPlay((uint)(round & 1), First, 0.2f, Plain.WithLayer(2));
            track.TransitionPlaySynced((uint)(round & 3) + 5u, Second, 0.2f, Plain.WithLayer(2));
            track.TransitionBlend(9u, clips, weights, 0.2f, Plain.WithLayer(3));
            track.Seek(1, First, 0.5f);
            track.Step(0.016f);
            track.Stop(1, 0.1f);
            _ = track.Find(0, First) + track.SyncedRate(1) + track.NormalizedTime(0, Second);
        }
        for (var round = 0; round < 200; round++)
            Cycle(round);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var round = 0; round < 500; round++)
            Cycle(round);
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}

public class TransitionPlaybackTests
{
    static readonly ClipIndex First = new ClipIndex(0);
    static readonly ClipIndex Second = new ClipIndex(1);
    static readonly ClipIndex Third = new ClipIndex(2);

    static PlayOptions Plain => PlayOptions.Default;

    [Fact]
    public void LocomotionSwitchPreservesGaitPhaseWithoutRestartingRepeatedCalls()
    {
        var track = new PlaybackTrack(8);
        track.TransitionPlay(1u, First, 0.4f, Plain);
        track.Step(0.73f);
        Assert.Equal(RequestDecision.Start, track.TransitionPlaySynced(2u, Second, 0.4f, Plain));
        Assert.Equal(0.73f, track[0].Phase, 5);
        track.Step(0.1f);
        var phase = track[0].Phase;
        Assert.Equal(RequestDecision.Keep, track.TransitionPlaySynced(2u, Second, 0.4f, Plain));
        Assert.Equal(phase, track[0].Phase);
        Assert.Equal(1, track.Count);
    }

    [Fact]
    public void RepeatingTheSameRequestKeepsRevisionAndPhase()
    {
        var track = new PlaybackTrack(8);
        Assert.Equal(RequestDecision.Start, track.TransitionPlay(7u, First, 0.3f, Plain));
        Assert.NotEqual(0, track.Intent.Pending);
        Assert.Equal(1u, track.Intent.Request.Revision);
        Assert.Equal(0.3f, track.Intent.Seconds, 6);
        track.Step(0.4f);
        track.ClearPending();
        Assert.Equal(RequestDecision.Keep, track.TransitionPlay(7u, First, 0.3f, Plain));
        Assert.Equal(0, track.Intent.Pending);
        Assert.Equal(1u, track.Intent.Request.Revision);
        Assert.Equal(1, track.Count);
        Assert.Equal(0.4f, track[0].Phase, 6);
    }

    [Fact]
    public void ANewKeyCutsToTheDestinationAndLeavesNoOutgoingState()
    {
        var track = new PlaybackTrack(8);
        track.TransitionPlay(1u, First, 0.5f, Plain);
        track.Step(0.3f);
        track.ClearPending();
        Assert.Equal(RequestDecision.Start, track.TransitionPlay(2u, Second, 0.5f, Plain));
        Assert.Equal(2u, track.Intent.Request.Revision);
        Assert.NotEqual(0, track.Intent.Pending);
        Assert.Equal(1, track.Count);
        Assert.True(track[0].Clip.Is(Second));
        Assert.Equal(0f, track[0].Phase);
        Assert.Equal(1f, track[0].Fade);
        Assert.True(track[0].FadeRate >= 0f);
    }

    [Fact]
    public void ReusingAKeyForADifferentRecipeIsMisuseAndChangesNothing()
    {
        var track = new PlaybackTrack(8);
        track.TransitionPlay(5u, First, 0.5f, Plain);
        track.ClearPending();
        var before = track.Intent;
        var state = track[0];
        Assert.Equal(RequestDecision.Misuse, track.TransitionPlay(5u, Second, 0.5f, Plain));
        Assert.True(PlaybackChecks.SameIntent(before, track.Intent));
        Assert.Equal(0, track.Intent.Pending);
        Assert.Equal(1, track.Count);
        Assert.True(PlaybackChecks.Same(state, track[0]));
        Assert.Equal(RequestDecision.Misuse, track.TransitionPlay(5u, First, 0.5f, Plain.WithLayer(1)));
        Assert.Equal(RequestDecision.Misuse, track.TransitionPlay(5u, First, 0.5f, Plain.WithMode(ClipMode.Additive)));
        Assert.Equal(RequestDecision.Misuse, track.TransitionPlay(5u, First, 0.5f, Plain.WithMask(new MaskIndex(1))));
        Assert.Equal(RequestDecision.Misuse, track.TransitionBlend(5u, new[] { First, Second }, new[] { 0.5f, 0.5f }, 0.5f, Plain));
        Assert.True(PlaybackChecks.SameIntent(before, track.Intent));
        Assert.True(PlaybackChecks.Same(state, track[0]));
    }

    [Fact]
    public void ChangingBlendWeightsInsideOneRecipeIsNotANewTransition()
    {
        var track = new PlaybackTrack(8);
        var clips = new[] { First, Second };
        Assert.Equal(RequestDecision.Start, track.TransitionBlend(9u, clips, new[] { 0.3f, 0.7f }, 0.4f, Plain));
        track.ClearPending();
        Assert.Equal(RequestDecision.Keep, track.TransitionBlend(9u, clips, new[] { 0.6f, 0.4f }, 0.4f, Plain));
        Assert.Equal(0, track.Intent.Pending);
        Assert.Equal(1u, track.Intent.Request.Revision);
        Assert.Equal(2, track.Count);
        Assert.Equal(0.6f, track[0].Weight, 6);
        Assert.Equal(0.4f, track[1].Weight, 6);
    }

    [Fact]
    public void StartingARestartableRequestAgainBumpsTheRevisionAndRestarts()
    {
        var track = new PlaybackTrack(8);
        track.TransitionPlay(3u, First, 0.2f, Plain.WithRestart(true));
        track.Step(0.5f);
        track.ClearPending();
        Assert.Equal(RequestDecision.Start, track.TransitionPlay(3u, First, 0.2f, Plain.WithRestart(true)));
        Assert.Equal(2u, track.Intent.Request.Revision);
        Assert.NotEqual(0, track.Intent.Pending);
        Assert.Equal(1, track.Count);
        Assert.Equal(0f, track[0].Phase);
    }

    [Fact]
    public void SecondsOfTheIntentAreNeverNegativeOrNonFinite()
    {
        var track = new PlaybackTrack(8);
        track.TransitionPlay(1u, First, float.NaN, Plain);
        Assert.Equal(0f, track.Intent.Seconds);
        track.TransitionPlay(2u, First, -4f, Plain);
        Assert.Equal(0f, track.Intent.Seconds);
        track.TransitionPlay(3u, First, float.PositiveInfinity, Plain);
        Assert.Equal(0f, track.Intent.Seconds);
        track.TransitionPlay(4u, First, 0.25f, Plain);
        Assert.Equal(0.25f, track.Intent.Seconds);
    }

    [Fact]
    public void PlaySyncedCopiesThePhaseOfTheStrongestLoopingStateOfTheLayerOnly()
    {
        var track = new PlaybackTrack(8);
        track.Put(0, new ClipState { Clip = First, Layer = 0, Wrap = Wrap.Loop, Phase = 0.4f, Weight = 1f, Fade = 1f });
        track.Put(1, new ClipState { Clip = Second, Layer = 0, Wrap = Wrap.Loop, Phase = 0.9f, Weight = 0.2f, Fade = 1f });
        track.Put(2, new ClipState { Clip = Second, Layer = 1, Wrap = Wrap.Loop, Phase = 0.7f, Weight = 5f, Fade = 1f });
        track.Put(3, new ClipState { Clip = Second, Layer = 0, Wrap = Wrap.Clamp, Phase = 0.6f, Weight = 9f, Fade = 1f, Mode = ClipMode.Additive });
        track.SetCount(4);
        Assert.Equal(RequestDecision.Start, track.TransitionPlaySynced(1u, Third, 0f, Plain));
        var index = track.Find(0, Third);
        Assert.True(index >= 0);
        Assert.Equal(0.4f, track[index].Phase, 6);
        Assert.Equal(Wrap.Loop, track[index].Wrap);
        Assert.Equal(RequestDecision.Start, track.TransitionPlaySynced(2u, First, 0f, Plain.WithLayer(2)));
        Assert.Equal(0f, track[track.Find(2, First)].Phase);
    }

    [Fact]
    public void PlaySyncedDoesNotCopyThePhaseForRestartsOrNonLoopingWraps()
    {
        var track = new PlaybackTrack(8);
        track.TransitionPlay(1u, First, 0f, Plain);
        track.Step(0.6f);
        track.TransitionPlaySynced(2u, Second, 0f, Plain.WithRestart(true));
        Assert.Equal(0f, track[track.Find(0, Second)].Phase);
        track.Step(0.5f);
        track.TransitionPlaySynced(3u, Third, 0f, Plain.WithWrap(Wrap.Clamp));
        Assert.Equal(0f, track[track.Find(0, Third)].Phase);
    }

    [Fact]
    public void PlaySyncedNeverCopiesANonFinitePhase()
    {
        var track = new PlaybackTrack(4);
        track.Put(0, new ClipState { Clip = First, Layer = 0, Wrap = Wrap.Loop, Phase = float.NaN, Weight = 1f, Fade = 1f });
        track.SetCount(1);
        Assert.Equal(RequestDecision.Start, track.TransitionPlaySynced(1u, Second, 0f, Plain));
        var index = track.Find(0, Second);
        Assert.True(index >= 0);
        Assert.Equal(0f, track[index].Phase);
        PlaybackChecks.FiniteAndBounded(track);
    }

    [Fact]
    public void MembershipIsAStableHashOfLayerModeMaskAndClipsInOrder()
    {
        Assert.Equal(1268118805u, TransitionPlayback.Membership(new[] { First }, Plain));
        Assert.Equal(286314108u, TransitionPlayback.Membership(new[] { First, Second }, Plain));
        Assert.Equal(3960362406u, TransitionPlayback.Membership(new[] { Second, First }, Plain));
        Assert.Equal(4218009092u, TransitionPlayback.Membership(new[] { First }, Plain.WithLayer(1)));
        Assert.Equal(212812234u, TransitionPlayback.Membership(new[] { First }, Plain.WithMode(ClipMode.Additive)));
        Assert.Equal(630422188u, TransitionPlayback.Membership(new[] { First }, Plain.WithMask(new MaskIndex(1))));
        Assert.Equal(895367430u, TransitionPlayback.Membership(new[] { new ClipIndex(5), new ClipIndex(7), ClipIndex.None }, Plain.WithLayer(2).WithMode(ClipMode.Additive).WithMask(new MaskIndex(3))));
        Assert.Equal(1253111735u, TransitionPlayback.Membership(ReadOnlySpan<ClipIndex>.Empty, Plain));
    }

    [Fact]
    public void TheIntentKeepsTheUnityComponentLayout()
    {
        Assert.Equal(28, Marshal.SizeOf<TransitionIntent>());
        Assert.Equal(0, (int)Marshal.OffsetOf<TransitionIntent>(nameof(TransitionIntent.Request)));
        Assert.Equal(16, (int)Marshal.OffsetOf<TransitionIntent>(nameof(TransitionIntent.Seconds)));
        Assert.Equal(20, (int)Marshal.OffsetOf<TransitionIntent>(nameof(TransitionIntent.Elapsed)));
        Assert.Equal(24, (int)Marshal.OffsetOf<TransitionIntent>(nameof(TransitionIntent.Pending)));
        Assert.Equal(0, new TransitionIntent().Pending);
    }
}

public class PlaybackParityTests
{
    const int Roomy = 256;

    static readonly float[] Speeds = { 1f, 1f, 0f, -1f, 0.5f, 2f, 1.7f };
    static readonly float[] Seconds = { 0f, 0f, 0.05f, 0.3f, 1f, 0.7f };
    static readonly float[] Steps = { 0f, 0.016f, 0.016f, 0.05f, 0.2f, 0.7f, -0.03f };
    static readonly float[] NonFiniteSpeeds = { 1f, 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity };
    static readonly float[] NonFiniteSeconds = { 0f, 0.3f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f };
    static readonly float[] NonFiniteWeights = { 0.5f, 1f, 0f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, -0.5f };
    static readonly float[] NonFiniteSteps = { 0.016f, 0.1f, 0f, float.NaN, float.PositiveInfinity, float.NegativeInfinity };

    static ClipIndex RandomClip(Random random) => new ClipIndex((ushort)random.Next(3));

    static float Pick(Random random, float[] values) => values[random.Next(values.Length)];

    static PlayOptions RandomOptions(Random random, bool nonFinite) => new PlayOptions(
        (byte)random.Next(3),
        random.Next(4) == 0 ? ClipMode.Additive : ClipMode.Override,
        new MaskIndex((ushort)random.Next(2)),
        (Wrap)random.Next(3),
        Pick(random, nonFinite ? NonFiniteSpeeds : Speeds),
        random.Next(4) == 0);

    static float RandomSeconds(Random random, bool nonFinite)
    {
        if (nonFinite)
            return Pick(random, NonFiniteSeconds);
        return random.Next(5) == 0 ? (float)(random.NextDouble() * 0.6) : Pick(random, Seconds);
    }

    static (ClipIndex[] Clips, float[] Weights) RandomRecipe(Random random, bool nonFinite)
    {
        var clips = new ClipIndex[random.Next(5)];
        for (var i = 0; i < clips.Length; i++)
            clips[i] = random.Next(8) == 0 ? ClipIndex.None : RandomClip(random);
        var weights = new float[Math.Max(0, clips.Length + random.Next(-1, 2))];
        for (var i = 0; i < weights.Length; i++)
            weights[i] = nonFinite ? Pick(random, NonFiniteWeights) : random.Next(6) == 0 ? 0f : (float)random.NextDouble();
        return (clips, weights);
    }

    static Func<IPlaybackSurface, int> RandomCommand(Random random, bool nonFinite)
    {
        var options = RandomOptions(random, nonFinite);
        var clip = RandomClip(random);
        var seconds = RandomSeconds(random, nonFinite);
        var key = (uint)random.Next(1, 4);
        switch (random.Next(11))
        {
            case 0:
                return s => { s.Play(clip, options); return -1; };
            case 1:
            case 2:
                return s => { s.CrossFade(clip, seconds, options); return -1; };
            case 3:
            case 4:
                var recipe = RandomRecipe(random, nonFinite);
                return s => { s.Blend(recipe.Clips, recipe.Weights, seconds, options); return -1; };
            case 5:
                var layer = (byte)random.Next(3);
                return s => { s.Stop(layer, seconds); return -1; };
            case 6:
            case 7:
                var dt = Pick(random, nonFinite ? NonFiniteSteps : Steps);
                return s => { s.Step(dt); return -1; };
            case 8:
                return s => (int)s.TransitionPlay(key, clip, seconds, options);
            case 9:
                return s => (int)s.TransitionPlaySynced(key, clip, seconds, options);
            default:
                var blend = RandomRecipe(random, nonFinite);
                return s => (int)s.TransitionBlend(key, blend.Clips, blend.Weights, seconds, options);
        }
    }

    static void MatchQueries(IPlaybackSurface expected, IPlaybackSurface actual, string context)
    {
        for (var layer = 0; layer < 3; layer++)
        {
            Assert.Equal(PlaybackChecks.Bits(expected.SyncedRate((byte)layer)), PlaybackChecks.Bits(actual.SyncedRate((byte)layer)));
            for (var c = 0; c < 4; c++)
            {
                var clip = c == 3 ? ClipIndex.None : new ClipIndex((ushort)c);
                var at = $"{context} layer {layer} clip {c}";
                Assert.True(expected.Find((byte)layer, clip) == actual.Find((byte)layer, clip), $"{at}: Find");
                Assert.True(expected.IsPlaying((byte)layer, clip) == actual.IsPlaying((byte)layer, clip), $"{at}: IsPlaying");
                Assert.True(expected.HasFinished((byte)layer, clip) == actual.HasFinished((byte)layer, clip), $"{at}: HasFinished");
                Assert.True(PlaybackChecks.Bits(expected.NormalizedTime((byte)layer, clip)) == PlaybackChecks.Bits(actual.NormalizedTime((byte)layer, clip)), $"{at}: NormalizedTime");
            }
        }
        for (var i = 0; i < expected.Count; i++)
            Assert.True(PlaybackChecks.Bits(expected.Rate(i)) == PlaybackChecks.Bits(actual.Rate(i)), $"{context}: Rate {i}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void RandomCommandsMatchALineForLineTranscriptionOfTheUnityBuffer(int seed) => Replay(seed, false);

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public void NonFiniteSecondsWeightsSpeedsAndStepsMatchTheUnityTranscriptionToo(int seed) => Replay(seed, true);

    static void Replay(int seed, bool nonFinite)
    {
        var random = new Random(seed);
        var reference = new PlaybackReference(PlaybackTrack.ClipDurations);
        var track = new PlaybackTrack(Roomy);
        var decisions = new int[3];
        var largest = 0;
        for (var step = 0; step < 3000; step++)
        {
            var command = RandomCommand(random, nonFinite);
            var context = $"seed {seed} step {step}";
            var expectedResult = command(reference);
            Assert.True(reference.Count <= track.Slots.Length, $"{context}: the reference outgrew the test capacity");
            var actualResult = command(track);
            Assert.Equal(expectedResult, actualResult);
            if (expectedResult >= 0)
                decisions[expectedResult]++;
            PlaybackChecks.SameStates(reference, track, context);
            Assert.True(PlaybackChecks.SameIntent(reference.Intent, track.Intent), $"{context}: intent");
            MatchQueries(reference, track, context);
            largest = Math.Max(largest, reference.Count);
        }
        Assert.True(largest >= 4, $"seed {seed}: the largest buffer was {largest}");
        Assert.True(decisions[0] > 0 && decisions[1] > 0 && decisions[2] > 0, $"seed {seed}: start {decisions[0]} keep {decisions[1]} misuse {decisions[2]}");
    }
}
