using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class ValidationResultMetadataReadRepository : IValidationResultMetadataReadRepository
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

    private readonly IReadOnlyList<ValidationResultMetadataReadRecord> _records;

    public ValidationResultMetadataReadRepository()
        : this([])
    {
    }

    public ValidationResultMetadataReadRepository(IEnumerable<ValidationResultMetadataReadRecord> records) =>
        _records = records?.ToArray() ?? throw new ArgumentNullException(nameof(records));

    public ValidationResultMetadataReadResult GetByValidationResultId(
        string validationResultId,
        FrontendReadinessReadScope scope)
    {
        var key = Normalize(validationResultId);
        if (key is null)
            return ValidationResultMetadataReadResult.NotFound("ValidationResultIdRequired");

        var record = _records.FirstOrDefault(candidate =>
            string.Equals(candidate.ValidationResultId, key, StringComparison.OrdinalIgnoreCase));
        if (record is null)
            return ValidationResultMetadataReadResult.NotFound("ValidationResultMetadataNotFound");

        var tenantIssues = ValidateTenant(record, scope);
        if (tenantIssues.Count > 0)
            return ValidationResultMetadataReadResult.NotFound(tenantIssues.ToArray());

        var issues = ValidateRecord(record).ToArray();
        if (issues.Length > 0)
            return Redacted(key, issues);

        return new ValidationResultMetadataReadResult
        {
            Found = true,
            Metadata = new FrontendValidationResultMetadataReadModel
            {
                ValidationResultId = record.ValidationResultId.Trim(),
                Repository = record.Repository.Trim(),
                Branch = record.Branch.Trim(),
                RunId = record.RunId.Trim(),
                PatchHash = record.PatchHash.Trim(),
                Outcome = record.Outcome.Trim(),
                WhatRan = SafeValues(record.WhatRan),
                WhatPassed = SafeValues(record.WhatPassed),
                WhatFailed = SafeValues(record.WhatFailed),
                WhatWasSkipped = SkippedValues(record),
                IsStale = record.IsStale || !record.FreshnessKnown || IsExpired(record.ExpiresAtUtc),
                EvidenceRefs = SafeValues(record.EvidenceRefs),
                ReceiptRefs = SafeValues(record.ReceiptRefs),
                Boundary = FrontendReadBoundary.ReadOnlyStatus
            },
            Issues = [],
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };
    }

    private static ValidationResultMetadataReadResult Redacted(string validationResultId, IReadOnlyCollection<string> issues) =>
        new()
        {
            Found = true,
            Metadata = new FrontendValidationResultMetadataReadModel
            {
                ValidationResultId = validationResultId,
                Repository = "[redacted]",
                Branch = "[redacted]",
                RunId = "[redacted]",
                PatchHash = "[redacted]",
                Outcome = "UnsafeValidationMetadata",
                WhatRan = [],
                WhatPassed = [],
                WhatFailed = [],
                WhatWasSkipped = ["ValidationMetadataUnsafe"],
                IsStale = true,
                EvidenceRefs = [],
                ReceiptRefs = [],
                Boundary = FrontendReadBoundary.ReadOnlyStatus
            },
            Issues = issues,
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };

    private static IReadOnlyList<string> ValidateTenant(
        ValidationResultMetadataReadRecord record,
        FrontendReadinessReadScope scope)
    {
        if (!record.IsTenantScoped)
            return [];

        if (!scope.HasTenant)
            return ["TenantScopedValidationResultMetadataRequiresTenantScope"];

        if (record.TenantId is null or <= 0)
            return ["TenantScopedValidationResultMetadataRecordTenantRequired"];

        if (record.TenantId.Value != scope.TenantId)
            return ["ValidationResultMetadataTenantMismatch"];

        return [];
    }

    private static IEnumerable<string> ValidateRecord(ValidationResultMetadataReadRecord record)
    {
        if (Normalize(record.ValidationResultId) is null)
            yield return "ValidationResultMetadataValidationResultIdRequired";

        if (Normalize(record.Repository) is null)
            yield return "ValidationResultMetadataRepositoryRequired";

        if (Normalize(record.Branch) is null)
            yield return "ValidationResultMetadataBranchRequired";

        if (Normalize(record.RunId) is null)
            yield return "ValidationResultMetadataRunIdRequired";

        if (Normalize(record.PatchHash) is null)
            yield return "ValidationResultMetadataPatchHashRequired";

        if (Normalize(record.Outcome) is null)
            yield return "ValidationResultMetadataOutcomeRequired";

        if (record.ObservedAtUtc == default)
            yield return "ValidationResultMetadataObservedAtRequired";

        if (record.ContainsRawLogPayload)
            yield return "ValidationResultRawLogPayloadBlocked";

        if (record.ContainsRawCommandOutput)
            yield return "ValidationResultRawCommandOutputBlocked";

        if (record.ContainsRawTestOutput)
            yield return "ValidationResultRawTestOutputBlocked";

        if (record.ContainsRawBuildOutput)
            yield return "ValidationResultRawBuildOutputBlocked";

        if (record.ContainsPatchPayload)
            yield return "ValidationResultPatchPayloadBlocked";

        if (record.ContainsFullDiff)
            yield return "ValidationResultFullDiffBlocked";

        if (record.ContainsPrivateMaterial)
            yield return "ValidationResultPrivateMaterialBlocked";

        if (record.ContainsHiddenMaterial)
            yield return "ValidationResultHiddenMaterialBlocked";

        if (record.ContainsSecretMaterial)
            yield return "ValidationResultSecretMaterialBlocked";

        if (record.ClaimsApproval)
            yield return "ValidationResultApprovalClaimBlocked";

        if (record.ClaimsPolicySatisfaction)
            yield return "ValidationResultPolicySatisfactionClaimBlocked";

        if (record.ClaimsSourceApplyAuthority)
            yield return "ValidationResultSourceApplyAuthorityClaimBlocked";

        if (record.ClaimsExecution)
            yield return "ValidationResultExecutionClaimBlocked";

        if (record.ClaimsCommitAuthority || record.ClaimsPushAuthority || record.ClaimsPullRequestAuthority)
            yield return "ValidationResultDownstreamAuthorityClaimBlocked";

        if (record.ClaimsContinuation)
            yield return "ValidationResultContinuationClaimBlocked";

        if (record.ClaimsReleaseOrDeploymentAuthority)
            yield return "ValidationResultReleaseDeploymentClaimBlocked";

        if (HasAuthorityClaim(record.Repository) ||
            HasAuthorityClaim(record.Branch) ||
            HasAuthorityClaim(record.RunId) ||
            HasAuthorityClaim(record.PatchHash) ||
            HasAuthorityClaim(record.Outcome) ||
            SafeValues(record.WhatRan).Any(HasAuthorityClaim) ||
            SafeValues(record.WhatPassed).Any(HasAuthorityClaim) ||
            SafeValues(record.WhatFailed).Any(HasAuthorityClaim) ||
            SafeValues(record.WhatWasSkipped).Any(HasAuthorityClaim) ||
            SafeValues(record.EvidenceRefs).Any(HasAuthorityClaim) ||
            SafeValues(record.ReceiptRefs).Any(HasAuthorityClaim) ||
            SafeValues(record.Warnings).Any(HasAuthorityClaim) ||
            SafeValues(record.AuthorityWarnings).Any(HasAuthorityClaim))
        {
            yield return "ValidationResultMetadataUnsafeTextBlocked";
        }
    }

    private static IReadOnlyList<string> SkippedValues(ValidationResultMetadataReadRecord record)
    {
        var values = SafeValues(record.WhatWasSkipped).ToList();
        if (!record.FreshnessKnown)
            values.Add("FreshnessUnknown");

        return values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsExpired(DateTimeOffset? expiresAtUtc) =>
        expiresAtUtc.HasValue && expiresAtUtc.Value <= DateTimeOffset.UtcNow;

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
