namespace IronDev.Core.Governance;

public enum PatchProposalStatusKind
{
    ReadyForReview = 1,
    Blocked = 2,
    Failed = 3,
    Expired = 4
}

public sealed record PatchProposalStatusInput
{
    public required string OperationId { get; init; }
    public required string ProposalId { get; init; }
    public required string PatchHash { get; init; }
    public required string Subject { get; init; }

    public required PatchProposalStatusKind StatusKind { get; init; }

    public required IReadOnlyList<string> ArtifactRefs { get; init; }
    public required IReadOnlyList<string> ValidationRefs { get; init; }
    public required IReadOnlyList<string> BlockedReasons { get; init; }
    public required IReadOnlyList<string> MissingEvidence { get; init; }
    public required IReadOnlyList<string> ForbiddenActions { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed record PatchProposalGovernedOperationStatusMappingResult
{
    public required GovernedOperationStatus Status { get; init; }
    public required GovernedOperationStatusValidationResult CanonicalValidation { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> RedFlags { get; init; }
    public required bool IsValid { get; init; }
}
