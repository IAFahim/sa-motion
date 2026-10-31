# Source and data provenance

The current `SA.Motion` library is independently authored from the mathematical specification of radial finite-duration inertialization in David Bollo's [GDC 2018 presentation](https://media.gdcvault.com/gdc2018/presentations/bollo_david_inertialization_high_performance.pdf). Its normalized-time coefficients are the same quintic expressed in `u=t/T`; endpoint and edge contracts are tested. Quaternion/vector elementary operations are implemented directly from their mathematical definitions.

Current code contains no translated Delirium controller functions, native solver, encrypted tuning bank, game constants, character template or proprietary animation assets. MIT applies to this independently authored checkpoint; it is not a license declaration for the future controller port.

The planned `SA.ReactionControl` source-level port is separate, not yet created, and remains gated on an actual rights/notices/distribution decision and independently documented physical/control data. A language rewrite, separate assembly/repository or private visibility does not settle those obligations. See the implementation plan and tracking issue: https://github.com/IAFahim/com.sa.unity/issues/1 .

Unity integration is a thin caller/conversion/proof layer; it consumes the same portable DLL as the local NuGet consumer. HLSL/GPU animation integration and physical bodies/controller behavior are not claimed. The follow-on player proof covers Linux CoreCLR/native Burst only; no IL2CPP or other-platform result is inferred. No package was published to nuget.org.
