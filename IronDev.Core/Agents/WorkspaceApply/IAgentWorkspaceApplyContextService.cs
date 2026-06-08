namespace IronDev.Core.Agents.WorkspaceApply;

public interface IAgentWorkspaceApplyContextService
{
    Task<AgentWorkspaceApplyContext> CreateAsync(
        AgentWorkspaceApplyContextRequest request,
        CancellationToken cancellationToken = default);
}
