namespace IronDev.Core.Governance;

public sealed record BoundedRunAuthorityGrantedBy
{
    public required string PrincipalId { get; init; }
    public required string PrincipalKind { get; init; }
    public required string EvidenceRef { get; init; }
}
