namespace IronDev.Core.Governance;

public enum ControlledSourceApplyStatusKind
{
    Blocked = 1,
    Eligible = 2,
    Running = 3,
    Completed = 4,
    Failed = 5,
    Expired = 6
}

public sealed record ControlledSourceApplyStatusInput
{
    public required string OperationId { get; init; }
    public required string SourceApplyId { get; init; }
    public required string Subject { get; init; }

    public required string RepoId { get; init; }
    public required string Branch { get; init; }
    public required string PatchHash { get; init; }

    public required ControlledSourceApplyStatusKind StatusKind { get; init; }

    public required IReadOnlyList<string> EvidenceRefs { get; init; }
    public required IReadOnlyList<string> ReceiptRefs { get; init; }
    public required IReadOnlyList<string> BlockedReasons { get; init; }
    public required IReadOnlyList<string> MissingEvidence { get; init; }
    public required IReadOnlyList<string> ForbiddenActions { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed record ControlledSourceApplyGovernedOperationStatusMappingResult
{
    public required GovernedOperationStatus Status { get; init; }
    public required GovernedOperationStatusValidationResult CanonicalValidation { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> RedFlags { get; init; }
    public required bool IsValid { get; init; }
}
