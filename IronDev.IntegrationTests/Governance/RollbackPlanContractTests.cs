using System.Reflection;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("RollbackPlanContract")]
public sealed class RollbackPlanContractTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 6, 16, 20, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void RollbackPlanContract_RequiresPatchDryRunPolicySourceAndBranchBinding()
    {
        foreach (var property in new[]
        {
            nameof(RollbackPlan.RollbackPlanId),
            nameof(RollbackPlan.ProjectId),
            nameof(RollbackPlan.PatchArtifactId),
            nameof(RollbackPlan.PatchHash),
            nameof(RollbackPlan.ChangeSetHash),
            nameof(RollbackPlan.ControlledDryRunRequestId),
            nameof(RollbackPlan.DryRunExecutionAuditId),
            nameof(RollbackPlan.DryRunAuditHash),
            nameof(RollbackPlan.DryRunReceiptHash),
            nameof(RollbackPlan.PolicySatisfactionId),
            nameof(RollbackPlan.PolicySatisfactionHash),
            nameof(RollbackPlan.SubjectKind),
            nameof(RollbackPlan.SubjectId),
            nameof(RollbackPlan.SubjectHash),
            nameof(RollbackPlan.SourceSnapshotReference),
            nameof(RollbackPlan.SourceBaselineHash),
            nameof(RollbackPlan.WorkspaceBoundaryHash),
            nameof(RollbackPlan.ExpectedBranch),
            nameof(RollbackPlan.ExpectedCleanWorktreeHash)
        })
        {
            AssertHasProperty(typeof(RollbackPlan), property);
        }

        AssertValid(ValidPlan());
    }

    [TestMethod]
    public void RollbackPlanContract_RequiresRollbackPlanHashAndFileActions()
    {
        AssertHasProperty(typeof(RollbackPlan), nameof(RollbackPlan.RollbackPlanHash));
        AssertHasProperty(typeof(RollbackPlan), nameof(RollbackPlan.FileActions));

        AssertInvalid(ValidPlan() with { RollbackPlanHash = " " }, "ROLLBACK_PLAN_HASH_REQUIRED");
        AssertInvalid(ValidPlan() with { FileActions = [] }, "FILE_ACTIONS_REQUIRED");
    }

    [TestMethod]
    public void RollbackPlanContract_RequiresEvidenceBoundaryAndCreatedTimestamp()
    {
        AssertHasProperty(typeof(RollbackPlan), nameof(RollbackPlan.CreatedAtUtc));
        AssertHasProperty(typeof(RollbackPlan), nameof(RollbackPlan.EvidenceReferences));
        AssertHasProperty(typeof(RollbackPlan), nameof(RollbackPlan.BoundaryMaxims));
        AssertHasProperty(typeof(RollbackPlan), nameof(RollbackPlan.Boundary));

        AssertInvalid(ValidPlan() with { CreatedAtUtc = default }, "CREATED_AT_UTC_REQUIRED");
        AssertInvalid(ValidPlan() with { ExpiresAtUtc = CreatedAtUtc.AddMinutes(-1) }, "EXPIRES_AT_UTC_INVALID");
        AssertInvalid(ValidPlan() with { EvidenceReferences = [] }, "EVIDENCE_REFERENCES_REQUIRED");
        AssertInvalid(ValidPlan() with { BoundaryMaxims = [] }, "BOUNDARY_MAXIMS_REQUIRED");
        AssertInvalid(ValidPlan() with { Boundary = " " }, "BOUNDARY_REQUIRED");
    }

    [TestMethod]
    public void RollbackPlanContract_RejectsMissingRollbackPlanId() =>
        AssertInvalid(ValidPlan() with { RollbackPlanId = Guid.Empty }, "ROLLBACK_PLAN_ID_REQUIRED");

    [TestMethod]
    public void RollbackPlanContract_RejectsMissingPatchArtifactId() =>
        AssertInvalid(ValidPlan() with { PatchArtifactId = Guid.Empty }, "PATCH_ARTIFACT_ID_REQUIRED");

    [TestMethod]
    public void RollbackPlanContract_RejectsMissingPatchHash() =>
        AssertInvalid(ValidPlan() with { PatchHash = " " }, "PATCH_HASH_REQUIRED");

    [TestMethod]
    public void RollbackPlanContract_RejectsMissingSourceBaselineHash() =>
        AssertInvalid(ValidPlan() with { SourceBaselineHash = " " }, "SOURCE_BASELINE_HASH_REQUIRED");

    [TestMethod]
    public void RollbackPlanContract_RejectsMissingExpectedBranch() =>
        AssertInvalid(ValidPlan() with { ExpectedBranch = " " }, "EXPECTED_BRANCH_REQUIRED");

    [TestMethod]
    public void RollbackPlanContract_RejectsMissingExpectedCleanWorktreeHash() =>
        AssertInvalid(ValidPlan() with { ExpectedCleanWorktreeHash = " " }, "EXPECTED_CLEAN_WORKTREE_HASH_REQUIRED");

    [TestMethod]
    public void RollbackPlanContract_RejectsMissingFileActions() =>
        AssertInvalid(ValidPlan() with { FileActions = [] }, "FILE_ACTIONS_REQUIRED");

    [TestMethod]
    public void RollbackPlanContract_RejectsUnsafePaths()
    {
        foreach (var path in new[] { "../x.cs", @"C:\repo\x.cs", @"\\server\share\x.cs", ".git/config", "src/.git/config", "/" })
        {
            AssertInvalid(ValidPlan() with { FileActions = [RestoreAction(path)] }, "ROLLBACK_FILE_ACTION_PATH_UNSAFE");
        }
    }

    [TestMethod]
    public void RollbackPlanContract_AcceptsSafeRelativePaths()
    {
        foreach (var path in new[] { "src/IronDev.Core/Foo.cs", "tests/IronDev.Tests/FooTests.cs", "Docs/receipts/PR194_ROLLBACK_PLAN_CONTRACT.md" })
        {
            AssertValid(ValidPlan() with { FileActions = [RestoreAction(path)] });
        }
    }

    [TestMethod]
    public void RollbackPlanContract_RejectsInvalidActionKind() =>
        AssertInvalid(ValidPlan() with { FileActions = [RestoreAction("src/Foo.cs") with { PlannedActionKind = "Move" }] }, "ROLLBACK_FILE_ACTION_KIND_INVALID");

    [TestMethod]
    public void RollbackPlanContract_ValidatesRestoreModifiedFileAction()
    {
        AssertValid(ValidPlan() with { FileActions = [RestoreAction("src/Foo.cs")] });
        AssertInvalid(ValidPlan() with { FileActions = [RestoreAction("src/Foo.cs") with { RestoreContentHash = " " }] }, "RESTORE_CONTENT_HASH_REQUIRED");
        AssertInvalid(ValidPlan() with { FileActions = [RestoreAction("src/Foo.cs") with { DeleteContentHash = "sha256:delete" }] }, "DELETE_CONTENT_HASH_FORBIDDEN");
        AssertInvalid(ValidPlan() with { FileActions = [RestoreAction("src/Foo.cs") with { PreviousPath = "src/Old.cs" }] }, "PREVIOUS_PATH_FORBIDDEN");
    }

    [TestMethod]
    public void RollbackPlanContract_ValidatesDeleteCreatedFileAction()
    {
        AssertValid(ValidPlan() with { FileActions = [DeleteCreatedAction("src/New.cs")] });
        AssertInvalid(ValidPlan() with { FileActions = [DeleteCreatedAction("src/New.cs") with { DeleteContentHash = " " }] }, "DELETE_CONTENT_HASH_REQUIRED");
        AssertInvalid(ValidPlan() with { FileActions = [DeleteCreatedAction("src/New.cs") with { RestoreContentHash = "sha256:restore" }] }, "RESTORE_CONTENT_HASH_FORBIDDEN");
        AssertInvalid(ValidPlan() with { FileActions = [DeleteCreatedAction("src/New.cs") with { PreviousPath = "src/Old.cs" }] }, "PREVIOUS_PATH_FORBIDDEN");
    }

    [TestMethod]
    public void RollbackPlanContract_ValidatesRecreateDeletedFileAction()
    {
        AssertValid(ValidPlan() with { FileActions = [RecreateDeletedAction("src/Old.cs")] });
        AssertInvalid(ValidPlan() with { FileActions = [RecreateDeletedAction("src/Old.cs") with { RestoreContentHash = " " }] }, "RESTORE_CONTENT_HASH_REQUIRED");
        AssertInvalid(ValidPlan() with { FileActions = [RecreateDeletedAction("src/Old.cs") with { DeleteContentHash = "sha256:delete" }] }, "DELETE_CONTENT_HASH_FORBIDDEN");
        AssertInvalid(ValidPlan() with { FileActions = [RecreateDeletedAction("src/Old.cs") with { PreviousPath = "src/Older.cs" }] }, "PREVIOUS_PATH_FORBIDDEN");
    }

    [TestMethod]
    public void RollbackPlanContract_ValidatesRenameBackAction()
    {
        AssertValid(ValidPlan() with { FileActions = [RenameBackAction("src/New.cs", "src/Old.cs")] });
        AssertInvalid(ValidPlan() with { FileActions = [RenameBackAction("src/New.cs", " ")] }, "PREVIOUS_PATH_REQUIRED");
        AssertInvalid(ValidPlan() with { FileActions = [RenameBackAction("src/New.cs", "src/Old.cs") with { RestoreContentHash = " " }] }, "RESTORE_CONTENT_HASH_REQUIRED");
        AssertInvalid(ValidPlan() with { FileActions = [RenameBackAction("src/New.cs", "src/Old.cs") with { DeleteContentHash = "sha256:delete" }] }, "DELETE_CONTENT_HASH_FORBIDDEN");
    }

    [TestMethod]
    public void RollbackPlanContract_ValidatesNoopAction()
    {
        AssertValid(ValidPlan() with { FileActions = [NoopAction("src/MetadataOnly.cs")] });
        AssertInvalid(ValidPlan() with { FileActions = [NoopAction("src/MetadataOnly.cs") with { ExpectedCurrentContentHash = " " }] }, "ROLLBACK_FILE_ACTION_EXPECTED_CURRENT_CONTENT_HASH_REQUIRED");
        AssertInvalid(ValidPlan() with { FileActions = [NoopAction("src/MetadataOnly.cs") with { RollbackActionHash = " " }] }, "ROLLBACK_FILE_ACTION_HASH_REQUIRED");
        AssertInvalid(ValidPlan() with { FileActions = [NoopAction("src/MetadataOnly.cs") with { RestoreContentHash = "sha256:restore" }] }, "RESTORE_CONTENT_HASH_FORBIDDEN");
        AssertInvalid(ValidPlan() with { FileActions = [NoopAction("src/MetadataOnly.cs") with { DeleteContentHash = "sha256:delete" }] }, "DELETE_CONTENT_HASH_FORBIDDEN");
        AssertInvalid(ValidPlan() with { FileActions = [NoopAction("src/MetadataOnly.cs") with { PreviousPath = "src/Old.cs" }] }, "PREVIOUS_PATH_FORBIDDEN");
    }

    [TestMethod]
    public void RollbackPlanContract_RejectsPrivateRawMaterial()
    {
        foreach (var marker in new[] { "raw prompt", "raw completion", "raw tool output", "chain-of-thought", "private reasoning", "scratchpad", "secret" })
        {
            AssertInvalid(ValidPlan() with { EvidenceReferences = [$"evidence contains {marker}"] }, "PRIVATE_OR_RAW_MATERIAL_REJECTED");
            AssertInvalid(ValidPlan() with { BoundaryMaxims = [$"maxim contains {marker}"] }, "PRIVATE_OR_RAW_MATERIAL_REJECTED");
            AssertInvalid(ValidPlan() with { Boundary = $"boundary contains {marker}" }, "PRIVATE_OR_RAW_MATERIAL_REJECTED");
            AssertInvalid(ValidPlan() with { ExpectedBranch = $"feature/{marker}" }, "PRIVATE_OR_RAW_MATERIAL_REJECTED");
            AssertInvalid(ValidPlan() with { FileActions = [RestoreAction($"src/{marker}.cs")] }, "PRIVATE_OR_RAW_MATERIAL_REJECTED");
            AssertInvalid(ValidPlan() with { FileActions = [RenameBackAction("src/New.cs", $"src/{marker}.cs")] }, "PRIVATE_OR_RAW_MATERIAL_REJECTED");
        }
    }

    [TestMethod]
    public void RollbackPlanContract_RejectsAuthorityClaims()
    {
        foreach (var marker in new[] { "source applied", "rollback executed", "rollback succeeded", "workflow continued", "release ready" })
        {
            AssertInvalid(ValidPlan() with { EvidenceReferences = [$"evidence claims {marker}"] }, "AUTHORITY_CLAIM_REJECTED");
            AssertInvalid(ValidPlan() with { BoundaryMaxims = [$"maxim claims {marker}"] }, "AUTHORITY_CLAIM_REJECTED");
            AssertInvalid(ValidPlan() with { ExpectedBranch = $"feature/{marker}" }, "AUTHORITY_CLAIM_REJECTED");
        }
    }

    [TestMethod]
    public void RollbackPlanContract_BoundaryStatesPlanIsNotExecution()
    {
        foreach (var statement in BoundaryStatements())
        {
            StringAssert.Contains(RollbackPlanBoundaryText.Boundary, statement);
        }

        AssertValid(ValidPlan());
    }

    [TestMethod]
    public void RollbackPlanContract_DoesNotExposeExecutionOrApplyAuthority()
    {
        var properties = typeof(RollbackPlan)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();

        foreach (var forbidden in new[] { "ExecutedAtUtc", "ExecutionResult", "RollbackSucceeded", "CanRollback", "CanApplySource", "SourceApplied", "WorkflowContinued", "ReleaseReady" })
        {
            Assert.IsFalse(properties.Contains(forbidden, StringComparer.Ordinal), $"RollbackPlan must not expose {forbidden}.");
        }
    }

    [TestMethod]
    public void RollbackPlanContract_DoesNotAddRollbackExecutionOrSourceApply()
    {
        foreach (var token in new[]
        {
            "ExecuteRollback",
            "RollbackExecutor",
            "ApplySourceAsync",
            "SourceApplyService",
            "ControlledSourceApply",
            "ContinueWorkflowAsync",
            "ApproveReleaseAsync",
            "ReleaseReady = true",
            "CanApplySource = true"
        })
        {
            AssertNoProductionToken(token);
        }
    }

    [TestMethod]
    public void RollbackPlanContract_DoesNotAddSqlApiCliUiRuntime()
    {
        foreach (var file in Pr194ChangedFiles())
        {
            var relative = file.Replace('\\', '/');
            foreach (var token in new[] { "Database", "Controller", "Program.cs", "Cli", "Tauri", "UI", "IHostedService", "BackgroundService", "Scheduler" })
            {
                Assert.IsFalse(relative.Contains(token, StringComparison.OrdinalIgnoreCase), $"PR194 must not add {token}: {relative}");
            }
        }
    }

    [TestMethod]
    public void RollbackPlanContract_DoesNotCallModelsAgentsMemoryRetrieval()
    {
        foreach (var token in new[] { "LLM", "model call", "AgentDispatch", "ToolExecution", "PromoteMemory", "ActivateRetrieval", "Vector", "Embedding", "Weaviate" })
        {
            AssertNoProductionToken(token);
        }
    }

    [TestMethod]
    public void RollbackPlanContract_ReceiptStatesBoundary()
    {
        var receipt = File.ReadAllText(ReceiptPath());

        foreach (var statement in new[]
        {
            "PR194 adds the Rollback Plan contract.",
            "This PR is contract/test/receipt only.",
            "This PR defines rollback plan shape.",
            "This PR does not execute rollback.",
            "This PR does not apply source.",
            "This PR does not mutate source.",
            "This PR does not continue workflow.",
            "This PR does not approve release.",
            "This PR does not add SQL.",
            "This PR does not add API.",
            "This PR does not add CLI.",
            "This PR does not add UI.",
            "Rollback plan binds patch artifact, patch hash, change-set hash, dry-run receipt hash, dry-run audit hash, policy satisfaction hash, subject hash, source baseline hash, workspace boundary hash, expected branch, expected clean worktree hash, rollback plan hash, file actions, evidence references, and boundary maxims.",
            "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate",
            "The next Block U target is Rollback Gate Evaluator.",
            "PR195 - Rollback Gate Evaluator",
            "PR194 defines the escape hatch. It does not open it."
        })
        {
            StringAssert.Contains(receipt, statement);
        }

        foreach (var statement in BoundaryStatements())
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    private static RollbackPlan ValidPlan() => new()
    {
        RollbackPlanId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        ProjectId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
        RollbackPlanKind = "PatchArtifactRollbackPlan",
        PatchArtifactId = Guid.Parse("22222222-3333-4444-5555-666666666666"),
        PatchHash = "sha256:patch",
        ChangeSetHash = "sha256:change-set",
        ControlledDryRunRequestId = Guid.Parse("33333333-4444-5555-6666-777777777777"),
        DryRunExecutionAuditId = Guid.Parse("44444444-5555-6666-7777-888888888888"),
        DryRunAuditHash = "sha256:dry-run-audit",
        DryRunReceiptHash = "sha256:dry-run-receipt",
        PolicySatisfactionId = Guid.Parse("99999999-8888-7777-6666-555555555555"),
        PolicySatisfactionHash = "sha256:policy-satisfaction",
        SubjectKind = "PatchProposal",
        SubjectId = "patch-proposal-123",
        SubjectHash = "sha256:subject",
        SourceSnapshotReference = "source-snapshot:abc123",
        SourceBaselineHash = "sha256:source-baseline",
        WorkspaceBoundaryHash = "sha256:workspace-boundary",
        ExpectedBranch = "feature/safe-apply",
        ExpectedCleanWorktreeHash = "sha256:clean-worktree",
        RollbackPlanHash = "sha256:rollback-plan",
        FileActions = [RestoreAction("src/IronDev.Core/Foo.cs")],
        CreatedAtUtc = CreatedAtUtc,
        ExpiresAtUtc = CreatedAtUtc.AddHours(1),
        EvidenceReferences = ["patch-artifact:22222222-3333-4444-5555-666666666666"],
        BoundaryMaxims = ["Rollback plan defines the intended escape hatch only."],
        Boundary = RollbackPlanBoundaryText.Boundary
    };

    private static RollbackPlanFileAction RestoreAction(string path) => new()
    {
        Path = path,
        PlannedActionKind = "RestoreModifiedFile",
        RestoreContentHash = $"sha256:restore-{path}",
        ExpectedCurrentContentHash = $"sha256:current-{path}",
        RollbackActionHash = $"sha256:action-{path}",
        IsBinary = false
    };

    private static RollbackPlanFileAction DeleteCreatedAction(string path) => new()
    {
        Path = path,
        PlannedActionKind = "DeleteCreatedFile",
        DeleteContentHash = $"sha256:delete-{path}",
        ExpectedCurrentContentHash = $"sha256:current-{path}",
        RollbackActionHash = $"sha256:action-{path}",
        IsBinary = false
    };

    private static RollbackPlanFileAction RecreateDeletedAction(string path) => new()
    {
        Path = path,
        PlannedActionKind = "RecreateDeletedFile",
        RestoreContentHash = $"sha256:restore-{path}",
        ExpectedCurrentContentHash = $"sha256:current-{path}",
        RollbackActionHash = $"sha256:action-{path}",
        IsBinary = false
    };

    private static RollbackPlanFileAction RenameBackAction(string path, string previousPath) => new()
    {
        Path = path,
        PreviousPath = previousPath,
        PlannedActionKind = "RenameBack",
        RestoreContentHash = $"sha256:restore-{path}",
        ExpectedCurrentContentHash = $"sha256:current-{path}",
        RollbackActionHash = $"sha256:action-{path}",
        IsBinary = false
    };

    private static RollbackPlanFileAction NoopAction(string path) => new()
    {
        Path = path,
        PlannedActionKind = "Noop",
        ExpectedCurrentContentHash = $"sha256:current-{path}",
        RollbackActionHash = $"sha256:action-{path}",
        IsBinary = false
    };

    private static string[] BoundaryStatements() =>
    [
        "Rollback plan is not rollback execution.",
        "Rollback plan is not rollback success.",
        "Rollback plan is not source apply.",
        "Rollback plan is not workflow continuation.",
        "Rollback plan is not release readiness.",
        "Rollback plan does not authorize source mutation by itself.",
        "Rollback plan defines the intended escape hatch only.",
        "Real source apply must require rollback support before mutation."
    ];

    private static void AssertValid(RollbackPlan plan)
    {
        var result = RollbackPlanValidation.Validate(plan);
        Assert.IsTrue(result.IsValid, string.Join(Environment.NewLine, result.Issues.Select(issue => $"{issue.Code}:{issue.Field}:{issue.Message}")));
    }

    private static void AssertInvalid(RollbackPlan plan, string expectedCode)
    {
        var result = RollbackPlanValidation.Validate(plan);
        Assert.IsFalse(result.IsValid, "Expected rollback plan to be invalid.");
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == expectedCode), $"Expected issue code {expectedCode}. Actual: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
    }

    private static void AssertHasProperty(Type type, string propertyName) =>
        Assert.IsNotNull(type.GetProperty(propertyName), $"Expected property {propertyName}.");

    private static void AssertNoProductionToken(string token)
    {
        foreach (var file in Pr194ProductionFiles())
        {
            Assert.IsFalse(File.ReadAllText(file).Contains(token, StringComparison.Ordinal), $"Unexpected production token {token} in {file}.");
        }
    }

    private static string[] Pr194ProductionFiles() =>
    [
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "RollbackPlan.cs"),
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "RollbackPlanValidation.cs")
    ];

    private static string[] Pr194ChangedFiles() =>
    [
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "RollbackPlan.cs"),
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "RollbackPlanValidation.cs"),
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR194_ROLLBACK_PLAN_CONTRACT.md"),
        Path.Combine(RepoRoot(), "IronDev.IntegrationTests", "Governance", "RollbackPlanContractTests.cs")
    ];

    private static string ReceiptPath() =>
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR194_ROLLBACK_PLAN_CONTRACT.md");

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
