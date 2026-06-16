namespace IronDev.Core.Governance;

public static class SourceApplyDryRunBoundaryText
{
    public const string Boundary = """
        Source apply dry-run is not source apply.
        Source apply dry-run is not source mutation.
        Source apply dry-run is not patch application.
        Source apply dry-run is not git execution.
        Source apply dry-run is not workflow continuation.
        Source apply dry-run is not release readiness.
        Source apply dry-run only proves what the controlled source-apply executor would attempt later if separately authorized.
        """;
}

public sealed record SourceApplyDryRunRequest
{
    public required Guid SourceApplyDryRunRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required SourceApplyRequest SourceApplyRequest { get; init; }
    public SourceApplyDryRunWorkspaceSnapshot? WorkspaceSnapshot { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = SourceApplyDryRunBoundaryText.Boundary;
}

public sealed record SourceApplyDryRunWorkspaceSnapshot
{
    public required string SourceBaselineHash { get; init; }
    public required string WorkspaceBoundaryHash { get; init; }
    public required string ExpectedBranch { get; init; }
    public required string ExpectedCleanWorktreeHash { get; init; }
    public required IReadOnlyList<SourceApplyDryRunWorkspaceFile> Files { get; init; }
}

public sealed record SourceApplyDryRunWorkspaceFile
{
    public required string Path { get; init; }
    public required string CurrentContentHash { get; init; }
    public bool Exists { get; init; } = true;
}

public sealed record SourceApplyDryRunResult
{
    public required bool Satisfied { get; init; }
    public required Guid SourceApplyDryRunRequestId { get; init; }
    public required Guid ProjectId { get; init; }
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
    public required IReadOnlyList<SourceApplyDryRunFileResult> FileResults { get; init; }
    public required IReadOnlyList<SourceApplyDryRunIssue> Issues { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = SourceApplyDryRunBoundaryText.Boundary;
}

public sealed record SourceApplyDryRunFileResult
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
    public required IReadOnlyList<SourceApplyDryRunIssue> Issues { get; init; }
}

public sealed record SourceApplyDryRunIssue(string Code, string Field, string Message);
