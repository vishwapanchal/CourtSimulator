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
