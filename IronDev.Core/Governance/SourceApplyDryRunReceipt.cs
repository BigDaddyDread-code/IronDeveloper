namespace IronDev.Core.Governance;

public static class SourceApplyDryRunReceiptBoundaryText
{
    public const string Boundary = """
        SourceApplyDryRunReceipt is not source apply.
        SourceApplyDryRunReceipt is not source mutation.
        SourceApplyDryRunReceipt is not patch application.
        SourceApplyDryRunReceipt is not git execution.
        SourceApplyDryRunReceipt is not a real source-apply receipt.
        SourceApplyDryRunReceipt is not workflow continuation.
        SourceApplyDryRunReceipt is not release readiness.
        SourceApplyDryRunReceipt only records what the dry-run said would be attempted later if separately authorized.
        """;
}

public sealed record SourceApplyDryRunReceipt
{
    public required Guid SourceApplyDryRunReceiptId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid SourceApplyDryRunRequestId { get; init; }
    public required string SourceApplyDryRunRequestHash { get; init; }
    public required bool DryRunSatisfied { get; init; }
    public required string DryRunResultHash { get; init; }
    public required Guid SourceApplyRequestId { get; init; }
    public required string SourceApplyRequestHash { get; init; }
    public required Guid SourceApplyGateEvaluationId { get; init; }
    public required string SourceApplyGateEvaluationHash { get; init; }
    public required Guid PatchArtifactId { get; init; }
    public required string PatchHash { get; init; }
    public required string ChangeSetHash { get; init; }
    public required Guid RollbackSupportReceiptId { get; init; }
    public required string RollbackSupportReceiptHash { get; init; }
    public required string SourceBaselineHash { get; init; }
    public required string WorkspaceBoundaryHash { get; init; }
    public required string ExpectedBranch { get; init; }
    public required string ExpectedCleanWorktreeHash { get; init; }
    public required IReadOnlyList<SourceApplyDryRunReceiptFileResult> FileResults { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required string SourceApplyDryRunReceiptHash { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = SourceApplyDryRunReceiptBoundaryText.Boundary;
}

public sealed record SourceApplyDryRunReceiptFileResult
{
    public required string Path { get; init; }
    public string? PreviousPath { get; init; }
    public required string OperationKind { get; init; }
    public required string PatchArtifactChangeHash { get; init; }
    public required string OperationHash { get; init; }
    public string? ExpectedBeforeContentHash { get; init; }
    public string? ExpectedAfterContentHash { get; init; }
    public string? ObservedCurrentContentHash { get; init; }
    public required bool PreconditionsSatisfied { get; init; }
    public required bool WouldCreate { get; init; }
    public required bool WouldModify { get; init; }
    public required bool WouldDelete { get; init; }
    public required bool WouldRename { get; init; }
    public required bool WouldNoop { get; init; }
    public required IReadOnlyList<string> IssueCodes { get; init; }
    public required string FileResultHash { get; init; }
}

public sealed record SourceApplyDryRunReceiptValidationIssue(string Code, string Field, string Message);

public sealed record SourceApplyDryRunReceiptValidationResult(IReadOnlyList<SourceApplyDryRunReceiptValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}
