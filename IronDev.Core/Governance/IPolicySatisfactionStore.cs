namespace IronDev.Core.Governance;

public interface IPolicySatisfactionStore
{
    Task SaveAsync(PolicySatisfactionRecord record, CancellationToken cancellationToken = default);

    Task<PolicySatisfactionRecord?> GetAsync(
        Guid projectId,
        Guid policySatisfactionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolicySatisfactionRecord>> ListBySubjectAsync(
        Guid projectId,
        string subjectKind,
        string subjectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolicySatisfactionRecord>> ListByAcceptedApprovalAsync(
        Guid projectId,
        Guid acceptedApprovalId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolicySatisfactionRecord>> ListByProjectAndCorrelationAsync(
        Guid projectId,
        string correlationId,
        CancellationToken cancellationToken = default);
}
