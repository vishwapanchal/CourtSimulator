using System;
using System.Threading;
using System.Threading.Tasks;
using CourtSim.Core.Cases;
using CourtSim.Core.Claude;

namespace CourtSim.Core.Advising
{
    /// <summary>
    /// Generates case-specific advice with Claude. The planned flaw and the target ruling come from the case
    /// file; Claude only writes the reasoning that carries them. Replies are validated by <see cref="AdviceReply"/>.
    /// </summary>
    public sealed class ClaudeAdvisor : IAdvisor
    {
        private readonly ClaudeClient _client;

        public ClaudeAdvisor(ClaudeClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>The last successful reply's metadata, for logging.</summary>
        public ClaudeMessageResponse LastResponse { get; private set; }

        public async Task<Advice> GetAdviceAsync(AdviceRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            AdviceScript target = request.TargetScript ?? throw new AdvisorException($"Case '{request.Case.Id}' has no authored advice for decision point {request.PointIndex}.");
            var message = new ClaudeMessageRequest(
                AdvicePromptBuilder.SystemPrompt,
                AdvicePromptBuilder.BuildBrief(request),
                AdvicePromptBuilder.BuildSchema(request));

            ClaudeMessageResponse response = await _client.CreateMessageAsync(message, cancellationToken);
            Advice advice = AdviceReply.Parse(response.Text, request, target, AdviceSource.Claude);
            LastResponse = response;
            return advice;
        }
    }
}
