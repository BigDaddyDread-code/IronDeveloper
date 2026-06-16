namespace IronDev.Core.Governance;

public static class SourceApplyGateBoundaryText
{
    public const string Boundary = """
        Source apply gate satisfaction is not source apply.
        Source apply gate satisfaction is not source mutation.
        Source apply gate satisfaction is not workflow continuation.
        Source apply gate satisfaction is not release readiness.
        Source apply gate satisfaction does not execute git.
        Source apply gate satisfaction only proves the required pre-apply evidence chain is internally consistent.
        """;
}

public sealed record SourceApplyGateAcceptedApprovalEvidence
{
    public required Guid ProjectId { get; init; }
    public required Guid AcceptedApprovalId { get; init; }
    public required string AcceptedApprovalHash { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string SubjectHash { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
}

public sealed record SourceApplyGatePolicySatisfactionEvidence
{
    public required Guid ProjectId { get; init; }
    public required Guid PolicySatisfactionId { get; init; }
    public required string PolicySatisfactionHash { get; init; }
    public required Guid AcceptedApprovalId { get; init; }
    public required string AcceptedApprovalHash { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string SubjectHash { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
}

public sealed record SourceApplyGateDryRunEvidence
{
    public required Guid ProjectId { get; init; }
    public required Guid ControlledDryRunRequestId { get; init; }
    public required Guid DryRunExecutionAuditId { get; init; }
    public required string DryRunAuditHash { get; init; }
    public required string DryRunReceiptHash { get; init; }
    public required Guid PolicySatisfactionId { get; init; }
    public required string PolicySatisfactionHash { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string SubjectHash { get; init; }
    public required string SourceSnapshotReference { get; init; }
    public required string SourceBaselineHash { get; init; }
    public required string WorkspaceBoundaryHash { get; init; }
    public required string ExpectedBranch { get; init; }
    public required string ExpectedCleanWorktreeHash { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
}

public sealed record SourceApplyGatePatchArtifactEvidence
{
    public required Guid ProjectId { get; init; }
    public required Guid PatchArtifactId { get; init; }
    public required string PatchHash { get; init; }
    public required string ChangeSetHash { get; init; }
    public required Guid ControlledDryRunRequestId { get; init; }
    public required Guid DryRunExecutionAuditId { get; init; }
    public required string DryRunAuditHash { get; init; }
    public required string DryRunReceiptHash { get; init; }
    public required Guid PolicySatisfactionId { get; init; }
    public required string PolicySatisfactionHash { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string SubjectHash { get; init; }
    public required string SourceSnapshotReference { get; init; }
    public required string SourceBaselineHash { get; init; }
    public required string WorkspaceBoundaryHash { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
}

public sealed record SourceApplyGateRollbackSupportEvidence
{
    public required Guid ProjectId { get; init; }
    public required Guid RollbackSupportReceiptId { get; init; }
    public required string RollbackSupportReceiptHash { get; init; }
    public required Guid RollbackPlanId { get; init; }
    public required string RollbackPlanHash { get; init; }
    public required string RollbackGateEvaluationHash { get; init; }
    public required bool RollbackGateSatisfied { get; init; }
    public required Guid PatchArtifactId { get; init; }
    public required string PatchHash { get; init; }
    public required string ChangeSetHash { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string SubjectHash { get; init; }
    public required string SourceSnapshotReference { get; init; }
    public required string SourceBaselineHash { get; init; }
    public required string WorkspaceBoundaryHash { get; init; }
    public required string ExpectedBranch { get; init; }
    public required string ExpectedCleanWorktreeHash { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
}

public sealed record SourceApplyGateEvaluationRequest
{
    public required Guid ProjectId { get; init; }
    public required Guid AcceptedApprovalId { get; init; }
    public required string AcceptedApprovalHash { get; init; }
    public required SourceApplyGateAcceptedApprovalEvidence AcceptedApproval { get; init; }
    public required Guid PolicySatisfactionId { get; init; }
    public required string PolicySatisfactionHash { get; init; }
    public required SourceApplyGatePolicySatisfactionEvidence PolicySatisfaction { get; init; }
    public required Guid ControlledDryRunRequestId { get; init; }
    public required Guid DryRunExecutionAuditId { get; init; }
    public required string DryRunAuditHash { get; init; }
    public required string DryRunReceiptHash { get; init; }
    public required SourceApplyGateDryRunEvidence ControlledDryRun { get; init; }
    public required Guid PatchArtifactId { get; init; }
    public required string PatchHash { get; init; }
    public required string ChangeSetHash { get; init; }
    public required SourceApplyGatePatchArtifactEvidence PatchArtifact { get; init; }
    public required Guid RollbackSupportReceiptId { get; init; }
    public required string RollbackSupportReceiptHash { get; init; }
    public required Guid RollbackPlanId { get; init; }
    public required string RollbackPlanHash { get; init; }
    public required string RollbackGateEvaluationHash { get; init; }
    public required SourceApplyGateRollbackSupportEvidence RollbackSupport { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string SubjectHash { get; init; }
    public required string SourceSnapshotReference { get; init; }
    public required string SourceBaselineHash { get; init; }
    public required string WorkspaceBoundaryHash { get; init; }
    public required string ExpectedBranch { get; init; }
    public required string ExpectedCleanWorktreeHash { get; init; }
    public DateTimeOffset? EvaluatedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = SourceApplyGateBoundaryText.Boundary;
}

public sealed record SourceApplyGateEvaluationIssue(string Code, string Field, string Message);

public sealed record SourceApplyGateEvaluationResult
{
    public required bool Satisfied { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid PatchArtifactId { get; init; }
    public required string PatchHash { get; init; }
    public required string ChangeSetHash { get; init; }
    public required Guid RollbackSupportReceiptId { get; init; }
    public required string SourceBaselineHash { get; init; }
    public required string ExpectedBranch { get; init; }
    public required string ExpectedCleanWorktreeHash { get; init; }
    public required IReadOnlyList<SourceApplyGateEvaluationIssue> Issues { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = SourceApplyGateBoundaryText.Boundary;
}
