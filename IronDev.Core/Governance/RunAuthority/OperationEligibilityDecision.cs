namespace IronDev.Core.Governance;

public sealed record OperationEligibilityDecision
{
    public required bool IsEligibleUnderProfileAndGrant { get; init; }
    public required RunAuthorityOperationKind OperationKind { get; init; }
    public required IReadOnlyCollection<string> BlockedReasons { get; init; }
    public required IReadOnlyCollection<string> MissingEvidence { get; init; }
    public required IReadOnlyCollection<string> ForbiddenActions { get; init; }
    public required IReadOnlyCollection<string> RequiredIndependentChecks { get; init; }
}
