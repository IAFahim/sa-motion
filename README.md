# SA.Motion

Engine-independent, unmanaged finite-duration inertialization kernels. The library targets .NET Standard 2.1; standalone tests use .NET 10. Unity consumes the exact packed DLL through a thin UPM bridge.

`ScalarDecay` uses a normalized-time quintic. `VectorOffset`, `RotationOffset` and `PoseTransition` capture offsets from corrected output histories and apply them over the current destination. Callers own state, timestamps and buffers. No Unity dependency, allocation, clock or solver is hidden in the kernels.

The radial construction preserves the initial pose in valid cases and has finite-duration zero value/velocity/acceleration endpoints. It does not guarantee unconditional full-vector initial velocity continuity: perpendicular velocity is projected away and away-going radial velocity is clamped. Degenerate-duration and signed-scale policies can choose the destination directly.

The independently authored implementation follows the mathematical specification in David Bollo's [GDC 2018 slides](https://media.gdcvault.com/gdc2018/presentations/bollo_david_inertialization_high_performance.pdf). It contains no Delirium controller translation, gated tuning or game assets. The separately planned controller port remains subject to its source-rights/data gates.

NuGet and Unity/Burst/player compatibility are measured independently. Local package output is not a registry publication.

## Use

```csharp
var history = new PoseHistory();
history.Reset(currentOutput);
var correction = PoseTransition.Create(history, destination, 0.3f);
var output = PoseTransition.Apply(ref correction, destination, elapsedSeconds);
```

Retain corrected output history with its actual sample interval and rebuild offsets on a new request. `Apply` takes state by ref so invalid signed destination scale clears the scale-only correction rather than reviving it later. Invalid quaternion history contributes no invented velocity.

Canonical production code is `src/SA.Motion`; `packages/com.sa.motion.unity` contains only the pinned DLL, Unity conversions and proof tests. `eng/pack_bridge.py` builds a local NuGet, verifies its consumer/hash, and stages the exact artifact. Unity projects can add that canonical package location as a local UPM file dependency and add it to manifest `testables` for the proof suite.

The [verification record](docs/verification.md) reports 38 passing .NET tests, the local NuGet consumer and 3 native-Burst/managed Editor bridge tests. Standalone player/AOT verification is still pending. [Provenance](docs/provenance.md) explains the independent current implementation and the separately gated future controller work.
