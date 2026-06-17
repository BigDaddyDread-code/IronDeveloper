using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class ReleaseReadinessDecisionRecordQueryService : IReleaseReadinessDecisionRecordQueryService
{
    private const int DefaultTake = 100;
    private const int MaxTake = 500;
    private const string RedactedUnsafeText = "[redacted: sensitive release readiness decision record text]";

    private static readonly string[] UnsafeTextMarkers =
    [
        "raw prompt",
        "rawprompt",
        "raw completion",
        "rawcompletion",
        "raw tool output",
        "rawtooloutput",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "private reasoning",
        "hidden reasoning",
        "scratchpad",
        "system prompt",
        "developer prompt",
        "password",
        "api_key",
        "apikey",
        "secret",
        "private key",
        "bearer",
        "approval granted",
        "release approved",
        "deployment approved",
        "merge approved",
        "release executed",
        "source applied",
        "rollback executed",
        "workflow continued",
        string.Concat("git ", "committed"),
        string.Concat("git ", "pushed"),
        "pull request created",
        "memory promoted",
        "retrieval activated"
    ];

    private readonly IReleaseReadinessDecisionRecordStore _store;

    public ReleaseReadinessDecisionRecordQueryService(IReleaseReadinessDecisionRecordStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public async Task<ReleaseReadinessDecisionRecordReadModel?> GetAsync(
        Guid projectId,
        Guid releaseReadinessDecisionRecordId,
        CancellationToken cancellationToken = default)
    {
        var record = await _store.GetAsync(projectId, releaseReadinessDecisionRecordId, cancellationToken);
        return record is null ? null : ToReadModel(record);
    }

    public async Task<ReleaseReadinessDecisionRecordReadModel?> GetByRecordHashAsync(
        Guid projectId,
        string releaseReadinessDecisionRecordHash,
        CancellationToken cancellationToken = default)
    {
        var record = await _store.GetByRecordHashAsync(projectId, releaseReadinessDecisionRecordHash, cancellationToken);
        return record is null ? null : ToReadModel(record);
    }

    public async Task<ReleaseReadinessDecisionRecordListReadModel> ListByReleaseReadinessReportAsync(
        Guid projectId,
        Guid releaseReadinessReportId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var records = await _store.ListByReleaseReadinessReportAsync(projectId, releaseReadinessReportId, cancellationToken);
        return ToListReadModel(projectId, records, take);
    }

    public async Task<ReleaseReadinessDecisionRecordListReadModel> ListByWorkflowRunAsync(
        Guid projectId,
        string workflowRunId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var records = await _store.ListByWorkflowRunAsync(projectId, workflowRunId, cancellationToken);
        return ToListReadModel(projectId, records, take);
    }

    public async Task<ReleaseReadinessDecisionRecordListReadModel> ListBySubjectAsync(
        Guid projectId,
        string subjectKind,
        string subjectId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var records = await _store.ListBySubjectAsync(projectId, subjectKind, subjectId, cancellationToken);
        return ToListReadModel(projectId, records, take);
    }

    private static ReleaseReadinessDecisionRecordListReadModel ToListReadModel(
        Guid projectId,
        IReadOnlyList<ReleaseReadinessDecisionRecord> records,
        int take)
    {
        var items = records.Take(NormalizeTake(take)).Select(ToReadModel).ToArray();
        return new ReleaseReadinessDecisionRecordListReadModel
        {
            ProjectId = projectId,
            Records = items,
            Count = items.Length,
            MutationOccurredInThisApi = false,
            ReleaseReadinessGateRanByThisApi = false,
            ReleaseApprovedByThisApi = false,
            DeploymentApprovedByThisApi = false,
            MergeApprovedByThisApi = false,
            ReleaseExecutedByThisApi = false,
            SourceApplyExecutedByThisApi = false,
            RollbackExecutedByThisApi = false,
            WorkflowContinuedByThisApi = false,
            GitOperationExecutedByThisApi = false,
            HumanReviewRequired = true
        };
    }

    private static int NormalizeTake(int take) =>
        take <= 0 ? DefaultTake : Math.Min(take, MaxTake);

    private static ReleaseReadinessDecisionRecordReadModel ToReadModel(ReleaseReadinessDecisionRecord record) => new()
    {
        ReleaseReadinessDecisionRecordId = record.ReleaseReadinessDecisionRecordId,
        ProjectId = record.ProjectId,
        ReleaseReadinessReportId = record.ReleaseReadinessReportId,
        ReleaseReadinessReportHash = SafeText(record.ReleaseReadinessReportHash),
        WorkflowRunId = SafeText(record.WorkflowRunId),
        WorkflowStepId = SafeText(record.WorkflowStepId),
        SubjectKind = SafeText(record.SubjectKind),
        SubjectId = SafeText(record.SubjectId),
        SubjectHash = SafeText(record.SubjectHash),
        DecisionStatus = SafeText(record.DecisionStatus),
        ReleaseReadinessEvidenceSatisfied = record.ReleaseReadinessEvidenceSatisfied,
        ReleaseApproved = record.ReleaseApproved,
        DeploymentApproved = record.DeploymentApproved,
        MergeApproved = record.MergeApproved,
        SourceApplyExecutedByDecision = record.SourceApplyExecutedByDecision,
        RollbackExecutedByDecision = record.RollbackExecutedByDecision,
        WorkflowMutatedByDecision = record.WorkflowMutatedByDecision,
        GitOperationExecutedByDecision = record.GitOperationExecutedByDecision,
        ReleaseExecutedByDecision = record.ReleaseExecutedByDecision,
        HumanReviewRequiredForReleaseApproval = record.HumanReviewRequiredForReleaseApproval,
        HumanReviewRequiredForDeployment = record.HumanReviewRequiredForDeployment,
        HumanReviewRequiredForMerge = record.HumanReviewRequiredForMerge,
        Reasons = record.Reasons.Select(ToReasonReadModel).ToArray(),
        EvidenceReferences = record.EvidenceReferences.Select(SafeText).ToArray(),
        BoundaryMaxims = record.BoundaryMaxims.Select(SafeText).ToArray(),
        DecidedAtUtc = record.DecidedAtUtc,
        ReleaseReadinessDecisionRecordHash = SafeText(record.ReleaseReadinessDecisionRecordHash),
        Boundary = SafeText(record.Boundary),
        AuthorityBoundary = ReleaseReadinessDecisionRecordReadBoundaryText.AuthorityBoundary,
        Warnings = ReleaseReadinessDecisionRecordReadBoundaryText.Warnings,
        MutationOccurredInThisApi = false,
        ReleaseReadinessGateRanByThisApi = false,
        ReleaseApprovedByThisApi = false,
        DeploymentApprovedByThisApi = false,
        MergeApprovedByThisApi = false,
        ReleaseExecutedByThisApi = false,
        SourceApplyExecutedByThisApi = false,
        RollbackExecutedByThisApi = false,
        WorkflowContinuedByThisApi = false,
        GitOperationExecutedByThisApi = false,
        HumanReviewRequired = true
    };

    private static ReleaseReadinessDecisionReasonReadModel ToReasonReadModel(ReleaseReadinessDecisionReason reason) => new()
    {
        Code = SafeText(reason.Code),
        Severity = SafeText(reason.Severity),
        Field = SafeText(reason.Field),
        Message = SafeText(reason.Message)
    };

    private static string SafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return UnsafeTextMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            ? RedactedUnsafeText
            : value.Trim();
    }
}
