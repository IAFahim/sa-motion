# Handoff R2b: balance tick repair

## Baseline (origin/integrate/r2, dcab749)

`StandTenSeconds`: `stand pelvis start 1.0000 end 0.3289 maxDrift 0.6734 maxTilt 42.15 supported 175/600 decision Falling firstFall 175`.
Push/latch/rerun/hostile unmeasured behind the stand failure.

## Experiment log (one line each: change + stand before -> after)

- E00 baseline, no change: firstFall 175, end 0.3289, maxTilt 42.15 (red).
- E01 WriteLegDrives knee +e -> -e (matches FixKnee, contract.md): firstFall 175 -> 175, end 0.3289 -> 0.3289 (no-op as predicted, FixKnee overwrote it).
- E02 knee A/B (FixKnee +error, WriteLegDrives -error): firstFall 175 -> 55, end 0.3225, maxTilt 39.82. -error wins; matches HarnessWorld RotX90 foot-backward analysis.
- E03 delete FixKnee (WriteLegDrives now identical): firstFall 175 -> 175. Pure no-op confirmed; knee sign settled at actual-desired.
