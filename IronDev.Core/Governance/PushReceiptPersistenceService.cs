using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public sealed class PushReceiptPersistenceService
{
    private readonly IPushReceiptPersistenceStore _store;

    public PushReceiptPersistenceService(IPushReceiptPersistenceStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<PersistPushReceiptResult> PersistAsync(
        PersistPushReceiptRequest? request,
        CancellationToken cancellationToken = default)
    {
        var validation = PushReceiptPersistenceValidator.ValidateRequest(request);
        if (!validation.IsValid || request?.Receipt is null)
        {
            return Result(
                request,
                validation.HasUnsafePayload
                    ? PushReceiptPersistenceStatus.RejectedUnsafePayload
                    : PushReceiptPersistenceStatus.InvalidRequest,
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
                return Result(request, PushReceiptPersistenceStatus.Conflict, false, ["PushReceiptPersistenceExistingReceiptScopeConflict"]);
            }

            if (string.Equals(existingReceipt.RecordFingerprint, record.RecordFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return Result(request, PushReceiptPersistenceStatus.AlreadyPersisted, true, []);
            }

            return Result(request, PushReceiptPersistenceStatus.Conflict, false, ["PushReceiptPersistenceReceiptFingerprintConflict"]);
        }

        var existingAttemptRecords = await _store
            .FindByPushAttemptIdAsync(record.PushAttemptId, cancellationToken)
            .ConfigureAwait(false);

        foreach (var existingAttempt in existingAttemptRecords)
        {
            if (!SameScope(existingAttempt, record))
            {
                return Result(request, PushReceiptPersistenceStatus.Conflict, false, ["PushReceiptPersistenceExistingAttemptScopeConflict"]);
            }

            if (TerminalOutcomeConflicts(existingAttempt.OutcomeKind, record.OutcomeKind))
            {
                return Result(request, PushReceiptPersistenceStatus.Conflict, false, ["PushReceiptPersistenceAttemptTerminalOutcomeConflict"]);
            }
        }

        var existingCommitRecords = await _store
            .FindByCommitShaAsync(record.CommitSha, cancellationToken)
            .ConfigureAwait(false);

        foreach (var existingCommit in existingCommitRecords)
        {
            if (Same(existingCommit.TargetBranchRef, record.TargetBranchRef) &&
                !SameScope(existingCommit, record))
            {
                return Result(request, PushReceiptPersistenceStatus.Conflict, false, ["PushReceiptPersistenceExistingCommitShaTargetScopeConflict"]);
            }
        }

        var existingTargetRecords = await _store
            .FindByTargetBranchRefAsync(record.TargetBranchRef, cancellationToken)
            .ConfigureAwait(false);

        foreach (var existingTarget in existingTargetRecords)
        {
            if (!string.IsNullOrWhiteSpace(record.ObservedRemoteHeadRef) &&
                Same(existingTarget.ObservedRemoteHeadRef, record.ObservedRemoteHeadRef) &&
                !SameScope(existingTarget, record))
            {
                return Result(request, PushReceiptPersistenceStatus.Conflict, false, ["PushReceiptPersistenceExistingObservedRemoteHeadScopeConflict"]);
            }
        }

        await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        return Result(request, PushReceiptPersistenceStatus.Persisted, true, []);
    }

    public static string ComputeRecordFingerprint(PushReceiptPersistenceRecord record)
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
                Pair(nameof(record.PushAttemptId), record.PushAttemptId),
                Pair(nameof(record.CommitReceiptId), record.CommitReceiptId),
                Pair(nameof(record.CommitAttemptId), record.CommitAttemptId),
                Pair(nameof(record.CommitSha), record.CommitSha),
                Pair(nameof(record.CommitTreeHash), record.CommitTreeHash),
                Pair(nameof(record.RepositoryRef), record.RepositoryRef),
                Pair(nameof(record.RemoteRef), record.RemoteRef),
                Pair(nameof(record.TargetBranchRef), record.TargetBranchRef),
                Pair(nameof(record.ExpectedRemoteHeadRef), record.ExpectedRemoteHeadRef),
                Pair(nameof(record.ObservedRemoteHeadRef), record.ObservedRemoteHeadRef),
                Pair(nameof(record.PushResultRef), record.PushResultRef),
                Pair(nameof(record.SourceApplyReceiptId), record.SourceApplyReceiptId),
                Pair(nameof(record.CommitPackageId), record.CommitPackageId),
                Pair(nameof(record.PatchArtifactId), record.PatchArtifactId),
                Pair(nameof(record.PatchArtifactHash), record.PatchArtifactHash),
                Pair(nameof(record.ValidationResultRef), record.ValidationResultRef),
                Pair(nameof(record.AcceptedApprovalRef), record.AcceptedApprovalRef),
                Pair(nameof(record.PolicySatisfactionRef), record.PolicySatisfactionRef),
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

    private static PersistPushReceiptResult Result(
        PersistPushReceiptRequest? request,
        PushReceiptPersistenceStatus status,
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
            PushAttemptId = request?.Receipt?.PushAttemptId ?? string.Empty,
            CommitSha = request?.Receipt?.CommitSha ?? string.Empty,
            TargetBranchRef = request?.Receipt?.TargetBranchRef ?? string.Empty,
            Issues = issues,
            Warnings = PushReceiptPersistenceValidator.RequiredWarnings,
            ForbiddenAuthorityImplications = PushReceiptPersistenceValidator.RequiredForbiddenAuthorityImplications
        };

    private static bool SameScope(
        PushReceiptPersistenceRecord left,
        PushReceiptPersistenceRecord right) =>
        Same(left.TenantId, right.TenantId) &&
        Same(left.ProjectId, right.ProjectId) &&
        Same(left.OperationId, right.OperationId) &&
        Same(left.CorrelationId, right.CorrelationId);

    private static bool TerminalOutcomeConflicts(
        PushReceiptOutcomeKind existing,
        PushReceiptOutcomeKind current) =>
        IsTerminal(existing) &&
        IsTerminal(current) &&
        existing != current;

    private static bool IsTerminal(PushReceiptOutcomeKind outcomeKind) =>
        outcomeKind is PushReceiptOutcomeKind.Succeeded or
            PushReceiptOutcomeKind.Failed or
            PushReceiptOutcomeKind.Interrupted or
            PushReceiptOutcomeKind.Cancelled;

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string Pair(string key, string? value) =>
        string.Create(CultureInfo.InvariantCulture, $"{key}={Normalize(value)}");

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string FormatTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
