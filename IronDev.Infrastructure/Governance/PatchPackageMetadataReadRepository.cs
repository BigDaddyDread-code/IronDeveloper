using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class PatchPackageMetadataReadRepository : IPatchPackageMetadataReadRepository
{
    private static readonly string[] AuthorityClaimMarkers =
    [
        "approved by",
        "approval granted",
        "grants approval",
        "accepted approval",
        "has authority",
        "grants authority",
        "authorized to",
        "satisfies policy",
        "policy satisfied",
        "source apply allowed",
        "apply allowed",
        "can execute",
        "may execute",
        "continue workflow",
        "next step may proceed",
        "promotes memory",
        "can release",
        "can deploy",
        "deploy now",
        "can commit",
        "can push",
        "can merge",
        "create pull request"
    ];

    private readonly IReadOnlyList<PatchPackageMetadataReadRecord> _records;

    public PatchPackageMetadataReadRepository()
        : this([])
    {
    }

    public PatchPackageMetadataReadRepository(IEnumerable<PatchPackageMetadataReadRecord> records) =>
        _records = records?.ToArray() ?? throw new ArgumentNullException(nameof(records));

    public PatchPackageMetadataReadResult GetByPackageId(
        string packageId,
        FrontendReadinessReadScope scope)
    {
        var key = Normalize(packageId);
        if (key is null)
            return PatchPackageMetadataReadResult.NotFound("PatchPackageIdRequired");

        var record = _records.FirstOrDefault(candidate =>
            string.Equals(candidate.PackageId, key, StringComparison.OrdinalIgnoreCase));
        if (record is null)
            return PatchPackageMetadataReadResult.NotFound("PatchPackageMetadataNotFound");

        var tenantIssues = ValidateTenant(record, scope);
        if (tenantIssues.Count > 0)
            return PatchPackageMetadataReadResult.NotFound(tenantIssues.ToArray());

        var issues = ValidateRecord(record).ToArray();
        if (issues.Length > 0)
            return Redacted(key, issues);

        return new PatchPackageMetadataReadResult
        {
            Found = true,
            Metadata = new FrontendPatchPackageMetadataReadModel
            {
                PackageId = record.PackageId.Trim(),
                Repository = record.Repository.Trim(),
                Branch = record.Branch.Trim(),
                RunId = record.RunId.Trim(),
                PatchHash = record.PatchHash.Trim(),
                ProposedFilePaths = SafeValues(record.ProposedFilePaths),
                ArtifactRefs = SafeValues(record.ArtifactRefs),
                EvidenceRefs = SafeValues(record.EvidenceRefs),
                ReceiptRefs = SafeValues(record.ReceiptRefs),
                ReviewSummaryRef = record.ReviewSummaryRef.Trim(),
                KnownRisksRef = record.KnownRisksRef.Trim(),
                Boundary = FrontendReadBoundary.ReadOnlyStatus
            },
            Issues = [],
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };
    }

    private static PatchPackageMetadataReadResult Redacted(string packageId, IReadOnlyCollection<string> issues) =>
        new()
        {
            Found = true,
            Metadata = new FrontendPatchPackageMetadataReadModel
            {
                PackageId = packageId,
                Repository = "[redacted]",
                Branch = "[redacted]",
                RunId = "[redacted]",
                PatchHash = "[redacted]",
                ProposedFilePaths = [],
                ArtifactRefs = [],
                EvidenceRefs = [],
                ReceiptRefs = [],
                ReviewSummaryRef = "[redacted]",
                KnownRisksRef = "[redacted]",
                Boundary = FrontendReadBoundary.ReadOnlyStatus
            },
            Issues = issues,
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };

    private static IReadOnlyList<string> ValidateTenant(
        PatchPackageMetadataReadRecord record,
        FrontendReadinessReadScope scope)
    {
        if (!record.IsTenantScoped)
            return [];

        if (!scope.HasTenant)
            return ["TenantScopedPatchPackageMetadataRequiresTenantScope"];

        if (record.TenantId is null or <= 0)
            return ["TenantScopedPatchPackageMetadataRecordTenantRequired"];

        if (record.TenantId.Value != scope.TenantId)
            return ["PatchPackageMetadataTenantMismatch"];

        return [];
    }

    private static IEnumerable<string> ValidateRecord(PatchPackageMetadataReadRecord record)
    {
        if (Normalize(record.PackageId) is null)
            yield return "PatchPackageMetadataPackageIdRequired";

        if (Normalize(record.Repository) is null)
            yield return "PatchPackageMetadataRepositoryRequired";

        if (Normalize(record.Branch) is null)
            yield return "PatchPackageMetadataBranchRequired";

        if (Normalize(record.RunId) is null)
            yield return "PatchPackageMetadataRunIdRequired";

        if (Normalize(record.PatchHash) is null)
            yield return "PatchPackageMetadataPatchHashRequired";

        if (Normalize(record.ReviewSummaryRef) is null)
            yield return "PatchPackageMetadataReviewSummaryRefRequired";

        if (Normalize(record.KnownRisksRef) is null)
            yield return "PatchPackageMetadataKnownRisksRefRequired";

        if (record.ObservedAtUtc == default)
            yield return "PatchPackageMetadataObservedAtRequired";

        if (record.ContainsRawPatchPayload)
            yield return "PatchPackageRawPayloadBlocked";

        if (record.ContainsFullDiff)
            yield return "PatchPackageFullDiffBlocked";

        if (record.ContainsPrivateMaterial)
            yield return "PatchPackagePrivateMaterialBlocked";

        if (record.ContainsHiddenMaterial)
            yield return "PatchPackageHiddenMaterialBlocked";

        if (record.ContainsSecretMaterial)
            yield return "PatchPackageSecretMaterialBlocked";

        if (record.ClaimsSourceApplyAuthority)
            yield return "PatchPackageSourceApplyAuthorityClaimBlocked";

        if (record.ClaimsApproval)
            yield return "PatchPackageApprovalClaimBlocked";

        if (record.ClaimsPolicySatisfaction)
            yield return "PatchPackagePolicySatisfactionClaimBlocked";

        if (record.ClaimsExecution)
            yield return "PatchPackageExecutionClaimBlocked";

        if (record.ClaimsCommitAuthority || record.ClaimsPushAuthority || record.ClaimsPullRequestAuthority)
            yield return "PatchPackageDownstreamAuthorityClaimBlocked";

        if (record.ClaimsContinuation)
            yield return "PatchPackageContinuationClaimBlocked";

        if (record.ClaimsReleaseOrDeploymentAuthority)
            yield return "PatchPackageReleaseDeploymentClaimBlocked";

        if (HasAuthorityClaim(record.Repository) ||
            HasAuthorityClaim(record.Branch) ||
            HasAuthorityClaim(record.RunId) ||
            HasAuthorityClaim(record.PatchHash) ||
            HasAuthorityClaim(record.ReviewSummaryRef) ||
            HasAuthorityClaim(record.KnownRisksRef) ||
            SafeValues(record.ProposedFilePaths).Any(HasAuthorityClaim) ||
            SafeValues(record.ArtifactRefs).Any(HasAuthorityClaim) ||
            SafeValues(record.EvidenceRefs).Any(HasAuthorityClaim) ||
            SafeValues(record.ReceiptRefs).Any(HasAuthorityClaim) ||
            SafeValues(record.Warnings).Any(HasAuthorityClaim) ||
            SafeValues(record.AuthorityWarnings).Any(HasAuthorityClaim))
        {
            yield return "PatchPackageMetadataUnsafeTextBlocked";
        }
    }

    private static bool HasAuthorityClaim(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        return AuthorityClaimMarkers.Any(marker => trimmed.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> SafeValues(IEnumerable<string>? values) =>
        values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
}
