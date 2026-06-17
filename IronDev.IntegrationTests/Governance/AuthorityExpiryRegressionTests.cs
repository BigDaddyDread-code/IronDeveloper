using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("AuthorityExpiryRegression")]
[TestCategory("PR225")]
public sealed class AuthorityExpiryRegressionTests
{
    private static readonly DateTimeOffset EvaluatedAtUtc = new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AuthorityExpiryRegression_ExpiredAcceptedApprovalIsBlocking() =>
        AssertExpiredEvidenceKind(AuthorityEvidenceKinds.AcceptedApproval, "accepted-approval");

    [TestMethod]
    public void AuthorityExpiryRegression_ExpiredPolicySatisfactionIsBlocking() =>
        AssertExpiredEvidenceKind(AuthorityEvidenceKinds.PolicySatisfaction, "policy-satisfaction");

    [TestMethod]
    public void AuthorityExpiryRegression_ExpiredSourceApplyRequestIsBlocking() =>
        AssertExpiredEvidenceKind(AuthorityEvidenceKinds.SourceApplyRequest, "source-apply-request");

    [TestMethod]
    public void AuthorityExpiryRegression_ExpiredSourceApplyReceiptIsBlocking() =>
        AssertExpiredEvidenceKind(AuthorityEvidenceKinds.SourceApplyReceipt, "source-apply-receipt");

    [TestMethod]
    public void AuthorityExpiryRegression_ExpiredRollbackExecutionReceiptIsBlocking() =>
        AssertExpiredEvidenceKind(AuthorityEvidenceKinds.RollbackExecutionReceipt, "rollback-execution-receipt");

    [TestMethod]
    public void AuthorityExpiryRegression_ExpiredRollbackExecutionAuditIsBlocking() =>
        AssertExpiredEvidenceKind(AuthorityEvidenceKinds.RollbackExecutionAudit, "rollback-execution-audit");

    [TestMethod]
    public void AuthorityExpiryRegression_ExpiredWorkflowContinuationGateIsBlocking() =>
        AssertExpiredEvidenceKind(AuthorityEvidenceKinds.WorkflowContinuationGate, "workflow-continuation-gate");

    [TestMethod]
    public void AuthorityExpiryRegression_ExpiredWorkflowTransitionRecordIsBlocking() =>
        AssertExpiredEvidenceKind(AuthorityEvidenceKinds.WorkflowTransitionRecord, "workflow-transition-record");

    [TestMethod]
    public void AuthorityExpiryRegression_ExpiredReleaseReadinessReportIsBlocking() =>
        AssertExpiredEvidenceKind(AuthorityEvidenceKinds.ReleaseReadinessReport, "release-readiness-report");

    [TestMethod]
    public void AuthorityExpiryRegression_ExpiredReleaseReadinessDecisionRecordIsBlocking() =>
        AssertExpiredEvidenceKind(AuthorityEvidenceKinds.ReleaseReadinessDecisionRecord, "release-readiness-decision");

    [TestMethod]
    public void AuthorityExpiryRegression_ExpiredGovernedReleaseGateResultIsBlocking() =>
        AssertExpiredEvidenceKind(AuthorityEvidenceKinds.GovernedReleaseGateResult, "governed-release-gate-result");

    [TestMethod]
    public void AuthorityExpiryRegression_ExpiryEqualToEvaluationTimeIsExpired()
    {
        var result = Detect(Request(Evidence(AuthorityEvidenceKinds.AcceptedApproval, "equal-time", EvaluatedAtUtc)));

        AssertExpired(result);
    }

    [TestMethod]
    public void AuthorityExpiryRegression_FutureExpiryIsCurrent()
    {
        var result = Detect(Request(Evidence(AuthorityEvidenceKinds.AcceptedApproval, "future-expiry", EvaluatedAtUtc.AddTicks(1))));

        Assert.IsTrue(result.IsCurrent, string.Join("; ", result.Findings.Select(finding => finding.Code)));
        Assert.IsFalse(result.HasExpiredEvidence);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void AuthorityExpiryRegression_NullExpiryIsCurrent()
    {
        var result = Detect(Request(Evidence(AuthorityEvidenceKinds.AcceptedApproval, "null-expiry", null)));

        Assert.IsTrue(result.IsCurrent, string.Join("; ", result.Findings.Select(finding => finding.Code)));
        Assert.IsFalse(result.HasExpiredEvidence);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void AuthorityExpiryRegression_FutureCreatedEvidenceIsRejectedEvenWhenExpiryIsFuture()
    {
        var result = Detect(Request(Evidence(AuthorityEvidenceKinds.PolicySatisfaction, "future-created", EvaluatedAtUtc.AddHours(1)) with
        {
            CreatedAtUtc = EvaluatedAtUtc.AddTicks(1)
        }));

        AssertBlocking(result, "EvidenceCreatedInFuture");
        Assert.IsFalse(result.IsCurrent);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void AuthorityExpiryRegression_ExpiredApprovalBlocksReleaseReadinessChain()
    {
        var result = Detect(Request(
            Evidence(AuthorityEvidenceKinds.AcceptedApproval, "expired-approval", EvaluatedAtUtc.AddSeconds(-1)),
            Evidence(AuthorityEvidenceKinds.PolicySatisfaction, "current-policy", EvaluatedAtUtc.AddHours(1)),
            Evidence(AuthorityEvidenceKinds.ReleaseReadinessReport, "current-report", EvaluatedAtUtc.AddHours(1))));

        AssertExpired(result);
        Assert.IsFalse(result.ReleaseApproved);
    }

    [TestMethod]
    public void AuthorityExpiryRegression_ExpiredPolicyBlocksReleaseReadinessChain()
    {
        var result = Detect(Request(
            Evidence(AuthorityEvidenceKinds.AcceptedApproval, "current-approval", EvaluatedAtUtc.AddHours(1)),
            Evidence(AuthorityEvidenceKinds.PolicySatisfaction, "expired-policy", EvaluatedAtUtc.AddSeconds(-1)),
            Evidence(AuthorityEvidenceKinds.ReleaseReadinessDecisionRecord, "current-decision", EvaluatedAtUtc.AddHours(1))));

        AssertExpired(result);
        Assert.IsFalse(result.ReleaseApproved);
    }

    [TestMethod]
    public void AuthorityExpiryRegression_ExpiredSourceApplyReceiptBlocksReleaseReadinessChain()
    {
        var result = Detect(Request(
            Evidence(AuthorityEvidenceKinds.SourceApplyReceipt, "expired-source-apply", EvaluatedAtUtc.AddSeconds(-1)),
            Evidence(AuthorityEvidenceKinds.ReleaseReadinessReport, "current-report", EvaluatedAtUtc.AddHours(1))));

        AssertExpired(result);
        Assert.IsFalse(result.SourceApplyExecuted);
    }

    [TestMethod]
    public void AuthorityExpiryRegression_ExpiredWorkflowTransitionBlocksReleaseReadinessChain()
    {
        var result = Detect(Request(
            Evidence(AuthorityEvidenceKinds.WorkflowTransitionRecord, "expired-transition", EvaluatedAtUtc.AddSeconds(-1)),
            Evidence(AuthorityEvidenceKinds.ReleaseReadinessDecisionRecord, "current-decision", EvaluatedAtUtc.AddHours(1))));

        AssertExpired(result);
        Assert.IsFalse(result.WorkflowContinued);
        Assert.IsFalse(result.WorkflowMutated);
    }

    [TestMethod]
    public void AuthorityExpiryRegression_ExpiredReleaseDecisionDoesNotBecomeReleaseApproval()
    {
        var result = Detect(Request(Evidence(AuthorityEvidenceKinds.ReleaseReadinessDecisionRecord, "expired-release-decision", EvaluatedAtUtc.AddSeconds(-1))));

        AssertExpired(result);
        Assert.IsFalse(result.ReleaseApproved);
        Assert.IsFalse(result.DeploymentApproved);
        Assert.IsFalse(result.MergeApproved);
    }

    [TestMethod]
    public void AuthorityExpiryRegression_OneExpiredEvidenceMakesWholeDetectionStale()
    {
        var result = Detect(Request(
            Evidence(AuthorityEvidenceKinds.AcceptedApproval, "current-approval", EvaluatedAtUtc.AddHours(1)),
            Evidence(AuthorityEvidenceKinds.PolicySatisfaction, "expired-policy", EvaluatedAtUtc.AddSeconds(-1))));

        AssertExpired(result);
        Assert.IsFalse(result.IsCurrent);
    }

    [TestMethod]
    public void AuthorityExpiryRegression_MixedCurrentAndExpiredEvidenceReportsExpiredFinding()
    {
        var result = Detect(Request(
            Evidence(AuthorityEvidenceKinds.SourceApplyRequest, "current-source-request", EvaluatedAtUtc.AddHours(1)),
            Evidence(AuthorityEvidenceKinds.RollbackExecutionReceipt, "expired-rollback-receipt", EvaluatedAtUtc.AddSeconds(-1)),
            Evidence(AuthorityEvidenceKinds.GovernedReleaseGateResult, "current-release-gate", null)));

        AssertExpired(result);
        Assert.AreEqual(1, result.Findings.Count(finding => finding.Code == "EvidenceExpired"));
    }

    [TestMethod]
    public void AuthorityExpiryRegression_AllCurrentEvidenceStaysCurrent()
    {
        var result = Detect(Request(
            Evidence(AuthorityEvidenceKinds.AcceptedApproval, "current-approval", EvaluatedAtUtc.AddHours(1)),
            Evidence(AuthorityEvidenceKinds.PolicySatisfaction, "current-policy", null),
            Evidence(AuthorityEvidenceKinds.WorkflowContinuationGate, "current-continuation", EvaluatedAtUtc.AddTicks(1))));

        Assert.IsTrue(result.IsCurrent, string.Join("; ", result.Findings.Select(finding => finding.Code)));
        Assert.IsFalse(result.HasExpiredEvidence);
        Assert.AreEqual(0, result.Findings.Count);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void AuthorityExpiryRegression_ExpiredEvidenceDoesNotRefreshAuthorityOrReissueEvidence()
    {
        var result = Detect(Request(Evidence(AuthorityEvidenceKinds.GovernedReleaseGateResult, "expired-gate", EvaluatedAtUtc.AddSeconds(-1))));

        AssertExpired(result);
        Assert.IsFalse(result.AuthorityRefreshed);
        Assert.IsFalse(result.EvidenceReissued);
    }

    [TestMethod]
    public void AuthorityExpiryRegression_ExpiredEvidenceDoesNotExecuteOrMutateAnything()
    {
        var result = Detect(Request(Evidence(AuthorityEvidenceKinds.SourceApplyReceipt, "expired-mutation-evidence", EvaluatedAtUtc.AddSeconds(-1))));

        AssertExpired(result);
        Assert.IsFalse(result.ReleaseExecuted);
        Assert.IsFalse(result.SourceApplyExecuted);
        Assert.IsFalse(result.RollbackExecuted);
        Assert.IsFalse(result.WorkflowContinued);
        Assert.IsFalse(result.WorkflowMutated);
        Assert.IsFalse(result.GitOperationExecuted);
    }

    [TestMethod]
    public void AuthorityExpiryRegression_ExpiredUnsafeEvidenceDoesNotEchoRawPrivateMaterial()
    {
        var result = Detect(Request(Evidence(AuthorityEvidenceKinds.AcceptedApproval, "unsafe-expired", EvaluatedAtUtc.AddSeconds(-1)) with
        {
            EvidenceId = "raw prompt leaked in expired approval",
            EvidenceReferences = ["chain of thought should not surface"]
        }));
        var serialized = JsonSerializer.Serialize(result);

        AssertExpired(result);
        AssertBlocking(result, "PrivateRawMaterialRejected");
        Assert.IsTrue(result.HasUnsafeMaterial);
        Assert.IsFalse(serialized.Contains("raw prompt leaked", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains("chain of thought should not surface", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void AuthorityExpiryRegression_ExpiredAuthorityClaimDoesNotBecomeApproval()
    {
        var result = Detect(Request(Evidence(AuthorityEvidenceKinds.ReleaseReadinessDecisionRecord, "expired-authority-claim", EvaluatedAtUtc.AddSeconds(-1)) with
        {
            EvidenceReferences = ["release approved"]
        }));

        AssertExpired(result);
        AssertBlocking(result, "AuthorityClaimRejected");
        Assert.IsFalse(result.ReleaseApproved);
        Assert.IsFalse(result.DeploymentApproved);
        Assert.IsFalse(result.MergeApproved);
    }

    [TestMethod]
    public void AuthorityExpiryRegression_StaticDetectorDoesNotGainRenewalExecutionOrRuntimePaths()
    {
        var root = RepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "StaleAuthorityDetection.cs"));

        StringAssert.Contains(source, "StaleAuthorityDetector");
        StringAssert.Contains(source, "EvidenceExpired");

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
            "AuthorityRefreshed = true",
            "EvidenceReissued = true",
            "RenewAuthority",
            "RefreshAuthority",
            "ReissueEvidence",
            "ExtendExpiry",
            "ApproveRelease",
            "DeployRelease",
            "MergeRelease",
            "ExecuteRelease",
            "ControlledSourceApplyExecutor",
            "ControlledRollbackExecutor",
            "GovernedWorkflowContinuationService",
            "GovernedReleaseGateService",
            "SqlConnection",
            "IDbConnection",
            "Dapper",
            "HttpClient",
            "ControllerBase",
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
            "Embedding"
        })
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Forbidden marker in stale authority detector: {marker}");
        }
    }

    private static void AssertExpiredEvidenceKind(string evidenceKind, string suffix)
    {
        var result = Detect(Request(Evidence(evidenceKind, suffix, EvaluatedAtUtc.AddSeconds(-1))));

        AssertExpired(result);
        Assert.AreEqual(evidenceKind, result.Findings.Single(finding => finding.Code == "EvidenceExpired").EvidenceKind);
    }

    private static StaleAuthorityDetectionResult Detect(StaleAuthorityDetectionRequest request) =>
        new StaleAuthorityDetector().Detect(request);

    private static StaleAuthorityDetectionRequest Request(params AuthorityEvidenceSnapshot[] evidence) => new()
    {
        StaleAuthorityDetectionRequestId = DeterministicGuid($"authority-expiry-regression-{string.Join("-", evidence.Select(item => item.EvidenceId))}"),
        ProjectId = ProjectId,
        SubjectKind = SubjectKind,
        SubjectId = SubjectId,
        CurrentSubjectHash = SubjectHash,
        WorkflowRunId = WorkflowRunId,
        WorkflowStepId = WorkflowStepId,
        EvaluatedAtUtc = EvaluatedAtUtc,
        Evidence = evidence,
        EvidenceReferences = ["authority-expiry-regression:evidence", "human-review:required"],
        BoundaryMaxims =
        [
            "Expired evidence is not approval.",
            "Expired evidence is not execution permission.",
            "Expired evidence is not authority renewal.",
            "Human review remains required."
        ]
    };

    private static AuthorityEvidenceSnapshot Evidence(string kind, string suffix, DateTimeOffset? expiresAtUtc) => new()
    {
        EvidenceKind = kind,
        EvidenceId = $"{kind}:{suffix}",
        EvidenceHash = HRaw($"evidence-{kind}-{suffix}"),
        SubjectKind = SubjectKind,
        SubjectId = SubjectId,
        SubjectHash = SubjectHash,
        WorkflowRunId = WorkflowRunId,
        WorkflowStepId = WorkflowStepId,
        CreatedAtUtc = EvaluatedAtUtc.AddMinutes(-10),
        ExpiresAtUtc = expiresAtUtc,
        PolicyVersion = "policy-v1",
        ApprovalVersion = "approval-v1",
        SourceBaselineHash = HRaw($"source-baseline-{suffix}"),
        Superseded = false,
        SupersededByEvidenceId = null,
        SupersededByEvidenceHash = null,
        EvidenceReferences = [$"{kind}:{suffix}", $"trace:{suffix}"]
    };

    private static void AssertExpired(StaleAuthorityDetectionResult result)
    {
        AssertBlocking(result, "EvidenceExpired");
        Assert.IsTrue(result.HasExpiredEvidence);
        Assert.IsTrue(result.HasStaleAuthority);
        Assert.IsFalse(result.IsCurrent);
    }

    private static void AssertBlocking(StaleAuthorityDetectionResult result, string code)
    {
        Assert.IsTrue(result.Findings.Any(finding => finding.Code == code && finding.Severity == StaleAuthorityFindingSeverities.Blocking), string.Join("; ", result.Findings.Select(finding => finding.Code)));
        AssertNoAuthority(result);
    }

    private static void AssertNoAuthority(StaleAuthorityDetectionResult result)
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

    private static readonly Guid ProjectId = Guid.Parse("5890da28-7df0-4c18-a991-631d8c319225");
    private const string SubjectKind = "ReleasePackage";
    private const string SubjectId = "release-package-pr225";
    private static readonly string SubjectHash = HRaw("subject-current-pr225");
    private const string WorkflowRunId = "workflow-run-pr225";
    private const string WorkflowStepId = "workflow-step-pr225";

    private static Guid DeterministicGuid(string value) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes(value)));

    private static string HRaw(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
