using System.Reflection;
using System.Text.Json;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("SourceApplyRequestValidation")]
public sealed class SourceApplyRequestValidationTests
{
    private static readonly DateTimeOffset RequestedAtUtc = new(2026, 6, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAtUtc = RequestedAtUtc.AddHours(2);

    [TestMethod]
    public void SourceApplyRequestValidation_AcceptsCompleteRequest()
    {
        var request = ValidRequest();
        var result = SourceApplyRequestValidation.Validate(request);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues.Select(issue => issue.Code)));
        Assert.AreEqual(request.ProjectId, request.SourceApplyGateEvaluation.ProjectId);
        Assert.AreEqual(request.SourceApplyGateEvaluationId, request.SourceApplyGateEvaluation.SourceApplyGateEvaluationId);
        Assert.AreEqual(request.SourceApplyGateEvaluationHash, request.SourceApplyGateEvaluation.SourceApplyGateEvaluationHash);
        Assert.AreEqual(request.AcceptedApprovalId, request.SourceApplyGateEvaluation.AcceptedApprovalId);
        Assert.AreEqual(request.PolicySatisfactionId, request.SourceApplyGateEvaluation.PolicySatisfactionId);
        Assert.AreEqual(request.ControlledDryRunRequestId, request.SourceApplyGateEvaluation.ControlledDryRunRequestId);
        Assert.AreEqual(request.PatchArtifactId, request.SourceApplyGateEvaluation.PatchArtifactId);
        Assert.AreEqual(request.RollbackSupportReceiptId, request.SourceApplyGateEvaluation.RollbackSupportReceiptId);
        Assert.AreEqual(request.SubjectHash, request.SourceApplyGateEvaluation.SubjectHash);
        Assert.AreEqual(request.SourceBaselineHash, request.SourceApplyGateEvaluation.SourceBaselineHash);
        Assert.AreEqual(request.ExpectedBranch, request.SourceApplyGateEvaluation.ExpectedBranch);
        Assert.AreEqual(request.ExpectedCleanWorktreeHash, request.SourceApplyGateEvaluation.ExpectedCleanWorktreeHash);
        CollectionAssert.AreEqual(new[] { "src/feature.cs" }, request.FileOperations.Select(operation => operation.Path).ToArray());
        StringAssert.Contains(request.Boundary, "not source apply");
    }

    [TestMethod]
    public void SourceApplyRequestValidation_RejectsUnsatisfiedSourceApplyGate()
    {
        var result = SourceApplyRequestValidation.Validate(ValidRequest() with
        {
            SourceApplyGateSatisfied = false,
            SourceApplyGateEvaluation = ValidGateEvidence() with { Satisfied = false }
        });

        AssertHasIssue(result, "SOURCE_APPLY_GATE_UNSATISFIED");
        AssertHasIssue(result, "SOURCE_APPLY_GATE_EVIDENCE_UNSATISFIED");
    }

    [TestMethod]
    public void SourceApplyRequestValidation_RejectsMissingRequiredEvidence()
    {
        var result = SourceApplyRequestValidation.Validate(ValidRequest() with
        {
            SourceApplyGateEvaluationId = Guid.Empty,
            SourceApplyGateEvaluationHash = " ",
            SourceApplyGateEvaluation = null!,
            AcceptedApprovalId = Guid.Empty,
            AcceptedApprovalHash = " ",
            PolicySatisfactionId = Guid.Empty,
            PolicySatisfactionHash = " ",
            ControlledDryRunRequestId = Guid.Empty,
            DryRunExecutionAuditId = Guid.Empty,
            DryRunAuditHash = " ",
            DryRunReceiptHash = " ",
            PatchArtifactId = Guid.Empty,
            PatchHash = " ",
            ChangeSetHash = " ",
            RollbackSupportReceiptId = Guid.Empty,
            RollbackSupportReceiptHash = " ",
            RollbackPlanId = Guid.Empty,
            RollbackPlanHash = " ",
            RollbackGateEvaluationHash = " ",
            SourceBaselineHash = " ",
            ExpectedBranch = " ",
            ExpectedCleanWorktreeHash = " ",
            SourceApplyRequestHash = " ",
            EvidenceReferences = [],
            BoundaryMaxims = []
        });

        AssertHasIssue(result, "SOURCE_APPLY_GATE_EVIDENCE_REQUIRED");
        AssertHasIssue(result, "ACCEPTED_APPROVAL_ID_REQUIRED");
        AssertHasIssue(result, "POLICY_SATISFACTION_ID_REQUIRED");
        AssertHasIssue(result, "CONTROLLED_DRY_RUN_REQUEST_ID_REQUIRED");
        AssertHasIssue(result, "PATCH_ARTIFACT_ID_REQUIRED");
        AssertHasIssue(result, "ROLLBACK_SUPPORT_RECEIPT_ID_REQUIRED");
        AssertHasIssue(result, "SOURCE_BASELINE_HASH_REQUIRED");
        AssertHasIssue(result, "EXPECTED_BRANCH_REQUIRED");
        AssertHasIssue(result, "EXPECTED_CLEAN_WORKTREE_HASH_REQUIRED");
        AssertHasIssue(result, "SOURCE_APPLY_REQUEST_HASH_REQUIRED");
        AssertHasIssue(result, "EVIDENCE_REFERENCES_REQUIRED");
        AssertHasIssue(result, "BOUNDARY_MAXIMS_REQUIRED");
    }

    [TestMethod]
    public void SourceApplyRequestValidation_RejectsGateBindingMismatches()
    {
        var result = SourceApplyRequestValidation.Validate(ValidRequest() with
        {
            SourceApplyGateEvaluation = ValidGateEvidence() with
            {
                ProjectId = Guid.NewGuid(),
                SourceApplyGateEvaluationId = Guid.NewGuid(),
                SourceApplyGateEvaluationHash = "sha256:gate-other",
                AcceptedApprovalId = Guid.NewGuid(),
                AcceptedApprovalHash = "sha256:approval-other",
                PolicySatisfactionId = Guid.NewGuid(),
                PolicySatisfactionHash = "sha256:policy-other",
                ControlledDryRunRequestId = Guid.NewGuid(),
                DryRunExecutionAuditId = Guid.NewGuid(),
                DryRunAuditHash = "sha256:dry-run-audit-other",
                DryRunReceiptHash = "sha256:dry-run-receipt-other",
                PatchArtifactId = Guid.NewGuid(),
                PatchHash = "sha256:patch-other",
                ChangeSetHash = "sha256:change-set-other",
                RollbackSupportReceiptId = Guid.NewGuid(),
                RollbackSupportReceiptHash = "sha256:rollback-support-other",
                RollbackPlanId = Guid.NewGuid(),
                RollbackPlanHash = "sha256:rollback-plan-other",
                RollbackGateEvaluationHash = "sha256:rollback-gate-other",
                SubjectKind = "OtherSubject",
                SubjectId = "other-subject",
                SubjectHash = "sha256:other-subject",
                SourceSnapshotReference = "snapshot:other",
                SourceBaselineHash = "sha256:source-baseline-other",
                WorkspaceBoundaryHash = "sha256:workspace-other",
                ExpectedBranch = "release/other",
                ExpectedCleanWorktreeHash = "sha256:clean-other"
            }
        });

        AssertHasIssue(result, "PROJECT_ID_MISMATCH");
        AssertHasIssue(result, "SOURCE_APPLY_GATE_EVALUATION_ID_MISMATCH");
        AssertHasIssue(result, "SOURCE_APPLY_GATE_EVALUATION_HASH_MISMATCH");
        AssertHasIssue(result, "ACCEPTED_APPROVAL_ID_MISMATCH");
        AssertHasIssue(result, "POLICY_SATISFACTION_ID_MISMATCH");
        AssertHasIssue(result, "CONTROLLED_DRY_RUN_REQUEST_ID_MISMATCH");
        AssertHasIssue(result, "PATCH_ARTIFACT_ID_MISMATCH");
        AssertHasIssue(result, "ROLLBACK_SUPPORT_RECEIPT_ID_MISMATCH");
        AssertHasIssue(result, "ROLLBACK_PLAN_ID_MISMATCH");
        AssertHasIssue(result, "SUBJECT_KIND_MISMATCH");
        AssertHasIssue(result, "SOURCE_SNAPSHOT_REFERENCE_MISMATCH");
        AssertHasIssue(result, "WORKSPACE_BOUNDARY_HASH_MISMATCH");
        AssertHasIssue(result, "EXPECTED_BRANCH_MISMATCH");
    }

    [TestMethod]
    public void SourceApplyRequestValidation_RejectsInvalidExpiry()
    {
        var result = SourceApplyRequestValidation.Validate(ValidRequest() with
        {
            ExpiresAtUtc = RequestedAtUtc,
            SourceApplyGateEvaluation = ValidGateEvidence() with { ExpiresAtUtc = RequestedAtUtc.AddMinutes(-1) }
        });

        AssertHasIssue(result, "EXPIRES_AT_UTC_INVALID");
        AssertHasIssue(result, "SOURCE_APPLY_GATE_EVIDENCE_EXPIRED");
    }

    [TestMethod]
    public void SourceApplyRequestValidation_RejectsEmptyFileOperations()
    {
        var result = SourceApplyRequestValidation.Validate(ValidRequest() with { FileOperations = [] });

        AssertHasIssue(result, "FILE_OPERATIONS_REQUIRED");
    }

    [TestMethod]
    public void SourceApplyRequestValidation_RejectsInvalidFileOperationKind()
    {
        var result = SourceApplyRequestValidation.Validate(ValidRequest() with
        {
            FileOperations = [ValidOperation() with { OperationKind = "RunGitApply" }]
        });

        AssertHasIssue(result, "FILE_OPERATION_KIND_INVALID");
    }

    [TestMethod]
    public void SourceApplyRequestValidation_RejectsUnsafeFilePath()
    {
        foreach (var path in new[] { "../x.cs", @"C:\repo\x.cs", @"\\server\share\x.cs", ".git/config", "src/.git/config", "/" })
        {
            var result = SourceApplyRequestValidation.Validate(ValidRequest() with
            {
                FileOperations = [ValidOperation() with { Path = path }]
            });

            AssertHasIssue(result, "FILE_OPERATION_PATH_UNSAFE");
        }
    }

    [TestMethod]
    public void SourceApplyRequestValidation_RejectsDuplicateFileOperations()
    {
        var operation = ValidOperation();
        var result = SourceApplyRequestValidation.Validate(ValidRequest() with
        {
            FileOperations =
            [
                operation,
                operation with { OperationHash = "sha256:operation-duplicate" }
            ]
        });

        AssertHasIssue(result, "FILE_OPERATION_DUPLICATE");
    }

    [TestMethod]
    public void SourceApplyRequestValidation_RejectsMissingOperationHash()
    {
        var result = SourceApplyRequestValidation.Validate(ValidRequest() with
        {
            FileOperations = [ValidOperation() with { OperationHash = " " }]
        });

        AssertHasIssue(result, "FILE_OPERATION_HASH_REQUIRED");
    }

    [TestMethod]
    public void SourceApplyRequestValidation_RejectsPrivateRawMaterial()
    {
        var result = SourceApplyRequestValidation.Validate(ValidRequest() with
        {
            ExpectedBranch = "raw prompt leaked",
            EvidenceReferences = ["evidence:safe", "chain-of-thought leaked"]
        });
        var serialized = JsonSerializer.Serialize(result);

        AssertHasIssue(result, "PRIVATE_OR_RAW_MATERIAL_REJECTED");
        Assert.IsFalse(serialized.Contains("raw prompt leaked", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains("chain-of-thought leaked", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void SourceApplyRequestValidation_RejectsAuthorityClaims()
    {
        var result = SourceApplyRequestValidation.Validate(ValidRequest() with
        {
            BoundaryMaxims = ["source applied and release ready"]
        });

        AssertHasIssue(result, "AUTHORITY_CLAIM_REJECTED");
    }

    [TestMethod]
    public void SourceApplyRequestValidation_DoesNotExposeExecutionAuthority()
    {
        var methodNames = typeof(SourceApplyRequestValidation)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.Name)
            .ToArray();
        CollectionAssert.AreEquivalent(new[] { nameof(SourceApplyRequestValidation.Validate) }, methodNames);

        var publicNames = typeof(SourceApplyRequest)
            .GetProperties()
            .Select(property => property.Name)
            .Concat(typeof(SourceApplyRequestFileOperation).GetProperties().Select(property => property.Name))
            .ToArray();

        foreach (var forbidden in new[]
        {
            "CanApplySource",
            "SourceApplyApproved",
            "SourceApplied",
            "MutationAllowed",
            "WorkflowCanContinue",
            "ReleaseReady",
            "Execute",
            "ApplyNow",
            "CommitChanges"
        })
        {
            Assert.IsFalse(publicNames.Any(name => name.Equals(forbidden, StringComparison.Ordinal)), forbidden);
        }
    }

    [TestMethod]
    public void SourceApplyRequestValidation_ProductionFilesDoNotAddGitProcessFilesystemRuntimeSqlApiCliUi()
    {
        foreach (var path in ProductionFiles())
        {
            var text = File.ReadAllText(path);
            foreach (var forbidden in new[]
            {
                "SqlConnection",
                "DbCommand",
                "ControllerBase",
                "MapPost",
                "CommandLine",
                "ProcessStartInfo",
                "System.Diagnostics.Process",
                "File.Write",
                "File.Read",
                "Directory.CreateDirectory",
                "IHostedService",
                "BackgroundService",
                "SourceApplyExecutor",
                "RollbackExecutor",
                "CreateSourceApplyReceipt",
                "WorkflowContinuationService",
                "ReleaseReadiness",
                "MemoryPromotion",
                "RetrievalActivation"
            })
            {
                Assert.IsFalse(text.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"{Path.GetFileName(path)} contains forbidden token {forbidden}");
            }
        }
    }

    [TestMethod]
    public void SourceApplyRequestValidation_ReceiptDocumentsBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "PR200_SOURCE_APPLY_REQUEST_CONTRACT.md"));

        StringAssert.Contains(receipt, "PR200 adds the Core source apply request contract and validation only.");
        StringAssert.Contains(receipt, "PR200 does not add source apply.");
        StringAssert.Contains(receipt, "PR200 does not mutate source.");
        StringAssert.Contains(receipt, "PR200 does not call git.");
        StringAssert.Contains(receipt, "SourceApplyRequest is not source apply.");
        StringAssert.Contains(receipt, "does not launch");
    }

    private static SourceApplyRequest ValidRequest() => new()
    {
        SourceApplyRequestId = Ids.SourceApplyRequestId,
        ProjectId = Ids.ProjectId,
        SourceApplyGateEvaluationId = Ids.SourceApplyGateEvaluationId,
        SourceApplyGateEvaluationHash = "sha256:source-apply-gate",
        SourceApplyGateSatisfied = true,
        SourceApplyGateEvaluation = ValidGateEvidence(),
        AcceptedApprovalId = Ids.AcceptedApprovalId,
        AcceptedApprovalHash = "sha256:accepted-approval",
        PolicySatisfactionId = Ids.PolicySatisfactionId,
        PolicySatisfactionHash = "sha256:policy-satisfaction",
        ControlledDryRunRequestId = Ids.ControlledDryRunRequestId,
        DryRunExecutionAuditId = Ids.DryRunExecutionAuditId,
        DryRunAuditHash = "sha256:dry-run-audit",
        DryRunReceiptHash = "sha256:dry-run-receipt",
        PatchArtifactId = Ids.PatchArtifactId,
        PatchHash = "sha256:patch",
        ChangeSetHash = "sha256:change-set",
        RollbackSupportReceiptId = Ids.RollbackSupportReceiptId,
        RollbackSupportReceiptHash = "sha256:rollback-support",
        RollbackPlanId = Ids.RollbackPlanId,
        RollbackPlanHash = "sha256:rollback-plan",
        RollbackGateEvaluationHash = "sha256:rollback-gate",
        SubjectKind = "PatchArtifact",
        SubjectId = "patch-artifact:1",
        SubjectHash = "sha256:subject",
        SourceSnapshotReference = "snapshot:source",
        SourceBaselineHash = "sha256:source-baseline",
        WorkspaceBoundaryHash = "sha256:workspace-boundary",
        ExpectedBranch = "main",
        ExpectedCleanWorktreeHash = "sha256:clean-worktree",
        FileOperations = [ValidOperation()],
        RequestedAtUtc = RequestedAtUtc,
        ExpiresAtUtc = ExpiresAtUtc,
        SourceApplyRequestHash = "sha256:source-apply-request",
        EvidenceReferences = ["source-apply-gate:1", "patch-artifact:1", "rollback-support:1"],
        BoundaryMaxims = ["source apply request is not source apply"],
        Boundary = SourceApplyRequestBoundaryText.Boundary
    };

    private static SourceApplyRequestGateEvaluationEvidence ValidGateEvidence() => new()
    {
        SourceApplyGateEvaluationId = Ids.SourceApplyGateEvaluationId,
        SourceApplyGateEvaluationHash = "sha256:source-apply-gate",
        Satisfied = true,
        ProjectId = Ids.ProjectId,
        AcceptedApprovalId = Ids.AcceptedApprovalId,
        AcceptedApprovalHash = "sha256:accepted-approval",
        PolicySatisfactionId = Ids.PolicySatisfactionId,
        PolicySatisfactionHash = "sha256:policy-satisfaction",
        ControlledDryRunRequestId = Ids.ControlledDryRunRequestId,
        DryRunExecutionAuditId = Ids.DryRunExecutionAuditId,
        DryRunAuditHash = "sha256:dry-run-audit",
        DryRunReceiptHash = "sha256:dry-run-receipt",
        PatchArtifactId = Ids.PatchArtifactId,
        PatchHash = "sha256:patch",
        ChangeSetHash = "sha256:change-set",
        RollbackSupportReceiptId = Ids.RollbackSupportReceiptId,
        RollbackSupportReceiptHash = "sha256:rollback-support",
        RollbackPlanId = Ids.RollbackPlanId,
        RollbackPlanHash = "sha256:rollback-plan",
        RollbackGateEvaluationHash = "sha256:rollback-gate",
        SubjectKind = "PatchArtifact",
        SubjectId = "patch-artifact:1",
        SubjectHash = "sha256:subject",
        SourceSnapshotReference = "snapshot:source",
        SourceBaselineHash = "sha256:source-baseline",
        WorkspaceBoundaryHash = "sha256:workspace-boundary",
        ExpectedBranch = "main",
        ExpectedCleanWorktreeHash = "sha256:clean-worktree",
        ExpiresAtUtc = ExpiresAtUtc,
        EvidenceReferences = ["source-apply-gate:evidence"],
        BoundaryMaxims = ["source apply gate is checklist evidence only"],
        Boundary = SourceApplyGateBoundaryText.Boundary
    };

    private static SourceApplyRequestFileOperation ValidOperation() => new()
    {
        Path = "src/feature.cs",
        OperationKind = SourceApplyRequestFileOperationKinds.ModifyFile,
        BeforeContentHash = "sha256:before",
        AfterContentHash = "sha256:after",
        DiffHash = "sha256:diff",
        PatchArtifactChangeHash = "sha256:patch-change",
        OperationHash = "sha256:operation"
    };

    private static void AssertHasIssue(SourceApplyRequestValidationResult result, string code) =>
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == code), $"Expected issue {code}. Actual: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");

    private static IReadOnlyList<string> ProductionFiles() =>
    [
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "SourceApplyRequestModels.cs"),
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "SourceApplyRequestValidation.cs")
    ];

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }

    private static class Ids
    {
        public static readonly Guid SourceApplyRequestId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        public static readonly Guid ProjectId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        public static readonly Guid SourceApplyGateEvaluationId = Guid.Parse("30000000-0000-0000-0000-000000000001");
        public static readonly Guid AcceptedApprovalId = Guid.Parse("40000000-0000-0000-0000-000000000001");
        public static readonly Guid PolicySatisfactionId = Guid.Parse("50000000-0000-0000-0000-000000000001");
        public static readonly Guid ControlledDryRunRequestId = Guid.Parse("60000000-0000-0000-0000-000000000001");
        public static readonly Guid DryRunExecutionAuditId = Guid.Parse("70000000-0000-0000-0000-000000000001");
        public static readonly Guid PatchArtifactId = Guid.Parse("80000000-0000-0000-0000-000000000001");
        public static readonly Guid RollbackSupportReceiptId = Guid.Parse("90000000-0000-0000-0000-000000000001");
        public static readonly Guid RollbackPlanId = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    }
}
