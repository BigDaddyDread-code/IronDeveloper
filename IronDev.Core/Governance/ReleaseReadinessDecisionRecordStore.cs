namespace IronDev.Core.Governance;

public interface IReleaseReadinessDecisionRecordStore
{
    Task SaveAsync(
        ReleaseReadinessDecisionRecord record,
        CancellationToken cancellationToken = default);

    Task<ReleaseReadinessDecisionRecord?> GetAsync(
        Guid projectId,
        Guid releaseReadinessDecisionRecordId,
        CancellationToken cancellationToken = default);

    Task<ReleaseReadinessDecisionRecord?> GetByRecordHashAsync(
        Guid projectId,
        string releaseReadinessDecisionRecordHash,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReleaseReadinessDecisionRecord>> ListByReleaseReadinessReportAsync(
        Guid projectId,
        Guid releaseReadinessReportId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReleaseReadinessDecisionRecord>> ListByWorkflowRunAsync(
        Guid projectId,
        string workflowRunId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReleaseReadinessDecisionRecord>> ListBySubjectAsync(
        Guid projectId,
        string subjectKind,
        string subjectId,
        CancellationToken cancellationToken = default);
}
