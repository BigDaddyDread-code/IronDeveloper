using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("FailedApplyRecoveryCampaign")]
[TestCategory("PR226")]
public sealed class FailedApplyRecoveryCampaignTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 18, 13, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void FailedApplyRecoveryCampaign_NullRequestIsRejected()
    {
        var result = Run(null);

        AssertStatus(result, FailedApplyRecoveryCampaignStatuses.Rejected, "RequestRequired");
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_MissingSourceApplyFailureIsRejected()
    {
        var result = Run(ValidRequest() with { SourceApplyFailure = null! });

        AssertStatus(result, FailedApplyRecoveryCampaignStatuses.Rejected, "SourceApplyFailureEvidenceRequired");
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_SourceApplySucceededIsRejectedAsRecoveryCampaign()
    {
        var result = Run(ValidRequest() with
        {
            SourceApplyFailure = ValidSourceApplyFailure() with { SourceApplySucceeded = true, SourceApplyFailed = false, SourceApplyPartial = false }
        });

        AssertStatus(result, FailedApplyRecoveryCampaignStatuses.Rejected, "SourceApplySucceededNotRecovery");
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_SourceApplyFailureWithoutFailureOrPartialIsRejected()
    {
        var result = Run(ValidRequest() with
        {
            SourceApplyFailure = ValidSourceApplyFailure() with { SourceApplyFailed = false, SourceApplyPartial = false }
        });

        AssertStatus(result, FailedApplyRecoveryCampaignStatuses.Rejected, "SourceApplyFailureNotProven");
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_InvalidHashesAreRejected()
    {
        var result = Run(ValidRequest() with
        {
            SourceApplyFailure = ValidSourceApplyFailure() with { SourceApplyRequestHash = "not-a-hash" }
        });

        AssertStatus(result, FailedApplyRecoveryCampaignStatuses.Rejected, "SourceApplyFailureHashInvalid");
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_MissingFailurePathsAreRejected()
    {
        var result = Run(ValidRequest() with
        {
            SourceApplyFailure = ValidSourceApplyFailure() with { FailedPaths = [], AppliedPaths = [] }
        });

        AssertStatus(result, FailedApplyRecoveryCampaignStatuses.Rejected, "SourceApplyFailurePathsMissing");
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_MissingRollbackEvidenceFailsRecovery()
    {
        var result = Run(ValidRequest() with { RollbackRecovery = null, RollbackAudit = null });

        AssertStatus(result, FailedApplyRecoveryCampaignStatuses.RecoveryEvidenceMissing, "RollbackRecoveryEvidenceMissing");
        Assert.IsFalse(result.RollbackEvidencePresent);
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_RollbackNotExecutedFailsRecovery()
    {
        var result = Run(ValidRequest(rollback: ValidRollbackRecovery() with { RollbackExecuted = false }));

        AssertStatus(result, FailedApplyRecoveryCampaignStatuses.RecoveryEvidenceFailed, "RollbackNotExecuted");
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_RollbackFailedFailsRecovery()
    {
        var result = Run(ValidRequest(rollback: ValidRollbackRecovery() with { RollbackSucceeded = false }));

        AssertStatus(result, FailedApplyRecoveryCampaignStatuses.RecoveryEvidenceFailed, "RollbackFailed");
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_RollbackPartialFailsRecovery()
    {
        var result = Run(ValidRequest(rollback: ValidRollbackRecovery() with { RollbackPartial = true }));

        AssertStatus(result, FailedApplyRecoveryCampaignStatuses.RecoveryEvidenceFailed, "RollbackPartial");
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_RollbackFailedPathsFailRecovery()
    {
        var result = Run(ValidRequest(rollback: ValidRollbackRecovery() with { FailedRollbackPaths = ["src/failed.cs"] }));

        AssertStatus(result, FailedApplyRecoveryCampaignStatuses.RecoveryEvidenceFailed, "RollbackFailedPathsPresent");
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_MissingRollbackAuditFailsRecovery()
    {
        var result = Run(ValidRequest() with { RollbackAudit = null });

        AssertStatus(result, FailedApplyRecoveryCampaignStatuses.RecoveryEvidenceMissing, "RollbackAuditEvidenceMissing");
        Assert.IsFalse(result.RollbackAuditPresent);
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_RollbackAuditNotRunFailsRecovery()
    {
        var result = Run(ValidRequest(audit: ValidRollbackAudit() with { AuditRan = false }));

        AssertStatus(result, FailedApplyRecoveryCampaignStatuses.RecoveryEvidenceFailed, "RollbackAuditNotRun");
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_RollbackAuditInconsistentFailsRecovery()
    {
        var result = Run(ValidRequest(audit: ValidRollbackAudit() with { AuditConsistent = false }));

        AssertStatus(result, FailedApplyRecoveryCampaignStatuses.RecoveryEvidenceFailed, "RollbackAuditInconsistent");
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_RollbackAuditBaselineMismatchFailsRecovery()
    {
        var result = Run(ValidRequest(audit: ValidRollbackAudit() with { AuditedSourceBaselineHash = H("other-baseline") }));

        AssertStatus(result, FailedApplyRecoveryCampaignStatuses.RecoveryEvidenceFailed, "RollbackAuditBaselineMismatch");
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_RollbackAuditWorkspaceMismatchFailsRecovery()
    {
        var result = Run(ValidRequest(audit: ValidRollbackAudit() with { AuditedWorkspaceHash = H("other-workspace") }));

        AssertStatus(result, FailedApplyRecoveryCampaignStatuses.RecoveryEvidenceFailed, "RollbackAuditWorkspaceMismatch");
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_CompleteRollbackAndAuditEvidenceSatisfiesRecoveryEvidence()
    {
        var result = Run(ValidRequest());

        Assert.AreEqual(FailedApplyRecoveryCampaignStatuses.RecoveryEvidenceSatisfied, result.Status, string.Join("; ", result.Findings.Select(finding => finding.Code)));
        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.SourceApplyFailureConfirmed);
        Assert.IsTrue(result.RollbackEvidencePresent);
        Assert.IsTrue(result.RollbackSucceeded);
        Assert.IsTrue(result.RollbackAuditPresent);
        Assert.IsTrue(result.RollbackAuditConsistent);
        Assert.IsTrue(result.RecoveryEvidenceSatisfied);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_RecoveryEvidenceSatisfiedDoesNotApproveRelease()
    {
        var result = Run(ValidRequest());

        Assert.IsTrue(result.RecoveryEvidenceSatisfied);
        Assert.IsFalse(result.ReleaseApproved);
        Assert.IsFalse(result.DeploymentApproved);
        Assert.IsFalse(result.MergeApproved);
        Assert.IsTrue(result.HumanReviewRequired);
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_RecoveryEvidenceSatisfiedDoesNotContinueWorkflow()
    {
        var result = Run(ValidRequest());

        Assert.IsTrue(result.RecoveryEvidenceSatisfied);
        Assert.IsFalse(result.WorkflowContinued);
        Assert.IsFalse(result.WorkflowMutated);
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_RecoveryEvidenceSatisfiedDoesNotRetrySourceApply()
    {
        var result = Run(ValidRequest());

        Assert.IsTrue(result.RecoveryEvidenceSatisfied);
        Assert.IsFalse(result.SourceApplyRetried);
        Assert.IsFalse(result.SourceApplyExecuted);
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_StaleAuthorityBlocksRecovery()
    {
        var result = Run(ValidRequest(staleAuthority: StaleAuthority()));

        AssertStatus(result, FailedApplyRecoveryCampaignStatuses.RecoveryEvidenceStale, "StaleAuthorityBlocksRecovery");
        Assert.IsTrue(result.StaleAuthorityDetected);
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_NoStaleAuthorityEvidenceAddsWarningOnly()
    {
        var result = Run(ValidRequest() with { StaleAuthorityDetection = null });

        Assert.AreEqual(FailedApplyRecoveryCampaignStatuses.RecoveryEvidenceSatisfied, result.Status, string.Join("; ", result.Findings.Select(finding => finding.Code)));
        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.Findings.Any(finding => finding.Code == "StaleAuthorityEvidenceNotSupplied" && finding.Severity == FailedApplyRecoveryFindingSeverities.Warning));
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_CurrentStaleAuthorityEvidenceAllowsRecoveryEvidenceEvaluation()
    {
        var result = Run(ValidRequest(staleAuthority: CurrentStaleAuthority()));

        Assert.AreEqual(FailedApplyRecoveryCampaignStatuses.RecoveryEvidenceSatisfied, result.Status, string.Join("; ", result.Findings.Select(finding => finding.Code)));
        Assert.IsFalse(result.StaleAuthorityDetected);
        Assert.IsTrue(result.RecoveryEvidenceSatisfied);
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_FollowUpReadyEvidenceDoesNotApproveRelease()
    {
        var result = Run(ValidRequest(followUp: ReadyDecision()));

        Assert.AreEqual(FailedApplyRecoveryCampaignStatuses.RecoveryEvidenceSatisfied, result.Status, string.Join("; ", result.Findings.Select(finding => finding.Code)));
        Assert.IsTrue(result.FollowUpReadinessEvidencePresent);
        Assert.IsFalse(result.ReleaseApproved);
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_FollowUpReadinessAuthorityClaimFailsRecovery()
    {
        var result = Run(ValidRequest(followUp: ReadyDecision() with { ReleaseApproved = true }));

        AssertStatus(result, FailedApplyRecoveryCampaignStatuses.RecoveryEvidenceFailed, "FollowUpReadinessClaimsReleaseApproval");
        Assert.IsFalse(result.ReleaseApproved);
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_FollowUpReadinessExecutionClaimFailsRecovery()
    {
        var result = Run(ValidRequest(followUp: ReadyDecision() with { ReleaseExecutedByDecision = true }));

        AssertStatus(result, FailedApplyRecoveryCampaignStatuses.RecoveryEvidenceFailed, "FollowUpReadinessClaimsExecution");
        Assert.IsFalse(result.ReleaseExecuted);
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_PrivateRawMaterialIsRejected()
    {
        var result = Run(ValidRequest() with { RequestedBy = "raw prompt leaked" });

        AssertStatus(result, FailedApplyRecoveryCampaignStatuses.Rejected, "PrivateRawMaterialRejected");
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_AuthorityClaimIsRejected()
    {
        var result = Run(ValidRequest() with { EvidenceReferences = ["green to ship"] });

        AssertStatus(result, FailedApplyRecoveryCampaignStatuses.Rejected, "AuthorityClaimRejected");
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_DoesNotEchoUnsafeMaterial()
    {
        var result = Run(ValidRequest() with
        {
            CampaignName = "raw prompt secret campaign",
            SourceApplyFailure = ValidSourceApplyFailure() with { EvidenceReferences = ["chain of thought should not surface"] }
        });
        var serialized = JsonSerializer.Serialize(result);

        AssertStatus(result, FailedApplyRecoveryCampaignStatuses.Rejected, "PrivateRawMaterialRejected");
        Assert.IsFalse(serialized.Contains("raw prompt secret campaign", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains("chain of thought should not surface", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_RunnerHasNoSourceApplyRollbackWorkflowReleaseGitRuntimeDependencies()
    {
        var source = RunnerSource();

        foreach (var marker in new[]
        {
            "ControlledSourceApplyExecutor",
            "ControlledRollbackExecutor",
            "RollbackExecutionAudit",
            "GovernedWorkflowContinuationService",
            "ReleaseReadinessGateEvaluator",
            "GovernedReleaseGateService",
            "GovernedDogfoodCampaignRunner",
            "IReleaseReadinessDecisionRecordStore",
            "SqlConnection",
            "IDbConnection",
            "Dapper",
            "HttpClient",
            "HttpPost",
            "ControllerBase",
            "Cli",
            "Process.Start",
            "ProcessStartInfo",
            "git commit",
            "git push",
            "git merge",
            "gh pr",
            "IHostedService",
            "BackgroundService",
            "Scheduler"
        })
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.Ordinal), $"Forbidden marker in failed-apply recovery runner: {marker}");
        }
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_RunnerDoesNotUseStaleAuthorityDetectorDirectly()
    {
        var source = RunnerSource();

        Assert.IsFalse(source.Contains("StaleAuthorityDetector", StringComparison.Ordinal), "PR226 consumes supplied stale-authority evidence and must not rerun stale-authority detection.");
    }

    [TestMethod]
    public void FailedApplyRecoveryCampaign_RunnerDoesNotUseApiCliSqlStoreAgentsModelsToolsMemoryOrRetrieval()
    {
        var source = RunnerSource();

        foreach (var marker in new[]
        {
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
            "SourceApplyRetried = true",
            "RollbackExecutedByCampaign = true",
            "RollbackAuditExecutedByCampaign = true",
            "WorkflowContinued = true",
            "WorkflowMutated = true",
            "GitOperationExecuted = true",
            "AuthorityRefreshed = true",
            "EvidenceReissued = true"
        })
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.Ordinal), $"Forbidden marker in failed-apply recovery runner: {marker}");
        }
    }

    private static FailedApplyRecoveryCampaignResult Run(FailedApplyRecoveryCampaignRequest? request) =>
        new FailedApplyRecoveryCampaignRunner().Run(request);

    private static FailedApplyRecoveryCampaignRequest ValidRequest(
        SourceApplyFailureEvidence? sourceFailure = null,
        RollbackRecoveryEvidence? rollback = null,
        RollbackAuditEvidence? audit = null,
        StaleAuthorityDetectionResult? staleAuthority = null,
        ReleaseReadinessDecisionRecord? followUp = null) => new()
        {
            FailedApplyRecoveryCampaignRequestId = DeterministicGuid("failed-apply-recovery-campaign"),
            ProjectId = ProjectId,
            CampaignName = "PR226 failed apply recovery campaign",
            RequestedBy = "human-reviewer",
            RequestedAtUtc = Now,
            WorkflowRunId = WorkflowRunId,
            WorkflowStepId = WorkflowStepId,
            SubjectKind = SubjectKind,
            SubjectId = SubjectId,
            SubjectHash = SubjectHash,
            SourceApplyFailure = sourceFailure ?? ValidSourceApplyFailure(),
            RollbackRecovery = rollback ?? ValidRollbackRecovery(),
            RollbackAudit = audit ?? ValidRollbackAudit(),
            StaleAuthorityDetection = staleAuthority ?? CurrentStaleAuthority(),
            FollowUpReleaseReadinessDecision = followUp,
            EvidenceReferences = ["failed-apply-recovery:request", "human-review:required"],
            BoundaryMaxims =
            [
                "Recovery evidence satisfied is not release approval.",
                "Recovery campaign does not retry source apply.",
                "Human review remains required."
            ]
        };

    private static SourceApplyFailureEvidence ValidSourceApplyFailure() => new()
    {
        SourceApplyRequestId = DeterministicGuid("source-apply-request"),
        SourceApplyRequestHash = H("source-apply-request"),
        SourceApplyReceiptId = DeterministicGuid("source-apply-receipt"),
        SourceApplyReceiptHash = H("source-apply-receipt"),
        SourceApplyAttempted = true,
        SourceApplySucceeded = false,
        SourceApplyPartial = true,
        SourceApplyFailed = true,
        ExpectedBranch = "main",
        SourceBaselineHash = BaselineHash,
        WorkspaceHash = FailedWorkspaceHash,
        AttemptedAtUtc = Now.AddMinutes(-30),
        FailedPaths = ["src/Foo.cs"],
        AppliedPaths = ["src/Bar.cs"],
        EvidenceReferences = ["source-apply:failed", "source-apply:partial"]
    };

    private static RollbackRecoveryEvidence ValidRollbackRecovery() => new()
    {
        RollbackExecutionReceiptId = DeterministicGuid("rollback-execution-receipt"),
        RollbackExecutionReceiptHash = H("rollback-execution-receipt"),
        RollbackExecuted = true,
        RollbackSucceeded = true,
        RollbackPartial = false,
        RestoredSourceBaselineHash = BaselineHash,
        RestoredWorkspaceHash = RestoredWorkspaceHash,
        ExecutedAtUtc = Now.AddMinutes(-20),
        RestoredPaths = ["src/Foo.cs", "src/Bar.cs"],
        FailedRollbackPaths = [],
        EvidenceReferences = ["rollback:executed", "rollback:succeeded"]
    };

    private static RollbackAuditEvidence ValidRollbackAudit() => new()
    {
        RollbackAuditReportId = DeterministicGuid("rollback-audit-report"),
        RollbackAuditReportHash = H("rollback-audit-report"),
        AuditRan = true,
        AuditConsistent = true,
        AuditedSourceBaselineHash = BaselineHash,
        AuditedWorkspaceHash = RestoredWorkspaceHash,
        AuditedAtUtc = Now.AddMinutes(-10),
        Findings = ["rollback audit consistent"],
        EvidenceReferences = ["rollback-audit:consistent"]
    };

    private static StaleAuthorityDetectionResult CurrentStaleAuthority() => new()
    {
        StaleAuthorityDetectionRequestId = DeterministicGuid("current-stale-authority"),
        ProjectId = ProjectId,
        SubjectKind = SubjectKind,
        SubjectId = SubjectId,
        CurrentSubjectHash = SubjectHash,
        IsCurrent = true,
        HasStaleAuthority = false,
        HasExpiredEvidence = false,
        HasSupersededEvidence = false,
        HasSubjectHashMismatch = false,
        HasWorkflowMismatch = false,
        HasUnsafeMaterial = false,
        Findings = [],
        EvidenceReferences = ["stale-authority:current"],
        BoundaryMaxims = ["Stale authority detection is not approval."],
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
        EvaluatedAtUtc = Now.AddMinutes(-5)
    };

    private static StaleAuthorityDetectionResult StaleAuthority() =>
        CurrentStaleAuthority() with
        {
            IsCurrent = false,
            HasStaleAuthority = true,
            HasExpiredEvidence = true,
            Findings =
            [
                new StaleAuthorityFinding
                {
                    Code = "EvidenceExpired",
                    Severity = StaleAuthorityFindingSeverities.Blocking,
                    EvidenceKind = AuthorityEvidenceKinds.AcceptedApproval,
                    EvidenceId = "accepted-approval:expired",
                    Field = "ExpiresAtUtc",
                    Message = "Evidence is expired."
                }
            ]
        };

    private static ReleaseReadinessDecisionRecord ReadyDecision()
    {
        var record = new ReleaseReadinessDecisionRecord
        {
            ReleaseReadinessDecisionRecordId = DeterministicGuid("release-readiness-decision"),
            ProjectId = ProjectId,
            ReleaseReadinessReportId = DeterministicGuid("release-readiness-report"),
            ReleaseReadinessReportHash = H("release-readiness-report"),
            WorkflowRunId = WorkflowRunId,
            WorkflowStepId = WorkflowStepId,
            SubjectKind = SubjectKind,
            SubjectId = SubjectId,
            SubjectHash = SubjectHash,
            DecisionStatus = ReleaseReadinessDecisionStatuses.ReadyEvidenceSatisfied,
            ReleaseReadinessEvidenceSatisfied = true,
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
                    Code = "ReadyEvidenceSatisfied",
                    Severity = ReleaseReadinessDecisionReasonSeverities.Info,
                    Field = "ReleaseReadinessEvidenceSatisfied",
                    Message = "Supplied readiness evidence is satisfied for review only."
                }
            ],
            EvidenceReferences = ["release-readiness:follow-up"],
            BoundaryMaxims = ["Ready evidence is not release approval."],
            DecidedAtUtc = Now,
            ReleaseReadinessDecisionRecordHash = H("pending")
        };

        return record with { ReleaseReadinessDecisionRecordHash = ReleaseReadinessDecisionRecordHashing.ComputeHash(record) };
    }

    private static void AssertStatus(FailedApplyRecoveryCampaignResult result, string status, string code)
    {
        Assert.AreEqual(status, result.Status, string.Join("; ", result.Findings.Select(finding => finding.Code)));
        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.RecoveryEvidenceSatisfied);
        Assert.IsTrue(result.Findings.Any(finding => finding.Code == code), string.Join("; ", result.Findings.Select(finding => finding.Code)));
        AssertNoAuthority(result);
    }

    private static void AssertNoAuthority(FailedApplyRecoveryCampaignResult result)
    {
        Assert.IsFalse(result.SourceApplyRetried);
        Assert.IsFalse(result.SourceApplyExecuted);
        Assert.IsFalse(result.RollbackExecutedByCampaign);
        Assert.IsFalse(result.RollbackAuditExecutedByCampaign);
        Assert.IsFalse(result.WorkflowContinued);
        Assert.IsFalse(result.WorkflowMutated);
        Assert.IsFalse(result.ReleaseApproved);
        Assert.IsFalse(result.DeploymentApproved);
        Assert.IsFalse(result.MergeApproved);
        Assert.IsFalse(result.ReleaseExecuted);
        Assert.IsFalse(result.GitOperationExecuted);
        Assert.IsFalse(result.AuthorityRefreshed);
        Assert.IsFalse(result.EvidenceReissued);
        Assert.IsTrue(result.HumanReviewRequired);
    }

    private static string RunnerSource() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Infrastructure", "Governance", "FailedApplyRecoveryCampaignRunner.cs"));

    private static readonly Guid ProjectId = Guid.Parse("f1a85137-5699-48e2-a787-c1e7bcb60c1f");
    private const string WorkflowRunId = "workflow-run-pr226";
    private const string WorkflowStepId = "workflow-step-pr226";
    private const string SubjectKind = "ReleasePackage";
    private const string SubjectId = "release-package-pr226";
    private static readonly string SubjectHash = H("subject-pr226");
    private static readonly string BaselineHash = H("source-baseline-pr226");
    private static readonly string FailedWorkspaceHash = H("failed-workspace-pr226");
    private static readonly string RestoredWorkspaceHash = H("restored-workspace-pr226");

    private static Guid DeterministicGuid(string value) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes(value)));

    private static string H(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
