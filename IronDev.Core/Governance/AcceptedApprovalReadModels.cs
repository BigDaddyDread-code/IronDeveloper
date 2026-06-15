namespace IronDev.Core.Governance;

public static class AcceptedApprovalReadBoundaryText
{
    public const string AuthorityBoundary = "Accepted approval is necessary but not sufficient for policy satisfaction, source apply, workflow continuation, or release readiness.";

    public static IReadOnlyList<string> Warnings { get; } =
    [
        "Accepted approval read API is not approval creation.",
        "Reading a persisted approval is not policy satisfaction.",
        "Reading a persisted approval is not dry-run execution.",
        "Reading a persisted approval is not patch artifact creation.",
        "Reading a persisted approval is not source apply.",
        "Reading a persisted approval is not workflow continuation.",
        "Reading a persisted approval is not release readiness.",
        "Reading a persisted approval does not authorize execution.",
        "Human review remains required for source apply and memory promotion."
    ];
}

public sealed record AcceptedApprovalReadBoundary
{
    public bool AcceptedApprovalReadIsApprovalCreation { get; init; }
    public bool AcceptedApprovalReadSatisfiesPolicy { get; init; }
    public bool AcceptedApprovalReadRunsDryRun { get; init; }
    public bool AcceptedApprovalReadCreatesPatchArtifact { get; init; }
    public bool AcceptedApprovalReadAppliesSource { get; init; }
    public bool AcceptedApprovalReadContinuesWorkflow { get; init; }
    public bool AcceptedApprovalReadApprovesRelease { get; init; }
    public bool ReadingPersistedApprovalAuthorizesExecution { get; init; }
    public bool HumanReviewRequiredForSourceApply { get; init; } = true;
    public bool HumanReviewRequiredForMemoryPromotion { get; init; } = true;
}

public sealed record AcceptedApprovalReadModel(
    Guid AcceptedApprovalId,
    Guid ProjectId,
    string ApprovalTargetKind,
    string ApprovalTargetId,
    string ApprovalTargetHash,
    string CapabilityCode,
    string ApprovalPurpose,
    string ApprovedByActorId,
    string? ApprovedByActorDisplayName,
    DateTimeOffset AcceptedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    string CorrelationId,
    string CausationId,
    IReadOnlyList<string> EvidenceReferences,
    IReadOnlyList<string> BoundaryMaxims,
    bool IsExpired,
    string AuthorityBoundary,
    AcceptedApprovalReadBoundary Boundary,
    IReadOnlyList<string> Warnings);

public interface IAcceptedApprovalQueryService
{
    Task<AcceptedApprovalReadModel?> GetAsync(
        Guid projectId,
        Guid acceptedApprovalId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AcceptedApprovalReadModel>> ListByTargetAsync(
        Guid projectId,
        string approvalTargetKind,
        string approvalTargetId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AcceptedApprovalReadModel>> ListByProjectAndCorrelationAsync(
        Guid projectId,
        string correlationId,
        CancellationToken cancellationToken = default);
}
