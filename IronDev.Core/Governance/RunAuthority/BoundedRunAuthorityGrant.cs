namespace IronDev.Core.Governance;

public sealed record BoundedRunAuthorityGrant
{
    public required string GrantId { get; init; }
    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required IReadOnlyCollection<RunAuthorityOperationKind> AllowedOperationKinds { get; init; }
    public required IReadOnlyCollection<string> AllowedFileGlobs { get; init; }
    public required IReadOnlyCollection<string> ForbiddenFileGlobs { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
    public required int MaxMutations { get; init; }
    public required IReadOnlyCollection<BoundedRunAuthorityRequiredValidation> RequiredValidation { get; init; }
    public required IReadOnlyCollection<RunAuthorityOperationKind> StopBeforeOperationKinds { get; init; }
    public required BoundedRunAuthorityGrantedBy GrantedBy { get; init; }
    public required string HumanReadableIntent { get; init; }
}
