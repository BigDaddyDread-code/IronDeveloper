namespace IronDev.Core.Agents;

public interface IAgentRunHealthSummaryService
{
    Task<AgentRunHealthSummaryResponse> GetSummaryAsync(
        AgentRunHealthSummaryRequest request,
        CancellationToken cancellationToken = default);
}
