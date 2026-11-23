# AI advisor

At each decision point the assistant card suggests a ruling. Some suggestions are deliberately flawed,
and the debrief reveals which ones and why. This page covers how flaws are chosen, how advice is
produced (offline from the case file, or written by a language model: a local model in Ollama by
default, or Claude), what each backend is sent, how failures are handled, and the decision log format.

Code: `Assets/Scripts/Core/Advising/`, `Assets/Scripts/Core/Ollama/`, `Assets/Scripts/Core/Claude/`,
`Assets/Scripts/Core/Http/`, `Assets/Scripts/Core/Logging/`,
`Assets/Scripts/Core/Sessions/`, and the Unity glue in `Assets/Scripts/Runtime/Advising/` and
`Assets/Scripts/Runtime/App/CaseSessionController.cs`. Case authoring: `docs/CASE_FORMAT.md`.

## Flaw taxonomy

`FlawKind` (`Core/Advising/Advice.cs`). Wire names are used in case files and logs.

| Wire name | Meaning | Example (`hale-burglary.json`) |
| --- | --- | --- |
| `none` | Sound advice: the correct law applied to an accurate reading of the record. | Release on conditions: the presumption of release applies, and light conditions manage the modest risks. |
| `misstated_law` | Applies the wrong legal test, burden or rule. | "An unbroken chain of custody is a precondition of admissibility." |
| `misread_fact` | Relies on something the record does not say. | "His record shows he has failed to appear before." He never has (F4). |
| `omitted_factor` | Ignores a material consideration that changes the outcome. | A custodial sentence that never accounts for the early guilty plea (F6). |
| `improper_factor` | Relies on a consideration the law treats as irrelevant or forbidden. | Refusing bail because the accused stayed silent in interview. |

The taxonomy is what the debrief teaches and what adaptive difficulty works with: `AdaptiveController`
ranks the kinds from obvious to subtle (`misread_fact`, `misstated_law`, `improper_factor`,
`omitted_factor`) and shifts toward the subtle end for a trainee who keeps following flawed advice. See
[Adaptive difficulty](#adaptive-difficulty).

## Who decides the flaws

**Game logic decides, never the model.** At the start of each session, `AdvicePlanner.Create` builds an
`AdvicePlan`: one `FlawKind` per decision point.

- It flaws exactly **Flawed Advice Count** points (a `CaseSessionController` setting, 0 to 3, default 1),
  clamped to the points that have authored flawed variants. Points are chosen at random.
- For each flawed point it draws a flaw kind from that point's authored variants (optional per-kind
  weights; missing kinds weigh 1; 0 excludes a kind where possible).
- **Seed** on the controller fixes the plan; 0 draws a new plan every session.
- With **Adaptive** on (the default), the plan still fixes *which* points are flawed, but the kind is
  re-chosen by `AdaptiveController.ChooseFlaw` when that point's advice is requested, from the same
  authored variants. It is still game logic, never the model, and the chosen kind is in the `decision`
  record's `advice.flaw`.

## Adaptive difficulty

`AdaptiveController` (`Core/Adaptive/AdaptiveController.cs`) learns from each ruling's outcome (rolling
history of 20). "Accepted" means the ruling matched the suggestion, whatever the Accept/Challenge click.

| Trigger | Effect on the advice |
| --- | --- |
| Two flawed suggestions accepted in a row (`misled_by_flaw` ×2) | Subtlety +1 (max 3). Each authored flaw kind weighs (1 + rank)^subtlety, rank 0 = `misread_fact` … 3 = `omitted_factor`. |
| Two sound suggestions rejected in a row (`overrode_sound` ×2) | The next request carries `ExplanationDetail.Detailed`, once. |

**Explanation detail.** `AdviceRequest.Detail` is `Standard` or `Detailed`. For `Detailed`, the brief adds:
"Explanation: detailed. Use four to six sentences (up to 160 words) and explain how each factor you cite
supports the ruling, describing every factor in plain words, never by its id." `ScriptedAdvisor` (and so
the fallback) appends the cited factors in plain words to the authored rationale. The `decision` record's
`advice.detail` says which level was used.

State is saved per trainee at `<persistentDataPath>/trainees/<Trainee Id>.json`. Each firing shows a toast
and writes an [`adaptation`](#adaptation) record. A live run with `qwen3.5:9b`, where the subtlety rule
fires after two accepted flaws, is in `docs/evidence/phase-b-adaptive-playtest.md`.

The plan selects an authored advice entry for every point (`AdviceRequest.TargetScript`). That entry
fixes the **recommended option** and the **flaw**. Whichever advisor answers must recommend that option,
and the flaw recorded in the log is always the planned one. Scoring and the debrief therefore rest only
on authored ground truth: option grades and flaw explanations from the case file.

## Advisors

| Class | Role |
| --- | --- |
| `ScriptedAdvisor` | Offline. Returns the authored entry verbatim (rationale, cites, confidence, flaw explanation). Instant, deterministic and free. Used when **Use Model** is off, when Claude is selected without a key, in tests, and as the fallback. |
| `LocalModelAdvisor` | Asks a local model in Ollama (default `qwen3.5:9b`) to write the rationale for the planned ruling and flaw. No key, no internet, no cost. |
| `ClaudeAdvisor` | Asks Claude to write the rationale for the planned ruling and flaw. |
| `FallbackAdvisor` | Tries the model advisor; on any failure other than cancellation, answers with `ScriptedAdvisor` for the same plan. |

Both model advisors send the same system prompt, brief and schema (`AdvicePromptBuilder`) and validate
the reply with the same code (`AdviceReply.Parse`), so they obey identical rules.

`CaseSessionController.CreateAdvisor` picks one per session:

1. An advisor set with `UseAdvisor(...)` (tests, custom backends). Logged as `"custom"`.
2. **Use Model** off: `ScriptedAdvisor`. Logged as `"scripted"`.
3. **Backend** `LocalModel` (the default):
   `FallbackAdvisor(TimeoutAdvisor(CachingAdvisor(LocalModelAdvisor)), ScriptedAdvisor)`. Logged as
   the model tag, e.g. `"qwen3.5:9b"`. If Ollama isn't running, each request fails fast and the offline
   advice is shown.
4. **Backend** `Claude` and `ApiKeyLocator.Find()` returns a key: the same chain around `ClaudeAdvisor`.
   Logged as the model id, e.g. `"claude-opus-5-5"`.

See [Speed: cache, prefetch, timeout and GPU check](#speed-cache-prefetch-timeout-and-gpu-check) for what
the extra layers do.
5. **Backend** `Claude` without a key: a Unity warning, then `ScriptedAdvisor`. Logged as `"scripted"`.

The assistant card's footnote shows where the current advice came from: "Written by a local AI model for
this case.", "Written by Claude for this case.", "Offline advice from the case file." or "Offline advice:
the AI model was unavailable."

## Backends

| | Local model (default) | Claude (optional) |
| --- | --- | --- |
| Runs | On this machine, in Ollama | Anthropic's API, over the internet |
| Needs | Ollama and a pulled model | An API key (or a proxy token) |
| Cost | Free | Per token |
| Reasoning quality | Good with the 9B default; basic with the light 0.8B tag | Strong |
| Controller fields | **Local Model**, **Ollama Url** | **Model**, **Effort**, **Api Base Url** |

### Local model (Ollama)

`OllamaClient` (`Core/Ollama/OllamaClient.cs`) sends one `/api/chat` call per decision point over the same
`IHttpTransport` as Claude (`Core/Http/`). No headers beyond `content-type`, no key.

```http
POST http://127.0.0.1:11434/api/chat
content-type: application/json
```

Body, for the same bail point as the [Claude request](#claude-request) (long strings shortened with `...`):

```json
{
  "model": "qwen3.5:9b",
  "messages": [
    { "role": "system", "content": "You are the AI judicial assistant in CourtSim, a training simulation for trainee judges. ..." },
    { "role": "user", "content": "<case>\nTitle: State v. Marcus Hale\n...\n</instructions>" }
  ],
  "stream": false,
  "think": false,
  "keep_alive": "60m",
  "options": { "temperature": 0.3, "num_ctx": 4096 },
  "format": { "type": "object", "properties": { "recommended_option_id": { "type": "string", "enum": ["bail.deny"] }, "...": "..." } }
}
```

`format` is the same JSON schema Claude receives in `output_config.format.schema`. `think: false` turns
off the model's thinking mode, which only adds delay for short advice. The reply text is read from
`message.content`.

| Setting | Value | Where to change it |
| --- | --- | --- |
| Model | `qwen3.5:9b` | Controller **Local Model** field (`OllamaClientOptions.Model`) |
| Base URL | `http://127.0.0.1:11434` | Controller **Ollama Url** field |
| Timeout | 180 s (the first request also loads the model) | `OllamaClientOptions.Timeout` |
| `temperature` | 0.3 | `OllamaClientOptions.Temperature` |
| `num_ctx` | 4096 tokens (brief plus reply stay under 2,000; 4k keeps the 9B model fully in 8 GB of VRAM) | `OllamaClientOptions.ContextTokens` |
| `keep_alive` | `60m`, so the model stays loaded for a whole presentation | `OllamaClientOptions.KeepAlive` |

**Errors.** `OllamaClient` raises `OllamaException` (with `StatusCode` when there was a response). There
are no retries; `FallbackAdvisor` serves the offline advice instead.

| Cause | Message |
| --- | --- |
| No response (Ollama not running, wrong URL, timeout) | `Could not reach Ollama at <url>. Is it running? (...)` |
| Error status. 404 usually means the model isn't pulled. | `Ollama returned an error: <error>.`, plus ` Run: ollama pull <model>` on 404 |
| Body is not a JSON object | `Ollama returned a body that is not a JSON object.` |
| `done_reason: "length"` | `The local model's reply was cut off before it finished.` |
| Blank `message.content` | `The local model returned an empty reply.` |

A reply that parses but fails validation (wrong option, no rationale) raises `AdvisorException`, as for
Claude.

**Performance.** Measured on an RTX 5050 Laptop GPU (8 GB) with Ollama 0.40.16. `qwen3.5:9b` at
`num_ctx` 4096 runs 100% on the GPU (about 5.6 GB): the first request takes about 30 s because it loads
the model; after that each piece of advice takes about 4–7 s (the Phase B playtest logged 4.2–5.2 s at
roughly 1,000–1,200 tokens in, 130–190 out). `qwen3.5:0.8b` takes about 60 s to load cold and about 2 s
per advice (about 625 MB of VRAM). Prefetching (see [Cost and latency](#cost-and-latency)) and
`demo.bat`'s preload hide most of the first load.

**Model choice.** Any Ollama tag that supports structured output works; set it in **Local Model** and
`ollama pull` it first.

| Tag | Download | Speed | Trade-off |
| --- | --- | --- | --- |
| `qwen3.5:9b` (default) | ~5.6 GB of VRAM at 4k context | ~4–7 s per advice | Best reasoning of the three; the strongest model that fits fully in an 8 GB GPU. Flawed rationales occasionally contradict themselves. |
| `qwen3.5:4b` | Smaller | Faster | A middle ground on a mid-range GPU. |
| `qwen3.5:0.8b` (light) | ~1.3 GB, ~625 MB VRAM | ~2 s per advice | Fast and runs almost anywhere. Rationales can run long, and it more often misstates a fact or gives the flaw away. |

Whatever the tag, the ruling and flaw are enforced by the plan and validation, so a weak rationale can
make the exercise less convincing but never changes the grade.

The system prompt asks for a rationale under 90 words with no option or factor ids in it, which helps the
small model most.

### Claude

Optional. Select **Backend** `Claude` and provide a key; see [Claude request](#claude-request),
[Enabling Claude advice](#enabling-claude-advice) and [API key handling](#api-key-handling). The system
prompt, brief, output schema and validation below apply to both backends; the retry policy, cost and key
sections are Claude only.

## Claude request

`ClaudeClient` (`Core/Claude/ClaudeClient.cs`) sends one Messages API call per decision point over an
injectable `IHttpTransport`. In Unity that is `UnityWebRequestTransport`, which works on every Unity
platform, including Android-based headsets, without extra DLLs. It must be called from the main thread.

```http
POST https://api.anthropic.com/v1/messages
content-type: application/json
x-api-key: <your API key>
anthropic-version: 2026-10-21
anthropic-beta: server-side-fallback-2026-07-01
```

Body, for the bail point of `hale-burglary` with a planned `misread_fact` flaw (long strings shortened
with `...`):

```json
{
  "model": "claude-opus-5-5",
  "max_tokens": 16000,
  "messages": [
    { "role": "user", "content": "<case>\nTitle: State v. Marcus Hale\n...\n</instructions>" }
  ],
  "system": "You are the AI judicial assistant in CourtSim, a training simulation for trainee judges. ...",
  "output_config": {
    "effort": "medium",
    "format": {
      "type": "json_schema",
      "schema": {
        "type": "object",
        "properties": {
          "recommended_option_id": { "type": "string", "enum": ["bail.deny"] },
          "rationale": { "type": "string", "description": "Two to four sentences addressed to the judge." },
          "cited_factor_ids": {
            "type": "array",
            "items": { "type": "string", "enum": ["ties", "record", "nonviolent", "strength", "presumption", "silence"] }
          },
          "confidence": { "type": "string", "enum": ["low", "medium", "high"] },
          "flaw_explanation": { "type": "string", "description": "Debrief explanation of the flaw; empty for sound advice." }
        },
        "required": ["recommended_option_id", "rationale", "cited_factor_ids", "confidence", "flaw_explanation"],
        "additionalProperties": false
      }
    }
  },
  "fallbacks": "default"
}
```

| Setting | Value | Where to change it |
| --- | --- | --- |
| Model | `claude-opus-5-5` | Controller **Model** field (`ClaudeClientOptions.Model`) |
| `output_config.effort` | `medium`, sent explicitly | Controller **Effort** field: `low`, `medium`, `high`, `xhigh` or `max` |
| `output_config.format` | JSON schema built per request by `AdvicePromptBuilder.BuildSchema` | Code |
| `fallbacks: "default"` + `anthropic-beta: server-side-fallback-2026-07-01` | On | `ClaudeClientOptions.UseServerFallbacks` |
| `max_tokens` | 16000 | `ClaudeClientOptions.MaxTokens` |
| API base URL | `https://api.anthropic.com` | Controller **Api Base Url** field (see [API key handling](#api-key-handling)) |

With server-side fallbacks on, a request the model declines can be re-run on a fallback model inside
the same call, so the trainee still gets advice. `ClaudeMessageResponse.Model` reports which model
actually answered.

### System prompt and brief

The system prompt is the constant `AdvicePromptBuilder.SystemPrompt`. It tells the model that the cases
are fictional, sets the voice (first person, two to four sentences, tied to the record), explains that
some advice is deliberately flawed, and tells it to embed exactly the described flaw without hinting at
it, to recommend exactly the given option, and to cite only the listed factors. It also asks for a
rationale under 90 words that names rulings and facts in plain words, never by option or factor id.
Both backends get the same system prompt and brief.

The user message is the brief from `AdvicePromptBuilder.BuildBrief`:

```text
<case>
Title: State v. Marcus Hale
Accused: Marcus Hale, age 24. Part-time warehouse worker who lives with his mother at the same address he has had for nine years.
Charges: Burglary of commercial premises; Handling stolen goods (alternative count)
Summary: A night-time burglary of an electronics shop. The accused was stopped two hours later with boxed laptops in his car.
Record:
- [F1] At 02:10 the alarm at Brightline Electronics sounded. ...
- ...
- [F6] Before trial Hale pleaded guilty to burglary at the first opportunity. ...
</case>

<decision_point>
Bail application. Hale applies for bail pending trial. The prosecution does not oppose release, but asks for conditions.
Prosecution submits: The case is strong and the goods were found in his car. ...
Defence submits: He has lived at the same address for nine years, works, and has never failed to appear. ...
Options:
- bail.grant: Release without conditions
- bail.grant_conditions: Release with a curfew, residence and no-contact conditions
- bail.deny: Refuse bail and remand in custody
Factors you may cite:
- ties: Stable address for nine years and current employment.
- ...
- silence: He declined to answer questions after his initial explanation.
</decision_point>

<instructions>
Recommend option: bail.deny
Advice type: flawed. Misread fact: the advice relies on something the record does not say.
The specific flaw to embed: The record shows the opposite: Hale paid his only fine and has never missed a court date (F4). The advice invents a failure to appear and builds the whole case for custody on it.
</instructions>
```

- The whole record is sent every time. An exhibit's title, description, provenance and objection are
  added inside `<case>` only for the point that references it.
- From the second point on, an `<earlier_rulings>` block lists the trainee's previous rulings by
  decision-point title and option label.
- For sound advice the instructions say `Advice type: sound.` The authored rationale is never sent;
  the model writes its own wording for the same ruling and flaw.

## Output schema and validation

The reply must be one JSON object, from either backend:

| Field | Schema | What `AdviceReply.Parse` does with it |
| --- | --- | --- |
| `recommended_option_id` | string, `enum` of the single planned option | Must equal the planned option, or the reply is rejected. |
| `rationale` | string | Must be non-blank, or the reply is rejected. Trimmed. Shown to the trainee. Two guards also reject it: for flawed advice, a rationale that **admits its own flaw** (contains "flaw", "mistake", "incorrect", "erroneous", "misappl…", "misstat…", "misread", "wrongly", "proceed as if" or "deliberately wrong", case-insensitive); and for any advice, a rationale that **leaks an internal id** (an option or factor id containing `.` or `_`, such as `bail.grant` or `early_plea`). |
| `cited_factor_ids` | array of strings, `enum` of this point's factor ids | Unknown ids and duplicates are dropped silently. |
| `confidence` | `low`, `medium` or `high` | An invalid value falls back to the authored confidence. |
| `flaw_explanation` | string | Sound advice: always cleared. Flawed advice: used for the debrief; if blank, the authored explanation is used instead. |

The flaw stored on the advice always comes from the plan, never from the reply. A rejected reply
raises `AdvisorException`, and `FallbackAdvisor` serves the authored advice instead.

## Failures, retries and fallback

Local model errors are listed under [Local model (Ollama)](#local-model-ollama); they are not retried.
`ClaudeClient` raises `ClaudeException` with a `Kind`:

| Kind | Cause | Retried |
| --- | --- | --- |
| `NotConfigured` | No API key. | No |
| `Transport` | No HTTP response: DNS, TLS, connection or timeout. | Yes |
| `Http` | Error status. `ErrorType` holds the API's `error.type`, `RequestId` the `request-id` header. | 408, 409, 429 and 5xx only |
| `Refusal` | HTTP 200 with `stop_reason: "refusal"` (after any server-side fallback). `ErrorType` holds `stop_details.category`. | No |
| `Truncated` | `stop_reason: "max_tokens"`. | No |
| `InvalidResponse` | Body is not a JSON object, or the reply has no text blocks. | No |

**Retry policy**

- At most **2 retries** (3 attempts).
- An `x-should-retry: true` or `false` response header overrides the status-based rule.
- The delay honours a numeric `retry-after` header (seconds), capped at 20 s. Without one it backs off
  exponentially: 0.5 s × 2^attempt plus up to 25% jitter (about 0.5 s, then about 1 s), capped at 20 s.
- Each attempt has a **90 s** timeout (`ClaudeClientOptions.Timeout`). In the worst case a request takes
  about 5 minutes (three timeouts plus two delays) before falling back.
- Cancellation (a new session starts, or the controller is destroyed) aborts the request in flight. It
  is not retried and does not fall back.

**Fallback**

Any other failure, including a reply rejected by validation, makes `FallbackAdvisor` answer with the
authored advice for the same planned flaw, whichever backend failed. The controller records the reason on
the session (`AI model unavailable, used offline advice: <message>`), which goes into that decision's
`adviceError`, and logs a Unity warning. The session never stalls on the network or on Ollama.

While advice is pending, the card says "I am reviewing the record for this ruling." The trainee can
still rule; that ruling is logged with outcome `no_advice`.

A reply that is empty or only whitespace counts as a failure too, so the panel is never left blank.

## Speed: cache, prefetch, timeout and GPU check

Slow or missing advice was traced to Ollama running the 9B model on the CPU (38 s for a tiny reply,
against 1.5 s on the GPU). Four measures keep advice fast and always present:

| Measure | Where | What it does |
| --- | --- | --- |
| **Disk cache** | `CachingAdvisor`, `AdviceCacheKey`, `AdviceCacheEntry` (Core); `FileAdviceStore` (Runtime) | Every model-written suggestion is saved as one JSON file in `<persistentDataPath>/advice-cache/`. A case seen before shows its advice instantly, even with Ollama off. A damaged or out-of-date file is ignored and the advice is generated again. Delete the folder to force fresh text. |
| **Prefetch** | `AdvicePrefetch` (Core), `CaseSessionController.StartPrefetch` | When a session starts, advice for all three decision points is generated (or loaded from the cache) one after another in the background. With adaptation on, a flawed point's flaw kind is chosen only when the judge reaches it, so every authored flaw of that point is prepared, the likelier first. |
| **No duplicates** | `CachingAdvisor` | The session's own request for a point joins a reply that is already being generated instead of sending a second request. |
| **25 s fallback** | `TimeoutAdvisor` inside `FallbackAdvisor` | If the model hasn't answered after **Model Wait Seconds** (default 25), the case file's scripted advice for the same variant is shown and the reason is logged (`[advice] AI advice failed or was too slow ...` in the console, `adviceError` in the decision log). The model keeps writing in the background and its reply is cached for next time. |
| **GPU check** | `OllamaModelStatus`, `OllamaClient.GetModelStatusAsync` | After the first generated reply, the game calls `GET /api/ps`. If the model's `size_vram` is 0, or under 90% of its `size`, it logs "Ollama is running the model on the CPU; restart Ollama to use the GPU". |

**Cache key.** `v<PromptVersion>_<model>_<case id>_p<point>-<kind>_<flaw>_<detail>_<fingerprint>`, for example
`v1_qwen3.5-9b_state-v-arif-mulla_p0-bail_none_standard_1a2b3c4d`. The fingerprint is an FNV-1a hash of the
system prompt and the brief, so editing a case file or the prompt makes old entries stop matching. Bump
`AdviceCacheKey.PromptVersion` when the schema or validation changes. Earlier rulings are left out of the
key on purpose: they only colour the wording, while the ruling and the flaw come from the case file.
Leaving them out is what lets advice be prepared before the hearing (and it means prefetched briefs have no
`<earlier_rulings>` block). Only model-written advice is cached, never the scripted fallback. The disk cache
is only used when **Write Log File** is on, so tests leave no files.

Controller fields: **Model Wait Seconds**, **Prefetch Advice**, **Cache Advice On Disk**.

**`demo.bat`** stops any running Ollama (`ollama.exe`, `ollama app.exe`), starts `ollama serve` fresh,
preloads the model with a one-token request (`keep_alive` 60m), then prints `ollama ps` and a warning
if the PROCESSOR column mentions the CPU.

## Cost and latency

Prefetching applies to both backends. The local model is free; its latency is under
[Local model (Ollama)](#local-model-ollama). The rest of this section is about Claude.

- **One request per decision point**: three per session, plus any retries. Requests are not streamed or
  batched.
- **Prefetched.** Advice for all three points is requested, one after another, when the session starts
  (see [Speed](#speed-cache-prefetch-timeout-and-gpu-check)); briefs prepared this way have no earlier
  rulings. Cached advice costs nothing: a case played before sends no requests at all.
- **Input** is the system prompt plus the brief: about 4,400 characters for the Hale bail point, or
  roughly 1,000 to 1,500 tokens for the shipped cases. Measure actual usage with
  `ClaudeMessageResponse.InputTokens` and `OutputTokens` (`ClaudeAdvisor.LastResponse`).
- **Output** is a short JSON object plus the model's thinking. Claude Opus 5.5 always thinks adaptively;
  thinking is billed as output and counts toward `max_tokens`. **Effort** is the main cost and latency
  lever: `low` is faster and cheaper, `high` and above are slower. Only text blocks are read from the reply.
- **No prompt caching.** The system prompt is identical across requests, but no `cache_control` is sent
  and the prompt is shorter than the minimum cacheable prefix.
- Prices change; check Anthropic's pricing page. At the time of writing Claude Opus 5.5 costs $4 per
  million input tokens and $20 per million output tokens. A request answered after a server-side
  fallback is billed according to Anthropic's fallback rules.
- Token usage and the answering model are not yet written to the decision log.

## Enabling local advice (default)

1. Install Ollama: `winget install Ollama.Ollama` (or the installer from ollama.com). It installs per
   user and runs in the background on port 11434.
2. Pull the model once: `ollama pull qwen3.5:9b` (or run `demo.bat`, which pulls and preloads it). On a
   GPU with less than 8 GB, pull `qwen3.5:0.8b` and set it in **Local Model**.
3. Press Play. **Use Model** is on and **Backend** is `LocalModel` by default. The assistant card footnote
   reads "Written by a local AI model for this case.", and the log's `session_start` record shows
   `"advisor":"qwen3.5:9b"`.

No key is needed. If Ollama isn't running or the model isn't pulled, the card shows the offline advice and
the Unity console says why. To use only the offline advice, untick **Use Model**.

Ollama and the model are installed by each user; neither is in the repository or in builds.

To try it headless: `tools/unity.ps1 -Task live` plays one case against the local model and writes
`Logs/batch/live/live-transcript.md`. Pass runner options with `-LiveArgs`, e.g.
`-LiveArgs '-liveModel qwen3.5:0.8b'` or `-LiveArgs '-liveBackend claude'` (needs `ANTHROPIC_API_KEY`).

## Enabling Claude advice

Claude is optional. To use it:

1. Get an Anthropic API key.
2. Make it available by **one** of:
   - **Environment variable** `ANTHROPIC_API_KEY`. On Windows, for your user:
     ```powershell
     [Environment]::SetEnvironmentVariable("ANTHROPIC_API_KEY", "<your key>", "User")
     ```
     Restart Unity Hub and the Editor afterwards so they inherit it.
   - **Key file** `anthropic_api_key.txt` in `Application.persistentDataPath`, containing only the key.
     On Windows that is `%USERPROFILE%\AppData\LocalLow\DefaultCompany\CourtSim\anthropic_api_key.txt`
     (the folder follows **Company Name** and **Product Name** in Player Settings). Surrounding whitespace
     is ignored.

   The environment variable wins if both are set. An unreadable file is treated as no key.
3. On the Case Session Controller, leave **Use Model** ticked and set **Backend** to `Claude`. The
   bootstrap adds a controller with default settings (local backend) if the scene has none, so add one to
   the scene to change it.
4. Press Play. The assistant card footnote reads "Written by Claude for this case.", and the log's
   `session_start` record shows `"advisor":"claude-opus-5-5"`.

With **Backend** `Claude` and no key, the controller logs a warning and uses the offline advice. To switch
back, set **Backend** to `LocalModel`.

## API key handling

`ApiKeyLocator` (`Runtime/Advising/ApiKeyLocator.cs`) reads the key from the environment variable, then the
key file, and nothing else. The key never enters the project or a build. The local backend uses no key.

- **Never commit a key**, and never put one under `Assets/`. Everything under `Assets/` is tracked by git,
  and anything under `Assets/Resources/` is packed into player builds.
- Never log the key. `ClaudeClient.BuildHeaders()` returns it in `x-api-key`; don't print the headers.

> **Security warning.** A key stored on a trainee's machine, in an environment variable or a file, can be
> read by anyone who uses that machine, and a key shipped inside a client application can always be
> extracted. Use your own key only for local development.

**For deployments, use a proxy.** Run a small service you control that holds the real key, and set the
controller's **Api Base Url** to it. The client posts to `<base URL>/v1/messages` with the headers shown
above. The controller only enables Claude when `ApiKeyLocator` finds a key, and it sends that value as
`x-api-key`, so give each trainee machine a token issued by your proxy in that slot, never the Anthropic
key. The proxy checks the token, replaces it with the real key and forwards the request. It is also the
place to enforce rate limits, pin the model and record usage. Use HTTPS.

The request also states which advice is flawed. Anyone who inspects traffic on the trainee's machine can
see the plan. That affects the integrity of an exercise, not security; a proxy does not hide it unless
the proxy builds the brief itself.

## Voice (Sarvam, Kannada-English)

Every spoken line (the assistant's advice, and the bench clerk, Public Prosecutor and defence advocate in
the hearing) is spoken in **Kannada-English code-mixed** speech, the way people in Karnataka courts
actually talk ("Case strong ಇದೆ: ಅವರ room-ನಿಂದ phones recover ಮಾಡಲಾಗಿದೆ."). **All text on screen,
including subtitles and the assistant card, stays English.** Only the audio changes.

**Pipeline** (`AssistantVoice.Speak(englishText, role)` in `Runtime/Audio/AssistantVoice.cs`, using
`SarvamClient` in `Core/Sarvam/SarvamClient.cs`):

1. Look the line up in the disk cache. A hit plays at once, with no network and no cost.
2. `POST https://api.sarvam.ai/translate`: `mayura:v1`, `en-IN` to `kn-IN`, `mode: "code-mixed"`, and
   `speaker_gender` of the role's voice (so verb forms match the speaker). Inputs over 1,000 characters
   are split at sentence ends and the translations joined.
3. `POST https://api.sarvam.ai/text-to-speech`: `bulbul:v3`, `kn-IN`, the role's speaker, pace 1.0,
   22,050 Hz. Inputs over 2,500 characters are split the same way and the WAV pieces joined
   (`WavWriter.Concatenate`).
4. Save the WAV to the cache and play it through an `AudioSource`.

The requests go through `UnityWebRequestTransport` with async/await on the main thread, so nothing
blocks. While steps 2–3 run, `IsPreparing` is true: the typewriter text and the demo recorder wait for it.
`Stop()` (or a new line) cancels the requests and the audio. In batch mode nothing is spoken or sent.

**Voices** (`Core/Sarvam/SarvamVoiceCast.cs`):

| Role | Speaker |
| --- | --- |
| AI assistant | always `roopa` (female), so the trainee always recognises it |
| Public Prosecutor | male, chosen per case |
| Bench clerk | male, chosen per case, never the same as the prosecutor |
| Defence advocate | male or female, chosen per case, never the same as the other two |

`SarvamVoiceCast.For(caseId)` picks the per-case voices from fixed pools (male: anand, vijay, gokul, rahul,
shubh, aditya, kabir, rohan, varun, mohit; female: kavitha, priya, kavya, ritu, shruti, neha, pooja,
ishita, rupali; `roopa` is reserved). It hashes the case id with 32-bit FNV-1a, not `string.GetHashCode`
(which can differ between runs), so a case always gets the same voices. With no case id the default cast
is anand / kavitha / gokul. `HearingDirector` sets `AssistantVoice.CaseId` from `session.Case.Id`.

**Key.** `SarvamKeyLocator` (`Runtime/Audio/SarvamKeyLocator.cs`) reads the environment variable
`SARVAM_API_KEY`, then the one-line file `sarvam_api_key.txt` in `Application.persistentDataPath`
(on Windows `%USERPROFILE%\AppData\LocalLow\DefaultCompany\CourtSim\`). The key is sent only in the
`api-subscription-key` header. It is never logged, and `SarvamClient` removes it from any error message.
`.gitignore` blocks `**/sarvam_api_key*`. The same rules as for the Anthropic key apply: never commit a
key, never put one under `Assets/`.

**Cache.** `<persistentDataPath>/voice-cache/<sha1>.wav`. The SHA-1 covers the role, speaker, gender,
models, language, mode, pace, sample rate and the English text, so changing any of them makes a new clip.
Using a clip marks it as recently used; beyond 300 clips the least recently used are deleted. Delete the
folder at any time to clear it. Play a demo once with a network connection and later runs of the same
lines are offline and free.

**Fallback.** With no key, or when Sarvam fails (no network, key rejected, rate limit (HTTP 429), server
error, or more than 60 seconds), the line is spoken in English by the offline Windows voice instead, and
one warning is logged for the whole session. After a rejected key, or three failures in a row, Sarvam is
not tried again until the next run.

**Privacy.** Every spoken line is sent to Sarvam AI's servers to be translated and synthesised. These are
the case's scripted submissions and the assistant's advice text; no trainee input, decision or log is
sent. Cached clips stay on the machine.

## Decision log

`CaseSession` writes the log through an `IDecisionLogSink` (`Core/Logging/DecisionLog.cs`).
`CaseSessionController` writes every record to memory (for the debrief) and, when **Write Log File** is on
(the default), to a file:

```text
<persistentDataPath>/logs/<yyyyMMdd-HHmmss>-<caseId>-<first 8 characters of sessionId>.jsonl
```

On Windows: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\CourtSim\logs\`. The timestamp is the session's
start time in UTC. One file per session.

Format: JSON Lines (one JSON object per line), UTF-8 without BOM, `\n` line endings, appended record by
record, so a crashed session still leaves a readable log. Write errors never interrupt a session
(`JsonLinesFileSink.LastError` holds the last one). Schema id: `courtsim.log/1`.

Every record has `type`, `schema`, `sessionId` (32 hex characters), `caseId` and `timestampUtc`
(ISO 8601, UTC, milliseconds). A complete session is one `session_start`, three `decision` records (each
possibly followed by an `adaptation` record) and one `session_end`.

### `session_start`

```json
{"type":"session_start","schema":"courtsim.log/1","sessionId":"3f2a9c1e7b4d4e0f9a6b2c8d1e5f7a90","caseId":"hale-burglary","caseTitle":"State v. Marcus Hale","difficulty":1,"advisor":"claude-opus-5-5","plan":["misread_fact","none","none"],"timestampUtc":"2026-11-20T09:15:02.481Z"}
```

| Field | Meaning |
| --- | --- |
| `caseTitle`, `difficulty` | From the case file. |
| `advisor` | The requested model: the Ollama tag (e.g. `qwen3.5:9b`) or Claude model id, even if every request later falls back. `scripted` when **Use Model** is off or Claude has no key, `custom` for an override. |
| `plan` | The planned flaw per decision point, in order. With adaptive difficulty on, flawed points read `"flawed"`, because the kind is chosen when the advice is requested; the `decision` record's `advice.flaw` has it. |

### `decision`

Advice written by Claude and followed (the trainee was misled):

```json
{"type":"decision","schema":"courtsim.log/1","sessionId":"3f2a9c1e7b4d4e0f9a6b2c8d1e5f7a90","caseId":"hale-burglary","timestampUtc":"2026-11-20T09:16:03.851Z","index":0,"kind":"bail","optionId":"bail.deny","quality":"unsound","secondsToDecide":41.37,"stance":"accepted","outcome":"misled_by_flaw","advice":{"recommendedOptionId":"bail.deny","flaw":"misread_fact","confidence":"medium","source":"claude","followed":true}}
```

Offline advice after a model failure (here Claude; a local failure reads e.g. `... Could not reach Ollama at http://127.0.0.1:11434. Is it running? ...`):

```json
{"type":"decision","schema":"courtsim.log/1","sessionId":"3f2a9c1e7b4d4e0f9a6b2c8d1e5f7a90","caseId":"hale-burglary","timestampUtc":"2026-11-20T09:17:06.901Z","index":1,"kind":"evidence_admissibility","optionId":"evidence.admit","quality":"sound","secondsToDecide":63.05,"stance":"none","outcome":"trusted_sound","advice":{"recommendedOptionId":"evidence.admit","flaw":"none","confidence":"high","source":"scripted","followed":true},"adviceError":"AI model unavailable, used offline advice: Claude API returned HTTP 529 (overloaded_error): Overloaded"}
```

A ruling made before advice was ready:

```json
{"type":"decision","schema":"courtsim.log/1","sessionId":"3f2a9c1e7b4d4e0f9a6b2c8d1e5f7a90","caseId":"hale-burglary","timestampUtc":"2026-11-20T09:17:19.701Z","index":2,"kind":"sentencing","optionId":"sentence.non_custodial","quality":"sound","secondsToDecide":12.8,"stance":"none","outcome":"no_advice","advice":null,"adviceError":"Advice was not ready when the ruling was made."}
```

| Field | Meaning |
| --- | --- |
| `index`, `kind` | Decision point position (0 to 2) and kind. |
| `optionId` | The ruling. |
| `quality` | The author's grade of that ruling: `sound`, `defensible` or `unsound`. |
| `secondsToDecide` | From when the decision dialog was first shown for this point (or, if it never was, from the previous ruling or the session start) to the ruling. Rounded to 0.01 s. |
| `stance` | The trainee's last Accept/Challenge click before ruling: `accepted`, `challenged` or `none`. Always `none` when no advice was shown. |
| `outcome` | How the ruling relates to the advice; see below. |
| `advice` | `null` when no advice was ready. Otherwise `recommendedOptionId`, `flaw`, `confidence`, `source` (`local`, `claude` or `scripted`), `followed` (ruling equals the recommendation) and `detail` (`standard` or `detailed`). |
| `adviceError` | Present only when set: why advice was missing, failed, was cancelled, or came from the fallback. |

### Outcomes

| Outcome | Advice | Ruling |
| --- | --- | --- |
| `trusted_sound` | Sound | Followed the recommendation. |
| `overrode_sound` | Sound | Departed from the recommendation. |
| `misled_by_flaw` | Flawed | Followed the recommendation. |
| `caught_flaw` | Flawed | Departed from the recommendation. |
| `no_advice` | None ready when the trainee ruled | Any. |

The outcome depends only on whether the ruling matches the recommendation, not on the stance: a trainee
can challenge advice and still follow it. `caught_flaw` means only that the flawed recommendation was not
followed; read `quality` to see whether the ruling chosen instead was sound.

### `adaptation`

Written straight after the `decision` record that made an adaptive rule fire (so a session can hold zero or
more):

```json
{"type":"adaptation","schema":"courtsim.log/1","sessionId":"3f2a9c1e7b4d4e0f9a6b2c8d1e5f7a90","afterIndex":1,"note":"Two flawed suggestions accepted in a row: flaws are now subtler (level 1).","state":{"subtlety":1,"detailedExplanationPending":false,"acceptedFlawsInARow":0,"rejectedSoundInARow":0,"history":["misled_by_flaw","misled_by_flaw"]},"timestampUtc":"2026-11-20T12:22:41.118Z"}
```

`afterIndex` is the decision point just ruled on, `note` is the text of the in-game toast, and `state` is
the controller's full state, the same JSON saved to `trainees/<id>.json`.

### `session_end`

```json
{"type":"session_end","schema":"courtsim.log/1","sessionId":"3f2a9c1e7b4d4e0f9a6b2c8d1e5f7a90","caseId":"hale-burglary","timestampUtc":"2026-11-20T09:17:19.701Z","summary":{"rulings":3,"soundRulings":2,"defensibleRulings":0,"unsoundRulings":1,"flawedAdviceSeen":1,"flawsCaught":0,"timesMisled":1,"soundAdviceSeen":1,"soundAdviceOverridden":0,"flawDetectionRate":0}}
```

`summary` is `SessionSummary`: ruling counts by quality, then `flawedAdviceSeen` (`flawsCaught` +
`timesMisled`), `soundAdviceSeen` (trusted + overridden), `soundAdviceOverridden`, and
`flawDetectionRate` = `flawsCaught / flawedAdviceSeen`, rounded to 3 decimals, or `null` when no flawed
advice was shown. Rulings with `no_advice` count toward the quality totals only.

Phase C (in progress) adds `overTrust` = `timesMisled / flawedAdviceSeen` (flawed suggestions accepted ÷
shown) and `underTrust` = `soundAdviceOverridden / soundAdviceSeen` (sound suggestions rejected ÷ shown),
each `null` when the denominator is 0. The debrief shows them as percentages with a one-line reading:
50% or more is high, 25% or more moderate, otherwise low.

The debrief (`Core/Sessions/Debrief.cs`) is built from the same entries: for each ruling, its grade, the
sound ruling if the trainee missed it, the author's feedback, a line describing the outcome, and for
flawed advice the flaw kind and its explanation.
