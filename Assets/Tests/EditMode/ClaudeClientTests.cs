using System;
using System.Collections.Generic;
using System.Threading;
using CourtSim.Core.Claude;
using CourtSim.Core.Http;
using CourtSim.Core.Json;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    public sealed class ClaudeClientTests
    {
        private FakeTransport _transport;
        private DelayRecorder _delays;

        [SetUp]
        public void SetUp()
        {
            _transport = new FakeTransport();
            _delays = new DelayRecorder();
        }

        [Test]
        public void Constructor_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new ClaudeClient(null, _transport));
            Assert.Throws<ArgumentNullException>(() => new ClaudeClient(Options(), null));
        }

        [Test]
        public void Options_Defaults_MatchDocumentedValues()
        {
            var options = new ClaudeClientOptions();

            Assert.That(options.BaseUrl, Is.EqualTo("https://api.anthropic.com"));
            Assert.That(options.Model, Is.EqualTo("claude-opus-5-5"));
            Assert.That(options.ApiVersion, Is.EqualTo("2026-10-21"));
            Assert.That(options.Effort, Is.EqualTo("medium"));
            Assert.That(options.MaxRetries, Is.EqualTo(2));
            Assert.That(options.UseServerFallbacks, Is.True);
            Assert.That(options.ApiKey, Is.Null);
        }

        [Test]
        public void Endpoint_TrimsTrailingSlash()
        {
            ClaudeClientOptions options = Options();
            options.BaseUrl = "https://proxy.example/";

            Assert.That(new ClaudeClient(options, _transport).Endpoint, Is.EqualTo("https://proxy.example/v1/messages"));
            Assert.That(Client().Endpoint, Is.EqualTo("https://api.anthropic.com/v1/messages"));
        }

        [Test]
        public void MessageRequest_Arguments_AreValidated()
        {
            Assert.Throws<ArgumentNullException>(() => new ClaudeMessageRequest("s", null));
            var request = new ClaudeMessageRequest(null, "u");
            Assert.That(request.SystemPrompt, Is.Empty);
            Assert.That(request.OutputSchema, Is.Null);
        }

        [Test]
        public void BuildRequestBody_FullRequest_HasExpectedShape()
        {
            JsonValue schema = JsonValue.NewObject().Set("type", "object");
            ClaudeClientOptions options = Options();
            options.MaxTokens = 1234;
            options.Effort = "high";

            JsonValue body = new ClaudeClient(options, _transport).BuildRequestBody(new ClaudeMessageRequest("Be careful.", "Advise me.", schema));

            Assert.That(body.GetString("model"), Is.EqualTo("claude-opus-5-5"));
            Assert.That(body.GetNumber("max_tokens"), Is.EqualTo(1234));
            Assert.That(body.GetString("system"), Is.EqualTo("Be careful."));
            JsonValue messages = body.Get("messages");
            Assert.That(messages.Items.Count, Is.EqualTo(1));
            Assert.That(messages.Items[0].GetString("role"), Is.EqualTo("user"));
            Assert.That(messages.Items[0].GetString("content"), Is.EqualTo("Advise me."));
            JsonValue outputConfig = body.Get("output_config");
            Assert.That(outputConfig.GetString("effort"), Is.EqualTo("high"));
            JsonValue format = outputConfig.Get("format");
            Assert.That(format.GetString("type"), Is.EqualTo("json_schema"));
            Assert.That(format.Get("schema"), Is.SameAs(schema));
            Assert.That(body.GetString("fallbacks"), Is.EqualTo("default"));
        }

        [Test]
        public void BuildRequestBody_MinimalOptions_OmitsOptionalMembers()
        {
            ClaudeClientOptions options = Options();
            options.Effort = null;
            options.UseServerFallbacks = false;

            JsonValue body = new ClaudeClient(options, _transport).BuildRequestBody(new ClaudeMessageRequest(string.Empty, "Hi"));

            Assert.That(body.Has("system"), Is.False);
            Assert.That(body.Has("output_config"), Is.False);
            Assert.That(body.Has("fallbacks"), Is.False);
            Assert.That(body.Has("model"), Is.True);
        }

        [Test]
        public void BuildRequestBody_SchemaWithoutEffort_SendsFormatOnly()
        {
            ClaudeClientOptions options = Options();
            options.Effort = string.Empty;

            JsonValue body = new ClaudeClient(options, _transport).BuildRequestBody(new ClaudeMessageRequest("s", "u", JsonValue.NewObject()));

            Assert.That(body.Get("output_config").Has("effort"), Is.False);
            Assert.That(body.Get("output_config").Has("format"), Is.True);
        }

        [Test]
        public void BuildHeaders_Default_IncludesKeyVersionAndFallbackBeta()
        {
            IReadOnlyList<KeyValuePair<string, string>> headers = Client().BuildHeaders();

            Assert.That(HeaderValue(headers, "content-type"), Is.EqualTo("application/json"));
            Assert.That(HeaderValue(headers, "x-api-key"), Is.EqualTo("sk-test"));
            Assert.That(HeaderValue(headers, "anthropic-version"), Is.EqualTo("2026-10-21"));
            Assert.That(HeaderValue(headers, "anthropic-beta"), Is.EqualTo("server-side-fallback-2026-07-01"));
        }

        [Test]
        public void BuildHeaders_FallbacksDisabled_OmitsBeta()
        {
            ClaudeClientOptions options = Options();
            options.UseServerFallbacks = false;
            options.ApiKey = null;

            IReadOnlyList<KeyValuePair<string, string>> headers = new ClaudeClient(options, _transport).BuildHeaders();

            Assert.That(HeaderValue(headers, "anthropic-beta"), Is.Null);
            Assert.That(HeaderValue(headers, "x-api-key"), Is.Empty);
        }

        [Test]
        public void CreateMessage_Success_SendsBodyHeadersAndTimeout()
        {
            ClaudeClientOptions options = Options();
            options.Timeout = TimeSpan.FromSeconds(12);
            var client = new ClaudeClient(options, _transport, _delays.Delay);
            _transport.Enqueue(ClaudeResponses.Ok("{\"ok\":true}"));
            var request = new ClaudeMessageRequest("sys", "user", JsonValue.NewObject().Set("type", "object"));

            ClaudeMessageResponse response = client.CreateMessageAsync(request, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(_transport.Requests.Count, Is.EqualTo(1));
            HttpRequestData sent = _transport.Requests[0];
            Assert.That(sent.Url, Is.EqualTo("https://api.anthropic.com/v1/messages"));
            Assert.That(sent.Timeout, Is.EqualTo(TimeSpan.FromSeconds(12)));
            Assert.That(sent.Body, Is.EqualTo(client.BuildRequestBody(request).ToJson()));
            Assert.That(HeaderValue(sent.Headers, "x-api-key"), Is.EqualTo("sk-test"));
            Assert.That(response.Text, Is.EqualTo("{\"ok\":true}"));
            Assert.That(_delays.Delays, Is.Empty);
        }

        [Test]
        public void CreateMessage_Success_ParsesMetadataAndUsage()
        {
            JsonValue body = ClaudeResponses.MessageBody("end_turn", ClaudeResponses.TextBlock("hi"));
            body.Set("model", "claude-sonnet-fallback");
            _transport.Enqueue(ClaudeResponses.Response(200, body.ToJson(), ("Request-Id", "req_123")));

            ClaudeMessageResponse response = Send(Client());

            Assert.That(response.Id, Is.EqualTo("msg_01"));
            Assert.That(response.Model, Is.EqualTo("claude-sonnet-fallback"));
            Assert.That(response.StopReason, Is.EqualTo("end_turn"));
            Assert.That(response.InputTokens, Is.EqualTo(120));
            Assert.That(response.OutputTokens, Is.EqualTo(45));
            Assert.That(response.RequestId, Is.EqualTo("req_123"));
        }

        [Test]
        public void CreateMessage_NoUsage_ReportsZeroTokens()
        {
            JsonValue body = JsonValue.NewObject()
                .Set("stop_reason", "end_turn")
                .Set("content", TestCases.ArrayOf(ClaudeResponses.TextBlock("x")));
            _transport.Enqueue(ClaudeResponses.Response(200, body.ToJson()));

            ClaudeMessageResponse response = Send(Client());

            Assert.That(response.InputTokens, Is.EqualTo(0));
            Assert.That(response.OutputTokens, Is.EqualTo(0));
            Assert.That(response.RequestId, Is.Null);
            Assert.That(response.Id, Is.Null);
        }

        [Test]
        public void CreateMessage_MixedBlocks_ConcatenatesOnlyTextBlocks()
        {
            JsonValue body = ClaudeResponses.MessageBody(
                "end_turn",
                JsonValue.NewObject().Set("type", "thinking").Set("thinking", "secret").Set("text", "not this"),
                ClaudeResponses.TextBlock("Hello, "),
                JsonValue.NewObject().Set("type", "fallback").Set("text", "nor this").Set("model", "other"),
                JsonValue.NewObject().Set("type", "text"),
                ClaudeResponses.TextBlock("world"));
            _transport.Enqueue(ClaudeResponses.Response(200, body.ToJson()));

            Assert.That(Send(Client()).Text, Is.EqualTo("Hello, world"));
        }

        [Test]
        public void CreateMessage_NullRequest_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Client().CreateMessageAsync(null, CancellationToken.None).GetAwaiter().GetResult());
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void CreateMessage_NoApiKey_ThrowsNotConfigured(string apiKey)
        {
            ClaudeClientOptions options = Options();
            options.ApiKey = apiKey;
            var client = new ClaudeClient(options, _transport, _delays.Delay);

            ClaudeException error = Assert.Throws<ClaudeException>(() => Send(client));

            Assert.That(client.IsConfigured, Is.False);
            Assert.That(error.Kind, Is.EqualTo(ClaudeErrorKind.NotConfigured));
            Assert.That(_transport.Requests, Is.Empty);
        }

        [Test]
        public void CreateMessage_CancelledToken_DoesNotSend()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                ClaudeClient client = Client();

                Assert.Catch<OperationCanceledException>(() => client.CreateMessageAsync(new ClaudeMessageRequest("s", "u"), cancellation.Token).GetAwaiter().GetResult());
                Assert.That(_transport.Requests, Is.Empty);
            }
        }

        [TestCase(408)]
        [TestCase(409)]
        [TestCase(429)]
        [TestCase(500)]
        [TestCase(503)]
        [TestCase(529)]
        public void CreateMessage_TransientStatus_RetriesThenSucceeds(int status)
        {
            _transport.Enqueue(ClaudeResponses.Error(status, "overloaded_error", "Busy")).Enqueue(ClaudeResponses.Ok("done"));

            ClaudeMessageResponse response = Send(Client());

            Assert.That(response.Text, Is.EqualTo("done"));
            Assert.That(_transport.Requests.Count, Is.EqualTo(2));
            Assert.That(_delays.Delays.Count, Is.EqualTo(1));
        }

        [Test]
        public void CreateMessage_TransportFailure_RetriesThenSucceeds()
        {
            _transport.Enqueue(HttpResponseData.Failed("DNS lookup failed")).Enqueue(ClaudeResponses.Ok("done"));

            Assert.That(Send(Client()).Text, Is.EqualTo("done"));
            Assert.That(_transport.Requests.Count, Is.EqualTo(2));
        }

        [Test]
        public void CreateMessage_PersistentTransportFailure_ThrowsTransport()
        {
            for (int i = 0; i < 3; i++)
            {
                _transport.Enqueue(HttpResponseData.Failed("connection reset"));
            }

            ClaudeException error = Assert.Throws<ClaudeException>(() => Send(Client()));

            Assert.That(error.Kind, Is.EqualTo(ClaudeErrorKind.Transport));
            Assert.That(error.Message, Does.Contain("connection reset"));
            Assert.That(error.StatusCode, Is.EqualTo(0));
            Assert.That(_transport.Requests.Count, Is.EqualTo(3));
        }

        [Test]
        public void CreateMessage_RetryAfterHeader_IsHonoured()
        {
            _transport
                .Enqueue(ClaudeResponses.Error(429, "rate_limit_error", "Slow down", ("retry-after", "3")))
                .Enqueue(ClaudeResponses.Error(529, "overloaded_error", "Busy", ("Retry-After", "0.25")))
                .Enqueue(ClaudeResponses.Ok("done"));

            Send(Client());

            Assert.That(_delays.Delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(0.25) }));
        }

        [Test]
        public void CreateMessage_LongRetryAfter_IsCappedAtTwentySeconds()
        {
            _transport.Enqueue(ClaudeResponses.Error(429, "rate_limit_error", "Slow down", ("retry-after", "600"))).Enqueue(ClaudeResponses.Ok("done"));

            Send(Client());

            Assert.That(_delays.Delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(20) }));
        }

        [TestCase("soon")]
        [TestCase("-1")]
        [TestCase("")]
        public void CreateMessage_UnusableRetryAfter_UsesJitteredBackoff(string retryAfter)
        {
            _transport.Enqueue(ClaudeResponses.Error(500, "api_error", "Oops", ("retry-after", retryAfter))).Enqueue(ClaudeResponses.Ok("done"));

            Send(Client());

            Assert.That(_delays.Delays.Count, Is.EqualTo(1));
            Assert.That(_delays.Delays[0].TotalSeconds, Is.InRange(0.5, 0.625));
        }

        [Test]
        public void CreateMessage_NoRetryAfter_BacksOffExponentially()
        {
            ClaudeClientOptions options = Options();
            options.MaxRetries = 3;
            var client = new ClaudeClient(options, _transport, _delays.Delay, new Random(7));
            for (int i = 0; i < 3; i++)
            {
                _transport.Enqueue(ClaudeResponses.Error(500, "api_error", "Oops"));
            }

            _transport.Enqueue(ClaudeResponses.Ok("done"));

            Send(client);

            Assert.That(_delays.Delays.Count, Is.EqualTo(3));
            Assert.That(_delays.Delays[0].TotalSeconds, Is.InRange(0.5, 0.625));
            Assert.That(_delays.Delays[1].TotalSeconds, Is.InRange(1.0, 1.25));
            Assert.That(_delays.Delays[2].TotalSeconds, Is.InRange(2.0, 2.5));
        }

        [Test]
        public void CreateMessage_BadRequest_DoesNotRetryAndParsesErrorBody()
        {
            _transport.Enqueue(ClaudeResponses.Error(400, "invalid_request_error", "max_tokens is too large", ("request-id", "req_400")));

            ClaudeException error = Assert.Throws<ClaudeException>(() => Send(Client()));

            Assert.That(error.Kind, Is.EqualTo(ClaudeErrorKind.Http));
            Assert.That(error.StatusCode, Is.EqualTo(400));
            Assert.That(error.ErrorType, Is.EqualTo("invalid_request_error"));
            Assert.That(error.RequestId, Is.EqualTo("req_400"));
            Assert.That(error.Message, Is.EqualTo("Claude API returned HTTP 400 (invalid_request_error): max_tokens is too large"));
            Assert.That(_transport.Requests.Count, Is.EqualTo(1));
            Assert.That(_delays.Delays, Is.Empty);
        }

        [TestCase("not json")]
        [TestCase("")]
        [TestCase("[1,2]")]
        public void CreateMessage_UnparseableErrorBody_StillReportsStatus(string body)
        {
            _transport.Enqueue(ClaudeResponses.Response(401, body));

            ClaudeException error = Assert.Throws<ClaudeException>(() => Send(Client()));

            Assert.That(error.Kind, Is.EqualTo(ClaudeErrorKind.Http));
            Assert.That(error.StatusCode, Is.EqualTo(401));
            Assert.That(error.ErrorType, Is.Null);
            Assert.That(error.Message, Is.EqualTo("Claude API returned HTTP 401."));
        }

        [Test]
        public void CreateMessage_ShouldRetryFalse_OverridesRetryableStatus()
        {
            _transport.Enqueue(ClaudeResponses.Error(500, "api_error", "Oops", ("x-should-retry", "false")));

            ClaudeException error = Assert.Throws<ClaudeException>(() => Send(Client()));

            Assert.That(error.StatusCode, Is.EqualTo(500));
            Assert.That(_transport.Requests.Count, Is.EqualTo(1));
        }

        [Test]
        public void CreateMessage_ShouldRetryTrue_OverridesNonRetryableStatus()
        {
            _transport.Enqueue(ClaudeResponses.Error(400, "invalid_request_error", "Try again", ("x-should-retry", "true"))).Enqueue(ClaudeResponses.Ok("done"));

            Assert.That(Send(Client()).Text, Is.EqualTo("done"));
            Assert.That(_transport.Requests.Count, Is.EqualTo(2));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(4)]
        public void CreateMessage_PersistentFailure_StopsAfterMaxRetries(int maxRetries)
        {
            ClaudeClientOptions options = Options();
            options.MaxRetries = maxRetries;
            var client = new ClaudeClient(options, _transport, _delays.Delay, new Random(1));
            for (int i = 0; i < maxRetries + 2; i++)
            {
                _transport.Enqueue(ClaudeResponses.Error(529, "overloaded_error", "Busy"));
            }

            ClaudeException error = Assert.Throws<ClaudeException>(() => Send(client));

            Assert.That(error.StatusCode, Is.EqualTo(529));
            Assert.That(error.ErrorType, Is.EqualTo("overloaded_error"));
            Assert.That(_transport.Requests.Count, Is.EqualTo(maxRetries + 1));
            Assert.That(_delays.Delays.Count, Is.EqualTo(maxRetries));
        }

        [Test]
        public void CreateMessage_Refusal_ThrowsRefusalWithCategory()
        {
            JsonValue body = ClaudeResponses.MessageBody("refusal", ClaudeResponses.TextBlock("partial"));
            body.Set("stop_details", JsonValue.NewObject().Set("type", "refusal").Set("category", "cyber"));
            _transport.Enqueue(ClaudeResponses.Response(200, body.ToJson(), ("request-id", "req_r")));

            ClaudeException error = Assert.Throws<ClaudeException>(() => Send(Client()));

            Assert.That(error.Kind, Is.EqualTo(ClaudeErrorKind.Refusal));
            Assert.That(error.ErrorType, Is.EqualTo("cyber"));
            Assert.That(error.RequestId, Is.EqualTo("req_r"));
            Assert.That(error.Message, Does.Contain("(cyber)"));
            Assert.That(_transport.Requests.Count, Is.EqualTo(1));
        }

        [Test]
        public void CreateMessage_RefusalWithoutDetails_HasNoCategory()
        {
            _transport.Enqueue(ClaudeResponses.Response(200, ClaudeResponses.MessageBody("refusal").ToJson()));

            ClaudeException error = Assert.Throws<ClaudeException>(() => Send(Client()));

            Assert.That(error.Kind, Is.EqualTo(ClaudeErrorKind.Refusal));
            Assert.That(error.ErrorType, Is.Null);
        }

        [Test]
        public void CreateMessage_MaxTokens_ThrowsTruncated()
        {
            _transport.Enqueue(ClaudeResponses.Response(200, ClaudeResponses.MessageBody("max_tokens", ClaudeResponses.TextBlock("{\"half\":")).ToJson()));

            ClaudeException error = Assert.Throws<ClaudeException>(() => Send(Client()));

            Assert.That(error.Kind, Is.EqualTo(ClaudeErrorKind.Truncated));
        }

        [Test]
        public void CreateMessage_OnlyThinking_ThrowsInvalidResponse()
        {
            JsonValue body = ClaudeResponses.MessageBody("end_turn", JsonValue.NewObject().Set("type", "thinking").Set("thinking", "hmm"));
            _transport.Enqueue(ClaudeResponses.Response(200, body.ToJson()));

            ClaudeException error = Assert.Throws<ClaudeException>(() => Send(Client()));

            Assert.That(error.Kind, Is.EqualTo(ClaudeErrorKind.InvalidResponse));
            Assert.That(error.Message, Does.Contain("end_turn"));
        }

        [Test]
        public void CreateMessage_NoContent_ThrowsInvalidResponse()
        {
            _transport.Enqueue(ClaudeResponses.Response(200, "{}"));

            ClaudeException error = Assert.Throws<ClaudeException>(() => Send(Client()));

            Assert.That(error.Kind, Is.EqualTo(ClaudeErrorKind.InvalidResponse));
            Assert.That(error.Message, Does.Contain("stop_reason: none"));
        }

        [TestCase("<html>bad gateway</html>")]
        [TestCase("[]")]
        [TestCase("")]
        public void CreateMessage_NonObjectSuccessBody_ThrowsInvalidResponse(string body)
        {
            _transport.Enqueue(ClaudeResponses.Response(200, body, ("request-id", "req_bad")));

            ClaudeException error = Assert.Throws<ClaudeException>(() => Send(Client()));

            Assert.That(error.Kind, Is.EqualTo(ClaudeErrorKind.InvalidResponse));
            Assert.That(error.StatusCode, Is.EqualTo(200));
            Assert.That(error.RequestId, Is.EqualTo("req_bad"));
        }

        [Test]
        public void HttpResponseData_Headers_AreCaseInsensitive()
        {
            var response = new HttpResponseData(200, null, new[] { new KeyValuePair<string, string>("Retry-After", "1") });

            Assert.That(response.Header("retry-after"), Is.EqualTo("1"));
            Assert.That(response.Header("missing"), Is.Null);
            Assert.That(response.Body, Is.Empty);
            Assert.That(response.IsTransportError, Is.False);
        }

        [Test]
        public void HttpResponseData_Failed_IsTransportError()
        {
            HttpResponseData response = HttpResponseData.Failed("timeout");

            Assert.That(response.StatusCode, Is.EqualTo(0));
            Assert.That(response.IsTransportError, Is.True);
            Assert.That(response.TransportError, Is.EqualTo("timeout"));
        }

        [Test]
        public void HttpRequestData_Arguments_AreValidated()
        {
            Assert.Throws<ArgumentNullException>(() => new HttpRequestData(null, null, null, TimeSpan.Zero));
            var request = new HttpRequestData("https://x", null, null, TimeSpan.FromSeconds(1));
            Assert.That(request.Headers, Is.Empty);
            Assert.That(request.Body, Is.Empty);
        }

        [Test]
        public void ClaudeException_StoresDetails()
        {
            var error = new ClaudeException(ClaudeErrorKind.Http, "m", 503, "api_error", "req");

            Assert.That(error.Kind, Is.EqualTo(ClaudeErrorKind.Http));
            Assert.That(error.Message, Is.EqualTo("m"));
            Assert.That(error.StatusCode, Is.EqualTo(503));
            Assert.That(error.ErrorType, Is.EqualTo("api_error"));
            Assert.That(error.RequestId, Is.EqualTo("req"));
        }

        private static ClaudeClientOptions Options()
        {
            return new ClaudeClientOptions { ApiKey = "sk-test" };
        }

        private static string HeaderValue(IReadOnlyList<KeyValuePair<string, string>> headers, string name)
        {
            foreach (KeyValuePair<string, string> header in headers)
            {
                if (header.Key == name)
                {
                    return header.Value;
                }
            }

            return null;
        }

        private static ClaudeMessageResponse Send(ClaudeClient client)
        {
            return client.CreateMessageAsync(new ClaudeMessageRequest("system", "user"), CancellationToken.None).GetAwaiter().GetResult();
        }

        private ClaudeClient Client()
        {
            return new ClaudeClient(Options(), _transport, _delays.Delay, new Random(42));
        }
    }
}
