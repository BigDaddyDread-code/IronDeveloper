namespace IronDev.Core.Governance;

public static class PatchBaseHashValidationBoundaryText
{
    public const string Boundary = """
        Patch base/hash validation is not patch artifact creation.
        Patch base/hash validation is not source apply.
        Patch base/hash validation is not rollback.
        Patch base/hash validation is not workflow continuation.
        Patch base/hash validation is not release readiness.
        Patch base/hash validation does not authorize source mutation by itself.
        Patch base/hash validation only verifies artifact binding and hashes.
        """;
}

public sealed record PatchBaseHashValidationContext
{
    public required PatchArtifact PatchArtifact { get; init; }
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
    public required string ValidationPlanId { get; init; }
    public required string ValidationPlanHash { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
}

public sealed record PatchBaseHashValidationIssue(
    string Code,
    string Field,
    string Message);

public sealed record PatchBaseHashValidationResult(
    IReadOnlyList<PatchBaseHashValidationIssue> Issues,
    string? ComputedChangeSetHash,
    string? ComputedPatchHash)
{
    public bool IsValid => Issues.Count == 0;
}
