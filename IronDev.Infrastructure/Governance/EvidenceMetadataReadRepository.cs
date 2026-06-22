using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class EvidenceMetadataReadRepository : IEvidenceMetadataReadRepository
{
    private static readonly string[] RequiredWarnings =
    [
        "Evidence metadata is reference-only.",
        "Evidence ref is not approval.",
        "Evidence ref is not authority.",
        "Evidence ref is not policy satisfaction.",
        "Evidence ref does not authorize execution or workflow continuation."
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
        "workflow continuation",
        "promotes memory",
        "is release candidate",
        "can deploy",
        "deploy now"
    ];

    private readonly IReadOnlyList<EvidenceMetadataReadRecord> _records;

    public EvidenceMetadataReadRepository()
        : this([])
    {
    }

    public EvidenceMetadataReadRepository(IEnumerable<EvidenceMetadataReadRecord> records) =>
        _records = records?.ToArray() ?? throw new ArgumentNullException(nameof(records));

    public EvidenceMetadataReadResult GetByEvidenceRef(
        string evidenceRef,
        FrontendReadinessReadScope scope)
    {
        var key = Normalize(evidenceRef);
        if (key is null)
            return EvidenceMetadataReadResult.NotFound("EvidenceRefRequired");

        var record = _records.FirstOrDefault(candidate =>
            string.Equals(candidate.EvidenceRef, key, StringComparison.OrdinalIgnoreCase));
        if (record is null)
            return EvidenceMetadataReadResult.NotFound("EvidenceMetadataNotFound");

        var tenantIssues = ValidateTenant(record, scope);
        if (tenantIssues.Count > 0)
            return EvidenceMetadataReadResult.NotFound(tenantIssues.ToArray());

        var issues = ValidateRecord(record).ToArray();
        if (issues.Length > 0)
            return Redacted(key, issues);

        return new EvidenceMetadataReadResult
        {
            Found = true,
            Metadata = new FrontendEvidenceMetadataReadModel
            {
                EvidenceRef = record.EvidenceRef.Trim(),
                EvidenceKind = record.EvidenceKind.Trim(),
                Summary = record.Summary.Trim(),
                ReferenceOnly = true,
                ContainsRawPayload = false,
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

    private static EvidenceMetadataReadResult Redacted(string evidenceRef, IReadOnlyCollection<string> issues) =>
        new()
        {
            Found = true,
            Metadata = new FrontendEvidenceMetadataReadModel
            {
                EvidenceRef = evidenceRef,
                EvidenceKind = "RedactedEvidenceMetadata",
                Summary = "[redacted: evidence metadata unavailable]",
                ReferenceOnly = true,
                ContainsRawPayload = false,
                Warnings = RequiredWarnings
                    .Prepend("Evidence metadata was redacted because it contained unsafe or private material.")
                    .ToArray(),
                Boundary = FrontendReadBoundary.ReadOnlyStatus
            },
            Issues = issues,
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };

    private static IReadOnlyList<string> ValidateTenant(
        EvidenceMetadataReadRecord record,
        FrontendReadinessReadScope scope)
    {
        if (!record.IsTenantScoped)
            return [];

        if (!scope.HasTenant)
            return ["TenantScopedEvidenceMetadataRequiresTenantScope"];

        if (record.TenantId is null or <= 0)
            return ["TenantScopedEvidenceMetadataRecordTenantRequired"];

        if (record.TenantId.Value != scope.TenantId)
            return ["EvidenceMetadataTenantMismatch"];

        return [];
    }

    private static IEnumerable<string> ValidateRecord(EvidenceMetadataReadRecord record)
    {
        if (Normalize(record.EvidenceRef) is null)
            yield return "EvidenceMetadataRefInvalid";

        if (Normalize(record.EvidenceKind) is null)
            yield return "EvidenceMetadataKindRequired";

        if (Normalize(record.Summary) is null)
            yield return "EvidenceMetadataSummaryRequired";

        if (record.ObservedAtUtc == default)
            yield return "EvidenceMetadataObservedAtRequired";

        if (record.ContainsRawPayload)
            yield return "EvidenceMetadataRawPayloadBlocked";

        if (record.ContainsPrivateMaterial)
            yield return "EvidenceMetadataPrivateMaterialBlocked";

        if (record.ContainsPatchPayload)
            yield return "EvidenceMetadataPatchPayloadBlocked";

        if (record.ContainsHiddenMaterial)
            yield return "EvidenceMetadataHiddenMaterialBlocked";

        if (HasAuthorityClaim(record.Summary) ||
            SafeValues(record.Warnings).Any(HasAuthorityClaim) ||
            SafeValues(record.AuthorityWarnings).Any(HasAuthorityClaim))
        {
            yield return "EvidenceMetadataAuthorityClaimBlocked";
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
