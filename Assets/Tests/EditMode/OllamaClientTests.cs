using System;
using System.Collections.Generic;
using System.Threading;
using CourtSim.Core.Http;
using CourtSim.Core.Json;
using CourtSim.Core.Ollama;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    public sealed class OllamaClientTests
    {
        private FakeTransport _transport;

        [SetUp]
        public void SetUp()
        {
            _transport = new FakeTransport();
        }

        [Test]
        public void Constructor_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new OllamaClient(null, _transport));
            Assert.Throws<ArgumentNullException>(() => new OllamaClient(new OllamaClientOptions(), null));
        }

        [Test]
        public void Options_Defaults_MatchDocumentedValues()
        {
            var options = new OllamaClientOptions();

            Assert.That(options.BaseUrl, Is.EqualTo("http://127.0.0.1:11434"));
            Assert.That(options.Model, Is.EqualTo("qwen3.5:9b"));
            Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromSeconds(180)));
            Assert.That(options.Temperature, Is.EqualTo(0.3));
            Assert.That(options.ContextTokens, Is.EqualTo(4096));
            Assert.That(options.KeepAlive, Is.EqualTo("60m"));
            Assert.That(Client().Options, Is.Not.Null);
        }

        [Test]
        public void Endpoint_TrailingSlash_IsTrimmed()
        {
            var options = new OllamaClientOptions { BaseUrl = "http://gpu-box:11434/" };

            Assert.That(new OllamaClient(options, _transport).Endpoint, Is.EqualTo("http://gpu-box:11434/api/chat"));
            Assert.That(Client().Endpoint, Is.EqualTo("http://127.0.0.1:11434/api/chat"));
        }

        [Test]
        public void BuildRequestBody_WithSchema_HasExpectedShape()
        {
            var options = new OllamaClientOptions { Model = "qwen3.5:4b", Temperature = 0.7, ContextTokens = 4096, KeepAlive = "5m" };
            JsonValue schema = JsonValue.NewObject().Set("type", "object");

            JsonValue body = new OllamaClient(options, _transport).BuildRequestBody("Be careful.", "Advise me.", schema);

            Assert.That(body.GetString("model"), Is.EqualTo("qwen3.5:4b"));
            JsonValue messages = body.Get("messages");
            Assert.That(messages.Items.Count, Is.EqualTo(2));
            Assert.That(messages.Items[0].GetString("role"), Is.EqualTo("system"));
            Assert.That(messages.Items[0].GetString("content"), Is.EqualTo("Be careful."));
            Assert.That(messages.Items[1].GetString("role"), Is.EqualTo("user"));
            Assert.That(messages.Items[1].GetString("content"), Is.EqualTo("Advise me."));
            Assert.That(body.Get("stream").AsBool(), Is.False);
            Assert.That(body.Get("think").AsBool(), Is.False);
            Assert.That(body.GetString("keep_alive"), Is.EqualTo("5m"));
            Assert.That(body.Get("options").GetNumber("temperature"), Is.EqualTo(0.7));
            Assert.That(body.Get("options").GetNumber("num_ctx"), Is.EqualTo(4096));
            Assert.That(body.Get("format"), Is.SameAs(schema));
        }

        [Test]
        public void BuildRequestBody_NoSchemaOrText_OmitsFormatAndSendsEmptyContent()
        {
            JsonValue body = Client().BuildRequestBody(null, null, null);

            Assert.That(body.Has("format"), Is.False);
            Assert.That(body.Get("messages").Items[0].GetString("content"), Is.Empty);
            Assert.That(body.Get("messages").Items[1].GetString("content"), Is.Empty);
        }

        [Test]
        public void Chat_Success_SendsBodyHeaderAndTimeout()
        {
            var options = new OllamaClientOptions { Timeout = TimeSpan.FromSeconds(30) };
            var client = new OllamaClient(options, _transport);
            JsonValue schema = JsonValue.NewObject().Set("type", "object");
            _transport.Enqueue(OllamaResponses.Ok("{\"ok\":true}"));

            client.ChatAsync("sys", "user", schema, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(_transport.Requests.Count, Is.EqualTo(1));
            HttpRequestData sent = _transport.Requests[0];
            Assert.That(sent.Url, Is.EqualTo("http://127.0.0.1:11434/api/chat"));
            Assert.That(sent.Timeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(sent.Body, Is.EqualTo(client.BuildRequestBody("sys", "user", schema).ToJson()));
            Assert.That(HeaderValue(sent.Headers, "content-type"), Is.EqualTo("application/json"));
        }

        [Test]
        public void Chat_Success_ParsesContentModelTokensAndDuration()
        {
            JsonValue body = OllamaResponses.ChatBody("{\"a\":1}");
            body.Set("model", "qwen3.5:9b");
            _transport.Enqueue(new HttpResponseData(200, body.ToJson()));

            OllamaChatResponse response = Send(Client());

            Assert.That(response.Content, Is.EqualTo("{\"a\":1}"));
            Assert.That(response.Model, Is.EqualTo("qwen3.5:9b"));
            Assert.That(response.PromptTokens, Is.EqualTo(812));
            Assert.That(response.OutputTokens, Is.EqualTo(96));
            Assert.That(response.TotalSeconds, Is.EqualTo(2.5).Within(1e-9));
        }

        [Test]
        public void Chat_MinimalBody_FallsBackToConfiguredModelAndZeroMetrics()
        {
            JsonValue body = JsonValue.NewObject().Set("message", JsonValue.NewObject().Set("content", "hi"));
            _transport.Enqueue(new HttpResponseData(200, body.ToJson()));

            OllamaChatResponse response = Send(Client());

            Assert.That(response.Model, Is.EqualTo(OllamaClientOptions.DefaultModel));
            Assert.That(response.PromptTokens, Is.EqualTo(0));
            Assert.That(response.OutputTokens, Is.EqualTo(0));
            Assert.That(response.TotalSeconds, Is.EqualTo(0));
        }

        [Test]
        public void Chat_TransportError_ThrowsAskingIfRunning()
        {
            _transport.Enqueue(HttpResponseData.Failed("Cannot connect to destination host"));

            OllamaException error = Assert.Throws<OllamaException>(() => Send(Client()));

            Assert.That(error.Message, Does.Contain("Is it running"));
            Assert.That(error.Message, Does.Contain("http://127.0.0.1:11434"));
            Assert.That(error.Message, Does.Contain("Cannot connect to destination host"));
            Assert.That(error.StatusCode, Is.EqualTo(0));
        }

        [Test]
        public void Chat_ModelNotFound_SuggestsPull()
        {
            var options = new OllamaClientOptions { Model = "x" };
            _transport.Enqueue(OllamaResponses.Error(404, "model 'x' not found"));

            OllamaException error = Assert.Throws<OllamaException>(() => Send(new OllamaClient(options, _transport)));

            Assert.That(error.StatusCode, Is.EqualTo(404));
            Assert.That(error.Message, Does.Contain("model 'x' not found"));
            Assert.That(error.Message, Does.Contain("ollama pull x"));
        }

        [TestCase("")]
        [TestCase("<html>Internal Server Error</html>")]
        [TestCase("[1]")]
        public void Chat_ServerErrorWithoutJsonError_ReportsStatus(string body)
        {
            _transport.Enqueue(new HttpResponseData(500, body));

            OllamaException error = Assert.Throws<OllamaException>(() => Send(Client()));

            Assert.That(error.StatusCode, Is.EqualTo(500));
            Assert.That(error.Message, Does.Contain("HTTP 500"));
            Assert.That(error.Message, Does.Not.Contain("ollama pull"));
        }

        [TestCase("")]
        [TestCase("not json")]
        [TestCase("[]")]
        [TestCase("\"text\"")]
        public void Chat_NonObjectBody_ThrowsOllamaException(string body)
        {
            _transport.Enqueue(new HttpResponseData(200, body));

            OllamaException error = Assert.Throws<OllamaException>(() => Send(Client()));

            Assert.That(error.Message, Does.Contain("not a JSON object"));
            Assert.That(error.StatusCode, Is.EqualTo(200));
        }

        [Test]
        public void Chat_DoneReasonLength_ThrowsCutOff()
        {
            _transport.Enqueue(new HttpResponseData(200, OllamaResponses.ChatBody("{\"half\":", "length").ToJson()));

            OllamaException error = Assert.Throws<OllamaException>(() => Send(Client()));

            Assert.That(error.Message, Does.Contain("cut off"));
        }

        [TestCase("")]
        [TestCase("   ")]
        public void Chat_EmptyContent_ThrowsEmptyReply(string content)
        {
            _transport.Enqueue(OllamaResponses.Ok(content));

            OllamaException error = Assert.Throws<OllamaException>(() => Send(Client()));

            Assert.That(error.Message, Does.Contain("empty reply"));
        }

        [Test]
        public void Chat_NoMessage_ThrowsEmptyReply()
        {
            _transport.Enqueue(new HttpResponseData(200, "{\"done\":true}"));

            OllamaException error = Assert.Throws<OllamaException>(() => Send(Client()));

            Assert.That(error.Message, Does.Contain("empty reply"));
        }

        [Test]
        public void OllamaException_StoresDetails()
        {
            var error = new OllamaException("m", 503);

            Assert.That(error.Message, Is.EqualTo("m"));
            Assert.That(error.StatusCode, Is.EqualTo(503));
            Assert.That(new OllamaException("n").StatusCode, Is.EqualTo(0));
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

        private static OllamaChatResponse Send(OllamaClient client)
        {
            return client.ChatAsync("system", "user", null, CancellationToken.None).GetAwaiter().GetResult();
        }

        private OllamaClient Client()
        {
            return new OllamaClient(new OllamaClientOptions(), _transport);
        }
    }
}
