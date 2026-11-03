# SH5: recover the shape renderer and visible demo

Continue from sa-motion 851b06b, com.sa.unity 6e602ad and sa-demo 460a464. Read sa-motion#3 and com.sa.unity#2. Use isolated packet/SH5-shape-render-fix worktrees and the cloned sa-demo; leave the owner's ruk2 assets alone.

Owns the broken GPU test matrix call sites, renderer/upload/shader fixes supported by actual evidence, focused regression tests, a usable shape demo/view in sa-demo, and receipts/handoff. Keep portable math unchanged unless a reproduced failure requires a correction. No reference-package edits.

Done when the Unity tests compile and pass, real Synty neutral/shaped rendering has finite verified matrices and a visible measured geometry change, normal handling matches nonuniform deformation, the host scene opens/reloads and plays sensibly with visible controls, and exact limitations/commands/captures are recorded. Commit and push green fixes; never report the entire corpus/physics production gate as complete based on one demo.
