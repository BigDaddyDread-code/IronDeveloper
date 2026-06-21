namespace IronDev.Core.Governance;

public sealed record BoundedRunAuthorityRequiredValidation
{
    public required string ValidationKind { get; init; }
    public required bool MustPass { get; init; }
    public required IReadOnlyCollection<string> EvidenceRefPrefixes { get; init; }
}
