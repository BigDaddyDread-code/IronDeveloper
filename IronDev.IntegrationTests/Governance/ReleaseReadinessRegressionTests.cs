using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("ReleaseReadinessRegression")]
[TestCategory("PR222")]
public sealed class ReleaseReadinessRegressionTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 17, 23, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public void ReleaseReadinessRegression_ReportCompleteDoesNotMeanReleaseReady()
    {
        var report = CompleteReport("complete-not-ready");

        Assert.AreEqual(ReleaseReadinessReportStatuses.Complete, report.Status);
        Assert.IsFalse(report.ReleaseReadinessDecided);
        Assert.IsFalse(report.ReleaseReady);
        Assert.IsFalse(report.ReleaseApproved);
        Assert.IsFalse(report.DeploymentApproved);
        Assert.IsFalse(report.MergeApproved);
        Assert.IsFalse(report.SourceApplyExecutedByReport);
        Assert.IsFalse(report.RollbackExecutedByReport);
        Assert.IsFalse(report.WorkflowMutatedByReport);
        Assert.IsFalse(report.GitOperationExecutedByReport);
        Assert.IsTrue(report.HumanReviewRequiredForReadiness);
        Assert.IsTrue(report.HumanReviewRequiredForReleaseApproval);
        Assert.IsTrue(ReleaseReadinessReportValidation.Validate(report).IsValid);
    }

    [TestMethod]
    public void ReleaseReadinessRegression_ReadyEvidenceSatisfiedDecisionDoesNotApproveRelease()
    {
        var decision = Evaluate(CompleteReport("ready-evidence"));

        Assert.AreEqual(ReleaseReadinessDecisionStatuses.ReadyEvidenceSatisfied, decision.DecisionStatus);
        Assert.IsTrue(decision.ReleaseReadinessEvidenceSatisfied);
        AssertDecisionHasNoAuthority(decision);
    }

    [TestMethod]
    public async Task ReleaseReadinessRegression_GovernedGateSuccessIsStoredEvidenceOnly()
    {
        var store = new FakeReleaseReadinessDecisionRecordStore();
        var service = new GovernedReleaseGateService(new ReleaseReadinessGateEvaluator(), store);

        var result = await service.EvaluateAsync(ValidGovernedRequest("success-is-evidence"));

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues.Select(issue => issue.Code)));
        Assert.AreEqual(GovernedReleaseGateStatuses.DecisionRecordStored, result.Status);
        Assert.IsTrue(result.ReleaseReadinessGateRan);
        Assert.IsTrue(result.DecisionRecordStored);
        Assert.AreEqual(1, store.Saved.Count);
        Assert.IsNotNull(result.DecisionRecord);
        AssertResultHasNoAuthority(result);
        AssertDecisionHasNoAuthority(result.DecisionRecord!);
    }

    [TestMethod]
    public async Task ReleaseReadinessRegression_GovernedGateSaveFailureDoesNotClaimSuccess()
    {
        var service = new GovernedReleaseGateService(
            new ReleaseReadinessGateEvaluator(),
            new FakeReleaseReadinessDecisionRecordStore { ThrowOnSave = true });

        var result = await service.EvaluateAsync(ValidGovernedRequest("save-failure"));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(GovernedReleaseGateStatuses.DecisionRecordSaveFailed, result.Status);
        Assert.IsTrue(result.ReleaseReadinessGateRan);
        Assert.IsFalse(result.DecisionRecordStored);
        Assert.IsNull(result.DecisionRecord);
        AssertResultHasNoAuthority(result);
    }

    [TestMethod]
    public async Task ReleaseReadinessRegression_GovernedGateReadBackFailureDoesNotClaimSuccess()
    {
        var service = new GovernedReleaseGateService(
            new ReleaseReadinessGateEvaluator(),
            new FakeReleaseReadinessDecisionRecordStore { HideOnRead = true });

        var result = await service.EvaluateAsync(ValidGovernedRequest("readback-failure"));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(GovernedReleaseGateStatuses.DecisionRecordReadBackFailed, result.Status);
        Assert.IsTrue(result.ReleaseReadinessGateRan);
        Assert.IsFalse(result.DecisionRecordStored);
        Assert.IsNull(result.DecisionRecord);
        AssertResultHasNoAuthority(result);
    }

    [TestMethod]
    public async Task ReleaseReadinessRegression_BlockedDecisionCanBeStoredSuccessfulEvaluation()
    {
        var store = new FakeReleaseReadinessDecisionRecordStore();
        var service = new GovernedReleaseGateService(new ReleaseReadinessGateEvaluator(), store);
        var blockedReport = Rehash(CompleteReport("blocked-stored") with
        {
            Status = ReleaseReadinessReportStatuses.BlockedByMissingEvidence,
            Findings = [Finding("EvidenceMissing", ReleaseReadinessFindingSeverities.Blocking)]
        });

        var result = await service.EvaluateAsync(ValidGovernedRequest("blocked-stored") with { ReleaseReadinessReport = blockedReport });

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues.Select(issue => issue.Code)));
        Assert.IsTrue(result.DecisionRecordStored);
        Assert.IsNotNull(result.DecisionRecord);
        Assert.AreEqual(ReleaseReadinessDecisionStatuses.BlockedByMissingEvidence, result.DecisionRecord!.DecisionStatus);
        Assert.IsFalse(result.ReleaseReadinessEvidenceSatisfied);
        AssertResultHasNoAuthority(result);
        AssertDecisionHasNoAuthority(result.DecisionRecord);
    }

    [TestMethod]
    public void ReleaseReadinessRegression_DecisionStoreRemainsAppendOnly()
    {
        var root = RepositoryRoot();
        var migration = File.ReadAllText(Path.Combine(root, "Database", "migrate_release_readiness_decision_record.sql"));
        var storeTests = File.ReadAllText(Path.Combine(root, "IronDev.IntegrationTests", "Governance", "ReleaseReadinessDecisionRecordStoreTests.cs"));
        var store = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlReleaseReadinessDecisionRecordStore.cs"));

        StringAssert.Contains(migration, "TR_ReleaseReadinessDecisionRecord_BlockUpdateDelete");
        StringAssert.Contains(migration, "AFTER UPDATE, DELETE");
        StringAssert.Contains(migration, "usp_ReleaseReadinessDecisionRecord_Save");
        StringAssert.Contains(store, "ReleaseReadinessDecisionRecordValidation.Validate");
        StringAssert.Contains(storeTests, "ReleaseReadinessDecisionRecordStore_SaveSameRecordTwiceIsIdempotent");
        StringAssert.Contains(storeTests, "ReleaseReadinessDecisionRecordStore_SaveSameIdDifferentHashIsRejected");
        StringAssert.Contains(storeTests, "ReleaseReadinessDecisionRecordStore_SaveSameHashDifferentIdIsRejected");
        StringAssert.Contains(storeTests, "ReleaseReadinessDecisionRecordStore_DirectSqlUpdateIsBlocked");
        StringAssert.Contains(storeTests, "ReleaseReadinessDecisionRecordStore_DirectSqlDeleteIsBlocked");
    }

    [TestMethod]
    public void ReleaseReadinessRegression_DecisionStoreRejectsReleaseAuthority()
    {
        var decision = Rehash(Evaluate(CompleteReport("store-rejects-authority")) with { ReleaseApproved = true });

        var validation = ReleaseReadinessDecisionRecordValidation.Validate(decision);

        Assert.IsFalse(validation.IsValid);
        Assert.IsTrue(validation.Issues.Any(issue => issue.Code == "ReleaseApprovedRejected"));
    }

    [TestMethod]
    public void ReleaseReadinessRegression_PrivateRawMaterialCannotReachDecisionRecord()
    {
        var report = Rehash(CompleteReport("private-raw") with
        {
            EvidenceReferences = ["release-readiness-report:private-raw", "raw prompt hidden reasoning"]
        });

        var decision = Evaluate(report);
        var serialized = JsonSerializer.Serialize(decision);

        Assert.AreNotEqual(ReleaseReadinessDecisionStatuses.ReadyEvidenceSatisfied, decision.DecisionStatus);
        AssertDecisionHasNoAuthority(decision);
        Assert.IsFalse(serialized.Contains("raw prompt", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains("hidden reasoning", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ReleaseReadinessRegression_AuthorityClaimsCannotCreateReadyDecision()
    {
        var report = Rehash(CompleteReport("authority-claim") with
        {
            BoundaryMaxims = ["release approved", "safe to deploy"]
        });

        var decision = Evaluate(report);

        Assert.AreNotEqual(ReleaseReadinessDecisionStatuses.ReadyEvidenceSatisfied, decision.DecisionStatus);
        AssertDecisionHasNoAuthority(decision);
    }

    [TestMethod]
    public void ReleaseReadinessRegression_GateNormalizesReportHashForDecisionRecord()
    {
        var report = CompleteReport("hash-normalized");
        var decision = Evaluate(report);

        StringAssert.StartsWith(report.ReleaseReadinessReportHash, "sha256:");
        Assert.AreEqual(64, decision.ReleaseReadinessReportHash.Length);
        Assert.IsFalse(decision.ReleaseReadinessReportHash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(64, decision.ReleaseReadinessDecisionRecordHash.Length);
    }

    [TestMethod]
    public void ReleaseReadinessRegression_DecisionRecordHashMismatchRejected()
    {
        var decision = Evaluate(CompleteReport("hash-mismatch")) with
        {
            ReleaseReadinessDecisionRecordHash = RawHash("wrong")
        };

        var validation = ReleaseReadinessDecisionRecordValidation.Validate(decision);

        Assert.IsFalse(validation.IsValid);
        Assert.IsTrue(validation.Issues.Any(issue => issue.Code == "ReleaseReadinessDecisionRecordHashMismatch"));
    }

    [TestMethod]
    public async Task ReleaseReadinessRegression_HumanReviewRequiredAcrossReportDecisionStoreAndService()
    {
        var report = CompleteReport("human-review");
        var decision = Evaluate(report);
        var service = new GovernedReleaseGateService(new ReleaseReadinessGateEvaluator(), new FakeReleaseReadinessDecisionRecordStore());
        var result = await service.EvaluateAsync(ValidGovernedRequest("human-review"));

        Assert.IsTrue(report.HumanReviewRequiredForReadiness);
        Assert.IsTrue(report.HumanReviewRequiredForReleaseApproval);
        Assert.IsTrue(decision.HumanReviewRequiredForReleaseApproval);
        Assert.IsTrue(decision.HumanReviewRequiredForDeployment);
        Assert.IsTrue(decision.HumanReviewRequiredForMerge);
        Assert.IsTrue(result.HumanReviewRequiredForReleaseApproval);
        Assert.IsTrue(result.HumanReviewRequiredForDeployment);
        Assert.IsTrue(result.HumanReviewRequiredForMerge);
    }

    [TestMethod]
    public void ReleaseReadinessRegression_StaticProductionFilesDoNotGainReleaseExecutionAuthority()
    {
        var root = RepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "ReleaseReadinessReport.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "ReleaseReadinessDecisionRecord.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "ReleaseReadinessGateEvaluator.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "GovernedReleaseGate.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "GovernedReleaseGateService.cs"),
            Path.Combine(root, "IronDev.Api", "Controllers", "GovernedReleaseGateController.cs"),
            Path.Combine(root, "tools", "IronDev.Cli", "CliReleaseGate.cs")
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var forbidden in ForbiddenProductionTokensFor(file))
            {
                Assert.IsFalse(text.Contains(forbidden, StringComparison.Ordinal), $"Forbidden token in {file}: {forbidden}");
            }
        }
    }

    private static string[] ForbiddenProductionTokensFor(string file)
    {
        var common =
            new[]
            {
                "ReleaseApproved = true",
                "DeploymentApproved = true",
                "MergeApproved = true",
                "ReleaseExecuted = true",
                "SourceApplyExecuted = true",
                "RollbackExecuted = true",
                "WorkflowContinued = true",
                "WorkflowMutated = true",
                "GitOperationExecuted = true",
                "ReleaseApprovalService",
                "DeploymentApprovalService",
                "MergeApprovalService",
                "ReleaseExecutionService",
                "DeployRelease",
                "MergeRelease",
                "ExecuteRelease",
                "TagRelease",
                "ControlledSourceApplyExecutor",
                "ControlledRollbackExecutor",
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
            };

        return file.EndsWith("GovernedReleaseGateService.cs", StringComparison.Ordinal)
            ? common.Except(["ReleaseReadinessGateEvaluator", "IReleaseReadinessDecisionRecordStore", "SaveAsync", "GetAsync", "GetByRecordHashAsync"]).ToArray()
            : common;
    }

    private static ReleaseReadinessDecisionRecord Evaluate(ReleaseReadinessReport report) =>
        new ReleaseReadinessGateEvaluator().Evaluate(new ReleaseReadinessGateRequest
        {
            ReleaseReadinessGateRequestId = DeterministicGuid($"gate-{report.ReleaseReadinessReportId}"),
            ProjectId = report.ProjectId,
            ReleaseReadinessReport = report,
            RequestedAtUtc = Now,
            EvidenceReferences = report.EvidenceReferences,
            BoundaryMaxims = report.BoundaryMaxims,
            Boundary = ReleaseReadinessGateBoundaryText.Boundary
        });

    private static GovernedReleaseGateRequest ValidGovernedRequest(string suffix) => new()
    {
        GovernedReleaseGateRequestId = DeterministicGuid($"governed-release-gate-regression-{suffix}"),
        ProjectId = ProjectId,
        ReleaseReadinessReport = CompleteReport(suffix),
        RequestedBy = $"human-reviewer-{suffix}",
        RequestedAtUtc = Now,
        EvidenceReferences = [$"release-readiness-report:{suffix}", $"human-review:{suffix}"],
        BoundaryMaxims = ["Governed release gate is not release approval.", "Human review remains required."]
    };

    private static ReleaseReadinessReport CompleteReport(string suffix)
    {
        var report = new ReleaseReadinessReport
        {
            ReleaseReadinessReportId = DeterministicGuid($"release-readiness-report-regression-{suffix}"),
            ProjectId = ProjectId,
            ReleaseReadinessReportRequestId = DeterministicGuid($"release-readiness-report-request-regression-{suffix}"),
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
            ReportedAtUtc = Now.AddMinutes(-5),
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

    private static void AssertResultHasNoAuthority(GovernedReleaseGateResult result)
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

    private static void AssertDecisionHasNoAuthority(ReleaseReadinessDecisionRecord decision)
    {
        Assert.IsFalse(decision.ReleaseApproved);
        Assert.IsFalse(decision.DeploymentApproved);
        Assert.IsFalse(decision.MergeApproved);
        Assert.IsFalse(decision.SourceApplyExecutedByDecision);
        Assert.IsFalse(decision.RollbackExecutedByDecision);
        Assert.IsFalse(decision.WorkflowMutatedByDecision);
        Assert.IsFalse(decision.GitOperationExecutedByDecision);
        Assert.IsFalse(decision.ReleaseExecutedByDecision);
        Assert.IsTrue(decision.HumanReviewRequiredForReleaseApproval);
        Assert.IsTrue(decision.HumanReviewRequiredForDeployment);
        Assert.IsTrue(decision.HumanReviewRequiredForMerge);
    }

    private static ReleaseReadinessReport Rehash(ReleaseReadinessReport report) =>
        report with { ReleaseReadinessReportHash = ReleaseReadinessReportHashing.ComputeReportHash(report) };

    private static ReleaseReadinessDecisionRecord Rehash(ReleaseReadinessDecisionRecord record) =>
        record with { ReleaseReadinessDecisionRecordHash = ReleaseReadinessDecisionRecordHashing.ComputeHash(record) };

    private static readonly Guid ProjectId = Guid.Parse("1843549d-898d-4506-9a6f-02a3918f4f27");

    private static Guid DeterministicGuid(string value) => new(MD5.HashData(Encoding.UTF8.GetBytes(value)));

    private static string H(string value) => $"sha256:{RawHash(value)}";

    private static string RawHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class FakeReleaseReadinessDecisionRecordStore : IReleaseReadinessDecisionRecordStore
    {
        public bool ThrowOnSave { get; init; }
        public bool HideOnRead { get; init; }
        public List<ReleaseReadinessDecisionRecord> Saved { get; } = [];

        public Task SaveAsync(ReleaseReadinessDecisionRecord record, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave)
                throw new InvalidOperationException("save failed");

            var existing = Saved.FirstOrDefault(item =>
                item.ProjectId == record.ProjectId &&
                item.ReleaseReadinessDecisionRecordId == record.ReleaseReadinessDecisionRecordId);
            if (existing is not null)
            {
                if (!string.Equals(existing.ReleaseReadinessDecisionRecordHash, record.ReleaseReadinessDecisionRecordHash, StringComparison.Ordinal))
                    throw new InvalidOperationException("same id different hash");
                return Task.CompletedTask;
            }

            Saved.Add(record);
            return Task.CompletedTask;
        }

        public Task<ReleaseReadinessDecisionRecord?> GetAsync(Guid projectId, Guid releaseReadinessDecisionRecordId, CancellationToken cancellationToken = default)
        {
            if (HideOnRead)
                return Task.FromResult<ReleaseReadinessDecisionRecord?>(null);
            return Task.FromResult(Saved.FirstOrDefault(item => item.ProjectId == projectId && item.ReleaseReadinessDecisionRecordId == releaseReadinessDecisionRecordId));
        }

        public Task<ReleaseReadinessDecisionRecord?> GetByRecordHashAsync(Guid projectId, string releaseReadinessDecisionRecordHash, CancellationToken cancellationToken = default)
        {
            if (HideOnRead)
                return Task.FromResult<ReleaseReadinessDecisionRecord?>(null);
            return Task.FromResult(Saved.FirstOrDefault(item => item.ProjectId == projectId && string.Equals(item.ReleaseReadinessDecisionRecordHash, releaseReadinessDecisionRecordHash, StringComparison.Ordinal)));
        }

        public Task<IReadOnlyList<ReleaseReadinessDecisionRecord>> ListByReleaseReadinessReportAsync(Guid projectId, Guid releaseReadinessReportId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReleaseReadinessDecisionRecord>>(Saved.Where(item => item.ProjectId == projectId && item.ReleaseReadinessReportId == releaseReadinessReportId).ToArray());

        public Task<IReadOnlyList<ReleaseReadinessDecisionRecord>> ListByWorkflowRunAsync(Guid projectId, string workflowRunId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReleaseReadinessDecisionRecord>>(Saved.Where(item => item.ProjectId == projectId && item.WorkflowRunId == workflowRunId).ToArray());

        public Task<IReadOnlyList<ReleaseReadinessDecisionRecord>> ListBySubjectAsync(Guid projectId, string subjectKind, string subjectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReleaseReadinessDecisionRecord>>(Saved.Where(item => item.ProjectId == projectId && item.SubjectKind == subjectKind && item.SubjectId == subjectId).ToArray());
    }
}
