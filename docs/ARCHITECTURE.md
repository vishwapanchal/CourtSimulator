# Architecture

## Layers

```
┌─────────────────────────────────────────────────────────────┐
│ CourtSim.Editor   (editor only)                             │
│   CourtroomSceneGenerator · EditorMaterialProvider          │
├─────────────────────────────────────────────────────────────┤
│ CourtSim.Runtime  (MonoBehaviours, Unity glue)              │
│   App/        CourtSimBootstrap · GameServices              │
│               CaseSessionController                         │
│   Cases/      CaseRepository                                │
│   Advising/   UnityWebRequestTransport · ApiKeyLocator      │
│   Courtroom/  CourtroomBuilder · FixtureDecorator · Lighting│
│   Player/     FirstPersonController · IPlayerInputSource    │
│   Interaction/ InteractionProbe · Interactable              │
│   UI/         UIManager · UIFactory · UITheme · screens     │
├─────────────────────────────────────────────────────────────┤
│ CourtSim.Core     (pure C#, noEngineReferences, unit-tested)│
│   Events/ Navigation/ UI/ Courtroom/ Decisions/ Interaction/│
│   Json/ Cases/ Advising/ Http/ Ollama/ Claude/ Logging/     │
│   Sessions/ Adaptive/                                       │
└─────────────────────────────────────────────────────────────┘
```

Dependencies point downward only. `CourtSim.Core` compiles without Unity, and
`tools/CoreVerification` builds it against .NET Standard 2.1 to prove that.

## Core namespaces added in Phase 2

| Namespace | Contents |
| --- | --- |
| `Json` | `JsonValue` and `JsonReader`: a minimal strict RFC 8259 reader and writer (insertion-ordered objects, nesting limit 64). Unity's `JsonUtility` can't represent case files or API payloads, and Core takes no third-party dependency. |
| `Cases` | `CaseFile` and its parts (facts, exhibits, decision points, factors, option grades, authored advice), and `CaseFileReader`, which validates strictly and reports every error with a JSON path. Format: `docs/CASE_FORMAT.md`. |
| `Advising` | The `FlawKind` taxonomy, `Advice`, `AdvicePlan` and `AdvicePlanner` (which points get flawed advice), `IAdvisor` and `AdviceRequest`, and the advisors `ScriptedAdvisor`, `LocalModelAdvisor`, `ClaudeAdvisor` and `FallbackAdvisor`. `AdvicePromptBuilder` holds the system prompt, the brief and the output schema; `AdviceReply` validates a model's reply the same way for both model advisors. See `docs/AI_ADVISOR.md`. |
| `Http` | The `IHttpTransport` abstraction with `HttpRequestData` and `HttpResponseData`, shared by both model clients. (Moved out of `Claude`.) |
| `Ollama` | `OllamaClient` (`/api/chat` request with a JSON-schema `format`, error handling), `OllamaClientOptions`, `OllamaChatResponse` and `OllamaException`. The default backend. |
| `Claude` | `ClaudeClient` (Messages API request, retries, refusal and truncation handling), its options and exceptions. |
| `Logging` | `DecisionLogEntry`, `AdviceOutcome`, `AdviceStance`, `SessionSummary`, and the sinks `MemoryLogSink`, `JsonLinesFileSink` and `CompositeLogSink`. Schema `courtsim.log/1`. |
| `Sessions` | `CaseSession`, one trainee's pass through one case (advice prefetch, scoring, logging), and `Debrief` (per-ruling items, headline, and the Phase C over-/under-trust lines). |

## Adaptive difficulty (Phase B): `Core/Adaptive`

`AdaptiveController` is a small, serialisable state machine fed one `AdviceOutcome` per ruling by
`CaseSession.Submit`. It keeps a rolling history of the last 20 outcomes and two streak counters.

| Rule | Trigger | Effect |
| --- | --- | --- |
| Subtler flaws | Two `misled_by_flaw` in a row (a flawed suggestion whose ruling was followed) | `Subtlety` +1, up to `MaxSubtlety` = 3 |
| More explanation | Two `overrode_sound` in a row (a sound suggestion whose ruling was not followed) | `DetailedExplanationPending`: the next suggestion is requested with `ExplanationDetail.Detailed`, then the flag clears |

Any other outcome breaks a streak; `no_advice` neither extends nor breaks one, since it says nothing
about trust.

**Choosing the flaw kind.** `AdvicePlanner` still decides *which* points are flawed. When advice for a
flawed point is requested, `ChooseFlaw` picks the kind from those the case authored for that point.
`ObviousToSubtle` ranks them: `misread_fact` (0, checkable against the record), `misstated_law` (1),
`improper_factor` (2), `omitted_factor` (3, nothing said is wrong, something is missing). Each kind weighs
(1 + rank)^subtlety, so level 0 is uniform and level 3 makes an omitted factor 64 times as likely as a
misread fact.

**Detail.** `TakeExplanationDetail` returns `Detailed` once. Model advisors then get a brief asking for
four to six sentences that explain each cited factor in plain words; `ScriptedAdvisor` appends the cited
factors to the authored rationale.

**Persistence and visibility.** `ToJson`/`FromJson` round-trip the state (unknown or out-of-range values
fall back to a fresh trainee). `CaseSessionController` loads it from
`<persistentDataPath>/trainees/<Trainee Id>.json` on the first session and saves it on each adaptation,
at session end and on destroy, only when **Write Log File** is on (so tests leave no files). Each firing
writes an `adaptation` log record and raises `CaseSession.Adapted`, which the controller shows as a
toast. Turn it off with **Adaptive** on the controller.

Why these rules: they are the two failure modes the training targets, over-trust and under-trust, and
each response is the smallest change that addresses it (harder-to-spot flaws for a trainee who follows
blindly; better-argued advice for one who dismisses good advice). Two in a row avoids reacting to a single
slip, and the state is a handful of numbers that can be saved, tested and explained in a debrief.

## Runtime pieces added in Phase 2

| Type | Role |
| --- | --- |
| `Cases/CaseRepository` | Loads every `TextAsset` under `Resources/Cases`, validates each with `CaseFileReader`, logs and skips invalid files, and sorts by difficulty, then id. |
| `Advising/UnityWebRequestTransport` | `IHttpTransport` on `UnityWebRequest`. Works on every Unity platform; call it from the main thread. |
| `Advising/ApiKeyLocator` | Finds the Anthropic key in `ANTHROPIC_API_KEY`, else `<persistentDataPath>/anthropic_api_key.txt`. Never in `Assets/` or a build. |
| `App/CaseSessionController` | Picks the case, builds the flaw plan, chooses the advisor, opens the log file, loads and saves the trainee's adaptive state, and attaches the session to the UI. Added by the bootstrap if the scene has none. Inspector settings: Case Id, Flawed Advice Count, Seed, Use Model, Backend (`LocalModel` or `Claude`), Local Model (default `qwen3.5:9b`), Ollama Url, Model (Claude), Effort, Api Base Url, Write Log File, Trainee Id, Adaptive. |

## Runtime flow

1. **`CourtSimBootstrap.Awake`** (execution order −100) builds any missing part (courtroom, player, UI),
   creates one `LegacyPlayerInputSource` and passes it to the player, the interaction probe and the UI.
   It then finds or adds the `CaseSessionController` and calls `Initialize(ui)`.
2. **`UIManager.Start`** generates the HUD and screen canvases from `UITheme` and opens the main menu.
   **`CaseSessionController.Start`** starts the first session (see below).
3. The screen stack's top-changed callback is the **single place** that shows and hides screens,
   locks or frees the cursor, and publishes `GameplayInputBlockChanged`.
4. `FirstPersonController` and `InteractionProbe` listen for that event and stop reading input while
   any screen is open.
5. Looking at a fixture: `InteractionProbe` publishes `InteractionFocusChanged`, and the HUD shows the
   prompt. Pressing **E** publishes `InteractionRequested`, and `UIManager` opens the matching screen.
6. A ruling: `DecisionPointScreen` → `UIManager.SubmitDecision` → `CaseSession.Submit` (or
   `DecisionSequence.Record` when no case loaded) → publishes `DecisionSubmitted`.

## Session data flow

```
Resources/Cases/*.json ─▶ CaseRepository ─▶ CaseFile ─▶ AdvicePlanner ─▶ AdvicePlan
                                               │                           │
                                               ▼                           ▼
     IAdvisor ◀── AdviceRequest ────────── CaseSession ──▶ IDecisionLogSink ─▶ logs/*.jsonl
  (Scripted, or Fallback(LocalModel|Claude,    │   ▲
   Scripted))          flaw kind + detail ─────┼───┘ outcome per ruling
                                               │   AdaptiveController ◀─▶ trainees/<id>.json
                                               ▼
                    UIManager: decision dialog, assistant card, evidence viewer, toasts, debrief
```

1. **Load.** `CaseRepository.LoadAll` runs once, on the first session.
2. **Pick.** The controller plays the configured **Case Id**, or cycles through the cases from easiest to
   hardest, one per session.
3. **Plan.** `AdvicePlanner.Create(case, flawedCount, random)` fixes the flaw for each decision point
   before any advice exists. The model never chooses it.
4. **Advisor.** With **Use Model** on, **Backend** picks the model:
   - `LocalModel` (default): `FallbackAdvisor(LocalModelAdvisor(OllamaClient(options, UnityWebRequestTransport)), ScriptedAdvisor)`.
     No key; if Ollama isn't running, each request fails fast and falls back.
   - `Claude` with an API key: `FallbackAdvisor(ClaudeAdvisor(ClaudeClient(options, UnityWebRequestTransport)), ScriptedAdvisor)`.
     Without a key, a warning and `ScriptedAdvisor`.

   With **Use Model** off, `ScriptedAdvisor`. The authored offline advice is always the fallback.
5. **Sink.** `MemoryLogSink`, plus `JsonLinesFileSink` at `<persistentDataPath>/logs/` when **Write Log
   File** is on.
6. **Start.** `CaseSession.Start` writes `session_start` and requests advice for the first point.
   `UIManager.AttachSession` points the HUD, decision dialog, assistant card and evidence viewer at the
   session, and refreshes them on `AdviceChanged`.
7. **Rule.** Opening the decision dialog calls `MarkPresented` (starting the decision timer); Accept or
   Challenge on the assistant card calls `SetStance`; choosing an option calls `Submit`, which grades the
   ruling against the case's `OptionAssessment`, writes a `decision` record, feeds the outcome to the
   `AdaptiveController` (writing an `adaptation` record if a rule fires) and requests advice for the next
   point.
8. **Finish.** After the third ruling, the session writes `session_end`, raises `Completed`, and the
   decision dialog shows the debrief built by `Debrief.Build`.
9. **Restart.** The debrief's restart button raises `UIManager.RestartRequested`. The controller cancels
   the old session's outstanding advice requests and starts a new session on the next case.

Advice requests run on Unity's main thread: `UnityWebRequest` requires it, so `OllamaClient` and
`ClaudeClient` do not use `ConfigureAwait(false)`. `AdviceChanged` is raised on the caller's synchronization context.

## Event catalogue (`CourtSim.Core.Events`)

| Event | Publisher | Typical subscribers |
| --- | --- | --- |
| `GameplayInputBlockChanged` | `UIManager` | `FirstPersonController`, `InteractionProbe` |
| `ScreenChanged` | `UIManager` | analytics (not yet subscribed) |
| `InteractionFocusChanged` | `InteractionProbe` | `UIManager` → HUD prompt |
| `InteractionRequested` | `InteractionProbe` | `UIManager` |
| `DecisionSubmitted` | `UIManager` | analytics. The decision log and adaptive difficulty are driven by `CaseSession.Submit`, not from this event. |

## The courtroom is data

`CourtroomLayout.CreateDefault()` describes every fixture as an axis-aligned box with a role, a facing
and an optional interaction. `Validate()` rejects duplicate ids, missing required fixtures,
out-of-bounds or overlapping solids, and a blocked spawn point; unit tests run it on the default layout.
`CourtroomBuilder` turns the layout into geometry: one collider per fixture, matching the validated
footprint, plus a `Visual` child decorated by `FixtureDecorator`. To replace placeholder geometry with
real models, change `FixtureDecorator` only.

The saved scene (`Assets/Scenes/CourtRoom.unity`) is generated by **CourtSim → Generate CourtRoom
Scene**, which runs the same builder with asset-backed materials. If the scene is opened empty,
the bootstrap builds the same room at runtime.

## Cases are data too

Case files are the ground truth for scoring: option grades, the authored advice for every flaw kind,
and the debrief text all come from them. Advice from either model must recommend the option the plan selected,
and the flaw recorded for it is always the planned one, so a model reply can change the wording but
never the score.

## Extension points for the next phases

| Need | Where it plugs in |
| --- | --- |
| VR locomotion (XR Interaction Toolkit) | New `IPlayerInputSource` plus an XR rig factory; reuse `MovementMath` |
| More cases | Add JSON files to `Assets/Resources/Cases` (`docs/CASE_FORMAT.md`) |
| More adaptation (case choice, flawed count) | Extend `AdaptiveController` (it already has the outcome history) and have the controller pass the result to `PickCase` and `AdvicePlanner.Create` |
| Trust analytics across sessions | Read `overTrust`/`underTrust` from `session_end` records, or the trainee file's `history` |
| Another local model | Pull it in Ollama and set **Local Model** on the controller; no code change |
| Another advice backend | Implement `IAdvisor` (reuse `AdvicePromptBuilder` and `AdviceReply`) and pass it to `CaseSessionController.UseAdvisor`, keeping `FallbackAdvisor` around it |
| A key-holding proxy | Set **Api Base Url** on the controller (`ClaudeClientOptions.BaseUrl`); see `docs/AI_ADVISOR.md` |
| Another HTTP stack | Implement `IHttpTransport` |
| Log export or analytics | Implement `IDecisionLogSink` and add it to the controller's `CompositeLogSink` |
