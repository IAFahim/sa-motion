# Cost probe

Measures `PoseTransition.Apply` and the Unity/Motion conversions in the Editor under Burst.

```bash
python3 eng/stage_cost_probe.py --project /home/i/ruk2
unity command script recompile
unity command script eval_file --file run.cs
```

`run.cs` is one line: `return SA.Motion.Unity.CostProbe.MotionCostProbe.Run("<output>.json");`

The probe throws unless the Burst function pointers report native execution and the managed control reports managed execution. The managed control calls an unattributed twin of the kernel, because a managed call to a `[BurstCompile]` method is redirected to native code.

Results and scope: `docs/verification.md`, `docs/cost-result.json`.
