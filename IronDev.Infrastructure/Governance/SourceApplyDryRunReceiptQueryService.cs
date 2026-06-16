using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class SourceApplyDryRunReceiptQueryService : ISourceApplyDryRunReceiptQueryService
{
    private const string RedactedUnsafeText = "[redacted: sensitive source-apply dry-run receipt text]";

    private static readonly string[] PrivateMaterialMarkers =
    [
        "raw prompt",
        "rawprompt",
        "raw completion",
        "rawcompletion",
        "raw tool output",
        "rawtooloutput",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "private reasoning",
        "hidden reasoning",
        "scratchpad",
        "system prompt",
        "developer prompt",
        "password",
        "api_key",
        "apikey",
        "secret",
        "private key",
        "bearer"
    ];

    private readonly ISourceApplyDryRunReceiptStore _store;

    public SourceApplyDryRunReceiptQueryService(ISourceApplyDryRunReceiptStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public async Task<SourceApplyDryRunReceiptReadModel?> GetAsync(Guid projectId, Guid sourceApplyDryRunReceiptId, CancellationToken cancellationToken = default)
    {
        var receipt = await _store.GetAsync(projectId, sourceApplyDryRunReceiptId, cancellationToken);
        return receipt is null ? null : ToReadModel(receipt);
    }

    public async Task<SourceApplyDryRunReceiptReadModel?> GetByReceiptHashAsync(Guid projectId, string sourceApplyDryRunReceiptHash, CancellationToken cancellationToken = default)
    {
        var receipt = await _store.GetByReceiptHashAsync(projectId, sourceApplyDryRunReceiptHash, cancellationToken);
        return receipt is null ? null : ToReadModel(receipt);
    }

    public async Task<IReadOnlyList<SourceApplyDryRunReceiptReadModel>> ListBySourceApplyRequestAsync(Guid projectId, Guid sourceApplyRequestId, CancellationToken cancellationToken = default)
    {
        var receipts = await _store.ListBySourceApplyRequestAsync(projectId, sourceApplyRequestId, cancellationToken);
        return receipts.Select(ToReadModel).ToArray();
    }

    public async Task<IReadOnlyList<SourceApplyDryRunReceiptReadModel>> ListBySourceApplyGateEvaluationAsync(Guid projectId, Guid sourceApplyGateEvaluationId, CancellationToken cancellationToken = default)
    {
        var receipts = await _store.ListBySourceApplyGateEvaluationAsync(projectId, sourceApplyGateEvaluationId, cancellationToken);
        return receipts.Select(ToReadModel).ToArray();
    }

    public async Task<IReadOnlyList<SourceApplyDryRunReceiptReadModel>> ListByPatchArtifactAsync(Guid projectId, Guid patchArtifactId, CancellationToken cancellationToken = default)
    {
        var receipts = await _store.ListByPatchArtifactAsync(projectId, patchArtifactId, cancellationToken);
        return receipts.Select(ToReadModel).ToArray();
    }

    public async Task<IReadOnlyList<SourceApplyDryRunReceiptReadModel>> ListByRollbackSupportReceiptAsync(Guid projectId, Guid rollbackSupportReceiptId, CancellationToken cancellationToken = default)
    {
        var receipts = await _store.ListByRollbackSupportReceiptAsync(projectId, rollbackSupportReceiptId, cancellationToken);
        return receipts.Select(ToReadModel).ToArray();
    }

    private static SourceApplyDryRunReceiptReadModel ToReadModel(SourceApplyDryRunReceipt receipt) => new()
    {
        SourceApplyDryRunReceiptId = receipt.SourceApplyDryRunReceiptId,
        ProjectId = receipt.ProjectId,
        SourceApplyDryRunRequestId = receipt.SourceApplyDryRunRequestId,
        SourceApplyDryRunRequestHash = SafeText(receipt.SourceApplyDryRunRequestHash),
        DryRunSatisfied = receipt.DryRunSatisfied,
        DryRunResultHash = SafeText(receipt.DryRunResultHash),
        SourceApplyRequestId = receipt.SourceApplyRequestId,
        SourceApplyRequestHash = SafeText(receipt.SourceApplyRequestHash),
        SourceApplyGateEvaluationId = receipt.SourceApplyGateEvaluationId,
        SourceApplyGateEvaluationHash = SafeText(receipt.SourceApplyGateEvaluationHash),
        PatchArtifactId = receipt.PatchArtifactId,
        PatchHash = SafeText(receipt.PatchHash),
        ChangeSetHash = SafeText(receipt.ChangeSetHash),
        RollbackSupportReceiptId = receipt.RollbackSupportReceiptId,
        RollbackSupportReceiptHash = SafeText(receipt.RollbackSupportReceiptHash),
        SourceBaselineHash = SafeText(receipt.SourceBaselineHash),
        WorkspaceBoundaryHash = SafeText(receipt.WorkspaceBoundaryHash),
        ExpectedBranch = SafeText(receipt.ExpectedBranch),
        ExpectedCleanWorktreeHash = SafeText(receipt.ExpectedCleanWorktreeHash),
        FileResults = receipt.FileResults.Select(ToReadModel).ToArray(),
        CreatedAtUtc = receipt.CreatedAtUtc,
        ExpiresAtUtc = receipt.ExpiresAtUtc,
        SourceApplyDryRunReceiptHash = SafeText(receipt.SourceApplyDryRunReceiptHash),
        EvidenceReferences = receipt.EvidenceReferences.Select(SafeText).ToArray(),
        BoundaryMaxims = receipt.BoundaryMaxims.Select(SafeText).ToArray(),
        Boundary = SafeText(receipt.Boundary),
        AuthorityBoundary = SourceApplyDryRunReceiptReadBoundaryText.AuthorityBoundary,
        Warnings = SourceApplyDryRunReceiptReadBoundaryText.Warnings
    };

    private static SourceApplyDryRunReceiptFileResultReadModel ToReadModel(SourceApplyDryRunReceiptFileResult result) => new()
    {
        Path = SafeText(result.Path),
        PreviousPath = string.IsNullOrWhiteSpace(result.PreviousPath) ? null : SafeText(result.PreviousPath),
        OperationKind = SafeText(result.OperationKind),
        PatchArtifactChangeHash = SafeText(result.PatchArtifactChangeHash),
        OperationHash = SafeText(result.OperationHash),
        ExpectedBeforeContentHash = SafeText(result.ExpectedBeforeContentHash),
        ExpectedAfterContentHash = SafeText(result.ExpectedAfterContentHash),
        ObservedCurrentContentHash = SafeText(result.ObservedCurrentContentHash),
        PreconditionsSatisfied = result.PreconditionsSatisfied,
        WouldCreate = result.WouldCreate,
        WouldModify = result.WouldModify,
        WouldDelete = result.WouldDelete,
        WouldRename = result.WouldRename,
        WouldNoop = result.WouldNoop,
        IssueCodes = result.IssueCodes.Select(SafeText).ToArray(),
        FileResultHash = SafeText(result.FileResultHash)
    };

    private static string SafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return PrivateMaterialMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            ? RedactedUnsafeText
            : value.Trim();
    }
}
