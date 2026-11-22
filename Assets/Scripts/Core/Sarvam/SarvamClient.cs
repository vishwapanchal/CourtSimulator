using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CourtSim.Core.Http;
using CourtSim.Core.Json;

namespace CourtSim.Core.Sarvam
{
    /// <summary>Settings for Sarvam AI's translate and text-to-speech endpoints.</summary>
    public sealed class SarvamClientOptions
    {
        public const string DefaultBaseUrl = "https://api.sarvam.ai";

        /// <summary>Sarvam rejects translate inputs longer than this.</summary>
        public const int MaxTranslateChars = 1000;

        /// <summary>Sarvam rejects text-to-speech inputs longer than this.</summary>
        public const int MaxSpeechChars = 2500;

        public string BaseUrl { get; set; } = DefaultBaseUrl;

        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

        public string TranslateModel { get; set; } = "mayura:v1";

        public string SpeechModel { get; set; } = "bulbul:v3";

        /// <summary>The language the game's lines are written in.</summary>
        public string SourceLanguage { get; set; } = "en-IN";

        /// <summary>The spoken language: Kannada.</summary>
        public string TargetLanguage { get; set; } = "kn-IN";

        /// <summary>"code-mixed" keeps everyday English words (case, bail, phones) inside Kannada sentences,
        /// the way people in Karnataka courts actually talk.</summary>
        public string Mode { get; set; } = "code-mixed";

        /// <summary>Speaking speed; 1.0 is normal.</summary>
        public double Pace { get; set; } = 1.0;

        /// <summary>Samples per second of the returned audio.</summary>
        public int SampleRate { get; set; } = 22050;
    }

    /// <summary>Raised when Sarvam can't be reached or refuses a request. Messages never contain the API key.</summary>
    public sealed class SarvamException : Exception
    {
        public SarvamException(string message, int statusCode = 0) : base(message)
        {
            StatusCode = statusCode;
        }

        /// <summary>The HTTP status, or 0 when no response arrived.</summary>
        public int StatusCode { get; }

        /// <summary>True when the key itself was refused, so retrying is pointless.</summary>
        public bool IsKeyRejected => StatusCode == 401 || StatusCode == 403;
    }

    /// <summary>
    /// Minimal client for two Sarvam AI endpoints: <c>/translate</c> (English to Kannada-English code-mixed text)
    /// and <c>/text-to-speech</c> (text to a WAV file). Long text is split at sentence ends to stay within
    /// Sarvam's length limits. The key is sent only in the <c>api-subscription-key</c> header.
    /// </summary>
    public sealed class SarvamClient
    {
        public const string KeyHeader = "api-subscription-key";

        private readonly SarvamClientOptions _options;
        private readonly string _apiKey;
        private readonly IHttpTransport _transport;

        public SarvamClient(SarvamClientOptions options, string apiKey, IHttpTransport transport)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("A Sarvam API key is required.", nameof(apiKey));
            }

            _apiKey = apiKey.Trim();
        }

        public SarvamClientOptions Options => _options;

        public string TranslateEndpoint => _options.BaseUrl.TrimEnd('/') + "/translate";

        public string SpeechEndpoint => _options.BaseUrl.TrimEnd('/') + "/text-to-speech";

        /// <summary>
        /// Splits text into pieces of at most <paramref name="maxChars"/> characters, cutting after a sentence end
        /// (". ", "! ", "? ", the Kannada/Hindi danda "।" or a line break) where possible, else at a space, else
        /// hard at the limit. Returns no pieces for blank text.
        /// </summary>
        public static List<string> Split(string text, int maxChars)
        {
            if (maxChars < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(maxChars), "The limit must be at least 2 characters.");
            }

            var pieces = new List<string>();
            string rest = (text ?? string.Empty).Trim();
            while (rest.Length > maxChars)
            {
                int cut = FindCut(rest, maxChars);
                pieces.Add(rest.Substring(0, cut).Trim());
                rest = rest.Substring(cut).TrimStart();
            }

            if (rest.Length > 0)
            {
                pieces.Add(rest);
            }

            return pieces;
        }

        /// <summary>
        /// A file name for the cached audio of one line: the SHA-1 of everything that changes the sound (role,
        /// speaker, models, language, pace, text), as 40 hex digits. Same inputs, same name, so a repeat is a cache hit.
        /// </summary>
        public string CacheKey(SarvamVoiceRole role, SarvamVoice voice, string englishText)
        {
            if (voice == null)
            {
                throw new ArgumentNullException(nameof(voice));
            }

            string identity = string.Join(
                "|",
                role.ToString(),
                voice.Speaker,
                voice.Gender,
                _options.TranslateModel,
                _options.SpeechModel,
                _options.TargetLanguage,
                _options.Mode,
                _options.Pace.ToString("0.###", CultureInfo.InvariantCulture),
                _options.SampleRate.ToString(CultureInfo.InvariantCulture),
                englishText ?? string.Empty);

            using (SHA1 sha = SHA1.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(identity));
                var hex = new StringBuilder(digest.Length * 2);
                foreach (byte b in digest)
                {
                    hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }

                return hex.ToString();
            }
        }

        /// <summary>The JSON body for one translate request.</summary>
        public JsonValue BuildTranslateBody(string text, bool female)
        {
            return JsonValue.NewObject()
                .Set("input", text ?? string.Empty)
                .Set("source_language_code", _options.SourceLanguage)
                .Set("target_language_code", _options.TargetLanguage)
                .Set("model", _options.TranslateModel)
                .Set("mode", _options.Mode)
                .Set("speaker_gender", female ? "Female" : "Male");
        }

        /// <summary>The JSON body for one text-to-speech request.</summary>
        public JsonValue BuildSpeechBody(string text, string speaker)
        {
            return JsonValue.NewObject()
                .Set("text", text ?? string.Empty)
                .Set("language_code", _options.TargetLanguage)
                .Set("model", _options.SpeechModel)
                .Set("speaker", speaker ?? string.Empty)
                .Set("pace", _options.Pace)
                .Set("speech_sample_rate", _options.SampleRate);
        }

        /// <summary>
        /// Translates English into Kannada-English code-mixed text. <paramref name="female"/> picks the verb forms
        /// a woman would use when speaking about herself. Long text is translated in pieces and joined with spaces.
        /// </summary>
        public async Task<string> TranslateAsync(string englishText, bool female, CancellationToken cancellationToken)
        {
            var translated = new List<string>();
            foreach (string piece in Split(englishText, SarvamClientOptions.MaxTranslateChars))
            {
                JsonValue root = await PostAsync(TranslateEndpoint, BuildTranslateBody(piece, female), cancellationToken);
                string text = root.GetString("translated_text");
                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new SarvamException("Sarvam returned an empty translation.", 200);
                }

                translated.Add(text.Trim());
            }

            return string.Join(" ", translated);
        }

        /// <summary>
        /// Turns text into speech with the given bulbul speaker. Returns one WAV file per piece, in order
        /// (join them with <see cref="Presentation.WavWriter.Concatenate"/>). Blank text returns an empty list.
        /// </summary>
        public async Task<IReadOnlyList<byte[]>> SpeakAsync(string text, string speaker, CancellationToken cancellationToken)
        {
            var wavs = new List<byte[]>();
            foreach (string piece in Split(text, SarvamClientOptions.MaxSpeechChars))
            {
                JsonValue root = await PostAsync(SpeechEndpoint, BuildSpeechBody(piece, speaker), cancellationToken);
                JsonValue audios = root.Get("audios");
                if (audios == null || audios.Kind != JsonKind.Array || audios.Items.Count == 0)
                {
                    throw new SarvamException("Sarvam returned no audio.", 200);
                }

                foreach (JsonValue audio in audios.Items)
                {
                    wavs.Add(DecodeAudio(audio.AsString()));
                }
            }

            return wavs;
        }

        /// <summary>Turns one base64 "audios" entry back into WAV bytes.</summary>
        public static byte[] DecodeAudio(string base64)
        {
            if (string.IsNullOrWhiteSpace(base64))
            {
                throw new SarvamException("Sarvam returned an empty audio clip.", 200);
            }

            try
            {
                return Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                throw new SarvamException("Sarvam returned audio that is not valid base64.", 200);
            }
        }

        private static int FindCut(string text, int maxChars)
        {
            // 1. The last sentence end that fits, followed by a space (or the end of the text).
            for (int i = maxChars - 1; i > 0; i--)
            {
                char c = text[i];
                bool sentenceEnd = c == '.' || c == '!' || c == '?' || c == '।';
                if (c == '\n' || (sentenceEnd && char.IsWhiteSpace(text[i + 1])))
                {
                    return i + 1;
                }
            }

            // 2. Otherwise the last space that fits, so no word is broken.
            for (int i = maxChars; i > 0; i--)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    return i;
                }
            }

            // 3. One enormous word: cut at the limit, but never between the two halves of an emoji or rare character.
            return char.IsHighSurrogate(text[maxChars - 1]) ? maxChars - 1 : maxChars;
        }

        private async Task<JsonValue> PostAsync(string url, JsonValue body, CancellationToken cancellationToken)
        {
            var headers = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("content-type", "application/json"),
                new KeyValuePair<string, string>(KeyHeader, _apiKey),
            };
            var request = new HttpRequestData(url, headers, body.ToJson(), _options.Timeout);
            HttpResponseData response = await _transport.SendAsync(request, cancellationToken);

            if (response.IsTransportError)
            {
                throw new SarvamException(Hide($"Could not reach Sarvam AI at {_options.BaseUrl} ({response.TransportError})."));
            }

            JsonReader.TryParse(response.Body, out JsonValue root, out _);
            int status = response.StatusCode;
            if (status == 401 || status == 403)
            {
                throw new SarvamException(
                    $"Sarvam API key rejected (HTTP {status}). Check SARVAM_API_KEY or sarvam_api_key.txt.", status);
            }

            if (status == 429)
            {
                throw new SarvamException("Sarvam AI is rate limiting requests (HTTP 429). Wait a moment and try again.", status);
            }

            if (status < 200 || status >= 300)
            {
                // Sarvam reports problems as {"error": {"message": "...", "code": "...", "request_id": "..."}}.
                string message = root?.Get("error")?.GetString("message") ?? "no details";
                throw new SarvamException(Hide($"Sarvam AI returned an error (HTTP {status}): {message}"), status);
            }

            if (root == null || root.Kind != JsonKind.Object)
            {
                throw new SarvamException("Sarvam AI returned a body that is not a JSON object.", status);
            }

            return root;
        }

        /// <summary>Belt and braces: if a server or network message ever echoed the key, blank it out.</summary>
        private string Hide(string message)
        {
            return message.Replace(_apiKey, "[hidden key]");
        }
    }
}
