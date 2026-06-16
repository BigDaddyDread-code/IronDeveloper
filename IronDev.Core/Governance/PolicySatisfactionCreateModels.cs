using System.Security.Claims;

namespace IronDev.Core.Governance;

public static class PolicySatisfactionCreateBoundaryText
{
    public const string AuthorityBoundary = "Created policy satisfaction is durable evidence only; it is not dry-run execution, patch artifact creation, source apply, rollback, workflow continuation, or release readiness.";

    public static IReadOnlyList<string> Warnings { get; } =
    [
        "Policy satisfaction record creation is not dry-run execution.",
        "Policy satisfaction record creation is not patch artifact creation.",
        "Policy satisfaction record creation is not source apply.",
        "Policy satisfaction record creation is not rollback.",
        "Policy satisfaction record creation is not workflow continuation.",
        "Policy satisfaction record creation is not release readiness.",
        "Created policy satisfaction does not authorize execution by itself.",
        "Human review remains required for source apply and memory promotion."
    ];
}

public sealed record PolicySatisfactionCreateBoundary
{
    public bool PolicySatisfactionCreateRunsDryRun { get; init; }
    public bool PolicySatisfactionCreateCreatesPatchArtifact { get; init; }
    public bool PolicySatisfactionCreateAppliesSource { get; init; }
    public bool PolicySatisfactionCreateExecutesRollback { get; init; }
    public bool PolicySatisfactionCreateContinuesWorkflow { get; init; }
    public bool PolicySatisfactionCreateApprovesRelease { get; init; }
    public bool PolicySatisfactionCreateAuthorizesExecution { get; init; }
    public bool HumanReviewRequiredForSourceApply { get; init; } = true;
    public bool HumanReviewRequiredForMemoryPromotion { get; init; } = true;
}

public sealed record PolicySatisfactionCreateRequest(
    PolicyRequirement? PolicyRequirement,
    ApprovalSatisfactionEvaluation? ApprovalSatisfactionEvaluation,
    string? PolicyRequirementHash,
    DateTimeOffset? ExpiresAtUtc,
    string? CorrelationId,
    string? CausationId,
    IReadOnlyList<string>? EvidenceReferences,
    IReadOnlyList<string>? BoundaryMaxims,
    string? ClientRequestId);

public sealed record PolicySatisfactionCreateIssue(string Code, string Field, string Message);

public sealed record PolicySatisfactionCreateResult
{
    public PolicySatisfactionReadModel? PolicySatisfaction { get; init; }
    public IReadOnlyList<PolicySatisfactionCreateIssue> Issues { get; init; } = [];
    public bool IsConflict { get; init; }
    public bool IsSuccess => PolicySatisfaction is not null && Issues.Count == 0;
}

public interface IPolicySatisfactionCreateService
{
    Task<PolicySatisfactionCreateResult> CreateAsync(
        Guid routeProjectId,
        PolicySatisfactionCreateRequest? request,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);
}