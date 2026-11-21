# CourtSim

An adaptive courtroom simulation for training legal decision-making with AI assistance.
The trainee presides as judge and makes three rulings (**bail**, **admissibility of evidence** and
**sentencing**) while an AI assistant offers advice that is sometimes deliberately wrong. Every ruling is
graded against the case file and logged, the difficulty adapts to how the trainee treats the advice, and
a debrief reveals which suggestions were flawed and how far the trainee over- or under-trusted the AI.

> **Status.** Phases 0–2 (audit, foundation, case data and AI assistant) and **Phase B (adaptive
> difficulty)** are done. **Phase C (trust scores and debrief polish)** is in progress. The VR rig is not
> built yet: the game runs as a desktop first-person app. See `CHANGELOG.md`.

## Project structure at a glance

```
Case files (JSON) ─▶ CaseSession ◀── AdvicePlanner (which points are flawed)
                         │      ◀── AdaptiveController (how subtle, how detailed)
                         ▼
                  Advisor: local model (Ollama) | Claude | authored offline advice (always the fallback)
                         ▼
                  UI (bench, assistant card, evidence) ─▶ decision log (JSONL) ─▶ debrief + trust scores
```

Game logic, never the model, decides which advice is flawed; the model only writes the wording, and the
case file is the ground truth for grading. Details: `docs/ARCHITECTURE.md`, `docs/AI_ADVISOR.md`, and the
one-page summary in `docs/VIVA_CHEAT_SHEET.md`.

## Requirements

- **Unity 2022.3 LTS** (project file pins 2022.3.62f3; any newer 2022.3 patch works). Built-in render pipeline.
- Optional, to run core tests without Unity: **.NET SDK 8.0+**.
- Optional, for model-written advice: **Ollama** with `qwen3.5:9b` (free, local, the default; about 6 GB of
  VRAM) or `qwen3.5:0.8b` on smaller GPUs. Without it the assistant uses the offline advice from the case file.
- Optional, for Claude-written advice instead: an **Anthropic API key**.
- Optional, for MCP for Unity: **git** on `PATH` (Unity fetches the package from GitHub) and
  **uv** (`winget install --id astral-sh.uv`).
- Keep the checkout **outside OneDrive/Dropbox/iCloud** (e.g. `C:\dev\CourtSim`). Sync clients lock
  and rewrite files in `Library/` and corrupt the import cache.

## Demo (one double-click)

Run `demo.bat` in the project folder. It starts Ollama, preloads the local model onto the GPU, builds
`Builds\Windows\CourtSim.exe` the first time (a few minutes), and launches the game.

```
demo.bat            launch the demo (builds once if needed)
demo.bat rebuild    rebuild after code changes, then launch
demo.bat editor     open the Unity Editor straight into Play mode
demo.bat guided     launch straight into the guided demo (Esc stops it)
```

Close any Unity Editor that has the project open before building.

**Guided demo inside the game:** press **Watch guided demo** on the main menu (or run `demo.bat guided`). A scripted judge walks into the Sessions Court, approaches the bench, hears the AI assistant type and speak its advice, challenges a flawed suggestion, examines the exhibit, rules three times and reaches the debrief, with captions explaining each step. Press Esc to stop; control returns to you at the end.

**Recorded demo video:** `docs/demo/CourtSim-demo.mp4` (1080p, about 3 minutes, with the assistant's voice and typing sounds). Re-record with `Builds/Windows/CourtSim.exe -courtsimRecordDemo docs/demo/CourtSim-demo.mp4` (needs ffmpeg: `winget install Gyan.FFmpeg`).

## Quick start

1. Open the project folder in Unity Hub (**Add → Add project from disk**).
2. Open `Assets/Scenes/CourtRoom.unity` and press **Play**.

The scene is committed, but it is generated from code. After changing the layout or builder, regenerate
it with **CourtSim → Generate CourtRoom Scene** or `tools/unity.ps1 -Task scene`.

| Action | Keys |
| --- | --- |
| Move / walk faster | `W A S D` / hold `Shift` |
| Look | Mouse |
| Interact with the bench, evidence display or AI console | `E` |
| Toggle the AI assistant panel | `Tab` |
| Pause / back | `Esc` |

Walk through the gate in the bar. Look at the **judge's bench** and press `E` to make each ruling in
turn, or examine the **evidence display** on the left wall. The assistant card suggests a ruling at each
step; **Accept** or **Challenge** it, then rule. After the third ruling the dialog shows the debrief. Each
new session plays the next case, from easiest to hardest.

## AI-written advice

By default a small model running on your machine writes the advice. No key, no internet, no cost:

1. Install Ollama: `winget install Ollama.Ollama`. It runs in the background on port 11434.
2. Pull the model once: `ollama pull qwen3.5:9b` (`demo.bat` does this for you).
3. Press **Play**. The assistant card reads "Written by a local AI model for this case."

The default is `qwen3.5:9b` with a 4,096-token context, which fits entirely in an 8 GB laptop GPU (tested
on an RTX 5050 Laptop): about 30 s for the first load, then about 4–7 s per suggestion. Advice is fetched
ahead of time, so most of that wait is hidden. On a smaller GPU, pull `qwen3.5:0.8b` and set it in the Case
Session Controller's **Local Model** field: much faster, weaker reasoning. If Ollama isn't running, the card
shows the offline advice authored in the case file. Untick **Use Model** to always use the offline advice.

Model replies are validated before the trainee sees them: the ruling must be the planned one, and a
flawed rationale that admits its own flaw, or any rationale that leaks an internal id, is rejected in
favour of the authored advice.

## Adaptive difficulty

`AdaptiveController` (`Assets/Scripts/Core/Adaptive/`) watches how the trainee treats each suggestion
(a rolling history of the last 20 outcomes; "accepted" means the ruling matched the suggestion):

- **Two flawed suggestions accepted in a row:** flaws get subtler (level +1, up to 3). Flaw kinds are
  ranked from obvious to subtle (misread fact, misstated law, improper factor, omitted factor) and drawn
  with weight (1 + rank)^level, so level 0 is even and higher levels favour the subtle kinds.
- **Two sound suggestions rejected in a row:** the next suggestion explains its reasoning in detail.

The plan still decides which points are flawed; the controller picks only the flaw kind and the
explanation detail. A toast announces each change, the log gets an `adaptation` record, and the state is
saved per trainee at `<persistentDataPath>/trainees/<id>.json` (Inspector: **Trainee Id**, **Adaptive**).
A live run with `qwen3.5:9b` is in `docs/evidence/phase-b-adaptive-playtest.md`.

## Trust scores (Phase C, in progress)

The debrief reports two percentages, each with a one-line plain-English reading (50% or more is high,
25% or more moderate, otherwise low):

- **Over-trust** = flawed suggestions accepted ÷ flawed suggestions shown.
- **Under-trust** = sound suggestions rejected ÷ sound suggestions shown.

### Claude (optional)

Set **Backend** to `Claude` on the Case Session Controller, then set `ANTHROPIC_API_KEY` for your user, or
put the key alone in `%USERPROFILE%\AppData\LocalLow\DefaultCompany\CourtSim\anthropic_api_key.txt`, and
restart the Editor. The assistant card then reads "Written by Claude for this case." Without a key, or on
any API failure, it falls back to the offline advice.

Never commit a key or put it under `Assets/`. A key on a trainee's machine can be read by anyone who uses
it, so for deployments point the controller's **Api Base Url** at a proxy that holds the key.
`docs/AI_ADVISOR.md` covers both backends, model choice, the fallback rules and the proxy setup.

## Decision logs

Each session writes a JSON Lines log (schema `courtsim.log/1`) to
`%USERPROFILE%\AppData\LocalLow\DefaultCompany\CourtSim\logs\` (`Application.persistentDataPath/logs`
on other platforms). The format is in `docs/AI_ADVISOR.md`.

## MCP for Unity (Claude Code tooling)

The project includes [MCP for Unity](https://github.com/CoplayDev/unity-mcp) as an Editor package, and
`.mcp.json` registers its server for Claude Code. One-time setup:

1. Install uv: `winget install --id astral-sh.uv`. The first run of `uvx` downloads Python 3.12 and
   `mcpforunityserver` 10.3.0. (Python is pinned to 3.12 because uv's default lacked `pyyaml` wheels.)
2. Open the project in the Unity Editor and **leave it open**. The bridge runs only while the Editor GUI
   has the project open.
3. Start Claude Code in `C:\dev\CourtSim`. It picks up `.mcp.json` (server `unityMCP`); approve it when
   asked.
4. In Unity, open **Window → MCP for Unity** and confirm the bridge is running.

Batch tooling (`tools/unity.ps1`) cannot run while the Editor has the project open. Close the Editor
before a headless run, and reopen it to use MCP again.

## Tests

| Suite | How to run | Needs Unity |
| --- | --- | --- |
| Core (512 NUnit tests, coverage gate 80%) | `pwsh tools/verify-core.ps1` | No |
| Everything in Unity, headless: compile, regenerate scene, EditMode + PlayMode, coverage gate 80% | `pwsh tools/unity.ps1 -Task all -Coverage` | Yes (installed, no GUI) |
| One real case against the local model, headless, with a transcript | `pwsh tools/unity.ps1 -Task live` | Yes, plus Ollama |
| Same suites interactively | Window → General → Test Runner | Yes |

`tools/unity.ps1` runs Unity in batch mode, so close any Editor that has this project open first
(including one left open for MCP for Unity).
Latest results: Core **512/512** under .NET (**98.75%** line coverage); Unity EditMode **517/517** and
PlayMode **39/39** (**91.5%** line coverage across all CourtSim assemblies).

## Repository layout

```
Assets/
  Scripts/Core/      CourtSim.Core: pure C#, no UnityEngine (events, layout, decisions, cases,
                     advice, adaptive difficulty, Ollama and Claude clients, HTTP abstraction,
                     logging, sessions, JSON)
  Scripts/Runtime/   CourtSim.Runtime: bootstrap, session controller, case loading, HTTP transport,
                     courtroom builder, player, interaction, UI
  Scripts/Editor/    CourtSim.Editor: scene generator, material assets
  Resources/Cases/   Case files (courtsim.case/1 JSON)
  Tests/EditMode/    Core unit tests (also run by tools/verify-core.ps1)
  Tests/PlayMode/    Runtime integration tests (builder, UI flow, player, bootstrap)
  Tests/Editor/      Editor tooling tests (scene generator)
  Scenes/ Materials/ Generated by CourtSim → Generate CourtRoom Scene (committed)
docs/                Architecture, case format, AI advisor, viva cheat sheet, style guide,
                     Phase 0 audit; evidence/ holds live playtest transcripts
tools/               verify-core.ps1 (core under .NET) and unity.ps1 (headless Unity driver)
demo.bat             One-click demo: starts Ollama, preloads the model, builds and launches
.mcp.json            MCP for Unity server for Claude Code
```

Read `docs/ARCHITECTURE.md` for how the pieces fit, `docs/CASE_FORMAT.md` before writing a case, and
`CONTRIBUTING.md` before making changes.

## License

MIT; see `LICENSE`. Third-party attributions, and the status of every reference repository, are in
`NOTICES.md`.
