namespace IronDev.Core.Governance;

public interface IControlledDryRunReceiptStore
{
    Task SaveAsync(
        ControlledDryRunExecutionAudit audit,
        CancellationToken cancellationToken = default);

    Task<ControlledDryRunExecutionAudit?> GetAsync(
        Guid projectId,
        Guid dryRunExecutionAuditId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ControlledDryRunExecutionAudit>> ListByRequestAsync(
        Guid projectId,
        Guid controlledDryRunRequestId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ControlledDryRunExecutionAudit>> ListByPolicySatisfactionAsync(
        Guid projectId,
        Guid policySatisfactionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ControlledDryRunExecutionAudit>> ListBySubjectAsync(
        Guid projectId,
        string subjectKind,
        string subjectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ControlledDryRunExecutionAudit>> ListByAuditHashAsync(
        Guid projectId,
        string auditHash,
        CancellationToken cancellationToken = default);
}