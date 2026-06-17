using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class ReleaseGateNegativeCampaignRunner : IReleaseGateNegativeCampaignRunner
{
    private static readonly HashSet<string> SupportedCaseKinds = new(StringComparer.Ordinal)
    {
        ReleaseGateNegativeCaseKinds.InvalidRequest,
        ReleaseGateNegativeCaseKinds.UnsafeMaterial,
        ReleaseGateNegativeCaseKinds.AuthorityClaim,
        ReleaseGateNegativeCaseKinds.MissingEvidence,
        ReleaseGateNegativeCaseKinds.FailedEvidence,
        ReleaseGateNegativeCaseKinds.StaleEvidence,
        ReleaseGateNegativeCaseKinds.ExpiredEvidence,
        ReleaseGateNegativeCaseKinds.SubjectMismatch,
        ReleaseGateNegativeCaseKinds.WorkflowMismatch,
        ReleaseGateNegativeCaseKinds.HashMismatch,
        ReleaseGateNegativeCaseKinds.FollowUpRecoveryIncomplete,
        ReleaseGateNegativeCaseKinds.UnexpectedApprovalClaim,
        ReleaseGateNegativeCaseKinds.UnexpectedExecutionClaim,
        ReleaseGateNegativeCaseKinds.StoreFailure,
        ReleaseGateNegativeCaseKinds.ReadBackFailure
    };

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
        "source applied by campaign",
        "rollback executed by campaign",
        "workflow continued by campaign",
        "release gate passed",
        "ready to release",
        "authority refreshed",
        "evidence reissued",
        "git " + "committed",
        "git " + "pushed",
        "tag created",
        "pull request created",
        "memory promoted",
        "retrieval activated",
        "agent dispatched",
        "tool executed",
        "model called"
    ];

    private static readonly string[] SafeAuthorityPrefixes =
    [
        "not",
        "no",
        "does not",
        "do not",
        "must not",
        "never",
        "without",
        "negative case",
        "expected rejection",
        "expected blocked"
    ];

    private readonly IGovernedReleaseGateService _governedReleaseGateService;

    public ReleaseGateNegativeCampaignRunner(IGovernedReleaseGateService governedReleaseGateService)
    {
        _governedReleaseGateService = governedReleaseGateService ?? throw new ArgumentNullException(nameof(governedReleaseGateService));
    }

    public async Task<ReleaseGateNegativeCampaignResult> RunAsync(
        ReleaseGateNegativeCampaignRequest? request,
        CancellationToken cancellationToken = default)
    {
        var findings = ValidateRequest(request);
        if (request is null || findings.Count > 0)
        {
            return Rejected(request, findings);
        }

        var caseResults = new List<ReleaseGateNegativeCaseResult>();

        foreach (var negativeCase in request.Cases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            GovernedReleaseGateResult actual;
            try
            {
                actual = await _governedReleaseGateService
                    .EvaluateAsync(negativeCase.GovernedReleaseGateRequest, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                actual = FailedServiceResult(negativeCase);
            }

            caseResults.Add(MapCaseResult(negativeCase, actual));
        }

        var unexpectedPassCount = caseResults.Count(IsUnexpectedPass);
        var unexpectedFailureShapeCount = caseResults.Count(result => !result.CaseSucceeded && !IsUnexpectedPass(result));

        var status = unexpectedPassCount > 0
            ? ReleaseGateNegativeCampaignStatuses.CompletedWithUnexpectedPasses
            : unexpectedFailureShapeCount > 0
                ? ReleaseGateNegativeCampaignStatuses.CompletedWithUnexpectedFailureShape
                : ReleaseGateNegativeCampaignStatuses.Completed;

        var succeeded = string.Equals(status, ReleaseGateNegativeCampaignStatuses.Completed, StringComparison.Ordinal);
        return new ReleaseGateNegativeCampaignResult
        {
            ReleaseGateNegativeCampaignRequestId = request.ReleaseGateNegativeCampaignRequestId,
            ProjectId = request.ProjectId,
            CampaignName = SafeOutputText(request.CampaignName),
            Succeeded = succeeded,
            Status = status,
            RequestedCaseCount = request.Cases.Count,
            CompletedCaseCount = caseResults.Count,
            ExpectedNegativeOutcomeCount = caseResults.Count(result => result.CaseSucceeded),
            UnexpectedPassCount = unexpectedPassCount,
            UnexpectedFailureShapeCount = unexpectedFailureShapeCount,
            CaseResults = caseResults,
            Findings = BuildCompletionFindings(caseResults),
            EvidenceReferences = CollectEvidenceReferences(request),
            BoundaryMaxims = SafeList(request.BoundaryMaxims),
            ReleaseApproved = false,
            DeploymentApproved = false,
            MergeApproved = false,
            ReleaseExecuted = false,
            SourceApplyExecuted = false,
            RollbackExecuted = false,
            WorkflowContinued = false,
            WorkflowMutated = false,
            GitOperationExecuted = false,
            AuthorityRefreshed = false,
            EvidenceReissued = false,
            HumanReviewRequired = true,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Boundary = ReleaseGateNegativeCampaignBoundaryText.Boundary
        };
    }

    private static IReadOnlyList<ReleaseGateNegativeCampaignFinding> ValidateRequest(ReleaseGateNegativeCampaignRequest? request)
    {
        var findings = new List<ReleaseGateNegativeCampaignFinding>();
        if (request is null)
        {
            Add(findings, "RequestRequired", nameof(request), "Release gate negative campaign request is required.");
            return findings;
        }

        RequireGuid(findings, request.ReleaseGateNegativeCampaignRequestId, nameof(request.ReleaseGateNegativeCampaignRequestId));
        RequireGuid(findings, request.ProjectId, nameof(request.ProjectId));
        RequireText(findings, request.CampaignName, nameof(request.CampaignName));
        RequireText(findings, request.RequestedBy, nameof(request.RequestedBy));
        RequireText(findings, request.Boundary, nameof(request.Boundary));
        RequireList(findings, request.EvidenceReferences, nameof(request.EvidenceReferences));
        RequireList(findings, request.BoundaryMaxims, nameof(request.BoundaryMaxims));

        if (request.RequestedAtUtc == default)
            Add(findings, "RequestedAtRequired", nameof(request.RequestedAtUtc), "Requested timestamp is required.");

        if (request.MaxCases <= 0)
            Add(findings, "MaxCasesRequired", nameof(request.MaxCases), "Max cases must be greater than zero.");
        else if (request.MaxCases > 50)
            Add(findings, "MaxCasesTooHigh", nameof(request.MaxCases), "Max cases must not exceed 50.");

        if (request.Cases is null || request.Cases.Count == 0)
        {
            Add(findings, "CasesRequired", nameof(request.Cases), "At least one negative release-gate case is required.");
            return findings;
        }

        if (request.Cases.Count > request.MaxCases)
            Add(findings, "CasesExceedMaxCases", nameof(request.Cases), "Case count must not exceed max cases.");

        var seenCaseIds = new HashSet<Guid>();
        var seenReleaseGateRequestIds = new HashSet<Guid>();
        for (var index = 0; index < request.Cases.Count; index++)
        {
            var negativeCase = request.Cases[index];
            var field = $"{nameof(request.Cases)}[{index}]";
            if (negativeCase is null)
            {
                Add(findings, "CaseRequired", field, "Negative release-gate case is required.");
                continue;
            }

            RequireGuid(findings, negativeCase.ReleaseGateNegativeCaseId, $"{field}.{nameof(negativeCase.ReleaseGateNegativeCaseId)}");
            RequireText(findings, negativeCase.CaseName, $"{field}.{nameof(negativeCase.CaseName)}");
            RequireText(findings, negativeCase.CaseKind, $"{field}.{nameof(negativeCase.CaseKind)}");
            RequireText(findings, negativeCase.ExpectedStatus, $"{field}.{nameof(negativeCase.ExpectedStatus)}");
            RequireList(findings, negativeCase.ExpectedIssueCodes, $"{field}.{nameof(negativeCase.ExpectedIssueCodes)}", allowEmpty: true);
            RequireList(findings, negativeCase.EvidenceReferences, $"{field}.{nameof(negativeCase.EvidenceReferences)}");
            RequireList(findings, negativeCase.BoundaryMaxims, $"{field}.{nameof(negativeCase.BoundaryMaxims)}");

            if (!SupportedCaseKinds.Contains(negativeCase.CaseKind))
                Add(findings, "UnsupportedCaseKind", $"{field}.{nameof(negativeCase.CaseKind)}", "Negative case kind is not supported.");

            if (!seenCaseIds.Add(negativeCase.ReleaseGateNegativeCaseId))
                Add(findings, "DuplicateCaseId", $"{field}.{nameof(negativeCase.ReleaseGateNegativeCaseId)}", "Negative case ids must be unique.");

            if (negativeCase.GovernedReleaseGateRequest is null)
            {
                Add(findings, "GovernedReleaseGateRequestRequired", $"{field}.{nameof(negativeCase.GovernedReleaseGateRequest)}", "Governed release gate request is required.");
            }
            else
            {
                if (negativeCase.GovernedReleaseGateRequest.ProjectId != request.ProjectId)
                    Add(findings, "CaseProjectMismatch", $"{field}.{nameof(negativeCase.GovernedReleaseGateRequest.ProjectId)}", "Case project must match campaign project.");

                if (!seenReleaseGateRequestIds.Add(negativeCase.GovernedReleaseGateRequest.GovernedReleaseGateRequestId))
                    Add(findings, "DuplicateGovernedReleaseGateRequestId", $"{field}.{nameof(negativeCase.GovernedReleaseGateRequest.GovernedReleaseGateRequestId)}", "Governed release gate request ids must be unique.");
            }

            if (negativeCase.ExpectedReleaseReadinessEvidenceSatisfied)
                Add(findings, "ExpectedReadyEvidenceSatisfiedRejected", $"{field}.{nameof(negativeCase.ExpectedReleaseReadinessEvidenceSatisfied)}", "Negative campaign cases cannot expect ready evidence satisfied.");
        }

        return findings;
    }

    private static ReleaseGateNegativeCaseResult MapCaseResult(ReleaseGateNegativeCase negativeCase, GovernedReleaseGateResult actual)
    {
        var actualIssueCodes = SafeList(actual.Issues?.Select(issue => issue.Code).ToArray());
        var expectedIssueCodes = SafeList(negativeCase.ExpectedIssueCodes);
        var matchedExpectedIssues = expectedIssueCodes.All(expected =>
            actualIssueCodes.Any(actualCode => string.Equals(actualCode, expected, StringComparison.Ordinal)));
        var matchedExpectedStatus = string.Equals(actual.Status, negativeCase.ExpectedStatus, StringComparison.Ordinal);
        var matchedExpectedSucceeded = actual.Succeeded == negativeCase.ExpectedSucceeded;
        var matchedExpectedDecisionStorage = actual.DecisionRecordStored == negativeCase.ExpectedDecisionRecordStored;
        var matchedExpectedEvidenceSatisfied = actual.ReleaseReadinessEvidenceSatisfied == negativeCase.ExpectedReleaseReadinessEvidenceSatisfied;
        var authorityStayedFalse = !actual.ReleaseApproved &&
            !actual.DeploymentApproved &&
            !actual.MergeApproved &&
            !actual.ReleaseExecuted &&
            !actual.SourceApplyExecuted &&
            !actual.RollbackExecuted &&
            !actual.WorkflowContinued &&
            !actual.WorkflowMutated &&
            !actual.GitOperationExecuted;
        var noUnexpectedReadyEvidence = !actual.ReleaseReadinessEvidenceSatisfied &&
            actual.DecisionRecord?.DecisionStatus != ReleaseReadinessDecisionStatuses.ReadyEvidenceSatisfied;
        var caseSucceeded = matchedExpectedStatus &&
            matchedExpectedSucceeded &&
            matchedExpectedDecisionStorage &&
            matchedExpectedEvidenceSatisfied &&
            matchedExpectedIssues &&
            authorityStayedFalse &&
            noUnexpectedReadyEvidence;

        return new ReleaseGateNegativeCaseResult
        {
            ReleaseGateNegativeCaseId = negativeCase.ReleaseGateNegativeCaseId,
            CaseName = SafeOutputText(negativeCase.CaseName),
            CaseKind = SafeOutputText(negativeCase.CaseKind),
            CaseSucceeded = caseSucceeded,
            ActualStatus = SafeOutputText(actual.Status),
            ActualSucceeded = actual.Succeeded,
            ActualDecisionRecordStored = actual.DecisionRecordStored,
            ActualReleaseReadinessEvidenceSatisfied = actual.ReleaseReadinessEvidenceSatisfied,
            MatchedExpectedStatus = matchedExpectedStatus,
            MatchedExpectedSucceeded = matchedExpectedSucceeded,
            MatchedExpectedDecisionStorage = matchedExpectedDecisionStorage,
            MatchedExpectedEvidenceSatisfied = matchedExpectedEvidenceSatisfied,
            MatchedExpectedIssues = matchedExpectedIssues,
            ActualIssueCodes = actualIssueCodes,
            ExpectedIssueCodes = expectedIssueCodes,
            ReleaseApproved = false,
            DeploymentApproved = false,
            MergeApproved = false,
            ReleaseExecuted = false,
            SourceApplyExecuted = false,
            RollbackExecuted = false,
            WorkflowContinued = false,
            WorkflowMutated = false,
            GitOperationExecuted = false,
            HumanReviewRequired = true
        };
    }

    private static bool IsUnexpectedPass(ReleaseGateNegativeCaseResult result) =>
        result.ActualReleaseReadinessEvidenceSatisfied;

    private static GovernedReleaseGateResult FailedServiceResult(ReleaseGateNegativeCase negativeCase) => new()
    {
        GovernedReleaseGateRequestId = negativeCase.GovernedReleaseGateRequest?.GovernedReleaseGateRequestId ?? Guid.Empty,
        ProjectId = negativeCase.GovernedReleaseGateRequest?.ProjectId ?? Guid.Empty,
        Succeeded = false,
        Status = GovernedReleaseGateStatuses.DecisionRecordSaveFailed,
        ReleaseReadinessGateRan = false,
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
        Issues = [new GovernedReleaseGateIssue("GovernedReleaseGateServiceFailed", nameof(IGovernedReleaseGateService), "Governed release gate service failed during negative campaign case.")],
        Warnings = GovernedReleaseGateBoundaryText.Warnings,
        CompletedAtUtc = DateTimeOffset.UtcNow,
        Boundary = GovernedReleaseGateBoundaryText.Boundary
    };

    private static ReleaseGateNegativeCampaignResult Rejected(
        ReleaseGateNegativeCampaignRequest? request,
        IReadOnlyList<ReleaseGateNegativeCampaignFinding> findings) => new()
        {
            ReleaseGateNegativeCampaignRequestId = request?.ReleaseGateNegativeCampaignRequestId ?? Guid.Empty,
            ProjectId = request?.ProjectId ?? Guid.Empty,
            CampaignName = SafeOutputText(request?.CampaignName),
            Succeeded = false,
            Status = ReleaseGateNegativeCampaignStatuses.Rejected,
            RequestedCaseCount = request?.Cases?.Count ?? 0,
            CompletedCaseCount = 0,
            ExpectedNegativeOutcomeCount = 0,
            UnexpectedPassCount = 0,
            UnexpectedFailureShapeCount = 0,
            CaseResults = [],
            Findings = findings,
            EvidenceReferences = CollectEvidenceReferences(request),
            BoundaryMaxims = SafeList(request?.BoundaryMaxims),
            ReleaseApproved = false,
            DeploymentApproved = false,
            MergeApproved = false,
            ReleaseExecuted = false,
            SourceApplyExecuted = false,
            RollbackExecuted = false,
            WorkflowContinued = false,
            WorkflowMutated = false,
            GitOperationExecuted = false,
            AuthorityRefreshed = false,
            EvidenceReissued = false,
            HumanReviewRequired = true,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Boundary = ReleaseGateNegativeCampaignBoundaryText.Boundary
        };

    private static IReadOnlyList<ReleaseGateNegativeCampaignFinding> BuildCompletionFindings(IReadOnlyList<ReleaseGateNegativeCaseResult> caseResults)
    {
        var findings = new List<ReleaseGateNegativeCampaignFinding>();
        foreach (var result in caseResults.Where(result => !result.CaseSucceeded))
        {
            Add(findings, IsUnexpectedPass(result) ? "UnexpectedPass" : "UnexpectedFailureShape", result.CaseName, "Negative release-gate case did not match the expected negative shape.");
        }

        return findings;
    }

    private static IReadOnlyList<string> CollectEvidenceReferences(ReleaseGateNegativeCampaignRequest? request)
    {
        if (request is null)
        {
            return [];
        }

        var references = new List<string>();
        AddRange(references, request.EvidenceReferences);
        foreach (var negativeCase in request.Cases ?? [])
        {
            AddRange(references, negativeCase?.EvidenceReferences);
        }

        return SafeList(references);
    }

    private static void RequireGuid(
        List<ReleaseGateNegativeCampaignFinding> findings,
        Guid value,
        string field)
    {
        if (value == Guid.Empty)
            Add(findings, $"{field}Required", field, $"{field} is required.");
    }

    private static void RequireText(
        List<ReleaseGateNegativeCampaignFinding> findings,
        string? value,
        string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(findings, $"{field}Required", field, $"{field} is required.");
            return;
        }

        ScanText(findings, value, field);
    }

    private static void RequireList(
        List<ReleaseGateNegativeCampaignFinding> findings,
        IReadOnlyList<string>? values,
        string field,
        bool allowEmpty = false)
    {
        if (values is null || (!allowEmpty && values.Count == 0) || values.Any(string.IsNullOrWhiteSpace))
        {
            Add(findings, $"{field}Required", field, $"{field} is required.");
            return;
        }

        foreach (var value in values)
            ScanText(findings, value, field);
    }

    private static void ScanText(
        List<ReleaseGateNegativeCampaignFinding> findings,
        string? value,
        string field)
    {
        if (ContainsPrivateOrRaw(value))
            Add(findings, "PrivateRawMaterialRejected", field, "Private, raw, prompt, scratchpad, patch, or secret-like material is not allowed.");

        if (ContainsAuthorityClaim(value))
            Add(findings, "AuthorityClaimRejected", field, "Authority claims are not allowed in release gate negative campaign metadata.");
    }

    private static bool ContainsPrivateOrRaw(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        PrivateOrRawMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAuthorityClaim(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = NormalizeForMarkerSearch(value);
        foreach (var marker in AuthorityMarkers.Select(NormalizeForMarkerSearch))
        {
            var index = normalized.IndexOf(marker, StringComparison.Ordinal);
            while (index >= 0)
            {
                var prefix = normalized[..index].TrimEnd();
                if (!SafeAuthorityPrefixes.Any(safePrefix => prefix.EndsWith(safePrefix, StringComparison.Ordinal)))
                {
                    return true;
                }

                index = normalized.IndexOf(marker, index + marker.Length, StringComparison.Ordinal);
            }
        }

        return false;
    }

    private static IReadOnlyList<string> SafeList(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
            return [];

        return values.Select(SafeOutputText).Where(value => value.Length > 0).ToArray();
    }

    private static string SafeOutputText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return ContainsPrivateOrRaw(value) || ContainsAuthorityClaim(value) ? "[redacted]" : value.Trim();
    }

    private static void AddRange(List<string> target, IEnumerable<string>? values)
    {
        if (values is null)
            return;

        target.AddRange(values);
    }

    private static string NormalizeForMarkerSearch(string value) =>
        value.Trim().ToLowerInvariant().Replace("_", " ", StringComparison.Ordinal);

    private static void Add(
        List<ReleaseGateNegativeCampaignFinding> findings,
        string code,
        string field,
        string message) =>
        findings.Add(new ReleaseGateNegativeCampaignFinding
        {
            Code = code,
            Severity = ReleaseGateNegativeCampaignFindingSeverities.Blocking,
            Field = SafeOutputText(field),
            Message = message
        });
}
