using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CourtSim.Core.Http;
using CourtSim.Core.Json;

namespace CourtSim.Core.Claude
{
    /// <summary>Configuration for <see cref="ClaudeClient"/>. Defaults suit short, structured advice requests.</summary>
    public sealed class ClaudeClientOptions
    {
        public const string DefaultBaseUrl = "https://api.anthropic.com";
        public const string DefaultModel = "claude-opus-5-5";
        public const string DefaultApiVersion = "2026-10-21";
        public const string ServerFallbackBeta = "server-side-fallback-2026-07-01";

        /// <summary>Sent as <c>x-api-key</c>. Never log it.</summary>
        public string ApiKey { get; set; }

        /// <summary>Point at your own proxy to keep the key off trainee machines.</summary>
        public string BaseUrl { get; set; } = DefaultBaseUrl;

        public string Model { get; set; } = DefaultModel;

        public string ApiVersion { get; set; } = DefaultApiVersion;

        /// <summary><c>output_config.effort</c>: low, medium, high, xhigh or max. Set explicitly; the model's default is medium.</summary>
        public string Effort { get; set; } = "medium";

        public int MaxTokens { get; set; } = 16000;

        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(90);

        /// <summary>Retries after the first attempt, for 408/409/429/5xx and transport failures.</summary>
        public int MaxRetries { get; set; } = 2;

        /// <summary>
        /// Sends <c>fallbacks: "default"</c> so a request the model declines is re-run on a fallback model
        /// inside the same call, instead of failing the trainee's advice.
        /// </summary>
        public bool UseServerFallbacks { get; set; } = true;
    }

    /// <summary>One Messages API call: a system prompt, a single user turn and an optional JSON output schema.</summary>
    public sealed class ClaudeMessageRequest
    {
        public ClaudeMessageRequest(string system, string userMessage, JsonValue outputSchema = null)
        {
            SystemPrompt = system ?? string.Empty;
            UserMessage = userMessage ?? throw new ArgumentNullException(nameof(userMessage));
            OutputSchema = outputSchema;
        }

        public string SystemPrompt { get; }

        public string UserMessage { get; }

        /// <summary>When set, sent as <c>output_config.format</c> so the reply is JSON matching this schema.</summary>
        public JsonValue OutputSchema { get; }
    }

    public sealed class ClaudeMessageResponse
    {
        public ClaudeMessageResponse(string id, string model, string stopReason, string text, int inputTokens, int outputTokens, string requestId)
        {
            Id = id;
            Model = model;
            StopReason = stopReason;
            Text = text;
            InputTokens = inputTokens;
            OutputTokens = outputTokens;
            RequestId = requestId;
        }

        public string Id { get; }

        /// <summary>The model that produced the reply; differs from the requested model after a server-side fallback.</summary>
        public string Model { get; }

        public string StopReason { get; }

        /// <summary>All text blocks, concatenated.</summary>
        public string Text { get; }

        public int InputTokens { get; }

        public int OutputTokens { get; }

        public string RequestId { get; }
    }

    public enum ClaudeErrorKind
    {
        /// <summary>No API key configured.</summary>
        NotConfigured,

        /// <summary>No HTTP response: DNS, TLS, connection or timeout.</summary>
        Transport,

        /// <summary>The API returned an error status.</summary>
        Http,

        /// <summary>The model declined (<c>stop_reason: refusal</c>).</summary>
        Refusal,

        /// <summary>Output hit <c>max_tokens</c>.</summary>
        Truncated,

        /// <summary>The response could not be understood.</summary>
        InvalidResponse,
    }

    public sealed class ClaudeException : Exception
    {
        public ClaudeException(ClaudeErrorKind kind, string message, int statusCode = 0, string errorType = null, string requestId = null)
            : base(message)
        {
            Kind = kind;
            StatusCode = statusCode;
            ErrorType = errorType;
            RequestId = requestId;
        }

        public ClaudeErrorKind Kind { get; }

        public int StatusCode { get; }

        /// <summary>The API's <c>error.type</c>, e.g. <c>rate_limit_error</c>; or the refusal category.</summary>
        public string ErrorType { get; }

        /// <summary>The <c>request-id</c> header, for support tickets.</summary>
        public string RequestId { get; }
    }

    /// <summary>
    /// Minimal Claude Messages API client over an injected <see cref="IHttpTransport"/>: builds the request,
    /// retries transient failures with backoff, and returns the reply text.
    /// </summary>
    /// <remarks>
    /// Deliberately does not use <c>ConfigureAwait(false)</c>: Unity's transport must be called on the main
    /// thread, and the retry loop issues further requests after awaiting.
    /// </remarks>
    public sealed class ClaudeClient
    {
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(20);

        private readonly ClaudeClientOptions _options;
        private readonly IHttpTransport _transport;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly Random _jitter;

        /// <param name="delay">Waits between retries; defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>. Injected by tests.</param>
        public ClaudeClient(ClaudeClientOptions options, IHttpTransport transport, Func<TimeSpan, CancellationToken, Task> delay = null, Random jitter = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _delay = delay ?? Task.Delay;
            _jitter = jitter ?? new Random();
        }

        public ClaudeClientOptions Options => _options;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

        public string Endpoint => _options.BaseUrl.TrimEnd('/') + "/v1/messages";

        /// <summary>The JSON body sent for <paramref name="request"/>.</summary>
        public JsonValue BuildRequestBody(ClaudeMessageRequest request)
        {
            JsonValue message = JsonValue.NewObject().Set("role", "user").Set("content", request.UserMessage);
            JsonValue body = JsonValue.NewObject()
                .Set("model", _options.Model)
                .Set("max_tokens", _options.MaxTokens)
                .Set("messages", JsonValue.NewArray().Add(message));
            if (request.SystemPrompt.Length > 0)
            {
                body.Set("system", request.SystemPrompt);
            }

            JsonValue outputConfig = JsonValue.NewObject();
            if (!string.IsNullOrEmpty(_options.Effort))
            {
                outputConfig.Set("effort", _options.Effort);
            }

            if (request.OutputSchema != null)
            {
                outputConfig.Set("format", JsonValue.NewObject().Set("type", "json_schema").Set("schema", request.OutputSchema));
            }

            if (outputConfig.Properties.Count > 0)
            {
                body.Set("output_config", outputConfig);
            }

            if (_options.UseServerFallbacks)
            {
                body.Set("fallbacks", "default");
            }

            return body;
        }

        /// <summary>The headers sent with every request. Includes the API key; never log the result.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> BuildHeaders()
        {
            var headers = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("content-type", "application/json"),
                new KeyValuePair<string, string>("x-api-key", _options.ApiKey ?? string.Empty),
                new KeyValuePair<string, string>("anthropic-version", _options.ApiVersion),
            };
            if (_options.UseServerFallbacks)
            {
                headers.Add(new KeyValuePair<string, string>("anthropic-beta", ClaudeClientOptions.ServerFallbackBeta));
            }

            return headers;
        }

        public async Task<ClaudeMessageResponse> CreateMessageAsync(ClaudeMessageRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!IsConfigured)
            {
                throw new ClaudeException(ClaudeErrorKind.NotConfigured, "No Anthropic API key is configured.");
            }

            var http = new HttpRequestData(Endpoint, BuildHeaders(), BuildRequestBody(request).ToJson(), _options.Timeout);
            for (int attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                HttpResponseData response = await _transport.SendAsync(http, cancellationToken);
                if (response.StatusCode >= 200 && response.StatusCode < 300)
                {
                    return ParseSuccess(response);
                }

                ClaudeException error = ToException(response);
                if (attempt >= _options.MaxRetries || !ShouldRetry(response))
                {
                    throw error;
                }

                await _delay(RetryDelay(response, attempt), cancellationToken);
            }
        }

        internal static bool ShouldRetry(HttpResponseData response)
        {
            string shouldRetry = response.Header("x-should-retry");
            if (shouldRetry == "true")
            {
                return true;
            }

            if (shouldRetry == "false")
            {
                return false;
            }

            int status = response.StatusCode;
            return response.IsTransportError || status == 408 || status == 409 || status == 429 || status >= 500;
        }

        internal TimeSpan RetryDelay(HttpResponseData response, int attempt)
        {
            string retryAfter = response.Header("retry-after");
            if (retryAfter != null
                && double.TryParse(retryAfter, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
                && seconds >= 0)
            {
                return TimeSpan.FromSeconds(Math.Min(seconds, MaxBackoff.TotalSeconds));
            }

            double baseSeconds = 0.5 * Math.Pow(2, attempt);
            double jittered = baseSeconds * (1 + (_jitter.NextDouble() * 0.25));
            return TimeSpan.FromSeconds(Math.Min(jittered, MaxBackoff.TotalSeconds));
        }

        private static ClaudeMessageResponse ParseSuccess(HttpResponseData response)
        {
            string requestId = response.Header("request-id");
            if (!JsonReader.TryParse(response.Body, out JsonValue root, out string parseError) || root.Kind != JsonKind.Object)
            {
                throw new ClaudeException(ClaudeErrorKind.InvalidResponse, "The API returned a body that is not a JSON object: " + parseError, response.StatusCode, null, requestId);
            }

            string stopReason = root.GetString("stop_reason");
            if (stopReason == "refusal")
            {
                string category = root.Get("stop_details")?.GetString("category");
                throw new ClaudeException(ClaudeErrorKind.Refusal, "The model declined to answer" + (category != null ? $" ({category})." : "."), response.StatusCode, category, requestId);
            }

            if (stopReason == "max_tokens")
            {
                throw new ClaudeException(ClaudeErrorKind.Truncated, "The reply was cut off at max_tokens.", response.StatusCode, null, requestId);
            }

            var text = new StringBuilder();
            JsonValue content = root.Get("content");
            if (content != null)
            {
                foreach (JsonValue block in content.Items)
                {
                    if (block.GetString("type") == "text")
                    {
                        text.Append(block.GetString("text", string.Empty));
                    }
                }
            }

            if (text.Length == 0)
            {
                throw new ClaudeException(ClaudeErrorKind.InvalidResponse, $"The reply contained no text (stop_reason: {stopReason ?? "none"}).", response.StatusCode, null, requestId);
            }

            JsonValue usage = root.Get("usage");
            return new ClaudeMessageResponse(
                root.GetString("id"),
                root.GetString("model"),
                stopReason,
                text.ToString(),
                (int)(usage?.GetNumber("input_tokens") ?? 0),
                (int)(usage?.GetNumber("output_tokens") ?? 0),
                requestId);
        }

        private static ClaudeException ToException(HttpResponseData response)
        {
            if (response.IsTransportError)
            {
                return new ClaudeException(ClaudeErrorKind.Transport, "Could not reach the Claude API: " + response.TransportError);
            }

            string errorType = null;
            string message = null;
            if (JsonReader.TryParse(response.Body, out JsonValue root, out _))
            {
                JsonValue error = root.Get("error");
                errorType = error?.GetString("type");
                message = error?.GetString("message");
            }

            return new ClaudeException(
                ClaudeErrorKind.Http,
                $"Claude API returned HTTP {response.StatusCode}" + (errorType != null ? $" ({errorType})" : string.Empty) + (message != null ? ": " + message : "."),
                response.StatusCode,
                errorType,
                response.Header("request-id"));
        }
    }
}
