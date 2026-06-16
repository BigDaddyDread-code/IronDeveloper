using System.Security.Cryptography;
using System.Text;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("SourceApplyNarrowRealApply")]
[TestCategory("SourceApplyReceipt")]
[TestCategory("PR204")]
public sealed class SourceApplyNarrowRealApplyTests
{
    [TestMethod]
    public async Task ControlledSourceApplyExecutor_WritesOnlyApprovedPatchArtifactContent()
    {
        var workspace = CreateWorkspace();
        var before = "before content";
        var after = "after content";
        var target = Path.Combine(workspace, "src", "feature.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, before, Encoding.UTF8);

        var fixture = Fixture(workspace, before, after);
        var store = new RecordingSourceApplyReceiptStore();
        var executor = new ControlledSourceApplyExecutor(store);

        var result = await executor.ApplyAsync(fixture.Request);

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Code)));
        Assert.IsTrue(result.MutationOccurred);
        Assert.IsFalse(result.PartialApplyOccurred);
        Assert.IsNotNull(result.Receipt);
        Assert.AreEqual(after, await File.ReadAllTextAsync(target, Encoding.UTF8));
        Assert.AreEqual(1, store.Saved.Count);
        Assert.IsTrue(store.Saved[0].ApplySucceeded);
        Assert.IsTrue(store.Saved[0].MutationOccurred);
        Assert.IsFalse(store.Saved[0].PartialApplyOccurred);
    }

    [TestMethod]
    public async Task ControlledSourceApplyExecutor_RejectsContentHashMismatchBeforeMutation()
    {
        var workspace = CreateWorkspace();
        var before = "before content";
        var after = "after content";
        var target = Path.Combine(workspace, "src", "feature.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, before, Encoding.UTF8);

        var fixture = Fixture(workspace, before, after);
        var badRequest = fixture.Request with
        {
            ApprovedContents =
            [
                fixture.Request.ApprovedContents[0] with { Content = "tampered content" }
            ]
        };
        var store = new RecordingSourceApplyReceiptStore();
        var executor = new ControlledSourceApplyExecutor(store);

        var result = await executor.ApplyAsync(badRequest);

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.MutationOccurred);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == "ContentHashMismatch"));
        Assert.AreEqual(before, await File.ReadAllTextAsync(target, Encoding.UTF8));
        Assert.AreEqual(0, store.Saved.Count);
    }

    [TestMethod]
    public void SourceApplyReceiptSqlMigration_DeclaresAppendOnlyReceiptBoundary()
    {
        var root = FindRepositoryRoot();
        var sql = File.ReadAllText(Path.Combine(root, "Database", "migrate_source_apply_receipt.sql"));
        var manifest = File.ReadAllText(Path.Combine(root, "Database", "migrations.json"));

        StringAssert.Contains(sql, "governance.SourceApplyReceipt");
        StringAssert.Contains(sql, "TR_SourceApplyReceipt_BlockUpdateDelete");
        StringAssert.Contains(sql, "usp_SourceApplyReceipt_Save");
        StringAssert.Contains(sql, "DENY INSERT, UPDATE, DELETE ON OBJECT::governance.SourceApplyReceipt");
        StringAssert.Contains(manifest, "Database/migrate_source_apply_receipt.sql");
    }

    private static TestFixture Fixture(string workspace, string before, string after)
    {
        var projectId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var gateId = Guid.NewGuid();
        var patchId = Guid.NewGuid();
        var rollbackId = Guid.NewGuid();
        var dryRunId = Guid.NewGuid();
        var controlledRequestId = Guid.NewGuid();
        var beforeHash = Hash(before);
        var afterHash = Hash(after);
        var sourceApplyRequestHash = Hash("source-apply-request");
        var gateHash = Hash("source-apply-gate");
        var dryRunHash = Hash("source-apply-dry-run");
        var dryRunResultHash = Hash("source-apply-dry-run-result");
        var rollbackHash = Hash("rollback-support");
        var sourceBaselineHash = Hash("source-baseline");
        var workspaceBoundaryHash = Hash("workspace-boundary");
        var expectedWorktreeHash = Hash("clean-worktree");
        var operationHash = Hash("operation");
        var diffHash = Hash("diff");
        var now = new DateTimeOffset(2026, 6, 17, 8, 0, 0, TimeSpan.Zero);

        var change = new PatchArtifactFileChange
        {
            Path = "src/feature.txt",
            PreviousPath = null,
            ChangeKind = "Modify",
            BeforeContentHash = beforeHash,
            AfterContentHash = afterHash,
            DiffHash = diffHash,
            NormalizedDiff = "modify src/feature.txt",
            IsBinary = false
        };
        var changeHash = PatchArtifactHashing.ComputeFileChangeHash(change);
        var changeSetHash = PatchArtifactHashing.ComputeChangeSetHash([change]);
        var patch = new PatchArtifact
        {
            PatchArtifactId = patchId,
            ProjectId = projectId,
            PatchArtifactKind = "SourcePatch",
            ControlledDryRunRequestId = Guid.NewGuid(),
            DryRunExecutionAuditId = Guid.NewGuid(),
            DryRunAuditHash = Hash("dry-run-audit"),
            DryRunReceiptHash = dryRunHash,
            PolicySatisfactionId = Guid.NewGuid(),
            PolicySatisfactionHash = Hash("policy-satisfaction"),
            SubjectKind = "SourceApplyRequest",
            SubjectId = requestId.ToString("D"),
            SubjectHash = sourceApplyRequestHash,
            SourceSnapshotReference = "snapshot-main",
            SourceBaselineHash = sourceBaselineHash,
            WorkspaceBoundaryHash = workspaceBoundaryHash,
            ValidationPlanId = "validation-plan",
            ValidationPlanHash = Hash("validation-plan"),
            PatchHash = "sha256:pending",
            ChangeSetHash = changeSetHash,
            FileChanges = [change],
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(1),
            EvidenceReferences = ["patch-evidence"],
            BoundaryMaxims = ["Patch artifact is not source apply."],
            Boundary = PatchArtifactBoundaryText.Boundary
        };
        patch = patch with { PatchHash = PatchArtifactHashing.ComputePatchHash(patch, changeSetHash) };

        var gateEvidence = new SourceApplyRequestGateEvaluationEvidence
        {
            SourceApplyGateEvaluationId = gateId,
            SourceApplyGateEvaluationHash = gateHash,
            Satisfied = true,
            ProjectId = projectId,
            AcceptedApprovalId = Guid.NewGuid(),
            AcceptedApprovalHash = Hash("accepted-approval"),
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
            RollbackPlanHash = Hash("rollback-plan"),
            RollbackGateEvaluationHash = Hash("rollback-gate"),
            SubjectKind = patch.SubjectKind,
            SubjectId = patch.SubjectId,
            SubjectHash = patch.SubjectHash,
            SourceSnapshotReference = patch.SourceSnapshotReference,
            SourceBaselineHash = sourceBaselineHash,
            WorkspaceBoundaryHash = workspaceBoundaryHash,
            ExpectedBranch = "main",
            ExpectedCleanWorktreeHash = expectedWorktreeHash,
            ExpiresAtUtc = now.AddHours(1),
            EvidenceReferences = ["gate-evidence"],
            BoundaryMaxims = ["Gate is not executor."]
        };

        var request = new SourceApplyRequest
        {
            SourceApplyRequestId = requestId,
            ProjectId = projectId,
            SourceApplyGateEvaluationId = gateId,
            SourceApplyGateEvaluationHash = gateHash,
            SourceApplyGateSatisfied = true,
            SourceApplyGateEvaluation = gateEvidence,
            AcceptedApprovalId = gateEvidence.AcceptedApprovalId,
            AcceptedApprovalHash = gateEvidence.AcceptedApprovalHash,
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
            RollbackPlanId = gateEvidence.RollbackPlanId,
            RollbackPlanHash = gateEvidence.RollbackPlanHash,
            RollbackGateEvaluationHash = gateEvidence.RollbackGateEvaluationHash,
            SubjectKind = patch.SubjectKind,
            SubjectId = patch.SubjectId,
            SubjectHash = patch.SubjectHash,
            SourceSnapshotReference = patch.SourceSnapshotReference,
            SourceBaselineHash = sourceBaselineHash,
            WorkspaceBoundaryHash = workspaceBoundaryHash,
            ExpectedBranch = "main",
            ExpectedCleanWorktreeHash = expectedWorktreeHash,
            FileOperations =
            [
                new SourceApplyRequestFileOperation
                {
                    Path = "src/feature.txt",
                    OperationKind = SourceApplyRequestFileOperationKinds.ModifyFile,
                    PreviousPath = null,
                    BeforeContentHash = beforeHash,
                    AfterContentHash = afterHash,
                    DiffHash = diffHash,
                    PatchArtifactChangeHash = changeHash,
                    OperationHash = operationHash
                }
            ],
            RequestedAtUtc = now,
            ExpiresAtUtc = now.AddHours(1),
            SourceApplyRequestHash = sourceApplyRequestHash,
            EvidenceReferences = ["source-apply-request-evidence"],
            BoundaryMaxims = ["Proposal is not apply."]
        };

        var dryRun = new SourceApplyDryRunReceipt
        {
            SourceApplyDryRunReceiptId = dryRunId,
            ProjectId = projectId,
            SourceApplyDryRunRequestId = Guid.NewGuid(),
            SourceApplyDryRunRequestHash = Hash("source-apply-dry-run-request"),
            DryRunSatisfied = true,
            DryRunResultHash = dryRunResultHash,
            SourceApplyRequestId = requestId,
            SourceApplyRequestHash = sourceApplyRequestHash,
            SourceApplyGateEvaluationId = gateId,
            SourceApplyGateEvaluationHash = gateHash,
            PatchArtifactId = patchId,
            PatchHash = patch.PatchHash,
            ChangeSetHash = changeSetHash,
            RollbackSupportReceiptId = rollbackId,
            RollbackSupportReceiptHash = rollbackHash,
            SourceBaselineHash = sourceBaselineHash,
            WorkspaceBoundaryHash = workspaceBoundaryHash,
            ExpectedBranch = "main",
            ExpectedCleanWorktreeHash = expectedWorktreeHash,
            FileResults =
            [
                new SourceApplyDryRunReceiptFileResult
                {
                    Path = "src/feature.txt",
                    PreviousPath = null,
                    OperationKind = SourceApplyRequestFileOperationKinds.ModifyFile,
                    PatchArtifactChangeHash = changeHash,
                    OperationHash = operationHash,
                    ExpectedBeforeContentHash = beforeHash,
                    ExpectedAfterContentHash = afterHash,
                    ObservedCurrentContentHash = beforeHash,
                    PreconditionsSatisfied = true,
                    WouldCreate = false,
                    WouldModify = true,
                    WouldDelete = false,
                    WouldRename = false,
                    WouldNoop = false,
                    IssueCodes = [],
                    FileResultHash = Hash("file-result")
                }
            ],
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
            SourceBaselineHash = sourceBaselineHash,
            WorkspaceBoundaryHash = workspaceBoundaryHash,
            ExpectedBranch = "main",
            ExpectedCleanWorktreeHash = expectedWorktreeHash,
            RollbackSupportReceiptHash = rollbackHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(1),
            EvidenceReferences = ["rollback-evidence"],
            BoundaryMaxims = ["Rollback support is not rollback execution."]
        };

        var controlled = new ControlledSourceApplyRequest
        {
            ControlledSourceApplyRequestId = controlledRequestId,
            ProjectId = projectId,
            SourceApplyRequest = request,
            SourceApplyDryRunReceipt = dryRun,
            PatchArtifact = patch,
            RollbackSupportReceipt = rollback,
            WorkspaceRoot = workspace,
            ApprovedWorkspaceBoundaryHash = workspaceBoundaryHash,
            ObservedBranch = "main",
            ObservedSourceBaselineHash = sourceBaselineHash,
            ObservedCleanWorktreeHashBeforeApply = expectedWorktreeHash,
            ApprovedContents =
            [
                new ControlledSourceApplyContent
                {
                    Path = "src/feature.txt",
                    AfterContentHash = afterHash,
                    Content = after
                }
            ],
            RequestedAtUtc = now,
            EvidenceReferences = ["real-apply-evidence"],
            BoundaryMaxims = ["SourceApplyReceipt is mutation evidence, not release approval."]
        };

        return new(controlled);
    }

    private static string CreateWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "irondev-pr204-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Hash(string value)
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

    private sealed record TestFixture(ControlledSourceApplyRequest Request);

    private sealed class RecordingSourceApplyReceiptStore : ISourceApplyReceiptStore
    {
        public List<SourceApplyReceipt> Saved { get; } = [];

        public Task SaveAsync(SourceApplyReceipt receipt, CancellationToken cancellationToken = default)
        {
            Saved.Add(receipt);
            return Task.CompletedTask;
        }

        public Task<SourceApplyReceipt?> GetAsync(Guid projectId, Guid sourceApplyReceiptId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Saved.FirstOrDefault(receipt => receipt.ProjectId == projectId && receipt.SourceApplyReceiptId == sourceApplyReceiptId));

        public Task<SourceApplyReceipt?> GetByReceiptHashAsync(Guid projectId, string sourceApplyReceiptHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(Saved.FirstOrDefault(receipt => receipt.ProjectId == projectId && receipt.SourceApplyReceiptHash == sourceApplyReceiptHash));

        public Task<IReadOnlyList<SourceApplyReceipt>> ListBySourceApplyRequestAsync(Guid projectId, Guid sourceApplyRequestId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SourceApplyReceipt>>(Saved.Where(receipt => receipt.ProjectId == projectId && receipt.SourceApplyRequestId == sourceApplyRequestId).ToArray());

        public Task<IReadOnlyList<SourceApplyReceipt>> ListBySourceApplyDryRunReceiptAsync(Guid projectId, Guid sourceApplyDryRunReceiptId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SourceApplyReceipt>>(Saved.Where(receipt => receipt.ProjectId == projectId && receipt.SourceApplyDryRunReceiptId == sourceApplyDryRunReceiptId).ToArray());

        public Task<IReadOnlyList<SourceApplyReceipt>> ListByPatchArtifactAsync(Guid projectId, Guid patchArtifactId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SourceApplyReceipt>>(Saved.Where(receipt => receipt.ProjectId == projectId && receipt.PatchArtifactId == patchArtifactId).ToArray());

        public Task<IReadOnlyList<SourceApplyReceipt>> ListByRollbackSupportReceiptAsync(Guid projectId, Guid rollbackSupportReceiptId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SourceApplyReceipt>>(Saved.Where(receipt => receipt.ProjectId == projectId && receipt.RollbackSupportReceiptId == rollbackSupportReceiptId).ToArray());
    }
}
