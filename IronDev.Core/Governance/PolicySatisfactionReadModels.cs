namespace IronDev.Core.Governance;

public static class PolicySatisfactionReadBoundaryText
{
    public const string AuthorityBoundary = "Persisted policy satisfaction is not dry-run execution, patch artifact creation, source apply, rollback, workflow continuation, or release readiness.";

    public static IReadOnlyList<string> Warnings { get; } =
    [
        "Policy satisfaction read API is not policy satisfaction creation.",
        "Persisted policy satisfaction is not dry-run execution.",
        "Persisted policy satisfaction is not patch artifact creation.",
        "Persisted policy satisfaction is not source apply.",
        "Persisted policy satisfaction is not rollback.",
        "Persisted policy satisfaction is not workflow continuation.",
        "Persisted policy satisfaction is not release readiness.",
        "Reading persisted policy satisfaction is not dry-run execution.",
        "Reading persisted policy satisfaction is not patch artifact creation.",
        "Reading persisted policy satisfaction is not source apply.",
        "Reading persisted policy satisfaction is not rollback.",
        "Reading persisted policy satisfaction is not workflow continuation.",
        "Reading persisted policy satisfaction is not release readiness.",
        "Reading persisted policy satisfaction does not authorize execution by itself.",
        "Human review remains required for source apply and memory promotion."
    ];
}

public sealed record PolicySatisfactionReadBoundary
{
    public bool PolicySatisfactionReadIsCreation { get; init; }
    public bool ReadingPersistedPolicySatisfactionRunsDryRun { get; init; }
    public bool ReadingPersistedPolicySatisfactionCreatesPatchArtifact { get; init; }
    public bool ReadingPersistedPolicySatisfactionAppliesSource { get; init; }
    public bool ReadingPersistedPolicySatisfactionExecutesRollback { get; init; }
    public bool ReadingPersistedPolicySatisfactionContinuesWorkflow { get; init; }
    public bool ReadingPersistedPolicySatisfactionApprovesRelease { get; init; }
    public bool ReadingPersistedPolicySatisfactionAuthorizesExecution { get; init; }
    public bool HumanReviewRequiredForSourceApply { get; init; } = true;
    public bool HumanReviewRequiredForMemoryPromotion { get; init; } = true;
}

public sealed record PolicySatisfactionReadModel(
    Guid PolicySatisfactionId,
    Guid ProjectId,
    string PolicyCode,
    string PolicyVersion,
    string SubjectKind,
    string SubjectId,
    string SubjectHash,
    string CapabilityCode,
    Guid AcceptedApprovalId,
    string ApprovalRequirementHash,
    DateTimeOffset ApprovalEvaluatedAtUtc,
    DateTimeOffset SatisfiedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    string CorrelationId,
    string CausationId,
    IReadOnlyList<string> EvidenceReferences,
    IReadOnlyList<string> BoundaryMaxims,
    bool IsExpired,
    string AuthorityBoundary,
    PolicySatisfactionReadBoundary Boundary,
    IReadOnlyList<string> Warnings);

public interface IPolicySatisfactionQueryService
{
    Task<PolicySatisfactionReadModel?> GetAsync(
        Guid projectId,
        Guid policySatisfactionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolicySatisfactionReadModel>> ListBySubjectAsync(
        Guid projectId,
        string subjectKind,
        string subjectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolicySatisfactionReadModel>> ListByAcceptedApprovalAsync(
        Guid projectId,
        Guid acceptedApprovalId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolicySatisfactionReadModel>> ListByProjectAndCorrelationAsync(
        Guid projectId,
        string correlationId,
        CancellationToken cancellationToken = default);
}
