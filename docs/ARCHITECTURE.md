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
│   Json/ Cases/ Advising/ Claude/ Logging/ Sessions/         │
└─────────────────────────────────────────────────────────────┘
```

Dependencies point downward only. `CourtSim.Core` compiles without Unity, and
`tools/CoreVerification` builds it against .NET Standard 2.1 to prove that.

## Core namespaces added in Phase 2

| Namespace | Contents |
| --- | --- |
| `Json` | `JsonValue` and `JsonReader`: a minimal strict RFC 8259 reader and writer (insertion-ordered objects, nesting limit 64). Unity's `JsonUtility` can't represent case files or API payloads, and Core takes no third-party dependency. |
| `Cases` | `CaseFile` and its parts (facts, exhibits, decision points, factors, option grades, authored advice), and `CaseFileReader`, which validates strictly and reports every error with a JSON path. Format: `docs/CASE_FORMAT.md`. |
| `Advising` | The `FlawKind` taxonomy, `Advice`, `AdvicePlan` and `AdvicePlanner` (which points get flawed advice), `IAdvisor` and `AdviceRequest`, and the advisors `ScriptedAdvisor`, `ClaudeAdvisor` and `FallbackAdvisor`. `AdvicePromptBuilder` holds the system prompt, the brief and the output schema. See `docs/AI_ADVISOR.md`. |
| `Claude` | `ClaudeClient` (Messages API request, retries, refusal and truncation handling), its options and exceptions, and the `IHttpTransport` abstraction with `HttpRequestData` and `HttpResponseData`. |
| `Logging` | `DecisionLogEntry`, `AdviceOutcome`, `AdviceStance`, `SessionSummary`, and the sinks `MemoryLogSink`, `JsonLinesFileSink` and `CompositeLogSink`. Schema `courtsim.log/1`. |
| `Sessions` | `CaseSession`, one trainee's pass through one case (advice prefetch, scoring, logging), and `Debrief`. |

## Runtime pieces added in Phase 2

| Type | Role |
| --- | --- |
| `Cases/CaseRepository` | Loads every `TextAsset` under `Resources/Cases`, validates each with `CaseFileReader`, logs and skips invalid files, and sorts by difficulty, then id. |
| `Advising/UnityWebRequestTransport` | `IHttpTransport` on `UnityWebRequest`. Works on every Unity platform; call it from the main thread. |
| `Advising/ApiKeyLocator` | Finds the Anthropic key in `ANTHROPIC_API_KEY`, else `<persistentDataPath>/anthropic_api_key.txt`. Never in `Assets/` or a build. |
| `App/CaseSessionController` | Picks the case, builds the flaw plan, chooses the advisor, opens the log file and attaches the session to the UI. Added by the bootstrap if the scene has none. Inspector settings: Case Id, Flawed Advice Count, Seed, Use Claude, Model, Effort, Api Base Url, Write Log File. |

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
  (Scripted, or Fallback(Claude, Scripted))    │
                                               ▼
                    UIManager: decision dialog, assistant card, evidence viewer, debrief
```

1. **Load.** `CaseRepository.LoadAll` runs once, on the first session.
2. **Pick.** The controller plays the configured **Case Id**, or cycles through the cases from easiest to
   hardest, one per session.
3. **Plan.** `AdvicePlanner.Create(case, flawedCount, random)` fixes the flaw for each decision point
   before any advice exists. The model never chooses it.
4. **Advisor.** With an API key and **Use Claude**:
   `FallbackAdvisor(ClaudeAdvisor(ClaudeClient(options, UnityWebRequestTransport)), ScriptedAdvisor)`.
   Otherwise `ScriptedAdvisor`.
5. **Sink.** `MemoryLogSink`, plus `JsonLinesFileSink` at `<persistentDataPath>/logs/` when **Write Log
   File** is on.
6. **Start.** `CaseSession.Start` writes `session_start` and requests advice for the first point.
   `UIManager.AttachSession` points the HUD, decision dialog, assistant card and evidence viewer at the
   session, and refreshes them on `AdviceChanged`.
7. **Rule.** Opening the decision dialog calls `MarkPresented` (starting the decision timer); Accept or
   Challenge on the assistant card calls `SetStance`; choosing an option calls `Submit`, which grades the
   ruling against the case's `OptionAssessment`, writes a `decision` record and requests advice for the
   next point.
8. **Finish.** After the third ruling, the session writes `session_end`, raises `Completed`, and the
   decision dialog shows the debrief built by `Debrief.Build`.
9. **Restart.** The debrief's restart button raises `UIManager.RestartRequested`. The controller cancels
   the old session's outstanding advice requests and starts a new session on the next case.

Advice requests run on Unity's main thread: `UnityWebRequest` requires it, so `ClaudeClient` does not use
`ConfigureAwait(false)`. `AdviceChanged` is raised on the caller's synchronization context.

## Event catalogue (`CourtSim.Core.Events`)

| Event | Publisher | Typical subscribers |
| --- | --- | --- |
| `GameplayInputBlockChanged` | `UIManager` | `FirstPersonController`, `InteractionProbe` |
| `ScreenChanged` | `UIManager` | analytics (Phase 3) |
| `InteractionFocusChanged` | `InteractionProbe` | `UIManager` → HUD prompt |
| `InteractionRequested` | `InteractionProbe` | `UIManager` |
| `DecisionSubmitted` | `UIManager` | adaptive difficulty (Phase 3). The decision log is written by `CaseSession`, not from this event. |

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
and the debrief text all come from them. Advice from Claude must recommend the option the plan selected,
and the flaw recorded for it is always the planned one, so a model reply can change the wording but
never the score.

## Extension points for the next phases

| Need | Where it plugs in |
| --- | --- |
| VR locomotion (XR Interaction Toolkit) | New `IPlayerInputSource` plus an XR rig factory; reuse `MovementMath` |
| More cases | Add JSON files to `Assets/Resources/Cases` (`docs/CASE_FORMAT.md`) |
| Adaptive difficulty | Core-side policy that reads `SessionSummary` and the log, then picks the next case by difficulty and passes the flawed count and flaw weights to `AdvicePlanner.Create` |
| Another advice backend | Implement `IAdvisor` and pass it to `CaseSessionController.UseAdvisor`, keeping `FallbackAdvisor` around it |
| A key-holding proxy | Set **Api Base Url** on the controller (`ClaudeClientOptions.BaseUrl`); see `docs/AI_ADVISOR.md` |
| Another HTTP stack | Implement `IHttpTransport` |
| Log export or analytics | Implement `IDecisionLogSink` and add it to the controller's `CompositeLogSink` |
