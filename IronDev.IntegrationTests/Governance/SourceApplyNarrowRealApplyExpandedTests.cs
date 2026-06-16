using System.Data;
using System.Security.Cryptography;
using System.Text;
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
[TestCategory("SourceApplyNarrowRealApply")]
[TestCategory("PR204")]
public sealed class SourceApplyNarrowRealApplyExpandedTests
{
    [TestMethod]
    public async Task ControlledSourceApplyExecutor_CoversCreateDeleteRenameAndNoop()
    {
        var workspace = Workspace();
        var fixture = Fixture.Create(workspace,
        [
            Op.Create("src/create.txt", "created"),
            Op.Delete("src/delete.txt", "delete me"),
            Op.Rename("src/old.txt", "src/renamed.txt", "move me"),
            Op.Noop("src/noop.txt", "stay")
        ]);
        fixture.WriteInitialFiles();
        var store = new RecordingStore();
        var result = await new ControlledSourceApplyExecutor(store).ApplyAsync(fixture.Request);

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(i => i.Code)));
        Assert.AreEqual("created", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "create.txt"), Encoding.UTF8));
        Assert.IsFalse(File.Exists(Path.Combine(workspace, "src", "delete.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(workspace, "src", "old.txt")));
        Assert.AreEqual("move me", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "renamed.txt"), Encoding.UTF8));
        Assert.AreEqual("stay", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "noop.txt"), Encoding.UTF8));
        Assert.IsTrue(result.FileResults.Any(r => r.Created));
        Assert.IsTrue(result.FileResults.Any(r => r.Deleted));
        Assert.IsTrue(result.FileResults.Any(r => r.Renamed));
        Assert.IsTrue(result.FileResults.Any(r => r.Noop));
    }

    [TestMethod]
    public async Task ControlledSourceApplyExecutor_RejectsPathEscapeBeforeMutation()
    {
        var workspace = Workspace();
        var fixture = Fixture.Create(workspace, [Op.Modify("src/file.txt", "before", "after")]);
        fixture.WriteInitialFiles();
        var op = fixture.Request.SourceApplyRequest.FileOperations[0] with { Path = "../escape.txt" };
        var bad = fixture.Request with { SourceApplyRequest = fixture.Request.SourceApplyRequest with { FileOperations = [op] } };
        var store = new RecordingStore();

        var result = await new ControlledSourceApplyExecutor(store).ApplyAsync(bad);

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.MutationOccurred);
        Assert.AreEqual("before", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "file.txt"), Encoding.UTF8));
        Assert.AreEqual(0, store.Saved.Count);
    }

    [DataTestMethod]
    [DataRow("DryRunMismatch")]
    [DataRow("RollbackMismatch")]
    [DataRow("PatchMismatch")]
    [DataRow("BranchMismatch")]
    [DataRow("BaselineMismatch")]
    [DataRow("WorktreeMismatch")]
    [DataRow("ExpiredDryRun")]
    [DataRow("ExpiredRollback")]
    public async Task ControlledSourceApplyExecutor_RejectsEvidenceMismatchBeforeMutation(string mutation)
    {
        var workspace = Workspace();
        var fixture = Fixture.Create(workspace, [Op.Modify("src/file.txt", "before", "after")]);
        fixture.WriteInitialFiles();
        var store = new RecordingStore();

        var result = await new ControlledSourceApplyExecutor(store).ApplyAsync(fixture.Mutate(mutation));

        Assert.IsFalse(result.Succeeded, mutation);
        Assert.IsFalse(result.MutationOccurred, mutation);
        Assert.AreEqual("before", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "file.txt"), Encoding.UTF8));
        Assert.AreEqual(0, store.Saved.Count);
    }

    [TestMethod]
    public async Task ControlledSourceApplyExecutor_PartialFailureReceiptPreservesFullPreflightPlan()
    {
        var workspace = Workspace();
        var fixture = Fixture.Create(workspace,
        [
            Op.Modify("src/a-first.txt", "one", "one updated"),
            Op.Modify("src/z-second.txt", "two", "two updated")
        ]);
        fixture.WriteInitialFiles();
        File.SetAttributes(Path.Combine(workspace, "src", "z-second.txt"), FileAttributes.ReadOnly);
        var store = new RecordingStore();

        var result = await new ControlledSourceApplyExecutor(store).ApplyAsync(fixture.Request);

        File.SetAttributes(Path.Combine(workspace, "src", "z-second.txt"), FileAttributes.Normal);
        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.MutationOccurred);
        Assert.IsTrue(result.PartialApplyOccurred);
        Assert.IsNotNull(result.Receipt);
        Assert.AreEqual(2, result.Receipt!.FileResults.Count);
        Assert.IsTrue(result.Receipt.FileResults.Any(r => r.Path == "src/a-first.txt" && r.MutationApplied));
        Assert.IsTrue(result.Receipt.FileResults.Any(r => r.Path == "src/z-second.txt" && !r.MutationApplied && r.IssueCodes.Contains("ApplyFailed")));
        Assert.AreEqual(1, store.Saved.Count);
    }

    private static string Workspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "irondev-pr204-expanded-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed record Op(string Kind, string Path, string? PreviousPath, string? Before, string? After)
    {
        public static Op Create(string path, string after) => new(SourceApplyRequestFileOperationKinds.CreateFile, path, null, null, after);
        public static Op Modify(string path, string before, string after) => new(SourceApplyRequestFileOperationKinds.ModifyFile, path, null, before, after);
        public static Op Delete(string path, string before) => new(SourceApplyRequestFileOperationKinds.DeleteFile, path, null, before, null);
        public static Op Rename(string previousPath, string path, string content) => new(SourceApplyRequestFileOperationKinds.RenameFile, path, previousPath, content, content);
        public static Op Noop(string path, string content) => new(SourceApplyRequestFileOperationKinds.Noop, path, null, content, content);
    }

    private sealed class Fixture
    {
        private readonly IReadOnlyList<Op> _ops;
        private Fixture(ControlledSourceApplyRequest request, IReadOnlyList<Op> ops) { Request = request; _ops = ops; }
        public ControlledSourceApplyRequest Request { get; }

        public static Fixture Create(string workspace, IReadOnlyList<Op> ops)
        {
            var projectId = Guid.NewGuid();
            var requestId = Guid.NewGuid();
            var gateId = Guid.NewGuid();
            var patchId = Guid.NewGuid();
            var rollbackId = Guid.NewGuid();
            var dryRunId = Guid.NewGuid();
            var sourceApplyRequestHash = H("source-apply-request");
            var gateHash = H("gate");
            var dryRunHash = H("dry-run");
            var rollbackHash = H("rollback");
            var baselineHash = H("baseline");
            var boundaryHash = H("workspace");
            var worktreeHash = H("clean-worktree");
            var now = new DateTimeOffset(2026, 6, 17, 8, 0, 0, TimeSpan.Zero);
            var changes = ops.Where(o => o.Kind != SourceApplyRequestFileOperationKinds.Noop).Select(Change).ToArray();
            var changeSetHash = PatchArtifactHashing.ComputeChangeSetHash(changes);
            var patch = new PatchArtifact
            {
                PatchArtifactId = patchId,
                ProjectId = projectId,
                PatchArtifactKind = "SourcePatch",
                ControlledDryRunRequestId = Guid.NewGuid(),
                DryRunExecutionAuditId = Guid.NewGuid(),
                DryRunAuditHash = H("dry-run-audit"),
                DryRunReceiptHash = dryRunHash,
                PolicySatisfactionId = Guid.NewGuid(),
                PolicySatisfactionHash = H("policy"),
                SubjectKind = "SourceApplyRequest",
                SubjectId = requestId.ToString("D"),
                SubjectHash = sourceApplyRequestHash,
                SourceSnapshotReference = "snapshot-main",
                SourceBaselineHash = baselineHash,
                WorkspaceBoundaryHash = boundaryHash,
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
                SourceApplyGateEvaluationId = gateId,
                SourceApplyGateEvaluationHash = gateHash,
                Satisfied = true,
                ProjectId = projectId,
                AcceptedApprovalId = Guid.NewGuid(),
                AcceptedApprovalHash = H("approval"),
                PolicySatisfactionId = patch.PolicySatisfactionId,
                PolicySatisfactionHash = patch.PolicySatisfactionHash,
                ControlledDryRunRequestId = patch.ControlledDryRunRequestId,
                DryRunExecutionAuditId = patch.DryRunExecutionAuditId,
                DryRunAuditHash = patch.DryRunAuditHash,
                DryRunReceiptHash = dryRunHash,
                PatchArtifactId = patchId,
                PatchHash = patch.PatchHash,
                ChangeSetHash = changeSetHash,
                RollbackSupportReceiptId = rollbackId,
                RollbackSupportReceiptHash = rollbackHash,
                RollbackPlanId = Guid.NewGuid(),
                RollbackPlanHash = H("rollback-plan"),
                RollbackGateEvaluationHash = H("rollback-gate"),
                SubjectKind = patch.SubjectKind,
                SubjectId = patch.SubjectId,
                SubjectHash = patch.SubjectHash,
                SourceSnapshotReference = patch.SourceSnapshotReference,
                SourceBaselineHash = baselineHash,
                WorkspaceBoundaryHash = boundaryHash,
                ExpectedBranch = "main",
                ExpectedCleanWorktreeHash = worktreeHash,
                ExpiresAtUtc = now.AddHours(1),
                EvidenceReferences = ["gate-evidence"],
                BoundaryMaxims = ["Gate is not executor."]
            };
            var fileOps = ops.Select(FileOp).ToArray();
            var request = new SourceApplyRequest
            {
                SourceApplyRequestId = requestId,
                ProjectId = projectId,
                SourceApplyGateEvaluationId = gateId,
                SourceApplyGateEvaluationHash = gateHash,
                SourceApplyGateSatisfied = true,
                SourceApplyGateEvaluation = gate,
                AcceptedApprovalId = gate.AcceptedApprovalId,
                AcceptedApprovalHash = gate.AcceptedApprovalHash,
                PolicySatisfactionId = patch.PolicySatisfactionId,
                PolicySatisfactionHash = patch.PolicySatisfactionHash,
                ControlledDryRunRequestId = patch.ControlledDryRunRequestId,
                DryRunExecutionAuditId = patch.DryRunExecutionAuditId,
                DryRunAuditHash = patch.DryRunAuditHash,
                DryRunReceiptHash = dryRunHash,
                PatchArtifactId = patchId,
                PatchHash = patch.PatchHash,
                ChangeSetHash = changeSetHash,
                RollbackSupportReceiptId = rollbackId,
                RollbackSupportReceiptHash = rollbackHash,
                RollbackPlanId = gate.RollbackPlanId,
                RollbackPlanHash = gate.RollbackPlanHash,
                RollbackGateEvaluationHash = gate.RollbackGateEvaluationHash,
                SubjectKind = patch.SubjectKind,
                SubjectId = patch.SubjectId,
                SubjectHash = patch.SubjectHash,
                SourceSnapshotReference = patch.SourceSnapshotReference,
                SourceBaselineHash = baselineHash,
                WorkspaceBoundaryHash = boundaryHash,
                ExpectedBranch = "main",
                ExpectedCleanWorktreeHash = worktreeHash,
                FileOperations = fileOps,
                RequestedAtUtc = now,
                ExpiresAtUtc = now.AddHours(1),
                SourceApplyRequestHash = sourceApplyRequestHash,
                EvidenceReferences = ["request-evidence"],
                BoundaryMaxims = ["Proposal is not apply."]
            };
            var dryRun = new SourceApplyDryRunReceipt
            {
                SourceApplyDryRunReceiptId = dryRunId,
                ProjectId = projectId,
                SourceApplyDryRunRequestId = Guid.NewGuid(),
                SourceApplyDryRunRequestHash = H("dry-run-request"),
                DryRunSatisfied = true,
                DryRunResultHash = H("dry-run-result"),
                SourceApplyRequestId = requestId,
                SourceApplyRequestHash = sourceApplyRequestHash,
                SourceApplyGateEvaluationId = gateId,
                SourceApplyGateEvaluationHash = gateHash,
                PatchArtifactId = patchId,
                PatchHash = patch.PatchHash,
                ChangeSetHash = changeSetHash,
                RollbackSupportReceiptId = rollbackId,
                RollbackSupportReceiptHash = rollbackHash,
                SourceBaselineHash = baselineHash,
                WorkspaceBoundaryHash = boundaryHash,
                ExpectedBranch = "main",
                ExpectedCleanWorktreeHash = worktreeHash,
                FileResults = fileOps.Select(o => new SourceApplyDryRunReceiptFileResult
                {
                    Path = o.Path,
                    PreviousPath = o.PreviousPath,
                    OperationKind = o.OperationKind,
                    PatchArtifactChangeHash = o.PatchArtifactChangeHash,
                    OperationHash = o.OperationHash,
                    ExpectedBeforeContentHash = o.BeforeContentHash,
                    ExpectedAfterContentHash = o.AfterContentHash,
                    ObservedCurrentContentHash = o.BeforeContentHash,
                    PreconditionsSatisfied = true,
                    WouldCreate = o.OperationKind == SourceApplyRequestFileOperationKinds.CreateFile,
                    WouldModify = o.OperationKind == SourceApplyRequestFileOperationKinds.ModifyFile,
                    WouldDelete = o.OperationKind == SourceApplyRequestFileOperationKinds.DeleteFile,
                    WouldRename = o.OperationKind == SourceApplyRequestFileOperationKinds.RenameFile,
                    WouldNoop = o.OperationKind == SourceApplyRequestFileOperationKinds.Noop,
                    IssueCodes = [],
                    FileResultHash = H("file-result:" + o.Path)
                }).ToArray(),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddHours(1),
                SourceApplyDryRunReceiptHash = dryRunHash,
                EvidenceReferences = ["dry-run-evidence"],
                BoundaryMaxims = ["Dry-run receipt is not source apply."]
            };
            var rollback = new RollbackSupportReceipt
            {
                RollbackSupportReceiptId = rollbackId,
                ProjectId = projectId,
                RollbackPlanId = request.RollbackPlanId,
                RollbackPlanHash = request.RollbackPlanHash,
                RollbackGateSatisfied = true,
                RollbackGateEvaluationHash = request.RollbackGateEvaluationHash,
                PatchArtifactId = patchId,
                PatchHash = patch.PatchHash,
                ChangeSetHash = changeSetHash,
                ControlledDryRunRequestId = patch.ControlledDryRunRequestId,
                DryRunExecutionAuditId = patch.DryRunExecutionAuditId,
                DryRunAuditHash = patch.DryRunAuditHash,
                DryRunReceiptHash = dryRunHash,
                PolicySatisfactionId = patch.PolicySatisfactionId,
                PolicySatisfactionHash = patch.PolicySatisfactionHash,
                SubjectKind = patch.SubjectKind,
                SubjectId = patch.SubjectId,
                SubjectHash = patch.SubjectHash,
                SourceSnapshotReference = patch.SourceSnapshotReference,
                SourceBaselineHash = baselineHash,
                WorkspaceBoundaryHash = boundaryHash,
                ExpectedBranch = "main",
                ExpectedCleanWorktreeHash = worktreeHash,
                RollbackSupportReceiptHash = rollbackHash,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddHours(1),
                EvidenceReferences = ["rollback-evidence"],
                BoundaryMaxims = ["Rollback support is not rollback execution."]
            };
            var controlled = new ControlledSourceApplyRequest
            {
                ControlledSourceApplyRequestId = Guid.NewGuid(),
                ProjectId = projectId,
                SourceApplyRequest = request,
                SourceApplyDryRunReceipt = dryRun,
                PatchArtifact = patch,
                RollbackSupportReceipt = rollback,
                WorkspaceRoot = workspace,
                ApprovedWorkspaceBoundaryHash = boundaryHash,
                ObservedBranch = "main",
                ObservedSourceBaselineHash = baselineHash,
                ObservedCleanWorktreeHashBeforeApply = worktreeHash,
                ApprovedContents = ops
                    .Where(o => o.Kind is SourceApplyRequestFileOperationKinds.CreateFile or SourceApplyRequestFileOperationKinds.ModifyFile)
                    .Select(o => new ControlledSourceApplyContent { Path = o.Path, AfterContentHash = H(o.After ?? string.Empty), Content = o.After ?? string.Empty })
                    .ToArray(),
                RequestedAtUtc = now,
                EvidenceReferences = ["real-apply-evidence"],
                BoundaryMaxims = ["SourceApplyReceipt is mutation evidence, not release approval."]
            };
            return new(controlled, ops);
        }

        public void WriteInitialFiles()
        {
            foreach (var op in _ops)
            {
                var path = op.Kind == SourceApplyRequestFileOperationKinds.RenameFile ? op.PreviousPath! : op.Path;
                var content = op.Kind == SourceApplyRequestFileOperationKinds.CreateFile ? null : op.Before;
                if (content is null) continue;
                var fullPath = Path.Combine(Request.WorkspaceRoot, path);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, content, Encoding.UTF8);
            }
        }

        public ControlledSourceApplyRequest Mutate(string mutation) => mutation switch
        {
            "DryRunMismatch" => Request with { SourceApplyDryRunReceipt = Request.SourceApplyDryRunReceipt with { SourceApplyRequestHash = H("wrong-dry-run") } },
            "RollbackMismatch" => Request with { RollbackSupportReceipt = Request.RollbackSupportReceipt with { PatchHash = H("wrong-rollback") } },
            "PatchMismatch" => Request with { PatchArtifact = Request.PatchArtifact with { ChangeSetHash = H("wrong-patch") } },
            "BranchMismatch" => Request with { ObservedBranch = "feature/not-main" },
            "BaselineMismatch" => Request with { ObservedSourceBaselineHash = H("wrong-baseline") },
            "WorktreeMismatch" => Request with { ObservedCleanWorktreeHashBeforeApply = H("dirty-worktree") },
            "ExpiredDryRun" => Request with { SourceApplyDryRunReceipt = Request.SourceApplyDryRunReceipt with { ExpiresAtUtc = Request.RequestedAtUtc.AddSeconds(-1) } },
            "ExpiredRollback" => Request with { RollbackSupportReceipt = Request.RollbackSupportReceipt with { ExpiresAtUtc = Request.RequestedAtUtc.AddSeconds(-1) } },
            _ => Request
        };

        private static PatchArtifactFileChange Change(Op op) => new()
        {
            Path = op.Path,
            PreviousPath = op.PreviousPath,
            ChangeKind = op.Kind switch
            {
                SourceApplyRequestFileOperationKinds.CreateFile => "Create",
                SourceApplyRequestFileOperationKinds.ModifyFile => "Modify",
                SourceApplyRequestFileOperationKinds.DeleteFile => "Delete",
                SourceApplyRequestFileOperationKinds.RenameFile => "Rename",
                _ => "Modify"
            },
            BeforeContentHash = op.Before is null ? null : H(op.Before),
            AfterContentHash = op.After is null ? null : H(op.After),
            DiffHash = H("diff:" + op.Path),
            NormalizedDiff = "change " + op.Path,
            IsBinary = false
        };

        private static SourceApplyRequestFileOperation FileOp(Op op)
        {
            var change = op.Kind == SourceApplyRequestFileOperationKinds.Noop ? null : Change(op);
            return new SourceApplyRequestFileOperation
            {
                Path = op.Path,
                PreviousPath = op.PreviousPath,
                OperationKind = op.Kind,
                BeforeContentHash = op.Before is null ? null : H(op.Before),
                AfterContentHash = op.After is null ? null : H(op.After),
                DiffHash = change?.DiffHash,
                PatchArtifactChangeHash = change is null ? H("noop:" + op.Path) : PatchArtifactHashing.ComputeFileChangeHash(change),
                OperationHash = H("operation:" + op.Kind + ":" + op.Path)
            };
        }
    }

    private static string H(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private sealed class RecordingStore : ISourceApplyReceiptStore
    {
        public List<SourceApplyReceipt> Saved { get; } = [];
        public Task SaveAsync(SourceApplyReceipt receipt, CancellationToken cancellationToken = default) { Saved.Add(receipt); return Task.CompletedTask; }
        public Task<SourceApplyReceipt?> GetAsync(Guid projectId, Guid sourceApplyReceiptId, CancellationToken cancellationToken = default) => Task.FromResult(Saved.FirstOrDefault(r => r.ProjectId == projectId && r.SourceApplyReceiptId == sourceApplyReceiptId));
        public Task<SourceApplyReceipt?> GetByReceiptHashAsync(Guid projectId, string sourceApplyReceiptHash, CancellationToken cancellationToken = default) => Task.FromResult(Saved.FirstOrDefault(r => r.ProjectId == projectId && r.SourceApplyReceiptHash == sourceApplyReceiptHash));
        public Task<IReadOnlyList<SourceApplyReceipt>> ListBySourceApplyRequestAsync(Guid projectId, Guid sourceApplyRequestId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SourceApplyReceipt>>(Saved.Where(r => r.ProjectId == projectId && r.SourceApplyRequestId == sourceApplyRequestId).ToArray());
        public Task<IReadOnlyList<SourceApplyReceipt>> ListBySourceApplyDryRunReceiptAsync(Guid projectId, Guid sourceApplyDryRunReceiptId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SourceApplyReceipt>>(Saved.Where(r => r.ProjectId == projectId && r.SourceApplyDryRunReceiptId == sourceApplyDryRunReceiptId).ToArray());
        public Task<IReadOnlyList<SourceApplyReceipt>> ListByPatchArtifactAsync(Guid projectId, Guid patchArtifactId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SourceApplyReceipt>>(Saved.Where(r => r.ProjectId == projectId && r.PatchArtifactId == patchArtifactId).ToArray());
        public Task<IReadOnlyList<SourceApplyReceipt>> ListByRollbackSupportReceiptAsync(Guid projectId, Guid rollbackSupportReceiptId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SourceApplyReceipt>>(Saved.Where(r => r.ProjectId == projectId && r.RollbackSupportReceiptId == rollbackSupportReceiptId).ToArray());
    }
}

[TestClass]
[TestCategory("SourceApplyReceiptStore")]
[TestCategory("SourceApplyNarrowRealApply")]
[TestCategory("PR204")]
public sealed class SourceApplyReceiptStoreExpandedTests : IntegrationTestBase
{
    private SqlSourceApplyReceiptStore _store = default!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropSourceApplyReceiptAsync();
        await ApplySqlFileAsync("Database", "migrate_source_apply_receipt.sql");
        _store = new SqlSourceApplyReceiptStore(ServiceProvider.GetRequiredService<IDbConnectionFactory>());
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        await DropSourceApplyReceiptAsync();
        await base.TestCleanup();
    }

    [TestMethod]
    public async Task SourceApplyReceiptStore_SaveGetListAndHashRoundTrip()
    {
        var receipt = ValidReceipt();
        await _store.SaveAsync(receipt);

        AssertReceipt(receipt, await _store.GetAsync(receipt.ProjectId, receipt.SourceApplyReceiptId));
        AssertReceipt(receipt, await _store.GetByReceiptHashAsync(receipt.ProjectId, receipt.SourceApplyReceiptHash));
        AssertSingle(receipt, await _store.ListBySourceApplyRequestAsync(receipt.ProjectId, receipt.SourceApplyRequestId));
        AssertSingle(receipt, await _store.ListBySourceApplyDryRunReceiptAsync(receipt.ProjectId, receipt.SourceApplyDryRunReceiptId));
        AssertSingle(receipt, await _store.ListByPatchArtifactAsync(receipt.ProjectId, receipt.PatchArtifactId));
        AssertSingle(receipt, await _store.ListByRollbackSupportReceiptAsync(receipt.ProjectId, receipt.RollbackSupportReceiptId));
    }

    [TestMethod]
    public async Task SourceApplyReceiptStore_IdempotentSameHashAndConflictDifferentHash()
    {
        var receipt = ValidReceipt();
        await _store.SaveAsync(receipt);
        await _store.SaveAsync(receipt);

        await ExpectSqlExceptionAsync(() => _store.SaveAsync(receipt with { SourceApplyReceiptHash = H("different") }));
    }

    [TestMethod]
    public async Task SourceApplyReceiptStore_BlocksDirectUpdateAndDelete()
    {
        var receipt = ValidReceipt();
        await _store.SaveAsync(receipt);

        await ExpectSqlExceptionAsync(() => ExecuteSqlAsync(
            "UPDATE governance.SourceApplyReceipt SET ObservedBranch = @ObservedBranch WHERE SourceApplyReceiptId = @SourceApplyReceiptId",
            new { ObservedBranch = "other", receipt.SourceApplyReceiptId }));
        await ExpectSqlExceptionAsync(() => ExecuteSqlAsync(
            "DELETE FROM governance.SourceApplyReceipt WHERE SourceApplyReceiptId = @SourceApplyReceiptId",
            new { receipt.SourceApplyReceiptId }));
    }


    private async Task ApplySqlFileAsync(params string[] pathParts)
    {
        var root = FindRepositoryRoot();
        var sql = await File.ReadAllTextAsync(Path.Combine([root, .. pathParts]));
        using var connection = ServiceProvider.GetRequiredService<IDbConnectionFactory>().CreateConnection();
        foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(batch))
            {
                await connection.ExecuteAsync(new CommandDefinition(batch, commandType: CommandType.Text));
            }
        }
    }

    private static async Task ExpectSqlExceptionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (SqlException)
        {
            return;
        }

        Assert.Fail("Expected SqlException.");
    }    private async Task ExecuteSqlAsync(string sql, object parameters)
    {
        using var connection = ServiceProvider.GetRequiredService<IDbConnectionFactory>().CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, commandType: CommandType.Text));
    }

    private static SourceApplyReceipt ValidReceipt()
    {
        var fileResult = new SourceApplyReceiptFileResult
        {
            Path = "src/feature.txt",
            PreviousPath = null,
            OperationKind = SourceApplyRequestFileOperationKinds.ModifyFile,
            PatchArtifactChangeHash = H("patch-change"),
            OperationHash = H("operation"),
            BeforeContentHash = H("before"),
            AfterContentHash = H("after"),
            PreconditionsSatisfied = true,
            MutationApplied = true,
            Created = false,
            Modified = true,
            Deleted = false,
            Renamed = false,
            Noop = false,
            IssueCodes = [],
            FileResultHash = "sha256:pending"
        };
        fileResult = fileResult with { FileResultHash = SourceApplyReceiptHashing.ComputeFileResultHash(fileResult) };
        var receipt = new SourceApplyReceipt
        {
            SourceApplyReceiptId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            ControlledSourceApplyRequestId = Guid.NewGuid(),
            SourceApplyRequestId = Guid.NewGuid(),
            SourceApplyRequestHash = H("source-request"),
            SourceApplyDryRunReceiptId = Guid.NewGuid(),
            SourceApplyDryRunReceiptHash = H("dry-run-receipt"),
            SourceApplyGateEvaluationId = Guid.NewGuid(),
            SourceApplyGateEvaluationHash = H("gate"),
            PatchArtifactId = Guid.NewGuid(),
            PatchHash = H("patch"),
            ChangeSetHash = H("change-set"),
            RollbackSupportReceiptId = Guid.NewGuid(),
            RollbackSupportReceiptHash = H("rollback"),
            SourceBaselineHash = H("baseline"),
            WorkspaceBoundaryHash = H("workspace"),
            ExpectedBranch = "main",
            ExpectedCleanWorktreeHash = H("clean-before"),
            ObservedBranch = "main",
            ObservedCleanWorktreeHashBeforeApply = H("clean-before"),
            ObservedCleanWorktreeHashAfterApply = H("clean-after"),
            MutationOccurred = true,
            ApplySucceeded = true,
            PartialApplyOccurred = false,
            FileResults = [fileResult],
            IssueCodes = ["NoIssues"],
            AppliedAtUtc = new DateTimeOffset(2026, 6, 17, 9, 0, 0, TimeSpan.Zero),
            SourceApplyReceiptHash = "sha256:pending",
            EvidenceReferences = ["source-apply-receipt-evidence"],
            BoundaryMaxims = ["SourceApplyReceipt is mutation evidence, not release approval."],
            Boundary = SourceApplyReceiptBoundaryText.Boundary
        };
        return receipt with { SourceApplyReceiptHash = SourceApplyReceiptHashing.ComputeReceiptHash(receipt) };
    }

    private static void AssertReceipt(SourceApplyReceipt expected, SourceApplyReceipt? actual)
    {
        Assert.IsNotNull(actual);
        Assert.AreEqual(expected.SourceApplyReceiptId, actual!.SourceApplyReceiptId);
        Assert.AreEqual(expected.SourceApplyReceiptHash, actual.SourceApplyReceiptHash);
        Assert.AreEqual(expected.FileResults[0].FileResultHash, actual.FileResults[0].FileResultHash);
    }

    private static void AssertSingle(SourceApplyReceipt expected, IReadOnlyList<SourceApplyReceipt> actual)
    {
        Assert.AreEqual(1, actual.Count);
        Assert.AreEqual(expected.SourceApplyReceiptId, actual[0].SourceApplyReceiptId);
    }

    private async Task DropSourceApplyReceiptAsync()
    {
        using var connection = ServiceProvider.GetRequiredService<IDbConnectionFactory>().CreateConnection();
        var sql = """
            IF OBJECT_ID(N'governance.TR_SourceApplyReceipt_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_SourceApplyReceipt_BlockUpdateDelete;
            IF OBJECT_ID(N'governance.TR_SourceApplyReceipt_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER governance.TR_SourceApplyReceipt_ValidateInsert;
            DROP PROCEDURE IF EXISTS governance.usp_SourceApplyReceipt_ListByRollbackSupportReceipt;
            DROP PROCEDURE IF EXISTS governance.usp_SourceApplyReceipt_ListByPatchArtifact;
            DROP PROCEDURE IF EXISTS governance.usp_SourceApplyReceipt_ListBySourceApplyDryRunReceipt;
            DROP PROCEDURE IF EXISTS governance.usp_SourceApplyReceipt_ListBySourceApplyRequest;
            DROP PROCEDURE IF EXISTS governance.usp_SourceApplyReceipt_GetByReceiptHash;
            DROP PROCEDURE IF EXISTS governance.usp_SourceApplyReceipt_Get;
            DROP PROCEDURE IF EXISTS governance.usp_SourceApplyReceipt_Save;
            IF OBJECT_ID(N'governance.SourceApplyReceipt', N'U') IS NOT NULL DROP TABLE governance.SourceApplyReceipt;
            """;
        await connection.ExecuteAsync(new CommandDefinition(sql, commandType: CommandType.Text));
    }

    private static string H(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
