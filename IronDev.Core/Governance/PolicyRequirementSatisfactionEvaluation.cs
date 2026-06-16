namespace IronDev.Core.Governance;

public static class PolicyRequirementSatisfactionBoundaryText
{
    public const string Boundary = """
        Policy requirement satisfaction evaluation is not a policy satisfaction record.
        Policy requirement satisfaction evaluation is not dry-run execution.
        Policy requirement satisfaction evaluation is not patch artifact creation.
        Policy requirement satisfaction evaluation is not source apply.
        Policy requirement satisfaction evaluation is not rollback.
        Policy requirement satisfaction evaluation is not workflow continuation.
        Policy requirement satisfaction evaluation is not release readiness.
        Satisfied policy requirement does not authorize execution.
        """;
}

public sealed record PolicyRequirementSatisfactionIssue(string Code, string Field, string Message);

public sealed record PolicyRequirementSatisfactionEvaluation
{
    public required bool IsSatisfied { get; init; }
    public Guid? AcceptedApprovalId { get; init; }
    public string? ApprovalRequirementHash { get; init; }
    public string? PolicyRequirementHash { get; init; }
    public required IReadOnlyList<PolicyRequirementSatisfactionIssue> Issues { get; init; }
    public string Boundary { get; init; } = PolicyRequirementSatisfactionBoundaryText.Boundary;
}
