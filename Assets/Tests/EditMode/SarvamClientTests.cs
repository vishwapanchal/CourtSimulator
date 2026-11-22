using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CourtSim.Core.Http;
using CourtSim.Core.Json;
using CourtSim.Core.Presentation;
using CourtSim.Core.Sarvam;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    public sealed class SarvamClientTests
    {
        private const string Key = "sk_test_SECRET_1234567890";

        private FakeTransport _transport;

        [SetUp]
        public void SetUp()
        {
            _transport = new FakeTransport();
        }

        [Test]
        public void Constructor_BadArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new SarvamClient(null, Key, _transport));
            Assert.Throws<ArgumentNullException>(() => new SarvamClient(new SarvamClientOptions(), Key, null));
            var error = Assert.Throws<ArgumentException>(() => new SarvamClient(new SarvamClientOptions(), "  ", _transport));
            Assert.That(error.Message, Does.Contain("key is required"));
        }

        [Test]
        public void Options_Defaults_MatchDocumentedValues()
        {
            SarvamClientOptions options = Client().Options;

            Assert.That(options.BaseUrl, Is.EqualTo("https://api.sarvam.ai"));
            Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromSeconds(60)));
            Assert.That(options.TranslateModel, Is.EqualTo("mayura:v1"));
            Assert.That(options.SpeechModel, Is.EqualTo("bulbul:v3"));
            Assert.That(options.SourceLanguage, Is.EqualTo("en-IN"));
            Assert.That(options.TargetLanguage, Is.EqualTo("kn-IN"));
            Assert.That(options.Mode, Is.EqualTo("code-mixed"));
            Assert.That(options.Pace, Is.EqualTo(1.0));
            Assert.That(options.SampleRate, Is.EqualTo(22050));
        }

        [Test]
        public void Endpoints_TrailingSlash_IsTrimmed()
        {
            var client = new SarvamClient(new SarvamClientOptions { BaseUrl = "https://proxy.local/" }, Key, _transport);

            Assert.That(client.TranslateEndpoint, Is.EqualTo("https://proxy.local/translate"));
            Assert.That(client.SpeechEndpoint, Is.EqualTo("https://proxy.local/text-to-speech"));
        }

        [Test]
        public void TranslateAsync_SendsExpectedRequest_ReturnsTranslation()
        {
            _transport.Enqueue(TranslateOk("Case strong ಇದೆ."));

            string result = Client().TranslateAsync("The case is strong.", true, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result, Is.EqualTo("Case strong ಇದೆ."));
            HttpRequestData request = _transport.Requests.Single();
            Assert.That(request.Url, Is.EqualTo("https://api.sarvam.ai/translate"));
            Assert.That(request.Timeout, Is.EqualTo(TimeSpan.FromSeconds(60)));
            Assert.That(HeaderValue(request, "api-subscription-key"), Is.EqualTo(Key));
            Assert.That(HeaderValue(request, "content-type"), Is.EqualTo("application/json"));

            JsonValue body = JsonReader.Parse(request.Body);
            Assert.That(body.GetString("input"), Is.EqualTo("The case is strong."));
            Assert.That(body.GetString("source_language_code"), Is.EqualTo("en-IN"));
            Assert.That(body.GetString("target_language_code"), Is.EqualTo("kn-IN"));
            Assert.That(body.GetString("model"), Is.EqualTo("mayura:v1"));
            Assert.That(body.GetString("mode"), Is.EqualTo("code-mixed"));
            Assert.That(body.GetString("speaker_gender"), Is.EqualTo("Female"));
        }

        [Test]
        public void BuildTranslateBody_Male_SaysMale()
        {
            Assert.That(Client().BuildTranslateBody("Hello.", false).GetString("speaker_gender"), Is.EqualTo("Male"));
        }

        [Test]
        public void TranslateAsync_LongText_IsSentInPiecesAndJoined()
        {
            string sentence = new string('a', 599) + ".";
            string text = sentence + " " + sentence + " " + sentence;
            _transport.Enqueue(TranslateOk("one")).Enqueue(TranslateOk("two")).Enqueue(TranslateOk("three"));

            string result = Client().TranslateAsync(text, false, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result, Is.EqualTo("one two three"));
            Assert.That(_transport.Requests.Count, Is.EqualTo(3));
            foreach (HttpRequestData request in _transport.Requests)
            {
                Assert.That(JsonReader.Parse(request.Body).GetString("input"), Is.EqualTo(sentence));
            }
        }

        [Test]
        public void TranslateAsync_EmptyTranslation_Throws()
        {
            _transport.Enqueue(TranslateOk("  "));

            var error = Assert.Throws<SarvamException>(() => Client().TranslateAsync("Hi.", false, CancellationToken.None).GetAwaiter().GetResult());

            Assert.That(error.Message, Does.Contain("empty translation"));
        }

        [Test]
        public void SpeakAsync_SendsExpectedRequest_DecodesBase64Wav()
        {
            byte[] wav = TinyWav(0.5f, -0.5f);
            _transport.Enqueue(SpeechOk(wav));

            IReadOnlyList<byte[]> result = Client().SpeakAsync("ನಮಸ್ಕಾರ", "roopa", CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(wav));
            WavData decoded = WavReader.Read(result[0]);
            Assert.That(decoded.SampleRate, Is.EqualTo(22050));
            Assert.That(decoded.FrameCount, Is.EqualTo(2));

            HttpRequestData request = _transport.Requests.Single();
            Assert.That(request.Url, Is.EqualTo("https://api.sarvam.ai/text-to-speech"));
            Assert.That(HeaderValue(request, "api-subscription-key"), Is.EqualTo(Key));
            JsonValue body = JsonReader.Parse(request.Body);
            Assert.That(body.GetString("text"), Is.EqualTo("ನಮಸ್ಕಾರ"));
            Assert.That(body.GetString("language_code"), Is.EqualTo("kn-IN"));
            Assert.That(body.GetString("model"), Is.EqualTo("bulbul:v3"));
            Assert.That(body.GetString("speaker"), Is.EqualTo("roopa"));
            Assert.That(body.GetNumber("pace"), Is.EqualTo(1.0));
            Assert.That(body.GetNumber("speech_sample_rate"), Is.EqualTo(22050));
        }

        [Test]
        public void SpeakAsync_LongText_ReturnsOneWavPerPiece()
        {
            string text = new string('x', 2000) + ". " + new string('y', 2000) + ".";
            _transport.Enqueue(SpeechOk(TinyWav(0.1f))).Enqueue(SpeechOk(TinyWav(0.2f)));

            IReadOnlyList<byte[]> result = Client().SpeakAsync(text, "anand", CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(_transport.Requests.Count, Is.EqualTo(2));
            Assert.That(JsonReader.Parse(_transport.Requests[1].Body).GetString("text"), Is.EqualTo(new string('y', 2000) + "."));
        }

        [Test]
        public void SpeakAsync_BlankText_SendsNothing()
        {
            IReadOnlyList<byte[]> result = Client().SpeakAsync("   ", "anand", CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result, Is.Empty);
            Assert.That(_transport.Requests, Is.Empty);
        }

        [Test]
        public void SpeakAsync_NoAudios_Throws()
        {
            _transport.Enqueue(new HttpResponseData(200, "{\"request_id\":\"r1\",\"audios\":[]}"));

            var error = Assert.Throws<SarvamException>(() => Client().SpeakAsync("Hi.", "anand", CancellationToken.None).GetAwaiter().GetResult());

            Assert.That(error.Message, Does.Contain("no audio"));
        }

        [Test]
        public void DecodeAudio_InvalidInput_Throws()
        {
            Assert.That(Assert.Throws<SarvamException>(() => SarvamClient.DecodeAudio("not base64!")).Message, Does.Contain("base64"));
            Assert.That(Assert.Throws<SarvamException>(() => SarvamClient.DecodeAudio("")).Message, Does.Contain("empty"));
        }

        [TestCase(401)]
        [TestCase(403)]
        public void Errors_KeyRefused_SayKeyRejectedWithoutTheKey(int status)
        {
            _transport.Enqueue(ErrorResponse(status, "Invalid API key " + Key));

            var error = Assert.Throws<SarvamException>(() => Client().TranslateAsync("Hi.", false, CancellationToken.None).GetAwaiter().GetResult());

            Assert.That(error.Message, Does.Contain("Sarvam API key rejected"));
            Assert.That(error.StatusCode, Is.EqualTo(status));
            Assert.That(error.IsKeyRejected, Is.True);
            Assert.That(error.Message, Does.Not.Contain(Key));
        }

        [Test]
        public void Errors_RateLimited_SaysSo()
        {
            _transport.Enqueue(ErrorResponse(429, "Too many requests"));

            var error = Assert.Throws<SarvamException>(() => Client().SpeakAsync("Hi.", "anand", CancellationToken.None).GetAwaiter().GetResult());

            Assert.That(error.Message, Does.Contain("rate limiting"));
            Assert.That(error.StatusCode, Is.EqualTo(429));
            Assert.That(error.IsKeyRejected, Is.False);
        }

        [Test]
        public void Errors_ServerError_IncludesServerMessageButNeverTheKey()
        {
            _transport.Enqueue(ErrorResponse(500, "Internal failure while using " + Key));

            var error = Assert.Throws<SarvamException>(() => Client().TranslateAsync("Hi.", false, CancellationToken.None).GetAwaiter().GetResult());

            Assert.That(error.Message, Does.Contain("HTTP 500"));
            Assert.That(error.Message, Does.Contain("Internal failure"));
            Assert.That(error.Message, Does.Not.Contain(Key));
        }

        [Test]
        public void Errors_ServerErrorWithoutJson_SaysNoDetails()
        {
            _transport.Enqueue(new HttpResponseData(502, "<html>Bad gateway</html>"));

            var error = Assert.Throws<SarvamException>(() => Client().TranslateAsync("Hi.", false, CancellationToken.None).GetAwaiter().GetResult());

            Assert.That(error.Message, Does.Contain("HTTP 502"));
            Assert.That(error.Message, Does.Contain("no details"));
        }

        [Test]
        public void Errors_TransportFailure_SaysCouldNotReachWithoutTheKey()
        {
            _transport.Enqueue(HttpResponseData.Failed("Cannot resolve destination host " + Key));

            var error = Assert.Throws<SarvamException>(() => Client().TranslateAsync("Hi.", false, CancellationToken.None).GetAwaiter().GetResult());

            Assert.That(error.Message, Does.Contain("Could not reach Sarvam AI"));
            Assert.That(error.StatusCode, Is.EqualTo(0));
            Assert.That(error.Message, Does.Not.Contain(Key));
        }

        [Test]
        public void Errors_SuccessButNotJsonObject_Throws()
        {
            _transport.Enqueue(new HttpResponseData(200, "[1, 2]"));

            var error = Assert.Throws<SarvamException>(() => Client().TranslateAsync("Hi.", false, CancellationToken.None).GetAwaiter().GetResult());

            Assert.That(error.Message, Does.Contain("not a JSON object"));
        }

        [Test]
        public void Split_ShortText_IsOnePiece()
        {
            Assert.That(SarvamClient.Split("  Hello there.  ", 1000), Is.EqualTo(new[] { "Hello there." }));
            Assert.That(SarvamClient.Split(null, 1000), Is.Empty);
        }

        [Test]
        public void Split_PrefersSentenceEnds()
        {
            List<string> pieces = SarvamClient.Split("One two. Three four! Five six?", 12);

            Assert.That(pieces, Is.EqualTo(new[] { "One two.", "Three four!", "Five six?" }));
        }

        [Test]
        public void Split_KannadaDandaAndLineBreak_CountAsSentenceEnds()
        {
            Assert.That(SarvamClient.Split("ಒಂದು ಎರಡು। ಮೂರು ನಾಲ್ಕು", 14), Is.EqualTo(new[] { "ಒಂದು ಎರಡು।", "ಮೂರು ನಾಲ್ಕು" }));
            Assert.That(SarvamClient.Split("first line\nsecond line", 15), Is.EqualTo(new[] { "first line", "second line" }));
        }

        [Test]
        public void Split_NoSentenceEnd_CutsAtSpace()
        {
            Assert.That(SarvamClient.Split("alpha beta gamma delta", 12), Is.EqualTo(new[] { "alpha beta", "gamma delta" }));
        }

        [Test]
        public void Split_OneHugeWord_CutsAtLimitWithoutBreakingSurrogates()
        {
            Assert.That(SarvamClient.Split("abcdefghij", 4), Is.EqualTo(new[] { "abcd", "efgh", "ij" }));

            // "😀" is one emoji stored as two chars; a cut must not land between them.
            List<string> pieces = SarvamClient.Split("abc😀def", 4);
            Assert.That(pieces[0], Is.EqualTo("abc"));
            Assert.That(string.Concat(pieces), Is.EqualTo("abc😀def"));
        }

        [Test]
        public void Split_EveryPieceFitsTheLimit()
        {
            string text = string.Join(" ", Enumerable.Repeat("The accused was found with three phones.", 80));

            List<string> pieces = SarvamClient.Split(text, SarvamClientOptions.MaxTranslateChars);

            Assert.That(pieces.Count, Is.GreaterThan(1));
            Assert.That(pieces.All(p => p.Length <= SarvamClientOptions.MaxTranslateChars), Is.True);
            Assert.That(string.Join(" ", pieces), Is.EqualTo(text));
        }

        [Test]
        public void Split_TinyLimit_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SarvamClient.Split("abc", 1));
        }

        [Test]
        public void CacheKey_IsStableAndChangesWithSpeakerRoleAndText()
        {
            SarvamClient client = Client();
            var anand = new SarvamVoice("anand", false);
            string key = client.CacheKey(SarvamVoiceRole.Prosecutor, anand, "Bail is opposed.");

            Assert.That(key, Has.Length.EqualTo(40));
            Assert.That(key, Does.Match("^[0-9a-f]{40}$"));
            Assert.That(client.CacheKey(SarvamVoiceRole.Prosecutor, new SarvamVoice("anand", false), "Bail is opposed."), Is.EqualTo(key));
            Assert.That(client.CacheKey(SarvamVoiceRole.Prosecutor, new SarvamVoice("vijay", false), "Bail is opposed."), Is.Not.EqualTo(key));
            Assert.That(client.CacheKey(SarvamVoiceRole.Clerk, anand, "Bail is opposed."), Is.Not.EqualTo(key));
            Assert.That(client.CacheKey(SarvamVoiceRole.Prosecutor, anand, "Bail is granted."), Is.Not.EqualTo(key));
            Assert.That(key, Does.Not.Contain(Key));
            Assert.Throws<ArgumentNullException>(() => client.CacheKey(SarvamVoiceRole.Clerk, null, "x"));
        }

        [Test]
        public void WavWriter_RoundTripsThroughWavReader()
        {
            var original = new WavData(22050, 1, new[] { 0f, 0.5f, -0.5f, 1.5f });

            WavData decoded = WavReader.Read(WavWriter.Write(original));

            Assert.That(decoded.SampleRate, Is.EqualTo(22050));
            Assert.That(decoded.Channels, Is.EqualTo(1));
            Assert.That(decoded.Samples.Length, Is.EqualTo(4));
            Assert.That(decoded.Samples[1], Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(decoded.Samples[2], Is.EqualTo(-0.5f).Within(0.001f));
            Assert.That(decoded.Samples[3], Is.EqualTo(1f).Within(0.001f), "values beyond full scale are clamped");
            Assert.Throws<ArgumentNullException>(() => WavWriter.Write(null));
        }

        [Test]
        public void WavWriter_Concatenate_JoinsInOrder()
        {
            byte[] first = TinyWav(0.25f);
            byte[] second = TinyWav(-0.25f, 0.75f);

            WavData joined = WavReader.Read(WavWriter.Concatenate(new[] { first, second }));

            Assert.That(joined.FrameCount, Is.EqualTo(3));
            Assert.That(joined.Samples[0], Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(joined.Samples[2], Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(WavWriter.Concatenate(new[] { first }), Is.SameAs(first));
        }

        [Test]
        public void WavWriter_Concatenate_RejectsEmptyOrMismatchedInput()
        {
            Assert.Throws<ArgumentException>(() => WavWriter.Concatenate(new byte[0][]));
            Assert.Throws<ArgumentException>(() => WavWriter.Concatenate(null));
            byte[] other = WavWriter.Write(new WavData(16000, 1, new[] { 0f }));
            Assert.Throws<FormatException>(() => WavWriter.Concatenate(new[] { TinyWav(0f), other }));
        }

        private SarvamClient Client()
        {
            return new SarvamClient(new SarvamClientOptions(), Key, _transport);
        }

        private static string HeaderValue(HttpRequestData request, string name)
        {
            return request.Headers.Where(h => h.Key == name).Select(h => h.Value).SingleOrDefault();
        }

        private static byte[] TinyWav(params float[] samples)
        {
            return WavWriter.Write(new WavData(22050, 1, samples));
        }

        private static HttpResponseData TranslateOk(string text)
        {
            JsonValue body = JsonValue.NewObject()
                .Set("request_id", "req_1")
                .Set("translated_text", text)
                .Set("source_language_code", "en-IN");
            return new HttpResponseData(200, body.ToJson());
        }

        private static HttpResponseData SpeechOk(byte[] wav)
        {
            JsonValue body = JsonValue.NewObject()
                .Set("request_id", "req_2")
                .Set("audios", JsonValue.StringArray(new[] { Convert.ToBase64String(wav) }));
            return new HttpResponseData(200, body.ToJson());
        }

        private static HttpResponseData ErrorResponse(int status, string message)
        {
            JsonValue body = JsonValue.NewObject().Set("error", JsonValue.NewObject()
                .Set("message", message)
                .Set("code", "error_code")
                .Set("request_id", "req_err"));
            return new HttpResponseData(status, body.ToJson());
        }
    }
}
