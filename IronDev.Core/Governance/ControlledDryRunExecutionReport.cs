namespace IronDev.Core.Governance;

public static class ControlledDryRunExecutionBoundaryText
{
    public const string Boundary = """
        Controlled dry-run execution is not patch artifact creation.
        Controlled dry-run execution is not source apply.
        Controlled dry-run execution is not rollback.
        Controlled dry-run execution is not workflow continuation.
        Controlled dry-run execution is not release readiness.
        Controlled dry-run execution does not authorize source mutation by itself.
        Controlled dry-run report is in-memory only in PR182.
        """;
}

public sealed record ControlledDryRunExecutionReport
{
    public required Guid ControlledDryRunRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string WorkspaceId { get; init; }
    public required string WorkspaceBoundaryHash { get; init; }
    public required string ValidationPlanId { get; init; }
    public required string ValidationPlanHash { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public required IReadOnlyList<ControlledDryRunCommandReport> CommandReports { get; init; }
    public required bool DryRunCompleted { get; init; }
    public required bool DryRunSucceeded { get; init; }
    public required string Boundary { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed record ControlledDryRunCommandReport
{
    public required string CommandId { get; init; }
    public required int ExitCode { get; init; }
    public required bool TimedOut { get; init; }
    public required string StandardOutputSummary { get; init; }
    public required string StandardErrorSummary { get; init; }
}
