using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("StaleAuthorityDetection")]
[TestCategory("PR224")]
public sealed class StaleAuthorityDetectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 17, 23, 10, 0, TimeSpan.Zero);

    [TestMethod]
    public void StaleAuthorityDetection_CurrentEvidenceProducesCurrentResult()
    {
        var result = Detect(ValidRequest("current", ValidEvidence(AuthorityEvidenceKinds.AcceptedApproval, "current")));

        Assert.IsTrue(result.IsCurrent, string.Join("; ", result.Findings.Select(finding => finding.Code)));
        Assert.IsFalse(result.HasStaleAuthority);
        Assert.IsFalse(result.HasExpiredEvidence);
        Assert.IsFalse(result.HasSupersededEvidence);
        Assert.IsFalse(result.HasSubjectHashMismatch);
        Assert.IsFalse(result.HasWorkflowMismatch);
        Assert.IsFalse(result.HasUnsafeMaterial);
        Assert.AreEqual(0, result.Findings.Count);
    }

    [TestMethod]
    public void StaleAuthorityDetection_CurrentEvidenceDoesNotApproveRelease()
    {
        var result = Detect(ValidRequest("approval", ValidEvidence(AuthorityEvidenceKinds.AcceptedApproval, "approval")));

        Assert.IsTrue(result.IsCurrent);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void StaleAuthorityDetection_CurrentEvidenceDoesNotRefreshAuthority()
    {
        var result = Detect(ValidRequest("refresh", ValidEvidence(AuthorityEvidenceKinds.PolicySatisfaction, "refresh")));

        Assert.IsTrue(result.IsCurrent);
        Assert.IsFalse(result.AuthorityRefreshed);
        Assert.IsFalse(result.EvidenceReissued);
    }

    [TestMethod]
    public void StaleAuthorityDetection_ExpiredEvidenceProducesBlockingFinding()
    {
        var result = Detect(ValidRequest("expired", ValidEvidence(AuthorityEvidenceKinds.AcceptedApproval, "expired") with
        {
            ExpiresAtUtc = Now
        }));

        AssertBlocking(result, "EvidenceExpired");
        Assert.IsTrue(result.HasExpiredEvidence);
        Assert.IsTrue(result.HasStaleAuthority);
    }

    [TestMethod]
    public void StaleAuthorityDetection_EvidenceExpiringInFutureIsAccepted()
    {
        var result = Detect(ValidRequest("future-expiry", ValidEvidence(AuthorityEvidenceKinds.AcceptedApproval, "future-expiry") with
        {
            ExpiresAtUtc = Now.AddMinutes(10)
        }));

        Assert.IsTrue(result.IsCurrent, string.Join("; ", result.Findings.Select(finding => finding.Code)));
        Assert.IsFalse(result.HasExpiredEvidence);
    }

    [TestMethod]
    public void StaleAuthorityDetection_EvidenceCreatedInFutureIsRejected()
    {
        var result = Detect(ValidRequest("future-created", ValidEvidence(AuthorityEvidenceKinds.PolicySatisfaction, "future-created") with
        {
            CreatedAtUtc = Now.AddMinutes(1)
        }));

        AssertBlocking(result, "EvidenceCreatedInFuture");
    }

    [TestMethod]
    public void StaleAuthorityDetection_SupersededEvidenceProducesBlockingFinding()
    {
        var result = Detect(ValidRequest("superseded", ValidEvidence(AuthorityEvidenceKinds.SourceApplyReceipt, "superseded") with
        {
            Superseded = true,
            SupersededByEvidenceId = "source-apply-receipt-new",
            SupersededByEvidenceHash = HRaw("source-apply-receipt-new")
        }));

        AssertBlocking(result, "EvidenceSuperseded");
        Assert.IsTrue(result.HasSupersededEvidence);
    }

    [TestMethod]
    public void StaleAuthorityDetection_SupersededEvidenceRequiresReplacementReference()
    {
        var result = Detect(ValidRequest("superseded-missing", ValidEvidence(AuthorityEvidenceKinds.SourceApplyReceipt, "superseded-missing") with
        {
            Superseded = true
        }));

        AssertBlocking(result, "EvidenceSuperseded");
        AssertBlocking(result, "SupersedingEvidenceMissing");
        Assert.IsTrue(result.HasSupersededEvidence);
    }

    [TestMethod]
    public void StaleAuthorityDetection_SubjectHashMismatchProducesBlockingFinding()
    {
        var result = Detect(ValidRequest("subject-hash", ValidEvidence(AuthorityEvidenceKinds.AcceptedApproval, "subject-hash") with
        {
            SubjectHash = HRaw("old-subject")
        }));

        AssertBlocking(result, "SubjectBindingMismatch");
        Assert.IsTrue(result.HasSubjectHashMismatch);
    }

    [TestMethod]
    public void StaleAuthorityDetection_SubjectIdMismatchProducesBlockingFinding()
    {
        var result = Detect(ValidRequest("subject-id", ValidEvidence(AuthorityEvidenceKinds.AcceptedApproval, "subject-id") with
        {
            SubjectId = "old-release-package"
        }));

        AssertBlocking(result, "SubjectBindingMismatch");
        Assert.IsTrue(result.HasSubjectHashMismatch);
    }

    [TestMethod]
    public void StaleAuthorityDetection_WorkflowRunMismatchProducesBlockingFinding()
    {
        var result = Detect(ValidRequest("workflow-run", ValidEvidence(AuthorityEvidenceKinds.WorkflowTransitionRecord, "workflow-run") with
        {
            WorkflowRunId = "old-workflow-run"
        }));

        AssertBlocking(result, "WorkflowBindingMismatch");
        Assert.IsTrue(result.HasWorkflowMismatch);
    }

    [TestMethod]
    public void StaleAuthorityDetection_WorkflowStepMismatchProducesBlockingFinding()
    {
        var result = Detect(ValidRequest("workflow-step", ValidEvidence(AuthorityEvidenceKinds.WorkflowTransitionRecord, "workflow-step") with
        {
            WorkflowStepId = "old-workflow-step"
        }));

        AssertBlocking(result, "WorkflowBindingMismatch");
        Assert.IsTrue(result.HasWorkflowMismatch);
    }

    [TestMethod]
    public void StaleAuthorityDetection_AcceptsRawSha256Hash()
    {
        var result = Detect(ValidRequest("raw-hash", ValidEvidence(AuthorityEvidenceKinds.ReleaseReadinessReport, "raw-hash") with
        {
            EvidenceHash = HRaw("raw-hash"),
            SubjectHash = SubjectHash
        }));

        Assert.IsTrue(result.IsCurrent, string.Join("; ", result.Findings.Select(finding => finding.Code)));
    }

    [TestMethod]
    public void StaleAuthorityDetection_AcceptsPrefixedSha256Hash()
    {
        var result = Detect(ValidRequest("prefixed-hash", ValidEvidence(AuthorityEvidenceKinds.ReleaseReadinessReport, "prefixed-hash") with
        {
            EvidenceHash = H("prefixed-hash"),
            SubjectHash = H("subject-current")
        }) with { CurrentSubjectHash = H("subject-current") });

        Assert.IsTrue(result.IsCurrent, string.Join("; ", result.Findings.Select(finding => finding.Code)));
    }

    [TestMethod]
    public void StaleAuthorityDetection_RejectsInvalidHash()
    {
        var result = Detect(ValidRequest("invalid-hash", ValidEvidence(AuthorityEvidenceKinds.ReleaseReadinessReport, "invalid-hash") with
        {
            EvidenceHash = "not-a-hash"
        }));

        AssertBlocking(result, "InvalidEvidenceHash");
    }

    [TestMethod]
    public void StaleAuthorityDetection_NullRequestIsRejected()
    {
        var result = new StaleAuthorityDetector().Detect(null);

        Assert.IsFalse(result.IsCurrent);
        AssertBlocking(result, "RequestRequired");
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void StaleAuthorityDetection_EmptyEvidenceIsRejected()
    {
        var result = Detect(ValidRequest("empty") with { Evidence = [] });

        AssertBlocking(result, "EvidenceRequired");
    }

    [TestMethod]
    public void StaleAuthorityDetection_UnsupportedEvidenceKindIsRejected()
    {
        var result = Detect(ValidRequest("unsupported", ValidEvidence("OtherEvidence", "unsupported")));

        AssertBlocking(result, "UnsupportedEvidenceKind");
    }

    [TestMethod]
    public void StaleAuthorityDetection_DefaultEvaluatedAtIsRejected()
    {
        var result = Detect(ValidRequest("default-time", ValidEvidence(AuthorityEvidenceKinds.AcceptedApproval, "default-time")) with
        {
            EvaluatedAtUtc = default
        });

        AssertBlocking(result, "EvaluatedAtRequired");
    }

    [TestMethod]
    public void StaleAuthorityDetection_BlankSubjectOrWorkflowFieldsAreRejected()
    {
        var result = Detect(ValidRequest("blank", ValidEvidence(AuthorityEvidenceKinds.AcceptedApproval, "blank")) with
        {
            SubjectKind = "",
            SubjectId = "",
            WorkflowRunId = "",
            WorkflowStepId = ""
        });

        AssertBlocking(result, "SubjectKindRequired");
        AssertBlocking(result, "SubjectIdRequired");
        AssertBlocking(result, "WorkflowRunIdRequired");
        AssertBlocking(result, "WorkflowStepIdRequired");
    }

    [TestMethod]
    public void StaleAuthorityDetection_PrivateRawMaterialProducesBlockingFinding()
    {
        var result = Detect(ValidRequest("private", ValidEvidence(AuthorityEvidenceKinds.AcceptedApproval, "private") with
        {
            EvidenceReferences = ["raw prompt leaked"]
        }));

        AssertBlocking(result, "PrivateRawMaterialRejected");
        Assert.IsTrue(result.HasUnsafeMaterial);
    }

    [TestMethod]
    public void StaleAuthorityDetection_AuthorityClaimProducesBlockingFinding()
    {
        var result = Detect(ValidRequest("authority-claim", ValidEvidence(AuthorityEvidenceKinds.AcceptedApproval, "authority-claim") with
        {
            EvidenceReferences = ["green to ship"]
        }));

        AssertBlocking(result, "AuthorityClaimRejected");
        Assert.IsTrue(result.HasUnsafeMaterial);
    }

    [TestMethod]
    public void StaleAuthorityDetection_DoesNotEchoUnsafeMaterial()
    {
        var result = Detect(ValidRequest("no-echo", ValidEvidence(AuthorityEvidenceKinds.AcceptedApproval, "no-echo") with
        {
            EvidenceId = "raw prompt green to ship"
        }));
        var serialized = JsonSerializer.Serialize(result);

        AssertBlocking(result, "PrivateRawMaterialRejected");
        AssertBlocking(result, "AuthorityClaimRejected");
        Assert.IsFalse(serialized.Contains("raw prompt", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains("green to ship", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void StaleAuthorityDetection_AllowsNegativeBoundaryWording()
    {
        var result = Detect(ValidRequest("negative", ValidEvidence(AuthorityEvidenceKinds.AcceptedApproval, "negative")) with
        {
            BoundaryMaxims = ["not release approval", "not deployment approval", "not merge approval", "does not execute release", "does not run git"]
        });

        Assert.IsTrue(result.IsCurrent, string.Join("; ", result.Findings.Select(finding => finding.Code)));
    }

    [TestMethod]
    public void StaleAuthorityDetection_StaleApprovalBlocksReleaseReadinessEvidence()
    {
        var result = Detect(ValidRequest("stale-approval", ValidEvidence(AuthorityEvidenceKinds.AcceptedApproval, "stale-approval") with
        {
            ExpiresAtUtc = Now.AddSeconds(-1)
        }));

        AssertBlocking(result, "EvidenceExpired");
        Assert.IsFalse(result.ReleaseApproved);
    }

    [TestMethod]
    public void StaleAuthorityDetection_StalePolicySatisfactionBlocksReleaseReadinessEvidence()
    {
        var result = Detect(ValidRequest("stale-policy", ValidEvidence(AuthorityEvidenceKinds.PolicySatisfaction, "stale-policy") with
        {
            Superseded = true,
            SupersededByEvidenceId = "policy-satisfaction-new",
            SupersededByEvidenceHash = HRaw("policy-satisfaction-new")
        }));

        AssertBlocking(result, "EvidenceSuperseded");
        Assert.IsFalse(result.ReleaseApproved);
    }

    [TestMethod]
    public void StaleAuthorityDetection_StaleSourceApplyReceiptBlocksReleaseReadinessEvidence()
    {
        var result = Detect(ValidRequest("stale-source", ValidEvidence(AuthorityEvidenceKinds.SourceApplyReceipt, "stale-source") with
        {
            SubjectHash = HRaw("old-source-subject")
        }));

        AssertBlocking(result, "SubjectBindingMismatch");
        Assert.IsFalse(result.SourceApplyExecuted);
    }

    [TestMethod]
    public void StaleAuthorityDetection_StaleWorkflowTransitionBlocksReleaseReadinessEvidence()
    {
        var result = Detect(ValidRequest("stale-transition", ValidEvidence(AuthorityEvidenceKinds.WorkflowTransitionRecord, "stale-transition") with
        {
            WorkflowStepId = "old-step"
        }));

        AssertBlocking(result, "WorkflowBindingMismatch");
        Assert.IsFalse(result.WorkflowContinued);
        Assert.IsFalse(result.WorkflowMutated);
    }

    [TestMethod]
    public void StaleAuthorityDetection_StaleReleaseReadinessDecisionDoesNotBecomeReleaseApproval()
    {
        var result = Detect(ValidRequest("stale-decision", ValidEvidence(AuthorityEvidenceKinds.ReleaseReadinessDecisionRecord, "stale-decision") with
        {
            Superseded = true,
            SupersededByEvidenceId = "release-decision-new",
            SupersededByEvidenceHash = HRaw("release-decision-new")
        }));

        AssertBlocking(result, "EvidenceSuperseded");
        Assert.IsFalse(result.ReleaseApproved);
    }

    [TestMethod]
    public void StaleAuthorityDetection_CurrentCampaignEvidenceDoesNotBecomeAutonomy()
    {
        var result = Detect(ValidRequest("campaign", ValidEvidence(AuthorityEvidenceKinds.GovernedReleaseGateResult, "campaign")));

        Assert.IsTrue(result.IsCurrent, string.Join("; ", result.Findings.Select(finding => finding.Code)));
        AssertNoAuthority(result);
        Assert.IsFalse(result.AuthorityRefreshed);
        Assert.IsFalse(result.EvidenceReissued);
    }

    [TestMethod]
    public void StaleAuthorityDetection_StaticProductionFilesDoNotGainAuthorityRefreshExecutionOrRuntimePaths()
    {
        var root = RepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "StaleAuthorityDetection.cs"));

        StringAssert.Contains(source, "StaleAuthorityDetector");

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
            "ApproveRelease",
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
            "GovernedWorkflowContinuationService",
            "GovernedReleaseGateService",
            "GovernedDogfoodCampaignRunner",
            "SqlConnection",
            "IDbConnection",
            "Dapper",
            "HttpClient",
            "HttpPost",
            "ControllerBase",
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
            Assert.IsFalse(source.Contains(marker, StringComparison.Ordinal), $"Forbidden marker in stale authority detector: {marker}");
        }
    }

    private static StaleAuthorityDetectionResult Detect(StaleAuthorityDetectionRequest request) =>
        new StaleAuthorityDetector().Detect(request);

    private static StaleAuthorityDetectionRequest ValidRequest(string suffix, params AuthorityEvidenceSnapshot[] evidence) => new()
    {
        StaleAuthorityDetectionRequestId = DeterministicGuid($"stale-authority-detection-{suffix}"),
        ProjectId = ProjectId,
        SubjectKind = SubjectKind,
        SubjectId = SubjectId,
        CurrentSubjectHash = SubjectHash,
        WorkflowRunId = WorkflowRunId,
        WorkflowStepId = WorkflowStepId,
        EvaluatedAtUtc = Now,
        Evidence = evidence.Length == 0 ? [ValidEvidence(AuthorityEvidenceKinds.AcceptedApproval, suffix)] : evidence,
        EvidenceReferences = [$"stale-authority-check:{suffix}", $"human-review:{suffix}"],
        BoundaryMaxims = ["Stale authority detection is not approval.", "Human review remains required."]
    };

    private static AuthorityEvidenceSnapshot ValidEvidence(string kind, string suffix) => new()
    {
        EvidenceKind = kind,
        EvidenceId = $"{kind}:{suffix}",
        EvidenceHash = HRaw($"evidence-{kind}-{suffix}"),
        SubjectKind = SubjectKind,
        SubjectId = SubjectId,
        SubjectHash = SubjectHash,
        WorkflowRunId = WorkflowRunId,
        WorkflowStepId = WorkflowStepId,
        CreatedAtUtc = Now.AddMinutes(-5),
        ExpiresAtUtc = Now.AddHours(1),
        PolicyVersion = "policy-v1",
        ApprovalVersion = "approval-v1",
        SourceBaselineHash = HRaw($"source-baseline-{suffix}"),
        Superseded = false,
        SupersededByEvidenceId = null,
        SupersededByEvidenceHash = null,
        EvidenceReferences = [$"{kind}:{suffix}", $"trace:{suffix}"]
    };

    private static void AssertBlocking(StaleAuthorityDetectionResult result, string code)
    {
        Assert.IsFalse(result.IsCurrent);
        Assert.IsTrue(result.HasStaleAuthority);
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

    private static readonly Guid ProjectId = Guid.Parse("208498a9-b503-477c-80ff-bbd3b48c546a");
    private const string SubjectKind = "ReleasePackage";
    private const string SubjectId = "release-package-pr224";
    private static readonly string SubjectHash = HRaw("subject-current");
    private const string WorkflowRunId = "workflow-run-pr224";
    private const string WorkflowStepId = "workflow-step-pr224";

    private static Guid DeterministicGuid(string value) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes(value)));

    private static string H(string value) =>
        $"sha256:{HRaw(value)}";

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
