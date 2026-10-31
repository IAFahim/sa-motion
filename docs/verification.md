# First motion-kernel checkpoint

The initial checkpoint proves the portable kernel and its Unity Editor/Burst bridge. The follow-on player result validates the same artifact for the tested Linux CoreCLR/native-Burst host only; it is not an active-ragdoll controller or universal deployment certification.

Environment: .NET SDK 10.0.401, netstandard2.1/C#9 library, .NET10 test/package-consumer host; Unity 7000.0.0a12, installed Burst2 integration (package metadata 2.0.0), Entities/Physics 6.7.0 in `/home/i/ruk2`.

| Check | Actual result |
|---|---|
| Portable numerical/edge/interruption/ABI/allocation tests | 38 passed, zero failed/skipped |
| Local packed NuGet consumer | Passed; restored the package from the task-local feed, not a ProjectReference |
| Unity bridge tests | 3 passed, zero failed/skipped |
| Native execution proof | Synchronous IJob and compiled function pointer both asserted native sentinel=1; managed negative control=0 |
| Cross-runtime data | Managed Unity/Burst matched the .NET package consumer fixture within tolerance |
| Artifact/ABI | Packed and imported DLL SHA256 matched; assembly references only `netstandard`; POD size checks passed |
| Existing SA regression suite | 35 passed, zero failed/skipped, before bridge integration |
| Standalone player/native Burst | Passed on Linux CoreCLR with actual variable-input native job, measured loaded DLL hash and full numerical report; see player-verification.md |
| Bounded kernel/conversion cost probe | Pending separately; no performance claim from correctness tests |
| Delirium-derived controller | Not implemented; source/data gates remain unresolved |

Verified portable DLL SHA256:

`14cf3593e78d1c46653929f4abeabb8129d504df8c252f5b60f330958c85da4e`

The committed `packages/com.sa.motion.unity/artifact.json` and package-consumer fixture record this identity. The runtime DLL is the exact `lib/netstandard2.1/SA.Motion.dll` payload extracted from the locally packed NuGet. Its Unity importer is explicitly referenced. The Unity adapter allows engine references for Unity.Mathematics; the portable assembly itself has none.

Actual verification commands:

```bash
TMPDIR="$PWD/artifacts/tmp" NUGET_PACKAGES="$PWD/artifacts/nuget" DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 dotnet restore tests/SA.Motion.Tests/SA.Motion.Tests.csproj --force --locked-mode
TMPDIR="$PWD/artifacts/tmp" NUGET_PACKAGES="$PWD/artifacts/nuget" DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 dotnet test tests/SA.Motion.Tests/SA.Motion.Tests.csproj --configuration Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false --logger 'trx;LogFileName=portable.trx' --results-directory artifacts/test-results
python3 eng/pack_bridge.py
unity command --project-path /home/i/ruk2 test run --mode EditMode --filter SA.Motion.Unity.Tests --filter_type assembly --include_explicit false --async_tests true
unity command --project-path /home/i/ruk2 test status
```

The workspace links `packages/com.sa.motion.unity` through a local UPM file dependency and lists it in manifest `testables`. Resolve packages and compile before testing. Raw local results are in `artifacts/test-results/portable.trx`, `artifacts/package-smoke.json`, and Unity Pipeline's test-status output; artifacts are ignored rather than published with logs/caches.

A pre-pause attempt using shared temporary/cache state failed (SIGBUS from the old launch and later missing shared NuGet testhost binaries). The successful run uses task-owned compiler temp and package caches; the current ruk2 Editor starts and tests normally. No system/cache cleanup or modification of the other user's Editor was performed. No performance or multi-platform deployment result is claimed.
