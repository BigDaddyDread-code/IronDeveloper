using System.Security.Cryptography;
using System.Text;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class ReleaseReadinessDecisionRecordTests
{
    private static readonly Guid DecisionRecordId = Guid.Parse("73e70540-8e29-4a39-9058-7ee14c38d001");
    private static readonly Guid ProjectId = Guid.Parse("73e70540-8e29-4a39-9058-7ee14c38d002");
    private static readonly Guid ReportId = Guid.Parse("73e70540-8e29-4a39-9058-7ee14c38d003");
    private static readonly DateTimeOffset DecidedAt = new(2026, 6, 17, 5, 45, 0, TimeSpan.Zero);

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_ReadyEvidenceSatisfiedIsValidButDoesNotApproveRelease()
    {
        var record = ReadyRecord();

        var result = ReleaseReadinessDecisionRecordValidation.Validate(record);

        Assert.IsTrue(result.IsValid, Explain(result));
        Assert.IsTrue(record.ReleaseReadinessEvidenceSatisfied);
        Assert.IsFalse(record.ReleaseApproved);
        Assert.IsFalse(record.DeploymentApproved);
        Assert.IsFalse(record.MergeApproved);
        Assert.IsFalse(record.ReleaseExecutedByDecision);
        Assert.IsFalse(record.GitOperationExecutedByDecision);
        Assert.IsTrue(record.HumanReviewRequiredForReleaseApproval);
        Assert.IsTrue(record.HumanReviewRequiredForDeployment);
        Assert.IsTrue(record.HumanReviewRequiredForMerge);
    }

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_BlockedByMissingEvidenceIsValidWithBlockingReason()
    {
        var record = Rehash(ReadyRecord() with
        {
            DecisionStatus = ReleaseReadinessDecisionStatuses.BlockedByMissingEvidence,
            ReleaseReadinessEvidenceSatisfied = false,
            Reasons = new[]
            {
                Reason("ReportBlockedByMissingEvidence", ReleaseReadinessDecisionReasonSeverities.Blocking),
            },
        });

        var result = ReleaseReadinessDecisionRecordValidation.Validate(record);

        Assert.IsTrue(result.IsValid, Explain(result));
    }

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_BlockedByFailedEvidenceIsValidWithBlockingReason()
    {
        var record = Rehash(ReadyRecord() with
        {
            DecisionStatus = ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence,
            ReleaseReadinessEvidenceSatisfied = false,
            Reasons = new[]
            {
                Reason("ReportBlockedByFailedEvidence", ReleaseReadinessDecisionReasonSeverities.Blocking),
            },
        });

        var result = ReleaseReadinessDecisionRecordValidation.Validate(record);

        Assert.IsTrue(result.IsValid, Explain(result));
    }

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_BlockedByHumanReviewRequiredKeepsApprovalFalse()
    {
        var record = Rehash(ReadyRecord() with
        {
            DecisionStatus = ReleaseReadinessDecisionStatuses.BlockedByHumanReviewRequired,
            ReleaseReadinessEvidenceSatisfied = true,
            Reasons = new[]
            {
                Reason("HumanReviewRequiredForReleaseApproval", ReleaseReadinessDecisionReasonSeverities.Warning),
            },
        });

        var result = ReleaseReadinessDecisionRecordValidation.Validate(record);

        Assert.IsTrue(result.IsValid, Explain(result));
        Assert.IsFalse(record.ReleaseApproved);
        Assert.IsFalse(record.DeploymentApproved);
        Assert.IsFalse(record.MergeApproved);
        Assert.IsFalse(record.ReleaseExecutedByDecision);
    }

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_HashIsDeterministic()
    {
        var record = ReadyRecord();

        var first = ReleaseReadinessDecisionRecordHashing.ComputeHash(record);
        var second = ReleaseReadinessDecisionRecordHashing.ComputeHash(record);

        Assert.AreEqual(first, second);
        Assert.AreEqual(record.ReleaseReadinessDecisionRecordHash, first);
    }

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_RejectsReleaseApproval()
        => AssertInvalid(
            ReadyRecord() with { ReleaseApproved = true },
            "ReleaseApprovedRejected");

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_RejectsDeploymentApproval()
        => AssertInvalid(
            ReadyRecord() with { DeploymentApproved = true },
            "DeploymentApprovedRejected");

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_RejectsMergeApproval()
        => AssertInvalid(
            ReadyRecord() with { MergeApproved = true },
            "MergeApprovedRejected");

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_RejectsSourceApplyExecution()
        => AssertInvalid(
            ReadyRecord() with { SourceApplyExecutedByDecision = true },
            "SourceApplyExecutedByDecisionRejected");

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_RejectsRollbackExecution()
        => AssertInvalid(
            ReadyRecord() with { RollbackExecutedByDecision = true },
            "RollbackExecutedByDecisionRejected");

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_RejectsWorkflowMutation()
        => AssertInvalid(
            ReadyRecord() with { WorkflowMutatedByDecision = true },
            "WorkflowMutatedByDecisionRejected");

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_RejectsGitOperation()
        => AssertInvalid(
            ReadyRecord() with { GitOperationExecutedByDecision = true },
            "GitOperationExecutedByDecisionRejected");

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_RejectsReleaseExecution()
        => AssertInvalid(
            ReadyRecord() with { ReleaseExecutedByDecision = true },
            "ReleaseExecutedByDecisionRejected");

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_ReadyEvidenceSatisfiedRejectsBlockingReasons()
        => AssertInvalid(
            Rehash(ReadyRecord() with
            {
                Reasons = new[]
                {
                    Reason("UnexpectedBlockingReason", ReleaseReadinessDecisionReasonSeverities.Blocking),
                },
            }),
            "ReadyEvidenceSatisfiedRejectsBlockingReasons");

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_BlockedStatusRequiresBlockingReason()
        => AssertInvalid(
            Rehash(ReadyRecord() with
            {
                DecisionStatus = ReleaseReadinessDecisionStatuses.BlockedByMissingEvidence,
                ReleaseReadinessEvidenceSatisfied = false,
            }),
            "BlockedStatusRequiresBlockingReason");

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_BlockedStatusRejectsEvidenceSatisfiedTrue()
        => AssertInvalid(
            Rehash(ReadyRecord() with
            {
                DecisionStatus = ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence,
                ReleaseReadinessEvidenceSatisfied = true,
                Reasons = new[]
                {
                    Reason("ReportBlockedByFailedEvidence", ReleaseReadinessDecisionReasonSeverities.Blocking),
                },
            }),
            "BlockedStatusRejectsEvidenceSatisfiedTrue");

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_UnknownStatusRejected()
        => AssertInvalid(
            Rehash(ReadyRecord() with { DecisionStatus = "ReleaseReady" }),
            "UnknownDecisionStatus");

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_RejectsPrivateRawMaterial()
        => AssertInvalid(
            Rehash(ReadyRecord() with
            {
                Reasons = new[]
                {
                    Reason("ReportComplete", ReleaseReadinessDecisionReasonSeverities.Info) with
                    {
                        Message = "raw prompt leaked",
                    },
                },
            }),
            "PrivateRawMaterialRejected");

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_RejectsReleaseAuthorityClaims()
        => AssertInvalid(
            Rehash(ReadyRecord() with
            {
                Reasons = new[]
                {
                    Reason("ReleaseAuthorityClaim", ReleaseReadinessDecisionReasonSeverities.Info) with
                    {
                        Message = "release approved",
                    },
                },
            }),
            "AuthorityClaimRejected");

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_AllowsNegativeBoundaryWording()
    {
        var record = Rehash(ReadyRecord() with
        {
            Boundary = """
                This is not release approval.
                This is not deployment approval.
                This is not merge approval.
                This does not execute release.
                This does not run git.
                """,
        });

        var result = ReleaseReadinessDecisionRecordValidation.Validate(record);

        Assert.IsTrue(result.IsValid, Explain(result));
    }

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_HashChangesWhenStatusChanges()
    {
        var original = ReadyRecord();
        var changed = Rehash(original with
        {
            DecisionStatus = ReleaseReadinessDecisionStatuses.BlockedByHumanReviewRequired,
            Reasons = new[]
            {
                Reason("HumanReviewRequiredForReleaseApproval", ReleaseReadinessDecisionReasonSeverities.Warning),
            },
        });

        Assert.AreNotEqual(original.ReleaseReadinessDecisionRecordHash, changed.ReleaseReadinessDecisionRecordHash);
    }

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_HashChangesWhenReasonsChange()
    {
        var original = ReadyRecord();
        var changed = Rehash(original with
        {
            Reasons = new[]
            {
                Reason("WorkflowTransitionEvidenceSatisfied", ReleaseReadinessDecisionReasonSeverities.Info),
            },
        });

        Assert.AreNotEqual(original.ReleaseReadinessDecisionRecordHash, changed.ReleaseReadinessDecisionRecordHash);
    }

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_HashChangesWhenEvidenceReferenceChanges()
    {
        var original = ReadyRecord();
        var changed = Rehash(original with
        {
            EvidenceReferences = new[]
            {
                "release-readiness-report:changed",
                "workflow-transition-record:workflow-transition-record-1",
            },
        });

        Assert.AreNotEqual(original.ReleaseReadinessDecisionRecordHash, changed.ReleaseReadinessDecisionRecordHash);
    }

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_HashMismatchRejected()
        => AssertInvalid(
            ReadyRecord() with { ReleaseReadinessDecisionRecordHash = Sha("different") },
            "ReleaseReadinessDecisionRecordHashMismatch");

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_StaticProductionFileHasNoForbiddenRuntimeSurface()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "IronDev.Core",
            "Governance",
            "ReleaseReadinessDecisionRecord.cs"));

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
            "WorkflowContinuationExecutor",
            "GovernedWorkflowContinuationService",
            "ReleaseReadinessReportBuilder().Build",
            "ControlledSourceApplyExecutor",
            "ControlledRollbackExecutor",
            "AgentDispatch",
            "ModelProvider",
            "ToolInvoker",
            "PromoteMemory",
            "ActivateRetrieval",
            "Weaviate",
            "Embedding",
        };

        foreach (var marker in forbidden)
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.Ordinal), $"Forbidden marker found: {marker}");
        }
    }

    [TestMethod]
    public void ReleaseReadinessDecisionRecord_ReceiptStatesBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "Docs",
            "receipts",
            "PR217_RELEASE_READINESS_DECISION_RECORD_CONTRACT.md"));

        StringAssert.Contains(receipt, "PR217 adds release-readiness decision record contract only.");
        StringAssert.Contains(receipt, "PR217 does not decide release readiness.");
        StringAssert.Contains(receipt, "ReadyEvidenceSatisfied does not mean release approved.");
        StringAssert.Contains(receipt, "Human review remains required for release approval, deployment, and merge.");
        StringAssert.Contains(receipt, "PR217 defines the release-readiness decision receipt. It does not decide readiness.");
    }

    private static void AssertInvalid(ReleaseReadinessDecisionRecord record, string expectedCode)
    {
        var result = ReleaseReadinessDecisionRecordValidation.Validate(record);

        Assert.IsFalse(result.IsValid, "Record should be invalid.");
        Assert.IsTrue(
            result.Issues.Any(issue => issue.Code == expectedCode),
            $"Expected {expectedCode}. Actual: {Explain(result)}");
    }

    private static ReleaseReadinessDecisionRecord ReadyRecord()
    {
        var record = new ReleaseReadinessDecisionRecord
        {
            ReleaseReadinessDecisionRecordId = DecisionRecordId,
            ProjectId = ProjectId,
            ReleaseReadinessReportId = ReportId,
            ReleaseReadinessReportHash = Sha("release-readiness-report"),
            WorkflowRunId = "workflow-run-1",
            WorkflowStepId = "workflow-step-1",
            SubjectKind = "ReleasePackage",
            SubjectId = "release-package-1",
            SubjectHash = Sha("release-package"),
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
            Reasons = new[]
            {
                Reason("ReportComplete", ReleaseReadinessDecisionReasonSeverities.Info),
                Reason("WorkflowTransitionEvidenceSatisfied", ReleaseReadinessDecisionReasonSeverities.Info),
                Reason("HumanReviewRequiredForReleaseApproval", ReleaseReadinessDecisionReasonSeverities.Warning),
            },
            EvidenceReferences = new[]
            {
                "release-readiness-report:release-readiness-report-1",
                "workflow-transition-record:workflow-transition-record-1",
            },
            BoundaryMaxims = new[]
            {
                "Decision record is not release approval.",
                "Decision record is not deployment approval.",
                "Decision record is not merge approval.",
                "Human review remains required.",
            },
            DecidedAtUtc = DecidedAt,
            ReleaseReadinessDecisionRecordHash = Sha("temporary"),
        };

        return Rehash(record);
    }

    private static ReleaseReadinessDecisionRecord Rehash(ReleaseReadinessDecisionRecord record)
        => record with
        {
            ReleaseReadinessDecisionRecordHash = ReleaseReadinessDecisionRecordHashing.ComputeHash(record),
        };

    private static ReleaseReadinessDecisionReason Reason(string code, string severity)
        => new()
        {
            Code = code,
            Severity = severity,
            Field = "ReleaseReadinessReport",
            Message = $"{code} recorded as evidence only.",
        };

    private static string Sha(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Explain(ReleaseReadinessDecisionRecordValidationResult result)
        => string.Join("; ", result.Issues.Select(issue => $"{issue.Code}:{issue.Field}:{issue.Message}"));
}
