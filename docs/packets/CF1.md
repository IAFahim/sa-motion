# CF1: shared animation foundation

Owns new portable shared-animation, retarget, streaming/codec and timeline kernels in src/SA.Animation; new SO(3)/velocity-aware decay kernels in src/SA.Motion; their new tests; families/humanoid-v1.safamily; docs/shared-family-contract.md; docs/handoff/CF1.md. Existing public behavior remains intact. No Unity source, scenes, manifests or third-party package edits.

Source specification: /home/i/ruk2/plan.md. Build the difficult neutral algorithms and exact failure/lifetime contracts, then update that plan with implemented APIs and actual receipts. Engine adapters, UI and content pilot remain explicitly separate.

Done when: both library targets pass the Motion and Animation suites plus new numeric/hostile-input/end-to-end tests; source rules pass; standalone fixtures evaluate shared clips through different tables, interrupt, seek, encode/decode and reject bad data; an exact handoff names remaining integration steps.
