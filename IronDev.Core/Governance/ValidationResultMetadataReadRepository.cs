namespace IronDev.Core.Governance;

public interface IValidationResultMetadataReadRepository
{
    ValidationResultMetadataReadResult GetByValidationResultId(
        string validationResultId,
        FrontendReadinessReadScope scope);
}

public sealed record ValidationResultMetadataReadRecord
{
    public required string ValidationResultId { get; init; }
    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }
    public required string Outcome { get; init; }
    public required IReadOnlyCollection<string> WhatRan { get; init; }
    public required IReadOnlyCollection<string> WhatPassed { get; init; }
    public required IReadOnlyCollection<string> WhatFailed { get; init; }
    public required IReadOnlyCollection<string> WhatWasSkipped { get; init; }
    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required IReadOnlyCollection<string> ReceiptRefs { get; init; }
    public bool FreshnessKnown { get; init; }
    public bool IsStale { get; init; }
    public bool IsTenantScoped { get; init; } = true;
    public int? TenantId { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public bool ContainsRawLogPayload { get; init; }
    public bool ContainsRawCommandOutput { get; init; }
    public bool ContainsRawTestOutput { get; init; }
    public bool ContainsRawBuildOutput { get; init; }
    public bool ContainsPatchPayload { get; init; }
    public bool ContainsFullDiff { get; init; }
    public bool ContainsPrivateMaterial { get; init; }
    public bool ContainsHiddenMaterial { get; init; }
    public bool ContainsSecretMaterial { get; init; }
    public bool ClaimsApproval { get; init; }
    public bool ClaimsPolicySatisfaction { get; init; }
    public bool ClaimsSourceApplyAuthority { get; init; }
    public bool ClaimsExecution { get; init; }
    public bool ClaimsCommitAuthority { get; init; }
    public bool ClaimsPushAuthority { get; init; }
    public bool ClaimsPullRequestAuthority { get; init; }
    public bool ClaimsContinuation { get; init; }
    public bool ClaimsReleaseOrDeploymentAuthority { get; init; }
    public required IReadOnlyCollection<string> Warnings { get; init; }
    public required IReadOnlyCollection<string> AuthorityWarnings { get; init; }
}

public sealed record ValidationResultMetadataReadResult
{
    public required bool Found { get; init; }
    public FrontendValidationResultMetadataReadModel? Metadata { get; init; }
    public required IReadOnlyCollection<string> Issues { get; init; }
    public required FrontendReadBoundary Boundary { get; init; }

    public static ValidationResultMetadataReadResult NotFound(params string[] issues) =>
        new()
        {
            Found = false,
            Metadata = null,
            Issues = issues,
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };
}
