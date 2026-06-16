namespace IronDev.Core.Governance;

public static class SourceApplyRequestBoundaryText
{
    public const string Boundary = """
        SourceApplyRequest is not source apply.
        SourceApplyRequest is not source mutation.
        SourceApplyRequest is not executor approval.
        SourceApplyRequest is not workflow continuation.
        SourceApplyRequest is not release readiness.
        SourceApplyRequest only records a proposed request to apply a patch artifact later under controlled execution.
        """;
}

public static class SourceApplyRequestFileOperationKinds
{
    public const string CreateFile = "CreateFile";
    public const string ModifyFile = "ModifyFile";
    public const string DeleteFile = "DeleteFile";
    public const string RenameFile = "RenameFile";
    public const string Noop = "Noop";

    public static IReadOnlyList<string> Known { get; } =
    [
        CreateFile,
        ModifyFile,
        DeleteFile,
        RenameFile,
        Noop
    ];
}

public sealed record SourceApplyRequestGateEvaluationEvidence
{
    public required Guid SourceApplyGateEvaluationId { get; init; }
    public required string SourceApplyGateEvaluationHash { get; init; }
    public required bool Satisfied { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid AcceptedApprovalId { get; init; }
    public required string AcceptedApprovalHash { get; init; }
    public required Guid PolicySatisfactionId { get; init; }
    public required string PolicySatisfactionHash { get; init; }
    public required Guid ControlledDryRunRequestId { get; init; }
    public required Guid DryRunExecutionAuditId { get; init; }
    public required string DryRunAuditHash { get; init; }
    public required string DryRunReceiptHash { get; init; }
    public required Guid PatchArtifactId { get; init; }
    public required string PatchHash { get; init; }
    public required string ChangeSetHash { get; init; }
    public required Guid RollbackSupportReceiptId { get; init; }
    public required string RollbackSupportReceiptHash { get; init; }
    public required Guid RollbackPlanId { get; init; }
    public required string RollbackPlanHash { get; init; }
    public required string RollbackGateEvaluationHash { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string SubjectHash { get; init; }
    public required string SourceSnapshotReference { get; init; }
    public required string SourceBaselineHash { get; init; }
    public required string WorkspaceBoundaryHash { get; init; }
    public required string ExpectedBranch { get; init; }
    public required string ExpectedCleanWorktreeHash { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = SourceApplyGateBoundaryText.Boundary;
}

public sealed record SourceApplyRequest
{
    public required Guid SourceApplyRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid SourceApplyGateEvaluationId { get; init; }
    public required string SourceApplyGateEvaluationHash { get; init; }
    public required bool SourceApplyGateSatisfied { get; init; }
    public required SourceApplyRequestGateEvaluationEvidence SourceApplyGateEvaluation { get; init; }
    public required Guid AcceptedApprovalId { get; init; }
    public required string AcceptedApprovalHash { get; init; }
    public required Guid PolicySatisfactionId { get; init; }
    public required string PolicySatisfactionHash { get; init; }
    public required Guid ControlledDryRunRequestId { get; init; }
    public required Guid DryRunExecutionAuditId { get; init; }
    public required string DryRunAuditHash { get; init; }
    public required string DryRunReceiptHash { get; init; }
    public required Guid PatchArtifactId { get; init; }
    public required string PatchHash { get; init; }
    public required string ChangeSetHash { get; init; }
    public required Guid RollbackSupportReceiptId { get; init; }
    public required string RollbackSupportReceiptHash { get; init; }
    public required Guid RollbackPlanId { get; init; }
    public required string RollbackPlanHash { get; init; }
    public required string RollbackGateEvaluationHash { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string SubjectHash { get; init; }
    public required string SourceSnapshotReference { get; init; }
    public required string SourceBaselineHash { get; init; }
    public required string WorkspaceBoundaryHash { get; init; }
    public required string ExpectedBranch { get; init; }
    public required string ExpectedCleanWorktreeHash { get; init; }
    public required IReadOnlyList<SourceApplyRequestFileOperation> FileOperations { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required string SourceApplyRequestHash { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = SourceApplyRequestBoundaryText.Boundary;
}

public sealed record SourceApplyRequestFileOperation
{
    public required string Path { get; init; }
    public required string OperationKind { get; init; }
    public string? PreviousPath { get; init; }
    public string? BeforeContentHash { get; init; }
    public string? AfterContentHash { get; init; }
    public string? DiffHash { get; init; }
    public required string PatchArtifactChangeHash { get; init; }
    public required string OperationHash { get; init; }
}

public sealed record SourceApplyRequestValidationIssue(string Code, string Field, string Message);

public sealed record SourceApplyRequestValidationResult(IReadOnlyList<SourceApplyRequestValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}
