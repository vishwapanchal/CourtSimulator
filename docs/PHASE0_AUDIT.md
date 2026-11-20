# Phase 0: source repository audit

**Date:** 2026-11-20 · **Scope:** the nine repositories cloned into `../../sources/`.
**Question:** what can each one contribute to a Unity VR courtroom simulation where a trainee
judge makes three rulings with help from a deliberately fallible AI assistant?

## Summary

Only three of the nine repositories are Unity projects, and **none contains a courtroom scene,
3D models or VR locomotion**. The rest are Python or TypeScript research prototypes for LLM
courtroom agents. So "unifying the codebases" in practice means:

1. Build the Unity project, environment and UI from scratch, under one architecture and license.
2. Take *design ideas* from the agent-simulation repos, and copy no code from them.
3. Keep two repositories as candidates for later phases: **SimCourt** (MIT) for case data, and
   **LLMUnity** (Apache-2.0) as an offline LLM backend.

One small helper was adapted (LLMUnity's EventSystem input-module selection, Apache-2.0); it is
recorded in `NOTICES.md`.

## Per-repository findings

| Repository | Stack | License | Useful for | Decision |
| --- | --- | --- | --- | --- |
| **AgentCourt** | Python, LLM agents | None found (all rights reserved) | Adversarial lawyer agents that evolve; ideas for opposing-counsel behaviour | Reference only |
| **ai-assisted-moot-courts** | Python, Hydra, vLLM | None found | Prompts for justice-specific questioning; evaluation methodology | Reference only |
| **AIMootCourt** | Python (ChatArena fork), Azure OpenAI | Apache-2.0 | Three-agent moot court turn-taking | Reference only. Turn orchestration is simple enough to rewrite. |
| **Lexai** | Next.js 15, Supabase, OpenAI | README claims MIT; no LICENSE file | Argument scoring rubric; professor dashboard UX | Reference only. Feeds Phase 3 analytics design. |
| **LLMUnity** | Unity 2022.3+, llama.cpp, RAG | Apache-2.0 | Offline LLM inference and RAG inside Unity | EventSystem helper adapted. Candidate offline backend (optional package, Phase 2+). |
| **Milo** | Python, RTP audio, Whisper | README claims AFL-3.0; no LICENSE file | Voice agent architecture for XR | Reference only. Voice is out of scope until a later phase. |
| **OpenAI-Unity** | Unity 2020.3+, `UnityWebRequest`, Newtonsoft JSON | MIT | Async HTTP and streaming patterns | Reference only. Phase 2 writes a dedicated Claude client on `UnityWebRequest` with no Newtonsoft dependency. |
| **SimCourt** | Python, Gradio; ~1,800 JSON case/eval files | MIT | Structured Chinese criminal-case data (articles, charges, judgments) | Candidate Phase 2 case source. Needs translation and schema mapping; attribute in NOTICES if used. |
| **unity-voice-agents** | Unity 6, STT, event bus | CC BY-SA 4.0 | Event-bus and chat-display patterns | Reference only. ShareAlike would infect the codebase, so the event bus was written independently. |

## Technical observations

- **Unity versions diverge:** 2020.3 (OpenAI-Unity), 2022.3 (LLMUnity) and Unity 6 (voice agents).
  The project targets **2022.3 LTS**: it is supported, it is the minimum for LLMUnity, and it is
  compatible with XR Interaction Toolkit 2.x/3.x. Code is kept at C# 9 / .NET Standard 2.1.
- **Duplicated concerns across repos:** each Unity repo ships its own HTTP client, JSON layer and
  event dispatching. CourtSim has exactly one of each: `CourtSim.Core.Events.EventBus` now; one
  HTTP/LLM client in Phase 2.
- **Secrets:** AIMootCourt and ai-assisted-moot-courts pass API keys via shell exports and CLI
  arguments. CourtSim will load keys from an untracked file or the OS environment
  (`.gitignore` already blocks `.env` and `Resources/ApiKeys*.json`).
- **No reusable art.** All Phase 1 visuals are procedural placeholders, built so that imported models
  can replace them without touching layout, colliders or interaction code.

## Risks carried forward

| Risk | Mitigation |
| --- | --- |
| No usable case data in English | SimCourt translation, or author 3–5 synthetic cases for the MVP |
| Flawed-suggestion design is unspecified | Phase 2: define a taxonomy of flaws (wrong law, misread fact, ignored factor) per decision point |
| Unity editor not available on the dev machine | Resolved in 0.1.1: Unity 2022.3 is installed and `tools/unity.ps1` drives it headless. Core is still also verified under .NET (`tools/verify-core.ps1`) |
| Project folder lives under OneDrive | Resolved in 0.1.1: the project moved to a local folder outside OneDrive |
