namespace IronDev.Core.Governance;

public static class RollbackPlanBoundaryText
{
    public const string Boundary = """
        Rollback plan is not rollback execution.
        Rollback plan is not rollback success.
        Rollback plan is not source apply.
        Rollback plan is not workflow continuation.
        Rollback plan is not release readiness.
        Rollback plan does not authorize source mutation by itself.
        Rollback plan defines the intended escape hatch only.
        Real source apply must require rollback support before mutation.
        """;
}

public sealed record RollbackPlan
{
    public required Guid RollbackPlanId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string RollbackPlanKind { get; init; }
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
    public required string RollbackPlanHash { get; init; }
    public required IReadOnlyList<RollbackPlanFileAction> FileActions { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = RollbackPlanBoundaryText.Boundary;
}

public sealed record RollbackPlanFileAction
{
    public required string Path { get; init; }
    public string? PreviousPath { get; init; }
    public required string PlannedActionKind { get; init; }
    public string? RestoreContentHash { get; init; }
    public string? DeleteContentHash { get; init; }
    public required string ExpectedCurrentContentHash { get; init; }
    public required string RollbackActionHash { get; init; }
    public bool IsBinary { get; init; }
}
