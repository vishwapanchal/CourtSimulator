# CourtSim: viva cheat sheet

## What it does

CourtSim is a courtroom simulation (Unity 2022.3, desktop first-person; VR planned) where a trainee judge
makes three rulings: bail, admissibility of evidence and sentencing. An AI assistant suggests each ruling,
and some suggestions are deliberately flawed. The system grades every ruling against an authored case
file, adapts the difficulty to how the trainee treats the advice, and ends with a debrief that reveals
the flaws and scores over-trust and under-trust in the AI.

## Architecture at a glance

```
Case files (JSON, ground truth)
        │
        ▼
   CaseSession ◀── AdvicePlanner      (which points get flawed advice)
        │      ◀── AdaptiveController (which flaw kind, how much explanation)
        ▼
   Advisor: Local LLM (Ollama qwen3.5:9b) | Claude (optional) | Scripted (authored, always the fallback)
        ▼
   UI: bench, assistant card, evidence viewer, toasts
        ▼
   DecisionLog (JSON Lines) ─▶ Debrief + trust scores
```

## The key design decision

**Game logic plants the flaws; the model only writes the wording.** The planner picks which advice is
flawed and the controller picks the flaw kind (misread fact, misstated law, improper factor, omitted
factor). The model must recommend the planned ruling, and every grade comes from the authored case file.
So a model mistake can make the advice less convincing, but it can never change the score. That makes
the training measurable, repeatable and safe to run on a small local model.

## Likely questions

**1. Why not let the LLM decide when to be wrong?**
I couldn't measure anything if it did. I'd have no reliable ground truth for what was flawed, and models
are inconsistent at deliberate error. Fixing the flaw in code means every log entry knows exactly what the
trainee faced, and validation rejects any reply that recommends a different ruling.

**2. How is correctness graded?**
Each case file grades every option as sound, defensible or unsound, with feedback. The ruling is looked up
in that table, never judged by the AI. Advice outcomes are trusted sound, overrode sound, misled by flaw or
caught flaw, decided by whether the ruling matched the suggestion.

**3. How does adaptation work, and why these rules?**
`AdaptiveController` keeps the last 20 outcomes. Two flawed suggestions accepted in a row raise subtlety
(max 3), weighting flaw kinds (1 + rank)^level toward the subtle end. Two sound suggestions rejected in a
row make the next suggestion explain itself in detail. Those are the two failure modes, over-trust and
under-trust, and each rule is the smallest fix for one of them. "Two in a row" ignores one-off slips. The
state is saved per trainee, logged and shown as a toast, so it's transparent.

**4. Why a local model?**
Privacy, cost and availability. Nothing leaves the machine, there's no API key or per-token bill, and it
works offline. `qwen3.5:9b` at a 4k context fits entirely in an 8 GB laptop GPU (about 4–7 s per suggestion,
prefetched); `qwen3.5:0.8b` is the light option. Claude is optional. If anything fails, the authored
advice is shown. Guards reject rationales that admit their flaw or leak internal ids.

**5. How do you know it works without clicking through the editor?**
Automated tests run headless. Core has 512/512 tests under .NET (98.75% line coverage). Unity has 517/517
EditMode and 39/39 PlayMode tests (91.5% coverage) via `tools/unity.ps1 -Task all -Coverage`.
`tools/unity.ps1 -Task live` plays real cases against the local model and writes a transcript.
`docs/evidence/phase-b-adaptive-playtest.md` shows adaptation firing live. `demo.bat` builds and launches
the game in one step.

**6. Limitations and what's next?**
There's no real VR yet. Next is XR Interaction Toolkit/OpenXR, teleport, world-space UI and TextMeshPro;
the input layer is already device-agnostic for this. Only three cases ship. The small model sometimes
contradicts itself inside a flawed rationale, which weakens realism but not grading. Trust scores (Phase C)
are being finished, and adapting case choice is future work.
