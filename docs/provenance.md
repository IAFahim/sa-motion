# Provenance and licence record (Gate L)

Recorded 2026-10-31. Decision taken by the assistant on the owner's instruction "do whatever with licence and just complete that". This is an engineering record, not legal advice.

## Components

| Component | Origin | Licence |
|---|---|---|
| `src/SA.Motion`, `src/SA.Humanoid`, `packages/com.sa.motion.unity`, `com.sa.unity` | Written for this project from published mathematics and standard mechanics. `SA.Humanoid` profile values cite de Leva (1996) and Winter. No upstream code. | MIT |
| `src/SA.ReactionControl` and its tests | Source-level translation and adaptation of controller functions from Delirium Physics, plus original code. | GPL-3.0-only |

## Upstream

- Repository: https://github.com/PVCPC/delirium-physics, revision `8e4ad4e`, declared `GPL-3.0-only` (`Cargo.toml`, `LICENSE`).
- Upstream's own description: "a reverse-engineered port of Euphoria, NaturalMotion's ragdoll and character physics engine, as it runs in GTA IV: rewritten in Rust from the game's own program code and checked against the running game".
- Upstream's numbers (its tuning bank) are read from the user's own GTA IV and are not in the repository.

## Decision

1. All code translated or adapted from Delirium lives only under `src/SA.ReactionControl` and `tests/SA.ReactionControl.Tests`. Those directories carry the GPL-3.0 `LICENSE` and a `NOTICE.md` naming the upstream and revision. No per-file comment headers, because source comments are disallowed; the directory-level notices are the notice mechanism, and this is the first thing a lawyer should review.
2. The MIT projects never import Delirium code. `SA.ReactionControl` may reference them, which the GPL permits.
3. Any build or package containing `SA.ReactionControl` is a combined work that must be offered under GPL-3.0 if it is ever distributed. Therefore: no registry publication, no public repository, no binary release that includes it. Everything stays private.
4. No GTA files, no `kv.bin`, no values decoded from the game, no unlock-gate bypass. Every tuning slot becomes a named parameter with an authored starting value and an entry in the port record. The runtime must run with no GTA data present.
5. Every translated function has a port record in `docs/port/` (source name and line, C# name, basis, units, slot map, what was excluded or adapted).

## Risk the owner carries

The GPL applies to the Rust authors' rewrite. It does not establish that the underlying original program logic, which upstream says it reverse-engineered, is free of third-party rights. Keeping the derived part private and out of any distribution is what contains that risk; releasing it would need real legal advice first.
