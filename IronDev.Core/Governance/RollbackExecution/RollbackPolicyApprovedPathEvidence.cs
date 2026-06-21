namespace IronDev.Core.Governance.RollbackExecution;

public sealed record RollbackPolicyApprovedPathEvidence
{
    public required string EvidenceRef { get; init; }

    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required string SourceApplyReceiptRef { get; init; }
    public required string RollbackTargetId { get; init; }

    public required string PolicyId { get; init; }

    public required bool IsPolicyApprovedRollbackPath { get; init; }
    public required bool IsBoundToFailedOrReversibleSourceApply { get; init; }
    public required bool AllowsOnlyCompleteRollback { get; init; }
    public required bool AllowsPartialRollback { get; init; }
    public required bool AllowsDownstreamMutation { get; init; }

    public required DateTimeOffset ApprovedAtUtc { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
}
