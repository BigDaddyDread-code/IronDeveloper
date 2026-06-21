namespace IronDev.Core.Governance;

public sealed record BoundedRunAuthorityGrantDecision
{
    public required bool IsInsideGrantEnvelope { get; init; }
    public required IReadOnlyCollection<string> BlockedReasons { get; init; }
    public required IReadOnlyCollection<string> ForbiddenActions { get; init; }
    public required IReadOnlyCollection<string> RequiredIndependentChecks { get; init; }
}
