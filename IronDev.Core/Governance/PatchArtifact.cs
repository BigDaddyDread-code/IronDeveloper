namespace IronDev.Core.Governance;

public static class PatchArtifactBoundaryText
{
    public const string Boundary = """
        Patch artifact is not source apply.
        Patch artifact is not rollback.
        Patch artifact is not workflow continuation.
        Patch artifact is not release readiness.
        Patch artifact does not authorize source mutation by itself.
        Patch artifact is a proposed change package only.
        Patch artifact must be reviewed before source apply.
        Patch artifact must remain bound to its dry-run receipt and source baseline.
        """;
}

public sealed record PatchArtifact
{
    public required Guid PatchArtifactId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string PatchArtifactKind { get; init; }
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
    public required string ValidationPlanId { get; init; }
    public required string ValidationPlanHash { get; init; }
    public required string PatchHash { get; init; }
    public required string ChangeSetHash { get; init; }
    public required IReadOnlyList<PatchArtifactFileChange> FileChanges { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = PatchArtifactBoundaryText.Boundary;
}

public sealed record PatchArtifactFileChange
{
    public required string Path { get; init; }
    public string? PreviousPath { get; init; }
    public required string ChangeKind { get; init; }
    public string? BeforeContentHash { get; init; }
    public string? AfterContentHash { get; init; }
    public required string DiffHash { get; init; }
    public required string NormalizedDiff { get; init; }
    public bool IsBinary { get; init; }
}
