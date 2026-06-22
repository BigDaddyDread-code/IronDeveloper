namespace IronDev.Core.Governance.RollbackStatus;

public sealed record RollbackStatusEvaluationRequest
{
    public required string EvaluationId { get; init; }

    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required string SourceApplyReceiptRef { get; init; }

    public RollbackAvailabilityEvidence? Availability { get; init; }
    public RollbackPlanEvidence? Plan { get; init; }
    public RollbackAuthorityEvidence? Authority { get; init; }
    public RollbackRequestEvidence? Request { get; init; }
    public RollbackApplyReceiptEvidence? ApplyReceipt { get; init; }
    public RollbackWorktreeStateEvidence? Worktree { get; init; }
    public RollbackPostStateEvidence? PostState { get; init; }

    public required DateTimeOffset ObservedAtUtc { get; init; }

    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required IReadOnlyCollection<string> ReceiptRefs { get; init; }
}
