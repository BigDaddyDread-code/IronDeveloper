namespace IronDev.Core.AgentMemory;

public interface IConscienceMemoryGovernanceService
{
    Task<MemoryGovernanceCheckResult> CheckAsync(
        MemoryGovernanceCheckRequest request,
        CancellationToken cancellationToken = default);
}
