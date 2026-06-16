using System.Reflection;
using System.Text.Json;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("SourceApplyGateEvaluator")]
public sealed class SourceApplyGateEvaluatorTests
{
    private static readonly DateTimeOffset EvaluatedAtUtc = new(2026, 6, 17, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAtUtc = EvaluatedAtUtc.AddHours(2);

    [TestMethod]
    public void SourceApplyGateEvaluator_ExposesPureEvaluationContract()
    {
        AssertHasProperty(typeof(SourceApplyGateEvaluationRequest), nameof(SourceApplyGateEvaluationRequest.AcceptedApproval));
        AssertHasProperty(typeof(SourceApplyGateEvaluationRequest), nameof(SourceApplyGateEvaluationRequest.PolicySatisfaction));
        AssertHasProperty(typeof(SourceApplyGateEvaluationRequest), nameof(SourceApplyGateEvaluationRequest.ControlledDryRun));
        AssertHasProperty(typeof(SourceApplyGateEvaluationRequest), nameof(SourceApplyGateEvaluationRequest.PatchArtifact));
        AssertHasProperty(typeof(SourceApplyGateEvaluationRequest), nameof(SourceApplyGateEvaluationRequest.RollbackSupport));
        AssertHasProperty(typeof(SourceApplyGateEvaluationResult), nameof(SourceApplyGateEvaluationResult.Satisfied));
        AssertHasProperty(typeof(SourceApplyGateEvaluationResult), nameof(SourceApplyGateEvaluationResult.Issues));

        var methods = typeof(SourceApplyGateEvaluator)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.Name)
            .ToArray();

        CollectionAssert.AreEquivalent(new[] { nameof(SourceApplyGateEvaluator.Evaluate) }, methods);
    }

    [TestMethod]
    public void SourceApplyGateEvaluator_SatisfiedWhenEvidenceChainIsConsistent()
    {
        var request = ValidRequest();
        var result = SourceApplyGateEvaluator.Evaluate(request);

        Assert.IsTrue(result.Satisfied, string.Join(", ", result.Issues.Select(issue => issue.Code)));
        Assert.AreEqual(request.ProjectId, result.ProjectId);
        Assert.AreEqual(request.PatchArtifactId, result.PatchArtifactId);
        Assert.AreEqual(request.PatchHash, result.PatchHash);
        Assert.AreEqual(request.ChangeSetHash, result.ChangeSetHash);
        Assert.AreEqual(request.RollbackSupportReceiptId, result.RollbackSupportReceiptId);
        Assert.AreEqual(request.SourceBaselineHash, result.SourceBaselineHash);
        Assert.AreEqual(request.ExpectedBranch, result.ExpectedBranch);
        Assert.AreEqual(request.ExpectedCleanWorktreeHash, result.ExpectedCleanWorktreeHash);
        CollectionAssert.AreEqual(request.EvidenceReferences.ToArray(), result.EvidenceReferences.ToArray());
        CollectionAssert.AreEqual(request.BoundaryMaxims.ToArray(), result.BoundaryMaxims.ToArray());
        StringAssert.Contains(result.Boundary, "not source apply");
        StringAssert.Contains(result.Boundary, "does not execute git");
    }

    [TestMethod]
    public void SourceApplyGateEvaluator_ResultDoesNotGrantMutationAuthority()
    {
        var publicNames = typeof(SourceApplyGateEvaluationRequest)
            .GetProperties()
            .Select(property => property.Name)
            .Concat(typeof(SourceApplyGateEvaluationResult).GetProperties().Select(property => property.Name))
            .Concat(Enum.GetNames<SourceApplyGateAuthorityForbiddenNames>())
            .ToArray();

        foreach (var forbidden in new[]
        {
            "CanApplySource",
            "SourceApplyApproved",
            "SourceApplied",
            "MutationAllowed",
            "WorkflowCanContinue",
            "ReleaseReady"
        })
        {
            Assert.IsFalse(typeof(SourceApplyGateEvaluationResult).GetProperties().Any(property => property.Name.Equals(forbidden, StringComparison.Ordinal)), forbidden);
            Assert.IsFalse(publicNames.Where(name => name != nameof(SourceApplyGateAuthorityForbiddenNames.SourceApplyApproved)).Any(name => name.Equals(forbidden, StringComparison.Ordinal)), forbidden);
        }
    }

    [TestMethod]
    public void SourceApplyGateEvaluator_RejectsMissingEvidence()
    {
        var result = SourceApplyGateEvaluator.Evaluate(ValidRequest() with
        {
            AcceptedApproval = null!,
            PolicySatisfaction = null!,
            ControlledDryRun = null!,
            PatchArtifact = null!,
            RollbackSupport = null!,
            EvidenceReferences = [],
            BoundaryMaxims = []
        });

        AssertHasIssue(result, "ACCEPTED_APPROVAL_EVIDENCE_REQUIRED");
        AssertHasIssue(result, "POLICY_SATISFACTION_EVIDENCE_REQUIRED");
        AssertHasIssue(result, "CONTROLLED_DRY_RUN_EVIDENCE_REQUIRED");
        AssertHasIssue(result, "PATCH_ARTIFACT_EVIDENCE_REQUIRED");
        AssertHasIssue(result, "ROLLBACK_SUPPORT_EVIDENCE_REQUIRED");
        AssertHasIssue(result, "EVIDENCE_REFERENCES_REQUIRED");
        AssertHasIssue(result, "BOUNDARY_MAXIMS_REQUIRED");
    }

    [TestMethod]
    public void SourceApplyGateEvaluator_RejectsProjectMismatch()
    {
        var result = SourceApplyGateEvaluator.Evaluate(ValidRequest() with
        {
            PolicySatisfaction = ValidPolicySatisfaction() with { ProjectId = Guid.NewGuid() }
        });

        AssertHasIssue(result, "PROJECT_ID_MISMATCH");
    }

    [TestMethod]
    public void SourceApplyGateEvaluator_RejectsAcceptedApprovalMismatch()
    {
        var result = SourceApplyGateEvaluator.Evaluate(ValidRequest() with
        {
            AcceptedApproval = ValidAcceptedApproval() with { AcceptedApprovalId = Guid.NewGuid(), AcceptedApprovalHash = "sha256:accepted-approval-other" },
            PolicySatisfaction = ValidPolicySatisfaction() with { AcceptedApprovalId = Guid.NewGuid(), AcceptedApprovalHash = "sha256:accepted-approval-policy-other" }
        });

        AssertHasIssue(result, "ACCEPTED_APPROVAL_ID_MISMATCH");
        AssertHasIssue(result, "ACCEPTED_APPROVAL_HASH_MISMATCH");
    }

    [TestMethod]
    public void SourceApplyGateEvaluator_RejectsPolicySatisfactionMismatch()
    {
        var result = SourceApplyGateEvaluator.Evaluate(ValidRequest() with
        {
            PolicySatisfaction = ValidPolicySatisfaction() with { PolicySatisfactionId = Guid.NewGuid(), PolicySatisfactionHash = "sha256:policy-other" },
            ControlledDryRun = ValidDryRun() with { PolicySatisfactionId = Guid.NewGuid(), PolicySatisfactionHash = "sha256:dryrun-policy-other" },
            PatchArtifact = ValidPatchArtifact() with { PolicySatisfactionId = Guid.NewGuid(), PolicySatisfactionHash = "sha256:patch-policy-other" }
        });

        AssertHasIssue(result, "POLICY_SATISFACTION_ID_MISMATCH");
        AssertHasIssue(result, "POLICY_SATISFACTION_HASH_MISMATCH");
    }

    [TestMethod]
    public void SourceApplyGateEvaluator_RejectsDryRunBindingMismatch()
    {
        var result = SourceApplyGateEvaluator.Evaluate(ValidRequest() with
        {
            ControlledDryRun = ValidDryRun() with
            {
                ControlledDryRunRequestId = Guid.NewGuid(),
                DryRunExecutionAuditId = Guid.NewGuid(),
                DryRunAuditHash = "sha256:dryrun-audit-other",
                DryRunReceiptHash = "sha256:dryrun-receipt-other"
            },
            PatchArtifact = ValidPatchArtifact() with
            {
                ControlledDryRunRequestId = Guid.NewGuid(),
                DryRunExecutionAuditId = Guid.NewGuid(),
                DryRunAuditHash = "sha256:patch-dryrun-audit-other",
                DryRunReceiptHash = "sha256:patch-dryrun-receipt-other"
            }
        });

        AssertHasIssue(result, "CONTROLLED_DRY_RUN_REQUEST_ID_MISMATCH");
        AssertHasIssue(result, "DRY_RUN_EXECUTION_AUDIT_ID_MISMATCH");
        AssertHasIssue(result, "DRY_RUN_AUDIT_HASH_MISMATCH");
        AssertHasIssue(result, "DRY_RUN_RECEIPT_HASH_MISMATCH");
    }

    [TestMethod]
    public void SourceApplyGateEvaluator_RejectsPatchArtifactBindingMismatch()
    {
        var result = SourceApplyGateEvaluator.Evaluate(ValidRequest() with
        {
            PatchArtifact = ValidPatchArtifact() with { PatchArtifactId = Guid.NewGuid(), PatchHash = "sha256:patch-other", ChangeSetHash = "sha256:changeset-other" },
            RollbackSupport = ValidRollbackSupport() with { PatchArtifactId = Guid.NewGuid(), PatchHash = "sha256:rollback-patch-other", ChangeSetHash = "sha256:rollback-changeset-other" }
        });

        AssertHasIssue(result, "PATCH_ARTIFACT_ID_MISMATCH");
        AssertHasIssue(result, "PATCH_HASH_MISMATCH");
        AssertHasIssue(result, "CHANGE_SET_HASH_MISMATCH");
    }

    [TestMethod]
    public void SourceApplyGateEvaluator_RejectsRollbackSupportBindingMismatch()
    {
        var result = SourceApplyGateEvaluator.Evaluate(ValidRequest() with
        {
            RollbackSupport = ValidRollbackSupport() with
            {
                RollbackSupportReceiptId = Guid.NewGuid(),
                RollbackSupportReceiptHash = "sha256:rollback-support-other",
                RollbackPlanId = Guid.NewGuid(),
                RollbackPlanHash = "sha256:rollback-plan-other",
                RollbackGateEvaluationHash = "sha256:rollback-gate-other",
                RollbackGateSatisfied = false
            }
        });

        AssertHasIssue(result, "ROLLBACK_SUPPORT_RECEIPT_ID_MISMATCH");
        AssertHasIssue(result, "ROLLBACK_SUPPORT_RECEIPT_HASH_MISMATCH");
        AssertHasIssue(result, "ROLLBACK_PLAN_ID_MISMATCH");
        AssertHasIssue(result, "ROLLBACK_PLAN_HASH_MISMATCH");
        AssertHasIssue(result, "ROLLBACK_GATE_EVALUATION_HASH_MISMATCH");
        AssertHasIssue(result, "ROLLBACK_GATE_NOT_SATISFIED");
    }

    [TestMethod]
    public void SourceApplyGateEvaluator_RejectsSubjectBindingMismatch()
    {
        var result = SourceApplyGateEvaluator.Evaluate(ValidRequest() with
        {
            PatchArtifact = ValidPatchArtifact() with
            {
                SubjectKind = "DifferentSubject",
                SubjectId = "different-subject",
                SubjectHash = "sha256:different-subject"
            }
        });

        AssertHasIssue(result, "SUBJECT_KIND_MISMATCH");
        AssertHasIssue(result, "SUBJECT_ID_MISMATCH");
        AssertHasIssue(result, "SUBJECT_HASH_MISMATCH");
    }

    [TestMethod]
    public void SourceApplyGateEvaluator_RejectsSourceWorkspaceAndBranchMismatches()
    {
        var result = SourceApplyGateEvaluator.Evaluate(ValidRequest() with
        {
            ControlledDryRun = ValidDryRun() with
            {
                SourceSnapshotReference = "snapshot:other",
                SourceBaselineHash = "sha256:baseline-other",
                WorkspaceBoundaryHash = "sha256:workspace-other",
                ExpectedBranch = "feature/other",
                ExpectedCleanWorktreeHash = "sha256:clean-other"
            }
        });

        AssertHasIssue(result, "SOURCE_SNAPSHOT_REFERENCE_MISMATCH");
        AssertHasIssue(result, "SOURCE_BASELINE_HASH_MISMATCH");
        AssertHasIssue(result, "WORKSPACE_BOUNDARY_HASH_MISMATCH");
        AssertHasIssue(result, "EXPECTED_BRANCH_MISMATCH");
        AssertHasIssue(result, "EXPECTED_CLEAN_WORKTREE_HASH_MISMATCH");
    }

    [TestMethod]
    public void SourceApplyGateEvaluator_RejectsExpiredEvidence()
    {
        var expired = EvaluatedAtUtc.AddMinutes(-1);
        var result = SourceApplyGateEvaluator.Evaluate(ValidRequest() with
        {
            AcceptedApproval = ValidAcceptedApproval() with { ExpiresAtUtc = expired },
            PolicySatisfaction = ValidPolicySatisfaction() with { ExpiresAtUtc = expired },
            ControlledDryRun = ValidDryRun() with { ExpiresAtUtc = expired },
            PatchArtifact = ValidPatchArtifact() with { ExpiresAtUtc = expired },
            RollbackSupport = ValidRollbackSupport() with { ExpiresAtUtc = expired }
        });

        AssertHasIssue(result, "ACCEPTED_APPROVAL_EXPIRED");
        AssertHasIssue(result, "POLICY_SATISFACTION_EXPIRED");
        AssertHasIssue(result, "CONTROLLED_DRY_RUN_EXPIRED");
        AssertHasIssue(result, "PATCH_ARTIFACT_EXPIRED");
        AssertHasIssue(result, "ROLLBACK_SUPPORT_EXPIRED");
    }

    [TestMethod]
    public void SourceApplyGateEvaluator_RejectsPrivateRawMaterialWithoutEchoingIt()
    {
        var result = SourceApplyGateEvaluator.Evaluate(ValidRequest() with
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
    public void SourceApplyGateEvaluator_RejectsAuthorityClaimsWithoutBlockingNegativeBoundaryText()
    {
        var safeResult = SourceApplyGateEvaluator.Evaluate(ValidRequest());
        var unsafeResult = SourceApplyGateEvaluator.Evaluate(ValidRequest() with
        {
            BoundaryMaxims = ["source applied and release ready"]
        });

        Assert.IsTrue(safeResult.Satisfied);
        AssertHasIssue(unsafeResult, "AUTHORITY_CLAIM_REJECTED");
    }

    [TestMethod]
    public void SourceApplyGateEvaluator_ProductionFilesDoNotAddRuntimeApiCliSqlUiOrMutationPaths()
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
                "RollbackExecutor",
                "SourceApplyExecutor",
                "ApplyPatch",
                "MutateSource",
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
    public void SourceApplyGateEvaluator_ReceiptDocumentsBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "PR199_SOURCE_APPLY_GATE_EVALUATOR.md"));

        StringAssert.Contains(receipt, "PR199 adds a pure Core source-apply gate evaluator.");
        StringAssert.Contains(receipt, "PR199 does not add source apply.");
        StringAssert.Contains(receipt, "PR199 does not mutate source.");
        StringAssert.Contains(receipt, "PR199 does not call git.");
        StringAssert.Contains(receipt, "Source apply gate satisfaction is not source apply.");
        StringAssert.Contains(receipt, "does not press the button");
    }

    private static SourceApplyGateEvaluationRequest ValidRequest() => new()
    {
        ProjectId = Ids.ProjectId,
        AcceptedApprovalId = Ids.AcceptedApprovalId,
        AcceptedApprovalHash = "sha256:accepted-approval",
        AcceptedApproval = ValidAcceptedApproval(),
        PolicySatisfactionId = Ids.PolicySatisfactionId,
        PolicySatisfactionHash = "sha256:policy-satisfaction",
        PolicySatisfaction = ValidPolicySatisfaction(),
        ControlledDryRunRequestId = Ids.ControlledDryRunRequestId,
        DryRunExecutionAuditId = Ids.DryRunExecutionAuditId,
        DryRunAuditHash = "sha256:dry-run-audit",
        DryRunReceiptHash = "sha256:dry-run-receipt",
        ControlledDryRun = ValidDryRun(),
        PatchArtifactId = Ids.PatchArtifactId,
        PatchHash = "sha256:patch",
        ChangeSetHash = "sha256:change-set",
        PatchArtifact = ValidPatchArtifact(),
        RollbackSupportReceiptId = Ids.RollbackSupportReceiptId,
        RollbackSupportReceiptHash = "sha256:rollback-support",
        RollbackPlanId = Ids.RollbackPlanId,
        RollbackPlanHash = "sha256:rollback-plan",
        RollbackGateEvaluationHash = "sha256:rollback-gate",
        RollbackSupport = ValidRollbackSupport(),
        SubjectKind = "PatchArtifact",
        SubjectId = "patch-artifact:1",
        SubjectHash = "sha256:subject",
        SourceSnapshotReference = "snapshot:source",
        SourceBaselineHash = "sha256:source-baseline",
        WorkspaceBoundaryHash = "sha256:workspace-boundary",
        ExpectedBranch = "main",
        ExpectedCleanWorktreeHash = "sha256:clean-worktree",
        EvaluatedAtUtc = EvaluatedAtUtc,
        ExpiresAtUtc = ExpiresAtUtc,
        EvidenceReferences = ["accepted-approval:1", "policy-satisfaction:1", "dry-run:1", "patch-artifact:1", "rollback-support:1"],
        BoundaryMaxims = ["source apply gate is checklist evidence only"],
        Boundary = SourceApplyGateBoundaryText.Boundary
    };

    private static SourceApplyGateAcceptedApprovalEvidence ValidAcceptedApproval() => new()
    {
        ProjectId = Ids.ProjectId,
        AcceptedApprovalId = Ids.AcceptedApprovalId,
        AcceptedApprovalHash = "sha256:accepted-approval",
        SubjectKind = "PatchArtifact",
        SubjectId = "patch-artifact:1",
        SubjectHash = "sha256:subject",
        ExpiresAtUtc = ExpiresAtUtc
    };

    private static SourceApplyGatePolicySatisfactionEvidence ValidPolicySatisfaction() => new()
    {
        ProjectId = Ids.ProjectId,
        PolicySatisfactionId = Ids.PolicySatisfactionId,
        PolicySatisfactionHash = "sha256:policy-satisfaction",
        AcceptedApprovalId = Ids.AcceptedApprovalId,
        AcceptedApprovalHash = "sha256:accepted-approval",
        SubjectKind = "PatchArtifact",
        SubjectId = "patch-artifact:1",
        SubjectHash = "sha256:subject",
        ExpiresAtUtc = ExpiresAtUtc
    };

    private static SourceApplyGateDryRunEvidence ValidDryRun() => new()
    {
        ProjectId = Ids.ProjectId,
        ControlledDryRunRequestId = Ids.ControlledDryRunRequestId,
        DryRunExecutionAuditId = Ids.DryRunExecutionAuditId,
        DryRunAuditHash = "sha256:dry-run-audit",
        DryRunReceiptHash = "sha256:dry-run-receipt",
        PolicySatisfactionId = Ids.PolicySatisfactionId,
        PolicySatisfactionHash = "sha256:policy-satisfaction",
        SubjectKind = "PatchArtifact",
        SubjectId = "patch-artifact:1",
        SubjectHash = "sha256:subject",
        SourceSnapshotReference = "snapshot:source",
        SourceBaselineHash = "sha256:source-baseline",
        WorkspaceBoundaryHash = "sha256:workspace-boundary",
        ExpectedBranch = "main",
        ExpectedCleanWorktreeHash = "sha256:clean-worktree",
        ExpiresAtUtc = ExpiresAtUtc
    };

    private static SourceApplyGatePatchArtifactEvidence ValidPatchArtifact() => new()
    {
        ProjectId = Ids.ProjectId,
        PatchArtifactId = Ids.PatchArtifactId,
        PatchHash = "sha256:patch",
        ChangeSetHash = "sha256:change-set",
        ControlledDryRunRequestId = Ids.ControlledDryRunRequestId,
        DryRunExecutionAuditId = Ids.DryRunExecutionAuditId,
        DryRunAuditHash = "sha256:dry-run-audit",
        DryRunReceiptHash = "sha256:dry-run-receipt",
        PolicySatisfactionId = Ids.PolicySatisfactionId,
        PolicySatisfactionHash = "sha256:policy-satisfaction",
        SubjectKind = "PatchArtifact",
        SubjectId = "patch-artifact:1",
        SubjectHash = "sha256:subject",
        SourceSnapshotReference = "snapshot:source",
        SourceBaselineHash = "sha256:source-baseline",
        WorkspaceBoundaryHash = "sha256:workspace-boundary",
        ExpiresAtUtc = ExpiresAtUtc
    };

    private static SourceApplyGateRollbackSupportEvidence ValidRollbackSupport() => new()
    {
        ProjectId = Ids.ProjectId,
        RollbackSupportReceiptId = Ids.RollbackSupportReceiptId,
        RollbackSupportReceiptHash = "sha256:rollback-support",
        RollbackPlanId = Ids.RollbackPlanId,
        RollbackPlanHash = "sha256:rollback-plan",
        RollbackGateEvaluationHash = "sha256:rollback-gate",
        RollbackGateSatisfied = true,
        PatchArtifactId = Ids.PatchArtifactId,
        PatchHash = "sha256:patch",
        ChangeSetHash = "sha256:change-set",
        SubjectKind = "PatchArtifact",
        SubjectId = "patch-artifact:1",
        SubjectHash = "sha256:subject",
        SourceSnapshotReference = "snapshot:source",
        SourceBaselineHash = "sha256:source-baseline",
        WorkspaceBoundaryHash = "sha256:workspace-boundary",
        ExpectedBranch = "main",
        ExpectedCleanWorktreeHash = "sha256:clean-worktree",
        ExpiresAtUtc = ExpiresAtUtc
    };

    private static void AssertHasIssue(SourceApplyGateEvaluationResult result, string code) =>
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == code), $"Expected issue {code}. Actual: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");

    private static void AssertHasProperty(Type type, string propertyName) =>
        Assert.IsNotNull(type.GetProperty(propertyName), $"{type.Name}.{propertyName} is missing.");

    private static IReadOnlyList<string> ProductionFiles() =>
    [
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "SourceApplyGateEvaluationModels.cs"),
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "SourceApplyGateEvaluator.cs")
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

    private enum SourceApplyGateAuthorityForbiddenNames
    {
        SourceApplyApproved
    }

    private static class Ids
    {
        public static readonly Guid ProjectId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        public static readonly Guid AcceptedApprovalId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        public static readonly Guid PolicySatisfactionId = Guid.Parse("30000000-0000-0000-0000-000000000001");
        public static readonly Guid ControlledDryRunRequestId = Guid.Parse("40000000-0000-0000-0000-000000000001");
        public static readonly Guid DryRunExecutionAuditId = Guid.Parse("50000000-0000-0000-0000-000000000001");
        public static readonly Guid PatchArtifactId = Guid.Parse("60000000-0000-0000-0000-000000000001");
        public static readonly Guid RollbackSupportReceiptId = Guid.Parse("70000000-0000-0000-0000-000000000001");
        public static readonly Guid RollbackPlanId = Guid.Parse("80000000-0000-0000-0000-000000000001");
    }
}
