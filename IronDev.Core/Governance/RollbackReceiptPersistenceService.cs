using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public sealed class RollbackReceiptPersistenceService
{
    private readonly IRollbackReceiptPersistenceStore _store;

    public RollbackReceiptPersistenceService(IRollbackReceiptPersistenceStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<PersistRollbackReceiptResult> PersistAsync(
        PersistRollbackReceiptRequest? request,
        CancellationToken cancellationToken = default)
    {
        var validation = RollbackReceiptPersistenceValidator.ValidateRequest(request);
        if (!validation.IsValid || request?.Receipt is null)
        {
            return Result(
                request,
                validation.HasUnsafePayload
                    ? RollbackReceiptPersistenceStatus.RejectedUnsafePayload
                    : RollbackReceiptPersistenceStatus.InvalidRequest,
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
                return Result(request, RollbackReceiptPersistenceStatus.Conflict, false, ["RollbackReceiptPersistenceExistingReceiptScopeConflict"]);
            }

            if (string.Equals(existingReceipt.RecordFingerprint, record.RecordFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return Result(request, RollbackReceiptPersistenceStatus.AlreadyPersisted, true, []);
            }

            return Result(request, RollbackReceiptPersistenceStatus.Conflict, false, ["RollbackReceiptPersistenceReceiptFingerprintConflict"]);
        }

        var existingAttemptRecords = await _store
            .FindByRollbackAttemptIdAsync(record.RollbackAttemptId, cancellationToken)
            .ConfigureAwait(false);

        foreach (var existingAttempt in existingAttemptRecords)
        {
            if (!SameScope(existingAttempt, record))
            {
                return Result(request, RollbackReceiptPersistenceStatus.Conflict, false, ["RollbackReceiptPersistenceExistingAttemptScopeConflict"]);
            }

            if (TerminalOutcomeConflicts(existingAttempt.OutcomeKind, record.OutcomeKind))
            {
                return Result(request, RollbackReceiptPersistenceStatus.Conflict, false, ["RollbackReceiptPersistenceAttemptTerminalOutcomeConflict"]);
            }
        }

        var existingTargetRecords = await _store
            .FindByRollbackTargetRefAsync(record.RollbackTargetRef, cancellationToken)
            .ConfigureAwait(false);

        foreach (var existingTarget in existingTargetRecords)
        {
            if (!SameScope(existingTarget, record))
            {
                return Result(request, RollbackReceiptPersistenceStatus.Conflict, false, ["RollbackReceiptPersistenceExistingRollbackTargetScopeConflict"]);
            }
        }

        if (!string.IsNullOrWhiteSpace(record.RollbackResultRef))
        {
            var existingResultRecords = await _store
                .FindByRollbackResultRefAsync(record.RollbackResultRef, cancellationToken)
                .ConfigureAwait(false);

            foreach (var existingResult in existingResultRecords)
            {
                if (!SameScope(existingResult, record))
                {
                    return Result(request, RollbackReceiptPersistenceStatus.Conflict, false, ["RollbackReceiptPersistenceExistingRollbackResultScopeConflict"]);
                }
            }
        }

        await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        return Result(request, RollbackReceiptPersistenceStatus.Persisted, true, []);
    }

    public static string ComputeRecordFingerprint(RollbackReceiptPersistenceRecord record)
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
                Pair(nameof(record.RollbackAttemptId), record.RollbackAttemptId),
                Pair(nameof(record.RollbackPlanRef), record.RollbackPlanRef),
                Pair(nameof(record.RollbackResultRef), record.RollbackResultRef),
                Pair(nameof(record.RollbackTargetKind), record.RollbackTargetKind.ToString()),
                Pair(nameof(record.RollbackTargetRef), record.RollbackTargetRef),
                Pair(nameof(record.RollbackReasonCode), record.RollbackReasonCode),
                Pair(nameof(record.OriginalOperationId), record.OriginalOperationId),
                Pair(nameof(record.OriginalAttemptId), record.OriginalAttemptId),
                Pair(nameof(record.SourceApplyReceiptId), record.SourceApplyReceiptId),
                Pair(nameof(record.CommitReceiptId), record.CommitReceiptId),
                Pair(nameof(record.PushReceiptId), record.PushReceiptId),
                Pair(nameof(record.DraftPullRequestReceiptId), record.DraftPullRequestReceiptId),
                Pair(nameof(record.CommitSha), record.CommitSha),
                Pair(nameof(record.RepositoryRef), record.RepositoryRef),
                Pair(nameof(record.TargetBranchRef), record.TargetBranchRef),
                Pair(nameof(record.PullRequestRef), record.PullRequestRef),
                Pair(nameof(record.PullRequestNumberRef), record.PullRequestNumberRef),
                Pair(nameof(record.WorktreeBeforeRef), record.WorktreeBeforeRef),
                Pair(nameof(record.WorktreeAfterRef), record.WorktreeAfterRef),
                Pair(nameof(record.ValidationResultRef), record.ValidationResultRef),
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

    private static PersistRollbackReceiptResult Result(
        PersistRollbackReceiptRequest? request,
        RollbackReceiptPersistenceStatus status,
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
            RollbackAttemptId = request?.Receipt?.RollbackAttemptId ?? string.Empty,
            RollbackTargetKind = request?.Receipt?.RollbackTargetKind ?? RollbackTargetKind.Unknown,
            RollbackTargetRef = request?.Receipt?.RollbackTargetRef ?? string.Empty,
            Issues = issues,
            Warnings = RollbackReceiptPersistenceValidator.RequiredWarnings,
            ForbiddenAuthorityImplications = RollbackReceiptPersistenceValidator.RequiredForbiddenAuthorityImplications
        };

    private static bool SameScope(
        RollbackReceiptPersistenceRecord left,
        RollbackReceiptPersistenceRecord right) =>
        Same(left.TenantId, right.TenantId) &&
        Same(left.ProjectId, right.ProjectId) &&
        Same(left.OperationId, right.OperationId) &&
        Same(left.CorrelationId, right.CorrelationId);

    private static bool TerminalOutcomeConflicts(
        RollbackReceiptOutcomeKind existing,
        RollbackReceiptOutcomeKind current) =>
        IsTerminal(existing) &&
        IsTerminal(current) &&
        existing != current;

    private static bool IsTerminal(RollbackReceiptOutcomeKind outcomeKind) =>
        outcomeKind is RollbackReceiptOutcomeKind.Succeeded or
            RollbackReceiptOutcomeKind.Failed or
            RollbackReceiptOutcomeKind.Interrupted or
            RollbackReceiptOutcomeKind.Cancelled;

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string Pair(string key, string? value) =>
        string.Create(CultureInfo.InvariantCulture, $"{key}={Normalize(value)}");

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string FormatTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
