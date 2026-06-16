namespace IronDev.Core.Governance;

public static class ApprovalSatisfactionBoundaryText
{
    public const string Boundary = """
        Approval satisfaction evaluation is not policy satisfaction.
        Approval satisfaction evaluation is not dry-run execution.
        Approval satisfaction evaluation is not patch artifact creation.
        Approval satisfaction evaluation is not source apply.
        Approval satisfaction evaluation is not workflow continuation.
        Approval satisfaction evaluation is not release readiness.
        Satisfied approval requirement does not authorize execution.
        """;
}

public sealed record ApprovalSatisfactionIssue(string Code, string Field, string Message);

public sealed record ApprovalSatisfactionEvaluation
{
    public required bool IsSatisfied { get; init; }
    public Guid? AcceptedApprovalId { get; init; }
    public IReadOnlyList<string> EvidenceReferences { get; init; } = [];
    public IReadOnlyList<string> BoundaryMaxims { get; init; } = [];
    public required IReadOnlyList<ApprovalSatisfactionIssue> Issues { get; init; }
    public string Boundary { get; init; } = ApprovalSatisfactionBoundaryText.Boundary;
}
