namespace IronDev.Core.Governance;

public static class ControlledDryRunRequestBoundaryText
{
    public const string Boundary = """
        Controlled dry-run request is not dry-run execution.
        Controlled dry-run request is not a dry-run result.
        Controlled dry-run request is not patch artifact creation.
        Controlled dry-run request is not source apply.
        Controlled dry-run request is not rollback.
        Controlled dry-run request is not workflow continuation.
        Controlled dry-run request is not release readiness.
        Controlled dry-run request does not authorize execution by itself.
        Policy satisfaction is an input to controlled dry-run request.
        Policy satisfaction is not controlled dry-run execution.
        """;
}

public sealed record ControlledDryRunRequest
{
    public required Guid ControlledDryRunRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid PolicySatisfactionId { get; init; }
    public required string PolicySatisfactionHash { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string SubjectHash { get; init; }
    public required string CapabilityCode { get; init; }
    public required string WorkspaceKind { get; init; }
    public required string WorkspaceId { get; init; }
    public required string WorkspaceBoundaryHash { get; init; }
    public required string RequestedOperation { get; init; }
    public required string RequestedOperationHash { get; init; }
    public required string ValidationPlanKind { get; init; }
    public required string ValidationPlanId { get; init; }
    public required string ValidationPlanHash { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required string CorrelationId { get; init; }
    public required string CausationId { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = ControlledDryRunRequestBoundaryText.Boundary;
}
