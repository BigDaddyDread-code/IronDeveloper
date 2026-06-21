namespace IronDev.Core.Governance;

public sealed record SourceApplyAuthorityDecision
{
    public required bool IsEligibleForControlledSourceApply { get; init; }

    public required SourceApplyAuthorityPath AuthorityPath { get; init; }

    public required IReadOnlyCollection<string> BlockedReasons { get; init; }
    public required IReadOnlyCollection<string> MissingEvidence { get; init; }
    public required IReadOnlyCollection<string> ForbiddenActions { get; init; }
    public required IReadOnlyCollection<string> RequiredIndependentChecks { get; init; }

    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required IReadOnlyCollection<string> ReceiptRefs { get; init; }
}
