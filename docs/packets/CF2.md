# CF2: optional runtime shape addon

Base: b7fdaec, including the CF1 kernels and the subsequent C08 family publication. Worktree: /home/i/GitHub/sa-motion-wt/CF2. Branch: packet/CF2-runtime-shape-addon. Other worktrees and their uncommitted verification work are not owned by this packet.

Implement SA.Shape as a separate optional neutral package. SA.Animation must not depend on it. The only animation-core seams are a generic geometry overlay for retargeting and staging/commit access for transactional pose composition. Existing APIs/clip bytes remain compatible.

Owns src/SA.Shape, tests/SA.Shape.Tests, package/project wiring, eng/check_shape_package.py, minimal RetargetTable and TimelinePose extensions plus their regression tests, and shape contract/handoff/plan documentation. No Unity scripts, scenes, prefabs, package manifest or Rukhanka/UMA source edits. No UMA source is copied.

Done when: bounded parameters rebuild from immutable rest data without accumulation; positive length and skin deformations remain separate; per-character geometry works with shared clips; shape-neutral transition history handles changing proportions; staged failure preserves all committed outputs/state; normal matrices and updated contact/IK data are available; both library targets and legacy regressions pass; a handoff records exact receipts and remaining renderer/UI/physics work.
