using System.Security.Cryptography;
using System.Text;

namespace IronDev.UnitTests.Release;

internal static class ReleaseReadinessGateEvaluatorTestFixtures
{
    internal static readonly Guid ReleaseReadinessGateRequestId =
        Guid.Parse("61b6c6d4-9364-4ca1-af08-09e2de457601");

    internal static readonly Guid ProjectId =
        Guid.Parse("b20a5d6e-1c2b-4e94-8562-f2ac7c98b601");

    internal static readonly Guid ReleaseReadinessReportId =
        Guid.Parse("d2751547-b185-4822-a11f-7628562b5601");

    internal static readonly Guid ReleaseReadinessReportRequestId =
        Guid.Parse("d7d5fc58-cd86-4085-a70b-bec13cf8b601");

    internal static readonly Guid AcceptedApprovalId =
        Guid.Parse("20ac7fb8-9d2e-4b9d-bd29-72a3e3f4b601");

    internal static readonly Guid PolicySatisfactionId =
        Guid.Parse("31421eac-e919-444d-a3f4-ce83f950b601");

    internal static readonly Guid SourceApplyRequestId =
        Guid.Parse("70dc0ccb-48eb-4cb6-bd23-19388a32b601");

    internal static readonly Guid SourceApplyReceiptId =
        Guid.Parse("c6771f28-6a5a-4f78-a24c-85d0215ab601");

    internal static readonly Guid WorkflowContinuationGateEvaluationId =
        Guid.Parse("dce3971e-5094-40e6-a2e4-46712e31b601");

    internal static readonly Guid WorkflowTransitionRecordId =
        Guid.Parse("a102d527-4791-4a19-8a82-76d4440cb601");

    internal static readonly Guid RollbackExecutionReceiptId =
        Guid.Parse("a0d9acb7-8330-4e79-817e-e8fae063b601");

    internal static readonly Guid RollbackExecutionAuditReportId =
        Guid.Parse("d0fbb1c7-3027-4095-9641-27c1f217b601");

    internal static readonly DateTimeOffset RequestedAtUtc =
        new(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);

    internal const string WorkflowRunId = "workflow:g06";
    internal const string WorkflowStepId = "step:g06";
    internal const string SubjectKind = "ReleasePackage";
    internal const string SubjectId = "release-package:g06";

    internal static readonly string SubjectHash = H("release-package:g06");
    internal static readonly string AcceptedApprovalHash = H("accepted-approval:g06");
    internal static readonly string PolicySatisfactionHash = H("policy-satisfaction:g06");
    internal static readonly string SourceApplyRequestHash = H("source-apply-request:g06");
    internal static readonly string SourceApplyReceiptHash = H("source-apply-receipt:g06");
    internal static readonly string WorkflowContinuationGateEvaluationHash = H("workflow-continuation-gate:g06");
    internal static readonly string WorkflowTransitionRecordHash = H("workflow-transition-record:g06");
    internal static readonly string RollbackExecutionReceiptHash = H("rollback-execution-receipt:g06");
    internal static readonly string RollbackExecutionAuditReportHash = H("rollback-execution-audit:g06");

    internal static ReleaseReadinessGateRequest Request(
        ReleaseReadinessReport? report = null,
        IReadOnlyList<string>? evidenceReferences = null,
        IReadOnlyList<string>? boundaryMaxims = null) =>
        new()
        {
            ReleaseReadinessGateRequestId = ReleaseReadinessGateRequestId,
            ProjectId = ProjectId,
            ReleaseReadinessReport = report ?? CompleteReport(),
            RequestedAtUtc = RequestedAtUtc,
            EvidenceReferences = evidenceReferences ?? ["release-readiness-report:g06", "approval:evidence:g06", "policy:evidence:g06"],
            BoundaryMaxims = boundaryMaxims ?? ["Release readiness gate evaluator is not release approval."],
            Boundary = ReleaseReadinessGateBoundaryText.Boundary
        };

    internal static ReleaseReadinessDecisionRecord Evaluate(Func<ReleaseReadinessGateRequest, ReleaseReadinessGateRequest>? mutate = null)
    {
        var request = Request();
        if (mutate is not null)
        {
            request = mutate(request);
        }

        return new ReleaseReadinessGateEvaluator().Evaluate(request);
    }

    internal static ReleaseReadinessReport CompleteReport() =>
        Rehash(new ReleaseReadinessReport
        {
            ReleaseReadinessReportId = ReleaseReadinessReportId,
            ProjectId = ProjectId,
            ReleaseReadinessReportRequestId = ReleaseReadinessReportRequestId,
            Status = ReleaseReadinessReportStatuses.Complete,
            WorkflowRunId = WorkflowRunId,
            WorkflowStepId = WorkflowStepId,
            SubjectKind = SubjectKind,
            SubjectId = SubjectId,
            SubjectHash = SubjectHash,
            AcceptedApprovalId = AcceptedApprovalId,
            AcceptedApprovalHash = AcceptedApprovalHash,
            PolicySatisfactionId = PolicySatisfactionId,
            PolicySatisfactionHash = PolicySatisfactionHash,
            SourceApplyRequestId = SourceApplyRequestId,
            SourceApplyRequestHash = SourceApplyRequestHash,
            SourceApplyReceiptId = SourceApplyReceiptId,
            SourceApplyReceiptHash = SourceApplyReceiptHash,
            RollbackExecutionReceiptId = null,
            RollbackExecutionReceiptHash = null,
            RollbackExecutionAuditReportId = null,
            RollbackExecutionAuditReportHash = null,
            WorkflowContinuationGateEvaluationId = WorkflowContinuationGateEvaluationId,
            WorkflowContinuationGateEvaluationHash = WorkflowContinuationGateEvaluationHash,
            WorkflowTransitionRecordId = WorkflowTransitionRecordId,
            WorkflowTransitionRecordHash = WorkflowTransitionRecordHash,
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
            Findings = [Finding("ReportEvidenceComplete", ReleaseReadinessFindingSeverities.Info, "Report evidence complete.")],
            EvidenceReferences = ["release-readiness-report:g06", "workflow-transition-record:g06"],
            BoundaryMaxims = ["Release readiness report is evidence summary only.", "Release readiness report is not release approval."],
            ReportedAtUtc = RequestedAtUtc.AddMinutes(-5),
            ReleaseReadinessReportHash = "sha256:pending",
            Boundary = ReleaseReadinessReportBoundaryText.Boundary
        });

    internal static ReleaseReadinessReport Rehash(ReleaseReadinessReport report) =>
        report with { ReleaseReadinessReportHash = ReleaseReadinessReportHashing.ComputeReportHash(report) };

    internal static ReleaseReadinessReportFinding Finding(
        string code,
        string severity,
        string message = "Release readiness fact recorded as evidence only.") =>
        new()
        {
            Code = code,
            Severity = severity,
            Field = "ReleaseReadinessReport",
            Message = message
        };

    internal static void AssertReady(ReleaseReadinessDecisionRecord decision)
    {
        Assert.AreEqual(ReleaseReadinessDecisionStatuses.ReadyEvidenceSatisfied, decision.DecisionStatus);
        Assert.IsTrue(decision.ReleaseReadinessEvidenceSatisfied);
        Assert.IsFalse(decision.Reasons.Any(reason => reason.Severity == ReleaseReadinessDecisionReasonSeverities.Blocking));
    }

    internal static void AssertBlocked(ReleaseReadinessDecisionRecord decision, string status, string expectedReason)
    {
        Assert.AreEqual(status, decision.DecisionStatus);
        Assert.IsFalse(decision.ReleaseReadinessEvidenceSatisfied);
        AssertReason(decision, expectedReason);
    }

    internal static void AssertReason(ReleaseReadinessDecisionRecord decision, string expectedCode) =>
        Assert.IsTrue(
            decision.Reasons.Any(reason => string.Equals(reason.Code, expectedCode, StringComparison.Ordinal)),
            expectedCode);

    internal static void AssertAllAuthorityFalse(ReleaseReadinessDecisionRecord decision)
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

    internal static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    internal static string H(string value) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()}";
}
