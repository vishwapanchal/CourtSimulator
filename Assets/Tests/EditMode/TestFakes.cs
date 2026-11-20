using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CourtSim.Core.Advising;
using CourtSim.Core.Http;
using CourtSim.Core.Json;

namespace CourtSim.Core.Tests
{
    /// <summary>
    /// Scripted HTTP transport. Returns already-completed tasks so tests can block on results without
    /// deadlocking under Unity's main-thread synchronization context.
    /// </summary>
    internal sealed class FakeTransport : IHttpTransport
    {
        private readonly Queue<HttpResponseData> _responses = new Queue<HttpResponseData>();
        private readonly List<HttpRequestData> _requests = new List<HttpRequestData>();

        public IReadOnlyList<HttpRequestData> Requests => _requests;

        public FakeTransport Enqueue(HttpResponseData response)
        {
            _responses.Enqueue(response);
            return this;
        }

        public Task<HttpResponseData> SendAsync(HttpRequestData request, CancellationToken cancellationToken)
        {
            _requests.Add(request);
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("The fake transport has no scripted response left.");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }

    /// <summary>Retry delay that records what was requested and returns immediately.</summary>
    internal sealed class DelayRecorder
    {
        private readonly List<TimeSpan> _delays = new List<TimeSpan>();

        public IReadOnlyList<TimeSpan> Delays => _delays;

        public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
        {
            _delays.Add(delay);
            return Task.CompletedTask;
        }
    }

    /// <summary>Advisor driven by a delegate; records each request.</summary>
    internal sealed class StubAdvisor : IAdvisor
    {
        private readonly Func<AdviceRequest, CancellationToken, Task<Advice>> _handler;
        private readonly List<AdviceRequest> _requests = new List<AdviceRequest>();

        public StubAdvisor(Func<AdviceRequest, CancellationToken, Task<Advice>> handler)
        {
            _handler = handler;
        }

        public IReadOnlyList<AdviceRequest> Requests => _requests;

        public static StubAdvisor Returning(Advice advice)
        {
            return new StubAdvisor((request, token) => Task.FromResult(advice));
        }

        public static StubAdvisor Failing(Exception error)
        {
            return new StubAdvisor((request, token) => Task.FromException<Advice>(error));
        }

        public Task<Advice> GetAdviceAsync(AdviceRequest request, CancellationToken cancellationToken)
        {
            _requests.Add(request);
            return _handler(request, cancellationToken);
        }
    }

    /// <summary>A manually advanced UTC clock.</summary>
    internal sealed class FakeClock
    {
        public DateTime Now { get; set; } = new DateTime(2026, 3, 4, 9, 0, 0, DateTimeKind.Utc);

        public DateTime Read()
        {
            return Now;
        }

        public void Advance(double seconds)
        {
            Now = Now.AddSeconds(seconds);
        }
    }

    /// <summary>Builders for Messages API responses.</summary>
    internal static class ClaudeResponses
    {
        public static JsonValue TextBlock(string text)
        {
            return JsonValue.NewObject().Set("type", "text").Set("text", text);
        }

        public static JsonValue MessageBody(string stopReason, params JsonValue[] blocks)
        {
            return JsonValue.NewObject()
                .Set("id", "msg_01")
                .Set("type", "message")
                .Set("role", "assistant")
                .Set("model", "claude-opus-5-5")
                .Set("stop_reason", stopReason)
                .Set("content", TestCases.ArrayOf(blocks))
                .Set("usage", JsonValue.NewObject().Set("input_tokens", 120).Set("output_tokens", 45));
        }

        public static HttpResponseData Ok(string text)
        {
            return Response(200, MessageBody("end_turn", TextBlock(text)).ToJson(), ("request-id", "req_ok"));
        }

        public static HttpResponseData Response(int status, string body, params (string Name, string Value)[] headers)
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach ((string name, string value) in headers)
            {
                list.Add(new KeyValuePair<string, string>(name, value));
            }

            return new HttpResponseData(status, body, list);
        }

        public static HttpResponseData Error(int status, string type, string message, params (string Name, string Value)[] headers)
        {
            JsonValue body = JsonValue.NewObject()
                .Set("type", "error")
                .Set("error", JsonValue.NewObject().Set("type", type).Set("message", message));
            return Response(status, body.ToJson(), headers);
        }
    }

    /// <summary>Builders for Ollama <c>/api/chat</c> responses.</summary>
    internal static class OllamaResponses
    {
        public static JsonValue ChatBody(string content, string doneReason = "stop")
        {
            return JsonValue.NewObject()
                .Set("model", "qwen3.5:0.8b")
                .Set("message", JsonValue.NewObject().Set("role", "assistant").Set("content", content))
                .Set("done", true)
                .Set("done_reason", doneReason)
                .Set("total_duration", 2500000000)
                .Set("prompt_eval_count", 812)
                .Set("eval_count", 96);
        }

        public static HttpResponseData Ok(string content)
        {
            return new HttpResponseData(200, ChatBody(content).ToJson());
        }

        public static HttpResponseData Error(int status, string message)
        {
            return new HttpResponseData(status, JsonValue.NewObject().Set("error", message).ToJson());
        }
    }
}
