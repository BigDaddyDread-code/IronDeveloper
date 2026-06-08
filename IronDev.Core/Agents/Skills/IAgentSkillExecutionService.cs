namespace IronDev.Core.Agents.Skills;

public interface IAgentSkillExecutionService
{
    Task<AgentSkillExecutionResult> ExecuteAsync(
        AgentSkillExecutionRequest request,
        CancellationToken cancellationToken = default);
}
