---
name: unity-batch
description: Compile CourtSim in the real Unity Editor headlessly, regenerate the CourtRoom scene, and run EditMode/PlayMode tests with a coverage gate. Use after any change under Assets/ (Runtime, Editor, tests, asmdefs, packages), before committing, or when asked whether the Unity project builds or its tests pass.
---

# Drive Unity in batch mode

`tools/unity.ps1` runs Unity 2022.3 with `-batchmode -nographics`. Nobody has to open or click the Editor.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/unity.ps1 -Coverage        # everything
powershell -NoProfile -ExecutionPolicy Bypass -File tools/unity.ps1 -Task compile    # fastest check
powershell -NoProfile -ExecutionPolicy Bypass -File tools/unity.ps1 -Task scene,playmode
```

Tasks are `compile`, `scene`, `editmode`, `playmode`, `test` and `all`. When run through `-File`, pass
several tasks comma-separated. A full run takes about 3–6 minutes, so give the command a 10-minute
timeout.

## Outputs (`Logs/batch/`, git-ignored)

- `<task>.log` is the full Editor log. Search it for `error CS`, `Exception` and `Aborting batchmode`.
- `EditMode-results.xml` and `PlayMode-results.xml` are NUnit 3 results. The script prints any failures.
- `coverage/Report/Summary.xml` and `index.html` are coverage for the `CourtSim.*` assemblies. The gate
  is 80% lines.

## Failure patterns

- **Script hangs after "Aborting batchmode".** An older copy of the script waited on Unity's helper
  processes. The current script uses `WaitForExit`. If it still hangs, stop it and read the log.
- **"It looks like another Unity instance is running with this project open"**: an interactive
  Editor holds `Temp/UnityLockfile`. Don't kill the user's Editor. Ask them to close it, or report that
  verification is blocked.
- **License errors**: the Personal license is activated through Unity Hub. If batch mode reports no
  valid license, the user has to sign in to Hub. That is the only manual step there is.
- **PlayMode movement tests fail by tiny amounts**: batch mode runs at thousands of fps, which exposes
  frame-rate bugs such as `CharacterController.minMoveDistance`. Treat these as real bugs, not flaky tests.
- After `scene`, `git status` shows a changed `Assets/Scenes/CourtRoom.unity` whenever the layout or
  builder changed. Commit it.
