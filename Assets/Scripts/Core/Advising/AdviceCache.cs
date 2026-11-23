using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CourtSim.Core.Cases;
using CourtSim.Core.Json;

namespace CourtSim.Core.Advising
{
    /// <summary>
    /// Somewhere to keep generated advice between runs. Unity stores one JSON file per entry under
    /// <c>persistentDataPath/advice-cache</c>; tests use a dictionary. Implementations must not throw:
    /// a cache that can't be read or written simply behaves as empty.
    /// </summary>
    public interface IAdviceStore
    {
        /// <summary>The stored text for <paramref name="key"/>, or null if there is none.</summary>
        string Load(string key);

        void Save(string key, string json);
    }

    /// <summary>
    /// Builds the cache key for one piece of generated advice. Two requests share a key only when the model
    /// would be sent the same prompt for them, so a hit can be shown instead of asking the model again.
    /// </summary>
    public static class AdviceCacheKey
    {
        /// <summary>
        /// Bump this whenever the prompt, schema or validation rules change in a way the fingerprint below
        /// can't see. Every older cache entry then stops matching and is regenerated.
        /// </summary>
        public const int PromptVersion = 1;

        /// <summary>
        /// The key: model, case, decision point, flaw and detail in readable form, plus a short fingerprint of
        /// the exact prompt. The fingerprint means editing a case file automatically invalidates its cache.
        /// The key only uses letters, digits, '.', '_' and '-', so it is also a safe file name.
        /// </summary>
        /// <remarks>
        /// Earlier rulings are deliberately left out. They only colour the wording; the recommended ruling and
        /// the flaw come from the case file. Ignoring them is what lets advice be prepared before the hearing.
        /// </remarks>
        public static string For(string model, AdviceRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            AdviceScript target = request.TargetScript;
            FlawKind flaw = target != null ? target.Flaw : request.Flaw;
            string detail = request.Detail == ExplanationDetail.Detailed ? "detailed" : "standard";
            string readable = string.Join(
                "_",
                "v" + PromptVersion.ToString(CultureInfo.InvariantCulture),
                Clean(string.IsNullOrWhiteSpace(model) ? "unknown-model" : model),
                Clean(request.Case.Id),
                "p" + request.PointIndex.ToString(CultureInfo.InvariantCulture) + "-" + Clean(request.Point.Kind.ToString().ToLowerInvariant()),
                FlawKinds.ToWireName(flaw),
                detail);
            return readable + "_" + Fingerprint(request);
        }

        /// <summary>FNV-1a hash (8 hex digits) of the system prompt and the brief without earlier rulings.</summary>
        public static string Fingerprint(AdviceRequest request)
        {
            var withoutHistory = new AdviceRequest(request.Case, request.PointIndex, request.Flaw, null, request.Detail);
            string prompt = AdvicePromptBuilder.SystemPrompt + "\n" + AdvicePromptBuilder.BuildBrief(withoutHistory);

            // FNV-1a: a tiny, well-known hash that gives the same answer on every machine and .NET version
            // (string.GetHashCode does not).
            uint hash = 2166136261;
            foreach (char c in prompt)
            {
                hash ^= c;
                hash = unchecked(hash * 16777619);
            }

            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }

        // Model tags such as "qwen3.5:9b" contain ':' which Windows forbids in file names.
        private static string Clean(string text)
        {
            var clean = new StringBuilder(text.Length);
            foreach (char c in text.Trim().ToLowerInvariant())
            {
                bool safe = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '-';
                clean.Append(safe ? c : '-');
            }

            return clean.ToString();
        }
    }

    /// <summary>Turns generated advice into the JSON kept in the cache, and back.</summary>
    public static class AdviceCacheEntry
    {
        public const string Schema = "courtsim.advice-cache/1";

        public static string ToJson(string key, Advice advice, DateTime savedUtc)
        {
            if (advice == null)
            {
                throw new ArgumentNullException(nameof(advice));
            }

            return JsonValue.NewObject()
                .Set("schema", Schema)
                .Set("key", key)
                .Set("savedUtc", savedUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))
                .Set("source", SourceName(advice.Source))
                .Set("recommendedOptionId", advice.RecommendedOptionId)
                .Set("rationale", advice.Rationale)
                .Set("citedFactorIds", JsonValue.StringArray(advice.CitedFactorIds))
                .Set("confidence", AdviceConfidences.ToWireName(advice.Confidence))
                .Set("flaw", FlawKinds.ToWireName(advice.Flaw))
                .Set("flawExplanation", advice.FlawExplanation)
                .ToJson();
        }

        /// <summary>
        /// Reads a cached entry for <paramref name="request"/>. Returns false (and the caller asks the model
        /// again) if the text is damaged, belongs to another key, or no longer fits the case file.
        /// </summary>
        public static bool TryRead(string json, string expectedKey, AdviceRequest request, out Advice advice)
        {
            advice = null;
            if (string.IsNullOrWhiteSpace(json) || request == null
                || !JsonReader.TryParse(json, out JsonValue root, out _) || root.Kind != JsonKind.Object
                || root.GetString("schema") != Schema || root.GetString("key") != expectedKey)
            {
                return false;
            }

            AdviceScript target = request.TargetScript;
            string rationale = root.GetString("rationale");
            if (target == null || string.IsNullOrWhiteSpace(rationale)
                || root.GetString("recommendedOptionId") != target.RecommendedOptionId
                || !FlawKinds.TryParse(root.GetString("flaw"), out FlawKind flaw) || flaw != target.Flaw
                || !TryParseSource(root.GetString("source"), out AdviceSource source))
            {
                return false;
            }

            var cited = new List<string>();
            JsonValue citedJson = root.Get("citedFactorIds");
            if (citedJson != null)
            {
                foreach (JsonValue item in citedJson.Items)
                {
                    string id = item.AsString();
                    if (id != null && request.Point.FindFactor(id) != null && !cited.Contains(id))
                    {
                        cited.Add(id);
                    }
                }
            }

            if (!AdviceConfidences.TryParse(root.GetString("confidence"), out AdviceConfidence confidence))
            {
                confidence = target.Confidence;
            }

            advice = new Advice(
                request.Point.Kind,
                target.RecommendedOptionId,
                rationale.Trim(),
                cited,
                confidence,
                flaw,
                root.GetString("flawExplanation", string.Empty),
                source);
            return true;
        }

        private static string SourceName(AdviceSource source)
        {
            switch (source)
            {
                case AdviceSource.Claude:
                    return "claude";
                case AdviceSource.LocalModel:
                    return "local";
                default:
                    return "scripted";
            }
        }

        private static bool TryParseSource(string name, out AdviceSource source)
        {
            switch (name)
            {
                case "claude":
                    source = AdviceSource.Claude;
                    return true;
                case "local":
                    source = AdviceSource.LocalModel;
                    return true;
                case "scripted":
                    source = AdviceSource.Scripted;
                    return true;
                default:
                    source = AdviceSource.Scripted;
                    return false;
            }
        }
    }
}
