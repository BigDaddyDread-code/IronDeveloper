using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public sealed class DraftPullRequestReceiptPersistenceService
{
    private readonly IDraftPullRequestReceiptPersistenceStore _store;

    public DraftPullRequestReceiptPersistenceService(IDraftPullRequestReceiptPersistenceStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<PersistDraftPullRequestReceiptResult> PersistAsync(
        PersistDraftPullRequestReceiptRequest? request,
        CancellationToken cancellationToken = default)
    {
        var validation = DraftPullRequestReceiptPersistenceValidator.ValidateRequest(request);
        if (!validation.IsValid || request?.Receipt is null)
        {
            return Result(
                request,
                validation.HasUnsafePayload
                    ? DraftPullRequestReceiptPersistenceStatus.RejectedUnsafePayload
                    : DraftPullRequestReceiptPersistenceStatus.InvalidRequest,
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
                return Result(request, DraftPullRequestReceiptPersistenceStatus.Conflict, false, ["DraftPullRequestReceiptPersistenceExistingReceiptScopeConflict"]);
            }

            if (string.Equals(existingReceipt.RecordFingerprint, record.RecordFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return Result(request, DraftPullRequestReceiptPersistenceStatus.AlreadyPersisted, true, []);
            }

            return Result(request, DraftPullRequestReceiptPersistenceStatus.Conflict, false, ["DraftPullRequestReceiptPersistenceReceiptFingerprintConflict"]);
        }

        var existingAttemptRecords = await _store
            .FindByDraftPullRequestAttemptIdAsync(record.DraftPullRequestAttemptId, cancellationToken)
            .ConfigureAwait(false);

        foreach (var existingAttempt in existingAttemptRecords)
        {
            if (!SameScope(existingAttempt, record))
            {
                return Result(request, DraftPullRequestReceiptPersistenceStatus.Conflict, false, ["DraftPullRequestReceiptPersistenceExistingAttemptScopeConflict"]);
            }

            if (TerminalOutcomeConflicts(existingAttempt.OutcomeKind, record.OutcomeKind))
            {
                return Result(request, DraftPullRequestReceiptPersistenceStatus.Conflict, false, ["DraftPullRequestReceiptPersistenceAttemptTerminalOutcomeConflict"]);
            }
        }

        if (!string.IsNullOrWhiteSpace(record.PullRequestRef))
        {
            var existingPullRequestRecords = await _store
                .FindByPullRequestRefAsync(record.PullRequestRef, cancellationToken)
                .ConfigureAwait(false);

            foreach (var existingPullRequest in existingPullRequestRecords)
            {
                if (!SameScope(existingPullRequest, record))
                {
                    return Result(request, DraftPullRequestReceiptPersistenceStatus.Conflict, false, ["DraftPullRequestReceiptPersistenceExistingPullRequestRefScopeConflict"]);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(record.PullRequestNumberRef))
        {
            var existingNumberRecords = await _store
                .FindByPullRequestNumberRefAsync(record.PullRequestNumberRef, cancellationToken)
                .ConfigureAwait(false);

            foreach (var existingNumber in existingNumberRecords)
            {
                if (!SameScope(existingNumber, record))
                {
                    return Result(request, DraftPullRequestReceiptPersistenceStatus.Conflict, false, ["DraftPullRequestReceiptPersistenceExistingPullRequestNumberScopeConflict"]);
                }
            }
        }

        await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        return Result(request, DraftPullRequestReceiptPersistenceStatus.Persisted, true, []);
    }

    public static string ComputeRecordFingerprint(DraftPullRequestReceiptPersistenceRecord record)
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
                Pair(nameof(record.DraftPullRequestAttemptId), record.DraftPullRequestAttemptId),
                Pair(nameof(record.PushReceiptId), record.PushReceiptId),
                Pair(nameof(record.PushAttemptId), record.PushAttemptId),
                Pair(nameof(record.CommitReceiptId), record.CommitReceiptId),
                Pair(nameof(record.CommitAttemptId), record.CommitAttemptId),
                Pair(nameof(record.CommitSha), record.CommitSha),
                Pair(nameof(record.RepositoryRef), record.RepositoryRef),
                Pair(nameof(record.ProviderRef), record.ProviderRef),
                Pair(nameof(record.BaseBranchRef), record.BaseBranchRef),
                Pair(nameof(record.HeadBranchRef), record.HeadBranchRef),
                Pair(nameof(record.PullRequestRef), record.PullRequestRef),
                Pair(nameof(record.PullRequestNumberRef), record.PullRequestNumberRef),
                Pair(nameof(record.PullRequestWebRef), record.PullRequestWebRef),
                Pair(nameof(record.PullRequestTitleHash), record.PullRequestTitleHash),
                Pair(nameof(record.PullRequestBodyHash), record.PullRequestBodyHash),
                Pair(nameof(record.ObservedDraftState), record.ObservedDraftState.ToString()),
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

    private static PersistDraftPullRequestReceiptResult Result(
        PersistDraftPullRequestReceiptRequest? request,
        DraftPullRequestReceiptPersistenceStatus status,
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
            DraftPullRequestAttemptId = request?.Receipt?.DraftPullRequestAttemptId ?? string.Empty,
            PullRequestRef = request?.Receipt?.PullRequestRef ?? string.Empty,
            PullRequestNumberRef = request?.Receipt?.PullRequestNumberRef ?? string.Empty,
            ObservedDraftState = request?.Receipt?.ObservedDraftState ?? DraftPullRequestObservedState.Unknown,
            Issues = issues,
            Warnings = DraftPullRequestReceiptPersistenceValidator.RequiredWarnings,
            ForbiddenAuthorityImplications = DraftPullRequestReceiptPersistenceValidator.RequiredForbiddenAuthorityImplications
        };

    private static bool SameScope(
        DraftPullRequestReceiptPersistenceRecord left,
        DraftPullRequestReceiptPersistenceRecord right) =>
        Same(left.TenantId, right.TenantId) &&
        Same(left.ProjectId, right.ProjectId) &&
        Same(left.OperationId, right.OperationId) &&
        Same(left.CorrelationId, right.CorrelationId);

    private static bool TerminalOutcomeConflicts(
        DraftPullRequestReceiptOutcomeKind existing,
        DraftPullRequestReceiptOutcomeKind current) =>
        IsTerminal(existing) &&
        IsTerminal(current) &&
        existing != current;

    private static bool IsTerminal(DraftPullRequestReceiptOutcomeKind outcomeKind) =>
        outcomeKind is DraftPullRequestReceiptOutcomeKind.Succeeded or
            DraftPullRequestReceiptOutcomeKind.Failed or
            DraftPullRequestReceiptOutcomeKind.Interrupted or
            DraftPullRequestReceiptOutcomeKind.Cancelled;

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string Pair(string key, string? value) =>
        string.Create(CultureInfo.InvariantCulture, $"{key}={Normalize(value)}");

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string FormatTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
