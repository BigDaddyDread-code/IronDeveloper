namespace IronDev.Core.Governance;

public interface IAcceptedApprovalStore
{
    Task SaveAsync(AcceptedApprovalRecord record, CancellationToken cancellationToken = default);

    Task<AcceptedApprovalRecord?> GetAsync(
        Guid projectId,
        Guid acceptedApprovalId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AcceptedApprovalRecord>> ListByTargetAsync(
        Guid projectId,
        string approvalTargetKind,
        string approvalTargetId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AcceptedApprovalRecord>> ListByCorrelationAsync(
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AcceptedApprovalRecord>> ListByProjectAndCorrelationAsync(
        Guid projectId,
        string correlationId,
        CancellationToken cancellationToken = default);
}
