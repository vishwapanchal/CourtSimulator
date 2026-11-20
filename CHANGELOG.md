# Changelog

All notable changes are recorded here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Planned: Phase 2 (case data and AI assistant)
- Case file schema and loader producing `DecisionPointDefinition`s; 3–5 authored or SimCourt-derived cases.
- Claude API client on `UnityWebRequest` (streaming, timeouts, retries, key from environment or untracked file).
- Assistant advisor that produces correct and deliberately flawed suggestions from a defined flaw taxonomy.
- Evidence viewer bound to case exhibits.

### Planned: Phase 3 (logging and adaptation)
- Decision log (ruling, assistant suggestion, accept/challenge, latency) persisted per session.
- Adaptive difficulty policy in Core, driven by the log.
- XR Interaction Toolkit rig implementing `IPlayerInputSource`.

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
- Unity-dependent code (`CourtSim.Runtime`, `CourtSim.Editor`, PlayMode tests) was authored without a
  Unity editor available. It passed syntax and format checks but has not yet been compiled or run
  in Unity.
- Text uses Unity's legacy `UI.Text`; migrate to TextMeshPro for sharper type at VR distances.
- Placeholder materials assume the Built-in render pipeline; URP needs the materials converted.
