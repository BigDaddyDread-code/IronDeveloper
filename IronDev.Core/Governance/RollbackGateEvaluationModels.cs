namespace IronDev.Core.Governance;

public static class RollbackGateBoundaryText
{
    public const string Boundary = """
        Rollback gate evaluation is not rollback execution.
        Rollback gate evaluation is not rollback success.
        Rollback gate evaluation is not source apply.
        Rollback gate evaluation is not workflow continuation.
        Rollback gate evaluation is not release readiness.
        Rollback gate evaluation does not authorize source mutation by itself.
        Rollback gate satisfied means only that rollback support exists for the patch artifact.
        Real source apply must still pass the source-apply gate before mutation.
        """;
}

public sealed record RollbackGateEvaluationRequest
{
    public required Guid ProjectId { get; init; }
    public required PatchArtifact PatchArtifact { get; init; }
    public required RollbackPlan RollbackPlan { get; init; }
    public required string ExpectedBranch { get; init; }
    public required string ExpectedCleanWorktreeHash { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = RollbackGateBoundaryText.Boundary;
}

public sealed record RollbackGateEvaluationIssue(string Code, string Field, string Message);

public sealed record RollbackGateEvaluationResult
{
    public required bool Satisfied { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid PatchArtifactId { get; init; }
    public required string PatchHash { get; init; }
    public required string ChangeSetHash { get; init; }
    public required Guid RollbackPlanId { get; init; }
    public required string RollbackPlanHash { get; init; }
    public required string SourceBaselineHash { get; init; }
    public required string ExpectedBranch { get; init; }
    public required string ExpectedCleanWorktreeHash { get; init; }
    public required IReadOnlyList<RollbackGateEvaluationIssue> Issues { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = RollbackGateBoundaryText.Boundary;
}
