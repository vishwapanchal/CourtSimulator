using System;
using System.Collections.Generic;
using CourtSim.Core.Advising;
using CourtSim.Core.Decisions;
using CourtSim.Core.Json;

namespace CourtSim.Core.Cases
{
    /// <summary>The outcome of reading a case file: the case, or every problem found.</summary>
    public sealed class CaseReadResult
    {
        internal CaseReadResult(CaseFile caseFile, IReadOnlyList<string> errors)
        {
            Case = caseFile;
            Errors = errors;
        }

        /// <summary>The parsed case, or null when <see cref="Errors"/> is non-empty.</summary>
        public CaseFile Case { get; }

        /// <summary>Problems, each prefixed with a JSON path such as <c>decisionPoints[1].options[0].id</c>.</summary>
        public IReadOnlyList<string> Errors { get; }

        public bool Success => Case != null;
    }

    /// <summary>
    /// Parses and validates <c>courtsim.case/1</c> JSON (format: docs/CASE_FORMAT.md). Validation is strict
    /// because cases are ground truth for scoring: a case that loads is guaranteed to be playable and gradeable.
    /// </summary>
    public static class CaseFileReader
    {
        /// <summary>Decision points must appear in exactly this order.</summary>
        public static readonly IReadOnlyList<DecisionPointKind> RequiredOrder = new[]
        {
            DecisionPointKind.Bail,
            DecisionPointKind.EvidenceAdmissibility,
            DecisionPointKind.Sentencing,
        };

        public static CaseReadResult Read(string json)
        {
            var errors = new List<string>();
            if (!JsonReader.TryParse(json, out JsonValue root, out string parseError))
            {
                errors.Add("Invalid JSON: " + parseError);
                return new CaseReadResult(null, errors);
            }

            var reader = new Reader(errors);
            CaseFile caseFile = reader.ReadCase(root);
            return new CaseReadResult(errors.Count == 0 ? caseFile : null, errors);
        }

        public static string KindToWireName(DecisionPointKind kind)
        {
            switch (kind)
            {
                case DecisionPointKind.Bail:
                    return "bail";
                case DecisionPointKind.EvidenceAdmissibility:
                    return "evidence_admissibility";
                case DecisionPointKind.Sentencing:
                    return "sentencing";
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown decision point kind.");
            }
        }

        public static bool TryParseKind(string wireName, out DecisionPointKind kind)
        {
            foreach (DecisionPointKind candidate in RequiredOrder)
            {
                if (KindToWireName(candidate) == wireName)
                {
                    kind = candidate;
                    return true;
                }
            }

            kind = default;
            return false;
        }

        public static bool TryParseQuality(string wireName, out OptionQuality quality)
        {
            switch (wireName)
            {
                case "sound":
                    quality = OptionQuality.Sound;
                    return true;
                case "defensible":
                    quality = OptionQuality.Defensible;
                    return true;
                case "unsound":
                    quality = OptionQuality.Unsound;
                    return true;
                default:
                    quality = OptionQuality.Unsound;
                    return false;
            }
        }

        private sealed class Reader
        {
            private readonly List<string> _errors;

            public Reader(List<string> errors)
            {
                _errors = errors;
            }

            public CaseFile ReadCase(JsonValue root)
            {
                if (root.Kind != JsonKind.Object)
                {
                    Error("$", "must be a JSON object");
                    return null;
                }

                string schema = root.GetString("schema");
                if (schema != CaseFile.SchemaId)
                {
                    Error("schema", $"must be \"{CaseFile.SchemaId}\"");
                }

                string id = RequiredString(root, "id", "id");
                string title = RequiredString(root, "title", "title");
                int difficulty = (int)(root.GetNumber("difficulty") ?? 0);
                if (difficulty < 1 || difficulty > 3 || root.GetNumber("difficulty") != difficulty)
                {
                    Error("difficulty", "must be an integer from 1 to 3");
                }

                Defendant defendant = ReadDefendant(root.Get("defendant"));
                List<string> charges = ReadStrings(root.Get("charges"), "charges", true);
                List<CaseFact> facts = ReadFacts(root.Get("facts"));
                List<Exhibit> exhibits = ReadExhibits(root.Get("exhibits"));
                List<CaseDecisionPoint> points = ReadDecisionPoints(root.Get("decisionPoints"), exhibits);

                return new CaseFile(
                    id,
                    title,
                    root.GetString("summary"),
                    difficulty,
                    root.GetString("jurisdictionNote"),
                    defendant,
                    charges,
                    facts,
                    exhibits,
                    points);
            }

            private Defendant ReadDefendant(JsonValue value)
            {
                if (!RequireObject(value, "defendant"))
                {
                    return null;
                }

                string name = RequiredString(value, "name", "defendant.name");
                double? age = value.GetNumber("age");
                if (age == null || age < 10 || age > 120 || age != Math.Floor(age.Value))
                {
                    Error("defendant.age", "must be a whole number of years");
                }

                return new Defendant(name, (int)(age ?? 0), value.GetString("background"));
            }

            private List<CaseFact> ReadFacts(JsonValue value)
            {
                var facts = new List<CaseFact>();
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach ((JsonValue item, string path) in Elements(value, "facts", true))
                {
                    string id = RequiredString(item, "id", path + ".id");
                    string text = RequiredString(item, "text", path + ".text");
                    if (id != null && !ids.Add(id))
                    {
                        Error(path + ".id", $"duplicates fact id '{id}'");
                    }

                    facts.Add(new CaseFact(id, text));
                }

                return facts;
            }

            private List<Exhibit> ReadExhibits(JsonValue value)
            {
                var exhibits = new List<Exhibit>();
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach ((JsonValue item, string path) in Elements(value, "exhibits", false))
                {
                    string id = RequiredString(item, "id", path + ".id");
                    string title = RequiredString(item, "title", path + ".title");
                    if (id != null && !ids.Add(id))
                    {
                        Error(path + ".id", $"duplicates exhibit id '{id}'");
                    }

                    exhibits.Add(new Exhibit(
                        id,
                        title,
                        item.GetString("description"),
                        item.GetString("tenderedBy"),
                        item.GetString("provenance"),
                        item.GetString("objection")));
                }

                return exhibits;
            }

            private List<CaseDecisionPoint> ReadDecisionPoints(JsonValue value, List<Exhibit> exhibits)
            {
                var points = new List<CaseDecisionPoint>();
                List<(JsonValue Item, string Path)> elements = Elements(value, "decisionPoints", true);
                if (elements.Count != RequiredOrder.Count && value != null && value.Kind == JsonKind.Array)
                {
                    Error("decisionPoints", $"must contain exactly {RequiredOrder.Count} decision points (bail, evidence_admissibility, sentencing)");
                }

                for (int i = 0; i < elements.Count; i++)
                {
                    CaseDecisionPoint point = ReadDecisionPoint(elements[i].Item, elements[i].Path, i, exhibits);
                    if (point != null)
                    {
                        points.Add(point);
                    }
                }

                return points;
            }

            private CaseDecisionPoint ReadDecisionPoint(JsonValue item, string path, int index, List<Exhibit> exhibits)
            {
                string kindName = RequiredString(item, "kind", path + ".kind");
                bool kindOk = TryParseKind(kindName, out DecisionPointKind kind);
                if (kindName != null && !kindOk)
                {
                    Error(path + ".kind", $"'{kindName}' is not one of bail, evidence_admissibility, sentencing");
                }
                else if (kindOk && index < RequiredOrder.Count && kind != RequiredOrder[index])
                {
                    Error(path + ".kind", $"must be '{KindToWireName(RequiredOrder[index])}' (decision points run bail, evidence_admissibility, sentencing)");
                }

                string title = RequiredString(item, "title", path + ".title");
                string prompt = RequiredString(item, "prompt", path + ".prompt");

                string exhibitId = item.GetString("exhibitId");
                if (exhibitId != null && !exhibits.Exists(e => e.Id == exhibitId))
                {
                    Error(path + ".exhibitId", $"refers to unknown exhibit '{exhibitId}'");
                }

                if (kindOk && kind == DecisionPointKind.EvidenceAdmissibility && exhibitId == null)
                {
                    Error(path + ".exhibitId", "is required for an evidence_admissibility decision point");
                }

                List<CaseFactor> factors = ReadFactors(item.Get("factors"), path + ".factors");
                var options = new List<DecisionOption>();
                var assessments = new List<OptionAssessment>();
                ReadOptions(item.Get("options"), path + ".options", options, assessments);
                List<AdviceScript> advice = ReadAdvice(item.Get("advice"), path + ".advice", assessments, factors);

                JsonValue submissions = item.Get("submissions");
                if (!kindOk || title == null || options.Count < 2)
                {
                    return null;
                }

                return new CaseDecisionPoint(
                    new DecisionPointDefinition(kind, title, prompt ?? string.Empty, options),
                    submissions?.GetString("prosecution"),
                    submissions?.GetString("defence"),
                    exhibitId,
                    factors,
                    assessments,
                    advice);
            }

            private List<CaseFactor> ReadFactors(JsonValue value, string basePath)
            {
                var factors = new List<CaseFactor>();
                foreach ((JsonValue item, string path) in Elements(value, basePath, true))
                {
                    string id = RequiredString(item, "id", path + ".id");
                    string text = RequiredString(item, "text", path + ".text");
                    if (id != null && factors.Exists(f => f.Id == id))
                    {
                        Error(path + ".id", $"duplicates factor id '{id}'");
                    }

                    factors.Add(new CaseFactor(id, text));
                }

                return factors;
            }

            private void ReadOptions(JsonValue value, string basePath, List<DecisionOption> options, List<OptionAssessment> assessments)
            {
                List<(JsonValue Item, string Path)> elements = Elements(value, basePath, true);
                if (value != null && value.Kind == JsonKind.Array && elements.Count < 2)
                {
                    Error(basePath, "must contain at least two options");
                }

                foreach ((JsonValue item, string path) in elements)
                {
                    string id = RequiredString(item, "id", path + ".id");
                    string label = RequiredString(item, "label", path + ".label");
                    string qualityName = RequiredString(item, "quality", path + ".quality");
                    bool qualityOk = TryParseQuality(qualityName, out OptionQuality quality);
                    if (qualityName != null && !qualityOk)
                    {
                        Error(path + ".quality", $"'{qualityName}' is not one of sound, defensible, unsound");
                    }

                    if (id == null || label == null)
                    {
                        continue;
                    }

                    if (options.Exists(o => o.Id == id))
                    {
                        Error(path + ".id", $"duplicates option id '{id}'");
                        continue;
                    }

                    options.Add(new DecisionOption(id, label));
                    if (qualityOk)
                    {
                        assessments.Add(new OptionAssessment(id, quality, item.GetString("feedback")));
                    }
                }

                if (options.Count >= 2 && !assessments.Exists(a => a.Quality == OptionQuality.Sound))
                {
                    Error(basePath, "must include at least one option with quality 'sound'");
                }
            }

            private List<AdviceScript> ReadAdvice(JsonValue value, string basePath, List<OptionAssessment> assessments, List<CaseFactor> factors)
            {
                var scripts = new List<AdviceScript>();
                foreach ((JsonValue item, string path) in Elements(value, basePath, true))
                {
                    string flawName = RequiredString(item, "flaw", path + ".flaw");
                    bool flawOk = FlawKinds.TryParse(flawName, out FlawKind flaw);
                    if (flawName != null && !flawOk)
                    {
                        Error(path + ".flaw", $"'{flawName}' is not a known flaw kind");
                    }

                    string recommends = RequiredString(item, "recommends", path + ".recommends");
                    OptionAssessment target = assessments.Find(a => a.OptionId == recommends);
                    if (recommends != null && target == null)
                    {
                        Error(path + ".recommends", $"refers to unknown option '{recommends}'");
                    }
                    else if (target != null && flawOk)
                    {
                        if (flaw == FlawKind.None && target.Quality != OptionQuality.Sound)
                        {
                            Error(path + ".recommends", "sound advice (flaw 'none') must recommend an option graded 'sound'");
                        }
                        else if (flaw != FlawKind.None && target.Quality == OptionQuality.Sound)
                        {
                            Error(path + ".recommends", "flawed advice must recommend an option that is not graded 'sound'");
                        }
                    }

                    string confidenceName = item.GetString("confidence", "medium");
                    if (!AdviceConfidences.TryParse(confidenceName, out AdviceConfidence confidence))
                    {
                        Error(path + ".confidence", $"'{confidenceName}' is not one of low, medium, high");
                    }

                    string rationale = RequiredString(item, "rationale", path + ".rationale");
                    List<string> cites = ReadStrings(item.Get("cites"), path + ".cites", false);
                    foreach (string cite in cites)
                    {
                        if (!factors.Exists(f => f.Id == cite))
                        {
                            Error(path + ".cites", $"refers to unknown factor '{cite}'");
                        }
                    }

                    string explanation = item.GetString("flawExplanation");
                    if (flawOk && flaw != FlawKind.None && string.IsNullOrWhiteSpace(explanation))
                    {
                        Error(path + ".flawExplanation", "is required for flawed advice");
                    }

                    if (flawOk && scripts.Exists(s => s.Flaw == flaw))
                    {
                        Error(path + ".flaw", $"duplicates advice for flaw '{flawName}'");
                    }

                    if (flawOk && recommends != null)
                    {
                        scripts.Add(new AdviceScript(flaw, recommends, confidence, rationale, cites, explanation));
                    }
                }

                if (value != null && value.Kind == JsonKind.Array)
                {
                    if (!scripts.Exists(s => s.Flaw == FlawKind.None))
                    {
                        Error(basePath, "must include sound advice (flaw 'none')");
                    }

                    if (!scripts.Exists(s => s.Flaw != FlawKind.None))
                    {
                        Error(basePath, "must include at least one flawed variant");
                    }
                }

                return scripts;
            }

            private List<string> ReadStrings(JsonValue value, string basePath, bool required)
            {
                var strings = new List<string>();
                foreach ((JsonValue item, string path) in Elements(value, basePath, required))
                {
                    string text = item.AsString();
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        Error(path, "must be a non-empty string");
                    }
                    else
                    {
                        strings.Add(text);
                    }
                }

                return strings;
            }

            private List<(JsonValue Item, string Path)> Elements(JsonValue value, string path, bool required)
            {
                var elements = new List<(JsonValue, string)>();
                if (value == null || value.IsNull)
                {
                    if (required)
                    {
                        Error(path, "is required");
                    }

                    return elements;
                }

                if (value.Kind != JsonKind.Array)
                {
                    Error(path, "must be an array");
                    return elements;
                }

                if (required && value.Items.Count == 0)
                {
                    Error(path, "must not be empty");
                }

                for (int i = 0; i < value.Items.Count; i++)
                {
                    elements.Add((value.Items[i], $"{path}[{i}]"));
                }

                return elements;
            }

            private bool RequireObject(JsonValue value, string path)
            {
                if (value == null || value.Kind != JsonKind.Object)
                {
                    Error(path, "must be an object");
                    return false;
                }

                return true;
            }

            private string RequiredString(JsonValue owner, string key, string path)
            {
                if (owner == null || owner.Kind != JsonKind.Object)
                {
                    Error(path.Substring(0, Math.Max(0, path.LastIndexOf('.'))), "must be an object");
                    return null;
                }

                string text = owner.GetString(key);
                if (string.IsNullOrWhiteSpace(text))
                {
                    Error(path, "is required and must be a non-empty string");
                    return null;
                }

                return text;
            }

            private void Error(string path, string message)
            {
                string error = $"{(path.Length == 0 ? "$" : path)}: {message}";
                if (!_errors.Contains(error))
                {
                    _errors.Add(error);
                }
            }
        }
    }
}
