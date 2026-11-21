# Phase D: final validation (2026-11-21)

## 1. Test suites (batch mode, no Editor clicks)

| Suite | Command | Result | Line coverage |
| --- | --- | --- | --- |
| Core under .NET | `tools/verify-core.ps1` | 522 / 522 passed | 98.76% (gate 80%) |
| Unity compile | `tools/unity.ps1 -Task compile` | 0 errors, 0 warnings | |
| Unity EditMode | `tools/unity.ps1 -Task editmode` | 527 / 527 passed | |
| Unity PlayMode | `tools/unity.ps1 -Task playmode` | 39 / 39 passed | |
| All CourtSim assemblies in Unity | `-Coverage` | | 89.7% (Core 99.6%, Runtime 88.7%, Editor 18.5%) |

## 2. Live session: built player, real local model, menu to debrief

Run: `Builds/Windows/CourtSim.exe -courtsimScreenshots <dir>` (RTX 5050 Laptop 8 GB, Ollama `qwen3.5:9b`).
The tour opens each screen through the same UI calls the buttons make, rules on every point and opens the
debrief. Screens are in `docs/screenshots/`. Decision log: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\CourtSim\logs\`.

Case: State v. Marcus Hale. Advisor: `qwen3.5:9b` (every suggestion `source: local`, written by the model).

| # | Decision | AI suggestion | Planted flaw | Detail | Ruling | Graded | Outcome |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | Bail | Release with conditions | none | detailed\* | Release with conditions | sound | trusted sound |
| 2 | CCTV admissibility | Exclude the recording | misstated law | standard | Admit the recording | sound | caught flaw |
| 3 | Sentence | Community order | none | standard | Short custodial sentence | defensible | overrode sound |

Debrief: 2 of 3 rulings sound, 1 defensible. You caught 1 of 1 flawed suggestion.
**Over-trust 0%** (accepted 0 of 1 flawed). **Under-trust 50%** (rejected 1 of 2 sound).

\* The detailed explanation on decision 1 came from the trainee state saved by the previous run, which had
rejected two sound suggestions in a row. This shows the adaptive state persisting between sessions.

## 3. Performance and console

| Phase of play | Frames | Average | 99th percentile | Worst frame |
| --- | --- | --- | --- | --- |
| Courtroom, no UI open | 541 | 5.6 ms (180 fps) | 5.6 ms | 5.6 ms |
| Ruling while the AI generates advice | 1,552 | 5.6 ms (180 fps) | 5.6 ms | 16.7 ms |

No stutter: model requests run off the main thread through UnityWebRequest. The player log for the whole run
contains **no errors, exceptions or warnings**.

## 4. Repository hygiene

- Location: `C:\dev\CourtSim`, outside OneDrive.
- Secret scan of the full git history (Anthropic, GitHub and AWS key patterns, private keys): **0 matches**.
  No key files are tracked; `.gitignore` excludes `.env` and `anthropic_api_key*`.
- History: Conventional Commits on short-lived feature branches, fast-forward merged to `main`.

## 5. Not verified by automation

A human must still watch one full session played by hand (`demo.bat`, walk to the bench, press **E**, rule
three times, read the debrief). The automated tour drives the UI through code, not mouse and keyboard.
