using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CourtSim.Core.Http;
using UnityEngine.Networking;

namespace CourtSim.Runtime.Advising
{
    /// <summary>
    /// <see cref="IHttpTransport"/> on <see cref="UnityWebRequest"/>, which works on every Unity platform
    /// (including Android-based headsets) without extra DLLs. Call from the main thread.
    /// </summary>
    public sealed class UnityWebRequestTransport : IHttpTransport
    {
        public Task<HttpResponseData> SendAsync(HttpRequestData request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completion = new TaskCompletionSource<HttpResponseData>();
            bool isGet = string.Equals(request.Method, HttpRequestData.Get, StringComparison.OrdinalIgnoreCase);
            var web = new UnityWebRequest(request.Url, isGet ? UnityWebRequest.kHttpVerbGET : UnityWebRequest.kHttpVerbPOST)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = Math.Max(1, (int)Math.Ceiling(request.Timeout.TotalSeconds)),
            };

            // A GET (such as Ollama's status check) has no body to upload.
            if (!isGet)
            {
                web.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(request.Body));
            }

            foreach (KeyValuePair<string, string> header in request.Headers)
            {
                web.SetRequestHeader(header.Key, header.Value);
            }

            CancellationTokenRegistration registration = default;
            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(() =>
                {
                    if (completion.TrySetCanceled(cancellationToken))
                    {
                        web.Abort();
                    }
                });
            }

            UnityWebRequestAsyncOperation operation = web.SendWebRequest();
            operation.completed += _ =>
            {
                registration.Dispose();
                try
                {
                    completion.TrySetResult(ToResponse(web));
                }
                finally
                {
                    web.Dispose();
                }
            };

            return completion.Task;
        }

        private static HttpResponseData ToResponse(UnityWebRequest web)
        {
            if (web.result == UnityWebRequest.Result.ConnectionError || (web.responseCode == 0 && web.result != UnityWebRequest.Result.Success))
            {
                return HttpResponseData.Failed(string.IsNullOrEmpty(web.error) ? "Connection failed." : web.error);
            }

            Dictionary<string, string> headers = web.GetResponseHeaders();
            return new HttpResponseData((int)web.responseCode, web.downloadHandler != null ? web.downloadHandler.text : string.Empty, headers);
        }
    }
}
