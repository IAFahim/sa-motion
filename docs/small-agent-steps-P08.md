# Small-agent follow-ups for P08 (SOMA rig)

Branch base: `packet/P08-soma-rig`. Read `AGENTS.md` and `docs/soma.md` first. Each packet below is independent.

Every packet:
- Work in its own worktree.
- Run `eng/dotnet_test.sh tests/SA.Soma.Tests/SA.Soma.Tests.csproj` and `python3 eng/check_source_rules.py` before committing.
- End commit messages with the Co-Authored-By line.
- Never commit `testdata/eval-only/`.
- Never run Unity for S1-S4.

Global STOP-AND-REPORT conditions (any packet):
- An existing SA.Soma test fails before you change anything.
- A change would need edits to `src/SA.Animation` or `src/SA.Animation.Content`.
- You would have to copy NVIDIA motion, BVH, ONNX or USD files into the repository.
- A number in `docs/soma.md` stops reproducing.

## S1. Twist check from synthetic forearm roll

1. In `tests/SA.Soma.Tests/SomaRetargetTests.cs`, add a test.
   - Start from `World(BindModel())`.
   - Rotate SOMA `LeftForeArm` and `LeftHand` world rotations by 90 deg about the mirrored-back forearm axis. The axis is `BindPosition(LeftHand) - BindPosition(LeftForeArm)`.
2. Convert the pose with `Convert`.
3. Assert that SA bone 12 (left lower arm) local rotation equals a 90 deg twist about its own canonical bone axis (-X), within 0.1 deg.
4. Assert that bone 11 is unchanged from the bind conversion, within 0.01 deg.
5. Repeat for the right side (bones 31, 30) with axis +X.

STOP-AND-REPORT if the twist sign comes out reversed on one side only. That is a mirror-handedness bug.

## S2. Root heading smoothing option (pure function)

1. Add `SomaRoot.Smooth(ReadOnlySpan<RigidPose> root, int window, Span<RigidPose> output)` in `src/SA.Soma`.
   - Use a centred moving average of yaw (unwrap first) and of position.
   - Frame 0 must stay identity.
2. Add a test: a constant-heading clip is unchanged, and a 10-frame window removes a +/-5 deg sine wobble to under 0.5 deg.
3. Do NOT change `SomaClip.Convert`. Compensating the samples needs a separate packet.

STOP-AND-REPORT if smoothing the root without re-expressing the hips would be required to keep the pose. It is required, so report the design instead of guessing.

## S3. 77-joint input path test

1. Build a (2,77,7) float array.
   - Put the 30 model joints at `SomaRig.SkeletonIndex(j)`.
   - Fill the other 47 joints with identity and zero.
2. Call `SomaClip.Convert(..., jointsPerFrame: 77, ...)`.
3. Assert that the samples equal the (2,30,7) path exactly.

STOP-AND-REPORT if the results differ.

## S4. Input validation tests

Add one test per case, each asserting `AnimationError.InvalidFrame`:
- NaN position
- zero quaternion
- `frames = 0`
- wrong span lengths
- `jointsPerFrame = 31`
- `Encode` with `frames = 1`

STOP-AND-REPORT if any case throws instead of returning an error.

## S5. Unity-side wiring stub (packages/, needs the Unity lock)

1. Read `.claude/skills/sa-unity-cli/SKILL.md`.
2. Check Editor state with `eng/unity.sh eval` (`isPlaying`, open scenes, `isDirty`).
3. Add an Editor-only menu item "SA/Import SOMA JSON Clip".
   - Put it in `packages/com.sa.unity.reactions/Editor/SomaClipImporter.cs`, or in the package that owns `SharedFamilyBaker` if that is where Editor code lives.
   - It reads a JSON file in the format from `docs/soma.md` and calls `SomaRetarget.Create`, `SomaClip.Convert`, `SomaContacts.Merge` and `SomaClip.Encode`.
   - It writes the `.saca` bytes next to the JSON.
   - It logs the frame count, bytes, encoder error and planted-sole maximum, with the asset as Console context.
4. Do not wire contacts into `LegIKSystem` in this packet.
5. Run `eng/unity.sh compile`.

STOP-AND-REPORT if any of these happens:
- `SA.Soma` cannot be referenced from the package (asmdef or DLL policy).
- The family `AssetId` for humanoid-v1 is not available in the Editor.
- The owner's Editor is in Play mode or has a dirty scene.

## S6. Visual twist check on a character (needs a person)

1. After S5, import `req2.json` locally (eval-only, do not commit the result).
2. Preview it on a Humanoid character.
3. Record screenshots of the wrists and forearms at frames 0, 25 and 49 in `docs/soma.md`.

STOP-AND-REPORT with the screenshots if a forearm or hand shows a constant roll. Do not adjust the calibration secondaries by eye in the same packet.
