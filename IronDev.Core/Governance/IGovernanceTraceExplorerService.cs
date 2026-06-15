namespace IronDev.Core.Governance;

public interface IGovernanceTraceExplorerService
{
    Task<GovernanceTraceListResponse> SearchAsync(
        GovernanceTraceQuery query,
        CancellationToken cancellationToken = default);

    Task<GovernanceTraceDetailResponse> GetByTraceIdAsync(
        string traceId,
        CancellationToken cancellationToken = default);

    Task<GovernanceTraceDetailResponse> GetByCorrelationIdAsync(
        string correlationId,
        string projectReferenceId = "",
        CancellationToken cancellationToken = default);

    Task<GovernanceTraceDetailResponse> GetByWorkflowRunIdAsync(
        string workflowRunId,
        string projectReferenceId = "",
        CancellationToken cancellationToken = default);
}
