# Documentation index

Start with `README.md`, then `AGENTS.md` for the repo rules, then the group you need below.

## Start here

| Document | Purpose |
|---|---|
| `README.md` | Five-minute overview: purpose, layers, checks, licences, not built yet |
| `docs/architecture.md` | Layers and the animation-to-physics-to-bones data flow |
| `docs/demo.md` | What the Reactions demo shows, its checks, and what it is not |

## Evidence, measured not claimed

| Document | Purpose |
|---|---|
| `docs/verification.md` | Portable, bridge, and cost-probe results with the exact commands |
| `docs/player-verification.md` | Linux CoreCLR player proof and its limits |
| `docs/gate-m.md` | Two-body drive fixture: setup, steady errors, open items 6 and 7 |
| `docs/demo/reactions-demo.png` | Captured frames from the headless demo run behind `docs/demo.md` |
| `docs/cost-result.json` | Raw bounded cost-probe report consumed by `docs/verification.md` |
| `docs/player-proof-result.json` | Recorded player-proof numbers behind `docs/player-verification.md` |

## Design, how the pieces work

| Document | Purpose |
|---|---|
| `docs/profile.md` | Humanoid mass/limit sources: published-derived vs authored values |
| `docs/build.md` | `RagdollBuilder` API turning a mapped skeleton into body/joint frames |
| `docs/harness.md` | `SA.Harness` API: deterministic 15-body tuning simulator, test support only |
| `docs/provenance.md` | Gate L licence record: what is MIT, what is GPL-3.0-only, and the risk |
| `docs/port/R1.md` through `R8.md` | One port record per merged controller packet: source lines, slot map |
| `packages/com.sa.unity.reactions/README.md` | Reactions package setup, behavior, and limits inside Unity |

## Process, how the work is organized

| Document | Purpose |
|---|---|
| `docs/work-packets.md` | Packet waves, ownership, and what is still blocked |
| `docs/packets/<ID>.md` plus `R-common.md` | One spec per packet; `R-common.md` holds the shared port rules |
| `docs/handoff/<ID>.md` | Per-packet completion note with the real commands and outputs |
+
| `CHANGELOG.md` | Released vs unreleased behavior changes |
