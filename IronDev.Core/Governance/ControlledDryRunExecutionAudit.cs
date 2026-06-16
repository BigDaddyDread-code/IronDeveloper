namespace IronDev.Core.Governance;

public static class ControlledDryRunExecutionAuditBoundaryText
{
    public const string Boundary = """
        Dry-run execution audit is not dry-run execution.
        Dry-run execution audit is not dry-run result persistence.
        Dry-run execution audit is not patch artifact creation.
        Dry-run execution audit is not source apply.
        Dry-run execution audit is not rollback.
        Dry-run execution audit is not workflow continuation.
        Dry-run execution audit is not release readiness.
        Dry-run execution audit does not authorize source mutation by itself.
        Dry-run execution audit records evidence only.
        """;
}

public sealed record ControlledDryRunExecutionAudit
{
    public required Guid DryRunExecutionAuditId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid ControlledDryRunRequestId { get; init; }
    public required Guid PolicySatisfactionId { get; init; }
    public required string PolicySatisfactionHash { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string SubjectHash { get; init; }
    public required string WorkspaceId { get; init; }
    public required string WorkspaceKind { get; init; }
    public required string WorkspaceBoundaryHash { get; init; }
    public required string SourceSnapshotReference { get; init; }
    public required string ValidationPlanId { get; init; }
    public required string ValidationPlanHash { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public required bool DryRunCompleted { get; init; }
    public required bool DryRunSucceeded { get; init; }
    public required string ExecutionReportHash { get; init; }
    public required string AuditHash { get; init; }
    public required IReadOnlyList<ControlledDryRunCommandAudit> CommandAudits { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = ControlledDryRunExecutionAuditBoundaryText.Boundary;
}

public sealed record ControlledDryRunCommandAudit
{
    public required string CommandId { get; init; }
    public required string WorkingDirectory { get; init; }
    public required string Executable { get; init; }
    public required string CommandHash { get; init; }
    public required int ExitCode { get; init; }
    public required bool TimedOut { get; init; }
    public required string StandardOutputSummaryHash { get; init; }
    public required string StandardErrorSummaryHash { get; init; }
    public required string StandardOutputSummary { get; init; }
    public required string StandardErrorSummary { get; init; }
}
