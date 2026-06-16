namespace IronDev.Core.Governance;

public static class RollbackSupportReceiptBoundaryText
{
    public const string Boundary = """
        Rollback support receipt is not rollback execution.
        Rollback support receipt is not rollback success.
        Rollback support receipt is not source apply.
        Rollback support receipt is not workflow continuation.
        Rollback support receipt is not release readiness.
        Rollback support receipt does not authorize source mutation by itself.
        Rollback support receipt records that rollback support existed for a patch artifact.
        Real source apply must still pass the source-apply gate before mutation.
        """;
}

public sealed record RollbackSupportReceipt
{
    public required Guid RollbackSupportReceiptId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid RollbackPlanId { get; init; }
    public required string RollbackPlanHash { get; init; }
    public required bool RollbackGateSatisfied { get; init; }
    public required string RollbackGateEvaluationHash { get; init; }
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
    public required string ExpectedBranch { get; init; }
    public required string ExpectedCleanWorktreeHash { get; init; }
    public required string RollbackSupportReceiptHash { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = RollbackSupportReceiptBoundaryText.Boundary;
}
