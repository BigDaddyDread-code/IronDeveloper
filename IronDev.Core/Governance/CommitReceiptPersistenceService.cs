using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public sealed class CommitReceiptPersistenceService
{
    private readonly ICommitReceiptPersistenceStore _store;

    public CommitReceiptPersistenceService(ICommitReceiptPersistenceStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<PersistCommitReceiptResult> PersistAsync(
        PersistCommitReceiptRequest? request,
        CancellationToken cancellationToken = default)
    {
        var validation = CommitReceiptPersistenceValidator.ValidateRequest(request);
        if (!validation.IsValid || request?.Receipt is null)
        {
            return Result(
                request,
                validation.HasUnsafePayload
                    ? CommitReceiptPersistenceStatus.RejectedUnsafePayload
                    : CommitReceiptPersistenceStatus.InvalidRequest,
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
                return Result(request, CommitReceiptPersistenceStatus.Conflict, false, ["CommitReceiptPersistenceExistingReceiptScopeConflict"]);
            }

            if (string.Equals(existingReceipt.RecordFingerprint, record.RecordFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return Result(request, CommitReceiptPersistenceStatus.AlreadyPersisted, true, []);
            }

            return Result(request, CommitReceiptPersistenceStatus.Conflict, false, ["CommitReceiptPersistenceReceiptFingerprintConflict"]);
        }

        var existingAttemptRecords = await _store
            .FindByCommitAttemptIdAsync(record.CommitAttemptId, cancellationToken)
            .ConfigureAwait(false);

        foreach (var existingAttempt in existingAttemptRecords)
        {
            if (!SameScope(existingAttempt, record))
            {
                return Result(request, CommitReceiptPersistenceStatus.Conflict, false, ["CommitReceiptPersistenceExistingAttemptScopeConflict"]);
            }

            if (TerminalOutcomeConflicts(existingAttempt.OutcomeKind, record.OutcomeKind))
            {
                return Result(request, CommitReceiptPersistenceStatus.Conflict, false, ["CommitReceiptPersistenceAttemptTerminalOutcomeConflict"]);
            }
        }

        if (!string.IsNullOrWhiteSpace(record.CommitSha))
        {
            var existingCommitShaRecords = await _store
                .FindByCommitShaAsync(record.CommitSha, cancellationToken)
                .ConfigureAwait(false);

            foreach (var existingCommitSha in existingCommitShaRecords)
            {
                if (!SameScope(existingCommitSha, record))
                {
                    return Result(request, CommitReceiptPersistenceStatus.Conflict, false, ["CommitReceiptPersistenceExistingCommitShaScopeConflict"]);
                }
            }
        }

        await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        return Result(request, CommitReceiptPersistenceStatus.Persisted, true, []);
    }

    public static string ComputeRecordFingerprint(CommitReceiptPersistenceRecord record)
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
                Pair(nameof(record.CommitAttemptId), record.CommitAttemptId),
                Pair(nameof(record.CommitPackageId), record.CommitPackageId),
                Pair(nameof(record.CommitPackageHash), record.CommitPackageHash),
                Pair(nameof(record.SourceApplyReceiptId), record.SourceApplyReceiptId),
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
                Pair(nameof(record.RepositoryRef), record.RepositoryRef),
                Pair(nameof(record.TargetBranchRef), record.TargetBranchRef),
                Pair(nameof(record.BaseCommitRef), record.BaseCommitRef),
                Pair(nameof(record.ParentCommitRef), record.ParentCommitRef),
                Pair(nameof(record.CommitSha), record.CommitSha),
                Pair(nameof(record.CommitTreeHash), record.CommitTreeHash),
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

    private static PersistCommitReceiptResult Result(
        PersistCommitReceiptRequest? request,
        CommitReceiptPersistenceStatus status,
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
            CommitAttemptId = request?.Receipt?.CommitAttemptId ?? string.Empty,
            CommitSha = request?.Receipt?.CommitSha ?? string.Empty,
            Issues = issues,
            Warnings = CommitReceiptPersistenceValidator.RequiredWarnings,
            ForbiddenAuthorityImplications = CommitReceiptPersistenceValidator.RequiredForbiddenAuthorityImplications
        };

    private static bool SameScope(
        CommitReceiptPersistenceRecord left,
        CommitReceiptPersistenceRecord right) =>
        Same(left.TenantId, right.TenantId) &&
        Same(left.ProjectId, right.ProjectId) &&
        Same(left.OperationId, right.OperationId) &&
        Same(left.CorrelationId, right.CorrelationId);

    private static bool TerminalOutcomeConflicts(
        CommitReceiptOutcomeKind existing,
        CommitReceiptOutcomeKind current) =>
        IsTerminal(existing) &&
        IsTerminal(current) &&
        existing != current;

    private static bool IsTerminal(CommitReceiptOutcomeKind outcomeKind) =>
        outcomeKind is CommitReceiptOutcomeKind.Succeeded or
            CommitReceiptOutcomeKind.Failed or
            CommitReceiptOutcomeKind.Interrupted or
            CommitReceiptOutcomeKind.Cancelled;

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string Pair(string key, string? value) =>
        string.Create(CultureInfo.InvariantCulture, $"{key}={Normalize(value)}");

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string FormatTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
