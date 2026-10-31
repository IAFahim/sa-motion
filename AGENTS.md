# Rules for every agent working in this repository

Read this file and your one work packet in `docs/work-packets.md`. Nothing else is required.

## What this repository is

An engine-agnostic .NET motion library plus thin Unity adapters. The goal is that the logic can move to any engine and Unity only converts, schedules and calls.

## Where code goes

| If the code... | It lives in |
|---|---|
| compiles without Unity | `src/` (netstandard2.1, C# 9) |
| must touch Entities, Physics, Burst attributes or rendering | `packages/` |
| exchanges data with a physics engine | `src/` as plain structs: snapshot in, intent out. Never an interface, never a callback |

Default to `src/`. Move code to `packages/` only when it cannot compile without Unity, and say why in your handoff note.

## Code rules (checked by `python3 eng/check_source_rules.py`)

- No comments. Names carry the meaning. Explanations go in `docs/`.
- Pure functions over `readonly` data. Same input, same output. No hidden state, no statics that change.
- Total functions: every input has a defined output. Return a default or a diagnostic. Do not throw on bad data.
- Plain structs with `StructLayout.Sequential`. No managed allocation per call. No reflection. No LINQ.
- Small orthogonal pieces that compose. If you delete a piece, only its own tests fail.
- Do not rename or reformat code you did not need to change.

## Commands

| Command | Time | Use |
|---|---|---|
| `eng/dotnet_test.sh` | seconds | every change in `src/` and `tests/` |
| `python3 eng/check_source_rules.py` | instant | before every commit |
| `eng/unity.sh compile` | about 5 minutes | only for packages/ work |
| `eng/unity.sh test <assembly>` | minutes | only for packages/ work |

`eng/unity.sh` holds a lock, because one Editor serves everyone. Never start a second Editor. Never run Unity commands for a pure `src/` packet.

## Hard stops

- No code translated or adapted from Delirium until `docs/provenance.md` records the Gate L decision. That is a human decision. Language, names or storage layout changing does not clear it.
- No GTA files, no `kv.bin`, no extracted game tuning, no unlock bypass.
- Never delete or loosen an assertion, a guard or a test to get green. Fix the cause.
- Never claim a result you did not run. Write "not measured" for anything you did not measure.

## Working protocol

1. One packet, one branch, one worktree: `git worktree add ../sa-motion-wt/<ID> -b packet/<ID>`. Unity packets run in the main checkout, one at a time, under the lock.
2. Commit after every green step. A WIP commit is fine if the message says exactly what fails and why.
3. If you are told you are low on budget, stop and write `docs/handoff/<ID>.md` at once: done, not done, the exact failing command and its output, and the next step. Commit and push the branch.
4. Push your branch, never `main`. The integrator merges.
5. You are done only when the packet's "Done when" line is true and `docs/handoff/<ID>.md` exists.

## Reporting

End with: what changed, the commands you ran with their actual results, what you did not verify. Numbers come from runs, not memory.
