# Rules shared by every R packet (controller port)

Read `AGENTS.md`, `docs/provenance.md`, this file, then your packet. The upstream checkout is `/home/i/GitHub/delirium-physics` at revision 8e4ad4e. The plan is `/home/i/ruk2/Packages/com.sa.unity/plan.md` (sections 3, 8, 9).

## Licence

You are allowed and expected to read and translate the cited Rust ranges. Translated code goes only under `src/SA.ReactionControl/<Area>/` and `tests/SA.ReactionControl.Tests/<Area>/`. Never put it in `SA.Motion` or `SA.Humanoid`.

## Hard stops

- No GTA files, no `kv.bin`, no decoded game numbers, no unlock bypass. The code must run with nothing from the game.
- No comments in code. Explanations go in `docs/port/<ID>.md`.
- Do not guess what an opaque field means. Name a field after behavior you verified by reading how it is used. Otherwise keep a neutral name such as `Opaque148` and list it in the port record as unresolved.

## How to translate

- Do not emulate the console arithmetic (`fma` ordering, `frsqrte`, `vrefp`, bit-exact estimates). Use ordinary float math. Say so once in the port record.
- Types: `Vec3` and `Quat` become `Float3` and `Quaternion` (`SA.Motion.Numerics`). The 3-axes-plus-position matrix `M34` becomes `SA.Motion.Mechanics.Frame`. Engine types (`Character`, `Ragdoll`, `Collision`, `Obstacle`, `Ctx`) never appear. Inputs are `ReadOnlySpan<RoleBody>` (length `Roles.Count`, indexed by `(int)Role`), `SupportProbe`, and your own small input structs. Outputs are `Span<JointDrive>` (same indexing) and small result structs.
- The source has 21 parts, four spine segments, a neck and clavicles. We have the 15 roles in `src/SA.Humanoid/Roles.cs`. Where the source indexes a spine segment, collapse it onto `Torso` or `Pelvis` and write down the collapse.
- Each `kv!(n)` slot becomes a named field of your `<Area>Params` struct with an authored starting value and a one-line reason. Provide `<Area>Params.Standard`, never an all-zero default.
- Pure static functions plus small POD state structs the caller owns. No allocation, no statics that change, deterministic, total: a non-finite input gives a passive output (`JointDrive.Passive`, zero intent), never an exception.
- Where a function depends on native solver internals and has no meaningful translation, write the standard-mechanics replacement and mark it `ADAPTED` in the port record. Behavior acceptance matters, not line-by-line equivalence.

## Port record first

Before code, write and commit `docs/port/<ID>.md`: a table of source function (file:line), C# name, inputs and outputs with units and basis, the slot map, what was excluded or adapted and why. Then code. Keep the record in sync as you go.

## Tests

Synthetic fixtures from your packet's "Check" list. Add a hostile-input test (NaN, infinity, huge values) asserting finite, bounded output. Run `eng/dotnet_test.sh tests/SA.ReactionControl.Tests/SA.ReactionControl.Tests.csproj` and `python3 eng/check_source_rules.py`.

## Shared contract (do not edit)

`src/SA.ReactionControl/Contracts/*` belongs to the integrator: `RoleBody`, `SupportProbe`, `JointDrive`. If you need a change, put the request in your handoff and use a local adapter struct meanwhile. Your own types go in your Area folder.

## Finish

Same as the other packets: handoff note `docs/handoff/<ID>.md`, push `packet/<ID>`, never push main. Mention every unresolved field, every ADAPTED function, and what you could not test.
