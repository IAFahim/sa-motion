# Playback over fixed-capacity spans

`SA.Animation.Playback` and `TransitionPlayback` decide what a character plays. They are the portable form of the Unity `Playback` and `TransitionPlayback`: the `DynamicBuffer<ClipState>` becomes a plain `Span<ClipState>` plus an `int count`, and nothing allocates.

```
var states = new ClipState[8];
var count = 0;
Playback.CrossFade(states, ref count, walk, 0.2f, PlayOptions.Default);
Playback.Step(states, ref count, durations, deltaTime);
```

## The buffer

- Live states are `states[0..count)`. Order is the Unity order: a new state goes at the end, a removal shifts the later states down. Slots a command vacates are cleared, so a whole span compares equal when its live part does.
- The capacity is `min(states.Length, Playback.MaxStates)`, and `MaxStates` is 1024. Every function clamps `count` into `[0, capacity]`, and the commands that take `ref int count` write the clamped value back, so a bad count cannot index outside the span. Slots beyond `MaxStates` are never read or written. The limit exists so the one small scratch flag array stays on the stack for any span length.
- `durations` is a `ReadOnlySpan<float>` indexed by `ClipIndex.Value`. A missing, zero, negative or NaN duration means rate 0, like an invalid `ClipAsset` in Unity.

## Full buffer rule

When a command must add a state and `count` equals the capacity, it replaces the live state with the smallest `Weight * Fade`. The lowest index wins a tie, and a product that is not finite counts as 0. The new state is written into the replaced slot, so the others keep their order. A state that the same command already placed or reused is never replaced, so a `Blend` cannot evict its own members. If nothing can be replaced, that clip is skipped. With an empty span every command does nothing.

A command that reuses an existing state (the same clip, layer, mode, mask and synced flag, playing or fading) adds nothing and evicts nothing.

## Seek

`Seek(states, count, layer, clip, phase)` sets the phase of the state `Find` returns, wrapped by that state's wrap with `Phase.Wrapped`, and returns whether it found one. It moves that one state only. The other members of a synced group keep their own phase, so seek each member to keep the group together.

## Transitions

`TransitionIntent` is `Request`, `Seconds`, `Elapsed` and `Pending`, 28 bytes like the Unity component. `Pending` is a byte, 0 or 1, as `ClipState.Synced` is. `TransitionPlayback.Play`, `PlaySynced` and `Blend` return `Start`, `Keep` or `Misuse` from `TransitionRequests.Decide`, keyed by `Membership`, a hash of layer, mode, mask and the clip indices in order. `Start` sets `Request`, `Seconds` and `Pending`, then plays with a restart and no fade. `Keep` re-issues the same command on the existing states without a restart, so a repeated call rewinds nothing and a `Blend` only moves its weights. `Misuse` changes nothing. `PlaySynced` gives the new state the phase of the strongest looping state of the layer, unless the call restarts or does not loop.

## Totality

Speed, weights, seconds and delta time that are NaN or infinite count as 0, and negative seconds and weights count as 0, as in Unity. A fade time so small that its rate overflows is capped at `float.MaxValue`, so no state field becomes NaN or infinite and `Fade` stays in `[0, 1]`. Phases always go through `Phase.Wrapped`. No function throws.

## Differences from the Unity code

- The buffer has a capacity and the full buffer rule above; Unity grows without limit.
- `Synced` and `Pending` are bytes, so the structs stay blittable.
- `Step` needs no scratch array for the rates: it advances every phase first, then the fades. The synced rate does not read the phase, so the result is the same.
- Counts are clamped, vacated slots are cleared, and the non-finite handling above is a little stricter than Unity's.

`PlaybackParityTests` replays random commands on a line-for-line copy of the Unity buffer code and on this code, and requires the same states bit for bit while the buffer never fills. The Unity Editor was not run.
