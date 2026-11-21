# Case file format (`courtsim.case/1`)

A case file describes one training case: the record, the exhibits, and the three decision points the
trainee rules on (bail, then evidence admissibility, then sentencing). Each decision point carries the
author's grading of every option and the assistant's advice: one sound version and at least one
deliberately flawed one.

Case files are **ground truth**. Rulings are scored against the grades in the file, and the debrief
explains flaws using the file's text. `CaseFileReader` validates strictly, so a case that loads is
playable and gradeable.

All cases are **fictional** but set in **India**. Names, places (the shipped cases use the invented
Kesarnagar district) and events are made up and must not describe real people or real proceedings.
The law is real: offences and punishments under the Bharatiya Nyaya Sanhita, 2023 (BNS), arrest, bail
and procedure under the Bharatiya Nagarik Suraksha Sanhita, 2023 (BNSS), evidence under the Bharatiya
Sakshya Adhiniyam, 2023 (BSA), all in force from 1 July 2024, plus any special law a case needs (for
example the NDPS Act, 1985 or the Probation of Offenders Act, 1958). Use Indian court terms (FIR,
charge-sheet, Investigating Officer, Public Prosecutor or APP, defence advocate, panch witnesses) and
amounts in rupees (₹). Cases are teaching summaries, not legal advice.

**Cite only what you have checked.** Every section number and judgment in a case must be verified
against the bare Act or the judgment itself (India Code, the Gazette text or the Supreme Court
report). If a citation cannot be verified, state the principle without it. The model is told never to
invent citations, so a case file is the only source of them.

## Where case files live

- `Assets/Resources/Cases/<id>.json`, one case per file, with its `.meta` file committed alongside.
- `Runtime/Cases/CaseRepository.cs` loads every file in that folder, validates it with
  `Core/Cases/CaseFileReader.cs`, and sorts the valid cases by `difficulty`, then by `id`.
- An invalid file is skipped, and its errors are logged to the Unity Console as
  `Case file '<name>' is invalid and was skipped:` with one line per problem. The other cases still load.
  If no case is valid, the decision flow falls back to placeholder content.

Shipped cases:

| File | Title | Difficulty |
| --- | --- | --- |
| `state-v-sahil-kadam.json` | State v. Sahil Kadam (night house-breaking; s.63 BSA electronic evidence; probation of an offender under 21) | 1 |
| `state-v-mahesh-rathod.json` | State v. Mahesh Rathod (grievous hurt and a threatened witness; police confession vs. discovery under the s.23 BSA proviso) | 2 |
| `state-v-kunal-bisht.json` | State v. Kunal Bisht (NDPS intermediate quantity; s.37 bail; s.50 personal search) | 3 |

Start a new case by copying one of these.

## Writing a case

1. **Pick the teaching points first.** For each decision point, decide what the sound ruling is and
   why, and which mistakes a hurried assistant could plausibly make. The record and factors follow
   from that.
2. **Write the record (`facts`).** Number the facts `F1`, `F2` and so on. Every flaw must be detectable
   from the record or from a rule stated in a factor, so put the decisive detail somewhere the trainee
   can read it.
3. **Write each decision point:** the prompt, both sides' submissions, the factors, the options with
   grades and feedback, and the advice.
4. **List factors on both sides.** Include the considerations that support wrong answers as well as
   right ones (for example, "He declined to answer questions"). Advice may only cite factors listed
   for its decision point, so a flawed rationale needs its improper or distorted factor in the list.
5. **Validate**, then play the case end to end (see [Checking a case](#checking-a-case)).

## Field reference

Types are JSON types. **Required** strings must be non-empty and not just whitespace. Optional strings
may be omitted. A value of the wrong type (for example a number where a string is expected) counts
as missing.

### Top level

| Field | Type | Required | Rules and use |
| --- | --- | --- | --- |
| `schema` | string | yes | Exactly `"courtsim.case/1"`. |
| `id` | string | yes | Stable identifier, written to every log record and matched by the controller's **Case Id** field. Use kebab-case and the same name as the file. Convention: `state-v-<first>-<last>`. The reader does not check uniqueness across files, so keep ids unique yourself. |
| `title` | string | yes | Shown in the evidence viewer, the debrief header and toasts, and sent to the model. Convention: `State v. <Name>`. |
| `summary` | string | no | One or two sentences. Sent to the model in the case brief. |
| `difficulty` | number | yes | Integer `1` (introductory) to `3` (advanced). Controls the order cases are played in (adaptive case selection is planned, not built). |
| `jurisdictionNote` | string | no | States that the case is fictional. Not enforced, but every case must include it. Use the wording from the shipped cases: "Training case. Fictional facts; legal provisions are the Bharatiya Nyaya Sanhita, Bharatiya Nagarik Suraksha Sanhita and Bharatiya Sakshya Adhiniyam (in force from 1 July 2024) as summarised for teaching. Not legal advice." Name any special law the case relies on as well. |
| `defendant` | object | yes | See [Defendant](#defendant). |
| `charges` | array of strings | yes | Non-empty. Each entry is a non-empty string. |
| `facts` | array of objects | yes | Non-empty. See [Facts](#facts). |
| `exhibits` | array of objects | no | May be omitted or empty, but the evidence decision point needs an exhibit to refer to, so in practice a case has at least one. See [Exhibits](#exhibits). |
| `decisionPoints` | array of objects | yes | Exactly three, in the order `bail`, `evidence_admissibility`, `sentencing`. See [Decision points](#decision-points). |

### Defendant

| Field | Type | Required | Rules and use |
| --- | --- | --- | --- |
| `name` | string | yes | Sent to the model. |
| `age` | number | yes | Whole number of years from 10 to 120. |
| `background` | string | no | One sentence of personal circumstances. Sent to the model. |

### Facts

The numbered case record. Every model request includes all of it.

| Field | Type | Required | Rules and use |
| --- | --- | --- | --- |
| `id` | string | yes | Unique among the case's facts. Convention: `F1`, `F2`, ... Flaw explanations refer to facts by id, for example `(F4)`. |
| `text` | string | yes | One self-contained fact. |

### Exhibits

| Field | Type | Required | Rules and use |
| --- | --- | --- | --- |
| `id` | string | yes | Unique among the case's exhibits. The exhibit number as read in court, e.g. `P-1`. |
| `title` | string | yes | Evidence viewer heading. |
| `description` | string | no | What the exhibit shows. |
| `tenderedBy` | string | no | Usually `Prosecution` or `Defence`. |
| `provenance` | string | no | How the exhibit was obtained and handled. |
| `objection` | string | no | The opposing side's objection. The viewer shows "None" when it is empty. |

The evidence viewer shows the exhibit referenced by the first decision point that has an `exhibitId`,
so set `exhibitId` only on the `evidence_admissibility` point. The model receives an exhibit's details only
in the request for the decision point that references it.

### Decision points

| Field | Type | Required | Rules and use |
| --- | --- | --- | --- |
| `kind` | string | yes | `bail`, `evidence_admissibility` or `sentencing`, in that order (index 0, 1, 2). |
| `title` | string | yes | Decision dialog heading, e.g. `Bail application`. Earlier rulings are described to the model by this title. |
| `prompt` | string | yes | The question before the court, shown in the decision dialog. |
| `exhibitId` | string | `evidence_admissibility` only | Must match an exhibit `id`. Required on the evidence point, optional (and best left out) on the others. |
| `submissions` | object | no | `prosecution` and `defence` strings, both optional. Shown in the decision dialog and sent to the model. Note the spelling `defence`. |
| `factors` | array of objects | yes | Non-empty. See [Factors](#factors). |
| `options` | array of objects | yes | At least two. See [Options](#options). |
| `advice` | array of objects | yes | One sound entry and at least one flawed entry. See [Advice](#advice). |

### Factors

Considerations relevant to one decision point. Advice cites them by id, and the model may cite only these.

| Field | Type | Required | Rules and use |
| --- | --- | --- | --- |
| `id` | string | yes | Unique within the decision point. Short snake_case, e.g. `early_plea`. The same id may appear in different decision points. |
| `text` | string | yes | One sentence. A factor can state a fact (`"Stable address for nine years"`) or a rule (`"Gaps in the chain of custody usually go to weight, not admissibility"`). |

### Options

The rulings the trainee can choose from, shown as buttons in the order listed. Three options per point
is the convention.

| Field | Type | Required | Rules and use |
| --- | --- | --- | --- |
| `id` | string | yes | Unique within the decision point. Convention: `bail.*`, `evidence.*`, `sentence.*` (e.g. `bail.grant_conditions`). Written to the log. |
| `label` | string | yes | Button text and debrief wording. |
| `quality` | string | yes | `sound` (what the record and the law best support), `defensible` (a reasonable judge could do it, but it is weaker) or `unsound` (not supported). At least one option per point must be `sound`. Use exactly one: the debrief names the first `sound` option as "the sound ruling". |
| `feedback` | string | no | Debrief text explaining the grade. Not enforced, but write it for every option. |

### Advice

The assistant's suggestion for this decision point, in every variant the session might show. The flaw
plan picks one entry per point at the start of each session; see `docs/AI_ADVISOR.md`.

| Field | Type | Required | Rules and use |
| --- | --- | --- | --- |
| `flaw` | string | yes | `none` (sound advice) or one of `misstated_law`, `misread_fact`, `omitted_factor`, `improper_factor`. Each value may appear at most once per point. |
| `recommends` | string | yes | An option `id` of this point. Sound advice (`none`) must recommend an option graded `sound`. Flawed advice must recommend an option that is **not** graded `sound`. |
| `confidence` | string | no | `low`, `medium` or `high`. Defaults to `medium`. Shown on the assistant card. |
| `rationale` | string | yes | What the trainee reads in offline mode: first person (`"I would..."`), two to four sentences. A model (local or Claude) writes its own wording when enabled, but for the same ruling and flaw. |
| `cites` | array of strings | no | Factor ids from this point that the rationale relies on. Each must exist. |
| `flawExplanation` | string | flawed only | Required, non-blank, for flawed advice. Shown in the debrief, and given to the model as the exact flaw to embed. Use `""` for sound advice. |

## Validation rules

`CaseFileReader.Read(json)` returns either a `CaseFile` or **every** problem it found, each prefixed with
a JSON path such as `decisionPoints[1].options[0].id`. Fix them all in one pass.

**Syntax**

- Strict RFC 8259 JSON: no comments, no trailing commas, nesting no deeper than 64 levels. A syntax
  error is reported alone, as `Invalid JSON: <detail>`.
- The root must be an object (`$: must be a JSON object`).

**Shape**

- `schema` must be `"courtsim.case/1"`.
- Required strings must be present and non-blank (`<path>: is required and must be a non-empty string`).
- `difficulty` must be an integer from 1 to 3. `defendant.age` must be a whole number from 10 to 120.
- `defendant` must be an object. Every element of `facts`, `exhibits`, `decisionPoints`, `factors`,
  `options` and `advice` must be an object (`<path>: must be an object`).
- `charges`, `facts`, `decisionPoints`, and each point's `factors`, `options` and `advice` must be present,
  must be arrays and must not be empty. `exhibits` and `cites`, when present, must be arrays.
- Each entry of `charges` and `cites` must be a non-empty string.

**Identity and references**

- Ids must be unique: fact ids within the case, exhibit ids within the case, and factor ids and option ids
  within their decision point.
- `decisionPoints` must hold exactly three points, with `kind` values `bail`, `evidence_admissibility`
  and `sentencing` in that order.
- An `exhibitId` must name an exhibit in the case. The `evidence_admissibility` point must have one.
- Every advice `recommends` must name an option of the same point. Every `cites` entry must name a factor
  of the same point. (Advice cannot cite facts directly; refer to them by id in the rationale or the
  flaw explanation instead.)

**Grading and advice**

- Each point needs at least two options, and at least one graded `sound`. `quality` must be `sound`,
  `defensible` or `unsound`.
- `flaw` must be a known flaw kind, at most once per point.
- Each point needs exactly one sound entry (`flaw: "none"`) and at least one flawed entry.
- Sound advice must recommend a `sound` option. Flawed advice must recommend an option that is not `sound`.
- Flawed advice needs a non-blank `flawExplanation`.
- `confidence`, when present, must be `low`, `medium` or `high`.

**What the reader does not check**

- **Unknown fields are ignored silently.** A misspelt optional field, such as `"defense"` instead of
  `"defence"` or `"flaw_explanation"` instead of `"flawExplanation"`, is dropped without an error (the
  latter then fails the required-explanation rule). Check spelling against this page.
- Case ids are not checked for uniqueness across files, and the file name is not compared with `id`.
- Facts need not be referenced, and `jurisdictionNote` and option `feedback` are not required. The
  conventions above still apply.

## Minimal valid example

A small case that passes every rule: two options, one sound and one flawed advice entry per point.
Real cases have more facts, three options per point, submissions and several flawed variants.

```json
{
  "schema": "courtsim.case/1",
  "id": "state-v-ravi-more",
  "title": "State v. Ravi More",
  "summary": "A bicycle worth ₹3,000 taken from outside a railway station was found at the accused's home the next day.",
  "difficulty": 1,
  "jurisdictionNote": "Training case. Fictional facts; legal provisions are the Bharatiya Nyaya Sanhita, Bharatiya Nagarik Suraksha Sanhita and Bharatiya Sakshya Adhiniyam (in force from 1 July 2024) as summarised for teaching. Not legal advice.",
  "defendant": { "name": "Ravi More", "age": 30 },
  "charges": ["Theft (section 303(2), Bharatiya Nyaya Sanhita, 2023)"],
  "facts": [
    { "id": "F1", "text": "A bicycle worth ₹3,000 was taken from outside Kesarnagar railway station and found at the accused's home the next day. It was returned to its owner undamaged." },
    { "id": "F2", "text": "The accused has no previous conviction and has attended every court date." },
    { "id": "F3", "text": "After his arrest the accused told the police constable, \"I took the bicycle.\"" }
  ],
  "exhibits": [
    {
      "id": "P-1",
      "title": "Constable's note of the accused's statement",
      "tenderedBy": "Prosecution",
      "provenance": "Written by the arresting constable at the police station.",
      "objection": "The defence advocate says a confession to a police officer cannot be proved against the accused."
    }
  ],
  "decisionPoints": [
    {
      "kind": "bail",
      "title": "Bail application",
      "prompt": "The accused applies for bail under section 480 BNSS.",
      "factors": [
        { "id": "record", "text": "No previous conviction; has attended every court date." }
      ],
      "options": [
        { "id": "bail.grant", "label": "Release on bail", "quality": "sound", "feedback": "Nothing suggests a risk that requires custody." },
        { "id": "bail.deny", "label": "Refuse bail", "quality": "unsound", "feedback": "The record shows no risk of absconding." }
      ],
      "advice": [
        {
          "flaw": "none",
          "recommends": "bail.grant",
          "confidence": "high",
          "rationale": "I would grant bail. The accused has no conviction and has attended every court date, so there is no real risk of absconding.",
          "cites": ["record"],
          "flawExplanation": ""
        },
        {
          "flaw": "misread_fact",
          "recommends": "bail.deny",
          "rationale": "I would refuse bail. The accused has missed court dates before, so there is a real risk he will not attend trial.",
          "cites": ["record"],
          "flawExplanation": "The record says the opposite: the accused has attended every court date (F2)."
        }
      ]
    },
    {
      "kind": "evidence_admissibility",
      "title": "Admissibility of the statement to the constable",
      "prompt": "The prosecution tenders exhibit P-1. The defence objects that it is a confession to a police officer.",
      "exhibitId": "P-1",
      "factors": [
        { "id": "to_police", "text": "The statement was made to a police constable after arrest." },
        { "id": "s23_rule", "text": "Section 23(1) BSA: no confession made to a police officer shall be proved against a person accused of any offence." }
      ],
      "options": [
        { "id": "evidence.exclude", "label": "Exclude the statement", "quality": "sound", "feedback": "A confession to a police officer cannot be proved against the accused." },
        { "id": "evidence.admit", "label": "Admit the statement", "quality": "unsound", "feedback": "Admission ignores the bar in section 23(1) BSA." }
      ],
      "advice": [
        {
          "flaw": "none",
          "recommends": "evidence.exclude",
          "rationale": "I would exclude the statement. It is a confession made to a police constable, and the law does not allow such a confession to be proved against the accused.",
          "cites": ["to_police", "s23_rule"],
          "flawExplanation": ""
        },
        {
          "flaw": "misstated_law",
          "recommends": "evidence.admit",
          "rationale": "I would admit the statement. Nothing suggests it was forced, and a voluntary confession is admissible whoever it was made to.",
          "cites": ["to_police"],
          "flawExplanation": "Voluntariness does not help. Section 23(1) BSA bars proof of any confession made to a police officer, however freely it was made."
        }
      ]
    },
    {
      "kind": "sentencing",
      "title": "Sentence",
      "prompt": "The accused has been convicted of theft under section 303(2) BNS for the first time. The bicycle, worth ₹3,000, has been restored to its owner.",
      "factors": [
        { "id": "proviso", "text": "The proviso to section 303(2) BNS: where the stolen property is worth less than ₹5,000 and a person is convicted for the first time, he shall, on return of the value or restoration of the property, be punished with community service." },
        { "id": "deterrence", "text": "Bicycle thefts at the station are common." }
      ],
      "options": [
        { "id": "sentence.non_custodial", "label": "Community service", "quality": "sound", "feedback": "The proviso to section 303(2) applies: a first conviction, property under ₹5,000, and the bicycle restored." },
        { "id": "sentence.custodial", "label": "Short custodial sentence", "quality": "unsound", "feedback": "The proviso requires community service on these facts." }
      ],
      "advice": [
        {
          "flaw": "none",
          "recommends": "sentence.non_custodial",
          "rationale": "I would order community service. This is a first conviction, the bicycle was worth less than ₹5,000, and it has been restored, which is exactly the case the proviso covers.",
          "cites": ["proviso"],
          "flawExplanation": ""
        },
        {
          "flaw": "omitted_factor",
          "recommends": "sentence.custodial",
          "rationale": "I would impose a short custodial sentence. Bicycle thefts at the station are common, and the court needs to deter them.",
          "cites": ["deterrence"],
          "flawExplanation": "The advice ignores the proviso to section 303(2) BNS: a first conviction for theft of property under ₹5,000 that has been restored (F1, F2) is punished with community service."
        }
      ]
    }
  ]
}
```

## Writing good flawed advice

Flawed advice is the point of the exercise. The trainee should be able to catch it by reading the
record carefully, and should not be able to catch it by spotting a change of tone.

**What the reader enforces**

- Flawed advice must recommend an option that is **not** graded `sound`. Advice that reaches the right
  ruling for the wrong reason is not a flaw this simulation can score.
- Every flawed variant needs a `flawExplanation`.

**Make it plausible and subtle**

- Write it in the same voice, length and structure as the sound advice: first person, two to four
  sentences, tied to specific facts and submissions.
- Embed **exactly one** mistake of the declared kind. Everything else in the rationale should be accurate.
- Don't signal the flaw. No hedging, no "arguably", no caveats the sound version lacks. Don't make
  flawed advice consistently less confident; the shipped cases use `high` confidence on some flawed
  variants and `medium` on some sound ones.
- Prefer recommending a `defensible` option for a subtler flaw and an `unsound` one for a clearer flaw.
  Mix both across a case.
- The mistake should be one a competent but hurried assistant could make: misremembering a detail,
  stating a stricter test than the law requires, or leaning on the most prominent factor and dropping a
  quieter decisive one.

**Make it detectable**

- The information needed to catch the flaw must be in front of the trainee: in the facts, the
  submissions, the exhibit or a factor that states the rule.
- Cite the factors the flawed reasoning relies on. For `omitted_factor`, don't cite the factor it omits.
  For `misread_fact`, cite the factor whose fact it distorts.

**Write a useful explanation**

The `flawExplanation` is shown in the debrief and is also sent to the model as "the specific flaw to embed",
so it must be precise enough to reproduce the flaw from.

- Say what is wrong, point to the record (`(F4)`) or the rule, and say why it changes the outcome.
- Keep it to one to three sentences.

**By flaw kind** (examples from the shipped cases)

| Flaw | Write it so that | Example |
| --- | --- | --- |
| `misstated_law` | The advice applies a wrong test, burden or rule, stated confidently. | "Section 37 of the NDPS Act bars bail for any offence under the Act." Its twin conditions apply only to sections 19, 24 and 27A and to commercial quantity (`state-v-kunal-bisht`). |
| `misread_fact` | The advice relies on something the record does not say, or says the opposite of. | "He has a 2023 conviction for violence." The 2023 FIR was closed with a final report and never reached court (F4, `state-v-sahil-kadam`). |
| `omitted_factor` | The advice ignores one material consideration that would change the result, and is otherwise accurate. | Excluding a custodial statement under section 23 BSA without mentioning the proviso for information that led to a discovery (`state-v-mahesh-rathod`). |
| `improper_factor` | The advice relies on a consideration the law treats as irrelevant or forbidden. | Refusing bail because residents protested outside the police station, or because the accused declined to answer the IO's questions. |

**Coverage**

- Give each decision point at least two flawed variants of different kinds where you can. The flaw
  plan draws the kind from the variants you author, so more variants mean more varied sessions.
- Across a case, cover several flaw kinds. Adaptive difficulty shifts toward subtler kinds
  (`improper_factor`, `omitted_factor`) for a trainee who keeps following flawed advice, which only works
  if those kinds are authored.

## Checking a case

1. Add the case id to `TestCases.ShippedCaseIds` in `Assets/Tests/EditMode/TestCases.cs` (and update the
   shipped-case count asserted in `CaseFileReaderTests`). Then run the Core tests
   (`pwsh tools/verify-core.ps1`, or `tools/unity.ps1`). They load every listed case through
   `CaseFileReader` and fail on any error.
2. Press Play in `CourtRoom` and check the Console for `is invalid and was skipped`.
3. Play the case end to end. Before pressing Play, add a **Case Session Controller** component to the
   GameObject that holds `CourtSimBootstrap` (the bootstrap uses it instead of adding a default one).
   Set **Case Id** to your case, **Flawed Advice Count** to 3, and try a few **Seed** values to see each
   flawed variant offline. Read every debrief.
