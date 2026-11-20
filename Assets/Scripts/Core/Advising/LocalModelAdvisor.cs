using System;
using System.Threading;
using System.Threading.Tasks;
using CourtSim.Core.Cases;
using CourtSim.Core.Ollama;

namespace CourtSim.Core.Advising
{
    /// <summary>
    /// Generates advice with a model running locally in Ollama: no API key, no internet, no cost. It sends the
    /// same prompt and schema as <see cref="ClaudeAdvisor"/> and applies the same validation.
    /// </summary>
    public sealed class LocalModelAdvisor : IAdvisor
    {
        private readonly OllamaClient _client;

        public LocalModelAdvisor(OllamaClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>The last successful reply's metadata, for logging and transcripts.</summary>
        public OllamaChatResponse LastResponse { get; private set; }

        public async Task<Advice> GetAdviceAsync(AdviceRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            AdviceScript target = request.TargetScript ?? throw new AdvisorException($"Case '{request.Case.Id}' has no authored advice for decision point {request.PointIndex}.");
            OllamaChatResponse response = await _client.ChatAsync(
                AdvicePromptBuilder.SystemPrompt,
                AdvicePromptBuilder.BuildBrief(request),
                AdvicePromptBuilder.BuildSchema(request),
                cancellationToken);

            Advice advice = AdviceReply.Parse(response.Content, request, target, AdviceSource.LocalModel);
            LastResponse = response;
            return advice;
        }
    }
}
