# Third-party notices

CourtSim is MIT-licensed (see `LICENSE`). This file records every piece of third-party
work the project uses or adapts, and the reference repositories that were consulted
but not copied. Update it in the same change that brings in new third-party material.

## Adapted code

### LLMUnity: EventSystem input-module selection

- **Source:** <https://github.com/undreamai/LLMUnity>, `Runtime/Helpers/EventSystemAutoSetup.cs`
- **Copyright:** Undream AI
- **License:** Apache License 2.0 (<https://www.apache.org/licenses/LICENSE-2.0>)
- **Used in:** `Assets/Scripts/Runtime/UI/UIManager.cs`, `EnsureEventSystem`
- **Changes:** Rewritten as a static helper that only *creates* an EventSystem when the scene has
  none (it never removes existing modules), with the same reflection lookup for
  `InputSystemUIInputModule` and a `StandaloneInputModule` fallback.

Apache-2.0 §4 notice: the adapted portion is distributed under Apache-2.0 terms. The rest of the
project remains MIT.

## Unity packages

The packages in `Packages/manifest.json` are Unity Technologies packages distributed under the
Unity Companion License (<https://unity.com/legal/licenses/unity-companion-license>). They are not
vendored into this repository.

## Out-of-Unity test tooling (`tools/CoreVerification`, not shipped in builds)

| Package | License |
| --- | --- |
| NUnit 3.14 | MIT |
| NUnit3TestAdapter | MIT |
| Microsoft.NET.Test.Sdk | MIT |
| coverlet.collector | MIT |

## Reference repositories (consulted, nothing copied)

These were reviewed in the Phase 0 audit (`docs/PHASE0_AUDIT.md`). No code, text or assets were
copied from them. Where a repository has no license file, **all rights are reserved by its
authors** and it must stay reference-only.

| Repository | License | Status |
| --- | --- | --- |
| [AgentCourt](https://github.com/relic-yuexi/AgentCourt) | None found | Reference only (ideas: adversarial lawyer agents) |
| [ai-assisted-moot-courts](https://github.com/princeton-polaris-lab/ai-assisted-moot-courts) | None found | Reference only (ideas: justice-specific questioning) |
| [AIMootCourt](https://github.com/HKUGenAI/AIMootCourt) | Apache-2.0 (ChatArena fork) | Reference only |
| [Lexai](https://github.com/naimakader/Lexai) | README claims MIT; no LICENSE file | Reference only (ideas: argument scoring UX) |
| [LLMUnity](https://github.com/undreamai/LLMUnity) | Apache-2.0 | One helper adapted (above); candidate offline-LLM backend |
| [Milo](https://github.com/Advanced-Reality-Lab/Milo) | README claims AFL-3.0; no LICENSE file | Reference only (ideas: XR voice agent) |
| [OpenAI-Unity](https://github.com/srcnalt/OpenAI-Unity) | MIT | Reference only (HTTP client patterns for Phase 2) |
| [SimCourt](https://github.com/Miracle-2001/SimCourt) | MIT | Reference only; candidate case-data source (attribute if used) |
| [unity-voice-agents](https://github.com/stefanwebb/unity-voice-agents) | CC BY-SA 4.0 | Reference only; ShareAlike terms make copying unsuitable |
