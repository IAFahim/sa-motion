# Cost probe — saved WIP

This source is preserved for handoff. It is **not verified cost evidence** and does not complete the remaining P0B cost requirement.

The source compiled in the current Unity Editor. One invocation was attempted through the CLI and correctly stopped with:

`Native cost proof fell back to managed execution`

No timing report was accepted or recorded. Investigate that sentinel/invocation path before interpreting any timings; do not remove the native-execution assertion merely to obtain numbers. The already reviewed and published core/Editor/player proofs remain separate: commits `2316a28` and `cbab818`, DLL SHA256 `14cf3593e78d1c46653929f4abeabb8129d504df8c252f5b60f330958c85da4e`.

`eng/stage_cost_probe.py` stages the canonical source into a marker-guarded task-owned `Assets/SA.Motion.CostProof` directory. The intended probe uses 64 runtime cases, warmup, five bounded samples, checksum validation and native/managed sentinels. It reports loop/boundary-inclusive kernel and conversion/baseline batch costs, not isolated conversion instructions, a crowd result or a frame-time guarantee. None of those planned measurements has passed verification yet.

No physics/controller source was started in this handoff. Delirium-derived source/data gates remain unresolved. Generated staging, raw logs and build artifacts are not part of this commit.
