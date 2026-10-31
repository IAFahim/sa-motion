# Hinge axis and range inference (N12)

Namespace `SA.Humanoid.Hinge`. Independent MIT code: no Delirium input.

## API

```csharp
readonly struct HingeEstimate { Float3 Axis; float Min; float Max; float Fit; bool Valid }
static class HingeInference { HingeEstimate Estimate(Quaternion rest, ReadOnlySpan<Quaternion> relativeSamples) }
```

`relativeSamples` are child-in-parent rotations. `rest` is the rest child-in-parent
rotation. `Axis` is a unit vector in the parent-local frame (up to sign). `Min`
and `Max` are angles in radians about `Axis` relative to `rest`, wrapped to the
shortest arc (`Min <= Max`). `Fit` in 0..1 is the share of rotation energy about
`Axis` (1 is a perfect hinge).

## Method

Each sample is reduced to its parent-frame delta `sample * inverse(rest)`, mapped
to a rotation vector (axis times angle, canonicalized so `q` and `-q` agree), and
accumulated into a 3x3 covariance plus total energy. The axis is the dominant
eigenvector (power iteration, fixed 32 steps, seeded by the largest diagonal, so
the result is deterministic). Per-sample signed angles about that axis give
`Min`/`Max`; `Fit` is the aligned energy share. Only stack locals are used; the
input span is read twice and never stored.

## Validity

`Valid` is false when any of these hold:

| Condition | Threshold |
|---|---|
| Fewer than 3 samples | `HingeInference.MinSamples = 3` |
| Any sample or `rest` is not a valid rotation (non-finite, zero-length) | — |
| Motion range `Max - Min` under | `HingeInference.MinMotion = 0.05` rad |
| `Fit` below | `HingeInference.MinFit = 0.8` |

Invalid estimates still return finite fields (`Axis` falls back to +X, angles to
0, `Fit` clamped to 0..1). No input throws: every bad value maps to an invalid
estimate with finite fields.

## Notes

- The axis sign is arbitrary; `Min`/`Max` flip with it. Compare axes with
  `abs(dot)` and ranges up to an overall sign.
- Angles are shortest-arc, so inferred ranges stay inside `[-pi, pi]`; clips that
  wind past half a turn are out of scope.
- A single non-finite or zero-length sample invalidates the whole estimate
  rather than being skipped, so one corrupt frame can never silently bias the
  axis.
