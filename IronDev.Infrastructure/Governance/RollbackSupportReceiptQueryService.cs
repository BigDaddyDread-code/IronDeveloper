using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class RollbackSupportReceiptQueryService : IRollbackSupportReceiptQueryService
{
    private const string RedactedUnsafeText = "[redacted: sensitive rollback receipt text]";

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

    private readonly IRollbackSupportReceiptStore _store;

    public RollbackSupportReceiptQueryService(IRollbackSupportReceiptStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public async Task<RollbackSupportReceiptReadModel?> GetAsync(Guid projectId, Guid rollbackSupportReceiptId, CancellationToken cancellationToken = default)
    {
        var receipt = await _store.GetAsync(projectId, rollbackSupportReceiptId, cancellationToken);
        return receipt is null ? null : ToReadModel(receipt);
    }

    public async Task<RollbackSupportReceiptReadModel?> GetByReceiptHashAsync(Guid projectId, string rollbackSupportReceiptHash, CancellationToken cancellationToken = default)
    {
        var receipt = await _store.GetByReceiptHashAsync(projectId, rollbackSupportReceiptHash, cancellationToken);
        return receipt is null ? null : ToReadModel(receipt);
    }

    public async Task<IReadOnlyList<RollbackSupportReceiptReadModel>> ListByPatchArtifactAsync(Guid projectId, Guid patchArtifactId, CancellationToken cancellationToken = default)
    {
        var receipts = await _store.ListByPatchArtifactAsync(projectId, patchArtifactId, cancellationToken);
        return receipts.Select(ToReadModel).ToArray();
    }

    public async Task<IReadOnlyList<RollbackSupportReceiptReadModel>> ListByPatchHashAsync(Guid projectId, string patchHash, CancellationToken cancellationToken = default)
    {
        var receipts = await _store.ListByPatchHashAsync(projectId, patchHash, cancellationToken);
        return receipts.Select(ToReadModel).ToArray();
    }

    public async Task<IReadOnlyList<RollbackSupportReceiptReadModel>> ListByRollbackPlanAsync(Guid projectId, Guid rollbackPlanId, CancellationToken cancellationToken = default)
    {
        var receipts = await _store.ListByRollbackPlanAsync(projectId, rollbackPlanId, cancellationToken);
        return receipts.Select(ToReadModel).ToArray();
    }

    public async Task<IReadOnlyList<RollbackSupportReceiptReadModel>> ListBySourceBaselineHashAsync(Guid projectId, string sourceBaselineHash, CancellationToken cancellationToken = default)
    {
        var receipts = await _store.ListBySourceBaselineHashAsync(projectId, sourceBaselineHash, cancellationToken);
        return receipts.Select(ToReadModel).ToArray();
    }

    private static RollbackSupportReceiptReadModel ToReadModel(RollbackSupportReceipt receipt) => new()
    {
        RollbackSupportReceiptId = receipt.RollbackSupportReceiptId,
        ProjectId = receipt.ProjectId,
        RollbackPlanId = receipt.RollbackPlanId,
        RollbackPlanHash = SafeText(receipt.RollbackPlanHash),
        RollbackGateSatisfied = receipt.RollbackGateSatisfied,
        RollbackGateEvaluationHash = SafeText(receipt.RollbackGateEvaluationHash),
        PatchArtifactId = receipt.PatchArtifactId,
        PatchHash = SafeText(receipt.PatchHash),
        ChangeSetHash = SafeText(receipt.ChangeSetHash),
        ControlledDryRunRequestId = receipt.ControlledDryRunRequestId,
        DryRunExecutionAuditId = receipt.DryRunExecutionAuditId,
        DryRunAuditHash = SafeText(receipt.DryRunAuditHash),
        DryRunReceiptHash = SafeText(receipt.DryRunReceiptHash),
        PolicySatisfactionId = receipt.PolicySatisfactionId,
        PolicySatisfactionHash = SafeText(receipt.PolicySatisfactionHash),
        SubjectKind = SafeText(receipt.SubjectKind),
        SubjectId = SafeText(receipt.SubjectId),
        SubjectHash = SafeText(receipt.SubjectHash),
        SourceSnapshotReference = SafeText(receipt.SourceSnapshotReference),
        SourceBaselineHash = SafeText(receipt.SourceBaselineHash),
        WorkspaceBoundaryHash = SafeText(receipt.WorkspaceBoundaryHash),
        ExpectedBranch = SafeText(receipt.ExpectedBranch),
        ExpectedCleanWorktreeHash = SafeText(receipt.ExpectedCleanWorktreeHash),
        RollbackSupportReceiptHash = SafeText(receipt.RollbackSupportReceiptHash),
        CreatedAtUtc = receipt.CreatedAtUtc,
        ExpiresAtUtc = receipt.ExpiresAtUtc,
        EvidenceReferences = receipt.EvidenceReferences.Select(SafeText).ToArray(),
        BoundaryMaxims = receipt.BoundaryMaxims.Select(SafeText).ToArray(),
        Boundary = SafeText(receipt.Boundary),
        AuthorityBoundary = RollbackSupportReceiptReadBoundaryText.AuthorityBoundary,
        Warnings = RollbackSupportReceiptReadBoundaryText.Warnings
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
