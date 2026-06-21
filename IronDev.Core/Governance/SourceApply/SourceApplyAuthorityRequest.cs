namespace IronDev.Core.Governance;

public sealed record SourceApplyAuthorityRequest
{
    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required IReadOnlyCollection<string> AffectedFilePaths { get; init; }

    public required DateTimeOffset ObservedAtUtc { get; init; }

    public AcceptedSourceApplyRequestEvidence? AcceptedApplyRequest { get; init; }

    public BoundedRunAuthorityGrant? BoundedRunAuthorityGrant { get; init; }

    public required IReadOnlyCollection<OperationEligibilityValidationEvidence> ValidationEvidence { get; init; }

    public required int MutationsAlreadyConsumed { get; init; }
    public required int RequestedMutationCount { get; init; }

    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required IReadOnlyCollection<string> ReceiptRefs { get; init; }
}
