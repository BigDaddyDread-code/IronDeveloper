namespace IronDev.Core.Governance;

public sealed record RunAuthorityDecision
{
    public required RunAuthorityProfileKind ProfileKind { get; init; }
    public required RunAuthorityOperationKind RequestedOperation { get; init; }
    public required bool IsAllowedByProfile { get; init; }
    public required IReadOnlyCollection<string> BlockedReasons { get; init; }
    public required IReadOnlyCollection<string> ForbiddenActions { get; init; }
    public required IReadOnlyCollection<string> RequiredIndependentChecks { get; init; }
}
