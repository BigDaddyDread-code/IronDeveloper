using System.Security.Cryptography;
using System.Text;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("GovernedReleaseGate")]
[TestCategory("PR221")]
public sealed class GovernedReleaseGateTests
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 6, 17, 21, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task GovernedReleaseGate_CompleteReportStoresReadyEvidenceDecision()
    {
        var store = new FakeReleaseReadinessDecisionRecordStore();
        var service = new GovernedReleaseGateService(new ReleaseReadinessGateEvaluator(), store);
        var request = ValidRequest("ready");

        var result = await service.EvaluateAsync(request);

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues.Select(issue => issue.Code)));
        Assert.AreEqual(GovernedReleaseGateStatuses.DecisionRecordStored, result.Status);
        Assert.IsTrue(result.ReleaseReadinessGateRan);
        Assert.IsTrue(result.DecisionRecordStored);
        Assert.IsTrue(result.ReleaseReadinessEvidenceSatisfied);
        Assert.AreEqual(1, store.Saved.Count);
        Assert.IsNotNull(result.DecisionRecord);
        Assert.AreEqual(request.GovernedReleaseGateRequestId, result.DecisionRecord!.ReleaseReadinessDecisionRecordId);
        Assert.AreEqual(ReleaseReadinessDecisionStatuses.ReadyEvidenceSatisfied, result.DecisionRecord.DecisionStatus);
        AssertAllAuthorityFalse(result);
        AssertAllAuthorityFalse(result.DecisionRecord);
    }

    [TestMethod]
    public async Task GovernedReleaseGate_BlockedReportStoresBlockedDecision()
    {
        var store = new FakeReleaseReadinessDecisionRecordStore();
        var service = new GovernedReleaseGateService(new ReleaseReadinessGateEvaluator(), store);
        var report = Rehash(CompleteReport("blocked") with
        {
            Status = ReleaseReadinessReportStatuses.BlockedByMissingEvidence,
            Findings = [Finding("EvidenceMissing", ReleaseReadinessFindingSeverities.Blocking)]
        });

        var result = await service.EvaluateAsync(ValidRequest("blocked") with { ReleaseReadinessReport = report });

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues.Select(issue => issue.Code)));
        Assert.IsTrue(result.ReleaseReadinessGateRan);
        Assert.IsTrue(result.DecisionRecordStored);
        Assert.IsFalse(result.ReleaseReadinessEvidenceSatisfied);
        Assert.IsNotNull(result.DecisionRecord);
        Assert.AreEqual(ReleaseReadinessDecisionStatuses.BlockedByMissingEvidence, result.DecisionRecord!.DecisionStatus);
        Assert.AreEqual(1, store.Saved.Count);
        AssertAllAuthorityFalse(result);
        AssertAllAuthorityFalse(result.DecisionRecord);
    }

    [TestMethod]
    public async Task GovernedReleaseGate_SameRequestSameEvidenceIsIdempotent()
    {
        var store = new FakeReleaseReadinessDecisionRecordStore();
        var service = new GovernedReleaseGateService(new ReleaseReadinessGateEvaluator(), store);
        var request = ValidRequest("idempotent");

        var first = await service.EvaluateAsync(request);
        var second = await service.EvaluateAsync(request);

        Assert.IsTrue(first.Succeeded);
        Assert.IsTrue(second.Succeeded);
        Assert.AreEqual(1, store.Saved.Count);
        Assert.AreEqual(first.DecisionRecord!.ReleaseReadinessDecisionRecordHash, second.DecisionRecord!.ReleaseReadinessDecisionRecordHash);
    }

    [TestMethod]
    public async Task GovernedReleaseGate_SameRequestChangedEvidenceFails()
    {
        var store = new FakeReleaseReadinessDecisionRecordStore();
        var service = new GovernedReleaseGateService(new ReleaseReadinessGateEvaluator(), store);
        var request = ValidRequest("conflict");
        var changedReport = Rehash(request.ReleaseReadinessReport with { WorkflowTransitionRecordValid = false });

        var first = await service.EvaluateAsync(request);
        var second = await service.EvaluateAsync(request with { ReleaseReadinessReport = changedReport });

        Assert.IsTrue(first.Succeeded);
        Assert.IsFalse(second.Succeeded);
        Assert.AreEqual(GovernedReleaseGateStatuses.DecisionRecordSaveFailed, second.Status);
        Assert.IsTrue(second.ReleaseReadinessGateRan);
        Assert.IsFalse(second.DecisionRecordStored);
        Assert.IsNull(second.DecisionRecord);
        Assert.AreEqual(1, store.Saved.Count);
    }

    [TestMethod]
    public async Task GovernedReleaseGate_SaveFailureDoesNotClaimSuccess()
    {
        var store = new FakeReleaseReadinessDecisionRecordStore { ThrowOnSave = true };
        var service = new GovernedReleaseGateService(new ReleaseReadinessGateEvaluator(), store);

        var result = await service.EvaluateAsync(ValidRequest("save-failed"));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(GovernedReleaseGateStatuses.DecisionRecordSaveFailed, result.Status);
        Assert.IsTrue(result.ReleaseReadinessGateRan);
        Assert.IsFalse(result.DecisionRecordStored);
        Assert.IsNull(result.DecisionRecord);
        AssertAllAuthorityFalse(result);
    }

    [TestMethod]
    public async Task GovernedReleaseGate_ReadBackFailureDoesNotClaimSuccess()
    {
        var store = new FakeReleaseReadinessDecisionRecordStore { HideOnRead = true };
        var service = new GovernedReleaseGateService(new ReleaseReadinessGateEvaluator(), store);

        var result = await service.EvaluateAsync(ValidRequest("readback-failed"));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(GovernedReleaseGateStatuses.DecisionRecordReadBackFailed, result.Status);
        Assert.IsTrue(result.ReleaseReadinessGateRan);
        Assert.IsFalse(result.DecisionRecordStored);
        Assert.IsNull(result.DecisionRecord);
        Assert.AreEqual(1, store.Saved.Count);
    }

    [TestMethod]
    public async Task GovernedReleaseGate_RejectsUnsafeRequestMaterial()
    {
        var store = new FakeReleaseReadinessDecisionRecordStore();
        var service = new GovernedReleaseGateService(new ReleaseReadinessGateEvaluator(), store);
        var request = ValidRequest("unsafe") with { RequestedBy = "raw prompt release approved" };

        var result = await service.EvaluateAsync(request);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(GovernedReleaseGateStatuses.Rejected, result.Status);
        Assert.IsFalse(result.ReleaseReadinessGateRan);
        Assert.IsFalse(result.DecisionRecordStored);
        Assert.IsNull(result.DecisionRecord);
        Assert.AreEqual(0, store.Saved.Count);
    }

    [TestMethod]
    public async Task GovernedReleaseGate_NeverApprovesReleaseDeploymentOrMerge()
    {
        var service = new GovernedReleaseGateService(new ReleaseReadinessGateEvaluator(), new FakeReleaseReadinessDecisionRecordStore());

        var result = await service.EvaluateAsync(ValidRequest("authority"));

        Assert.IsTrue(result.Succeeded);
        AssertAllAuthorityFalse(result);
        AssertAllAuthorityFalse(result.DecisionRecord!);
    }

    [TestMethod]
    public void GovernedReleaseGate_StaticBoundariesStayNarrow()
    {
        var root = RepositoryRoot();
        var core = File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "GovernedReleaseGate.cs"));
        var service = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "GovernedReleaseGateService.cs"));

        StringAssert.Contains(service, "ReleaseReadinessGateEvaluator");
        StringAssert.Contains(service, "IReleaseReadinessDecisionRecordStore");
        StringAssert.Contains(service, "SaveAsync");
        StringAssert.Contains(service, "GetAsync");

        foreach (var marker in new[]
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
            "ReleaseExecutionService",
            "DeployRelease",
            "MergeRelease",
            "ExecuteRelease",
            "ControlledSourceApplyExecutor",
            "ControlledRollbackExecutor",
            "GovernedWorkflowContinuationService",
            "Process.Start",
            "ProcessStartInfo",
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
            Assert.IsFalse(service.Contains(marker, StringComparison.Ordinal), $"Forbidden marker in service: {marker}");
        }
    }

    private static GovernedReleaseGateRequest ValidRequest(string suffix) => new()
    {
        GovernedReleaseGateRequestId = DeterministicGuid($"governed-release-gate-{suffix}"),
        ProjectId = ProjectId,
        ReleaseReadinessReport = CompleteReport(suffix),
        RequestedBy = $"human-reviewer-{suffix}",
        RequestedAtUtc = RequestedAt,
        EvidenceReferences = [$"release-readiness-report:{suffix}", $"human-review:{suffix}"],
        BoundaryMaxims = ["Governed release gate is not release approval.", "Human review remains required."]
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

    private static void AssertAllAuthorityFalse(GovernedReleaseGateResult result)
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

    private static void AssertAllAuthorityFalse(ReleaseReadinessDecisionRecord decision)
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

    private static readonly Guid ProjectId = Guid.Parse("300c3828-bef1-44a0-8dd8-5ec55f742211");

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
