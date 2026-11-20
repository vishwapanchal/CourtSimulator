# CourtSim

An adaptive courtroom simulation for training legal decision-making with AI assistance.
The trainee presides as judge and makes three rulings (**bail**, **admissibility of evidence** and
**sentencing**) while an AI assistant offers advice that is sometimes deliberately wrong. Later phases
log every ruling and adapt case difficulty to the trainee's performance.

> **Status: Phase 2, case data and AI assistant.** Three fictional cases drive the decision flow.
> Every ruling is graded against the case file and logged, the assistant's advice is written by a local
> model (Ollama) by default, optionally by Claude, or taken from the case file offline, and a debrief at the end reveals which suggestions were flawed. Adaptive
> difficulty and the VR rig arrive in Phase 3. See `CHANGELOG.md`.

## Requirements

- **Unity 2022.3 LTS** (project file pins 2022.3.62f3; any newer 2022.3 patch works). Built-in render pipeline.
- Optional, to run core tests without Unity: **.NET SDK 8.0+**.
- Optional, for model-written advice: **Ollama** with `qwen3.5:0.8b` (free, local, the default; see below).
  Without it the assistant uses the offline advice from the case file.
- Optional, for Claude-written advice instead: an **Anthropic API key**.
- Optional, for MCP for Unity: **git** on `PATH` (Unity fetches the package from GitHub) and
  **uv** (`winget install --id astral-sh.uv`).
- Keep the checkout **outside OneDrive/Dropbox/iCloud** (e.g. `C:\dev\CourtSim`). Sync clients lock
  and rewrite files in `Library/` and corrupt the import cache.

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
2. Pull the model once: `ollama pull qwen3.5:0.8b` (about 1.3 GB download, about 625 MB of VRAM).
3. Press **Play**. The assistant card reads "Written by a local AI model for this case."

The first request takes about a minute while the model loads; after that advice arrives in about two
seconds on a laptop GPU. If Ollama isn't running, the card shows the offline advice authored in the case
file. Larger tags such as `qwen3.5:4b` or `qwen3.5:9b` write better reasoning; set one in the Case Session
Controller's **Local Model** field after pulling it. Untick **Use Model** to always use the offline advice.

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
| Core (97 NUnit tests, coverage gate 80%) | `pwsh tools/verify-core.ps1` | No |
| Everything in Unity, headless: compile, regenerate scene, EditMode + PlayMode, coverage gate 80% | `pwsh tools/unity.ps1 -Coverage` | Yes (installed, no GUI) |
| Same suites interactively | Window → General → Test Runner | Yes |

`tools/unity.ps1` runs Unity in batch mode, so close any Editor that has this project open first
(including one left open for MCP for Unity).
Current line coverage is **94%** across all CourtSim assemblies in Unity, and **95%** for Core under .NET.

## Repository layout

```
Assets/
  Scripts/Core/      CourtSim.Core: pure C#, no UnityEngine (events, layout, decisions, cases,
                     advice, Ollama and Claude clients, HTTP abstraction, logging,
                     sessions, JSON)
  Scripts/Runtime/   CourtSim.Runtime: bootstrap, session controller, case loading, HTTP transport,
                     courtroom builder, player, interaction, UI
  Scripts/Editor/    CourtSim.Editor: scene generator, material assets
  Resources/Cases/   Case files (courtsim.case/1 JSON)
  Tests/EditMode/    Core unit tests (also run by tools/verify-core.ps1)
  Tests/PlayMode/    Runtime integration tests (builder, UI flow, player, bootstrap)
  Tests/Editor/      Editor tooling tests (scene generator)
  Scenes/ Materials/ Generated by CourtSim → Generate CourtRoom Scene (committed)
docs/                Architecture, case format, AI advisor, style guide, Phase 0 audit
tools/               verify-core.ps1 (core under .NET) and unity.ps1 (headless Unity driver)
.mcp.json            MCP for Unity server for Claude Code
```

Read `docs/ARCHITECTURE.md` for how the pieces fit, `docs/CASE_FORMAT.md` before writing a case, and
`CONTRIBUTING.md` before making changes.

## License

MIT; see `LICENSE`. Third-party attributions, and the status of every reference repository, are in
`NOTICES.md`.
