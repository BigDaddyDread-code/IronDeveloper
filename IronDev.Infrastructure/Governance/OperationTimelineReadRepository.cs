using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class OperationTimelineReadRepository : IOperationTimelineReadRepository
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

    private readonly IReadOnlyList<OperationTimelineEventReadRecord> _records;

    public OperationTimelineReadRepository()
        : this([])
    {
    }

    public OperationTimelineReadRepository(IEnumerable<OperationTimelineEventReadRecord> records) =>
        _records = records?.ToArray() ?? throw new ArgumentNullException(nameof(records));

    public OperationTimelineReadResult GetByOperationId(
        string operationId,
        FrontendReadinessReadScope scope)
    {
        var key = Normalize(operationId);
        if (key is null)
            return OperationTimelineReadResult.NotFound("OperationIdRequired");

        var records = _records
            .Where(candidate => string.Equals(candidate.OperationId, key, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (records.Length == 0)
            return OperationTimelineReadResult.NotFound("OperationTimelineNotFound");

        var entries = new List<FrontendTimelineEntry>();
        var issues = new List<string>();

        foreach (var record in records)
        {
            var tenantIssues = ValidateTenant(record, scope);
            if (tenantIssues.Count > 0)
            {
                issues.AddRange(tenantIssues);
                continue;
            }

            var recordIssues = ValidateRecord(record).ToArray();
            if (recordIssues.Length > 0)
            {
                issues.AddRange(recordIssues);
                entries.Add(RedactedEntry(record));
                continue;
            }

            entries.Add(new FrontendTimelineEntry
            {
                EntryId = record.EntryId.Trim(),
                EventKind = record.EventKind.Trim(),
                Summary = record.Summary.Trim(),
                EvidenceRefs = SafeValues(record.EvidenceRefs),
                ReceiptRefs = SafeValues(record.ReceiptRefs),
                ObservedAtUtc = record.ObservedAtUtc
            });
        }

        if (entries.Count == 0)
            return OperationTimelineReadResult.NotFound(
                issues.Count == 0 ? ["OperationTimelineNotFound"] : issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

        var ordered = entries
            .OrderBy(entry => entry.ObservedAtUtc)
            .ThenBy(entry => entry.EntryId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new OperationTimelineReadResult
        {
            Found = true,
            Timeline = new FrontendOperationTimelineReadModel
            {
                OperationId = key,
                Entries = ordered,
                Boundary = FrontendReadBoundary.ReadOnlyStatus
            },
            Issues = issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };
    }

    private static FrontendTimelineEntry RedactedEntry(OperationTimelineEventReadRecord record) =>
        new()
        {
            EntryId = string.IsNullOrWhiteSpace(record.EntryId) ? "redacted-timeline-event" : record.EntryId.Trim(),
            EventKind = "RedactedTimelineEvent",
            Summary = "[redacted: timeline event unavailable]",
            EvidenceRefs = [],
            ReceiptRefs = [],
            ObservedAtUtc = record.ObservedAtUtc == default ? DateTimeOffset.UnixEpoch : record.ObservedAtUtc
        };

    private static IReadOnlyList<string> ValidateTenant(
        OperationTimelineEventReadRecord record,
        FrontendReadinessReadScope scope)
    {
        if (!record.IsTenantScoped)
            return [];

        if (!scope.HasTenant)
            return ["TenantScopedTimelineEventRequiresTenantScope"];

        if (record.TenantId is null or <= 0)
            return ["TenantScopedTimelineEventRecordTenantRequired"];

        if (record.TenantId.Value != scope.TenantId)
            return ["TimelineEventTenantMismatch"];

        return [];
    }

    private static IEnumerable<string> ValidateRecord(OperationTimelineEventReadRecord record)
    {
        if (Normalize(record.OperationId) is null)
            yield return "TimelineEventOperationIdRequired";

        if (Normalize(record.EntryId) is null)
            yield return "TimelineEventEntryIdRequired";

        if (Normalize(record.EventKind) is null)
            yield return "TimelineEventKindRequired";

        if (Normalize(record.Summary) is null)
            yield return "TimelineEventSummaryRequired";

        if (record.ObservedAtUtc == default)
            yield return "TimelineEventObservedAtRequired";

        if (record.ContainsRawPayload)
            yield return "TimelineEventRawPayloadBlocked";

        if (record.ContainsPrivateMaterial)
            yield return "TimelineEventPrivateMaterialBlocked";

        if (record.ContainsPatchPayload)
            yield return "TimelineEventPatchPayloadBlocked";

        if (record.ContainsHiddenMaterial)
            yield return "TimelineEventHiddenMaterialBlocked";

        if (record.ClaimsAuthority)
            yield return "TimelineEventAuthorityClaimBlocked";

        if (record.ClaimsContinuation)
            yield return "TimelineEventContinuationClaimBlocked";

        if (record.ClaimsApproval)
            yield return "TimelineEventApprovalClaimBlocked";

        if (record.ClaimsPolicySatisfaction)
            yield return "TimelineEventPolicySatisfactionClaimBlocked";

        if (record.ClaimsExecution)
            yield return "TimelineEventExecutionClaimBlocked";

        if (HasAuthorityClaim(record.Summary))
            yield return "TimelineEventUnsafeTextBlocked";
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
