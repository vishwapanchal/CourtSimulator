using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CourtSim.Core.Claude;
using CourtSim.Core.Http;
using CourtSim.Core.Json;
using CourtSim.Runtime.Advising;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CourtSim.Runtime.Tests
{
    /// <summary>Runs <see cref="ClaudeClient"/> over the real UnityWebRequest transport against a local HTTP stub.</summary>
    public sealed class UnityWebRequestTransportTests
    {
        private const float TimeoutSeconds = 15f;
        private const string ReplyJson =
            "{\"id\":\"msg_test\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"claude-opus-5-5\",\"stop_reason\":\"end_turn\","
            + "\"content\":[{\"type\":\"thinking\",\"thinking\":\"\"},{\"type\":\"text\",\"text\":\"Hello from the stub\"}],"
            + "\"usage\":{\"input_tokens\":12,\"output_tokens\":5}}";

        private HttpStub _stub;

        [TearDown]
        public void TearDown()
        {
            _stub?.Dispose();
            _stub = null;
        }

        [UnityTest]
        public IEnumerator CreateMessage_OverUnityWebRequest_SendsHeadersAndParsesReply()
        {
            _stub = new HttpStub(200, ReplyJson, TimeSpan.Zero);
            ClaudeClient client = CreateClient(_stub.BaseUrl, 0);

            Task<ClaudeMessageResponse> task = client.CreateMessageAsync(new ClaudeMessageRequest("system", "hi"), CancellationToken.None);
            yield return WaitFor(task);

            Assert.That(task.Status, Is.EqualTo(TaskStatus.RanToCompletion), task.Exception?.ToString());
            Assert.That(task.Result.Text, Is.EqualTo("Hello from the stub"));
            Assert.That(task.Result.RequestId, Is.EqualTo("req_stub"));
            Assert.That(task.Result.InputTokens, Is.EqualTo(12));

            HttpStub.Captured request = _stub.Requests[0];
            Assert.That(request.Path, Is.EqualTo("/v1/messages"));
            Assert.That(request.Headers["x-api-key"], Is.EqualTo("test-key"));
            Assert.That(request.Headers["anthropic-version"], Is.EqualTo(ClaudeClientOptions.DefaultApiVersion));
            Assert.That(request.Headers["content-type"], Does.StartWith("application/json"));
            Assert.That(JsonReader.Parse(request.Body).GetString("model"), Is.EqualTo(ClaudeClientOptions.DefaultModel));
        }

        [UnityTest]
        public IEnumerator CreateMessage_BadRequest_ThrowsHttpErrorWithoutRetrying()
        {
            _stub = new HttpStub(400, "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"bad\"}}", TimeSpan.Zero);
            ClaudeClient client = CreateClient(_stub.BaseUrl, 2);

            Task<ClaudeMessageResponse> task = client.CreateMessageAsync(new ClaudeMessageRequest(string.Empty, "hi"), CancellationToken.None);
            yield return WaitFor(task);

            var error = task.Exception?.GetBaseException() as ClaudeException;
            Assert.That(error, Is.Not.Null, task.Exception?.ToString());
            Assert.That(error.Kind, Is.EqualTo(ClaudeErrorKind.Http));
            Assert.That(error.StatusCode, Is.EqualTo(400));
            Assert.That(error.ErrorType, Is.EqualTo("invalid_request_error"));
            Assert.That(_stub.Requests.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator CreateMessage_ConnectionRefused_ThrowsTransportError()
        {
            ClaudeClient client = CreateClient($"http://127.0.0.1:{FreePort()}", 0);

            Task<ClaudeMessageResponse> task = client.CreateMessageAsync(new ClaudeMessageRequest(string.Empty, "hi"), CancellationToken.None);
            yield return WaitFor(task);

            var error = task.Exception?.GetBaseException() as ClaudeException;
            Assert.That(error, Is.Not.Null, task.Exception?.ToString());
            Assert.That(error.Kind, Is.EqualTo(ClaudeErrorKind.Transport));
        }

        [UnityTest]
        public IEnumerator SendAsync_Cancelled_CancelsTheTask()
        {
            _stub = new HttpStub(200, ReplyJson, TimeSpan.FromSeconds(3));
            var transport = new UnityWebRequestTransport();
            var cancellation = new CancellationTokenSource();

            Task<HttpResponseData> task = transport.SendAsync(
                new HttpRequestData(_stub.BaseUrl + "/v1/messages", null, "{}", TimeSpan.FromSeconds(10)),
                cancellation.Token);
            yield return null;
            cancellation.Cancel();
            yield return WaitFor(task);

            Assert.That(task.IsCanceled, Is.True);
        }

        private static ClaudeClient CreateClient(string baseUrl, int maxRetries)
        {
            var options = new ClaudeClientOptions
            {
                ApiKey = "test-key",
                BaseUrl = baseUrl,
                MaxRetries = maxRetries,
                Timeout = TimeSpan.FromSeconds(10),
            };
            return new ClaudeClient(options, new UnityWebRequestTransport(), (delay, token) => Task.CompletedTask);
        }

        private static IEnumerator WaitFor(Task task)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!task.IsCompleted)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail("Timed out waiting for the request.");
                }

                yield return null;
            }
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        /// <summary>A one-route HTTP server on a background thread that records each request.</summary>
        private sealed class HttpStub : IDisposable
        {
            private readonly HttpListener _listener = new HttpListener();
            private readonly int _status;
            private readonly string _body;
            private readonly TimeSpan _delay;
            private readonly Thread _thread;
            private readonly List<Captured> _requests = new List<Captured>();

            public HttpStub(int status, string body, TimeSpan delay)
            {
                _status = status;
                _body = body;
                _delay = delay;
                BaseUrl = $"http://127.0.0.1:{FreePort()}";
                _listener.Prefixes.Add(BaseUrl + "/");
                _listener.Start();
                _thread = new Thread(Serve) { IsBackground = true };
                _thread.Start();
            }

            public string BaseUrl { get; }

            public IReadOnlyList<Captured> Requests
            {
                get
                {
                    lock (_requests)
                    {
                        return _requests.ToArray();
                    }
                }
            }

            public void Dispose()
            {
                try
                {
                    _listener.Stop();
                    _listener.Close();
                }
                catch (ObjectDisposedException)
                {
                    // Already closed.
                }
            }

            private void Serve()
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = _listener.GetContext();
                    }
                    catch (Exception)
                    {
                        return; // Stopped.
                    }

                    try
                    {
                        string requestBody;
                        using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                        {
                            requestBody = reader.ReadToEnd();
                        }

                        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (string key in context.Request.Headers.AllKeys)
                        {
                            headers[key] = context.Request.Headers[key];
                        }

                        lock (_requests)
                        {
                            _requests.Add(new Captured(context.Request.Url.AbsolutePath, headers, requestBody));
                        }

                        if (_delay > TimeSpan.Zero)
                        {
                            Thread.Sleep(_delay);
                        }

                        byte[] bytes = Encoding.UTF8.GetBytes(_body);
                        context.Response.StatusCode = _status;
                        context.Response.ContentType = "application/json";
                        context.Response.AddHeader("request-id", "req_stub");
                        context.Response.ContentLength64 = bytes.Length;
                        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                        context.Response.Close();
                    }
                    catch (Exception)
                    {
                        // The client went away (e.g. the cancellation test); keep serving.
                    }
                }
            }

            public sealed class Captured
            {
                public Captured(string path, Dictionary<string, string> headers, string body)
                {
                    Path = path;
                    Headers = headers;
                    Body = body;
                }

                public string Path { get; }

                public Dictionary<string, string> Headers { get; }

                public string Body { get; }
            }
        }
    }
}
