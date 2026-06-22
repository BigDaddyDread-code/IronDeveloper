namespace IronDev.Core.Governance;

public interface IPatchPackageMetadataReadRepository
{
    PatchPackageMetadataReadResult GetByPackageId(
        string packageId,
        FrontendReadinessReadScope scope);
}

public sealed record PatchPackageMetadataReadRecord
{
    public required string PackageId { get; init; }
    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }
    public required IReadOnlyCollection<string> ProposedFilePaths { get; init; }
    public required IReadOnlyCollection<string> ArtifactRefs { get; init; }
    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required IReadOnlyCollection<string> ReceiptRefs { get; init; }
    public required string ReviewSummaryRef { get; init; }
    public required string KnownRisksRef { get; init; }
    public bool IsTenantScoped { get; init; } = true;
    public int? TenantId { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public bool ContainsRawPatchPayload { get; init; }
    public bool ContainsFullDiff { get; init; }
    public bool ContainsPrivateMaterial { get; init; }
    public bool ContainsHiddenMaterial { get; init; }
    public bool ContainsSecretMaterial { get; init; }
    public bool ClaimsSourceApplyAuthority { get; init; }
    public bool ClaimsApproval { get; init; }
    public bool ClaimsPolicySatisfaction { get; init; }
    public bool ClaimsExecution { get; init; }
    public bool ClaimsCommitAuthority { get; init; }
    public bool ClaimsPushAuthority { get; init; }
    public bool ClaimsPullRequestAuthority { get; init; }
    public bool ClaimsContinuation { get; init; }
    public bool ClaimsReleaseOrDeploymentAuthority { get; init; }
    public required IReadOnlyCollection<string> Warnings { get; init; }
    public required IReadOnlyCollection<string> AuthorityWarnings { get; init; }
}

public sealed record PatchPackageMetadataReadResult
{
    public required bool Found { get; init; }
    public FrontendPatchPackageMetadataReadModel? Metadata { get; init; }
    public required IReadOnlyCollection<string> Issues { get; init; }
    public required FrontendReadBoundary Boundary { get; init; }

    public static PatchPackageMetadataReadResult NotFound(params string[] issues) =>
        new()
        {
            Found = false,
            Metadata = null,
            Issues = issues,
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };
}
