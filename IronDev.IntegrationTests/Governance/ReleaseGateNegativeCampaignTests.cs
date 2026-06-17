using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("ReleaseGateNegativeCampaign")]
[TestCategory("PR228")]
public sealed class ReleaseGateNegativeCampaignTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 18, 15, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_NullRequestIsRejected()
    {
        var service = new FakeGovernedReleaseGateService();
        var result = await RunAsync(null, service);

        AssertRejected(result, "RequestRequired");
        Assert.AreEqual(0, service.Requests.Count);
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_RejectsNoCases()
    {
        var result = await RunAsync(ValidCampaign([]));

        AssertRejected(result, "CasesRequired");
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_RejectsTooManyCases()
    {
        var cases = Enumerable.Range(0, 51).Select(index => ValidCase($"too-many-{index}")).ToArray();
        var result = await RunAsync(ValidCampaign(cases) with { MaxCases = 51 });

        AssertRejected(result, "MaxCasesTooHigh");
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_RejectsDuplicateCaseIds()
    {
        var first = ValidCase("duplicate-case-1");
        var second = ValidCase("duplicate-case-2") with { ReleaseGateNegativeCaseId = first.ReleaseGateNegativeCaseId };

        var result = await RunAsync(ValidCampaign([first, second]));

        AssertRejected(result, "DuplicateCaseId");
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_RejectsDuplicateGovernedReleaseGateRequestIds()
    {
        var first = ValidCase("duplicate-request-1");
        var second = ValidCase("duplicate-request-2") with
        {
            GovernedReleaseGateRequest = ValidReleaseGateRequest("duplicate-request-2") with
            {
                GovernedReleaseGateRequestId = first.GovernedReleaseGateRequest.GovernedReleaseGateRequestId
            }
        };

        var result = await RunAsync(ValidCampaign([first, second]));

        AssertRejected(result, "DuplicateGovernedReleaseGateRequestId");
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_RejectsProjectMismatch()
    {
        var negativeCase = ValidCase("project-mismatch") with
        {
            GovernedReleaseGateRequest = ValidReleaseGateRequest("project-mismatch") with { ProjectId = DeterministicGuid("other-project") }
        };

        var result = await RunAsync(ValidCampaign([negativeCase]));

        AssertRejected(result, "CaseProjectMismatch");
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_RejectsUnsupportedCaseKind()
    {
        var result = await RunAsync(ValidCampaign([ValidCase("unsupported") with { CaseKind = "MadeUpNegativeCase" }]));

        AssertRejected(result, "UnsupportedCaseKind");
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_RejectsExpectedReadyEvidenceSatisfied()
    {
        var result = await RunAsync(ValidCampaign([ValidCase("bad-expectation") with { ExpectedReleaseReadinessEvidenceSatisfied = true }]));

        AssertRejected(result, "ExpectedReadyEvidenceSatisfiedRejected");
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_InvalidRequestCaseProducesExpectedRejection()
    {
        await AssertExpectedNegativeCaseAsync(
            ValidCase("invalid-request", ReleaseGateNegativeCaseKinds.InvalidRequest, expectedIssues: ["RequestRequired"]),
            RejectedGateResult(ValidReleaseGateRequest("invalid-request"), "RequestRequired"));
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_UnsafeMaterialCaseProducesExpectedRejection()
    {
        var negativeCase = ValidCase("unsafe-material", ReleaseGateNegativeCaseKinds.UnsafeMaterial, expectedIssues: ["UnsafeRequestMaterialRejected"]) with
        {
            GovernedReleaseGateRequest = ValidReleaseGateRequest("unsafe-material") with { RequestedBy = "raw prompt hidden" }
        };

        await AssertExpectedNegativeCaseAsync(
            negativeCase,
            RejectedGateResult(negativeCase.GovernedReleaseGateRequest, "UnsafeRequestMaterialRejected"));
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_AuthorityClaimCaseProducesExpectedRejection()
    {
        var negativeCase = ValidCase("authority-claim", ReleaseGateNegativeCaseKinds.AuthorityClaim, expectedIssues: ["AuthorityRequestMaterialRejected"]) with
        {
            GovernedReleaseGateRequest = ValidReleaseGateRequest("authority-claim") with { RequestedBy = "green to ship" }
        };

        await AssertExpectedNegativeCaseAsync(
            negativeCase,
            RejectedGateResult(negativeCase.GovernedReleaseGateRequest, "AuthorityRequestMaterialRejected"));
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_MissingEvidenceCaseProducesExpectedBlockedDecision()
    {
        var negativeCase = ValidCase(
            "missing-evidence",
            ReleaseGateNegativeCaseKinds.MissingEvidence,
            expectedStatus: GovernedReleaseGateStatuses.DecisionRecordStored,
            expectedSucceeded: true,
            expectedStored: true,
            expectedIssues: []);

        await AssertExpectedNegativeCaseAsync(
            negativeCase,
            BlockedGateResult(negativeCase.GovernedReleaseGateRequest, ReleaseReadinessDecisionStatuses.BlockedByMissingEvidence));
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_FailedEvidenceCaseProducesExpectedBlockedDecision()
    {
        var negativeCase = ValidCase(
            "failed-evidence",
            ReleaseGateNegativeCaseKinds.FailedEvidence,
            expectedStatus: GovernedReleaseGateStatuses.DecisionRecordStored,
            expectedSucceeded: true,
            expectedStored: true,
            expectedIssues: []);

        await AssertExpectedNegativeCaseAsync(
            negativeCase,
            BlockedGateResult(negativeCase.GovernedReleaseGateRequest, ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence));
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_HashMismatchCaseProducesExpectedRejection()
    {
        await AssertExpectedNegativeCaseAsync(
            ValidCase("hash-mismatch", ReleaseGateNegativeCaseKinds.HashMismatch, expectedIssues: ["ReleaseReadinessReportHashMismatch"]),
            RejectedGateResult(ValidReleaseGateRequest("hash-mismatch"), "ReleaseReadinessReportHashMismatch"));
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_StaleEvidenceCaseProducesExpectedBlockedOrRejectedResult()
    {
        await AssertExpectedNegativeCaseAsync(
            ValidCase("stale-evidence", ReleaseGateNegativeCaseKinds.StaleEvidence, expectedIssues: ["StaleAuthorityBlocksReleaseGate"]),
            RejectedGateResult(ValidReleaseGateRequest("stale-evidence"), "StaleAuthorityBlocksReleaseGate"));
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_ExpiredEvidenceCaseProducesExpectedBlockedOrRejectedResult()
    {
        await AssertExpectedNegativeCaseAsync(
            ValidCase("expired-evidence", ReleaseGateNegativeCaseKinds.ExpiredEvidence, expectedIssues: ["EvidenceExpired"]),
            RejectedGateResult(ValidReleaseGateRequest("expired-evidence"), "EvidenceExpired"));
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_UnexpectedReadyEvidenceFailsCampaign()
    {
        var negativeCase = ValidCase("unexpected-ready");
        var service = new FakeGovernedReleaseGateService();
        service.Results.Enqueue(ReadyGateResult(negativeCase.GovernedReleaseGateRequest));

        var result = await RunAsync(ValidCampaign([negativeCase]), service);

        Assert.AreEqual(ReleaseGateNegativeCampaignStatuses.CompletedWithUnexpectedPasses, result.Status);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(1, result.UnexpectedPassCount);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_UnexpectedDecisionRecordStorageFailsWhenExpectedNoStore()
    {
        var negativeCase = ValidCase("unexpected-store", expectedIssues: ["RequestRequired"]);
        var service = new FakeGovernedReleaseGateService();
        service.Results.Enqueue(BlockedGateResult(negativeCase.GovernedReleaseGateRequest, ReleaseReadinessDecisionStatuses.BlockedByMissingEvidence));

        var result = await RunAsync(ValidCampaign([negativeCase]), service);

        Assert.AreEqual(ReleaseGateNegativeCampaignStatuses.CompletedWithUnexpectedFailureShape, result.Status);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(1, result.UnexpectedFailureShapeCount);
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_MissingExpectedIssueCodeFailsCampaign()
    {
        var negativeCase = ValidCase("missing-issue", expectedIssues: ["ExpectedIssueMissing"]);
        var service = new FakeGovernedReleaseGateService();
        service.Results.Enqueue(RejectedGateResult(negativeCase.GovernedReleaseGateRequest, "DifferentIssue"));

        var result = await RunAsync(ValidCampaign([negativeCase]), service);

        Assert.AreEqual(ReleaseGateNegativeCampaignStatuses.CompletedWithUnexpectedFailureShape, result.Status);
        Assert.IsFalse(result.CaseResults[0].MatchedExpectedIssues);
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_UnexpectedApprovalFlagFailsCampaign()
    {
        var negativeCase = ValidCase("approval-flag", ReleaseGateNegativeCaseKinds.UnexpectedApprovalClaim, expectedIssues: ["Rejected"]);
        var service = new FakeGovernedReleaseGateService();
        service.Results.Enqueue(RejectedGateResult(negativeCase.GovernedReleaseGateRequest, "Rejected") with { ReleaseApproved = true });

        var result = await RunAsync(ValidCampaign([negativeCase]), service);

        Assert.AreEqual(ReleaseGateNegativeCampaignStatuses.CompletedWithUnexpectedFailureShape, result.Status);
        Assert.IsFalse(result.Succeeded);
        AssertNoAuthority(result);
        AssertNoAuthority(result.CaseResults[0]);
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_UnexpectedExecutionFlagFailsCampaign()
    {
        var negativeCase = ValidCase("execution-flag", ReleaseGateNegativeCaseKinds.UnexpectedExecutionClaim, expectedIssues: ["Rejected"]);
        var service = new FakeGovernedReleaseGateService();
        service.Results.Enqueue(RejectedGateResult(negativeCase.GovernedReleaseGateRequest, "Rejected") with { ReleaseExecuted = true });

        var result = await RunAsync(ValidCampaign([negativeCase]), service);

        Assert.AreEqual(ReleaseGateNegativeCampaignStatuses.CompletedWithUnexpectedFailureShape, result.Status);
        Assert.IsFalse(result.Succeeded);
        AssertNoAuthority(result);
        AssertNoAuthority(result.CaseResults[0]);
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_AllExpectedNegativeCasesCompleteSuccessfully()
    {
        var first = ValidCase("all-negative-1", ReleaseGateNegativeCaseKinds.InvalidRequest, expectedIssues: ["RequestRequired"]);
        var second = ValidCase(
            "all-negative-2",
            ReleaseGateNegativeCaseKinds.MissingEvidence,
            expectedStatus: GovernedReleaseGateStatuses.DecisionRecordStored,
            expectedSucceeded: true,
            expectedStored: true,
            expectedIssues: []);
        var service = new FakeGovernedReleaseGateService();
        service.Results.Enqueue(RejectedGateResult(first.GovernedReleaseGateRequest, "RequestRequired"));
        service.Results.Enqueue(BlockedGateResult(second.GovernedReleaseGateRequest, ReleaseReadinessDecisionStatuses.BlockedByMissingEvidence));

        var result = await RunAsync(ValidCampaign([first, second]), service);

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.Findings.Select(finding => finding.Code)));
        Assert.AreEqual(ReleaseGateNegativeCampaignStatuses.Completed, result.Status);
        Assert.AreEqual(2, result.CompletedCaseCount);
        Assert.AreEqual(2, result.ExpectedNegativeOutcomeCount);
        Assert.AreEqual(0, result.UnexpectedPassCount);
        Assert.AreEqual(0, result.UnexpectedFailureShapeCount);
        Assert.IsTrue(result.CaseResults.All(caseResult => caseResult.CaseSucceeded));
        AssertNoAuthority(result);
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_CompletedDoesNotMeanReleaseApproved()
    {
        var result = await SuccessfulCampaignAsync("not-approved");

        Assert.IsTrue(result.Succeeded);
        Assert.IsFalse(result.ReleaseApproved);
        Assert.IsFalse(result.DeploymentApproved);
        Assert.IsFalse(result.MergeApproved);
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_CompletedDoesNotMeanReleaseReady()
    {
        var result = await SuccessfulCampaignAsync("not-ready");

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.CaseResults.All(caseResult => !caseResult.ActualReleaseReadinessEvidenceSatisfied));
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_CompletedDoesNotRefreshAuthority()
    {
        var result = await SuccessfulCampaignAsync("not-refresh");

        Assert.IsTrue(result.Succeeded);
        Assert.IsFalse(result.AuthorityRefreshed);
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_CompletedDoesNotReissueEvidence()
    {
        var result = await SuccessfulCampaignAsync("not-reissue");

        Assert.IsTrue(result.Succeeded);
        Assert.IsFalse(result.EvidenceReissued);
    }

    [TestMethod]
    public async Task ReleaseGateNegativeCampaign_HumanReviewRequired()
    {
        var result = await SuccessfulCampaignAsync("human-review");

        Assert.IsTrue(result.HumanReviewRequired);
        Assert.IsTrue(result.CaseResults.All(caseResult => caseResult.HumanReviewRequired));
    }

    [TestMethod]
    public void ReleaseGateNegativeCampaign_RunnerDependsOnlyOnGovernedReleaseGateService()
    {
        var constructor = typeof(ReleaseGateNegativeCampaignRunner).GetConstructors().Single();

        CollectionAssert.AreEqual(
            new[] { typeof(IGovernedReleaseGateService) },
            constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
    }

    [TestMethod]
    public void ReleaseGateNegativeCampaign_RunnerDoesNotUseEvaluatorStoreSqlApiCliGitRuntimeAgentsModelsToolsMemoryOrRetrieval()
    {
        var runner = RunnerSource();

        StringAssert.Contains(runner, "IGovernedReleaseGateService");

        foreach (var marker in new[]
        {
            "ReleaseReadinessGateEvaluator",
            "IReleaseReadinessDecisionRecordStore",
            "SqlConnection",
            "IDbConnection",
            "Dapper",
            "HttpClient",
            "HttpPost",
            "ControllerBase",
            "IronDevApiClient",
            "Cli",
            "ControlledSourceApplyExecutor",
            "ControlledRollbackExecutor",
            "GovernedWorkflowContinuationService",
            "WorkflowTransitionRecordStore",
            "Process.Start",
            "ProcessStartInfo",
            "git commit",
            "git push",
            "git merge",
            "gh pr",
            "IHostedService",
            "BackgroundService",
            "Scheduler",
            "AgentDispatch",
            "ModelProvider",
            "ToolInvoker",
            "PromoteMemory",
            "ActivateRetrieval",
            "Weaviate",
            "Embedding",
            "ReleaseApproved = true",
            "DeploymentApproved = true",
            "MergeApproved = true",
            "ReleaseExecuted = true",
            "SourceApplyExecuted = true",
            "RollbackExecuted = true",
            "WorkflowContinued = true",
            "WorkflowMutated = true",
            "GitOperationExecuted = true",
            "AuthorityRefreshed = true",
            "EvidenceReissued = true"
        })
        {
            Assert.IsFalse(runner.Contains(marker, StringComparison.Ordinal), $"Forbidden marker in release gate negative campaign runner: {marker}");
        }
    }

    [TestMethod]
    public void ReleaseGateNegativeCampaign_RunnerDoesNotUseStaleAuthorityDetectorOrRecoveryRunners()
    {
        var runner = RunnerSource();

        foreach (var marker in new[]
        {
            "StaleAuthorityDetector",
            "GovernedDogfoodCampaignRunner",
            "FailedApplyRecoveryCampaignRunner",
            "FailedContinuationRecoveryCampaignRunner"
        })
        {
            Assert.IsFalse(runner.Contains(marker, StringComparison.Ordinal), $"Forbidden marker in release gate negative campaign runner: {marker}");
        }
    }

    private static async Task AssertExpectedNegativeCaseAsync(ReleaseGateNegativeCase negativeCase, GovernedReleaseGateResult gateResult)
    {
        var service = new FakeGovernedReleaseGateService();
        service.Results.Enqueue(gateResult);

        var result = await RunAsync(ValidCampaign([negativeCase]), service);

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.Findings.Select(finding => finding.Code)));
        Assert.AreEqual(ReleaseGateNegativeCampaignStatuses.Completed, result.Status);
        Assert.AreEqual(1, result.CompletedCaseCount);
        Assert.AreEqual(1, service.Requests.Count);
        Assert.IsTrue(result.CaseResults[0].CaseSucceeded);
        AssertNoAuthority(result);
        AssertNoAuthority(result.CaseResults[0]);
    }

    private static async Task<ReleaseGateNegativeCampaignResult> SuccessfulCampaignAsync(string suffix)
    {
        var negativeCase = ValidCase(suffix, expectedIssues: ["RequestRequired"]);
        var service = new FakeGovernedReleaseGateService();
        service.Results.Enqueue(RejectedGateResult(negativeCase.GovernedReleaseGateRequest, "RequestRequired"));

        return await RunAsync(ValidCampaign([negativeCase]), service);
    }

    private static Task<ReleaseGateNegativeCampaignResult> RunAsync(ReleaseGateNegativeCampaignRequest? request) =>
        RunAsync(request, new FakeGovernedReleaseGateService());

    private static Task<ReleaseGateNegativeCampaignResult> RunAsync(
        ReleaseGateNegativeCampaignRequest? request,
        FakeGovernedReleaseGateService service) =>
        new ReleaseGateNegativeCampaignRunner(service).RunAsync(request);

    private static ReleaseGateNegativeCampaignRequest ValidCampaign(IReadOnlyList<ReleaseGateNegativeCase> cases) => new()
    {
        ReleaseGateNegativeCampaignRequestId = DeterministicGuid("release-gate-negative-campaign"),
        ProjectId = ProjectId,
        CampaignName = "PR228 negative release gate campaign",
        RequestedBy = "human-reviewer",
        RequestedAtUtc = Now,
        MaxCases = Math.Max(cases.Count, 1),
        Cases = cases,
        EvidenceReferences = ["release-gate-negative-campaign:request", "human-review:required"],
        BoundaryMaxims = ["Negative campaign success is not release approval.", "Human review remains required."]
    };

    private static ReleaseGateNegativeCase ValidCase(
        string suffix,
        string kind = ReleaseGateNegativeCaseKinds.InvalidRequest,
        string expectedStatus = GovernedReleaseGateStatuses.Rejected,
        bool expectedSucceeded = false,
        bool expectedStored = false,
        IReadOnlyList<string>? expectedIssues = null) => new()
        {
            ReleaseGateNegativeCaseId = DeterministicGuid($"negative-case-{suffix}"),
            CaseName = $"negative-case-{suffix}",
            CaseKind = kind,
            GovernedReleaseGateRequest = ValidReleaseGateRequest(suffix),
            ExpectedStatus = expectedStatus,
            ExpectedSucceeded = expectedSucceeded,
            ExpectedDecisionRecordStored = expectedStored,
            ExpectedReleaseReadinessEvidenceSatisfied = false,
            ExpectedIssueCodes = expectedIssues ?? ["RequestRequired"],
            EvidenceReferences = [$"negative-case:{suffix}"],
            BoundaryMaxims = ["Expected rejection is not release approval."]
        };

    private static GovernedReleaseGateRequest ValidReleaseGateRequest(string suffix) => new()
    {
        GovernedReleaseGateRequestId = DeterministicGuid($"governed-release-gate-{suffix}"),
        ProjectId = ProjectId,
        ReleaseReadinessReport = CompleteReport(suffix),
        RequestedBy = $"human-reviewer-{suffix}",
        RequestedAtUtc = Now,
        EvidenceReferences = [$"release-readiness-report:{suffix}", $"human-review:{suffix}"],
        BoundaryMaxims = ["Governed release gate is not release approval.", "Human review remains required."]
    };

    private static GovernedReleaseGateResult RejectedGateResult(GovernedReleaseGateRequest request, string issueCode) => new()
    {
        GovernedReleaseGateRequestId = request.GovernedReleaseGateRequestId,
        ProjectId = request.ProjectId,
        Succeeded = false,
        Status = GovernedReleaseGateStatuses.Rejected,
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
        Issues = [new GovernedReleaseGateIssue(issueCode, "negative-case", "Negative release gate case rejected as expected.")],
        Warnings = GovernedReleaseGateBoundaryText.Warnings,
        CompletedAtUtc = Now,
        Boundary = GovernedReleaseGateBoundaryText.Boundary
    };

    private static GovernedReleaseGateResult BlockedGateResult(GovernedReleaseGateRequest request, string decisionStatus)
    {
        var decision = DecisionRecord(request, decisionStatus, evidenceSatisfied: false);
        return new GovernedReleaseGateResult
        {
            GovernedReleaseGateRequestId = request.GovernedReleaseGateRequestId,
            ProjectId = request.ProjectId,
            Succeeded = true,
            Status = GovernedReleaseGateStatuses.DecisionRecordStored,
            ReleaseReadinessGateRan = true,
            DecisionRecordStored = true,
            DecisionRecord = decision,
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
            Issues = [],
            Warnings = GovernedReleaseGateBoundaryText.Warnings,
            CompletedAtUtc = Now,
            Boundary = GovernedReleaseGateBoundaryText.Boundary
        };
    }

    private static GovernedReleaseGateResult ReadyGateResult(GovernedReleaseGateRequest request)
    {
        var decision = DecisionRecord(request, ReleaseReadinessDecisionStatuses.ReadyEvidenceSatisfied, evidenceSatisfied: true);
        return BlockedGateResult(request, ReleaseReadinessDecisionStatuses.ReadyEvidenceSatisfied) with
        {
            Succeeded = true,
            DecisionRecord = decision,
            ReleaseReadinessEvidenceSatisfied = true
        };
    }

    private static ReleaseReadinessDecisionRecord DecisionRecord(
        GovernedReleaseGateRequest request,
        string decisionStatus,
        bool evidenceSatisfied)
    {
        var record = new ReleaseReadinessDecisionRecord
        {
            ReleaseReadinessDecisionRecordId = request.GovernedReleaseGateRequestId,
            ProjectId = request.ProjectId,
            ReleaseReadinessReportId = request.ReleaseReadinessReport.ReleaseReadinessReportId,
            ReleaseReadinessReportHash = request.ReleaseReadinessReport.ReleaseReadinessReportHash,
            WorkflowRunId = request.ReleaseReadinessReport.WorkflowRunId,
            WorkflowStepId = request.ReleaseReadinessReport.WorkflowStepId,
            SubjectKind = request.ReleaseReadinessReport.SubjectKind,
            SubjectId = request.ReleaseReadinessReport.SubjectId,
            SubjectHash = request.ReleaseReadinessReport.SubjectHash,
            DecisionStatus = decisionStatus,
            ReleaseReadinessEvidenceSatisfied = evidenceSatisfied,
            ReleaseApproved = false,
            DeploymentApproved = false,
            MergeApproved = false,
            SourceApplyExecutedByDecision = false,
            RollbackExecutedByDecision = false,
            WorkflowMutatedByDecision = false,
            GitOperationExecutedByDecision = false,
            ReleaseExecutedByDecision = false,
            HumanReviewRequiredForReleaseApproval = true,
            HumanReviewRequiredForDeployment = true,
            HumanReviewRequiredForMerge = true,
            Reasons =
            [
                new ReleaseReadinessDecisionReason
                {
                    Code = decisionStatus,
                    Severity = evidenceSatisfied ? ReleaseReadinessDecisionReasonSeverities.Info : ReleaseReadinessDecisionReasonSeverities.Blocking,
                    Field = nameof(ReleaseReadinessDecisionRecord.DecisionStatus),
                    Message = "Decision record is evidence only."
                }
            ],
            EvidenceReferences = request.EvidenceReferences,
            BoundaryMaxims = request.BoundaryMaxims,
            DecidedAtUtc = Now,
            ReleaseReadinessDecisionRecordHash = H("pending")
        };

        return record with { ReleaseReadinessDecisionRecordHash = ReleaseReadinessDecisionRecordHashing.ComputeHash(record) };
    }

    private static ReleaseReadinessReport CompleteReport(string suffix)
    {
        var report = new ReleaseReadinessReport
        {
            ReleaseReadinessReportId = DeterministicGuid($"release-readiness-report-{suffix}"),
            ProjectId = ProjectId,
            ReleaseReadinessReportRequestId = DeterministicGuid($"release-readiness-report-request-{suffix}"),
            Status = ReleaseReadinessReportStatuses.Complete,
            WorkflowRunId = $"workflow-run-{suffix}",
            WorkflowStepId = $"workflow-step-{suffix}",
            SubjectKind = "ReleasePackage",
            SubjectId = $"release-package-{suffix}",
            SubjectHash = H($"release-package-{suffix}"),
            AcceptedApprovalId = DeterministicGuid($"accepted-approval-{suffix}"),
            AcceptedApprovalHash = H($"accepted-approval-{suffix}"),
            PolicySatisfactionId = DeterministicGuid($"policy-satisfaction-{suffix}"),
            PolicySatisfactionHash = H($"policy-satisfaction-{suffix}"),
            SourceApplyRequestId = DeterministicGuid($"source-apply-request-{suffix}"),
            SourceApplyRequestHash = H($"source-apply-request-{suffix}"),
            SourceApplyReceiptId = DeterministicGuid($"source-apply-receipt-{suffix}"),
            SourceApplyReceiptHash = H($"source-apply-receipt-{suffix}"),
            RollbackExecutionReceiptId = null,
            RollbackExecutionReceiptHash = null,
            RollbackExecutionAuditReportId = null,
            RollbackExecutionAuditReportHash = null,
            WorkflowContinuationGateEvaluationId = DeterministicGuid($"workflow-continuation-gate-{suffix}"),
            WorkflowContinuationGateEvaluationHash = H($"workflow-continuation-gate-{suffix}"),
            WorkflowTransitionRecordId = DeterministicGuid($"workflow-transition-record-{suffix}"),
            WorkflowTransitionRecordHash = H($"workflow-transition-record-{suffix}"),
            ApprovalEvidencePresent = true,
            PolicyEvidencePresent = true,
            SourceApplySucceeded = true,
            SourceApplyPartial = false,
            RollbackWasExecuted = false,
            RollbackSucceeded = false,
            RollbackPartial = false,
            RollbackAuditConsistent = false,
            WorkflowContinuationSucceeded = true,
            WorkflowTransitionRecordValid = true,
            ReleaseReadinessDecided = false,
            ReleaseReady = false,
            ReleaseApproved = false,
            DeploymentApproved = false,
            MergeApproved = false,
            SourceApplyExecutedByReport = false,
            RollbackExecutedByReport = false,
            WorkflowMutatedByReport = false,
            GitOperationExecutedByReport = false,
            HumanReviewRequiredForReadiness = true,
            HumanReviewRequiredForReleaseApproval = true,
            Findings =
            [
                new ReleaseReadinessReportFinding
                {
                    Code = "ReportEvidenceComplete",
                    Severity = ReleaseReadinessFindingSeverities.Info,
                    Field = "ReleaseReadinessReport",
                    Message = "Evidence complete for test fixture only."
                }
            ],
            EvidenceReferences = [$"release-readiness-report:{suffix}", $"workflow-transition-record:{suffix}"],
            BoundaryMaxims = ["Release readiness report is evidence summary only.", "Release readiness report is not release approval."],
            ReportedAtUtc = Now.AddMinutes(-5),
            ReleaseReadinessReportHash = "sha256:pending"
        };

        return report with { ReleaseReadinessReportHash = ReleaseReadinessReportHashing.ComputeReportHash(report) };
    }

    private static void AssertRejected(ReleaseGateNegativeCampaignResult result, string issueCode)
    {
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ReleaseGateNegativeCampaignStatuses.Rejected, result.Status, string.Join("; ", result.Findings.Select(finding => finding.Code)));
        Assert.IsTrue(result.Findings.Any(finding => finding.Code == issueCode), string.Join("; ", result.Findings.Select(finding => finding.Code)));
        AssertNoAuthority(result);
    }

    private static void AssertNoAuthority(ReleaseGateNegativeCampaignResult result)
    {
        Assert.IsFalse(result.ReleaseApproved);
        Assert.IsFalse(result.DeploymentApproved);
        Assert.IsFalse(result.MergeApproved);
        Assert.IsFalse(result.ReleaseExecuted);
        Assert.IsFalse(result.SourceApplyExecuted);
        Assert.IsFalse(result.RollbackExecuted);
        Assert.IsFalse(result.WorkflowContinued);
        Assert.IsFalse(result.WorkflowMutated);
        Assert.IsFalse(result.GitOperationExecuted);
        Assert.IsFalse(result.AuthorityRefreshed);
        Assert.IsFalse(result.EvidenceReissued);
        Assert.IsTrue(result.HumanReviewRequired);
    }

    private static void AssertNoAuthority(ReleaseGateNegativeCaseResult result)
    {
        Assert.IsFalse(result.ReleaseApproved);
        Assert.IsFalse(result.DeploymentApproved);
        Assert.IsFalse(result.MergeApproved);
        Assert.IsFalse(result.ReleaseExecuted);
        Assert.IsFalse(result.SourceApplyExecuted);
        Assert.IsFalse(result.RollbackExecuted);
        Assert.IsFalse(result.WorkflowContinued);
        Assert.IsFalse(result.WorkflowMutated);
        Assert.IsFalse(result.GitOperationExecuted);
        Assert.IsTrue(result.HumanReviewRequired);
    }

    private static string RunnerSource() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Infrastructure", "Governance", "ReleaseGateNegativeCampaignRunner.cs"));

    private static readonly Guid ProjectId = Guid.Parse("04c8f741-35cc-4450-949e-2d69468e7459");

    private static Guid DeterministicGuid(string value) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes(value)));

    private static string H(string value) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()}";

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class FakeGovernedReleaseGateService : IGovernedReleaseGateService
    {
        public List<GovernedReleaseGateRequest> Requests { get; } = [];
        public Queue<GovernedReleaseGateResult> Results { get; } = new();

        public Task<GovernedReleaseGateResult> EvaluateAsync(
            GovernedReleaseGateRequest? request,
            CancellationToken cancellationToken = default)
        {
            if (request is not null)
                Requests.Add(request);

            return Task.FromResult(Results.Dequeue());
        }
    }
}
