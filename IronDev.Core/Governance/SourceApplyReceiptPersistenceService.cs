using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public sealed class SourceApplyReceiptPersistenceService
{
    private readonly ISourceApplyReceiptPersistenceStore _store;

    public SourceApplyReceiptPersistenceService(ISourceApplyReceiptPersistenceStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<PersistSourceApplyReceiptResult> PersistAsync(
        PersistSourceApplyReceiptRequest? request,
        CancellationToken cancellationToken = default)
    {
        var validation = SourceApplyReceiptPersistenceValidator.ValidateRequest(request);
        if (!validation.IsValid || request?.Receipt is null)
        {
            return Result(
                request,
                validation.HasUnsafePayload
                    ? SourceApplyReceiptPersistenceStatus.RejectedUnsafePayload
                    : SourceApplyReceiptPersistenceStatus.InvalidRequest,
                false,
                validation.Issues);
        }

        var record = request.Receipt with
        {
            RecordFingerprint = ComputeRecordFingerprint(request.Receipt)
        };

        var existingReceipt = await _store.FindByReceiptIdAsync(record.ReceiptId, cancellationToken).ConfigureAwait(false);
        if (existingReceipt is not null)
        {
            if (!SameScope(existingReceipt, record))
            {
                return Result(request, SourceApplyReceiptPersistenceStatus.Conflict, false, ["SourceApplyReceiptPersistenceExistingReceiptScopeConflict"]);
            }

            if (string.Equals(existingReceipt.RecordFingerprint, record.RecordFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return Result(request, SourceApplyReceiptPersistenceStatus.AlreadyPersisted, true, []);
            }

            return Result(request, SourceApplyReceiptPersistenceStatus.Conflict, false, ["SourceApplyReceiptPersistenceReceiptFingerprintConflict"]);
        }

        var existingAttemptRecords = await _store
            .FindBySourceApplyAttemptIdAsync(record.SourceApplyAttemptId, cancellationToken)
            .ConfigureAwait(false);

        foreach (var existingAttempt in existingAttemptRecords)
        {
            if (!SameScope(existingAttempt, record))
            {
                return Result(request, SourceApplyReceiptPersistenceStatus.Conflict, false, ["SourceApplyReceiptPersistenceExistingAttemptScopeConflict"]);
            }

            if (TerminalOutcomeConflicts(existingAttempt.OutcomeKind, record.OutcomeKind))
            {
                return Result(request, SourceApplyReceiptPersistenceStatus.Conflict, false, ["SourceApplyReceiptPersistenceAttemptTerminalOutcomeConflict"]);
            }
        }

        await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        return Result(request, SourceApplyReceiptPersistenceStatus.Persisted, true, []);
    }

    public static string ComputeRecordFingerprint(SourceApplyReceiptPersistenceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var canonical = string.Join(
            "\n",
            [
                Pair(nameof(record.TenantId), record.TenantId),
                Pair(nameof(record.ProjectId), record.ProjectId),
                Pair(nameof(record.OperationId), record.OperationId),
                Pair(nameof(record.CorrelationId), record.CorrelationId),
                Pair(nameof(record.ReceiptId), record.ReceiptId),
                Pair(nameof(record.SourceApplyAttemptId), record.SourceApplyAttemptId),
                Pair(nameof(record.PatchArtifactId), record.PatchArtifactId),
                Pair(nameof(record.PatchArtifactHash), record.PatchArtifactHash),
                Pair(nameof(record.PatchBaseRef), record.PatchBaseRef),
                Pair(nameof(record.ValidationResultRef), record.ValidationResultRef),
                Pair(nameof(record.AcceptedApprovalRef), record.AcceptedApprovalRef),
                Pair(nameof(record.PolicySatisfactionRef), record.PolicySatisfactionRef),
                Pair(nameof(record.DryRunRef), record.DryRunRef),
                Pair(nameof(record.WorktreeBeforeRef), record.WorktreeBeforeRef),
                Pair(nameof(record.WorktreeAfterRef), record.WorktreeAfterRef),
                Pair(nameof(record.OutcomeKind), record.OutcomeKind.ToString()),
                Pair(nameof(record.OutcomeReasonCode), record.OutcomeReasonCode),
                Pair(nameof(record.StartedAtUtc), FormatTime(record.StartedAtUtc)),
                Pair(nameof(record.CompletedAtUtc), record.CompletedAtUtc is null ? string.Empty : FormatTime(record.CompletedAtUtc.Value)),
                Pair(nameof(record.RecordedAtUtc), FormatTime(record.RecordedAtUtc)),
                Pair(nameof(record.Source), record.Source),
                Pair(nameof(record.IsRedacted), record.IsRedacted ? "true" : "false"),
                Pair(nameof(record.RedactionReason), record.RedactionReason)
            ]);

        return $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant()}";
    }

    private static PersistSourceApplyReceiptResult Result(
        PersistSourceApplyReceiptRequest? request,
        SourceApplyReceiptPersistenceStatus status,
        bool isValid,
        IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = isValid,
            PersistenceStatus = status,
            TenantId = request?.TenantId ?? string.Empty,
            ProjectId = request?.ProjectId ?? string.Empty,
            OperationId = request?.OperationId ?? string.Empty,
            CorrelationId = request?.CorrelationId ?? string.Empty,
            ReceiptId = request?.Receipt?.ReceiptId ?? string.Empty,
            SourceApplyAttemptId = request?.Receipt?.SourceApplyAttemptId ?? string.Empty,
            Issues = issues,
            Warnings = SourceApplyReceiptPersistenceValidator.RequiredWarnings,
            ForbiddenAuthorityImplications = SourceApplyReceiptPersistenceValidator.RequiredForbiddenAuthorityImplications
        };

    private static bool SameScope(
        SourceApplyReceiptPersistenceRecord left,
        SourceApplyReceiptPersistenceRecord right) =>
        Same(left.TenantId, right.TenantId) &&
        Same(left.ProjectId, right.ProjectId) &&
        Same(left.OperationId, right.OperationId) &&
        Same(left.CorrelationId, right.CorrelationId);

    private static bool TerminalOutcomeConflicts(
        SourceApplyReceiptOutcomeKind existing,
        SourceApplyReceiptOutcomeKind current) =>
        IsTerminal(existing) &&
        IsTerminal(current) &&
        existing != current;

    private static bool IsTerminal(SourceApplyReceiptOutcomeKind outcomeKind) =>
        outcomeKind is SourceApplyReceiptOutcomeKind.Succeeded or
            SourceApplyReceiptOutcomeKind.Failed or
            SourceApplyReceiptOutcomeKind.Interrupted or
            SourceApplyReceiptOutcomeKind.Cancelled;

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string Pair(string key, string? value) =>
        string.Create(CultureInfo.InvariantCulture, $"{key}={Normalize(value)}");

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string FormatTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
