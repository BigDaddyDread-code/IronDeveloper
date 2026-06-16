namespace IronDev.Core.Governance;

public static class ControlledDryRunReceiptWriteBoundaryText
{
    public const string Boundary = """
        Dry-run receipt write integration is not patch artifact creation.
        Dry-run receipt write integration is not source apply.
        Dry-run receipt write integration is not rollback.
        Dry-run receipt write integration is not workflow continuation.
        Dry-run receipt write integration is not release readiness.
        Dry-run receipt write integration does not authorize source mutation by itself.
        Dry-run receipt write integration records cage-run evidence only.
        """;
}

public sealed record ControlledDryRunReceiptWriteResult
{
    public required Guid DryRunExecutionAuditId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid ControlledDryRunRequestId { get; init; }
    public required string ExecutionReportHash { get; init; }
    public required string AuditHash { get; init; }
    public required bool DryRunCompleted { get; init; }
    public required bool DryRunSucceeded { get; init; }
    public required ControlledDryRunExecutionAudit Audit { get; init; }
    public required string Boundary { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}
