# Changelog

All notable changes are recorded here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Local model backend, now the default: `OllamaClient` (`Core/Ollama`) calls a model running in Ollama on
  `http://127.0.0.1:11434/api/chat` with a JSON-schema `format`, `think: false`, `keep_alive` 15 min and a
  180 s timeout. Default model `qwen3.5:0.8b` (~1.3 GB): no API key, no internet, no cost. Errors say what
  to do (start Ollama, `ollama pull <model>`). `LocalModelAdvisor` sends the same prompt and schema as
  Claude; its advice is logged with `"source": "local"`.
- `AdviceReply`: one validator for replies from either model.
- `CaseSessionController`: **Backend** (`LocalModel` by default, or `Claude`), **Local Model** and
  **Ollama Url** fields.
- `tools/unity.ps1 -Task live` and `LiveSessionRunner` default to the local backend
  (`-liveBackend local|claude`, `-liveModel <name>`); a key is needed only for Claude.

### Changed
- Claude is optional. **Use Claude** is replaced by **Use Model** (off: authored offline advice only).
  With **Backend** `Claude` and no key, the controller logs a warning and uses the offline advice. The
  offline advice remains the fallback for both backends.
- `IHttpTransport`, `HttpRequestData` and `HttpResponseData` moved from `CourtSim.Core.Claude` to
  `CourtSim.Core.Http`.
- The system prompt asks for a rationale under 90 words with no option or factor ids in it.
- Fallback messages and the card footnote say "AI model" instead of "Claude": `adviceError` reads
  `AI model unavailable, used offline advice: ...`.

### Planned: Phase 3 (adaptation and XR)
- Adaptive difficulty policy in Core, driven by the decision log: picks the next case, the number of
  flawed suggestions and the flaw kinds to emphasise.
- XR Interaction Toolkit rig implementing `IPlayerInputSource`.
- Migrate UI text from legacy `UI.Text` to TextMeshPro.

## [0.2.0] - 2026-11-20

Phase 2: case data, the AI assistant and decision logging.

### Added
- Case file format `courtsim.case/1` (`docs/CASE_FORMAT.md`) and `CaseFileReader`, which validates strictly
  and reports every error with its JSON path. `CaseRepository` loads `Assets/Resources/Cases` and skips
  invalid files with a logged error.
- Three fictional cases: `hale-burglary` (difficulty 1), `raman-assault` (2) and `okafor-supply` (3), each
  with graded options, debrief feedback, sound advice and flawed variants for every decision point.
- Flaw taxonomy (`misstated_law`, `misread_fact`, `omitted_factor`, `improper_factor`) and `AdvicePlanner`,
  which decides in game logic, never in the model, which decision points get flawed advice.
- Advisors: `ScriptedAdvisor` (authored offline advice), `ClaudeAdvisor` (Claude writes the rationale for the
  planned ruling and flaw; the reply is validated against the case) and `FallbackAdvisor` (offline advice on
  any failure). See `docs/AI_ADVISOR.md`.
- `ClaudeClient` over an injectable `IHttpTransport`: model `claude-opus-5-5`, API version `2026-10-21`,
  explicit `output_config.effort` (default `medium`), JSON-schema output, server-side fallbacks, up to two
  retries on 408/409/429/5xx and transport errors honouring `retry-after` and `x-should-retry`, a 90 s
  timeout, and refusal and `max_tokens` handling. `UnityWebRequestTransport` implements the transport.
- Decision log (`courtsim.log/1`, JSON Lines) with `session_start`, `decision` and `session_end` records:
  ruling, grade, advice, Accept/Challenge stance, time to decide, outcome (`trusted_sound`, `overrode_sound`,
  `misled_by_flaw`, `caught_flaw`, `no_advice`) and a session summary with the flaw detection rate. Written to
  `<persistentDataPath>/logs/`. (Planned for Phase 3; delivered early.)
- `CaseSession` and `Debrief` in Core; `CaseSessionController` in Runtime, added by the bootstrap, with
  Inspector settings for case, flawed-advice count, seed, Claude model, effort, API base URL and logging.
- UI bound to the session: submissions in the decision dialog, advice with Accept/Challenge on the
  assistant card, the case exhibit in the evidence viewer, and an end-of-session debrief with restart.
- `CourtSim.Core.Json`, a small strict JSON reader and writer.
- MCP for Unity 10.3.0 (CoplayDev, MIT) as an Editor-only package, and `.mcp.json` for Claude Code.
- `docs/CASE_FORMAT.md` and `docs/AI_ADVISOR.md`.

### Changed
- The decision flow runs on case files. Placeholder content is used only when no valid case loads.
- Rulings are recorded through `CaseSession`, which writes the log. `DecisionSubmitted` is still published.
- The Claude client sends single non-streaming requests rather than streaming as planned: replies are
  short structured JSON.
- `Packages/manifest.json` now includes a Git-hosted package, so Unity needs git on `PATH` to resolve it.

### Security
- The API key is read only from `ANTHROPIC_API_KEY` or `<persistentDataPath>/anthropic_api_key.txt`
  (`ApiKeyLocator`). It is never stored under `Assets/`, included in builds or logged.
- A key on a trainee machine, or in any shipped client, can be extracted. For deployments, set the
  controller's API base URL to a proxy that holds the key and issues its own tokens to clients.

### Known issues
- MCP for Unity needs the Editor open on the project, and batch mode (`tools/unity.ps1`) can't run while it
  is. Close the Editor before headless runs.
- Token usage and the model that actually answered (after a server-side fallback) are not yet logged.

## [0.1.1] - 2026-11-20

Phase 1 verified in Unity 2022.3.62f3, not just under .NET.

### Added
- `tools/unity.ps1`: headless Unity driver (compile, regenerate scene, EditMode/PlayMode tests, coverage
  gate of 80%), plus the `unity-batch` Claude skill that wraps it.
- PlayMode tests for the bootstrap, player locomotion, gravity, look clamping, interaction focus and
  input-driven screens, using a scriptable `FakeInputSource`. Editor tests for the scene generator.
  Totals: 102 EditMode and 24 PlayMode tests, with 94% line coverage across all CourtSim assemblies.
- `CourtSimBootstrap.UseInputSource` to swap the input device at runtime (the XR rig will use it).
- `CourtroomSceneGenerator.Generate(path, addToBuildSettings)` overload.
- Generated `Assets/Scenes/CourtRoom.unity`, its material assets, all `.meta` files and the full
  `ProjectSettings/` are now committed.

### Fixed
- The player could not move at high frame rates. `CharacterController.minMoveDistance` (default
  0.001 m) discarded per-frame moves above about 2,400 fps, which includes every frame in batch mode.
  It is now 0.

### Changed
- Editor pinned to 2022.3.62f3, and the Rider package to 3.0.36 (the versions Unity resolved on first import).
- The project moved out of OneDrive to a plain local folder to avoid sync locks on `Library/`.

## [0.1.0] - 2026-11-20

Phase 1: unified project foundation and environment preview.

### Added
- Unity 2022.3 LTS project (`app/CourtSim`) with three assemblies: `CourtSim.Core` (engine-independent),
  `CourtSim.Runtime` and `CourtSim.Editor`.
- Data-driven courtroom layout with validation, and a procedural builder for the judge's bench, clerk's
  desk, witness stand, counsel tables, dock, evidence display, AI assistant console, bar and gallery.
- Editor command **CourtSim → Generate CourtRoom Scene**, which bakes the scene with material assets; also
  available headless for CI.
- Desktop `FirstPersonController` (walk, sprint, mouse look, gravity) behind a device-agnostic
  `IPlayerInputSource`, ready for a VR source.
- Look-at interaction: HUD prompt, then `E` opens the decision dialog or the evidence viewer, or shows the assistant.
- `UIManager`: one owner for the screen stack, HUD, cursor locking and the gameplay-input block.
  Themed, asset-free UI built from `UIFactory` and `UITheme`: main menu, pause, controls, decision point,
  evidence viewer, toasts, and an AI assistant card with a standing caution.
- Placeholder decision flow: bail, then admissibility, then sentencing, recorded by `DecisionSequence`
  and announced through `DecisionSubmitted`.
- Synchronous typed `EventBus` with safe re-entrancy and error isolation.
- 97 core unit tests (95% line coverage) runnable with or without Unity; PlayMode tests for the builder
  and UI flow.
- `.editorconfig` style enforcement, `tools/verify-core.ps1`, a GitHub Actions workflow, and the
  `README`, `CONTRIBUTING`, `NOTICES`, architecture, style guide and Phase 0 audit documents.

### Known issues
- Unity-dependent code was authored without a Unity editor available. It was verified in 0.1.1.
- Text uses Unity's legacy `UI.Text`; migrate to TextMeshPro for sharper type at VR distances.
- Placeholder materials assume the Built-in render pipeline; URP needs the materials converted.
