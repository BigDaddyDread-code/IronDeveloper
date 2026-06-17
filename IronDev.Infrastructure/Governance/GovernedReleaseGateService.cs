using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class GovernedReleaseGateService : IGovernedReleaseGateService
{
    private readonly ReleaseReadinessGateEvaluator _evaluator;
    private readonly IReleaseReadinessDecisionRecordStore _store;

    public GovernedReleaseGateService(
        ReleaseReadinessGateEvaluator evaluator,
        IReleaseReadinessDecisionRecordStore store)
    {
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<GovernedReleaseGateResult> EvaluateAsync(
        GovernedReleaseGateRequest? request,
        CancellationToken cancellationToken = default)
    {
        var requestIssues = GovernedReleaseGateValidation.ValidateRequest(request);
        if (request is null || requestIssues.Count > 0)
            return Rejected(request, requestIssues, releaseReadinessGateRan: false);

        ReleaseReadinessDecisionRecord decision;
        try
        {
            decision = _evaluator.Evaluate(new ReleaseReadinessGateRequest
            {
                ReleaseReadinessGateRequestId = request.GovernedReleaseGateRequestId,
                ProjectId = request.ProjectId,
                ReleaseReadinessReport = request.ReleaseReadinessReport,
                RequestedAtUtc = request.RequestedAtUtc,
                EvidenceReferences = request.EvidenceReferences,
                BoundaryMaxims = request.BoundaryMaxims,
                Boundary = ReleaseReadinessGateBoundaryText.Boundary
            });
        }
        catch (Exception ex)
        {
            return Rejected(request, [Issue("ReleaseReadinessGateEvaluationFailed", nameof(ReleaseReadinessGateEvaluator), $"Release readiness gate evaluation failed: {ex.Message}")], releaseReadinessGateRan: true);
        }

        var decisionValidation = ReleaseReadinessDecisionRecordValidation.Validate(decision);
        if (!decisionValidation.IsValid)
        {
            return Rejected(
                request,
                decisionValidation.Issues
                    .Select(issue => Issue($"DecisionRecord.{issue.Code}", $"DecisionRecord.{issue.Field}", issue.Message))
                    .ToArray(),
                releaseReadinessGateRan: true);
        }

        try
        {
            await _store.SaveAsync(decision, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return SaveFailed(request, [Issue("DecisionRecordSaveFailed", nameof(IReleaseReadinessDecisionRecordStore), $"Release readiness decision record save failed: {ex.Message}")], releaseReadinessGateRan: true);
        }

        var stored = await _store.GetAsync(request.ProjectId, decision.ReleaseReadinessDecisionRecordId, cancellationToken).ConfigureAwait(false)
            ?? await _store.GetByRecordHashAsync(request.ProjectId, decision.ReleaseReadinessDecisionRecordHash, cancellationToken).ConfigureAwait(false);

        if (stored is null)
        {
            return ReadBackFailed(request, [Issue("DecisionRecordReadBackFailed", nameof(IReleaseReadinessDecisionRecordStore), "Stored release readiness decision record could not be read back.")], releaseReadinessGateRan: true);
        }

        return Success(request, stored);
    }

    private static GovernedReleaseGateResult Success(GovernedReleaseGateRequest request, ReleaseReadinessDecisionRecord record) => new()
    {
        GovernedReleaseGateRequestId = request.GovernedReleaseGateRequestId,
        ProjectId = request.ProjectId,
        Succeeded = true,
        Status = GovernedReleaseGateStatuses.DecisionRecordStored,
        ReleaseReadinessGateRan = true,
        DecisionRecordStored = true,
        DecisionRecord = record,
        ReleaseReadinessEvidenceSatisfied = record.ReleaseReadinessEvidenceSatisfied,
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
        Issues = [],
        Warnings = GovernedReleaseGateBoundaryText.Warnings,
        CompletedAtUtc = DateTimeOffset.UtcNow,
        Boundary = GovernedReleaseGateBoundaryText.Boundary
    };

    private static GovernedReleaseGateResult Rejected(
        GovernedReleaseGateRequest? request,
        IReadOnlyList<GovernedReleaseGateIssue> issues,
        bool releaseReadinessGateRan) =>
        Failure(request, GovernedReleaseGateStatuses.Rejected, issues, releaseReadinessGateRan);

    private static GovernedReleaseGateResult SaveFailed(
        GovernedReleaseGateRequest request,
        IReadOnlyList<GovernedReleaseGateIssue> issues,
        bool releaseReadinessGateRan) =>
        Failure(request, GovernedReleaseGateStatuses.DecisionRecordSaveFailed, issues, releaseReadinessGateRan);

    private static GovernedReleaseGateResult ReadBackFailed(
        GovernedReleaseGateRequest request,
        IReadOnlyList<GovernedReleaseGateIssue> issues,
        bool releaseReadinessGateRan) =>
        Failure(request, GovernedReleaseGateStatuses.DecisionRecordReadBackFailed, issues, releaseReadinessGateRan);

    private static GovernedReleaseGateResult Failure(
        GovernedReleaseGateRequest? request,
        string status,
        IReadOnlyList<GovernedReleaseGateIssue> issues,
        bool releaseReadinessGateRan) =>
        new()
        {
            GovernedReleaseGateRequestId = request?.GovernedReleaseGateRequestId ?? Guid.Empty,
            ProjectId = request?.ProjectId ?? Guid.Empty,
            Succeeded = false,
            Status = status,
            ReleaseReadinessGateRan = releaseReadinessGateRan,
            DecisionRecordStored = false,
            DecisionRecord = null,
            ReleaseReadinessEvidenceSatisfied = false,
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
            Issues = issues,
            Warnings = GovernedReleaseGateBoundaryText.Warnings,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Boundary = GovernedReleaseGateBoundaryText.Boundary
        };

    private static GovernedReleaseGateIssue Issue(string code, string field, string message) => new(code, field, message);
}
