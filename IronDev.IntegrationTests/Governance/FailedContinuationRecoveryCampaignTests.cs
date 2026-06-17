using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("FailedContinuationRecoveryCampaign")]
[TestCategory("PR227")]
public sealed class FailedContinuationRecoveryCampaignTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 18, 14, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_NullRequestIsRejected()
    {
        var result = Run(null);

        AssertStatus(result, FailedContinuationRecoveryCampaignStatuses.Rejected, "RequestRequired");
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_MissingContinuationFailureIsRejected()
    {
        var result = Run(ValidRequest() with { WorkflowContinuationFailure = null! });

        AssertStatus(result, FailedContinuationRecoveryCampaignStatuses.Rejected, "WorkflowContinuationFailureEvidenceRequired");
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_ContinuationSucceededIsRejectedAsRecoveryCampaign()
    {
        var result = Run(ValidRequest() with
        {
            WorkflowContinuationFailure = ValidContinuationFailure() with { ContinuationSucceeded = true, ContinuationFailed = false }
        });

        AssertStatus(result, FailedContinuationRecoveryCampaignStatuses.Rejected, "WorkflowContinuationSucceededNotRecovery");
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_ContinuationFailureWithoutFailureFlagIsRejected()
    {
        var result = Run(ValidRequest() with
        {
            WorkflowContinuationFailure = ValidContinuationFailure() with { ContinuationFailed = false }
        });

        AssertStatus(result, FailedContinuationRecoveryCampaignStatuses.Rejected, "WorkflowContinuationFailureNotProven");
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_WorkflowMutatedDuringFailureIsRejected()
    {
        var result = Run(ValidRequest() with
        {
            WorkflowContinuationFailure = ValidContinuationFailure() with { WorkflowMutated = true }
        });

        AssertStatus(result, FailedContinuationRecoveryCampaignStatuses.Rejected, "WorkflowMutatedDuringFailure");
        Assert.IsTrue(result.WorkflowWasMutatedDuringFailure);
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_InvalidHashesAreRejected()
    {
        var result = Run(ValidRequest() with
        {
            WorkflowContinuationFailure = ValidContinuationFailure() with { GovernedWorkflowContinuationRequestHash = "not-a-hash" }
        });

        AssertStatus(result, FailedContinuationRecoveryCampaignStatuses.Rejected, "WorkflowContinuationFailureHashInvalid");
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_MissingFailureReasonsAreRejected()
    {
        var result = Run(ValidRequest() with
        {
            WorkflowContinuationFailure = ValidContinuationFailure() with { FailedTransitionReasons = [] }
        });

        AssertStatus(result, FailedContinuationRecoveryCampaignStatuses.Rejected, "WorkflowContinuationFailureReasonsMissing");
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_MissingTransitionRecoveryEvidenceFailsRecovery()
    {
        var result = Run(ValidRequest() with { WorkflowTransitionRecovery = null });

        AssertStatus(result, FailedContinuationRecoveryCampaignStatuses.RecoveryEvidenceMissing, "WorkflowTransitionRecoveryEvidenceMissing");
        Assert.IsFalse(result.TransitionRecoveryEvidencePresent);
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_FailureNotReviewedFailsRecovery()
    {
        var result = Run(ValidRequest(recovery: ValidTransitionRecovery() with { FailureReviewed = false }));

        AssertStatus(result, FailedContinuationRecoveryCampaignStatuses.RecoveryEvidenceFailed, "ContinuationFailureNotReviewed");
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_WorkflowStateNotConfirmedUnchangedFailsRecovery()
    {
        var result = Run(ValidRequest(recovery: ValidTransitionRecovery() with { WorkflowStateConfirmedUnchanged = false }));

        AssertStatus(result, FailedContinuationRecoveryCampaignStatuses.RecoveryEvidenceFailed, "WorkflowStateNotConfirmedUnchanged");
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_RetryHumanReviewNotRequiredFailsRecovery()
    {
        var result = Run(ValidRequest(recovery: ValidTransitionRecovery() with { RetryRequiresHumanReview = false }));

        AssertStatus(result, FailedContinuationRecoveryCampaignStatuses.RecoveryEvidenceFailed, "RetryHumanReviewNotRequired");
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_RecoveryWorkflowRunMismatchFailsRecovery()
    {
        var result = Run(ValidRequest(recovery: ValidTransitionRecovery() with { ConfirmedWorkflowRunId = "other-workflow-run" }));

        AssertStatus(result, FailedContinuationRecoveryCampaignStatuses.RecoveryEvidenceFailed, "RecoveryWorkflowRunMismatch");
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_RecoveryWorkflowStepMismatchFailsRecovery()
    {
        var result = Run(ValidRequest(recovery: ValidTransitionRecovery() with { ConfirmedWorkflowStepId = "other-workflow-step" }));

        AssertStatus(result, FailedContinuationRecoveryCampaignStatuses.RecoveryEvidenceFailed, "RecoveryWorkflowStepMismatch");
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_RecoveryWorkflowStateHashMismatchFailsRecovery()
    {
        var result = Run(ValidRequest(recovery: ValidTransitionRecovery() with { ConfirmedWorkflowStateHash = H("other-state") }));

        AssertStatus(result, FailedContinuationRecoveryCampaignStatuses.RecoveryEvidenceFailed, "RecoveryWorkflowStateHashMismatch");
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_RecoveryFindingsMissingFailsRecovery()
    {
        var result = Run(ValidRequest(recovery: ValidTransitionRecovery() with { Findings = [] }));

        AssertStatus(result, FailedContinuationRecoveryCampaignStatuses.RecoveryEvidenceFailed, "RecoveryFindingsMissing");
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_CompleteTransitionRecoveryEvidenceSatisfiesRecoveryEvidence()
    {
        var result = Run(ValidRequest());

        Assert.AreEqual(FailedContinuationRecoveryCampaignStatuses.RecoveryEvidenceSatisfied, result.Status, string.Join("; ", result.Findings.Select(finding => finding.Code)));
        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.ContinuationFailureConfirmed);
        Assert.IsTrue(result.TransitionRecoveryEvidencePresent);
        Assert.IsTrue(result.WorkflowStateConfirmedUnchanged);
        Assert.IsTrue(result.RetryRequiresHumanReview);
        Assert.IsTrue(result.RecoveryEvidenceSatisfied);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_RecoveryEvidenceSatisfiedDoesNotApproveRelease()
    {
        var result = Run(ValidRequest());

        Assert.IsTrue(result.RecoveryEvidenceSatisfied);
        Assert.IsFalse(result.ReleaseApproved);
        Assert.IsFalse(result.DeploymentApproved);
        Assert.IsFalse(result.MergeApproved);
        Assert.IsTrue(result.HumanReviewRequired);
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_RecoveryEvidenceSatisfiedDoesNotContinueWorkflow()
    {
        var result = Run(ValidRequest());

        Assert.IsTrue(result.RecoveryEvidenceSatisfied);
        Assert.IsFalse(result.WorkflowContinued);
        Assert.IsFalse(result.WorkflowMutated);
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_RecoveryEvidenceSatisfiedDoesNotRetryContinuation()
    {
        var result = Run(ValidRequest());

        Assert.IsTrue(result.RecoveryEvidenceSatisfied);
        Assert.IsFalse(result.WorkflowContinuationRetried);
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_RecoveryEvidenceSatisfiedDoesNotCreateTransitionRecord()
    {
        var result = Run(ValidRequest());

        Assert.IsTrue(result.RecoveryEvidenceSatisfied);
        Assert.IsFalse(result.WorkflowTransitionRecordCreated);
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_StaleAuthorityBlocksRecovery()
    {
        var result = Run(ValidRequest(staleAuthority: StaleAuthority()));

        AssertStatus(result, FailedContinuationRecoveryCampaignStatuses.RecoveryEvidenceStale, "StaleAuthorityBlocksContinuationRecovery");
        Assert.IsTrue(result.StaleAuthorityDetected);
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_NoStaleAuthorityEvidenceAddsWarningOnly()
    {
        var result = Run(ValidRequest() with { StaleAuthorityDetection = null });

        Assert.AreEqual(FailedContinuationRecoveryCampaignStatuses.RecoveryEvidenceSatisfied, result.Status, string.Join("; ", result.Findings.Select(finding => finding.Code)));
        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.Findings.Any(finding => finding.Code == "StaleAuthorityEvidenceNotSupplied" && finding.Severity == FailedContinuationRecoveryFindingSeverities.Warning));
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_CurrentStaleAuthorityEvidenceAllowsRecoveryEvidenceEvaluation()
    {
        var result = Run(ValidRequest(staleAuthority: CurrentStaleAuthority()));

        Assert.AreEqual(FailedContinuationRecoveryCampaignStatuses.RecoveryEvidenceSatisfied, result.Status, string.Join("; ", result.Findings.Select(finding => finding.Code)));
        Assert.IsFalse(result.StaleAuthorityDetected);
        Assert.IsTrue(result.RecoveryEvidenceSatisfied);
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_FollowUpReadyEvidenceDoesNotApproveRelease()
    {
        var result = Run(ValidRequest(followUp: ReadyDecision()));

        Assert.AreEqual(FailedContinuationRecoveryCampaignStatuses.RecoveryEvidenceSatisfied, result.Status, string.Join("; ", result.Findings.Select(finding => finding.Code)));
        Assert.IsTrue(result.FollowUpReadinessEvidencePresent);
        Assert.IsFalse(result.ReleaseApproved);
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_FollowUpReadinessAuthorityClaimFailsRecovery()
    {
        var result = Run(ValidRequest(followUp: ReadyDecision() with { ReleaseApproved = true }));

        AssertStatus(result, FailedContinuationRecoveryCampaignStatuses.RecoveryEvidenceFailed, "FollowUpReadinessClaimsReleaseApproval");
        Assert.IsFalse(result.ReleaseApproved);
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_FollowUpReadinessExecutionClaimFailsRecovery()
    {
        var result = Run(ValidRequest(followUp: ReadyDecision() with { ReleaseExecutedByDecision = true }));

        AssertStatus(result, FailedContinuationRecoveryCampaignStatuses.RecoveryEvidenceFailed, "FollowUpReadinessClaimsExecution");
        Assert.IsFalse(result.ReleaseExecuted);
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_PrivateRawMaterialIsRejected()
    {
        var result = Run(ValidRequest() with { RequestedBy = "raw prompt leaked" });

        AssertStatus(result, FailedContinuationRecoveryCampaignStatuses.Rejected, "PrivateRawMaterialRejected");
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_AuthorityClaimIsRejected()
    {
        var result = Run(ValidRequest() with { EvidenceReferences = ["safe to continue"] });

        AssertStatus(result, FailedContinuationRecoveryCampaignStatuses.Rejected, "AuthorityClaimRejected");
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_DoesNotEchoUnsafeMaterial()
    {
        var result = Run(ValidRequest() with
        {
            CampaignName = "raw prompt secret campaign",
            WorkflowContinuationFailure = ValidContinuationFailure() with { EvidenceReferences = ["chain of thought should not surface"] }
        });
        var serialized = JsonSerializer.Serialize(result);

        AssertStatus(result, FailedContinuationRecoveryCampaignStatuses.Rejected, "PrivateRawMaterialRejected");
        Assert.IsFalse(serialized.Contains("raw prompt secret campaign", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains("chain of thought should not surface", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_AllowsNegativeBoundaryWording()
    {
        var result = Run(ValidRequest() with
        {
            BoundaryMaxims =
            [
                "Recovery evidence is not release approved.",
                "Campaign does not continue workflow.",
                "Campaign does not retry workflow continuation.",
                "Campaign does not mutate workflow state."
            ]
        });

        Assert.AreEqual(FailedContinuationRecoveryCampaignStatuses.RecoveryEvidenceSatisfied, result.Status, string.Join("; ", result.Findings.Select(finding => finding.Code)));
        Assert.IsFalse(result.Findings.Any(finding => finding.Code == "AuthorityClaimRejected"));
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_RunnerHasNoWorkflowContinuationReleaseGitRuntimeDependencies()
    {
        var source = RunnerSource();

        foreach (var marker in new[]
        {
            "GovernedWorkflowContinuationService",
            "WorkflowTransitionRecordStore",
            "WorkflowContinuationGateEvaluator",
            "ReleaseReadinessGateEvaluator",
            "GovernedReleaseGateService",
            "GovernedDogfoodCampaignRunner",
            "FailedApplyRecoveryCampaignRunner",
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
            Assert.IsFalse(source.Contains(marker, StringComparison.Ordinal), $"Forbidden marker in failed-continuation recovery runner: {marker}");
        }
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_RunnerDoesNotUseStaleAuthorityDetectorDirectly()
    {
        var source = RunnerSource();

        Assert.IsFalse(source.Contains("StaleAuthorityDetector", StringComparison.Ordinal), "PR227 consumes supplied stale-authority evidence and must not rerun stale-authority detection.");
    }

    [TestMethod]
    public void FailedContinuationRecoveryCampaign_RunnerDoesNotUseApiCliSqlStoreAgentsModelsToolsMemoryOrRetrieval()
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
            "RollbackExecuted = true",
            "RollbackAuditExecuted = true",
            "WorkflowContinuationRetried = true",
            "WorkflowContinued = true",
            "WorkflowMutated = true",
            "WorkflowTransitionRecordCreated = true",
            "GitOperationExecuted = true",
            "AuthorityRefreshed = true",
            "EvidenceReissued = true"
        })
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.Ordinal), $"Forbidden marker in failed-continuation recovery runner: {marker}");
        }
    }

    private static FailedContinuationRecoveryCampaignResult Run(FailedContinuationRecoveryCampaignRequest? request) =>
        new FailedContinuationRecoveryCampaignRunner().Run(request);

    private static FailedContinuationRecoveryCampaignRequest ValidRequest(
        WorkflowContinuationFailureEvidence? failure = null,
        WorkflowTransitionRecoveryEvidence? recovery = null,
        StaleAuthorityDetectionResult? staleAuthority = null,
        ReleaseReadinessDecisionRecord? followUp = null) => new()
        {
            FailedContinuationRecoveryCampaignRequestId = DeterministicGuid("failed-continuation-recovery-campaign"),
            ProjectId = ProjectId,
            CampaignName = "PR227 failed continuation recovery campaign",
            RequestedBy = "human-reviewer",
            RequestedAtUtc = Now,
            WorkflowRunId = WorkflowRunId,
            WorkflowStepId = WorkflowStepId,
            SubjectKind = SubjectKind,
            SubjectId = SubjectId,
            SubjectHash = SubjectHash,
            WorkflowContinuationFailure = failure ?? ValidContinuationFailure(),
            WorkflowTransitionRecovery = recovery ?? ValidTransitionRecovery(),
            StaleAuthorityDetection = staleAuthority ?? CurrentStaleAuthority(),
            FollowUpReleaseReadinessDecision = followUp,
            EvidenceReferences = ["failed-continuation-recovery:request", "human-review:required"],
            BoundaryMaxims =
            [
                "Recovery evidence satisfied is not release approval.",
                "Recovery campaign does not continue workflow.",
                "Human review remains required."
            ]
        };

    private static WorkflowContinuationFailureEvidence ValidContinuationFailure() => new()
    {
        GovernedWorkflowContinuationRequestId = DeterministicGuid("governed-workflow-continuation-request"),
        GovernedWorkflowContinuationRequestHash = H("governed-workflow-continuation-request"),
        WorkflowTransitionRecordId = null,
        WorkflowTransitionRecordHash = null,
        ContinuationAttempted = true,
        ContinuationSucceeded = false,
        ContinuationFailed = true,
        WorkflowMutated = false,
        FromWorkflowStepId = WorkflowStepId,
        IntendedToWorkflowStepId = IntendedWorkflowStepId,
        ExpectedWorkflowStateHash = ExpectedWorkflowStateHash,
        ObservedWorkflowStateHash = ObservedWorkflowStateHash,
        AttemptedAtUtc = Now.AddMinutes(-30),
        FailedTransitionReasons = ["gate evidence stale", "human review required"],
        EvidenceReferences = ["workflow-continuation:failed", "workflow-state:unchanged"]
    };

    private static WorkflowTransitionRecoveryEvidence ValidTransitionRecovery() => new()
    {
        RecoveryEvidenceId = DeterministicGuid("workflow-transition-recovery-evidence"),
        RecoveryEvidenceHash = H("workflow-transition-recovery-evidence"),
        FailureReviewed = true,
        WorkflowStateConfirmedUnchanged = true,
        RetryRequiresHumanReview = true,
        ConfirmedWorkflowRunId = WorkflowRunId,
        ConfirmedWorkflowStepId = WorkflowStepId,
        ConfirmedWorkflowStateHash = ObservedWorkflowStateHash,
        ReviewedAtUtc = Now.AddMinutes(-20),
        Findings = ["failed continuation reviewed", "workflow state unchanged", "retry requires human review"],
        EvidenceReferences = ["workflow-transition-recovery:reviewed"]
    };

    private static StaleAuthorityDetectionResult CurrentStaleAuthority() => new()
    {
        StaleAuthorityDetectionRequestId = DeterministicGuid("current-stale-authority-pr227"),
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
                    EvidenceKind = AuthorityEvidenceKinds.WorkflowTransitionRecord,
                    EvidenceId = "workflow-transition:expired",
                    Field = "ExpiresAtUtc",
                    Message = "Evidence is expired."
                }
            ]
        };

    private static ReleaseReadinessDecisionRecord ReadyDecision()
    {
        var record = new ReleaseReadinessDecisionRecord
        {
            ReleaseReadinessDecisionRecordId = DeterministicGuid("release-readiness-decision-pr227"),
            ProjectId = ProjectId,
            ReleaseReadinessReportId = DeterministicGuid("release-readiness-report-pr227"),
            ReleaseReadinessReportHash = H("release-readiness-report-pr227"),
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

    private static void AssertStatus(FailedContinuationRecoveryCampaignResult result, string status, string code)
    {
        Assert.AreEqual(status, result.Status, string.Join("; ", result.Findings.Select(finding => finding.Code)));
        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.RecoveryEvidenceSatisfied);
        Assert.IsTrue(result.Findings.Any(finding => finding.Code == code), string.Join("; ", result.Findings.Select(finding => finding.Code)));
        AssertNoAuthority(result);
    }

    private static void AssertNoAuthority(FailedContinuationRecoveryCampaignResult result)
    {
        Assert.IsFalse(result.WorkflowContinuationRetried);
        Assert.IsFalse(result.WorkflowContinued);
        Assert.IsFalse(result.WorkflowMutated);
        Assert.IsFalse(result.WorkflowTransitionRecordCreated);
        Assert.IsFalse(result.SourceApplyExecuted);
        Assert.IsFalse(result.RollbackExecuted);
        Assert.IsFalse(result.RollbackAuditExecuted);
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
        File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Infrastructure", "Governance", "FailedContinuationRecoveryCampaignRunner.cs"));

    private static readonly Guid ProjectId = Guid.Parse("ab2c51e7-9c3a-46ce-b9ef-8a3ab61308e4");
    private const string WorkflowRunId = "workflow-run-pr227";
    private const string WorkflowStepId = "workflow-step-pr227";
    private const string IntendedWorkflowStepId = "workflow-step-pr227-next";
    private const string SubjectKind = "WorkflowContinuationPackage";
    private const string SubjectId = "workflow-continuation-package-pr227";
    private static readonly string SubjectHash = H("subject-pr227");
    private static readonly string ExpectedWorkflowStateHash = H("expected-workflow-state-pr227");
    private static readonly string ObservedWorkflowStateHash = H("observed-workflow-state-pr227");

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
