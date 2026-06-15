namespace IronDev.Core.Governance;

public static class PolicySatisfactionBoundaryText
{
    public const string Boundary = """
        Accepted approval is an input to policy satisfaction.
        Approval satisfaction evaluation is an input to policy satisfaction.
        Accepted approval is not policy satisfaction.
        Satisfied approval requirement is not policy satisfaction.
        Policy satisfaction record is not dry-run execution.
        Policy satisfaction record is not patch artifact creation.
        Policy satisfaction record is not source apply.
        Policy satisfaction record is not rollback.
        Policy satisfaction record is not workflow continuation.
        Policy satisfaction record is not release readiness.
        Policy satisfaction record does not authorize execution by itself.
        """;
}

public sealed record PolicySatisfactionRecord
{
    public required Guid PolicySatisfactionId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string PolicyCode { get; init; }
    public required string PolicyVersion { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string SubjectHash { get; init; }
    public required string CapabilityCode { get; init; }
    public required Guid AcceptedApprovalId { get; init; }
    public required string ApprovalRequirementHash { get; init; }
    public required DateTimeOffset ApprovalEvaluatedAtUtc { get; init; }
    public required DateTimeOffset SatisfiedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required string CorrelationId { get; init; }
    public required string CausationId { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = PolicySatisfactionBoundaryText.Boundary;
}
