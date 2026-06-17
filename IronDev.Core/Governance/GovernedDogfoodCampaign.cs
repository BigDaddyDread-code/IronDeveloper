namespace IronDev.Core.Governance;

public static class GovernedDogfoodCampaignStatuses
{
    public const string Completed = "Completed";
    public const string CompletedWithBlockedDecisions = "CompletedWithBlockedDecisions";
    public const string CompletedWithFailedIterations = "CompletedWithFailedIterations";
    public const string Rejected = "Rejected";
}

public static class GovernedDogfoodCampaignBoundaryText
{
    public const string Boundary = """
        Governed dogfood campaign repeats explicitly supplied governed release gate requests only.
        Governed dogfood campaign is not autonomous operation.
        Governed dogfood campaign is not release approval.
        Governed dogfood campaign is not deployment approval.
        Governed dogfood campaign is not merge approval.
        Governed dogfood campaign is not release execution.
        Governed dogfood campaign is not source apply.
        Governed dogfood campaign is not rollback execution.
        Governed dogfood campaign is not workflow continuation.
        Governed dogfood campaign does not mutate workflow state.
        Governed dogfood campaign does not run git.
        Governed dogfood campaign does not create pull requests.
        Governed dogfood campaign does not call agents, models, tools, UI, memory, or retrieval.
        Governed dogfood campaign does not schedule itself.
        Human review remains required for release approval, deployment, and merge.
        """;

    public static IReadOnlyList<string> Warnings { get; } =
    [
        "Repeated governed dogfood campaign is not autonomy.",
        "Repeated governed dogfood campaign is not release approval.",
        "Campaign completed means the bounded campaign loop completed.",
        "Ready evidence satisfied count does not mean release approved.",
        "Blocked decision count does not mean campaign failure.",
        "Human review remains required for release approval, deployment, and merge."
    ];
}

public sealed record GovernedDogfoodCampaignRequest
{
    public required Guid GovernedDogfoodCampaignRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string CampaignName { get; init; }
    public required string RequestedBy { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required int MaxIterations { get; init; }
    public required IReadOnlyList<GovernedReleaseGateRequest> ReleaseGateRequests { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = GovernedDogfoodCampaignBoundaryText.Boundary;
}

public sealed record GovernedDogfoodCampaignResult
{
    public required Guid GovernedDogfoodCampaignRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string CampaignName { get; init; }
    public required bool Succeeded { get; init; }
    public required string Status { get; init; }
    public required int RequestedIterations { get; init; }
    public required int CompletedIterations { get; init; }
    public required int ReadyEvidenceSatisfiedCount { get; init; }
    public required int BlockedDecisionCount { get; init; }
    public required int FailedIterationCount { get; init; }
    public required IReadOnlyList<GovernedDogfoodCampaignIterationResult> Iterations { get; init; }
    public required IReadOnlyList<GovernedDogfoodCampaignIssue> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required bool ReleaseApproved { get; init; }
    public required bool DeploymentApproved { get; init; }
    public required bool MergeApproved { get; init; }
    public required bool ReleaseExecuted { get; init; }
    public required bool SourceApplyExecuted { get; init; }
    public required bool RollbackExecuted { get; init; }
    public required bool WorkflowContinued { get; init; }
    public required bool WorkflowMutated { get; init; }
    public required bool GitOperationExecuted { get; init; }
    public required bool HumanReviewRequiredForReleaseApproval { get; init; }
    public required bool HumanReviewRequiredForDeployment { get; init; }
    public required bool HumanReviewRequiredForMerge { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public string Boundary { get; init; } = GovernedDogfoodCampaignBoundaryText.Boundary;
}

public sealed record GovernedDogfoodCampaignIterationResult
{
    public required int IterationNumber { get; init; }
    public required Guid GovernedReleaseGateRequestId { get; init; }
    public required bool Succeeded { get; init; }
    public required string Status { get; init; }
    public required bool ReleaseReadinessGateRan { get; init; }
    public required bool DecisionRecordStored { get; init; }
    public required bool ReleaseReadinessEvidenceSatisfied { get; init; }
    public required Guid? ReleaseReadinessDecisionRecordId { get; init; }
    public required string? ReleaseReadinessDecisionRecordHash { get; init; }
    public required string? DecisionStatus { get; init; }
    public required bool ReleaseApproved { get; init; }
    public required bool DeploymentApproved { get; init; }
    public required bool MergeApproved { get; init; }
    public required bool ReleaseExecuted { get; init; }
    public required bool SourceApplyExecuted { get; init; }
    public required bool RollbackExecuted { get; init; }
    public required bool WorkflowContinued { get; init; }
    public required bool WorkflowMutated { get; init; }
    public required bool GitOperationExecuted { get; init; }
    public required bool HumanReviewRequiredForReleaseApproval { get; init; }
    public required bool HumanReviewRequiredForDeployment { get; init; }
    public required bool HumanReviewRequiredForMerge { get; init; }
    public required IReadOnlyList<GovernedDogfoodCampaignIssue> Issues { get; init; }
}

public sealed record GovernedDogfoodCampaignIssue(string Code, string Field, string Message);

public interface IGovernedDogfoodCampaignRunner
{
    Task<GovernedDogfoodCampaignResult> RunAsync(
        GovernedDogfoodCampaignRequest? request,
        CancellationToken cancellationToken = default);
}

public static class GovernedDogfoodCampaignValidation
{
    private static readonly string[] PrivateOrRawMarkers =
    [
        "raw prompt",
        "rawprompt",
        "raw completion",
        "rawcompletion",
        "raw tool output",
        "rawtooloutput",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "private reasoning",
        "hidden reasoning",
        "scratchpad",
        "entire patch",
        "entirepatch",
        "patch payload",
        "patchpayload",
        "password",
        "api_key",
        "apikey",
        "secret",
        "private key",
        "bearer"
    ];

    private static readonly string[] AuthorityMarkers =
    [
        "release approved",
        "approved for release",
        "deployment approved",
        "merge approved",
        "safe to deploy",
        "safe to merge",
        "can deploy",
        "can merge",
        "green to ship",
        "release executed",
        "source applied by decision",
        "rollback executed by decision",
        "workflow continued by decision",
        "git committed",
        "git pushed",
        "tag created",
        "pull request created",
        "memory promoted",
        "retrieval activated",
        "agent dispatched",
        "tool executed",
        "model called"
    ];

    public static IReadOnlyList<GovernedDogfoodCampaignIssue> ValidateRequest(GovernedDogfoodCampaignRequest? request)
    {
        var issues = new List<GovernedDogfoodCampaignIssue>();
        if (request is null)
        {
            Add(issues, "RequestRequired", "request", "Governed dogfood campaign request is required.");
            return issues;
        }

        RequireGuid(request.GovernedDogfoodCampaignRequestId, nameof(request.GovernedDogfoodCampaignRequestId), issues);
        RequireGuid(request.ProjectId, nameof(request.ProjectId), issues);
        RequireText(request.CampaignName, nameof(request.CampaignName), issues);
        RequireText(request.RequestedBy, nameof(request.RequestedBy), issues);
        RequireText(request.Boundary, nameof(request.Boundary), issues);
        RequireList(request.EvidenceReferences, nameof(request.EvidenceReferences), issues);
        RequireList(request.BoundaryMaxims, nameof(request.BoundaryMaxims), issues);

        if (request.RequestedAtUtc == default)
            Add(issues, "RequestedAtRequired", nameof(request.RequestedAtUtc), "Requested timestamp is required.");

        if (request.MaxIterations <= 0)
            Add(issues, "MaxIterationsRequired", nameof(request.MaxIterations), "Max iterations must be greater than zero.");
        else if (request.MaxIterations > 50)
            Add(issues, "MaxIterationsTooHigh", nameof(request.MaxIterations), "Max iterations must not exceed 50.");

        if (request.ReleaseGateRequests is null || request.ReleaseGateRequests.Count == 0)
        {
            Add(issues, "ReleaseGateRequestsRequired", nameof(request.ReleaseGateRequests), "At least one governed release gate request is required.");
            return issues;
        }

        if (request.ReleaseGateRequests.Count > request.MaxIterations)
            Add(issues, "ReleaseGateRequestsExceedMaxIterations", nameof(request.ReleaseGateRequests), "Release gate request count must not exceed max iterations.");

        var seen = new HashSet<Guid>();
        for (var index = 0; index < request.ReleaseGateRequests.Count; index++)
        {
            var releaseGateRequest = request.ReleaseGateRequests[index];
            var field = $"{nameof(request.ReleaseGateRequests)}[{index}]";
            if (releaseGateRequest is null)
            {
                Add(issues, "ReleaseGateRequestRequired", field, "Governed release gate request is required.");
                continue;
            }

            if (releaseGateRequest.ProjectId != request.ProjectId)
                Add(issues, "ReleaseGateRequestProjectMismatch", $"{field}.{nameof(releaseGateRequest.ProjectId)}", "Release gate request project must match campaign project.");

            if (!seen.Add(releaseGateRequest.GovernedReleaseGateRequestId))
                Add(issues, "DuplicateGovernedReleaseGateRequestId", $"{field}.{nameof(releaseGateRequest.GovernedReleaseGateRequestId)}", "Governed release gate request ids must be unique within a campaign.");

            foreach (var issue in GovernedReleaseGateValidation.ValidateRequest(releaseGateRequest))
                Add(issues, $"ReleaseGateRequest.{issue.Code}", $"{field}.{issue.Field}", issue.Message);
        }

        return issues;
    }

    public static bool ContainsUnsafeText(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (PrivateOrRawMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)) ||
         AuthorityMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)));

    private static void RequireGuid(Guid value, string field, List<GovernedDogfoodCampaignIssue> issues)
    {
        if (value == Guid.Empty)
            Add(issues, $"{field}Required", field, $"{field} is required.");
    }

    private static void RequireText(string? value, string field, List<GovernedDogfoodCampaignIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(issues, $"{field}Required", field, $"{field} is required.");
            return;
        }

        ScanText(value, field, issues);
    }

    private static void RequireList(IReadOnlyList<string>? values, string field, List<GovernedDogfoodCampaignIssue> issues)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
        {
            Add(issues, $"{field}Required", field, $"{field} is required.");
            return;
        }

        foreach (var value in values)
            ScanText(value, field, issues);
    }

    private static void ScanText(string? value, string field, List<GovernedDogfoodCampaignIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (PrivateOrRawMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            Add(issues, "UnsafeCampaignMaterialRejected", field, "Governed dogfood campaign request contains private, raw, prompt, scratchpad, patch, or secret-like material.");

        if (AuthorityMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            Add(issues, "AuthorityCampaignMaterialRejected", field, "Governed dogfood campaign request must not claim release, deployment, merge, execution, git, memory, retrieval, agent, model, or tool authority.");
    }

    private static void Add(List<GovernedDogfoodCampaignIssue> issues, string code, string field, string message) =>
        issues.Add(new GovernedDogfoodCampaignIssue(code, field, message));
}
