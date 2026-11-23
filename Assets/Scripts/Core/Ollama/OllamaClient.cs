using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CourtSim.Core.Http;
using CourtSim.Core.Json;

namespace CourtSim.Core.Ollama
{
    /// <summary>Settings for a local Ollama server. No API key: the model runs on this machine.</summary>
    public sealed class OllamaClientOptions
    {
        public const string DefaultBaseUrl = "http://127.0.0.1:11434";

        /// <summary>Qwen 3.5 9B (4-bit, 5.6 GB with a 4k context) is the strongest model that fits fully in an 8 GB
        /// GPU: ~30 s first load, then ~3 s per advice. Use "qwen3.5:0.8b" on machines without such a GPU.</summary>
        public const string DefaultModel = "qwen3.5:9b";

        public string BaseUrl { get; set; } = DefaultBaseUrl;

        public string Model { get; set; } = DefaultModel;

        /// <summary>Generous because the first request also loads the model into GPU memory.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(180);

        /// <summary>Low temperature keeps advice focused on the record; a little variety avoids identical wording.</summary>
        public double Temperature { get; set; } = 0.3;

        /// <summary>Context window in tokens. The brief plus reply stay under 2,000 tokens; 4,096 keeps a 9B model fully in 8 GB of VRAM.</summary>
        public int ContextTokens { get; set; } = 4096;

        /// <summary>How long Ollama keeps the model loaded between requests, so later advice is fast. An hour
        /// covers a whole presentation without the model being unloaded and reloaded mid-demo.</summary>
        public string KeepAlive { get; set; } = "60m";
    }

    /// <summary>One chat reply from Ollama.</summary>
    public sealed class OllamaChatResponse
    {
        public OllamaChatResponse(string model, string content, int promptTokens, int outputTokens, double totalSeconds)
        {
            Model = model;
            Content = content;
            PromptTokens = promptTokens;
            OutputTokens = outputTokens;
            TotalSeconds = totalSeconds;
        }

        public string Model { get; }

        public string Content { get; }

        public int PromptTokens { get; }

        public int OutputTokens { get; }

        /// <summary>Server-side time for the request, including model loading.</summary>
        public double TotalSeconds { get; }
    }

    /// <summary>Raised when the local model can't be reached or returns something unusable.</summary>
    public sealed class OllamaException : Exception
    {
        public OllamaException(string message, int statusCode = 0) : base(message)
        {
            StatusCode = statusCode;
        }

        public int StatusCode { get; }
    }

    /// <summary>
    /// Minimal client for Ollama's <c>/api/chat</c> endpoint. Sends a system prompt and one user turn,
    /// constrains the reply to a JSON schema (<c>format</c>) and turns off the model's thinking mode, which
    /// would only add delay for short advice.
    /// </summary>
    public sealed class OllamaClient
    {
        private readonly OllamaClientOptions _options;
        private readonly IHttpTransport _transport;

        public OllamaClient(OllamaClientOptions options, IHttpTransport transport)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public OllamaClientOptions Options => _options;

        public string Endpoint => _options.BaseUrl.TrimEnd('/') + "/api/chat";

        /// <summary>Lists the loaded models and where they sit (GPU or CPU memory).</summary>
        public string StatusEndpoint => _options.BaseUrl.TrimEnd('/') + "/api/ps";

        /// <summary>
        /// Asks Ollama where the configured model is loaded, so the game can warn when it is running on the CPU.
        /// Throws <see cref="OllamaException"/> if Ollama can't be reached.
        /// </summary>
        public async Task<OllamaModelStatus> GetModelStatusAsync(CancellationToken cancellationToken)
        {
            var request = new HttpRequestData(StatusEndpoint, null, string.Empty, TimeSpan.FromSeconds(5), HttpRequestData.Get);
            HttpResponseData response = await _transport.SendAsync(request, cancellationToken);
            if (response.IsTransportError)
            {
                throw new OllamaException($"Could not reach Ollama at {_options.BaseUrl}. ({response.TransportError})");
            }

            if (response.StatusCode < 200 || response.StatusCode >= 300)
            {
                throw new OllamaException($"Ollama's status check returned HTTP {response.StatusCode}.", response.StatusCode);
            }

            return OllamaModelStatus.FromPsReply(response.Body, _options.Model);
        }

        /// <summary>The JSON body sent to Ollama.</summary>
        public JsonValue BuildRequestBody(string systemPrompt, string userMessage, JsonValue schema)
        {
            JsonValue messages = JsonValue.NewArray()
                .Add(JsonValue.NewObject().Set("role", "system").Set("content", systemPrompt ?? string.Empty))
                .Add(JsonValue.NewObject().Set("role", "user").Set("content", userMessage ?? string.Empty));

            JsonValue body = JsonValue.NewObject()
                .Set("model", _options.Model)
                .Set("messages", messages)
                .Set("stream", false)
                .Set("think", false)
                .Set("keep_alive", _options.KeepAlive)
                .Set("options", JsonValue.NewObject()
                    .Set("temperature", _options.Temperature)
                    .Set("num_ctx", _options.ContextTokens));
            if (schema != null)
            {
                body.Set("format", schema);
            }

            return body;
        }

        public async Task<OllamaChatResponse> ChatAsync(string systemPrompt, string userMessage, JsonValue schema, CancellationToken cancellationToken)
        {
            var headers = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("content-type", "application/json") };
            var request = new HttpRequestData(Endpoint, headers, BuildRequestBody(systemPrompt, userMessage, schema).ToJson(), _options.Timeout);
            HttpResponseData response = await _transport.SendAsync(request, cancellationToken);

            if (response.IsTransportError)
            {
                throw new OllamaException($"Could not reach Ollama at {_options.BaseUrl}. Is it running? ({response.TransportError})");
            }

            JsonReader.TryParse(response.Body, out JsonValue root, out _);
            if (response.StatusCode < 200 || response.StatusCode >= 300)
            {
                // Ollama reports problems as {"error": "..."}; a missing model is the common one.
                string error = root?.GetString("error") ?? $"HTTP {response.StatusCode}";
                string hint = response.StatusCode == 404 ? $" Run: ollama pull {_options.Model}" : string.Empty;
                throw new OllamaException($"Ollama returned an error: {error}.{hint}", response.StatusCode);
            }

            if (root == null || root.Kind != JsonKind.Object)
            {
                throw new OllamaException("Ollama returned a body that is not a JSON object.", response.StatusCode);
            }

            if (root.GetString("done_reason") == "length")
            {
                throw new OllamaException("The local model's reply was cut off before it finished.", response.StatusCode);
            }

            string content = root.Get("message")?.GetString("content");
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new OllamaException("The local model returned an empty reply.", response.StatusCode);
            }

            return new OllamaChatResponse(
                root.GetString("model", _options.Model),
                content,
                (int)(root.GetNumber("prompt_eval_count") ?? 0),
                (int)(root.GetNumber("eval_count") ?? 0),
                (root.GetNumber("total_duration") ?? 0) / 1e9);
        }
    }
}
