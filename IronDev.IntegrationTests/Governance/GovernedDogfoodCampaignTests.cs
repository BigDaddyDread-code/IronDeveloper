using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("GovernedDogfoodCampaign")]
[TestCategory("PR223")]
public sealed class GovernedDogfoodCampaignTests
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 6, 17, 22, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task GovernedDogfoodCampaign_ValidReadyRequestsCompleteAsEvidenceOnly()
    {
        var request = ValidCampaign("ready", 2);
        var service = new FakeGovernedReleaseGateService();
        service.Results.Enqueue(ReadyGateResult(request.ReleaseGateRequests[0]));
        service.Results.Enqueue(ReadyGateResult(request.ReleaseGateRequests[1]));
        var runner = new GovernedDogfoodCampaignRunner(service);

        var result = await runner.RunAsync(request);

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues.Select(issue => issue.Code)));
        Assert.AreEqual(GovernedDogfoodCampaignStatuses.Completed, result.Status);
        Assert.AreEqual(2, result.RequestedIterations);
        Assert.AreEqual(2, result.CompletedIterations);
        Assert.AreEqual(2, result.ReadyEvidenceSatisfiedCount);
        Assert.AreEqual(0, result.BlockedDecisionCount);
        Assert.AreEqual(0, result.FailedIterationCount);
        Assert.AreEqual(2, service.Requests.Count);
        AssertNoAuthority(result);
        Assert.IsTrue(result.Iterations.All(iteration => iteration.DecisionRecordStored));
        Assert.IsTrue(result.Iterations.All(iteration => iteration.ReleaseReadinessEvidenceSatisfied));
        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("not release approval", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task GovernedDogfoodCampaign_BlockedDecisionsCompleteWithBlockedDecisionStatus()
    {
        var request = ValidCampaign("blocked", 2);
        var service = new FakeGovernedReleaseGateService();
        service.Results.Enqueue(ReadyGateResult(request.ReleaseGateRequests[0]));
        service.Results.Enqueue(BlockedGateResult(request.ReleaseGateRequests[1]));
        var runner = new GovernedDogfoodCampaignRunner(service);

        var result = await runner.RunAsync(request);

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues.Select(issue => issue.Code)));
        Assert.AreEqual(GovernedDogfoodCampaignStatuses.CompletedWithBlockedDecisions, result.Status);
        Assert.AreEqual(1, result.ReadyEvidenceSatisfiedCount);
        Assert.AreEqual(1, result.BlockedDecisionCount);
        Assert.AreEqual(0, result.FailedIterationCount);
        AssertNoAuthority(result);
        Assert.AreEqual(ReleaseReadinessDecisionStatuses.BlockedByMissingEvidence, result.Iterations[1].DecisionStatus);
    }

    [TestMethod]
    public async Task GovernedDogfoodCampaign_FailedIterationDoesNotClaimSuccess()
    {
        var request = ValidCampaign("failed", 2);
        var service = new FakeGovernedReleaseGateService();
        service.Results.Enqueue(ReadyGateResult(request.ReleaseGateRequests[0]));
        service.Results.Enqueue(FailedGateResult(request.ReleaseGateRequests[1]));
        var runner = new GovernedDogfoodCampaignRunner(service);

        var result = await runner.RunAsync(request);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(GovernedDogfoodCampaignStatuses.CompletedWithFailedIterations, result.Status);
        Assert.AreEqual(1, result.ReadyEvidenceSatisfiedCount);
        Assert.AreEqual(0, result.BlockedDecisionCount);
        Assert.AreEqual(1, result.FailedIterationCount);
        Assert.AreEqual(2, result.CompletedIterations);
        AssertNoAuthority(result);
        Assert.IsFalse(result.Iterations[1].DecisionRecordStored);
    }

    [TestMethod]
    public async Task GovernedDogfoodCampaign_MultipleIterationsRunSequentially()
    {
        var request = ValidCampaign("sequential", 3);
        var service = new FakeGovernedReleaseGateService();
        foreach (var item in request.ReleaseGateRequests)
            service.Results.Enqueue(ReadyGateResult(item));
        var runner = new GovernedDogfoodCampaignRunner(service);

        var result = await runner.RunAsync(request);

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEqual(
            request.ReleaseGateRequests.Select(item => item.GovernedReleaseGateRequestId).ToArray(),
            service.Requests.Select(item => item.GovernedReleaseGateRequestId).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, result.Iterations.Select(item => item.IterationNumber).ToArray());
    }

    [TestMethod]
    public async Task GovernedDogfoodCampaign_StopsAtConfiguredMaxIterations()
    {
        var request = ValidCampaign("max", 3) with { MaxIterations = 3 };
        var service = new FakeGovernedReleaseGateService();
        foreach (var item in request.ReleaseGateRequests)
            service.Results.Enqueue(ReadyGateResult(item));
        var runner = new GovernedDogfoodCampaignRunner(service);

        var result = await runner.RunAsync(request);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(3, result.CompletedIterations);
        Assert.AreEqual(3, service.Requests.Count);
    }

    [TestMethod]
    public async Task GovernedDogfoodCampaign_RejectsMoreRequestsThanMaxIterations()
    {
        var request = ValidCampaign("too-many", 2) with { MaxIterations = 1 };
        var service = new FakeGovernedReleaseGateService();
        var runner = new GovernedDogfoodCampaignRunner(service);

        var result = await runner.RunAsync(request);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(GovernedDogfoodCampaignStatuses.Rejected, result.Status);
        Assert.AreEqual(0, service.Requests.Count);
        AssertIssue(result, "ReleaseGateRequestsExceedMaxIterations");
    }

    [TestMethod]
    public async Task GovernedDogfoodCampaign_RejectsDuplicateGovernedReleaseGateRequestIds()
    {
        var request = ValidCampaign("duplicate", 2);
        var duplicate = request.ReleaseGateRequests[1] with { GovernedReleaseGateRequestId = request.ReleaseGateRequests[0].GovernedReleaseGateRequestId };
        request = request with { ReleaseGateRequests = [request.ReleaseGateRequests[0], duplicate] };
        var service = new FakeGovernedReleaseGateService();
        var runner = new GovernedDogfoodCampaignRunner(service);

        var result = await runner.RunAsync(request);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(0, service.Requests.Count);
        AssertIssue(result, "DuplicateGovernedReleaseGateRequestId");
    }

    [TestMethod]
    public async Task GovernedDogfoodCampaign_RejectsProjectMismatch()
    {
        var request = ValidCampaign("mismatch", 1);
        var mismatch = request.ReleaseGateRequests[0] with { ProjectId = DeterministicGuid("other-project") };
        request = request with { ReleaseGateRequests = [mismatch] };
        var service = new FakeGovernedReleaseGateService();
        var runner = new GovernedDogfoodCampaignRunner(service);

        var result = await runner.RunAsync(request);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(0, service.Requests.Count);
        AssertIssue(result, "ReleaseGateRequestProjectMismatch");
    }

    [TestMethod]
    public async Task GovernedDogfoodCampaign_RejectsNullOrInvalidRequest()
    {
        var service = new FakeGovernedReleaseGateService();
        var runner = new GovernedDogfoodCampaignRunner(service);

        var nullResult = await runner.RunAsync(null);
        var invalidResult = await runner.RunAsync(ValidCampaign("invalid", 1) with
        {
            GovernedDogfoodCampaignRequestId = Guid.Empty,
            CampaignName = "",
            RequestedBy = "",
            RequestedAtUtc = default,
            MaxIterations = 0,
            EvidenceReferences = [],
            BoundaryMaxims = [],
            Boundary = ""
        });

        Assert.IsFalse(nullResult.Succeeded);
        Assert.IsFalse(invalidResult.Succeeded);
        Assert.AreEqual(GovernedDogfoodCampaignStatuses.Rejected, nullResult.Status);
        Assert.AreEqual(GovernedDogfoodCampaignStatuses.Rejected, invalidResult.Status);
        Assert.AreEqual(0, service.Requests.Count);
    }

    [TestMethod]
    public async Task GovernedDogfoodCampaign_NeverApprovesReleaseDeploymentOrMerge()
    {
        var request = ValidCampaign("authority", 1);
        var service = new FakeGovernedReleaseGateService();
        service.Results.Enqueue(ReadyGateResult(request.ReleaseGateRequests[0]) with
        {
            ReleaseApproved = true,
            DeploymentApproved = true,
            MergeApproved = true
        });
        var runner = new GovernedDogfoodCampaignRunner(service);

        var result = await runner.RunAsync(request);

        AssertNoAuthority(result);
        AssertNoAuthority(result.Iterations[0]);
    }

    [TestMethod]
    public async Task GovernedDogfoodCampaign_NeverExecutesReleaseSourceRollbackWorkflowOrGit()
    {
        var request = ValidCampaign("execution", 1);
        var service = new FakeGovernedReleaseGateService();
        service.Results.Enqueue(ReadyGateResult(request.ReleaseGateRequests[0]) with
        {
            ReleaseExecuted = true,
            SourceApplyExecuted = true,
            RollbackExecuted = true,
            WorkflowContinued = true,
            WorkflowMutated = true,
            GitOperationExecuted = true
        });
        var runner = new GovernedDogfoodCampaignRunner(service);

        var result = await runner.RunAsync(request);

        AssertNoAuthority(result);
        AssertNoAuthority(result.Iterations[0]);
    }

    [TestMethod]
    public async Task GovernedDogfoodCampaign_HumanReviewRemainsRequired()
    {
        var request = ValidCampaign("human-review", 1);
        var service = new FakeGovernedReleaseGateService();
        service.Results.Enqueue(ReadyGateResult(request.ReleaseGateRequests[0]) with
        {
            HumanReviewRequiredForReleaseApproval = false,
            HumanReviewRequiredForDeployment = false,
            HumanReviewRequiredForMerge = false
        });
        var runner = new GovernedDogfoodCampaignRunner(service);

        var result = await runner.RunAsync(request);

        Assert.IsTrue(result.HumanReviewRequiredForReleaseApproval);
        Assert.IsTrue(result.HumanReviewRequiredForDeployment);
        Assert.IsTrue(result.HumanReviewRequiredForMerge);
        Assert.IsTrue(result.Iterations[0].HumanReviewRequiredForReleaseApproval);
        Assert.IsTrue(result.Iterations[0].HumanReviewRequiredForDeployment);
        Assert.IsTrue(result.Iterations[0].HumanReviewRequiredForMerge);
    }

    [TestMethod]
    public async Task GovernedDogfoodCampaign_CompletedDoesNotMeanReleaseApproved()
    {
        var request = ValidCampaign("completed-not-approved", 1);
        var service = new FakeGovernedReleaseGateService();
        service.Results.Enqueue(ReadyGateResult(request.ReleaseGateRequests[0]));
        var runner = new GovernedDogfoodCampaignRunner(service);

        var result = await runner.RunAsync(request);

        Assert.AreEqual(GovernedDogfoodCampaignStatuses.Completed, result.Status);
        Assert.IsFalse(result.ReleaseApproved);
        Assert.IsFalse(result.DeploymentApproved);
        Assert.IsFalse(result.MergeApproved);
    }

    [TestMethod]
    public async Task GovernedDogfoodCampaign_ReadyEvidenceCountDoesNotMeanReleaseApproved()
    {
        var request = ValidCampaign("ready-count", 2);
        var service = new FakeGovernedReleaseGateService();
        service.Results.Enqueue(ReadyGateResult(request.ReleaseGateRequests[0]));
        service.Results.Enqueue(ReadyGateResult(request.ReleaseGateRequests[1]));
        var runner = new GovernedDogfoodCampaignRunner(service);

        var result = await runner.RunAsync(request);

        Assert.AreEqual(2, result.ReadyEvidenceSatisfiedCount);
        Assert.IsFalse(result.ReleaseApproved);
        Assert.IsFalse(result.ReleaseExecuted);
    }

    [TestMethod]
    public async Task GovernedDogfoodCampaign_RejectsPrivateRawMaterial()
    {
        var request = ValidCampaign("unsafe", 1) with { CampaignName = "raw prompt campaign" };
        var service = new FakeGovernedReleaseGateService();
        var runner = new GovernedDogfoodCampaignRunner(service);

        var result = await runner.RunAsync(request);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(GovernedDogfoodCampaignStatuses.Rejected, result.Status);
        Assert.AreEqual(0, service.Requests.Count);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("raw prompt", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task GovernedDogfoodCampaign_RejectsAuthorityClaims()
    {
        var request = ValidCampaign("authority-claim", 1) with { BoundaryMaxims = ["green to ship"] };
        var service = new FakeGovernedReleaseGateService();
        var runner = new GovernedDogfoodCampaignRunner(service);

        var result = await runner.RunAsync(request);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(GovernedDogfoodCampaignStatuses.Rejected, result.Status);
        Assert.AreEqual(0, service.Requests.Count);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("green to ship", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task GovernedDogfoodCampaign_DoesNotEchoUnsafeMaterial()
    {
        var request = ValidCampaign("unsafe-issue", 1);
        var service = new FakeGovernedReleaseGateService();
        service.Results.Enqueue(FailedGateResult(request.ReleaseGateRequests[0]) with
        {
            Issues = [new GovernedReleaseGateIssue("Unsafe", "field", "raw prompt secret green to ship")]
        });
        var runner = new GovernedDogfoodCampaignRunner(service);

        var result = await runner.RunAsync(request);
        var serialized = JsonSerializer.Serialize(result);

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(serialized.Contains("raw prompt", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains("green to ship", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void GovernedDogfoodCampaign_RunnerDependsOnlyOnGovernedReleaseGateService()
    {
        var constructor = typeof(GovernedDogfoodCampaignRunner).GetConstructors().Single();
        CollectionAssert.AreEqual(
            new[] { typeof(IGovernedReleaseGateService) },
            constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
    }

    [TestMethod]
    public void GovernedDogfoodCampaign_RunnerDoesNotUseEvaluatorStoreSqlApiCliGitRuntimeAgentsModelsToolsMemoryOrRetrieval()
    {
        var root = RepositoryRoot();
        var core = File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "GovernedDogfoodCampaign.cs"));
        var runner = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "GovernedDogfoodCampaignRunner.cs"));

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
            "ReleaseApprovalService",
            "DeploymentApprovalService",
            "MergeApprovalService",
            "ReleaseExecutionService",
            "ControlledSourceApplyExecutor",
            "ControlledRollbackExecutor",
            "GovernedWorkflowContinuationService",
            "Process.Start",
            "ProcessStartInfo",
            "GitCommitService",
            "GitPushService",
            "GitMergeService",
            "GitHubPullRequestService",
            "IHostedService",
            "BackgroundService",
            "Scheduler",
            "AgentDispatch",
            "ModelProvider",
            "ToolInvoker",
            "PromoteMemory",
            "ActivateRetrieval",
            "Weaviate",
            "Embedding"
        })
        {
            Assert.IsFalse(core.Contains(marker, StringComparison.Ordinal), $"Forbidden marker in core: {marker}");
            Assert.IsFalse(runner.Contains(marker, StringComparison.Ordinal), $"Forbidden marker in runner: {marker}");
        }
    }

    [TestMethod]
    public async Task GovernedDogfoodCampaign_ReadyEvidenceSatisfiedStillDoesNotApproveRelease()
    {
        var request = ValidCampaign("reg-ready", 1);
        var service = new FakeGovernedReleaseGateService();
        service.Results.Enqueue(ReadyGateResult(request.ReleaseGateRequests[0]));
        var runner = new GovernedDogfoodCampaignRunner(service);

        var result = await runner.RunAsync(request);

        Assert.AreEqual(ReleaseReadinessDecisionStatuses.ReadyEvidenceSatisfied, result.Iterations[0].DecisionStatus);
        Assert.IsFalse(result.ReleaseApproved);
        Assert.IsFalse(result.Iterations[0].ReleaseApproved);
    }

    [TestMethod]
    public async Task GovernedDogfoodCampaign_BlockedDecisionStillDoesNotApproveRelease()
    {
        var request = ValidCampaign("reg-blocked", 1);
        var service = new FakeGovernedReleaseGateService();
        service.Results.Enqueue(BlockedGateResult(request.ReleaseGateRequests[0]));
        var runner = new GovernedDogfoodCampaignRunner(service);

        var result = await runner.RunAsync(request);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(GovernedDogfoodCampaignStatuses.CompletedWithBlockedDecisions, result.Status);
        Assert.IsFalse(result.ReleaseApproved);
        Assert.IsFalse(result.Iterations[0].ReleaseApproved);
    }

    [TestMethod]
    public async Task GovernedDogfoodCampaign_NoStoredDecisionRecordStillMeansNoSuccessfulIteration()
    {
        var request = ValidCampaign("reg-no-record", 1);
        var service = new FakeGovernedReleaseGateService();
        service.Results.Enqueue(FailedGateResult(request.ReleaseGateRequests[0]));
        var runner = new GovernedDogfoodCampaignRunner(service);

        var result = await runner.RunAsync(request);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(1, result.FailedIterationCount);
        Assert.IsFalse(result.Iterations[0].DecisionRecordStored);
        Assert.IsNull(result.Iterations[0].ReleaseReadinessDecisionRecordId);
    }

    private static GovernedDogfoodCampaignRequest ValidCampaign(string suffix, int iterations)
    {
        var releaseGateRequests = Enumerable.Range(1, iterations)
            .Select(index => ValidReleaseGateRequest($"{suffix}-{index}"))
            .ToArray();

        return new GovernedDogfoodCampaignRequest
        {
            GovernedDogfoodCampaignRequestId = DeterministicGuid($"campaign-{suffix}"),
            ProjectId = ProjectId,
            CampaignName = $"governed-dogfood-campaign-{suffix}",
            RequestedBy = $"human-reviewer-{suffix}",
            RequestedAtUtc = RequestedAt,
            MaxIterations = iterations,
            ReleaseGateRequests = releaseGateRequests,
            EvidenceReferences = [$"campaign-request:{suffix}", $"human-review:{suffix}"],
            BoundaryMaxims = ["Repeated governed dogfood campaign is not autonomy.", "Human review remains required."]
        };
    }

    private static GovernedReleaseGateRequest ValidReleaseGateRequest(string suffix) => new()
    {
        GovernedReleaseGateRequestId = DeterministicGuid($"governed-release-gate-{suffix}"),
        ProjectId = ProjectId,
        ReleaseReadinessReport = CompleteReport(suffix),
        RequestedBy = $"human-reviewer-{suffix}",
        RequestedAtUtc = RequestedAt,
        EvidenceReferences = [$"release-readiness-report:{suffix}", $"human-review:{suffix}"],
        BoundaryMaxims = ["Governed release gate is not release approval.", "Human review remains required."]
    };

    private static GovernedReleaseGateResult ReadyGateResult(GovernedReleaseGateRequest request) =>
        StoredGateResult(request, CompleteReport(request.GovernedReleaseGateRequestId.ToString("N")));

    private static GovernedReleaseGateResult BlockedGateResult(GovernedReleaseGateRequest request)
    {
        var report = Rehash(request.ReleaseReadinessReport with
        {
            Status = ReleaseReadinessReportStatuses.BlockedByMissingEvidence,
            Findings = [Finding("EvidenceMissing", ReleaseReadinessFindingSeverities.Blocking)]
        });
        return StoredGateResult(request, report);
    }

    private static GovernedReleaseGateResult StoredGateResult(GovernedReleaseGateRequest request, ReleaseReadinessReport report)
    {
        var decision = new ReleaseReadinessGateEvaluator().Evaluate(new ReleaseReadinessGateRequest
        {
            ReleaseReadinessGateRequestId = request.GovernedReleaseGateRequestId,
            ProjectId = request.ProjectId,
            ReleaseReadinessReport = report,
            RequestedAtUtc = request.RequestedAtUtc,
            EvidenceReferences = request.EvidenceReferences,
            BoundaryMaxims = request.BoundaryMaxims
        });

        return new GovernedReleaseGateResult
        {
            GovernedReleaseGateRequestId = request.GovernedReleaseGateRequestId,
            ProjectId = request.ProjectId,
            Succeeded = true,
            Status = GovernedReleaseGateStatuses.DecisionRecordStored,
            ReleaseReadinessGateRan = true,
            DecisionRecordStored = true,
            DecisionRecord = decision,
            ReleaseReadinessEvidenceSatisfied = decision.ReleaseReadinessEvidenceSatisfied,
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
            CompletedAtUtc = RequestedAt,
            Boundary = GovernedReleaseGateBoundaryText.Boundary
        };
    }

    private static GovernedReleaseGateResult FailedGateResult(GovernedReleaseGateRequest request) => new()
    {
        GovernedReleaseGateRequestId = request.GovernedReleaseGateRequestId,
        ProjectId = request.ProjectId,
        Succeeded = false,
        Status = GovernedReleaseGateStatuses.DecisionRecordSaveFailed,
        ReleaseReadinessGateRan = true,
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
        Issues = [new GovernedReleaseGateIssue("DecisionRecordSaveFailed", "store", "Decision record was not stored.")],
        Warnings = GovernedReleaseGateBoundaryText.Warnings,
        CompletedAtUtc = RequestedAt,
        Boundary = GovernedReleaseGateBoundaryText.Boundary
    };

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
            Findings = [Finding("ReportEvidenceComplete", ReleaseReadinessFindingSeverities.Info)],
            EvidenceReferences = [$"release-readiness-report:{suffix}", $"workflow-transition-record:{suffix}"],
            BoundaryMaxims = ["Release readiness report is evidence summary only.", "Release readiness report is not release approval."],
            ReportedAtUtc = RequestedAt.AddMinutes(-5),
            ReleaseReadinessReportHash = "sha256:pending"
        };

        return Rehash(report);
    }

    private static ReleaseReadinessReportFinding Finding(string code, string severity) => new()
    {
        Code = code,
        Severity = severity,
        Field = "ReleaseReadinessReport",
        Message = $"{code} recorded as evidence only."
    };

    private static ReleaseReadinessReport Rehash(ReleaseReadinessReport report) =>
        report with { ReleaseReadinessReportHash = ReleaseReadinessReportHashing.ComputeReportHash(report) };

    private static void AssertNoAuthority(GovernedDogfoodCampaignResult result)
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
        Assert.IsTrue(result.HumanReviewRequiredForReleaseApproval);
        Assert.IsTrue(result.HumanReviewRequiredForDeployment);
        Assert.IsTrue(result.HumanReviewRequiredForMerge);
    }

    private static void AssertNoAuthority(GovernedDogfoodCampaignIterationResult result)
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
        Assert.IsTrue(result.HumanReviewRequiredForReleaseApproval);
        Assert.IsTrue(result.HumanReviewRequiredForDeployment);
        Assert.IsTrue(result.HumanReviewRequiredForMerge);
    }

    private static void AssertIssue(GovernedDogfoodCampaignResult result, string code) =>
        Assert.IsTrue(result.Issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)), string.Join("; ", result.Issues.Select(issue => issue.Code)));

    private static readonly Guid ProjectId = Guid.Parse("6f8b50df-7cae-405f-90ea-b4b0c6d19b4d");

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
