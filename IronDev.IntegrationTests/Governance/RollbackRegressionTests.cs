using System.Reflection;
using System.Text.Json;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("RollbackRegression")]
public sealed class RollbackRegressionTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 6, 17, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void RollbackRegression_RollbackPlanIsPlanOnlyAndBindsRequiredEvidence()
    {
        var plan = ValidRollbackPlan(ValidPatchArtifact());
        var validation = RollbackPlanValidation.Validate(plan);

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues.Select(issue => issue.Code)));
        StringAssert.Contains(plan.Boundary, "Rollback plan is not rollback execution.");
        StringAssert.Contains(plan.Boundary, "Rollback plan is not source apply.");
        Assert.AreNotEqual(Guid.Empty, plan.PatchArtifactId);
        Assert.AreNotEqual(Guid.Empty, plan.ControlledDryRunRequestId);
        Assert.AreNotEqual(Guid.Empty, plan.DryRunExecutionAuditId);
        Assert.AreNotEqual(Guid.Empty, plan.PolicySatisfactionId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(plan.SourceBaselineHash));
        Assert.IsFalse(string.IsNullOrWhiteSpace(plan.ExpectedBranch));
        Assert.IsFalse(string.IsNullOrWhiteSpace(plan.ExpectedCleanWorktreeHash));
        Assert.IsTrue(plan.FileActions.Count > 0);
    }

    [TestMethod]
    public void RollbackRegression_RollbackPlanRejectsUnsafePathsKindsPrivateMaterialAndAuthorityClaims()
    {
        AssertPlanInvalid(ValidRollbackPlan(ValidPatchArtifact()) with { FileActions = [RestoreAction("../unsafe.cs")] }, "ROLLBACK_FILE_ACTION_PATH_UNSAFE");
        AssertPlanInvalid(ValidRollbackPlan(ValidPatchArtifact()) with { FileActions = [RestoreAction("src/Foo.cs") with { PlannedActionKind = "RunRollback" }] }, "ROLLBACK_FILE_ACTION_KIND_INVALID");
        AssertPlanInvalid(ValidRollbackPlan(ValidPatchArtifact()) with { EvidenceReferences = ["raw prompt leaked"] }, "PRIVATE_OR_RAW_MATERIAL_REJECTED");
        AssertPlanInvalid(ValidRollbackPlan(ValidPatchArtifact()) with { BoundaryMaxims = ["rollback executed"] }, "AUTHORITY_CLAIM_REJECTED");
    }

    [TestMethod]
    public void RollbackRegression_RollbackGateSatisfactionIsCoverageOnlyNotRollbackExecution()
    {
        var patch = ValidPatchArtifact();
        var plan = ValidRollbackPlan(patch);
        var result = RollbackGateEvaluator.Evaluate(ValidRequest(patch, plan));
        var serialized = JsonSerializer.Serialize(result);

        Assert.IsTrue(result.Satisfied, string.Join(", ", result.Issues.Select(issue => issue.Code)));
        StringAssert.Contains(result.Boundary, "Rollback gate evaluation is not rollback execution.");
        AssertNoAuthorityTokens(serialized);
    }

    [TestMethod]
    public void RollbackRegression_RollbackGateRejectsMissingWrongDuplicateExtraUnsafeCoverage()
    {
        var patch = ValidPatchArtifact(Changes(ModifyChange("src/existing.cs")));
        var validPlan = ValidRollbackPlan(patch);

        AssertGateIssue(validPlan with { FileActions = [] }, patch, "ROLLBACK_COVERAGE_MISSING");
        AssertGateIssue(validPlan with { FileActions = [DeleteCreatedAction(patch.FileChanges.Single())] }, patch, "ROLLBACK_ACTION_KIND_MISMATCH");
        AssertGateIssue(validPlan with { FileActions = [RestoreAction("src/existing.cs") with { RestoreContentHash = "sha256:wrong-before" }] }, patch, "ROLLBACK_ACTION_HASH_MISMATCH");
        AssertGateIssue(validPlan with { FileActions = [RestoreAction("src/existing.cs"), RestoreAction("src/existing.cs") with { RollbackActionHash = "sha256:duplicate" }] }, patch, "ROLLBACK_ACTION_DUPLICATE");
        AssertGateIssue(validPlan with { FileActions = [.. validPlan.FileActions, RestoreAction("src/unrelated.cs")] }, patch, "ROLLBACK_ACTION_NOT_BOUND_TO_PATCH_CHANGE");

        var unsafeResult = RollbackGateEvaluator.Evaluate(ValidRequest(patch, validPlan) with { EvidenceReferences = ["chain-of-thought leaked"] });
        AssertHasIssue(unsafeResult, "PRIVATE_OR_RAW_MATERIAL_REJECTED");

        var authorityResult = RollbackGateEvaluator.Evaluate(ValidRequest(patch, validPlan) with { BoundaryMaxims = ["release ready"] });
        AssertHasIssue(authorityResult, "AUTHORITY_CLAIM_REJECTED");
    }

    [TestMethod]
    public void RollbackRegression_RollbackSupportReceiptIsAppendOnlyEvidenceOnly()
    {
        var receipt = ValidRollbackSupportReceipt(ValidPatchArtifact());
        var validation = RollbackSupportReceiptValidation.Validate(receipt);
        var storeMethods = typeof(IRollbackSupportReceiptStore)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(method => method.Name)
            .ToArray();

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues.Select(issue => issue.Code)));
        StringAssert.Contains(receipt.Boundary, "Rollback support receipt is not rollback execution.");
        CollectionAssert.Contains(storeMethods, nameof(IRollbackSupportReceiptStore.SaveAsync));
        CollectionAssert.Contains(storeMethods, nameof(IRollbackSupportReceiptStore.GetAsync));

        foreach (var forbidden in new[] { "Update", "Delete", "Upsert", "Overwrite", "MarkExecuted", "MarkSucceeded", "Apply", "Continue", "Approve" })
        {
            Assert.IsFalse(storeMethods.Any(method => method.Contains(forbidden, StringComparison.OrdinalIgnoreCase)), forbidden);
        }
    }

    [TestMethod]
    public void RollbackRegression_RollbackSupportReceiptSqlStillBlocksMutationAndUnsafeAuthorityText()
    {
        var sql = File.ReadAllText(Path.Combine(RepoRoot(), "Database", "migrate_rollback_support_receipt.sql"));

        StringAssert.Contains(sql, "TR_RollbackSupportReceipt_BlockUpdateDelete");
        StringAssert.Contains(sql, "TR_RollbackSupportReceipt_ValidateInsert");
        StringAssert.Contains(sql, "DENY INSERT, UPDATE, DELETE ON OBJECT::governance.RollbackSupportReceipt");
        StringAssert.Contains(sql, "DENY ALTER ON SCHEMA::governance");
        StringAssert.Contains(sql, "private reasoning");
        StringAssert.Contains(sql, "raw prompt");
        StringAssert.Contains(sql, "rollback executed");
        StringAssert.Contains(sql, "source applied");
        StringAssert.Contains(sql, "release ready");
    }

    [TestMethod]
    public void RollbackRegression_RollbackReadApiCanDisplayEvidenceButCannotGrantAuthority()
    {
        var controllerText = File.ReadAllText(Path.Combine(RepoRoot(), "IronDev.Api", "Controllers", "RollbackSupportReceiptsV1Controller.cs"));
        var queryText = File.ReadAllText(Path.Combine(RepoRoot(), "IronDev.Infrastructure", "Governance", "RollbackSupportReceiptQueryService.cs"));
        var modelProperties = typeof(RollbackSupportReceiptReadModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();

        StringAssert.Contains(controllerText, "IRollbackSupportReceiptQueryService");
        Assert.IsFalse(controllerText.Contains("HttpPost", StringComparison.Ordinal), "Rollback read API must not add POST.");
        Assert.IsFalse(controllerText.Contains("HttpPut", StringComparison.Ordinal), "Rollback read API must not add PUT.");
        Assert.IsFalse(controllerText.Contains("HttpPatch", StringComparison.Ordinal), "Rollback read API must not add PATCH.");
        Assert.IsFalse(controllerText.Contains("HttpDelete", StringComparison.Ordinal), "Rollback read API must not add DELETE.");
        Assert.IsFalse(controllerText.Contains("IRollbackSupportReceiptStore", StringComparison.Ordinal), "Controller must not depend on write-capable store.");
        Assert.IsFalse(queryText.Contains(".SaveAsync", StringComparison.Ordinal), "Query service must not save rollback receipts.");

        foreach (var forbidden in new[] { "CanApplySource", "RollbackSucceeded", "RollbackExecuted", "WorkflowCanContinue", "ReleaseReady", "SourceApplyApproved" })
        {
            Assert.IsFalse(modelProperties.Contains(forbidden, StringComparer.Ordinal), forbidden);
        }
    }

    [TestMethod]
    public void RollbackRegression_RollbackSupportChainDoesNotExecuteRollback()
    {
        AssertNoForbiddenProductionTokens("RollbackExecutor", "ExecuteRollback", "RollbackExecuted", "RollbackSucceeded", "MarkRollbackExecuted", "MarkRollbackSucceeded");
    }

    [TestMethod]
    public void RollbackRegression_RollbackSupportChainDoesNotApplySource()
    {
        AssertNoForbiddenProductionTokens("ApplySource", "ApplySourceAsync", "SourceApplyService", "ControlledSourceApply", "SourceApplyRequest", "SourceApplyReceipt", "CanApplySource", "SourceApplyApproved");
    }

    [TestMethod]
    public void RollbackRegression_RollbackSupportChainDoesNotContinueWorkflowOrApproveRelease()
    {
        AssertNoForbiddenProductionTokens("ContinueWorkflow", "ContinueWorkflowAsync", "WorkflowContinued", "ApproveRelease", "ApproveReleaseAsync", "ReleaseReady", "ReleaseApproved");
    }

    [TestMethod]
    public void RollbackRegression_RollbackSupportChainDoesNotCallGitProcessOrWorktreeInspection()
    {
        AssertNoForbiddenProductionTokens("ProcessStartInfo", "System.Diagnostics.Process", "git ", "InspectWorktree", "WorktreeInspection", "GitWorktree");
    }

    [TestMethod]
    public void RollbackRegression_RollbackSupportChainDoesNotDispatchAgentsModelsToolsMemoryOrRetrieval()
    {
        AssertNoForbiddenProductionTokens("AgentDispatch", "ToolExecution", "LLM", "model call", "PromoteMemory", "ActivateRetrieval", "Vector", "Embedding", "Weaviate");
    }

    [TestMethod]
    public void RollbackRegression_RollbackSupportReceiptsDocumentNegativeAuthorityBoundary()
    {
        foreach (var receiptName in new[]
        {
            "PR194_ROLLBACK_PLAN_CONTRACT.md",
            "PR195_ROLLBACK_GATE_EVALUATOR.md",
            "PR196_ROLLBACK_RECEIPT_STORE.md",
            "PR197_ROLLBACK_READ_API.md",
            "PR198_ROLLBACK_REGRESSION_TESTS.md"
        })
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", receiptName));
            StringAssert.Contains(text, "not rollback execution");
            StringAssert.Contains(text, "not source apply");
        }
    }

    [TestMethod]
    public void RollbackRegression_Pr198IsTestsAndReceiptOnly()
    {
        foreach (var file in Pr198ChangedFiles())
        {
            var normalized = file.Replace('\\', '/');
            Assert.IsFalse(normalized.StartsWith("IronDev.Core/", StringComparison.OrdinalIgnoreCase), file);
            Assert.IsFalse(normalized.StartsWith("IronDev.Infrastructure/", StringComparison.OrdinalIgnoreCase), file);
            Assert.IsFalse(normalized.StartsWith("IronDev.Api/Controllers/", StringComparison.OrdinalIgnoreCase), file);
            Assert.IsFalse(normalized.StartsWith("Database/", StringComparison.OrdinalIgnoreCase), file);
            Assert.IsFalse(normalized.Contains("/Program.cs", StringComparison.OrdinalIgnoreCase), file);
        }
    }

    private static RollbackGateEvaluationRequest ValidRequest(PatchArtifact patch, RollbackPlan plan) => new()
    {
        ProjectId = patch.ProjectId,
        PatchArtifact = patch,
        RollbackPlan = plan,
        ExpectedBranch = plan.ExpectedBranch,
        ExpectedCleanWorktreeHash = plan.ExpectedCleanWorktreeHash,
        EvidenceReferences = ["rollback-gate:evidence"],
        BoundaryMaxims = ["rollback gate is support evidence only"],
        Boundary = RollbackGateBoundaryText.Boundary
    };

    private static PatchArtifact ValidPatchArtifact(IReadOnlyList<PatchArtifactFileChange>? changes = null) => new()
    {
        PatchArtifactId = Guid.Parse("10000000-0000-0000-0000-000000000198"),
        ProjectId = Guid.Parse("20000000-0000-0000-0000-000000000198"),
        PatchArtifactKind = "UnifiedDiffPatchArtifact",
        ControlledDryRunRequestId = Guid.Parse("30000000-0000-0000-0000-000000000198"),
        DryRunExecutionAuditId = Guid.Parse("40000000-0000-0000-0000-000000000198"),
        DryRunAuditHash = "sha256:dry-run-audit-pr198",
        DryRunReceiptHash = "sha256:dry-run-receipt-pr198",
        PolicySatisfactionId = Guid.Parse("50000000-0000-0000-0000-000000000198"),
        PolicySatisfactionHash = "sha256:policy-satisfaction-pr198",
        SubjectKind = "ControlledApplyPlan",
        SubjectId = "controlled-apply-plan-pr198",
        SubjectHash = "sha256:controlled-apply-plan-pr198",
        SourceSnapshotReference = "snapshot:source-pr198",
        SourceBaselineHash = "sha256:source-baseline-pr198",
        WorkspaceBoundaryHash = "sha256:workspace-boundary-pr198",
        ValidationPlanId = "validation-plan-pr198",
        ValidationPlanHash = "sha256:validation-plan-pr198",
        PatchHash = "sha256:patch-artifact-pr198",
        ChangeSetHash = "sha256:change-set-pr198",
        FileChanges = changes ?? Changes(ModifyChange("src/existing.cs")),
        CreatedAtUtc = CreatedAtUtc,
        EvidenceReferences = ["patch-artifact:evidence-pr198"],
        BoundaryMaxims = ["patch artifact is proposal evidence only"],
        Boundary = PatchArtifactBoundaryText.Boundary
    };

    private static RollbackPlan ValidRollbackPlan(PatchArtifact patch) => new()
    {
        RollbackPlanId = Guid.Parse("60000000-0000-0000-0000-000000000198"),
        ProjectId = patch.ProjectId,
        RollbackPlanKind = "PatchArtifactRollbackPlan",
        PatchArtifactId = patch.PatchArtifactId,
        PatchHash = patch.PatchHash,
        ChangeSetHash = patch.ChangeSetHash,
        ControlledDryRunRequestId = patch.ControlledDryRunRequestId,
        DryRunExecutionAuditId = patch.DryRunExecutionAuditId,
        DryRunAuditHash = patch.DryRunAuditHash,
        DryRunReceiptHash = patch.DryRunReceiptHash,
        PolicySatisfactionId = patch.PolicySatisfactionId,
        PolicySatisfactionHash = patch.PolicySatisfactionHash,
        SubjectKind = patch.SubjectKind,
        SubjectId = patch.SubjectId,
        SubjectHash = patch.SubjectHash,
        SourceSnapshotReference = patch.SourceSnapshotReference,
        SourceBaselineHash = patch.SourceBaselineHash,
        WorkspaceBoundaryHash = patch.WorkspaceBoundaryHash,
        ExpectedBranch = "main",
        ExpectedCleanWorktreeHash = "sha256:clean-worktree-pr198",
        RollbackPlanHash = "sha256:rollback-plan-pr198",
        FileActions = patch.FileChanges.Select(ActionForChange).ToArray(),
        CreatedAtUtc = CreatedAtUtc,
        ExpiresAtUtc = CreatedAtUtc.AddHours(1),
        EvidenceReferences = ["rollback-plan:evidence-pr198"],
        BoundaryMaxims = ["rollback plan is escape hatch evidence only"],
        Boundary = RollbackPlanBoundaryText.Boundary
    };

    private static RollbackSupportReceipt ValidRollbackSupportReceipt(PatchArtifact patch)
    {
        var plan = ValidRollbackPlan(patch);
        return new RollbackSupportReceipt
        {
            RollbackSupportReceiptId = Guid.Parse("70000000-0000-0000-0000-000000000198"),
            ProjectId = patch.ProjectId,
            RollbackPlanId = plan.RollbackPlanId,
            RollbackPlanHash = plan.RollbackPlanHash,
            RollbackGateSatisfied = true,
            RollbackGateEvaluationHash = "sha256:rollback-gate-evaluation-pr198",
            PatchArtifactId = patch.PatchArtifactId,
            PatchHash = patch.PatchHash,
            ChangeSetHash = patch.ChangeSetHash,
            ControlledDryRunRequestId = patch.ControlledDryRunRequestId,
            DryRunExecutionAuditId = patch.DryRunExecutionAuditId,
            DryRunAuditHash = patch.DryRunAuditHash,
            DryRunReceiptHash = patch.DryRunReceiptHash,
            PolicySatisfactionId = patch.PolicySatisfactionId,
            PolicySatisfactionHash = patch.PolicySatisfactionHash,
            SubjectKind = patch.SubjectKind,
            SubjectId = patch.SubjectId,
            SubjectHash = patch.SubjectHash,
            SourceSnapshotReference = patch.SourceSnapshotReference,
            SourceBaselineHash = patch.SourceBaselineHash,
            WorkspaceBoundaryHash = patch.WorkspaceBoundaryHash,
            ExpectedBranch = plan.ExpectedBranch,
            ExpectedCleanWorktreeHash = plan.ExpectedCleanWorktreeHash,
            RollbackSupportReceiptHash = "sha256:rollback-support-receipt-pr198",
            CreatedAtUtc = CreatedAtUtc,
            ExpiresAtUtc = CreatedAtUtc.AddHours(1),
            EvidenceReferences = ["rollback-gate-evaluation:evidence-pr198", "rollback-plan:evidence-pr198"],
            BoundaryMaxims = ["Rollback support receipt records rollback support only."],
            Boundary = RollbackSupportReceiptBoundaryText.Boundary
        };
    }

    private static IReadOnlyList<PatchArtifactFileChange> Changes(params PatchArtifactFileChange[] changes) => changes;

    private static PatchArtifactFileChange ModifyChange(string path) => new()
    {
        Path = path,
        ChangeKind = "Modify",
        BeforeContentHash = $"sha256:{path}:before",
        AfterContentHash = $"sha256:{path}:after",
        DiffHash = $"sha256:{path}:diff",
        NormalizedDiff = $"diff modify {path}"
    };

    private static RollbackPlanFileAction RestoreAction(string path) => new()
    {
        Path = path,
        PlannedActionKind = "RestoreModifiedFile",
        RestoreContentHash = $"sha256:{path}:before",
        ExpectedCurrentContentHash = $"sha256:{path}:after",
        RollbackActionHash = $"sha256:rollback:{path}:restore"
    };

    private static RollbackPlanFileAction DeleteCreatedAction(PatchArtifactFileChange change) => new()
    {
        Path = change.Path,
        PlannedActionKind = "DeleteCreatedFile",
        DeleteContentHash = change.AfterContentHash,
        ExpectedCurrentContentHash = change.AfterContentHash ?? "sha256:missing-after",
        RollbackActionHash = $"sha256:rollback:{change.Path}:delete-created"
    };

    private static RollbackPlanFileAction ActionForChange(PatchArtifactFileChange change) => new()
    {
        Path = change.Path,
        PlannedActionKind = "RestoreModifiedFile",
        RestoreContentHash = change.BeforeContentHash,
        ExpectedCurrentContentHash = change.AfterContentHash ?? "sha256:missing-after",
        RollbackActionHash = $"sha256:rollback:{change.Path}:restore-modified"
    };

    private static void AssertPlanInvalid(RollbackPlan plan, string code)
    {
        var result = RollbackPlanValidation.Validate(plan);
        Assert.IsFalse(result.IsValid, "Expected rollback plan to be invalid.");
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == code), $"Expected {code}. Actual: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
    }

    private static void AssertGateIssue(RollbackPlan plan, PatchArtifact patch, string code)
    {
        var result = RollbackGateEvaluator.Evaluate(ValidRequest(patch, plan));
        AssertHasIssue(result, code);
    }

    private static void AssertHasIssue(RollbackGateEvaluationResult result, string code) =>
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == code), $"Expected issue {code}. Actual: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");

    private static void AssertNoAuthorityTokens(string text)
    {
        foreach (var token in new[] { "rollbackExecuted", "rollbackSucceeded", "rollbackSuccess", "canApplySource", "sourceApplyApproved", "workflowContinued", "releaseReady" })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Unexpected authority token: {token}");
        }
    }

    private static void AssertNoForbiddenProductionTokens(params string[] tokens)
    {
        foreach (var file in RollbackProductionFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var token in tokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Forbidden token found in {file}: {token}");
            }
        }
    }

    private static IReadOnlyList<string> RollbackProductionFiles()
    {
        var root = RepoRoot();
        return
        [
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackPlan.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackPlanValidation.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackGateEvaluationModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackGateEvaluator.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackSupportReceipt.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackSupportReceiptValidation.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "IRollbackSupportReceiptStore.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlRollbackSupportReceiptStore.cs"),
            Path.Combine(root, "Database", "migrate_rollback_support_receipt.sql"),
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackSupportReceiptReadModels.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "RollbackSupportReceiptQueryService.cs"),
            Path.Combine(root, "IronDev.Api", "Controllers", "RollbackSupportReceiptsV1Controller.cs")
        ];
    }

    private static IReadOnlyList<string> Pr198ChangedFiles() =>
    [
        "IronDev.IntegrationTests/Governance/RollbackRegressionTests.cs",
        "IronDev.IntegrationTests.Api/RollbackReadApiRegressionTests.cs",
        "Docs/receipts/PR198_ROLLBACK_REGRESSION_TESTS.md"
    ];

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
