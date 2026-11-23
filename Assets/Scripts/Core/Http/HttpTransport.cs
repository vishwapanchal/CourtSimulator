using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CourtSim.Core.Http
{
    /// <summary>An HTTP request: a POST with a JSON body, or a GET (no body) for status checks.</summary>
    public sealed class HttpRequestData
    {
        public const string Post = "POST";
        public const string Get = "GET";

        public HttpRequestData(string url, IReadOnlyList<KeyValuePair<string, string>> headers, string body, TimeSpan timeout, string method = Post)
        {
            Url = url ?? throw new ArgumentNullException(nameof(url));
            Headers = headers ?? Array.Empty<KeyValuePair<string, string>>();
            Body = body ?? string.Empty;
            Timeout = timeout;
            Method = string.IsNullOrEmpty(method) ? Post : method;
        }

        /// <summary>"POST" (the default) or "GET".</summary>
        public string Method { get; }

        public string Url { get; }

        public IReadOnlyList<KeyValuePair<string, string>> Headers { get; }

        public string Body { get; }

        public TimeSpan Timeout { get; }
    }

    /// <summary>An HTTP response, or a transport failure (<see cref="StatusCode"/> 0 with <see cref="TransportError"/>).</summary>
    public sealed class HttpResponseData
    {
        private readonly Dictionary<string, string> _headers;

        public HttpResponseData(int statusCode, string body, IEnumerable<KeyValuePair<string, string>> headers = null, string transportError = null)
        {
            StatusCode = statusCode;
            Body = body ?? string.Empty;
            TransportError = transportError;
            _headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (headers != null)
            {
                foreach (KeyValuePair<string, string> header in headers)
                {
                    _headers[header.Key] = header.Value;
                }
            }
        }

        public int StatusCode { get; }

        public string Body { get; }

        /// <summary>Set when no HTTP response was received (DNS, TLS, connection or timeout failure).</summary>
        public string TransportError { get; }

        public bool IsTransportError => TransportError != null;

        public static HttpResponseData Failed(string error)
        {
            return new HttpResponseData(0, string.Empty, null, error);
        }

        /// <summary>The header value (case-insensitive name), or null.</summary>
        public string Header(string name)
        {
            return _headers.TryGetValue(name, out string value) ? value : null;
        }
    }

    /// <summary>
    /// Sends HTTP requests. Unity implements this with UnityWebRequest; tests use a scripted fake. Implementations
    /// report transport failures as <see cref="HttpResponseData.Failed"/> rather than throwing, and throw
    /// <see cref="OperationCanceledException"/> only when the cancellation token is cancelled.
    /// </summary>
    public interface IHttpTransport
    {
        Task<HttpResponseData> SendAsync(HttpRequestData request, CancellationToken cancellationToken);
    }
}
