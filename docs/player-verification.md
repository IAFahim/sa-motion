# Linux CoreCLR player proof

The reviewed variable-input proof built successfully and the actual player exited **0**. It validates the current-host portion of plan Gate R/P0B: .NET10, managed Unity, native Editor Burst and the tested Linux CoreCLR player consume the same netstandard2.1 artifact. Gate R is satisfied for that declared backend only. Full P0B also asks for bounded kernel/conversion cost measurements; those are still pending at this player checkpoint. It does not certify IL2CPP, other platforms, performance, active ragdolls or the source-derived controller.

The [recorded result](player-proof-result.json) contains observed position, all quaternion components, scale, scalar derivatives and tolerance. Native job sentinel=1, managed control=0, native/managed maximum error=0. Fixture comparison passes with absolute/component tolerance 2e-4; quaternion comparison accepts equivalent signs. Job inputs arrive from runtime JSON through NativeArray data, rather than all being constants embedded in the native entry point.

Expected fixture, independently verified build input and measured loaded-player DLL all have SHA256:

`14cf3593e78d1c46653929f4abeabb8129d504df8c252f5b60f330958c85da4e`

The built `Managed/SA.Motion.dll` was also independently hashed outside the player. `Plugins/lib_burst_generated.so` is present. The final graphics-enabled batch player's log has zero `Exception:`/`ERROR:` lines. An earlier `-nographics` experiment triggered unrelated whole-project graphics-world initialization errors and is not the clean proof result; no changes were made to those systems.

Build: Unity7000.0.0a12, Linux CoreCLR, development build, zero errors and two warnings. One warning notes that Assembly.Location may be empty in Unity stream-loaded contexts; this probe handles absence as a separately labelled verified-build-input path, while the actual tested run measured an available loaded path. The second is an existing TextMeshPro shader pragma deprecation. No IL2CPP result is claimed. A deliberately unwritable task-owned report path was tested separately and exited **3**, confirming report failure does not leave the proof process running.

Canonical sources are `packages/com.sa.motion.unity/Samples~/PlayerProof` and `eng/stage_player_probe.py`/`eng/build_player_probe.cs`/`eng/run_player_probe.py`. Reproduce with a running Editor linked to the canonical UPM bridge:

```bash
python3 eng/stage_player_probe.py --project /home/i/ruk2
unity command --project-path /home/i/ruk2 script recompile
unity command --project-path /home/i/ruk2 script recompile_status
unity command --project-path /home/i/ruk2 --timeout 600 script eval_file --file /home/i/GitHub/sa-motion/eng/build_player_probe.cs --timeout 600
python3 eng/run_player_probe.py
```

The stager retains generated `Assets/SA.Motion.Proof`, guarded by `.sa-motion-proof-owned`; it refuses to overwrite an unowned directory. The builder checks that same marker, verifies the artifact identity, checks scene saving, uses only its explicit additive proof scene and restores the previous active scene. The staging directory is retained for review/rebuild, not claimed to be automatically cleaned. Do not delete unrelated project assets or caches. Build binaries, raw logs and caches remain ignored under `artifacts/`, not in the commit.

Raw evidence: `artifacts/player-proof-final.json`, `artifacts/player-proof-final.log`, `artifacts/player/build-summary.txt`, `artifacts/player/build-warnings.txt`; the successful process exit was observed by the command runner. The published result is a small sanitized numerical record, not a dump of the Editor log.
