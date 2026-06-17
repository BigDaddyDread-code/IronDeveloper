using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class GovernedDogfoodCampaignRunner : IGovernedDogfoodCampaignRunner
{
    private readonly IGovernedReleaseGateService _releaseGateService;

    public GovernedDogfoodCampaignRunner(IGovernedReleaseGateService releaseGateService)
    {
        _releaseGateService = releaseGateService ?? throw new ArgumentNullException(nameof(releaseGateService));
    }

    public async Task<GovernedDogfoodCampaignResult> RunAsync(
        GovernedDogfoodCampaignRequest? request,
        CancellationToken cancellationToken = default)
    {
        var issues = GovernedDogfoodCampaignValidation.ValidateRequest(request);
        if (request is null || issues.Count > 0)
            return Rejected(request, issues);

        var iterations = new List<GovernedDogfoodCampaignIterationResult>();
        var readyEvidenceSatisfiedCount = 0;
        var blockedDecisionCount = 0;
        var failedIterationCount = 0;

        for (var index = 0; index < request.ReleaseGateRequests.Count && index < request.MaxIterations; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            GovernedReleaseGateResult releaseGateResult;
            try
            {
                releaseGateResult = await _releaseGateService
                    .EvaluateAsync(request.ReleaseGateRequests[index], cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                failedIterationCount++;
                iterations.Add(FailedIteration(index + 1, request.ReleaseGateRequests[index].GovernedReleaseGateRequestId));
                break;
            }

            var iteration = MapIteration(index + 1, request.ReleaseGateRequests[index].GovernedReleaseGateRequestId, releaseGateResult);
            iterations.Add(iteration);

            if (!iteration.Succeeded || !iteration.DecisionRecordStored)
            {
                failedIterationCount++;
                continue;
            }

            if (string.Equals(iteration.DecisionStatus, ReleaseReadinessDecisionStatuses.ReadyEvidenceSatisfied, StringComparison.Ordinal))
                readyEvidenceSatisfiedCount++;
            else if (IsBlockedDecision(iteration.DecisionStatus))
                blockedDecisionCount++;
        }

        var status = failedIterationCount > 0
            ? GovernedDogfoodCampaignStatuses.CompletedWithFailedIterations
            : blockedDecisionCount > 0
                ? GovernedDogfoodCampaignStatuses.CompletedWithBlockedDecisions
                : GovernedDogfoodCampaignStatuses.Completed;

        return new GovernedDogfoodCampaignResult
        {
            GovernedDogfoodCampaignRequestId = request.GovernedDogfoodCampaignRequestId,
            ProjectId = request.ProjectId,
            CampaignName = SafeText(request.CampaignName),
            Succeeded = failedIterationCount == 0,
            Status = status,
            RequestedIterations = request.ReleaseGateRequests.Count,
            CompletedIterations = iterations.Count,
            ReadyEvidenceSatisfiedCount = readyEvidenceSatisfiedCount,
            BlockedDecisionCount = blockedDecisionCount,
            FailedIterationCount = failedIterationCount,
            Iterations = iterations,
            Issues = failedIterationCount == 0 ? [] : [new GovernedDogfoodCampaignIssue("CampaignIterationFailed", nameof(GovernedReleaseGateResult), "At least one governed release gate iteration failed.")],
            Warnings = GovernedDogfoodCampaignBoundaryText.Warnings,
            ReleaseApproved = false,
            DeploymentApproved = false,
            MergeApproved = false,
            ReleaseExecuted = false,
            SourceApplyExecuted = false,
            RollbackExecuted = false,
            WorkflowContinued = false,
            WorkflowMutated = false,
            GitOperationExecuted = false,
            HumanReviewRequiredForReleaseApproval = true,
            HumanReviewRequiredForDeployment = true,
            HumanReviewRequiredForMerge = true,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Boundary = GovernedDogfoodCampaignBoundaryText.Boundary
        };
    }

    private static GovernedDogfoodCampaignResult Rejected(
        GovernedDogfoodCampaignRequest? request,
        IReadOnlyList<GovernedDogfoodCampaignIssue> issues) =>
        new()
        {
            GovernedDogfoodCampaignRequestId = request?.GovernedDogfoodCampaignRequestId ?? Guid.Empty,
            ProjectId = request?.ProjectId ?? Guid.Empty,
            CampaignName = string.Empty,
            Succeeded = false,
            Status = GovernedDogfoodCampaignStatuses.Rejected,
            RequestedIterations = request?.ReleaseGateRequests?.Count ?? 0,
            CompletedIterations = 0,
            ReadyEvidenceSatisfiedCount = 0,
            BlockedDecisionCount = 0,
            FailedIterationCount = 0,
            Iterations = [],
            Issues = issues,
            Warnings = GovernedDogfoodCampaignBoundaryText.Warnings,
            ReleaseApproved = false,
            DeploymentApproved = false,
            MergeApproved = false,
            ReleaseExecuted = false,
            SourceApplyExecuted = false,
            RollbackExecuted = false,
            WorkflowContinued = false,
            WorkflowMutated = false,
            GitOperationExecuted = false,
            HumanReviewRequiredForReleaseApproval = true,
            HumanReviewRequiredForDeployment = true,
            HumanReviewRequiredForMerge = true,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Boundary = GovernedDogfoodCampaignBoundaryText.Boundary
        };

    private static GovernedDogfoodCampaignIterationResult MapIteration(
        int iterationNumber,
        Guid governedReleaseGateRequestId,
        GovernedReleaseGateResult result)
    {
        var decision = result.DecisionRecord;
        return new GovernedDogfoodCampaignIterationResult
        {
            IterationNumber = iterationNumber,
            GovernedReleaseGateRequestId = governedReleaseGateRequestId,
            Succeeded = result.Succeeded,
            Status = SafeText(result.Status),
            ReleaseReadinessGateRan = result.ReleaseReadinessGateRan,
            DecisionRecordStored = result.DecisionRecordStored,
            ReleaseReadinessEvidenceSatisfied = result.ReleaseReadinessEvidenceSatisfied,
            ReleaseReadinessDecisionRecordId = decision?.ReleaseReadinessDecisionRecordId,
            ReleaseReadinessDecisionRecordHash = SafeTextOrNull(decision?.ReleaseReadinessDecisionRecordHash),
            DecisionStatus = SafeTextOrNull(decision?.DecisionStatus),
            ReleaseApproved = false,
            DeploymentApproved = false,
            MergeApproved = false,
            ReleaseExecuted = false,
            SourceApplyExecuted = false,
            RollbackExecuted = false,
            WorkflowContinued = false,
            WorkflowMutated = false,
            GitOperationExecuted = false,
            HumanReviewRequiredForReleaseApproval = true,
            HumanReviewRequiredForDeployment = true,
            HumanReviewRequiredForMerge = true,
            Issues = SafeIssues(result.Issues)
        };
    }

    private static GovernedDogfoodCampaignIterationResult FailedIteration(int iterationNumber, Guid requestId) =>
        new()
        {
            IterationNumber = iterationNumber,
            GovernedReleaseGateRequestId = requestId,
            Succeeded = false,
            Status = GovernedDogfoodCampaignStatuses.CompletedWithFailedIterations,
            ReleaseReadinessGateRan = false,
            DecisionRecordStored = false,
            ReleaseReadinessEvidenceSatisfied = false,
            ReleaseReadinessDecisionRecordId = null,
            ReleaseReadinessDecisionRecordHash = null,
            DecisionStatus = null,
            ReleaseApproved = false,
            DeploymentApproved = false,
            MergeApproved = false,
            ReleaseExecuted = false,
            SourceApplyExecuted = false,
            RollbackExecuted = false,
            WorkflowContinued = false,
            WorkflowMutated = false,
            GitOperationExecuted = false,
            HumanReviewRequiredForReleaseApproval = true,
            HumanReviewRequiredForDeployment = true,
            HumanReviewRequiredForMerge = true,
            Issues = [new GovernedDogfoodCampaignIssue("GovernedReleaseGateServiceFailed", nameof(IGovernedReleaseGateService), "Governed release gate service failed during campaign iteration.")]
        };

    private static bool IsBlockedDecision(string? decisionStatus) =>
        string.Equals(decisionStatus, ReleaseReadinessDecisionStatuses.BlockedByMissingEvidence, StringComparison.Ordinal) ||
        string.Equals(decisionStatus, ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence, StringComparison.Ordinal) ||
        string.Equals(decisionStatus, ReleaseReadinessDecisionStatuses.BlockedByHumanReviewRequired, StringComparison.Ordinal);

    private static IReadOnlyList<GovernedDogfoodCampaignIssue> SafeIssues(IReadOnlyList<GovernedReleaseGateIssue>? issues)
    {
        if (issues is null || issues.Count == 0)
            return [];

        return issues
            .Select(issue => new GovernedDogfoodCampaignIssue(
                SafeText(issue.Code),
                SafeText(issue.Field),
                GovernedDogfoodCampaignValidation.ContainsUnsafeText(issue.Message)
                    ? "Governed release gate issue contained unsafe material."
                    : SafeText(issue.Message)))
            .ToArray();
    }

    private static string SafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return GovernedDogfoodCampaignValidation.ContainsUnsafeText(value) ? "[redacted]" : value.Trim();
    }

    private static string? SafeTextOrNull(string? value)
    {
        var safe = SafeText(value);
        return string.IsNullOrEmpty(safe) ? null : safe;
    }
}
