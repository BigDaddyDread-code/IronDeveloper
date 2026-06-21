namespace IronDev.Core.Governance;

public sealed record AuthorityProfileStatusRequest
{
    public required string OperationId { get; init; }
    public required RunAuthorityOperationKind OperationKind { get; init; }
    public required string Subject { get; init; }

    public required AuthorityProfileKind ProfileKind { get; init; }

    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }

    public string? PatchHash { get; init; }

    public required DateTimeOffset ObservedAtUtc { get; init; }

    public OperationEligibilityDecision? EligibilityDecision { get; init; }

    public DateTimeOffset? GrantExpiresAtUtc { get; init; }

    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required IReadOnlyCollection<string> ReceiptRefs { get; init; }
}
