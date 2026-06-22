using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class GovernedOperationStatusReadRepository : IGovernedOperationStatusReadRepository
{
    private static readonly string[] InvalidStatusForbiddenActions =
    [
        "do not treat invalid stored operation status as authority",
        "do not execute from invalid operation status",
        "do not infer approval, policy satisfaction, source apply, rollback, commit, push, PR, merge, release, deployment, memory promotion, or workflow continuation from invalid operation status"
    ];

    private readonly IReadOnlyList<GovernedOperationStatusReadRecord> _records;

    public GovernedOperationStatusReadRepository()
        : this([])
    {
    }

    public GovernedOperationStatusReadRepository(IEnumerable<GovernedOperationStatusReadRecord> records) =>
        _records = records?.ToArray() ?? throw new ArgumentNullException(nameof(records));

    public GovernedOperationStatusReadResult GetByOperationId(
        string operationId,
        FrontendReadinessReadScope scope)
    {
        var key = Normalize(operationId);
        if (key is null)
            return GovernedOperationStatusReadResult.NotFound("OperationIdRequired");

        var record = _records.FirstOrDefault(candidate =>
            string.Equals(candidate.OperationId, key, StringComparison.OrdinalIgnoreCase));
        if (record is null)
            return GovernedOperationStatusReadResult.NotFound("OperationStatusNotFound");

        var tenantIssues = ValidateTenant(record, scope);
        if (tenantIssues.Count > 0)
            return GovernedOperationStatusReadResult.NotFound(tenantIssues.ToArray());

        var validation = GovernedOperationStatusValidator.Validate(record.Status);
        if (!validation.IsValid)
        {
            var issues = new[] { "StoredOperationStatusInvalid" }
                .Concat(validation.Issues)
                .Concat(validation.RedFlags)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new GovernedOperationStatusReadResult
            {
                Found = true,
                Status = BuildInvalidStatusDiagnostic(key, record.Status, issues),
                Issues = issues,
                EvidenceRefs = SafeRefs(record.Status.EvidenceRefs),
                Boundary = FrontendReadBoundary.ReadOnlyStatus
            };
        }

        return new GovernedOperationStatusReadResult
        {
            Found = true,
            Status = record.Status,
            Issues = [],
            EvidenceRefs = SafeRefs(record.Status.EvidenceRefs),
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };
    }

    private static GovernedOperationStatus BuildInvalidStatusDiagnostic(
        string operationId,
        GovernedOperationStatus storedStatus,
        IReadOnlyCollection<string> issues) =>
        new()
        {
            OperationId = operationId,
            OperationKind = "OperationStatusRead",
            Subject = $"operation-status:{operationId}",
            State = GovernedOperationState.Blocked,
            BlockedReasons = ["StoredOperationStatusInvalid"],
            MissingEvidence = ["valid-governed-operation-status-record"],
            NextSafeActions = ["inspect operation status producer"],
            ForbiddenActions = InvalidStatusForbiddenActions,
            EvidenceRefs = SafeRefs(storedStatus.EvidenceRefs).Concat(issues.Select(issue => $"operation-status-validation-issue:{issue}")).ToArray(),
            ReceiptRefs = SafeRefs(storedStatus.ReceiptRefs),
            ObservedAtUtc = storedStatus.ObservedAtUtc == default
                ? DateTimeOffset.UnixEpoch
                : storedStatus.ObservedAtUtc,
            ExpiresAtUtc = storedStatus.ExpiresAtUtc
        };

    private static IReadOnlyList<string> ValidateTenant(
        GovernedOperationStatusReadRecord record,
        FrontendReadinessReadScope scope)
    {
        if (!record.IsTenantScoped)
            return [];

        if (!scope.HasTenant)
            return ["TenantScopedOperationStatusRequiresTenantScope"];

        if (record.TenantId is null or <= 0)
            return ["TenantScopedOperationStatusRecordTenantRequired"];

        if (record.TenantId.Value != scope.TenantId)
            return ["OperationStatusTenantMismatch"];

        return [];
    }

    private static string? Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> SafeRefs(IEnumerable<string>? values) =>
        values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
}
