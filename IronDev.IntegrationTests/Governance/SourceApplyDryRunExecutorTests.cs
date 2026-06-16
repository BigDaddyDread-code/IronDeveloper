using System.Reflection;
using System.Text.Json;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("SourceApplyDryRunExecutor")]
public sealed class SourceApplyDryRunExecutorTests
{
    private static readonly DateTimeOffset RequestedAtUtc = new(2026, 6, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAtUtc = RequestedAtUtc.AddHours(2);

    [TestMethod]
    public void SourceApplyDryRunExecutor_SatisfiedWhenRequestAndSnapshotAreConsistent()
    {
        var request = ValidDryRunRequest();

        var result = SourceApplyDryRunExecutor.Execute(request);

        Assert.IsTrue(result.Satisfied, string.Join(", ", result.Issues.Select(issue => issue.Code)));
        Assert.AreEqual(request.SourceApplyDryRunRequestId, result.SourceApplyDryRunRequestId);
        Assert.AreEqual(request.ProjectId, result.ProjectId);
        Assert.AreEqual(request.SourceApplyRequest.SourceApplyRequestId, result.SourceApplyRequestId);
        Assert.AreEqual(request.SourceApplyRequest.SourceApplyRequestHash, result.SourceApplyRequestHash);
        Assert.AreEqual(request.SourceApplyRequest.PatchArtifactId, result.PatchArtifactId);
        Assert.AreEqual(request.SourceApplyRequest.RollbackSupportReceiptId, result.RollbackSupportReceiptId);
        Assert.AreEqual(request.SourceApplyRequest.SourceBaselineHash, result.SourceBaselineHash);
        Assert.AreEqual(request.SourceApplyRequest.WorkspaceBoundaryHash, result.WorkspaceBoundaryHash);
        Assert.AreEqual(request.SourceApplyRequest.ExpectedBranch, result.ExpectedBranch);
        Assert.AreEqual(request.SourceApplyRequest.ExpectedCleanWorktreeHash, result.ExpectedCleanWorktreeHash);
        CollectionAssert.Contains(result.EvidenceReferences.ToArray(), "dry-run-request:evidence");
        CollectionAssert.Contains(result.EvidenceReferences.ToArray(), "source-apply-gate:1");
        StringAssert.Contains(result.Boundary, "not source apply");
        StringAssert.Contains(result.Boundary, "not git execution");
    }

    [TestMethod]
    public void SourceApplyDryRunExecutor_ProducesWouldFlagsForEachOperationKind()
    {
        var result = SourceApplyDryRunExecutor.Execute(ValidDryRunRequest());

        AssertFlag(result, "src/new.cs", create: true);
        AssertFlag(result, "src/feature.cs", modify: true);
        AssertFlag(result, "src/delete.cs", delete: true);
        AssertFlag(result, "src/renamed.cs", rename: true);
        AssertFlag(result, "src/noop.cs", noop: true);
        Assert.IsTrue(result.FileResults.All(file => file.PreconditionsSatisfied));
    }

    [TestMethod]
    public void SourceApplyDryRunExecutor_RejectsInvalidSourceApplyRequest()
    {
        var sourceRequest = ValidSourceApplyRequest() with
        {
            SourceApplyGateSatisfied = false,
            SourceApplyGateEvaluation = ValidGateEvidence() with { Satisfied = false }
        };

        var result = SourceApplyDryRunExecutor.Execute(ValidDryRunRequest() with { SourceApplyRequest = sourceRequest });

        Assert.IsFalse(result.Satisfied);
        AssertHasIssue(result, "SOURCE_APPLY_REQUEST_INVALID");
    }

    [TestMethod]
    public void SourceApplyDryRunExecutor_RejectsProjectMismatch()
    {
        var result = SourceApplyDryRunExecutor.Execute(ValidDryRunRequest() with { ProjectId = Guid.NewGuid() });

        Assert.IsFalse(result.Satisfied);
        AssertHasIssue(result, "PROJECT_ID_MISMATCH");
    }

    [TestMethod]
    public void SourceApplyDryRunExecutor_RejectsWorkspaceSnapshotMismatch()
    {
        var snapshot = ValidSnapshot() with
        {
            SourceBaselineHash = "sha256:other-source-baseline",
            WorkspaceBoundaryHash = "sha256:other-workspace-boundary",
            ExpectedBranch = "release/other",
            ExpectedCleanWorktreeHash = "sha256:other-clean-worktree"
        };

        var result = SourceApplyDryRunExecutor.Execute(ValidDryRunRequest() with { WorkspaceSnapshot = snapshot });

        Assert.IsFalse(result.Satisfied);
        AssertHasIssue(result, "SNAPSHOT_SOURCE_BASELINE_MISMATCH");
        AssertHasIssue(result, "SNAPSHOT_WORKSPACE_BOUNDARY_MISMATCH");
        AssertHasIssue(result, "SNAPSHOT_EXPECTED_BRANCH_MISMATCH");
        AssertHasIssue(result, "SNAPSHOT_EXPECTED_CLEAN_WORKTREE_HASH_MISMATCH");
    }

    [TestMethod]
    public void SourceApplyDryRunExecutor_RejectsCreateTargetAlreadyExists()
    {
        var snapshot = ValidSnapshot() with
        {
            Files = ReplaceFile(ValidSnapshot().Files, new SourceApplyDryRunWorkspaceFile
            {
                Path = "src/new.cs",
                CurrentContentHash = "sha256:existing-new",
                Exists = true
            })
        };

        var result = SourceApplyDryRunExecutor.Execute(ValidDryRunRequest() with { WorkspaceSnapshot = snapshot });

        Assert.IsFalse(result.Satisfied);
        AssertHasIssue(result, "CREATE_TARGET_ALREADY_EXISTS");
    }

    [TestMethod]
    public void SourceApplyDryRunExecutor_RejectsModifyTargetMissingOrHashMismatch()
    {
        var missing = SourceApplyDryRunExecutor.Execute(ValidDryRunRequest() with
        {
            WorkspaceSnapshot = ValidSnapshot() with
            {
                Files = ReplaceFile(ValidSnapshot().Files, new SourceApplyDryRunWorkspaceFile
                {
                    Path = "src/feature.cs",
                    CurrentContentHash = "sha256:before-mod",
                    Exists = false
                })
            }
        });

        var mismatch = SourceApplyDryRunExecutor.Execute(ValidDryRunRequest() with
        {
            WorkspaceSnapshot = ValidSnapshot() with
            {
                Files = ReplaceFile(ValidSnapshot().Files, new SourceApplyDryRunWorkspaceFile
                {
                    Path = "src/feature.cs",
                    CurrentContentHash = "sha256:changed-mod",
                    Exists = true
                })
            }
        });

        AssertHasIssue(missing, "MODIFY_TARGET_MISSING");
        AssertHasIssue(mismatch, "CURRENT_FILE_HASH_MISMATCH");
    }

    [TestMethod]
    public void SourceApplyDryRunExecutor_RejectsDeleteTargetMissingOrHashMismatch()
    {
        var missing = SourceApplyDryRunExecutor.Execute(ValidDryRunRequest() with
        {
            WorkspaceSnapshot = ValidSnapshot() with
            {
                Files = ReplaceFile(ValidSnapshot().Files, new SourceApplyDryRunWorkspaceFile
                {
                    Path = "src/delete.cs",
                    CurrentContentHash = "sha256:before-delete",
                    Exists = false
                })
            }
        });

        var mismatch = SourceApplyDryRunExecutor.Execute(ValidDryRunRequest() with
        {
            WorkspaceSnapshot = ValidSnapshot() with
            {
                Files = ReplaceFile(ValidSnapshot().Files, new SourceApplyDryRunWorkspaceFile
                {
                    Path = "src/delete.cs",
                    CurrentContentHash = "sha256:changed-delete",
                    Exists = true
                })
            }
        });

        AssertHasIssue(missing, "DELETE_TARGET_MISSING");
        AssertHasIssue(mismatch, "CURRENT_FILE_HASH_MISMATCH");
    }

    [TestMethod]
    public void SourceApplyDryRunExecutor_RejectsRenameSourceMissingOrTargetExists()
    {
        var sourceMissing = SourceApplyDryRunExecutor.Execute(ValidDryRunRequest() with
        {
            WorkspaceSnapshot = ValidSnapshot() with
            {
                Files = ReplaceFile(ValidSnapshot().Files, new SourceApplyDryRunWorkspaceFile
                {
                    Path = "src/old-name.cs",
                    CurrentContentHash = "sha256:before-rename",
                    Exists = false
                })
            }
        });

        var targetExists = SourceApplyDryRunExecutor.Execute(ValidDryRunRequest() with
        {
            WorkspaceSnapshot = ValidSnapshot() with
            {
                Files = ReplaceFile(ValidSnapshot().Files, new SourceApplyDryRunWorkspaceFile
                {
                    Path = "src/renamed.cs",
                    CurrentContentHash = "sha256:target-exists",
                    Exists = true
                })
            }
        });

        AssertHasIssue(sourceMissing, "RENAME_SOURCE_MISSING");
        AssertHasIssue(targetExists, "RENAME_TARGET_ALREADY_EXISTS");
    }

    [TestMethod]
    public void SourceApplyDryRunExecutor_RejectsUnsafePaths()
    {
        var result = SourceApplyDryRunExecutor.Execute(ValidDryRunRequest() with
        {
            SourceApplyRequest = ValidSourceApplyRequest() with
            {
                FileOperations = [ModifyOperation("../secret.cs", before: "sha256:before-mod", after: "sha256:after-mod")]
            }
        });

        Assert.IsFalse(result.Satisfied);
        AssertHasIssue(result, "FILE_OPERATION_PATH_UNSAFE");
    }

    [TestMethod]
    public void SourceApplyDryRunExecutor_RejectsInvalidOperationKind()
    {
        var result = SourceApplyDryRunExecutor.Execute(ValidDryRunRequest() with
        {
            SourceApplyRequest = ValidSourceApplyRequest() with
            {
                FileOperations = [ModifyOperation("src/feature.cs", before: "sha256:before-mod", after: "sha256:after-mod") with { OperationKind = "RunGitApply" }]
            }
        });

        Assert.IsFalse(result.Satisfied);
        AssertHasIssue(result, "FILE_OPERATION_KIND_INVALID");
    }

    [TestMethod]
    public void SourceApplyDryRunExecutor_RejectsMissingOperationInputs()
    {
        var result = SourceApplyDryRunExecutor.Execute(ValidDryRunRequest() with
        {
            SourceApplyRequest = ValidSourceApplyRequest() with
            {
                FileOperations =
                [
                    ModifyOperation("src/feature.cs", before: " ", after: "sha256:after-mod") with
                    {
                        PatchArtifactChangeHash = " ",
                        OperationHash = " "
                    }
                ]
            }
        });

        Assert.IsFalse(result.Satisfied);
        AssertHasIssue(result, "BEFORE_CONTENT_HASH_REQUIRED");
        AssertHasIssue(result, "PATCH_ARTIFACT_CHANGE_HASH_REQUIRED");
        AssertHasIssue(result, "FILE_OPERATION_HASH_REQUIRED");
    }

    [TestMethod]
    public void SourceApplyDryRunExecutor_RejectsPrivateRawMaterial()
    {
        var result = SourceApplyDryRunExecutor.Execute(ValidDryRunRequest() with
        {
            EvidenceReferences = ["raw prompt leaked"],
            SourceApplyRequest = ValidSourceApplyRequest() with
            {
                ExpectedBranch = "chain-of-thought leaked",
                FileOperations = [ModifyOperation("src/raw prompt leaked.cs", before: "sha256:before-mod", after: "sha256:after-mod")]
            }
        });
        var serialized = JsonSerializer.Serialize(result);

        Assert.IsFalse(result.Satisfied);
        AssertHasIssue(result, "PRIVATE_OR_RAW_MATERIAL_REJECTED");
        Assert.IsFalse(serialized.Contains("raw prompt leaked", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains("chain-of-thought leaked", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void SourceApplyDryRunExecutor_RejectsAuthorityClaims()
    {
        var result = SourceApplyDryRunExecutor.Execute(ValidDryRunRequest() with
        {
            BoundaryMaxims = ["source applied and workflow continued"],
            SourceApplyRequest = ValidSourceApplyRequest() with
            {
                BoundaryMaxims = ["release ready"]
            }
        });
        var serialized = JsonSerializer.Serialize(result);

        Assert.IsFalse(result.Satisfied);
        AssertHasIssue(result, "AUTHORITY_CLAIM_REJECTED");
        Assert.IsFalse(serialized.Contains("source applied and workflow continued", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains("release ready", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void SourceApplyDryRunExecutor_DoesNotExposeMutationAuthority()
    {
        var methodNames = typeof(SourceApplyDryRunExecutor)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.Name)
            .ToArray();
        CollectionAssert.AreEquivalent(new[] { nameof(SourceApplyDryRunExecutor.Execute) }, methodNames);

        var publicNames = new[]
            {
                typeof(SourceApplyDryRunRequest),
                typeof(SourceApplyDryRunResult),
                typeof(SourceApplyDryRunFileResult),
                typeof(SourceApplyDryRunWorkspaceSnapshot),
                typeof(SourceApplyDryRunWorkspaceFile)
            }
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Select(property => property.Name)
            .ToArray();

        foreach (var forbidden in new[]
        {
            "SourceApplied",
            "SourceApplySucceeded",
            "MutationOccurred",
            "MutationAllowed",
            "AppliedAtUtc",
            "CommitCreated",
            "GitApplied",
            "WorkflowCanContinue",
            "ReleaseReady",
            "ReceiptWritten",
            "RollbackExecuted"
        })
        {
            Assert.IsFalse(publicNames.Any(name => name.Equals(forbidden, StringComparison.Ordinal)), forbidden);
        }
    }

    [TestMethod]
    public void SourceApplyDryRunExecutor_DoesNotAddGitProcessFilesystemRuntimeSqlApiCliUi()
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
                "Directory.GetFiles",
                "Directory.EnumerateFiles",
                "Directory.CreateDirectory",
                "git status",
                "git rev-parse",
                "IHostedService",
                "BackgroundService",
                "SourceApplyReceiptStore",
                "CreateSourceApplyReceipt",
                "RollbackExecutor",
                "WorkflowContinuationService",
                "ReleaseReadiness",
                "AgentDispatcher",
                "ModelProvider",
                "ToolInvoker",
                "MemoryPromotion",
                "RetrievalActivation"
            })
            {
                Assert.IsFalse(text.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"{Path.GetFileName(path)} contains forbidden token {forbidden}");
            }
        }
    }

    [TestMethod]
    public void SourceApplyDryRunExecutor_ReceiptDocumentsBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "PR201_SOURCE_APPLY_EXECUTOR_DRY_RUN_MODE.md"));

        StringAssert.Contains(receipt, "PR201 adds source-apply executor dry-run mode only.");
        StringAssert.Contains(receipt, "PR201 does not add real source apply.");
        StringAssert.Contains(receipt, "PR201 does not mutate source.");
        StringAssert.Contains(receipt, "PR201 does not write files.");
        StringAssert.Contains(receipt, "PR201 does not call git.");
        StringAssert.Contains(receipt, "Source apply dry-run is not source apply.");
        StringAssert.Contains(receipt, "does not ignite the engines");
    }

    private static void AssertFlag(
        SourceApplyDryRunResult result,
        string path,
        bool create = false,
        bool modify = false,
        bool delete = false,
        bool rename = false,
        bool noop = false)
    {
        var file = result.FileResults.Single(item => item.Path == path);
        Assert.AreEqual(create, file.WouldCreate, path);
        Assert.AreEqual(modify, file.WouldModify, path);
        Assert.AreEqual(delete, file.WouldDelete, path);
        Assert.AreEqual(rename, file.WouldRename, path);
        Assert.AreEqual(noop, file.WouldNoop, path);
    }

    private static SourceApplyDryRunRequest ValidDryRunRequest() => new()
    {
        SourceApplyDryRunRequestId = Ids.SourceApplyDryRunRequestId,
        ProjectId = Ids.ProjectId,
        SourceApplyRequest = ValidSourceApplyRequest(),
        WorkspaceSnapshot = ValidSnapshot(),
        RequestedAtUtc = RequestedAtUtc,
        EvidenceReferences = ["dry-run-request:evidence"],
        BoundaryMaxims = ["source apply dry-run is not source apply"],
        Boundary = SourceApplyDryRunBoundaryText.Boundary
    };

    private static SourceApplyRequest ValidSourceApplyRequest() => new()
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
        FileOperations = ValidOperations(),
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

    private static IReadOnlyList<SourceApplyRequestFileOperation> ValidOperations() =>
    [
        new SourceApplyRequestFileOperation
        {
            Path = "src/new.cs",
            OperationKind = SourceApplyRequestFileOperationKinds.CreateFile,
            AfterContentHash = "sha256:after-new",
            PatchArtifactChangeHash = "sha256:patch-change-create",
            OperationHash = "sha256:operation-create"
        },
        ModifyOperation("src/feature.cs", before: "sha256:before-mod", after: "sha256:after-mod"),
        new SourceApplyRequestFileOperation
        {
            Path = "src/delete.cs",
            OperationKind = SourceApplyRequestFileOperationKinds.DeleteFile,
            BeforeContentHash = "sha256:before-delete",
            PatchArtifactChangeHash = "sha256:patch-change-delete",
            OperationHash = "sha256:operation-delete"
        },
        new SourceApplyRequestFileOperation
        {
            PreviousPath = "src/old-name.cs",
            Path = "src/renamed.cs",
            OperationKind = SourceApplyRequestFileOperationKinds.RenameFile,
            PatchArtifactChangeHash = "sha256:patch-change-rename",
            OperationHash = "sha256:operation-rename"
        },
        new SourceApplyRequestFileOperation
        {
            Path = "src/noop.cs",
            OperationKind = SourceApplyRequestFileOperationKinds.Noop,
            PatchArtifactChangeHash = "sha256:patch-change-noop",
            OperationHash = "sha256:operation-noop"
        }
    ];

    private static SourceApplyRequestFileOperation ModifyOperation(string path, string before, string after) => new()
    {
        Path = path,
        OperationKind = SourceApplyRequestFileOperationKinds.ModifyFile,
        BeforeContentHash = before,
        AfterContentHash = after,
        DiffHash = "sha256:diff",
        PatchArtifactChangeHash = "sha256:patch-change-modify",
        OperationHash = "sha256:operation-modify"
    };

    private static SourceApplyDryRunWorkspaceSnapshot ValidSnapshot() => new()
    {
        SourceBaselineHash = "sha256:source-baseline",
        WorkspaceBoundaryHash = "sha256:workspace-boundary",
        ExpectedBranch = "main",
        ExpectedCleanWorktreeHash = "sha256:clean-worktree",
        Files =
        [
            new SourceApplyDryRunWorkspaceFile { Path = "src/new.cs", CurrentContentHash = "sha256:none", Exists = false },
            new SourceApplyDryRunWorkspaceFile { Path = "src/feature.cs", CurrentContentHash = "sha256:before-mod", Exists = true },
            new SourceApplyDryRunWorkspaceFile { Path = "src/delete.cs", CurrentContentHash = "sha256:before-delete", Exists = true },
            new SourceApplyDryRunWorkspaceFile { Path = "src/old-name.cs", CurrentContentHash = "sha256:before-rename", Exists = true },
            new SourceApplyDryRunWorkspaceFile { Path = "src/renamed.cs", CurrentContentHash = "sha256:none", Exists = false },
            new SourceApplyDryRunWorkspaceFile { Path = "src/noop.cs", CurrentContentHash = "sha256:noop", Exists = true }
        ]
    };

    private static IReadOnlyList<SourceApplyDryRunWorkspaceFile> ReplaceFile(IReadOnlyList<SourceApplyDryRunWorkspaceFile> files, SourceApplyDryRunWorkspaceFile replacement) =>
        files.Select(file => file.Path == replacement.Path ? replacement : file).ToArray();

    private static void AssertHasIssue(SourceApplyDryRunResult result, string code) =>
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == code) || result.FileResults.SelectMany(file => file.Issues).Any(issue => issue.Code == code),
            $"Expected issue {code}. Actual: {string.Join(", ", result.Issues.Select(issue => issue.Code).Concat(result.FileResults.SelectMany(file => file.Issues).Select(issue => issue.Code)))}");

    private static IReadOnlyList<string> ProductionFiles() =>
    [
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "SourceApplyDryRunModels.cs"),
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "SourceApplyDryRunExecutor.cs")
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
        public static readonly Guid SourceApplyDryRunRequestId = Guid.Parse("01000000-0000-0000-0000-000000000001");
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
