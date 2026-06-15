using System.Security.Claims;

namespace IronDev.Core.Governance;

public static class AcceptedApprovalCreateBoundaryText
{
    public const string AuthorityBoundary = "Accepted approval is necessary but not sufficient for policy satisfaction, source apply, workflow continuation, or release readiness.";

    public static IReadOnlyList<string> Warnings { get; } =
    [
        "Accepted approval creation is not policy satisfaction.",
        "Accepted approval creation is not dry-run execution.",
        "Accepted approval creation is not patch artifact creation.",
        "Accepted approval creation is not source apply.",
        "Accepted approval creation is not workflow continuation.",
        "Accepted approval creation is not release readiness.",
        "Creating an accepted approval record does not authorize execution.",
        "Human review remains required for source apply and memory promotion."
    ];
}

public sealed record AcceptedApprovalCreateBoundary
{
    public bool AcceptedApprovalCreateSatisfiesPolicy { get; init; }
    public bool AcceptedApprovalCreateRunsDryRun { get; init; }
    public bool AcceptedApprovalCreateCreatesPatchArtifact { get; init; }
    public bool AcceptedApprovalCreateAppliesSource { get; init; }
    public bool AcceptedApprovalCreateContinuesWorkflow { get; init; }
    public bool AcceptedApprovalCreateApprovesRelease { get; init; }
    public bool AcceptedApprovalCreateAuthorizesExecution { get; init; }
    public bool HumanReviewRequiredForSourceApply { get; init; } = true;
    public bool HumanReviewRequiredForMemoryPromotion { get; init; } = true;
}

public sealed record CreateAcceptedApprovalRequest(
    string? ApprovalTargetKind,
    string? ApprovalTargetId,
    string? ApprovalTargetHash,
    string? CapabilityCode,
    string? ApprovalPurpose,
    DateTimeOffset? ExpiresAtUtc,
    string? CorrelationId,
    string? CausationId,
    IReadOnlyList<string>? EvidenceReferences,
    IReadOnlyList<string>? BoundaryMaxims,
    string? ClientRequestId);

public sealed record AcceptedApprovalCreateIssue(string Code, string Field, string Message);

public sealed record AcceptedApprovalCreateResult
{
    public AcceptedApprovalReadModel? AcceptedApproval { get; init; }
    public IReadOnlyList<AcceptedApprovalCreateIssue> Issues { get; init; } = [];
    public bool IsSuccess => AcceptedApproval is not null && Issues.Count == 0;
}

public interface IAcceptedApprovalCreateService
{
    Task<AcceptedApprovalCreateResult> CreateAsync(
        Guid projectId,
        CreateAcceptedApprovalRequest? request,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);
}
