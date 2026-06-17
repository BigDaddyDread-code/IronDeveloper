using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;
using IronDev.Infrastructure.Governance;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("ControlledRollbackExecutor")]
[TestCategory("RollbackExecutionReceipt")]
[TestCategory("PR206")]
public sealed class ControlledRollbackExecutorTests
{
    [TestMethod]
    public async Task ControlledRollbackExecutor_CoversRestoreDeleteRecreateRenameBackAndNoop()
    {
        var workspace = Workspace();
        var fixture = RollbackFixture.Create(workspace,
        [
            Op.Modify("src/modified.txt", "old", "new"),
            Op.Create("src/created.txt", "created"),
            Op.Delete("src/deleted.txt", "deleted"),
            Op.Rename("src/old-name.txt", "src/new-name.txt", "move"),
            Op.Noop("src/noop.txt", "stay")
        ]);
        fixture.WriteAppliedState();
        var store = new RecordingRollbackStore();

        var result = await new ControlledRollbackExecutor(store).RollbackAsync(fixture.Request);

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Code)));
        Assert.AreEqual("old", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "modified.txt"), Encoding.UTF8));
        Assert.IsFalse(File.Exists(Path.Combine(workspace, "src", "created.txt")));
        Assert.AreEqual("deleted", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "deleted.txt"), Encoding.UTF8));
        Assert.IsFalse(File.Exists(Path.Combine(workspace, "src", "new-name.txt")));
        Assert.AreEqual("move", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "old-name.txt"), Encoding.UTF8));
        Assert.AreEqual("stay", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "noop.txt"), Encoding.UTF8));
        Assert.IsTrue(result.FileResults.Any(file => file.Restored));
        Assert.IsTrue(result.FileResults.Any(file => file.Deleted));
        Assert.IsTrue(result.FileResults.Any(file => file.Recreated));
        Assert.IsTrue(result.FileResults.Any(file => file.RenamedBack));
        Assert.IsTrue(result.FileResults.Any(file => file.Noop));
        Assert.AreEqual(1, store.Saved.Count);
        AssertReceiptDoesNotGrantAuthority(result.Receipt!);
    }

    [TestMethod]
    public async Task ControlledRollbackExecutor_RejectsEvidenceMismatchBeforeMutation()
    {
        var workspace = Workspace();
        var fixture = RollbackFixture.Create(workspace, [Op.Modify("src/file.txt", "old", "new")]);
        fixture.WriteAppliedState();
        var before = await File.ReadAllTextAsync(Path.Combine(workspace, "src", "file.txt"), Encoding.UTF8);
        var bad = fixture.Request with { ObservedCleanWorktreeHashBeforeRollback = H("wrong-worktree") };
        var store = new RecordingRollbackStore();

        var result = await new ControlledRollbackExecutor(store).RollbackAsync(bad);

        AssertRejectedBeforeMutation(result, store);
        Assert.AreEqual(before, await File.ReadAllTextAsync(Path.Combine(workspace, "src", "file.txt"), Encoding.UTF8));
    }

    [TestMethod]
    public async Task ControlledRollbackExecutor_RejectsDuplicateRollbackActionHashBeforeMutation()
    {
        var workspace = Workspace();
        var fixture = RollbackFixture.Create(workspace,
        [
            Op.Modify("src/first.txt", "first-old", "first-new"),
            Op.Modify("src/second.txt", "second-old", "second-new")
        ]);
        fixture.WriteAppliedState();
        var firstFile = Path.Combine(workspace, "src", "first.txt");
        var secondFile = Path.Combine(workspace, "src", "second.txt");
        var beforeFirst = await File.ReadAllTextAsync(firstFile, Encoding.UTF8);
        var beforeSecond = await File.ReadAllTextAsync(secondFile, Encoding.UTF8);
        var actions = fixture.Request.RollbackPlan.FileActions.ToArray();
        var duplicateHashAction = actions[1] with { RollbackActionHash = actions[0].RollbackActionHash };
        var bad = fixture.Request with
        {
            RollbackPlan = fixture.Request.RollbackPlan with { FileActions = [actions[0], duplicateHashAction] }
        };
        var store = new RecordingRollbackStore();

        var result = await new ControlledRollbackExecutor(store).RollbackAsync(bad);

        AssertRejectedBeforeMutation(result, store);
        AssertIssue(result, "DuplicateRollbackActionHash");
        Assert.AreEqual(beforeFirst, await File.ReadAllTextAsync(firstFile, Encoding.UTF8));
        Assert.AreEqual(beforeSecond, await File.ReadAllTextAsync(secondFile, Encoding.UTF8));
    }

    [TestMethod]
    public async Task ControlledRollbackExecutor_RejectsDuplicateRollbackActionTargetBeforeMutation()
    {
        var workspace = Workspace();
        var fixture = RollbackFixture.Create(workspace, [Op.Modify("src/file.txt", "old", "new")]);
        fixture.WriteAppliedState();
        var file = Path.Combine(workspace, "src", "file.txt");
        var before = await File.ReadAllTextAsync(file, Encoding.UTF8);
        var action = fixture.Request.RollbackPlan.FileActions[0];
        var duplicateTargetAction = action with { RollbackActionHash = H("duplicate-target-different-action-hash") };
        var bad = fixture.Request with
        {
            RollbackPlan = fixture.Request.RollbackPlan with { FileActions = [action, duplicateTargetAction] }
        };
        var store = new RecordingRollbackStore();

        var result = await new ControlledRollbackExecutor(store).RollbackAsync(bad);

        AssertRejectedBeforeMutation(result, store);
        AssertIssue(result, "DuplicateRollbackActionTarget");
        Assert.AreEqual(before, await File.ReadAllTextAsync(file, Encoding.UTF8));
    }

    [DataTestMethod]
    [DataRow("../escape.txt")]
    [DataRow("src/../escape.txt")]
    [DataRow("src\\escape.txt")]
    [DataRow(".git/config")]
    [DataRow("")]
    public async Task ControlledRollbackExecutor_RejectsUnsafePathBeforeMutation(string path)
    {
        var workspace = Workspace();
        var fixture = RollbackFixture.Create(workspace, [Op.Modify("src/file.txt", "old", "new")]);
        fixture.WriteAppliedState();
        var action = fixture.Request.RollbackPlan.FileActions[0] with { Path = path };
        var bad = fixture.Request with { RollbackPlan = fixture.Request.RollbackPlan with { FileActions = [action] } };
        var store = new RecordingRollbackStore();

        var result = await new ControlledRollbackExecutor(store).RollbackAsync(bad);

        AssertRejectedBeforeMutation(result, store);
        Assert.AreEqual("new", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "file.txt"), Encoding.UTF8));
    }

    [TestMethod]
    public async Task ControlledRollbackExecutor_RejectsCurrentHashMismatchBeforeMutation()
    {
        var workspace = Workspace();
        var fixture = RollbackFixture.Create(workspace, [Op.Modify("src/file.txt", "old", "new")]);
        fixture.WriteAppliedState();
        await File.WriteAllTextAsync(Path.Combine(workspace, "src", "file.txt"), "tampered", Encoding.UTF8);
        var store = new RecordingRollbackStore();

        var result = await new ControlledRollbackExecutor(store).RollbackAsync(fixture.Request);

        AssertRejectedBeforeMutation(result, store);
        Assert.AreEqual("tampered", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "file.txt"), Encoding.UTF8));
    }

    [TestMethod]
    public async Task ControlledRollbackExecutor_RejectsApprovedRollbackContentHashMismatchBeforeMutation()
    {
        var workspace = Workspace();
        var fixture = RollbackFixture.Create(workspace, [Op.Modify("src/file.txt", "old", "new")]);
        fixture.WriteAppliedState();
        var bad = fixture.Request with
        {
            ApprovedContents = [fixture.Request.ApprovedContents[0] with { Content = "tampered rollback content" }]
        };
        var store = new RecordingRollbackStore();

        var result = await new ControlledRollbackExecutor(store).RollbackAsync(bad);

        AssertRejectedBeforeMutation(result, store);
        Assert.AreEqual("new", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "file.txt"), Encoding.UTF8));
    }

    [TestMethod]
    public async Task ControlledRollbackExecutor_PartialFailureReceiptPreservesFullPlan()
    {
        var workspace = Workspace();
        var fixture = RollbackFixture.Create(workspace,
        [
            Op.Modify("src/a-first.txt", "one", "one applied"),
            Op.Modify("src/m-second.txt", "two", "two applied"),
            Op.Modify("src/z-third.txt", "three", "three applied")
        ]);
        fixture.WriteAppliedState();
        var blocked = Path.Combine(workspace, "src", "m-second.txt");
        File.SetAttributes(blocked, FileAttributes.ReadOnly);
        var store = new RecordingRollbackStore();

        var result = await new ControlledRollbackExecutor(store).RollbackAsync(fixture.Request);

        File.SetAttributes(blocked, FileAttributes.Normal);
        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.MutationOccurred);
        Assert.IsTrue(result.PartialRollbackOccurred);
        Assert.IsNotNull(result.Receipt);
        Assert.AreEqual(3, result.Receipt!.FileResults.Count);
        Assert.IsTrue(result.Receipt.FileResults.Any(file => file.Path == "src/a-first.txt" && file.MutationApplied));
        Assert.IsTrue(result.Receipt.FileResults.Any(file => file.Path == "src/m-second.txt" && !file.MutationApplied && file.IssueCodes.Contains("RollbackFailed")));
        Assert.IsTrue(result.Receipt.FileResults.Any(file => file.Path == "src/z-third.txt" && !file.MutationApplied));
        Assert.AreEqual(1, store.Saved.Count);
    }

    [TestMethod]
    public void ControlledRollbackExecutor_DoesNotAddApiCliUiRuntimeGitReleaseMemoryOrRetrievalSurface()
    {
        var root = RepositoryRoot();
        foreach (var file in RollbackExecutionProductionFiles(root))
        {
            var text = File.ReadAllText(file);
            var isExecutor = file.EndsWith(Path.Combine("IronDev.Infrastructure", "Governance", "ControlledRollbackExecutor.cs"), StringComparison.Ordinal);
            foreach (var token in new[] { "File.WriteAllText", "File.WriteAllBytes", "File.Delete", "File.Move", "Directory.CreateDirectory" })
            {
                if (isExecutor)
                {
                    continue;
                }
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"File mutation token outside controlled rollback executor: {file} {token}");
            }

            foreach (var token in new[]
            {
                "Process.Start",
                "ProcessStartInfo",
                "git commit",
                "git push",
                "git merge",
                "gh pr",
                "IHostedService",
                "BackgroundService",
                "Scheduler",
                "WorkflowContinuation",
                "ReleaseReadiness",
                "AgentDispatch",
                "ModelProvider",
                "ToolInvoker",
                "PromoteMemory",
                "ActivateRetrieval",
                "Weaviate",
                "Embedding"
            })
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Forbidden runtime token found in {file}: {token}");
            }
        }
    }

    private static void AssertRejectedBeforeMutation(ControlledRollbackExecutionResult result, RecordingRollbackStore store)
    {
        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.MutationOccurred);
        Assert.IsFalse(result.PartialRollbackOccurred);
        Assert.IsNull(result.Receipt);
        Assert.AreEqual(0, store.Saved.Count);
    }

    private static void AssertIssue(ControlledRollbackExecutionResult result, string code) =>
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == code), $"Expected issue {code}. Actual: {string.Join("; ", result.Issues.Select(issue => issue.Code))}");

    private static void AssertReceiptDoesNotGrantAuthority(RollbackExecutionReceipt receipt)
    {
        var text = JsonSerializer.Serialize(receipt);
        foreach (var token in new[] { "WorkflowCanContinue", "ReleaseReady", "ReleaseApproved", "GitCommitted", "GitPushed", "PullRequestCreated", "MemoryPromoted", "RetrievalActivated", "CanApplySource" })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Receipt contained authority token: {token}");
        }
    }

    private static string[] RollbackExecutionProductionFiles(string root) =>
    [
        Path.Combine(root, "IronDev.Core", "Governance", "RollbackExecutionReceipt.cs"),
        Path.Combine(root, "IronDev.Infrastructure", "Governance", "ControlledRollbackExecutor.cs"),
        Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlRollbackExecutionReceiptStore.cs"),
        Path.Combine(root, "Database", "migrate_rollback_execution_receipt.sql")
    ];

    private static string Workspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "irondev-pr206-rollback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root not found.");
    }

    private static string H(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private sealed record Op(string Kind, string Path, string? PreviousPath, string? Before, string? After)
    {
        public static Op Create(string path, string after) => new("Create", path, null, null, after);
        public static Op Modify(string path, string before, string after) => new("Modify", path, null, before, after);
        public static Op Delete(string path, string before) => new("Delete", path, null, before, null);
        public static Op Rename(string previousPath, string path, string content) => new("Rename", path, previousPath, content, content);
        public static Op Noop(string path, string content) => new("Noop", path, null, content, content);
    }

    private sealed class RollbackFixture
    {
        private readonly IReadOnlyList<Op> _ops;
        private RollbackFixture(ControlledRollbackExecutionRequest request, IReadOnlyList<Op> ops)
        {
            Request = request;
            _ops = ops;
        }

        public ControlledRollbackExecutionRequest Request { get; }

        public static RollbackFixture Create(string workspace, IReadOnlyList<Op> ops)
        {
            var projectId = Guid.NewGuid();
            var now = new DateTimeOffset(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);
            var patchId = Guid.NewGuid();
            var sourceApplyRequestId = Guid.NewGuid();
            var rollbackSupportId = Guid.NewGuid();
            var rollbackPlanId = Guid.NewGuid();
            var sourceApplyReceiptId = Guid.NewGuid();
            var baselineHash = H("baseline");
            var workspaceHash = H("workspace");
            var cleanBeforeApplyHash = H("clean-before-apply");
            var cleanAfterApplyHash = H("clean-after-apply-receipt-derived");
            var changes = ops.Where(op => op.Kind != "Noop").Select(Change).ToArray();
            var changeSetHash = PatchArtifactHashing.ComputeChangeSetHash(changes);
            var patch = new PatchArtifact
            {
                PatchArtifactId = patchId,
                ProjectId = projectId,
                PatchArtifactKind = "SourcePatch",
                ControlledDryRunRequestId = Guid.NewGuid(),
                DryRunExecutionAuditId = Guid.NewGuid(),
                DryRunAuditHash = H("dry-run-audit"),
                DryRunReceiptHash = H("dry-run-receipt"),
                PolicySatisfactionId = Guid.NewGuid(),
                PolicySatisfactionHash = H("policy"),
                SubjectKind = "SourceApplyRequest",
                SubjectId = sourceApplyRequestId.ToString("D"),
                SubjectHash = H("subject"),
                SourceSnapshotReference = "snapshot-main",
                SourceBaselineHash = baselineHash,
                WorkspaceBoundaryHash = workspaceHash,
                ValidationPlanId = "validation-plan",
                ValidationPlanHash = H("validation-plan"),
                PatchHash = "sha256:pending",
                ChangeSetHash = changeSetHash,
                FileChanges = changes,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddHours(1),
                EvidenceReferences = ["patch-evidence"],
                BoundaryMaxims = ["Patch artifact is not source apply."],
                Boundary = PatchArtifactBoundaryText.Boundary
            };
            patch = patch with { PatchHash = PatchArtifactHashing.ComputePatchHash(patch, changeSetHash) };
            var gate = new SourceApplyRequestGateEvaluationEvidence
            {
                SourceApplyGateEvaluationId = Guid.NewGuid(),
                SourceApplyGateEvaluationHash = H("source-apply-gate"),
                Satisfied = true,
                ProjectId = projectId,
                AcceptedApprovalId = Guid.NewGuid(),
                AcceptedApprovalHash = H("approval"),
                PolicySatisfactionId = patch.PolicySatisfactionId,
                PolicySatisfactionHash = patch.PolicySatisfactionHash,
                ControlledDryRunRequestId = patch.ControlledDryRunRequestId,
                DryRunExecutionAuditId = patch.DryRunExecutionAuditId,
                DryRunAuditHash = patch.DryRunAuditHash,
                DryRunReceiptHash = patch.DryRunReceiptHash,
                PatchArtifactId = patch.PatchArtifactId,
                PatchHash = patch.PatchHash,
                ChangeSetHash = patch.ChangeSetHash,
                RollbackSupportReceiptId = rollbackSupportId,
                RollbackSupportReceiptHash = H("rollback-support"),
                RollbackPlanId = rollbackPlanId,
                RollbackPlanHash = H("rollback-plan"),
                RollbackGateEvaluationHash = H("rollback-gate"),
                SubjectKind = patch.SubjectKind,
                SubjectId = patch.SubjectId,
                SubjectHash = patch.SubjectHash,
                SourceSnapshotReference = patch.SourceSnapshotReference,
                SourceBaselineHash = baselineHash,
                WorkspaceBoundaryHash = workspaceHash,
                ExpectedBranch = "main",
                ExpectedCleanWorktreeHash = cleanBeforeApplyHash,
                ExpiresAtUtc = now.AddHours(1),
                EvidenceReferences = ["gate-evidence"],
                BoundaryMaxims = ["Gate is not executor."]
            };
            var fileOps = ops.Select(FileOp).ToArray();
            var sourceApplyRequest = new SourceApplyRequest
            {
                SourceApplyRequestId = sourceApplyRequestId,
                ProjectId = projectId,
                SourceApplyGateEvaluationId = gate.SourceApplyGateEvaluationId,
                SourceApplyGateEvaluationHash = gate.SourceApplyGateEvaluationHash,
                SourceApplyGateSatisfied = true,
                SourceApplyGateEvaluation = gate,
                AcceptedApprovalId = gate.AcceptedApprovalId,
                AcceptedApprovalHash = gate.AcceptedApprovalHash,
                PolicySatisfactionId = patch.PolicySatisfactionId,
                PolicySatisfactionHash = patch.PolicySatisfactionHash,
                ControlledDryRunRequestId = patch.ControlledDryRunRequestId,
                DryRunExecutionAuditId = patch.DryRunExecutionAuditId,
                DryRunAuditHash = patch.DryRunAuditHash,
                DryRunReceiptHash = patch.DryRunReceiptHash,
                PatchArtifactId = patch.PatchArtifactId,
                PatchHash = patch.PatchHash,
                ChangeSetHash = patch.ChangeSetHash,
                RollbackSupportReceiptId = rollbackSupportId,
                RollbackSupportReceiptHash = gate.RollbackSupportReceiptHash,
                RollbackPlanId = rollbackPlanId,
                RollbackPlanHash = gate.RollbackPlanHash,
                RollbackGateEvaluationHash = gate.RollbackGateEvaluationHash,
                SubjectKind = patch.SubjectKind,
                SubjectId = patch.SubjectId,
                SubjectHash = patch.SubjectHash,
                SourceSnapshotReference = patch.SourceSnapshotReference,
                SourceBaselineHash = baselineHash,
                WorkspaceBoundaryHash = workspaceHash,
                ExpectedBranch = "main",
                ExpectedCleanWorktreeHash = cleanBeforeApplyHash,
                FileOperations = fileOps,
                RequestedAtUtc = now,
                ExpiresAtUtc = now.AddHours(1),
                SourceApplyRequestHash = H("source-apply-request"),
                EvidenceReferences = ["source-apply-request-evidence"],
                BoundaryMaxims = ["Source apply request is not apply."],
                Boundary = SourceApplyRequestBoundaryText.Boundary
            };
            var rollbackPlan = new RollbackPlan
            {
                RollbackPlanId = rollbackPlanId,
                ProjectId = projectId,
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
                SourceBaselineHash = baselineHash,
                WorkspaceBoundaryHash = workspaceHash,
                ExpectedBranch = "main",
                ExpectedCleanWorktreeHash = cleanBeforeApplyHash,
                RollbackPlanHash = gate.RollbackPlanHash,
                FileActions = ops.Select(ActionForOp).ToArray(),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddHours(1),
                EvidenceReferences = ["rollback-plan-evidence"],
                BoundaryMaxims = ["Rollback plan is not rollback execution."],
                Boundary = RollbackPlanBoundaryText.Boundary
            };
            var support = new RollbackSupportReceipt
            {
                RollbackSupportReceiptId = rollbackSupportId,
                ProjectId = projectId,
                RollbackPlanId = rollbackPlanId,
                RollbackPlanHash = rollbackPlan.RollbackPlanHash,
                RollbackGateSatisfied = true,
                RollbackGateEvaluationHash = gate.RollbackGateEvaluationHash,
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
                SourceBaselineHash = baselineHash,
                WorkspaceBoundaryHash = workspaceHash,
                ExpectedBranch = "main",
                ExpectedCleanWorktreeHash = cleanBeforeApplyHash,
                RollbackSupportReceiptHash = gate.RollbackSupportReceiptHash,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddHours(1),
                EvidenceReferences = ["rollback-support-evidence"],
                BoundaryMaxims = ["Rollback support is not rollback execution."],
                Boundary = RollbackSupportReceiptBoundaryText.Boundary
            };
            var sourceApplyReceipt = BuildSourceApplyReceipt(projectId, sourceApplyRequest, patch, support, sourceApplyReceiptId, cleanBeforeApplyHash, cleanAfterApplyHash, ops);
            var request = new ControlledRollbackExecutionRequest
            {
                ControlledRollbackExecutionRequestId = Guid.NewGuid(),
                ProjectId = projectId,
                RollbackPlan = rollbackPlan,
                RollbackSupportReceipt = support,
                SourceApplyRequest = sourceApplyRequest,
                SourceApplyReceipt = sourceApplyReceipt,
                PatchArtifact = patch,
                WorkspaceRoot = workspace,
                ApprovedWorkspaceBoundaryHash = workspaceHash,
                ObservedBranch = "main",
                ObservedSourceBaselineHash = baselineHash,
                ObservedCleanWorktreeHashBeforeRollback = cleanAfterApplyHash,
                ApprovedContents = ops.Where(op => op.Kind is "Modify" or "Delete").Select(op => new ControlledRollbackContent { Path = op.Path, ContentHash = H(op.Before ?? string.Empty), Content = op.Before ?? string.Empty }).ToArray(),
                RequestedAtUtc = now,
                EvidenceReferences = ["rollback-execution-request-evidence"],
                BoundaryMaxims = ["Rollback execution is mutation evidence, not release approval."]
            };
            return new(request, ops);
        }

        public void WriteAppliedState()
        {
            foreach (var op in _ops)
            {
                switch (op.Kind)
                {
                    case "Create":
                    case "Modify":
                    case "Noop":
                        Write(op.Path, op.After ?? op.Before ?? string.Empty);
                        break;
                    case "Rename":
                        Write(op.Path, op.After ?? op.Before ?? string.Empty);
                        break;
                    case "Delete":
                        var deletedPath = Path.Combine(Request.WorkspaceRoot, op.Path);
                        Directory.CreateDirectory(Path.GetDirectoryName(deletedPath)!);
                        if (File.Exists(deletedPath)) File.Delete(deletedPath);
                        break;
                }
            }
        }

        private void Write(string path, string content)
        {
            var full = Path.Combine(Request.WorkspaceRoot, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content, Encoding.UTF8);
        }

        private static PatchArtifactFileChange Change(Op op) => new()
        {
            Path = op.Path,
            PreviousPath = op.PreviousPath,
            ChangeKind = op.Kind,
            BeforeContentHash = op.Before is null ? null : H(op.Before),
            AfterContentHash = op.After is null ? null : H(op.After),
            DiffHash = H("diff:" + op.Path),
            NormalizedDiff = "change " + op.Path,
            IsBinary = false
        };

        private static SourceApplyRequestFileOperation FileOp(Op op)
        {
            var change = op.Kind == "Noop" ? null : Change(op);
            return new SourceApplyRequestFileOperation
            {
                Path = op.Path,
                PreviousPath = op.PreviousPath,
                OperationKind = op.Kind switch { "Create" => SourceApplyRequestFileOperationKinds.CreateFile, "Modify" => SourceApplyRequestFileOperationKinds.ModifyFile, "Delete" => SourceApplyRequestFileOperationKinds.DeleteFile, "Rename" => SourceApplyRequestFileOperationKinds.RenameFile, _ => SourceApplyRequestFileOperationKinds.Noop },
                BeforeContentHash = op.Before is null ? null : H(op.Before),
                AfterContentHash = op.After is null ? null : H(op.After),
                DiffHash = change?.DiffHash,
                PatchArtifactChangeHash = change is null ? H("noop:" + op.Path) : PatchArtifactHashing.ComputeFileChangeHash(change),
                OperationHash = H("operation:" + op.Kind + ":" + op.Path)
            };
        }

        private static RollbackPlanFileAction ActionForOp(Op op) => op.Kind switch
        {
            "Create" => new RollbackPlanFileAction { Path = op.Path, PlannedActionKind = RollbackPlanFileActionKinds.DeleteCreatedFile, DeleteContentHash = H(op.After ?? string.Empty), ExpectedCurrentContentHash = H(op.After ?? string.Empty), RollbackActionHash = H("rollback:delete-created:" + op.Path) },
            "Modify" => new RollbackPlanFileAction { Path = op.Path, PlannedActionKind = RollbackPlanFileActionKinds.RestoreModifiedFile, RestoreContentHash = H(op.Before ?? string.Empty), ExpectedCurrentContentHash = H(op.After ?? string.Empty), RollbackActionHash = H("rollback:restore:" + op.Path) },
            "Delete" => new RollbackPlanFileAction { Path = op.Path, PlannedActionKind = RollbackPlanFileActionKinds.RecreateDeletedFile, RestoreContentHash = H(op.Before ?? string.Empty), ExpectedCurrentContentHash = H("deleted-current:" + op.Path), RollbackActionHash = H("rollback:recreate:" + op.Path) },
            "Rename" => new RollbackPlanFileAction { Path = op.Path, PreviousPath = op.PreviousPath, PlannedActionKind = RollbackPlanFileActionKinds.RenameBack, RestoreContentHash = H(op.Before ?? string.Empty), ExpectedCurrentContentHash = H(op.After ?? string.Empty), RollbackActionHash = H("rollback:rename-back:" + op.Path) },
            _ => new RollbackPlanFileAction { Path = op.Path, PlannedActionKind = RollbackPlanFileActionKinds.Noop, ExpectedCurrentContentHash = H(op.Before ?? op.After ?? string.Empty), RollbackActionHash = H("rollback:noop:" + op.Path) }
        };

        private static SourceApplyReceipt BuildSourceApplyReceipt(Guid projectId, SourceApplyRequest request, PatchArtifact patch, RollbackSupportReceipt support, Guid sourceApplyReceiptId, string cleanBeforeApplyHash, string cleanAfterApplyHash, IReadOnlyList<Op> ops)
        {
            var results = ops.Select(op => BuildSourceApplyFileResult(op)).ToArray();
            var receipt = new SourceApplyReceipt
            {
                SourceApplyReceiptId = sourceApplyReceiptId,
                ProjectId = projectId,
                ControlledSourceApplyRequestId = Guid.NewGuid(),
                SourceApplyRequestId = request.SourceApplyRequestId,
                SourceApplyRequestHash = request.SourceApplyRequestHash,
                SourceApplyDryRunReceiptId = Guid.NewGuid(),
                SourceApplyDryRunReceiptHash = patch.DryRunReceiptHash,
                SourceApplyGateEvaluationId = request.SourceApplyGateEvaluationId,
                SourceApplyGateEvaluationHash = request.SourceApplyGateEvaluationHash,
                PatchArtifactId = patch.PatchArtifactId,
                PatchHash = patch.PatchHash,
                ChangeSetHash = patch.ChangeSetHash,
                RollbackSupportReceiptId = support.RollbackSupportReceiptId,
                RollbackSupportReceiptHash = support.RollbackSupportReceiptHash,
                SourceBaselineHash = patch.SourceBaselineHash,
                WorkspaceBoundaryHash = patch.WorkspaceBoundaryHash,
                ExpectedBranch = request.ExpectedBranch,
                ExpectedCleanWorktreeHash = request.ExpectedCleanWorktreeHash,
                ObservedBranch = "main",
                ObservedCleanWorktreeHashBeforeApply = cleanBeforeApplyHash,
                ObservedCleanWorktreeHashAfterApply = cleanAfterApplyHash,
                MutationOccurred = true,
                ApplySucceeded = true,
                PartialApplyOccurred = false,
                FileResults = results,
                IssueCodes = ["NoIssues"],
                AppliedAtUtc = new DateTimeOffset(2026, 6, 17, 12, 5, 0, TimeSpan.Zero),
                SourceApplyReceiptHash = "sha256:pending",
                EvidenceReferences = ["source-apply-receipt-evidence"],
                BoundaryMaxims = ["SourceApplyReceipt is mutation evidence, not release approval."],
                Boundary = SourceApplyReceiptBoundaryText.Boundary
            };
            return receipt with { SourceApplyReceiptHash = SourceApplyReceiptHashing.ComputeReceiptHash(receipt) };
        }

        private static SourceApplyReceiptFileResult BuildSourceApplyFileResult(Op op)
        {
            var fileOp = FileOp(op);
            var result = new SourceApplyReceiptFileResult
            {
                Path = fileOp.Path,
                PreviousPath = fileOp.PreviousPath,
                OperationKind = fileOp.OperationKind,
                PatchArtifactChangeHash = fileOp.PatchArtifactChangeHash,
                OperationHash = fileOp.OperationHash,
                BeforeContentHash = fileOp.BeforeContentHash,
                AfterContentHash = fileOp.AfterContentHash,
                PreconditionsSatisfied = true,
                MutationApplied = op.Kind != "Noop",
                Created = op.Kind == "Create",
                Modified = op.Kind == "Modify",
                Deleted = op.Kind == "Delete",
                Renamed = op.Kind == "Rename",
                Noop = op.Kind == "Noop",
                IssueCodes = [],
                FileResultHash = "sha256:pending"
            };
            return result with { FileResultHash = SourceApplyReceiptHashing.ComputeFileResultHash(result) };
        }
    }

    private sealed class RecordingRollbackStore : IRollbackExecutionReceiptStore
    {
        public List<RollbackExecutionReceipt> Saved { get; } = [];
        public Task SaveAsync(RollbackExecutionReceipt receipt, CancellationToken cancellationToken = default) { Saved.Add(receipt); return Task.CompletedTask; }
        public Task<RollbackExecutionReceipt?> GetAsync(Guid projectId, Guid rollbackExecutionReceiptId, CancellationToken cancellationToken = default) => Task.FromResult(Saved.FirstOrDefault(r => r.ProjectId == projectId && r.RollbackExecutionReceiptId == rollbackExecutionReceiptId));
        public Task<RollbackExecutionReceipt?> GetByReceiptHashAsync(Guid projectId, string rollbackExecutionReceiptHash, CancellationToken cancellationToken = default) => Task.FromResult(Saved.FirstOrDefault(r => r.ProjectId == projectId && r.RollbackExecutionReceiptHash == rollbackExecutionReceiptHash));
        public Task<IReadOnlyList<RollbackExecutionReceipt>> ListBySourceApplyReceiptAsync(Guid projectId, Guid sourceApplyReceiptId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RollbackExecutionReceipt>>(Saved.Where(r => r.ProjectId == projectId && r.SourceApplyReceiptId == sourceApplyReceiptId).ToArray());
        public Task<IReadOnlyList<RollbackExecutionReceipt>> ListByRollbackPlanAsync(Guid projectId, Guid rollbackPlanId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RollbackExecutionReceipt>>(Saved.Where(r => r.ProjectId == projectId && r.RollbackPlanId == rollbackPlanId).ToArray());
        public Task<IReadOnlyList<RollbackExecutionReceipt>> ListByRollbackSupportReceiptAsync(Guid projectId, Guid rollbackSupportReceiptId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RollbackExecutionReceipt>>(Saved.Where(r => r.ProjectId == projectId && r.RollbackSupportReceiptId == rollbackSupportReceiptId).ToArray());
        public Task<IReadOnlyList<RollbackExecutionReceipt>> ListByPatchArtifactAsync(Guid projectId, Guid patchArtifactId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RollbackExecutionReceipt>>(Saved.Where(r => r.ProjectId == projectId && r.PatchArtifactId == patchArtifactId).ToArray());
    }
}

[TestClass]
[TestCategory("RollbackExecutionReceiptStore")]
[TestCategory("PR206")]
public sealed class RollbackExecutionReceiptStoreTests : IntegrationTestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private SqlRollbackExecutionReceiptStore _store = default!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropRollbackExecutionReceiptAsync();
        await ApplySqlFileAsync("Database", "migrate_rollback_execution_receipt.sql");
        _store = new SqlRollbackExecutionReceiptStore(ServiceProvider.GetRequiredService<IDbConnectionFactory>());
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        await DropRollbackExecutionReceiptAsync();
        await base.TestCleanup();
    }

    [TestMethod]
    public async Task RollbackExecutionReceiptStore_SaveGetListAndHashRoundTrip()
    {
        var receipt = ValidReceipt();
        await _store.SaveAsync(receipt);

        AssertReceipt(receipt, await _store.GetAsync(receipt.ProjectId, receipt.RollbackExecutionReceiptId));
        AssertReceipt(receipt, await _store.GetByReceiptHashAsync(receipt.ProjectId, receipt.RollbackExecutionReceiptHash));
        AssertSingle(receipt, await _store.ListBySourceApplyReceiptAsync(receipt.ProjectId, receipt.SourceApplyReceiptId));
        AssertSingle(receipt, await _store.ListByRollbackPlanAsync(receipt.ProjectId, receipt.RollbackPlanId));
        AssertSingle(receipt, await _store.ListByRollbackSupportReceiptAsync(receipt.ProjectId, receipt.RollbackSupportReceiptId));
        AssertSingle(receipt, await _store.ListByPatchArtifactAsync(receipt.ProjectId, receipt.PatchArtifactId));
    }

    [TestMethod]
    public async Task RollbackExecutionReceiptStore_IdempotentSameHashAndConflictDifferentHash()
    {
        var receipt = ValidReceipt();
        await _store.SaveAsync(receipt);
        await _store.SaveAsync(receipt);

        await ExpectSqlExceptionAsync(() => _store.SaveAsync(receipt with { RollbackExecutionReceiptHash = H("changed-receipt-hash") }));
    }

    [TestMethod]
    public async Task RollbackExecutionReceiptSqlBoundaryRejectsDirectUnsafeTextAndAuthorityClaims()
    {
        await ExpectSqlExceptionAsync(() => InsertReceiptDirectAsync(ValidReceipt() with { EvidenceReferences = ["rawPrompt leaked"] }));
        await ExpectSqlExceptionAsync(() => InsertReceiptDirectAsync(ValidReceipt() with { BoundaryMaxims = ["release approved"] }));
    }

    [TestMethod]
    public async Task RollbackExecutionReceiptSqlBoundaryBlocksUpdateAndDelete()
    {
        var receipt = ValidReceipt();
        await _store.SaveAsync(receipt);

        await ExpectSqlExceptionAsync(() => ExecuteSqlAsync("UPDATE governance.RollbackExecutionReceipt SET ObservedBranch = @ObservedBranch WHERE RollbackExecutionReceiptId = @RollbackExecutionReceiptId", new { ObservedBranch = "other", receipt.RollbackExecutionReceiptId }));
        await ExpectSqlExceptionAsync(() => ExecuteSqlAsync("DELETE FROM governance.RollbackExecutionReceipt WHERE RollbackExecutionReceiptId = @RollbackExecutionReceiptId", new { receipt.RollbackExecutionReceiptId }));
    }

    [TestMethod]
    public void RollbackExecutionReceipt_MigrationInventoryAndReceiptAreRegistered()
    {
        var root = RepositoryRoot();
        var manifest = File.ReadAllText(Path.Combine(root, "Database", "migrations.json"));
        var inventory = File.ReadAllText(Path.Combine(root, "Database", "sql-inventory.json"));
        var verifier = File.ReadAllText(Path.Combine(root, "Database", "verify-migrations.ps1"));
        var receipt = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "PR206_ROLLBACK_EXECUTOR.md"));

        StringAssert.Contains(manifest, "Database/migrate_rollback_execution_receipt.sql");
        StringAssert.Contains(inventory, "database.migrate-rollback-execution-receipt");
        StringAssert.Contains(inventory, "runtime.rollback-execution-receipt-store");
        StringAssert.Contains(verifier, "governance.RollbackExecutionReceipt table");
        StringAssert.Contains(receipt, "PR206 pulls the emergency brake. It does not declare the crash cleaned up.");
        StringAssert.Contains(receipt, "This PR may mutate source only inside the approved workspace root after full rollback preflight passes.");
        StringAssert.Contains(receipt, "This PR does not continue workflow.");
        StringAssert.Contains(receipt, "This PR does not approve release.");
    }

    private async Task InsertReceiptDirectAsync(RollbackExecutionReceipt receipt)
    {
        const string sql = """
            INSERT INTO governance.RollbackExecutionReceipt
            (
                RollbackExecutionReceiptId, ProjectId, ControlledRollbackExecutionRequestId, RollbackPlanId, RollbackPlanHash,
                RollbackSupportReceiptId, RollbackSupportReceiptHash, SourceApplyRequestId, SourceApplyRequestHash,
                SourceApplyReceiptId, SourceApplyReceiptHash, PatchArtifactId, PatchHash, ChangeSetHash,
                SourceBaselineHash, WorkspaceBoundaryHash, ExpectedBranch, ExpectedCleanWorktreeHash, ObservedBranch,
                ObservedSourceBaselineHash, ObservedCleanWorktreeHashBeforeRollback, ObservedCleanWorktreeHashAfterRollback,
                MutationOccurred, RollbackSucceeded, PartialRollbackOccurred, FileResultsJson, IssueCodesJson,
                RolledBackAtUtc, RollbackExecutionReceiptHash, EvidenceReferencesJson, BoundaryMaximsJson, BoundaryText
            )
            VALUES
            (
                @RollbackExecutionReceiptId, @ProjectId, @ControlledRollbackExecutionRequestId, @RollbackPlanId, @RollbackPlanHash,
                @RollbackSupportReceiptId, @RollbackSupportReceiptHash, @SourceApplyRequestId, @SourceApplyRequestHash,
                @SourceApplyReceiptId, @SourceApplyReceiptHash, @PatchArtifactId, @PatchHash, @ChangeSetHash,
                @SourceBaselineHash, @WorkspaceBoundaryHash, @ExpectedBranch, @ExpectedCleanWorktreeHash, @ObservedBranch,
                @ObservedSourceBaselineHash, @ObservedCleanWorktreeHashBeforeRollback, @ObservedCleanWorktreeHashAfterRollback,
                @MutationOccurred, @RollbackSucceeded, @PartialRollbackOccurred, @FileResultsJson, @IssueCodesJson,
                @RolledBackAtUtc, @RollbackExecutionReceiptHash, @EvidenceReferencesJson, @BoundaryMaximsJson, @BoundaryText
            );
            """;
        await ExecuteSqlAsync(sql, new
        {
            receipt.RollbackExecutionReceiptId,
            receipt.ProjectId,
            receipt.ControlledRollbackExecutionRequestId,
            receipt.RollbackPlanId,
            receipt.RollbackPlanHash,
            receipt.RollbackSupportReceiptId,
            receipt.RollbackSupportReceiptHash,
            receipt.SourceApplyRequestId,
            receipt.SourceApplyRequestHash,
            receipt.SourceApplyReceiptId,
            receipt.SourceApplyReceiptHash,
            receipt.PatchArtifactId,
            receipt.PatchHash,
            receipt.ChangeSetHash,
            receipt.SourceBaselineHash,
            receipt.WorkspaceBoundaryHash,
            receipt.ExpectedBranch,
            receipt.ExpectedCleanWorktreeHash,
            receipt.ObservedBranch,
            receipt.ObservedSourceBaselineHash,
            receipt.ObservedCleanWorktreeHashBeforeRollback,
            receipt.ObservedCleanWorktreeHashAfterRollback,
            receipt.MutationOccurred,
            receipt.RollbackSucceeded,
            receipt.PartialRollbackOccurred,
            FileResultsJson = JsonSerializer.Serialize(receipt.FileResults, JsonOptions),
            IssueCodesJson = JsonSerializer.Serialize(receipt.IssueCodes, JsonOptions),
            receipt.RolledBackAtUtc,
            receipt.RollbackExecutionReceiptHash,
            EvidenceReferencesJson = JsonSerializer.Serialize(receipt.EvidenceReferences, JsonOptions),
            BoundaryMaximsJson = JsonSerializer.Serialize(receipt.BoundaryMaxims, JsonOptions),
            BoundaryText = receipt.Boundary
        });
    }

    private async Task ApplySqlFileAsync(params string[] pathParts)
    {
        var root = RepositoryRoot();
        var sql = await File.ReadAllTextAsync(Path.Combine([root, .. pathParts]));
        using var connection = ServiceProvider.GetRequiredService<IDbConnectionFactory>().CreateConnection();
        foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(batch)) await connection.ExecuteAsync(new CommandDefinition(batch, commandType: CommandType.Text));
        }
    }

    private async Task ExecuteSqlAsync(string sql, object parameters)
    {
        using var connection = ServiceProvider.GetRequiredService<IDbConnectionFactory>().CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, commandType: CommandType.Text));
    }

    private async Task DropRollbackExecutionReceiptAsync()
    {
        using var connection = ServiceProvider.GetRequiredService<IDbConnectionFactory>().CreateConnection();
        const string sql = """
            IF OBJECT_ID(N'governance.TR_RollbackExecutionReceipt_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_RollbackExecutionReceipt_BlockUpdateDelete;
            IF OBJECT_ID(N'governance.TR_RollbackExecutionReceipt_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER governance.TR_RollbackExecutionReceipt_ValidateInsert;
            DROP PROCEDURE IF EXISTS governance.usp_RollbackExecutionReceipt_ListByPatchArtifact;
            DROP PROCEDURE IF EXISTS governance.usp_RollbackExecutionReceipt_ListByRollbackSupportReceipt;
            DROP PROCEDURE IF EXISTS governance.usp_RollbackExecutionReceipt_ListByRollbackPlan;
            DROP PROCEDURE IF EXISTS governance.usp_RollbackExecutionReceipt_ListBySourceApplyReceipt;
            DROP PROCEDURE IF EXISTS governance.usp_RollbackExecutionReceipt_GetByReceiptHash;
            DROP PROCEDURE IF EXISTS governance.usp_RollbackExecutionReceipt_Get;
            DROP PROCEDURE IF EXISTS governance.usp_RollbackExecutionReceipt_Save;
            IF OBJECT_ID(N'governance.RollbackExecutionReceipt', N'U') IS NOT NULL DROP TABLE governance.RollbackExecutionReceipt;
            """;
        await connection.ExecuteAsync(new CommandDefinition(sql, commandType: CommandType.Text));
    }

    private static RollbackExecutionReceipt ValidReceipt()
    {
        var fileResult = new RollbackExecutionReceiptFileResult
        {
            Path = "src/feature.txt",
            PreviousPath = null,
            OperationKind = RollbackPlanFileActionKinds.RestoreModifiedFile,
            PatchArtifactChangeHash = H("patch-change"),
            RollbackActionHash = H("rollback-action"),
            BeforeContentHash = H("after"),
            AfterContentHash = H("before"),
            PreconditionsSatisfied = true,
            MutationApplied = true,
            Restored = true,
            Deleted = false,
            Recreated = false,
            RenamedBack = false,
            Noop = false,
            IssueCodes = [],
            FileResultHash = "sha256:pending"
        };
        fileResult = fileResult with { FileResultHash = RollbackExecutionReceiptHashing.ComputeFileResultHash(fileResult) };
        var receipt = new RollbackExecutionReceipt
        {
            RollbackExecutionReceiptId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            ControlledRollbackExecutionRequestId = Guid.NewGuid(),
            RollbackPlanId = Guid.NewGuid(),
            RollbackPlanHash = H("rollback-plan"),
            RollbackSupportReceiptId = Guid.NewGuid(),
            RollbackSupportReceiptHash = H("rollback-support"),
            SourceApplyRequestId = Guid.NewGuid(),
            SourceApplyRequestHash = H("source-request"),
            SourceApplyReceiptId = Guid.NewGuid(),
            SourceApplyReceiptHash = H("source-apply-receipt"),
            PatchArtifactId = Guid.NewGuid(),
            PatchHash = H("patch"),
            ChangeSetHash = H("change-set"),
            SourceBaselineHash = H("baseline"),
            WorkspaceBoundaryHash = H("workspace"),
            ExpectedBranch = "main",
            ExpectedCleanWorktreeHash = H("clean-before"),
            ObservedBranch = "main",
            ObservedSourceBaselineHash = H("baseline"),
            ObservedCleanWorktreeHashBeforeRollback = H("after-apply"),
            ObservedCleanWorktreeHashAfterRollback = H("after-rollback"),
            MutationOccurred = true,
            RollbackSucceeded = true,
            PartialRollbackOccurred = false,
            FileResults = [fileResult],
            IssueCodes = ["NoIssues"],
            RolledBackAtUtc = new DateTimeOffset(2026, 6, 17, 13, 0, 0, TimeSpan.Zero),
            RollbackExecutionReceiptHash = "sha256:pending",
            EvidenceReferences = ["rollback-execution-evidence"],
            BoundaryMaxims = ["RollbackExecutionReceipt is mutation evidence, not release approval."],
            Boundary = RollbackExecutionBoundaryText.Boundary
        };
        return receipt with { RollbackExecutionReceiptHash = RollbackExecutionReceiptHashing.ComputeReceiptHash(receipt) };
    }

    private static void AssertReceipt(RollbackExecutionReceipt expected, RollbackExecutionReceipt? actual)
    {
        Assert.IsNotNull(actual);
        Assert.AreEqual(expected.RollbackExecutionReceiptId, actual!.RollbackExecutionReceiptId);
        Assert.AreEqual(expected.RollbackExecutionReceiptHash, actual.RollbackExecutionReceiptHash);
        Assert.AreEqual(expected.FileResults[0].FileResultHash, actual.FileResults[0].FileResultHash);
    }

    private static void AssertSingle(RollbackExecutionReceipt expected, IReadOnlyList<RollbackExecutionReceipt> actual)
    {
        Assert.AreEqual(1, actual.Count);
        Assert.AreEqual(expected.RollbackExecutionReceiptId, actual[0].RollbackExecutionReceiptId);
    }

    private static async Task ExpectSqlExceptionAsync(Func<Task> action)
    {
        try { await action(); }
        catch (SqlException) { return; }
        Assert.Fail("Expected SqlException.");
    }

    private static string H(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root not found.");
    }
}
