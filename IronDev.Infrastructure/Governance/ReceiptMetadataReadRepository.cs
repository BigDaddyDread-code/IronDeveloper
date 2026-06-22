using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class ReceiptMetadataReadRepository : IReceiptMetadataReadRepository
{
    private static readonly string[] RequiredWarnings =
    [
        "Receipt metadata is reference-only.",
        "Receipt ref is not authority.",
        "Receipt ref is not approval.",
        "Receipt ref is not policy satisfaction.",
        "Receipt ref does not authorize execution.",
        "Receipt ref does not continue workflow."
    ];

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
        "can merge"
    ];

    private readonly IReadOnlyList<ReceiptMetadataReadRecord> _records;

    public ReceiptMetadataReadRepository()
        : this([])
    {
    }

    public ReceiptMetadataReadRepository(IEnumerable<ReceiptMetadataReadRecord> records) =>
        _records = records?.ToArray() ?? throw new ArgumentNullException(nameof(records));

    public ReceiptMetadataReadResult GetByReceiptRef(
        string receiptRef,
        FrontendReadinessReadScope scope)
    {
        var key = Normalize(receiptRef);
        if (key is null)
            return ReceiptMetadataReadResult.NotFound("ReceiptRefRequired");

        var record = _records.FirstOrDefault(candidate =>
            string.Equals(candidate.ReceiptRef, key, StringComparison.OrdinalIgnoreCase));
        if (record is null)
            return ReceiptMetadataReadResult.NotFound("ReceiptMetadataNotFound");

        var tenantIssues = ValidateTenant(record, scope);
        if (tenantIssues.Count > 0)
            return ReceiptMetadataReadResult.NotFound(tenantIssues.ToArray());

        var issues = ValidateRecord(record).ToArray();
        if (issues.Length > 0)
            return Redacted(key, issues);

        return new ReceiptMetadataReadResult
        {
            Found = true,
            Metadata = new FrontendReceiptMetadataReadModel
            {
                ReceiptRef = record.ReceiptRef.Trim(),
                ReceiptKind = record.ReceiptKind.Trim(),
                Summary = record.Summary.Trim(),
                ReferenceOnly = true,
                GrantsAuthority = false,
                ContinuesWorkflow = false,
                Warnings = SafeValues(record.Warnings)
                    .Concat(SafeValues(record.AuthorityWarnings))
                    .Concat(RequiredWarnings)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Boundary = FrontendReadBoundary.ReadOnlyStatus
            },
            Issues = [],
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };
    }

    private static ReceiptMetadataReadResult Redacted(string receiptRef, IReadOnlyCollection<string> issues) =>
        new()
        {
            Found = true,
            Metadata = new FrontendReceiptMetadataReadModel
            {
                ReceiptRef = receiptRef,
                ReceiptKind = "RedactedReceiptMetadata",
                Summary = "[redacted: receipt metadata unavailable]",
                ReferenceOnly = true,
                GrantsAuthority = false,
                ContinuesWorkflow = false,
                Warnings = RequiredWarnings
                    .Prepend("Receipt metadata was redacted because it contained unsafe or private material.")
                    .ToArray(),
                Boundary = FrontendReadBoundary.ReadOnlyStatus
            },
            Issues = issues,
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };

    private static IReadOnlyList<string> ValidateTenant(
        ReceiptMetadataReadRecord record,
        FrontendReadinessReadScope scope)
    {
        if (!record.IsTenantScoped)
            return [];

        if (!scope.HasTenant)
            return ["TenantScopedReceiptMetadataRequiresTenantScope"];

        if (record.TenantId is null or <= 0)
            return ["TenantScopedReceiptMetadataRecordTenantRequired"];

        if (record.TenantId.Value != scope.TenantId)
            return ["ReceiptMetadataTenantMismatch"];

        return [];
    }

    private static IEnumerable<string> ValidateRecord(ReceiptMetadataReadRecord record)
    {
        if (Normalize(record.ReceiptRef) is null)
            yield return "ReceiptMetadataRefInvalid";

        if (Normalize(record.ReceiptKind) is null)
            yield return "ReceiptMetadataKindRequired";

        if (Normalize(record.Summary) is null)
            yield return "ReceiptMetadataSummaryRequired";

        if (record.ObservedAtUtc == default)
            yield return "ReceiptMetadataObservedAtRequired";

        if (record.ContainsRawPayload)
            yield return "ReceiptMetadataRawPayloadBlocked";

        if (record.ContainsPrivateMaterial)
            yield return "ReceiptMetadataPrivateMaterialBlocked";

        if (record.ContainsPatchPayload)
            yield return "ReceiptMetadataPatchPayloadBlocked";

        if (record.ContainsHiddenMaterial)
            yield return "ReceiptMetadataHiddenMaterialBlocked";

        if (record.ClaimsAuthority)
            yield return "ReceiptMetadataAuthorityClaimBlocked";

        if (record.ClaimsContinuation)
            yield return "ReceiptMetadataContinuationClaimBlocked";

        if (record.ClaimsApproval)
            yield return "ReceiptMetadataApprovalClaimBlocked";

        if (record.ClaimsPolicySatisfaction)
            yield return "ReceiptMetadataPolicySatisfactionClaimBlocked";

        if (HasAuthorityClaim(record.Summary) ||
            SafeValues(record.Warnings).Any(HasAuthorityClaim) ||
            SafeValues(record.AuthorityWarnings).Any(HasAuthorityClaim))
        {
            yield return "ReceiptMetadataUnsafeTextBlocked";
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
