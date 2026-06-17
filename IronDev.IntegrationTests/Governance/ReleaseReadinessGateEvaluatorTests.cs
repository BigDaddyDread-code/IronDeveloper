using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("ReleaseReadinessGateEvaluator")]
[TestCategory("PR219")]
public sealed class ReleaseReadinessGateEvaluatorTests
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 6, 17, 18, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_CompleteReportProducesReadyEvidenceSatisfiedDecision()
    {
        var report = CompleteReport();
        var decision = new ReleaseReadinessGateEvaluator().Evaluate(Request(report));

        AssertReady(decision);
        AssertValid(decision);
        Assert.IsTrue(decision.Reasons.Any(reason => reason.Code == "ReportComplete"));
        Assert.AreEqual(report.ReleaseReadinessReportId, decision.ReleaseReadinessReportId);
        Assert.AreEqual(DecisionHash(report.ReleaseReadinessReportHash), decision.ReleaseReadinessReportHash);
    }

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_CompleteRollbackRecoveryReportProducesReadyEvidenceSatisfiedDecision()
    {
        var report = CompleteReport(rollbackRecovery: true);
        var decision = new ReleaseReadinessGateEvaluator().Evaluate(Request(report));

        AssertReady(decision);
        AssertValid(decision);
        Assert.IsTrue(decision.Reasons.Any(reason => reason.Code == "RollbackRecoveryEvidenceSatisfied"));
        AssertAllAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_NullRequestProducesBlockedByMissingEvidence()
    {
        var decision = new ReleaseReadinessGateEvaluator().Evaluate(null);

        AssertBlockedMissing(decision, "ReportBlockedByMissingEvidence");
        AssertValid(decision);
    }

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_MissingReportProducesBlockedByMissingEvidence()
    {
        var decision = new ReleaseReadinessGateEvaluator().Evaluate(Request(null!));

        AssertBlockedMissing(decision, "ReportRequired");
        AssertValid(decision);
    }

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_ReportBlockedByMissingEvidenceProducesBlockedDecision()
    {
        var report = Rehash(CompleteReport() with
        {
            Status = ReleaseReadinessReportStatuses.BlockedByMissingEvidence,
            Findings = [Finding("EvidenceMissing", ReleaseReadinessFindingSeverities.Blocking)]
        });

        var decision = new ReleaseReadinessGateEvaluator().Evaluate(Request(report));

        AssertBlockedMissing(decision, "ReportBlockedByMissingEvidence");
        AssertValid(decision);
    }

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_ReportBlockedByFailedEvidenceProducesBlockedDecision()
    {
        var report = Rehash(CompleteReport() with
        {
            Status = ReleaseReadinessReportStatuses.BlockedByFailedEvidence,
            Findings = [Finding("EvidenceFailed", ReleaseReadinessFindingSeverities.Blocking)]
        });

        var decision = new ReleaseReadinessGateEvaluator().Evaluate(Request(report));

        AssertBlockedFailed(decision, "ReportBlockedByFailedEvidence");
        AssertValid(decision);
    }

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_ReportHashMismatchProducesBlockedDecision()
    {
        var report = CompleteReport() with { ReleaseReadinessReportHash = H("different-report") };

        var decision = new ReleaseReadinessGateEvaluator().Evaluate(Request(report));

        AssertBlockedFailed(decision, "ReportHashMismatch");
        AssertValid(decision);
    }

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_ReportValidationIssueProducesBlockedDecision()
    {
        var report = Rehash(CompleteReport() with { HumanReviewRequiredForReleaseApproval = false });

        var decision = new ReleaseReadinessGateEvaluator().Evaluate(Request(report));

        AssertBlockedFailed(decision, "ReportValidation.HumanReviewForReleaseApprovalRequired");
        AssertValid(decision);
    }

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_ReportBlockingFindingProducesBlockedDecision()
    {
        var report = Rehash(CompleteReport() with { Findings = [Finding("BlockingFinding", ReleaseReadinessFindingSeverities.Blocking)] });

        var decision = new ReleaseReadinessGateEvaluator().Evaluate(Request(report));

        AssertBlockedFailed(decision, "ReportHasBlockingFindings");
        AssertValid(decision);
    }

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_FailedSourceApplyWithoutRollbackProducesBlockedDecision()
    {
        var report = Rehash(CompleteReport() with { SourceApplySucceeded = false, SourceApplyPartial = false });

        var decision = new ReleaseReadinessGateEvaluator().Evaluate(Request(report));

        AssertBlockedFailed(decision, "FailedSourceApplyWithoutRollbackRecovery");
        AssertValid(decision);
    }

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_PartialSourceApplyWithoutRollbackProducesBlockedDecision()
    {
        var report = Rehash(CompleteReport() with { SourceApplySucceeded = false, SourceApplyPartial = true });

        var decision = new ReleaseReadinessGateEvaluator().Evaluate(Request(report));

        AssertBlockedFailed(decision, "PartialSourceApplyWithoutRollbackRecovery");
        AssertValid(decision);
    }

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_InconsistentRollbackRecoveryProducesBlockedDecision()
    {
        var report = Rehash(CompleteReport() with
        {
            SourceApplySucceeded = false,
            SourceApplyPartial = true,
            RollbackWasExecuted = true,
            RollbackSucceeded = true,
            RollbackPartial = false,
            RollbackAuditConsistent = false
        });

        var decision = new ReleaseReadinessGateEvaluator().Evaluate(Request(report));

        AssertBlockedFailed(decision, "RollbackRecoveryEvidenceFailed");
        AssertValid(decision);
    }

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_UnsatisfiedWorkflowContinuationProducesBlockedDecision()
    {
        var report = Rehash(CompleteReport() with { WorkflowContinuationSucceeded = false });

        var decision = new ReleaseReadinessGateEvaluator().Evaluate(Request(report));

        AssertBlockedFailed(decision, "WorkflowContinuationEvidenceUnsatisfied");
        AssertValid(decision);
    }

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_InvalidWorkflowTransitionRecordProducesBlockedDecision()
    {
        var report = Rehash(CompleteReport() with { WorkflowTransitionRecordValid = false });

        var decision = new ReleaseReadinessGateEvaluator().Evaluate(Request(report));

        AssertBlockedFailed(decision, "WorkflowTransitionEvidenceInvalid");
        AssertValid(decision);
    }

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_ReportReleaseReadyTrueIsRejected()
        => AssertAuthorityBlocked(Rehash(CompleteReport() with { ReleaseReady = true }), "ReportClaimsReleaseAuthority");

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_ReportReleaseApprovedTrueIsRejected()
        => AssertAuthorityBlocked(Rehash(CompleteReport() with { ReleaseApproved = true }), "ReportClaimsReleaseAuthority");

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_ReportDeploymentApprovedTrueIsRejected()
        => AssertAuthorityBlocked(Rehash(CompleteReport() with { DeploymentApproved = true }), "ReportClaimsReleaseAuthority");

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_ReportMergeApprovedTrueIsRejected()
        => AssertAuthorityBlocked(Rehash(CompleteReport() with { MergeApproved = true }), "ReportClaimsReleaseAuthority");

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_ReportSourceApplyExecutedByReportTrueIsRejected()
        => AssertAuthorityBlocked(Rehash(CompleteReport() with { SourceApplyExecutedByReport = true }), "ReportClaimsExecutionAuthority");

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_ReportRollbackExecutedByReportTrueIsRejected()
        => AssertAuthorityBlocked(Rehash(CompleteReport() with { RollbackExecutedByReport = true }), "ReportClaimsExecutionAuthority");

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_ReportWorkflowMutatedByReportTrueIsRejected()
        => AssertAuthorityBlocked(Rehash(CompleteReport() with { WorkflowMutatedByReport = true }), "ReportClaimsExecutionAuthority");

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_ReportGitOperationExecutedByReportTrueIsRejected()
        => AssertAuthorityBlocked(Rehash(CompleteReport() with { GitOperationExecutedByReport = true }), "ReportClaimsExecutionAuthority");

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_PrivateRawMaterialProducesBlockedDecision()
    {
        var report = Rehash(CompleteReport() with { EvidenceReferences = ["raw prompt leaked"] });

        var decision = new ReleaseReadinessGateEvaluator().Evaluate(Request(report));

        AssertBlockedFailed(decision, "PrivateRawMaterialRejected");
        Assert.DoesNotContain("raw prompt", Serialized(decision), StringComparison.OrdinalIgnoreCase);
        AssertValid(decision);
    }

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_AuthorityClaimProducesBlockedDecision()
    {
        var report = Rehash(CompleteReport() with { BoundaryMaxims = ["release approved"] });

        var decision = new ReleaseReadinessGateEvaluator().Evaluate(Request(report));

        AssertBlockedFailed(decision, "AuthorityClaimRejected");
        Assert.DoesNotContain("release approved", Serialized(decision), StringComparison.OrdinalIgnoreCase);
        AssertValid(decision);
    }

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_AllowsNegativeBoundaryWording()
    {
        var report = Rehash(CompleteReport() with
        {
            BoundaryMaxims = ["not release approved", "does not execute release", "not deployment approved", "not merge approved"]
        });

        var decision = new ReleaseReadinessGateEvaluator().Evaluate(Request(report));

        AssertReady(decision);
        AssertValid(decision);
    }

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_DecisionHashIsDeterministic()
    {
        var request = Request(CompleteReport());
        var first = new ReleaseReadinessGateEvaluator().Evaluate(request);
        var second = new ReleaseReadinessGateEvaluator().Evaluate(request);

        Assert.AreEqual(first.ReleaseReadinessDecisionRecordHash, second.ReleaseReadinessDecisionRecordHash);
    }

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_DecisionHashChangesWhenStatusChanges()
    {
        var request = Request(CompleteReport());
        var ready = new ReleaseReadinessGateEvaluator().Evaluate(request);
        var blocked = new ReleaseReadinessGateEvaluator().Evaluate(request with
        {
            ReleaseReadinessReport = Rehash(request.ReleaseReadinessReport with { WorkflowTransitionRecordValid = false })
        });

        Assert.AreNotEqual(ready.ReleaseReadinessDecisionRecordHash, blocked.ReleaseReadinessDecisionRecordHash);
    }

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_DecisionRecordBindsToReport()
    {
        var report = CompleteReport();
        var request = Request(report);
        var decision = new ReleaseReadinessGateEvaluator().Evaluate(request);

        Assert.AreEqual(request.ProjectId, decision.ProjectId);
        Assert.AreEqual(report.ReleaseReadinessReportId, decision.ReleaseReadinessReportId);
        Assert.AreEqual(DecisionHash(report.ReleaseReadinessReportHash), decision.ReleaseReadinessReportHash);
        Assert.AreEqual(report.WorkflowRunId, decision.WorkflowRunId);
        Assert.AreEqual(report.WorkflowStepId, decision.WorkflowStepId);
        Assert.AreEqual(report.SubjectKind, decision.SubjectKind);
        Assert.AreEqual(report.SubjectId, decision.SubjectId);
        Assert.AreEqual(DecisionHash(report.SubjectHash), decision.SubjectHash);
        Assert.IsTrue(decision.EvidenceReferences.Any(reference => reference.Contains(report.ReleaseReadinessReportId.ToString("D"), StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_StaticProductionFileHasNoForbiddenRuntimeSurface()
    {
        var source = ReadRepositoryFile("IronDev.Core", "Governance", "ReleaseReadinessGateEvaluator.cs");
        var forbidden = new[]
        {
            "ReleaseApproved = true",
            "DeploymentApproved = true",
            "MergeApproved = true",
            "SourceApplyExecutedByDecision = true",
            "RollbackExecutedByDecision = true",
            "WorkflowMutatedByDecision = true",
            "GitOperationExecutedByDecision = true",
            "ReleaseExecutedByDecision = true",
            "HttpPost",
            "HttpPut",
            "HttpPatch",
            "HttpDelete",
            "ControllerBase",
            "SaveAsync",
            "ExecuteAsync",
            "Process.Start",
            "ProcessStartInfo",
            "git commit",
            "git push",
            "git merge",
            "gh pr",
            "ControlledSourceApplyExecutor",
            "ControlledRollbackExecutor",
            "GovernedWorkflowContinuationService",
            "ReleaseExecutionService",
            "DeploymentExecutionService",
            "MergeExecutionService",
            "AgentDispatch",
            "ModelProvider",
            "ToolInvoker",
            "PromoteMemory",
            "ActivateRetrieval",
            "Weaviate",
            "Embedding"
        };

        foreach (var marker in forbidden)
            Assert.IsFalse(source.Contains(marker, StringComparison.Ordinal), $"Forbidden production marker found: {marker}");
    }

    [TestMethod]
    public void ReleaseReadinessGateEvaluator_ReceiptStatesBoundary()
    {
        var receipt = ReadRepositoryFile("Docs", "receipts", "PR219_RELEASE_READINESS_GATE_EVALUATOR.md");

        StringAssert.Contains(receipt, "PR219 adds release-readiness gate evaluator logic only.");
        StringAssert.Contains(receipt, "PR219 may decide whether release-readiness evidence is satisfied.");
        StringAssert.Contains(receipt, "PR219 does not approve release.");
        StringAssert.Contains(receipt, "ReadyEvidenceSatisfied means evidence satisfied only.");
        StringAssert.Contains(receipt, "ReadyEvidenceSatisfied does not mean release approved.");
        StringAssert.Contains(receipt, "Human review remains required for release approval, deployment, and merge.");
        StringAssert.Contains(receipt, "PR219 checks the release-readiness evidence. It does not approve release.");
    }

    private static void AssertAuthorityBlocked(ReleaseReadinessReport report, string expectedCode)
    {
        var decision = new ReleaseReadinessGateEvaluator().Evaluate(Request(report));

        AssertBlockedFailed(decision, expectedCode);
        AssertAllAuthorityFlagsFalse(decision);
        AssertValid(decision);
    }

    private static void AssertReady(ReleaseReadinessDecisionRecord decision)
    {
        Assert.AreEqual(ReleaseReadinessDecisionStatuses.ReadyEvidenceSatisfied, decision.DecisionStatus);
        Assert.IsTrue(decision.ReleaseReadinessEvidenceSatisfied);
        AssertAllAuthorityFlagsFalse(decision);
        Assert.IsFalse(decision.Reasons.Any(reason => reason.Severity == ReleaseReadinessDecisionReasonSeverities.Blocking));
    }

    private static void AssertBlockedMissing(ReleaseReadinessDecisionRecord decision, string expectedReason)
    {
        Assert.AreEqual(ReleaseReadinessDecisionStatuses.BlockedByMissingEvidence, decision.DecisionStatus);
        Assert.IsFalse(decision.ReleaseReadinessEvidenceSatisfied);
        AssertHasReason(decision, expectedReason);
        AssertAllAuthorityFlagsFalse(decision);
    }

    private static void AssertBlockedFailed(ReleaseReadinessDecisionRecord decision, string expectedReason)
    {
        Assert.AreEqual(ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence, decision.DecisionStatus);
        Assert.IsFalse(decision.ReleaseReadinessEvidenceSatisfied);
        AssertHasReason(decision, expectedReason);
        AssertAllAuthorityFlagsFalse(decision);
    }

    private static void AssertHasReason(ReleaseReadinessDecisionRecord decision, string expectedCode) =>
        Assert.IsTrue(decision.Reasons.Any(reason => reason.Code.Contains(expectedCode, StringComparison.Ordinal)), string.Join("; ", decision.Reasons.Select(reason => reason.Code)));

    private static void AssertAllAuthorityFlagsFalse(ReleaseReadinessDecisionRecord decision)
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

    private static void AssertValid(ReleaseReadinessDecisionRecord decision)
    {
        var result = ReleaseReadinessDecisionRecordValidation.Validate(decision);
        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues.Select(issue => $"{issue.Code}:{issue.Field}:{issue.Message}")));
    }

    private static ReleaseReadinessGateRequest Request(ReleaseReadinessReport report) => new()
    {
        ReleaseReadinessGateRequestId = Guid.Parse("f8f29b51-30d6-4a6f-bfb7-236969a21901"),
        ProjectId = report?.ProjectId ?? Guid.Parse("b04c77d8-5caf-4ad8-9731-8406cbda2101"),
        ReleaseReadinessReport = report,
        RequestedAtUtc = RequestedAt,
        EvidenceReferences = ["release-readiness-gate:request-evidence"],
        BoundaryMaxims = ["Release readiness gate evaluator is not release approval."]
    };

    private static ReleaseReadinessReport CompleteReport(bool rollbackRecovery = false)
    {
        var report = new ReleaseReadinessReport
        {
            ReleaseReadinessReportId = Guid.Parse("ca98f9f1-a28f-47c7-bd33-a6139afc2101"),
            ProjectId = Guid.Parse("b04c77d8-5caf-4ad8-9731-8406cbda2101"),
            ReleaseReadinessReportRequestId = Guid.Parse("d216e7a2-961e-441d-af4d-6976ab002101"),
            Status = ReleaseReadinessReportStatuses.Complete,
            WorkflowRunId = "workflow-run-pr219",
            WorkflowStepId = "workflow-step-pr219",
            SubjectKind = "ReleasePackage",
            SubjectId = "release-package-pr219",
            SubjectHash = H("release-package-pr219"),
            AcceptedApprovalId = Guid.Parse("bb91619d-7e27-4701-8012-a2960e062101"),
            AcceptedApprovalHash = H("accepted-approval-pr219"),
            PolicySatisfactionId = Guid.Parse("3f1c72f4-b65e-428a-b640-22aa6f662101"),
            PolicySatisfactionHash = H("policy-satisfaction-pr219"),
            SourceApplyRequestId = Guid.Parse("a4631216-6382-4f9d-9237-a5f6d02c2101"),
            SourceApplyRequestHash = H("source-apply-request-pr219"),
            SourceApplyReceiptId = Guid.Parse("e2d32a3a-8841-48ff-92b4-6d1d6c0d2101"),
            SourceApplyReceiptHash = H("source-apply-receipt-pr219"),
            RollbackExecutionReceiptId = rollbackRecovery ? Guid.Parse("6117fef3-b507-4b16-aad0-58e9364b2101") : null,
            RollbackExecutionReceiptHash = rollbackRecovery ? H("rollback-execution-receipt-pr219") : null,
            RollbackExecutionAuditReportId = rollbackRecovery ? Guid.Parse("a473e450-578a-4c1b-b622-cb9d3aaa2101") : null,
            RollbackExecutionAuditReportHash = rollbackRecovery ? H("rollback-audit-pr219") : null,
            WorkflowContinuationGateEvaluationId = Guid.Parse("ea084623-bd04-4e0c-a3ad-f7e34cb92101"),
            WorkflowContinuationGateEvaluationHash = H("workflow-continuation-gate-pr219"),
            WorkflowTransitionRecordId = Guid.Parse("f6cce507-f662-428e-8ce8-a7300a4a2101"),
            WorkflowTransitionRecordHash = H("workflow-transition-record-pr219"),
            ApprovalEvidencePresent = true,
            PolicyEvidencePresent = true,
            SourceApplySucceeded = !rollbackRecovery,
            SourceApplyPartial = rollbackRecovery,
            RollbackWasExecuted = rollbackRecovery,
            RollbackSucceeded = rollbackRecovery,
            RollbackPartial = false,
            RollbackAuditConsistent = rollbackRecovery,
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
            EvidenceReferences = ["release-readiness-report:evidence", "workflow-transition-record:evidence"],
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

    private static string DecisionHash(string prefixedHash) =>
        prefixedHash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? prefixedHash[7..] : prefixedHash;

    private static string H(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static string Serialized(ReleaseReadinessDecisionRecord decision) =>
        JsonSerializer.Serialize(decision);

    private static string ReadRepositoryFile(params string[] path)
    {
        var root = RepositoryRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(path).ToArray()));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
