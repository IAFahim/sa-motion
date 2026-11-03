# SH5: shape shader and view recovery

Continues sa-motion 851b06b (`packet/SH1-shape-importer`), com.sa.unity 6e602ad (`packet/SH4-physical-sync`) and sa-demo 460a464. Work is isolated on `packet/SH5-shape-render-fix` in both code repositories and `fix/SH5-shape-render` in sa-demo. The owner's ruk2 scene/assets and existing package checkout were not changed.

## Fixed

- Completed the test-side migration to 64-byte GPU matrices. CPU/blob affine storage remains float3x4; GPU uploads/readbacks use float4x4. Parity checks include the homogeneous row rather than reading a 64-byte buffer into 48-byte elements.
- Corrected canonical shoulder token scoring, preserving the existing failing assertion and the authored Synty aliases.
- Changed the skin shader to inverse-transpose the blended position matrix for normals. Tangents use the forward linear transform and are orthogonalized to the resulting normal. Singular/nonfinite direction calculations have finite fallbacks. The CPU tangent helper now accepts the position matrix too.
- Explicitly ordered shared and shape palette uploads before EntitiesGraphicsSystem. Retained the existing NoSkin guard for shaped rigs so compute does not overwrite their CPU palette.
- Added GPU tests that execute the actual Skinning.hlsl on a nonuniform blended matrix, a singular palette and an unskinned vertex. Existing GPU pose/palette parity tests still run.
- Made the source-audit test resolve its package directory through PackageInfo, so it works with normal Git UPM packages as well as embedded packages.
- Fixed eng/unity.sh polling: `up_to_date` is a successful compile status, not a reason to wait for the full polling timeout. CLI calls carry the requested unity-cli skill labels.

## Real demo

The published snapshot had a character subscene but no saved host connecting it to a camera. The original package links were absolute `/home/os/...` symlinks. sa-demo now has a durable `Assets/Scenes/ShapeWalk.unity` host, permanent character/profile/material assets, camera, front lighting, floor, and visible proportion/clip controls. The source material and original atlas are preserved as a real asset. During relocation one scratch material reference was missed; the restart exposed it, it was repaired, and the saved scene was reloaded again.

The example profile now pairs child joint-length changes with the corresponding parent skin stretch. Each stretch basis follows the actual child offset. It uses reference grounding and separately controls both legs, the left leg and the right arm. It no longer mistakes hip spacing or foot stretch for leg length. The profile target remains `00d874d44f1568594fb86f649d38d08bbf2f265b208a56b2a356817912cf3f13`; the revised definition is `4970b2ae4b6ac3160d98701c845a6c3d6a64279fc60182277aa9ac543666f6ba`.

Use the Shape workshop Game view resolution (1280 x 720). The default CLI capture resizes an existing Game view texture; it does not necessarily change the rendering resolution. Capturing a narrow Game view as 1280 x 720 had distorted the image. Set the actual resolution first with the public PlayModeWindow API, or use the demo's Frame Game View menu. Captures come from inside Unity, never the desktop.

## Verification receipts

- Unity 7000.0.0a12, Intel RPL-S graphics. SA.Tests: 94 passed, zero failures/skips on OpenGL during the repair and on Vulkan. Final Vulkan shader compilation has no warnings. Three of the tests are the new direct shader checks.
- Actual Vulkan character: 52 rigid bones, 50 palette entries, 64-byte buffer stride; no nonfinite or zero-determinant palette entries in the inspected live Idle/Walk/Run states.
- Fixed Idle sample at time zero: both-legs/right-arm shape requests changed 50,345 viewport pixels above 3/255, excluding the controls panel. Largest palette component change was 0.1794486.
- Restore neutral: all 50 palette matrices and all 52 local joint poses exactly match the saved neutral values; zero viewport pixels changed above 3/255. This is an actual rendered comparison, not just a difference between requested parameters.
- Shaped reference support Y errors were -3.73e-8 m and 1.08e-7 m. These are reference-grounding checks, not terrain-planting or locomotion-slip measurements.
- Live walking with asymmetric proportions and a switch to Run remained finite and rendered. The subsequent runtime Console check showed zero errors and warnings.
- Saved host/profile/material were reloaded from disk and played. No probe component or hidden switch was added. The only new scene script is the visible demo control panel.

The sa-demo branch stores PNGs and JSON receipts under `docs/evidence`. Its README is the person's entry point. Package links are pinned Git dependencies rather than links to a particular home directory. Testable packages are declared explicitly so the Editor suites also run after normal UPM resolution.

## Reproduce

Read AGENTS.md first. Use the one Editor and the official CLI. The scripts accept the cloned project through SA_PROJECT:

```sh
SA_PROJECT=/home/i/GitHub/sa-demo eng/unity.sh compile
SA_PROJECT=/home/i/GitHub/sa-demo eng/unity.sh test SA.Tests
SA_PROJECT=/home/i/GitHub/sa-demo eng/unity.sh test SA.Motion.Unity.Tests
SA_PROJECT=/home/i/GitHub/sa-demo python3 eng/check_source_rules.py
```

The Vulkan check used a temporary launch wrapper through SA_UNITY_EDITOR to pass `-force-vulkan`, and closed the preceding Editor with EditorApplication.Exit. No windowed Editor was killed. eng/unity.sh eval performed C# execution; capture game_view captured the result.

Static ShapeTimelineSystem.LastWarning can retain a warning deliberately raised by a preceding unit-test world. Inspect a freshly cleared runtime Console for actual live errors rather than treating that static test diagnostic as a current fault.

## Still separate

This resolves the shader/test/view recovery in com.sa.unity#2. It does not complete all of sa-motion#3: the 50-character/500-motion pilot, production wardrobe/morph validation, terrain planting, boundary-velocity integration and live physical resizing remain separate gates. The demo explicitly plays clips in place and previews the skin/joint proportions. No NVIDIA, AMD, Windows, macOS or player-build result is claimed from this Intel Linux Editor run.
