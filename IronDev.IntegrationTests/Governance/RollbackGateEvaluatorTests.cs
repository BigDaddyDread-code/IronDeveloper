using System.Reflection;
using System.Text.Json;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("RollbackGateEvaluator")]
public sealed class RollbackGateEvaluatorTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 6, 16, 20, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void RollbackGateEvaluator_ExposesPureEvaluationContract()
    {
        AssertHasProperty(typeof(RollbackGateEvaluationRequest), nameof(RollbackGateEvaluationRequest.ProjectId));
        AssertHasProperty(typeof(RollbackGateEvaluationRequest), nameof(RollbackGateEvaluationRequest.PatchArtifact));
        AssertHasProperty(typeof(RollbackGateEvaluationRequest), nameof(RollbackGateEvaluationRequest.RollbackPlan));
        AssertHasProperty(typeof(RollbackGateEvaluationRequest), nameof(RollbackGateEvaluationRequest.ExpectedBranch));
        AssertHasProperty(typeof(RollbackGateEvaluationRequest), nameof(RollbackGateEvaluationRequest.ExpectedCleanWorktreeHash));
        AssertHasProperty(typeof(RollbackGateEvaluationRequest), nameof(RollbackGateEvaluationRequest.EvidenceReferences));
        AssertHasProperty(typeof(RollbackGateEvaluationRequest), nameof(RollbackGateEvaluationRequest.BoundaryMaxims));
        AssertHasProperty(typeof(RollbackGateEvaluationResult), nameof(RollbackGateEvaluationResult.Satisfied));
        AssertHasProperty(typeof(RollbackGateEvaluationResult), nameof(RollbackGateEvaluationResult.RollbackPlanHash));
        AssertHasProperty(typeof(RollbackGateEvaluationResult), nameof(RollbackGateEvaluationResult.Issues));

        var methods = typeof(RollbackGateEvaluator)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.Name)
            .ToArray();

        CollectionAssert.AreEquivalent(new[] { nameof(RollbackGateEvaluator.Evaluate) }, methods);
    }

    [TestMethod]
    public void RollbackGateEvaluator_SatisfiesWhenPatchAndRollbackPlanMatch()
    {
        var patch = ValidPatchArtifact(Changes(
            CreateChange("src/new-file.cs"),
            ModifyChange("src/existing-file.cs"),
            DeleteChange("src/old-file.cs"),
            RenameChange("src/new-name.cs", "src/old-name.cs")));
        var plan = ValidRollbackPlan(patch);
        var result = RollbackGateEvaluator.Evaluate(ValidRequest(patch, plan));

        Assert.IsTrue(result.Satisfied, string.Join(", ", result.Issues.Select(issue => issue.Code)));
        Assert.AreEqual(patch.PatchArtifactId, result.PatchArtifactId);
        Assert.AreEqual(patch.PatchHash, result.PatchHash);
        Assert.AreEqual(patch.ChangeSetHash, result.ChangeSetHash);
        Assert.AreEqual(plan.RollbackPlanId, result.RollbackPlanId);
        Assert.AreEqual(plan.RollbackPlanHash, result.RollbackPlanHash);
        Assert.AreEqual(patch.SourceBaselineHash, result.SourceBaselineHash);
        StringAssert.Contains(result.Boundary, "not rollback execution");
    }

    [TestMethod]
    public void RollbackGateEvaluator_NullRequestFailsClosed()
    {
        var result = RollbackGateEvaluator.Evaluate(null);

        Assert.IsFalse(result.Satisfied);
        AssertHasIssue(result, "REQUEST_REQUIRED");
        Assert.AreEqual(Guid.Empty, result.ProjectId);
        Assert.AreEqual(Guid.Empty, result.PatchArtifactId);
        Assert.AreEqual(Guid.Empty, result.RollbackPlanId);
        Assert.AreEqual(0, result.EvidenceReferences.Count);
    }

    [TestMethod]
    public void RollbackGateEvaluator_RejectsInvalidPatchArtifactAndRollbackPlan()
    {
        var patch = ValidPatchArtifact() with { PatchHash = " " };
        var plan = ValidRollbackPlan(ValidPatchArtifact()) with { RollbackPlanHash = " " };
        var result = RollbackGateEvaluator.Evaluate(ValidRequest(patch, plan));

        Assert.IsFalse(result.Satisfied);
        AssertHasIssue(result, "PATCH_ARTIFACT_INVALID");
        AssertHasIssue(result, "ROLLBACK_PLAN_INVALID");
    }

    [TestMethod]
    public void RollbackGateEvaluator_RejectsProjectMismatch()
    {
        var patch = ValidPatchArtifact();
        var plan = ValidRollbackPlan(patch) with { ProjectId = Guid.NewGuid() };
        var result = RollbackGateEvaluator.Evaluate(ValidRequest(patch, plan));

        Assert.IsFalse(result.Satisfied);
        AssertHasIssue(result, "PROJECT_ID_MISMATCH");
    }

    [TestMethod]
    public void RollbackGateEvaluator_RejectsPatchAndChangeSetMismatches()
    {
        var patch = ValidPatchArtifact();
        var plan = ValidRollbackPlan(patch) with
        {
            PatchArtifactId = Guid.NewGuid(),
            PatchHash = "sha256:different-patch",
            ChangeSetHash = "sha256:different-change-set"
        };
        var result = RollbackGateEvaluator.Evaluate(ValidRequest(patch, plan));

        AssertHasIssue(result, "PATCH_ARTIFACT_ID_MISMATCH");
        AssertHasIssue(result, "PATCH_HASH_MISMATCH");
        AssertHasIssue(result, "CHANGE_SET_HASH_MISMATCH");
    }

    [TestMethod]
    public void RollbackGateEvaluator_RejectsDryRunPolicyAndSubjectMismatches()
    {
        var patch = ValidPatchArtifact();
        var plan = ValidRollbackPlan(patch) with
        {
            ControlledDryRunRequestId = Guid.NewGuid(),
            DryRunExecutionAuditId = Guid.NewGuid(),
            DryRunAuditHash = "sha256:different-audit",
            DryRunReceiptHash = "sha256:different-receipt",
            PolicySatisfactionId = Guid.NewGuid(),
            PolicySatisfactionHash = "sha256:different-policy",
            SubjectKind = "DifferentSubject",
            SubjectId = "different-subject-id",
            SubjectHash = "sha256:different-subject"
        };
        var result = RollbackGateEvaluator.Evaluate(ValidRequest(patch, plan));

        AssertHasIssue(result, "CONTROLLED_DRY_RUN_REQUEST_ID_MISMATCH");
        AssertHasIssue(result, "DRY_RUN_EXECUTION_AUDIT_ID_MISMATCH");
        AssertHasIssue(result, "DRY_RUN_AUDIT_HASH_MISMATCH");
        AssertHasIssue(result, "DRY_RUN_RECEIPT_HASH_MISMATCH");
        AssertHasIssue(result, "POLICY_SATISFACTION_ID_MISMATCH");
        AssertHasIssue(result, "POLICY_SATISFACTION_HASH_MISMATCH");
        AssertHasIssue(result, "SUBJECT_KIND_MISMATCH");
        AssertHasIssue(result, "SUBJECT_ID_MISMATCH");
        AssertHasIssue(result, "SUBJECT_HASH_MISMATCH");
    }

    [TestMethod]
    public void RollbackGateEvaluator_RejectsSourceWorkspaceAndBranchMismatches()
    {
        var patch = ValidPatchArtifact();
        var plan = ValidRollbackPlan(patch) with
        {
            SourceSnapshotReference = "snapshot:different",
            SourceBaselineHash = "sha256:different-baseline",
            WorkspaceBoundaryHash = "sha256:different-workspace",
            ExpectedBranch = "main-different",
            ExpectedCleanWorktreeHash = "sha256:different-clean-worktree"
        };
        var result = RollbackGateEvaluator.Evaluate(ValidRequest(patch, plan) with
        {
            ExpectedBranch = "main",
            ExpectedCleanWorktreeHash = "sha256:clean-worktree"
        });

        AssertHasIssue(result, "SOURCE_SNAPSHOT_REFERENCE_MISMATCH");
        AssertHasIssue(result, "SOURCE_BASELINE_HASH_MISMATCH");
        AssertHasIssue(result, "WORKSPACE_BOUNDARY_HASH_MISMATCH");
        AssertHasIssue(result, "EXPECTED_BRANCH_MISMATCH");
        AssertHasIssue(result, "EXPECTED_CLEAN_WORKTREE_HASH_MISMATCH");
    }

    [TestMethod]
    public void RollbackGateEvaluator_RequiresRollbackCoverageForEveryPatchChange()
    {
        var patch = ValidPatchArtifact(Changes(CreateChange("src/new-file.cs")));
        var plan = ValidRollbackPlan(patch) with { FileActions = [] };
        var result = RollbackGateEvaluator.Evaluate(ValidRequest(patch, plan));

        Assert.IsFalse(result.Satisfied);
        AssertHasIssue(result, "ROLLBACK_PLAN_INVALID");
        AssertHasIssue(result, "ROLLBACK_COVERAGE_MISSING");
    }

    [TestMethod]
    public void RollbackGateEvaluator_RejectsWrongActionKindForPatchChange()
    {
        var patch = ValidPatchArtifact(Changes(ModifyChange("src/existing-file.cs")));
        var change = patch.FileChanges.Single();
        var plan = ValidRollbackPlan(patch) with
        {
            FileActions = [DeleteCreatedAction(change)]
        };
        var result = RollbackGateEvaluator.Evaluate(ValidRequest(patch, plan));

        Assert.IsFalse(result.Satisfied);
        AssertHasIssue(result, "ROLLBACK_ACTION_KIND_MISMATCH");
    }

    [TestMethod]
    public void RollbackGateEvaluator_RejectsRollbackActionHashMismatchForCreate()
    {
        var patch = ValidPatchArtifact(Changes(CreateChange("src/new-file.cs")));
        var action = DeleteCreatedAction(patch.FileChanges.Single()) with { DeleteContentHash = "sha256:wrong-after" };
        var plan = ValidRollbackPlan(patch) with { FileActions = [action] };
        var result = RollbackGateEvaluator.Evaluate(ValidRequest(patch, plan));

        AssertHasIssue(result, "ROLLBACK_ACTION_HASH_MISMATCH");
    }

    [TestMethod]
    public void RollbackGateEvaluator_RejectsRollbackActionHashMismatchForModify()
    {
        var patch = ValidPatchArtifact(Changes(ModifyChange("src/existing-file.cs")));
        var action = RestoreModifiedAction(patch.FileChanges.Single()) with { RestoreContentHash = "sha256:wrong-before" };
        var plan = ValidRollbackPlan(patch) with { FileActions = [action] };
        var result = RollbackGateEvaluator.Evaluate(ValidRequest(patch, plan));

        AssertHasIssue(result, "ROLLBACK_ACTION_HASH_MISMATCH");
    }

    [TestMethod]
    public void RollbackGateEvaluator_RejectsRollbackActionHashMismatchForDelete()
    {
        var patch = ValidPatchArtifact(Changes(DeleteChange("src/old-file.cs")));
        var action = RecreateDeletedAction(patch.FileChanges.Single()) with { RestoreContentHash = "sha256:wrong-before" };
        var plan = ValidRollbackPlan(patch) with { FileActions = [action] };
        var result = RollbackGateEvaluator.Evaluate(ValidRequest(patch, plan));

        AssertHasIssue(result, "ROLLBACK_ACTION_HASH_MISMATCH");
    }

    [TestMethod]
    public void RollbackGateEvaluator_RejectsRollbackActionHashMismatchForRename()
    {
        var patch = ValidPatchArtifact(Changes(RenameChange("src/new-name.cs", "src/old-name.cs")));
        var action = RenameBackAction(patch.FileChanges.Single()) with { PreviousPath = "src/wrong-old-name.cs" };
        var plan = ValidRollbackPlan(patch) with { FileActions = [action] };
        var result = RollbackGateEvaluator.Evaluate(ValidRequest(patch, plan));

        AssertHasIssue(result, "ROLLBACK_ACTION_HASH_MISMATCH");
    }

    [TestMethod]
    public void RollbackGateEvaluator_RejectsDuplicateRollbackActions()
    {
        var patch = ValidPatchArtifact(Changes(ModifyChange("src/existing-file.cs")));
        var action = RestoreModifiedAction(patch.FileChanges.Single());
        var plan = ValidRollbackPlan(patch) with { FileActions = [action, action with { RollbackActionHash = "sha256:rollback-duplicate" }] };
        var result = RollbackGateEvaluator.Evaluate(ValidRequest(patch, plan));

        AssertHasIssue(result, "ROLLBACK_ACTION_DUPLICATE");
    }

    [TestMethod]
    public void RollbackGateEvaluator_RejectsExtraNonNoopRollbackAction()
    {
        var patch = ValidPatchArtifact(Changes(ModifyChange("src/existing-file.cs")));
        var plan = ValidRollbackPlan(patch) with
        {
            FileActions = [.. ValidRollbackPlan(patch).FileActions, RestoreModifiedAction(ModifyChange("src/unrelated.cs"))]
        };
        var result = RollbackGateEvaluator.Evaluate(ValidRequest(patch, plan));

        AssertHasIssue(result, "ROLLBACK_ACTION_NOT_BOUND_TO_PATCH_CHANGE");
    }

    [TestMethod]
    public void RollbackGateEvaluator_AllowsNoopButNoopDoesNotSatisfyCoverage()
    {
        foreach (var change in new[]
        {
            CreateChange("src/create.cs"),
            ModifyChange("src/modify.cs"),
            DeleteChange("src/delete.cs"),
            RenameChange("src/rename-new.cs", "src/rename-old.cs")
        })
        {
            var patch = ValidPatchArtifact(Changes(change));
            var plan = ValidRollbackPlan(patch) with { FileActions = [NoopAction(change.Path)] };
            var result = RollbackGateEvaluator.Evaluate(ValidRequest(patch, plan));

            Assert.IsFalse(result.Satisfied, change.ChangeKind);
            AssertHasIssue(result, "ROLLBACK_COVERAGE_MISSING");
        }
    }

    [TestMethod]
    public void RollbackGateEvaluator_RejectsPrivateRawMaterialWithoutEchoingIt()
    {
        var patch = ValidPatchArtifact();
        var plan = ValidRollbackPlan(patch);
        var result = RollbackGateEvaluator.Evaluate(ValidRequest(patch, plan) with
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
    public void RollbackGateEvaluator_RejectsAuthorityClaimsWithoutBlockingCorrectNegativeBoundaryText()
    {
        var patch = ValidPatchArtifact();
        var plan = ValidRollbackPlan(patch);
        var negativeBoundaryResult = RollbackGateEvaluator.Evaluate(ValidRequest(patch, plan));
        var authorityResult = RollbackGateEvaluator.Evaluate(ValidRequest(patch, plan) with
        {
            BoundaryMaxims = ["rollback executed and release ready"]
        });

        Assert.IsTrue(negativeBoundaryResult.Satisfied);
        AssertHasIssue(authorityResult, "AUTHORITY_CLAIM_REJECTED");
    }

    [TestMethod]
    public void RollbackGateEvaluator_DoesNotExposeExecutionOrAuthorityVocabularyInPublicSurface()
    {
        var publicNames = typeof(RollbackGateEvaluator)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.Name)
            .Concat(typeof(RollbackGateEvaluationRequest).GetProperties().Select(property => property.Name))
            .Concat(typeof(RollbackGateEvaluationResult).GetProperties().Select(property => property.Name))
            .ToArray();

        foreach (var forbidden in new[]
        {
            "Execute",
            "Run",
            "RollbackSuccess",
            "ApplySource",
            "MutateSource",
            "ContinueWorkflow",
            "ApproveRelease"
        })
        {
            Assert.IsFalse(publicNames.Any(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)), forbidden);
        }
    }

    [TestMethod]
    public void RollbackGateEvaluator_ProductionFilesDoNotAddExecutorRuntimeSqlApiCliUiOrSourceMutationPaths()
    {
        foreach (var path in ProductionFiles())
        {
            var text = File.ReadAllText(path);
            foreach (var forbidden in new[]
            {
                "RollbackExecutor",
                "SourceApplyService",
                "WorkflowContinuation",
                "SqlConnection",
                "DbCommand",
                "ControllerBase",
                "MapPost",
                "CommandLine",
                "ProcessStartInfo",
                "git ",
                "File.WriteAllText",
                "File.ReadAllText",
                "Directory.CreateDirectory",
                "IHostedService",
                "BackgroundService"
            })
            {
                Assert.IsFalse(text.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"{Path.GetFileName(path)} contains forbidden token {forbidden}");
            }
        }
    }

    [TestMethod]
    public void RollbackGateEvaluator_ReceiptDocumentsBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "PR195_ROLLBACK_GATE_EVALUATOR.md"));

        StringAssert.Contains(receipt, "not rollback execution");
        StringAssert.Contains(receipt, "not source apply");
        StringAssert.Contains(receipt, "not workflow continuation");
        StringAssert.Contains(receipt, "does not authorize source mutation");
        StringAssert.Contains(receipt, "does not pull the lever");
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
        PatchArtifactId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
        ProjectId = Guid.Parse("20000000-0000-0000-0000-000000000001"),
        PatchArtifactKind = "UnifiedDiffPatchArtifact",
        ControlledDryRunRequestId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
        DryRunExecutionAuditId = Guid.Parse("40000000-0000-0000-0000-000000000001"),
        DryRunAuditHash = "sha256:dry-run-audit",
        DryRunReceiptHash = "sha256:dry-run-receipt",
        PolicySatisfactionId = Guid.Parse("50000000-0000-0000-0000-000000000001"),
        PolicySatisfactionHash = "sha256:policy-satisfaction",
        SubjectKind = "ControlledApplyPlan",
        SubjectId = "controlled-apply-plan-1",
        SubjectHash = "sha256:controlled-apply-plan",
        SourceSnapshotReference = "snapshot:source-1",
        SourceBaselineHash = "sha256:source-baseline",
        WorkspaceBoundaryHash = "sha256:workspace-boundary",
        ValidationPlanId = "validation-plan-1",
        ValidationPlanHash = "sha256:validation-plan",
        PatchHash = "sha256:patch-artifact",
        ChangeSetHash = "sha256:change-set",
        FileChanges = changes ?? Changes(ModifyChange("src/existing-file.cs")),
        CreatedAtUtc = CreatedAtUtc,
        EvidenceReferences = ["patch-artifact:evidence"],
        BoundaryMaxims = ["patch artifact is proposal evidence only"],
        Boundary = PatchArtifactBoundaryText.Boundary
    };

    private static RollbackPlan ValidRollbackPlan(PatchArtifact patch) => new()
    {
        RollbackPlanId = Guid.Parse("60000000-0000-0000-0000-000000000001"),
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
        ExpectedCleanWorktreeHash = "sha256:clean-worktree",
        RollbackPlanHash = "sha256:rollback-plan",
        FileActions = patch.FileChanges.Select(ActionForChange).ToArray(),
        CreatedAtUtc = CreatedAtUtc,
        EvidenceReferences = ["rollback-plan:evidence"],
        BoundaryMaxims = ["rollback plan is escape hatch evidence only"],
        Boundary = RollbackPlanBoundaryText.Boundary
    };

    private static IReadOnlyList<PatchArtifactFileChange> Changes(params PatchArtifactFileChange[] changes) => changes;

    private static PatchArtifactFileChange CreateChange(string path) => new()
    {
        Path = path,
        ChangeKind = "Create",
        AfterContentHash = $"sha256:{path}:after",
        DiffHash = $"sha256:{path}:diff",
        NormalizedDiff = $"diff create {path}"
    };

    private static PatchArtifactFileChange ModifyChange(string path) => new()
    {
        Path = path,
        ChangeKind = "Modify",
        BeforeContentHash = $"sha256:{path}:before",
        AfterContentHash = $"sha256:{path}:after",
        DiffHash = $"sha256:{path}:diff",
        NormalizedDiff = $"diff modify {path}"
    };

    private static PatchArtifactFileChange DeleteChange(string path) => new()
    {
        Path = path,
        ChangeKind = "Delete",
        BeforeContentHash = $"sha256:{path}:before",
        DiffHash = $"sha256:{path}:diff",
        NormalizedDiff = $"diff delete {path}"
    };

    private static PatchArtifactFileChange RenameChange(string path, string previousPath) => new()
    {
        Path = path,
        PreviousPath = previousPath,
        ChangeKind = "Rename",
        BeforeContentHash = $"sha256:{previousPath}:before",
        AfterContentHash = $"sha256:{path}:after",
        DiffHash = $"sha256:{path}:diff",
        NormalizedDiff = $"diff rename {previousPath} {path}"
    };

    private static RollbackPlanFileAction ActionForChange(PatchArtifactFileChange change) => change.ChangeKind switch
    {
        "Create" => DeleteCreatedAction(change),
        "Modify" => RestoreModifiedAction(change),
        "Delete" => RecreateDeletedAction(change),
        "Rename" => RenameBackAction(change),
        _ => NoopAction(change.Path)
    };

    private static RollbackPlanFileAction DeleteCreatedAction(PatchArtifactFileChange change) => new()
    {
        Path = change.Path,
        PlannedActionKind = "DeleteCreatedFile",
        DeleteContentHash = change.AfterContentHash,
        ExpectedCurrentContentHash = change.AfterContentHash ?? "sha256:missing-after",
        RollbackActionHash = $"sha256:rollback:{change.Path}:delete-created"
    };

    private static RollbackPlanFileAction RestoreModifiedAction(PatchArtifactFileChange change) => new()
    {
        Path = change.Path,
        PlannedActionKind = "RestoreModifiedFile",
        RestoreContentHash = change.BeforeContentHash,
        ExpectedCurrentContentHash = change.AfterContentHash ?? "sha256:missing-after",
        RollbackActionHash = $"sha256:rollback:{change.Path}:restore-modified"
    };

    private static RollbackPlanFileAction RecreateDeletedAction(PatchArtifactFileChange change) => new()
    {
        Path = change.Path,
        PlannedActionKind = "RecreateDeletedFile",
        RestoreContentHash = change.BeforeContentHash,
        ExpectedCurrentContentHash = "sha256:expected-deleted-state",
        RollbackActionHash = $"sha256:rollback:{change.Path}:recreate-deleted"
    };

    private static RollbackPlanFileAction RenameBackAction(PatchArtifactFileChange change) => new()
    {
        Path = change.Path,
        PreviousPath = change.PreviousPath,
        PlannedActionKind = "RenameBack",
        RestoreContentHash = change.BeforeContentHash,
        ExpectedCurrentContentHash = change.AfterContentHash ?? "sha256:missing-after",
        RollbackActionHash = $"sha256:rollback:{change.Path}:rename-back"
    };

    private static RollbackPlanFileAction NoopAction(string path) => new()
    {
        Path = path,
        PlannedActionKind = "Noop",
        ExpectedCurrentContentHash = "sha256:noop-current",
        RollbackActionHash = $"sha256:rollback:{path}:noop"
    };

    private static void AssertHasIssue(RollbackGateEvaluationResult result, string code) =>
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == code), $"Expected issue {code}. Actual: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");

    private static void AssertHasProperty(Type type, string propertyName) =>
        Assert.IsNotNull(type.GetProperty(propertyName), $"{type.Name}.{propertyName} is missing.");

    private static IReadOnlyList<string> ProductionFiles() =>
    [
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "RollbackGateEvaluationModels.cs"),
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "RollbackGateEvaluator.cs")
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
}

