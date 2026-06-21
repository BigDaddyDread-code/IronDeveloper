namespace IronDev.Core.Governance;

public sealed record OperationEligibilityRequest
{
    public required RunAuthorityProfile Profile { get; init; }
    public required BoundedRunAuthorityGrant Grant { get; init; }
    public required RunAuthorityOperationKind OperationKind { get; init; }
    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required IReadOnlyCollection<string> AffectedFilePaths { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public string? PatchHash { get; init; }
    public required int MutationsAlreadyConsumed { get; init; }
    public required int RequestedMutationCount { get; init; }
    public required IReadOnlyCollection<OperationEligibilityValidationEvidence> ValidationEvidence { get; init; }
}
