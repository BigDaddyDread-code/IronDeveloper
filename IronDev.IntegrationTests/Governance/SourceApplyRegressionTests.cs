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
[TestCategory("SourceApplyRegression")]
[TestCategory("PR205")]
public sealed class SourceApplyRegressionTests
{
    [TestMethod]
    public async Task SourceApplyRegression_DryRunReceiptDoesNotMutateSourceOrCreateRealReceipt()
    {
        var workspace = Workspace();
        var fixture = Fixture.Create(workspace, [Op.Modify("src/file.txt", "before", "after")]);
        fixture.WriteInitialFiles();
        var before = await File.ReadAllTextAsync(Path.Combine(workspace, "src", "file.txt"), Encoding.UTF8);
        var store = new RecordingStore();

        Assert.IsTrue(fixture.Request.SourceApplyDryRunReceipt.DryRunSatisfied);
        Assert.AreEqual(before, await File.ReadAllTextAsync(Path.Combine(workspace, "src", "file.txt"), Encoding.UTF8));
        Assert.AreEqual(0, store.Saved.Count);
        Assert.IsFalse(fixture.Request.SourceApplyDryRunReceipt.Boundary.Contains("CanApplySource", StringComparison.OrdinalIgnoreCase));
    }

    [DataTestMethod]
    [DataRow("MissingAcceptedApproval")]
    [DataRow("MissingPolicySatisfaction")]
    [DataRow("GateUnsatisfied")]
    [DataRow("DryRunUnsatisfied")]
    [DataRow("SourceApplyRequestHashMismatch")]
    [DataRow("SourceApplyGateHashMismatch")]
    [DataRow("PatchArtifactIdMismatch")]
    [DataRow("PatchHashMismatch")]
    [DataRow("ChangeSetMismatch")]
    [DataRow("RollbackReceiptIdMismatch")]
    [DataRow("RollbackReceiptHashMismatch")]
    [DataRow("RollbackPatchMismatch")]
    [DataRow("RollbackExpired")]
    [DataRow("DryRunExpired")]
    [DataRow("PatchExpired")]
    [DataRow("SourceBaselineMismatch")]
    [DataRow("WorkspaceBoundaryMismatch")]
    [DataRow("ExpectedBranchMismatch")]
    [DataRow("CleanWorktreeHashMismatch")]
    [DataRow("DryRunFileMissing")]
    [DataRow("DryRunPreconditionsUnsatisfied")]
    [DataRow("MissingPatchArtifactFileChange")]
    public async Task SourceApplyRegression_RealApplyRejectsEvidenceMismatchBeforeMutation(string mutation)
    {
        var workspace = Workspace();
        var fixture = Fixture.Create(workspace, [Op.Modify("src/file.txt", "before", "after")]);
        fixture.WriteInitialFiles();
        var store = new RecordingStore();

        var result = await new ControlledSourceApplyExecutor(store).ApplyAsync(fixture.Mutate(mutation));

        AssertRejectedBeforeMutation(result, store);
        Assert.AreEqual("before", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "file.txt"), Encoding.UTF8), mutation);
    }

    [TestMethod]
    public async Task SourceApplyRegression_RealApplyRejectsCurrentHashMismatchBeforeMutation()
    {
        var workspace = Workspace();
        var fixture = Fixture.Create(workspace, [Op.Modify("src/file.txt", "before", "after")]);
        fixture.WriteInitialFiles();
        await File.WriteAllTextAsync(Path.Combine(workspace, "src", "file.txt"), "changed outside approved chain", Encoding.UTF8);
        var store = new RecordingStore();

        var result = await new ControlledSourceApplyExecutor(store).ApplyAsync(fixture.Request);

        AssertRejectedBeforeMutation(result, store);
        Assert.AreEqual("changed outside approved chain", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "file.txt"), Encoding.UTF8));
    }

    [TestMethod]
    public async Task SourceApplyRegression_RealApplyRejectsApprovedContentHashMismatchBeforeMutation()
    {
        var workspace = Workspace();
        var fixture = Fixture.Create(workspace, [Op.Modify("src/file.txt", "before", "after")]);
        fixture.WriteInitialFiles();
        var bad = fixture.Request with
        {
            ApprovedContents =
            [
                fixture.Request.ApprovedContents[0] with { Content = "tampered approved content" }
            ]
        };
        var store = new RecordingStore();

        var result = await new ControlledSourceApplyExecutor(store).ApplyAsync(bad);

        AssertRejectedBeforeMutation(result, store);
        Assert.AreEqual("before", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "file.txt"), Encoding.UTF8));
    }

    [TestMethod]
    public async Task SourceApplyRegression_RealApplyRejectsPatchAfterHashMismatchBeforeMutation()
    {
        var workspace = Workspace();
        var fixture = Fixture.Create(workspace, [Op.Modify("src/file.txt", "before", "after")]);
        fixture.WriteInitialFiles();
        var changed = fixture.Request.PatchArtifact.FileChanges[0] with { AfterContentHash = H("wrong-after") };
        var bad = fixture.Request with
        {
            PatchArtifact = fixture.Request.PatchArtifact with { FileChanges = [changed] }
        };
        var store = new RecordingStore();

        var result = await new ControlledSourceApplyExecutor(store).ApplyAsync(bad);

        AssertRejectedBeforeMutation(result, store);
        Assert.AreEqual("before", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "file.txt"), Encoding.UTF8));
    }

    [DataTestMethod]
    [DataRow("../escape.txt")]
    [DataRow("src/../escape.txt")]
    [DataRow("src\\.\\escape.txt")]
    [DataRow(".git/config")]
    [DataRow("")]
    public async Task SourceApplyRegression_RealApplyRejectsUnsafeRelativePathBeforeMutation(string path)
    {
        var workspace = Workspace();
        var fixture = Fixture.Create(workspace, [Op.Modify("src/file.txt", "before", "after")]);
        fixture.WriteInitialFiles();
        var bad = fixture.WithFileOperation(fixture.Request.SourceApplyRequest.FileOperations[0] with { Path = path });
        var store = new RecordingStore();

        var result = await new ControlledSourceApplyExecutor(store).ApplyAsync(bad);

        AssertRejectedBeforeMutation(result, store);
        Assert.AreEqual("before", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "file.txt"), Encoding.UTF8));
    }

    [TestMethod]
    public async Task SourceApplyRegression_RealApplyRejectsRootedPathBeforeMutation()
    {
        var workspace = Workspace();
        var fixture = Fixture.Create(workspace, [Op.Modify("src/file.txt", "before", "after")]);
        fixture.WriteInitialFiles();
        var bad = fixture.WithFileOperation(fixture.Request.SourceApplyRequest.FileOperations[0] with { Path = Path.GetFullPath(Path.Combine(workspace, "..", "escape.txt")) });
        var store = new RecordingStore();

        var result = await new ControlledSourceApplyExecutor(store).ApplyAsync(bad);

        AssertRejectedBeforeMutation(result, store);
        Assert.AreEqual("before", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "file.txt"), Encoding.UTF8));
    }

    [TestMethod]
    public async Task SourceApplyRegression_RealApplyRejectsCreateWhenTargetExistsBeforeMutation()
    {
        var workspace = Workspace();
        var fixture = Fixture.Create(workspace, [Op.Create("src/file.txt", "after")]);
        Directory.CreateDirectory(Path.Combine(workspace, "src"));
        await File.WriteAllTextAsync(Path.Combine(workspace, "src", "file.txt"), "already exists", Encoding.UTF8);
        var store = new RecordingStore();

        var result = await new ControlledSourceApplyExecutor(store).ApplyAsync(fixture.Request);

        AssertRejectedBeforeMutation(result, store);
        Assert.AreEqual("already exists", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "file.txt"), Encoding.UTF8));
    }

    [TestMethod]
    public async Task SourceApplyRegression_RealApplyRejectsUnsupportedOperationBeforeMutation()
    {
        var workspace = Workspace();
        var fixture = Fixture.Create(workspace, [Op.Modify("src/file.txt", "before", "after")]);
        fixture.WriteInitialFiles();
        var bad = fixture.WithFileOperation(fixture.Request.SourceApplyRequest.FileOperations[0] with { OperationKind = "LaunchRocket" });
        var store = new RecordingStore();

        var result = await new ControlledSourceApplyExecutor(store).ApplyAsync(bad);

        AssertRejectedBeforeMutation(result, store);
        Assert.AreEqual("before", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "file.txt"), Encoding.UTF8));
    }

    [TestMethod]
    public async Task SourceApplyRegression_RealApplyRejectsDuplicateOperationBeforeMutation()
    {
        var workspace = Workspace();
        var fixture = Fixture.Create(workspace, [Op.Modify("src/file.txt", "before", "after")]);
        fixture.WriteInitialFiles();
        var op = fixture.Request.SourceApplyRequest.FileOperations[0];
        var bad = fixture.Request with
        {
            SourceApplyRequest = fixture.Request.SourceApplyRequest with { FileOperations = [op, op] }
        };
        var store = new RecordingStore();

        var result = await new ControlledSourceApplyExecutor(store).ApplyAsync(bad);

        AssertRejectedBeforeMutation(result, store);
        Assert.AreEqual("before", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "file.txt"), Encoding.UTF8));
    }

    [TestMethod]
    public async Task SourceApplyRegression_PartialApplyReceiptPreservesFullPlanAndNoAuthority()
    {
        var workspace = Workspace();
        var fixture = Fixture.Create(workspace,
        [
            Op.Modify("src/a-first.txt", "one", "one updated"),
            Op.Modify("src/m-second.txt", "two", "two updated"),
            Op.Modify("src/z-third.txt", "three", "three updated")
        ]);
        fixture.WriteInitialFiles();
        var blocked = Path.Combine(workspace, "src", "m-second.txt");
        File.SetAttributes(blocked, FileAttributes.ReadOnly);
        var store = new RecordingStore();

        var result = await new ControlledSourceApplyExecutor(store).ApplyAsync(fixture.Request);

        File.SetAttributes(blocked, FileAttributes.Normal);
        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.MutationOccurred);
        Assert.IsTrue(result.PartialApplyOccurred);
        Assert.IsNotNull(result.Receipt);
        Assert.AreEqual(3, result.Receipt!.FileResults.Count);
        Assert.IsTrue(result.Receipt.FileResults.Any(file => file.Path == "src/a-first.txt" && file.MutationApplied));
        Assert.IsTrue(result.Receipt.FileResults.Any(file => file.Path == "src/m-second.txt" && !file.MutationApplied && file.IssueCodes.Contains("ApplyFailed")));
        Assert.IsTrue(result.Receipt.FileResults.Any(file => file.Path == "src/z-third.txt" && !file.MutationApplied));
        Assert.IsFalse(result.Receipt.ApplySucceeded);
        Assert.IsTrue(result.Receipt.PartialApplyOccurred);
        Assert.AreEqual(1, store.Saved.Count);
        AssertReceiptDoesNotGrantAuthority(result.Receipt);
    }

    [TestMethod]
    public async Task SourceApplyRegression_SourceApplyReceiptBoundaryDoesNotGrantWorkflowReleaseRollbackGitMemoryOrRetrieval()
    {
        var workspace = Workspace();
        var fixture = Fixture.Create(workspace, [Op.Modify("src/file.txt", "before", "after")]);
        fixture.WriteInitialFiles();

        var result = await new ControlledSourceApplyExecutor(new RecordingStore()).ApplyAsync(fixture.Request);

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Code)));
        Assert.IsNotNull(result.Receipt);
        AssertReceiptDoesNotGrantAuthority(result.Receipt!);
        foreach (var statement in new[]
        {
            "SourceApplyReceipt is mutation evidence, not release approval.",
            "SourceApplyReceipt is not workflow continuation.",
            "SourceApplyReceipt is not rollback execution.",
            "SourceApplyReceipt does not authorize further source mutation.",
            "SourceApplyReceipt does not promote memory or activate retrieval.",
            "SourceApplyReceipt does not create git commits, pushes, merges, branches, or pull requests."
        })
        {
            StringAssert.Contains(result.Receipt!.Boundary, statement);
        }
    }

    [TestMethod]
    public void SourceApplyRegression_NoUnexpectedApiCliUiRuntimeWorkflowReleaseRollbackGitAgentModelMemoryOrRetrievalPath()
    {
        var root = RepositoryRoot();
        var realApplyApiControllers = Directory.GetFiles(Path.Combine(root, "IronDev.Api", "Controllers"), "*SourceApply*Receipt*Controller.cs")
            .Where(path => !path.EndsWith("SourceApplyDryRunReceiptsV1Controller.cs", StringComparison.Ordinal))
            .ToArray();
        Assert.AreEqual(0, realApplyApiControllers.Length, "PR205 must not expose a real source-apply receipt or execution API.");

        foreach (var file in SourceApplyProductionFiles(root))
        {
            var text = File.ReadAllText(file);
            var isExecutor = file.EndsWith(Path.Combine("IronDev.Infrastructure", "Governance", "ControlledSourceApplyExecutor.cs"), StringComparison.Ordinal);
            var isReceiptBoundaryModel = file.EndsWith(Path.Combine("IronDev.Core", "Governance", "SourceApplyReceipt.cs"), StringComparison.Ordinal);
            foreach (var token in new[] { "File.WriteAllText", "File.WriteAllBytes", "File.Delete", "File.Move", "Directory.CreateDirectory" })
            {
                if (isExecutor)
                {
                    continue;
                }

                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"File mutation token outside controlled executor: {file} {token}");
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
                "RollbackExecutor",
                "AgentDispatch",
                "ModelProvider",
                "ToolInvoker",
                "PromoteMemory",
                "ActivateRetrieval",
                "Weaviate",
                "Embedding"
            })
            {
                if (isReceiptBoundaryModel && token.StartsWith("git ", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Forbidden runtime token found in {file}: {token}");
            }
        }
    }

    [TestMethod]
    public void SourceApplyRegression_ReceiptDocumentLocksCageWithoutExpandingIt()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR205_SOURCE_APPLY_REGRESSION_TESTS.md"));

        foreach (var statement in new[]
        {
            "PR205 adds source-apply regression tests only.",
            "PR205 does not add source apply behavior.",
            "PR205 does not mutate source outside tests.",
            "PR205 does not add SQL.",
            "PR205 does not add API.",
            "PR205 does not add CLI.",
            "PR205 does not add UI.",
            "PR205 does not add runtime execution.",
            "PR205 does not execute rollback.",
            "PR205 does not continue workflow.",
            "PR205 does not approve release.",
            "PR205 does not infer release readiness.",
            "PR205 does not call agents, models, or tools.",
            "PR205 does not promote memory.",
            "PR205 does not activate retrieval.",
            "PR205 does not call git.",
            "PR205 does not run processes.",
            "Source apply gate satisfaction is not source apply.",
            "SourceApplyRequest is not source apply.",
            "Source apply dry-run is not source apply.",
            "SourceApplyDryRunReceipt is not source apply.",
            "SourceApplyDryRunReceipt read API is not source apply.",
            "Controlled source apply is source mutation.",
            "SourceApplyReceipt is mutation evidence.",
            "SourceApplyReceipt is not workflow continuation.",
            "SourceApplyReceipt is not release readiness.",
            "SourceApplyReceipt is not rollback execution.",
            "SourceApplyReceipt is not git commit, push, merge, branch creation, or PR creation.",
            "PR205 locks the launch cage. It does not launch anything new."
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    private static void AssertRejectedBeforeMutation(ControlledSourceApplyResult result, RecordingStore store)
    {
        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.MutationOccurred);
        Assert.IsNull(result.Receipt);
        Assert.AreEqual(0, store.Saved.Count);
    }

    private static void AssertReceiptDoesNotGrantAuthority(SourceApplyReceipt receipt)
    {
        var text = JsonSerializer.Serialize(receipt);
        foreach (var token in new[]
        {
            "WorkflowCanContinue",
            "ReleaseReady",
            "ReleaseApproved",
            "RollbackExecuted",
            "GitCommitted",
            "GitPushed",
            "Merged",
            "PullRequestCreated",
            "MemoryPromoted",
            "RetrievalActivated"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Receipt contained authority token: {token}");
        }
    }

    private static string Workspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "irondev-pr205-source-apply-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
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
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static string[] SourceApplyProductionFiles(string root) =>
    [
        Path.Combine(root, "IronDev.Core", "Governance", "SourceApplyGateEvaluator.cs"),
        Path.Combine(root, "IronDev.Core", "Governance", "SourceApplyRequestValidation.cs"),
        Path.Combine(root, "IronDev.Core", "Governance", "SourceApplyDryRunExecutor.cs"),
        Path.Combine(root, "IronDev.Core", "Governance", "SourceApplyDryRunReceipt.cs"),
        Path.Combine(root, "IronDev.Core", "Governance", "SourceApplyReceipt.cs"),
        Path.Combine(root, "IronDev.Infrastructure", "Governance", "SourceApplyDryRunReceiptQueryService.cs"),
        Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlSourceApplyReceiptStore.cs"),
        Path.Combine(root, "IronDev.Infrastructure", "Governance", "ControlledSourceApplyExecutor.cs"),
        Path.Combine(root, "IronDev.Api", "Controllers", "SourceApplyDryRunReceiptsV1Controller.cs")
    ];

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

        private Fixture(ControlledSourceApplyRequest request, IReadOnlyList<Op> ops)
        {
            Request = request;
            _ops = ops;
        }

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
            var changes = ops.Where(op => op.Kind != SourceApplyRequestFileOperationKinds.Noop).Select(Change).ToArray();
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
                FileResults = fileOps.Select(op => new SourceApplyDryRunReceiptFileResult
                {
                    Path = op.Path,
                    PreviousPath = op.PreviousPath,
                    OperationKind = op.OperationKind,
                    PatchArtifactChangeHash = op.PatchArtifactChangeHash,
                    OperationHash = op.OperationHash,
                    ExpectedBeforeContentHash = op.BeforeContentHash,
                    ExpectedAfterContentHash = op.AfterContentHash,
                    ObservedCurrentContentHash = op.BeforeContentHash,
                    PreconditionsSatisfied = true,
                    WouldCreate = op.OperationKind == SourceApplyRequestFileOperationKinds.CreateFile,
                    WouldModify = op.OperationKind == SourceApplyRequestFileOperationKinds.ModifyFile,
                    WouldDelete = op.OperationKind == SourceApplyRequestFileOperationKinds.DeleteFile,
                    WouldRename = op.OperationKind == SourceApplyRequestFileOperationKinds.RenameFile,
                    WouldNoop = op.OperationKind == SourceApplyRequestFileOperationKinds.Noop,
                    IssueCodes = [],
                    FileResultHash = H("file-result:" + op.Path)
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
                    .Where(op => op.Kind is SourceApplyRequestFileOperationKinds.CreateFile or SourceApplyRequestFileOperationKinds.ModifyFile)
                    .Select(op => new ControlledSourceApplyContent { Path = op.Path, AfterContentHash = H(op.After ?? string.Empty), Content = op.After ?? string.Empty })
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
                if (content is null)
                {
                    continue;
                }

                var fullPath = Path.Combine(Request.WorkspaceRoot, path);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, content, Encoding.UTF8);
            }
        }

        public ControlledSourceApplyRequest WithFileOperation(SourceApplyRequestFileOperation operation) =>
            Request with { SourceApplyRequest = Request.SourceApplyRequest with { FileOperations = [operation] } };

        public ControlledSourceApplyRequest Mutate(string mutation)
        {
            var request = Request.SourceApplyRequest;
            return mutation switch
            {
                "MissingAcceptedApproval" => Request with { SourceApplyRequest = request with { AcceptedApprovalId = Guid.Empty } },
                "MissingPolicySatisfaction" => Request with { SourceApplyRequest = request with { PolicySatisfactionId = Guid.Empty } },
                "GateUnsatisfied" => Request with { SourceApplyRequest = request with { SourceApplyGateSatisfied = false, SourceApplyGateEvaluation = request.SourceApplyGateEvaluation! with { Satisfied = false } } },
                "DryRunUnsatisfied" => Request with { SourceApplyDryRunReceipt = Request.SourceApplyDryRunReceipt with { DryRunSatisfied = false } },
                "SourceApplyRequestHashMismatch" => Request with { SourceApplyDryRunReceipt = Request.SourceApplyDryRunReceipt with { SourceApplyRequestHash = H("wrong-request") } },
                "SourceApplyGateHashMismatch" => Request with { SourceApplyDryRunReceipt = Request.SourceApplyDryRunReceipt with { SourceApplyGateEvaluationHash = H("wrong-gate") } },
                "PatchArtifactIdMismatch" => Request with { PatchArtifact = Request.PatchArtifact with { PatchArtifactId = Guid.NewGuid() } },
                "PatchHashMismatch" => Request with { PatchArtifact = Request.PatchArtifact with { PatchHash = H("wrong-patch") } },
                "ChangeSetMismatch" => Request with { PatchArtifact = Request.PatchArtifact with { ChangeSetHash = H("wrong-change-set") } },
                "RollbackReceiptIdMismatch" => Request with { RollbackSupportReceipt = Request.RollbackSupportReceipt with { RollbackSupportReceiptId = Guid.NewGuid() } },
                "RollbackReceiptHashMismatch" => Request with { RollbackSupportReceipt = Request.RollbackSupportReceipt with { RollbackSupportReceiptHash = H("wrong-rollback") } },
                "RollbackPatchMismatch" => Request with { RollbackSupportReceipt = Request.RollbackSupportReceipt with { PatchHash = H("wrong-rollback-patch") } },
                "RollbackExpired" => Request with { RollbackSupportReceipt = Request.RollbackSupportReceipt with { ExpiresAtUtc = Request.RequestedAtUtc.AddSeconds(-1) } },
                "DryRunExpired" => Request with { SourceApplyDryRunReceipt = Request.SourceApplyDryRunReceipt with { ExpiresAtUtc = Request.RequestedAtUtc.AddSeconds(-1) } },
                "PatchExpired" => Request with { PatchArtifact = Request.PatchArtifact with { ExpiresAtUtc = Request.RequestedAtUtc.AddSeconds(-1) } },
                "SourceBaselineMismatch" => Request with { ObservedSourceBaselineHash = H("wrong-baseline") },
                "WorkspaceBoundaryMismatch" => Request with { ApprovedWorkspaceBoundaryHash = H("wrong-workspace") },
                "ExpectedBranchMismatch" => Request with { ObservedBranch = "feature/not-main" },
                "CleanWorktreeHashMismatch" => Request with { ObservedCleanWorktreeHashBeforeApply = H("dirty-worktree") },
                "DryRunFileMissing" => Request with { SourceApplyDryRunReceipt = Request.SourceApplyDryRunReceipt with { FileResults = [] } },
                "DryRunPreconditionsUnsatisfied" => Request with
                {
                    SourceApplyDryRunReceipt = Request.SourceApplyDryRunReceipt with
                    {
                        FileResults =
                        [
                            Request.SourceApplyDryRunReceipt.FileResults[0] with
                            {
                                PreconditionsSatisfied = false,
                                IssueCodes = ["BeforeHashMismatch"]
                            }
                        ]
                    }
                },
                "MissingPatchArtifactFileChange" => Request with { PatchArtifact = Request.PatchArtifact with { FileChanges = [] } },
                _ => Request
            };
        }

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

    private sealed class RecordingStore : ISourceApplyReceiptStore
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

[TestClass]
[TestCategory("SourceApplyRegression")]
[TestCategory("SourceApplyReceiptStore")]
[TestCategory("PR205")]
public sealed class SourceApplyRegressionReceiptSqlTests : IntegrationTestBase
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
    public async Task SourceApplyRegression_SourceApplyReceiptStoreCanSaveReadAndListOnly()
    {
        var receipt = ValidReceipt();

        await _store.SaveAsync(receipt);

        AssertReceipt(receipt, await _store.GetAsync(receipt.ProjectId, receipt.SourceApplyReceiptId));
        AssertReceipt(receipt, await _store.GetByReceiptHashAsync(receipt.ProjectId, receipt.SourceApplyReceiptHash));
        AssertSingle(receipt, await _store.ListBySourceApplyRequestAsync(receipt.ProjectId, receipt.SourceApplyRequestId));
        AssertSingle(receipt, await _store.ListBySourceApplyDryRunReceiptAsync(receipt.ProjectId, receipt.SourceApplyDryRunReceiptId));
        AssertSingle(receipt, await _store.ListByPatchArtifactAsync(receipt.ProjectId, receipt.PatchArtifactId));
        AssertSingle(receipt, await _store.ListByRollbackSupportReceiptAsync(receipt.ProjectId, receipt.RollbackSupportReceiptId));

        var methodNames = typeof(ISourceApplyReceiptStore).GetMethods().Select(method => method.Name).Order(StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(
            new[]
            {
                "GetAsync",
                "GetByReceiptHashAsync",
                "ListByPatchArtifactAsync",
                "ListByRollbackSupportReceiptAsync",
                "ListBySourceApplyDryRunReceiptAsync",
                "ListBySourceApplyRequestAsync",
                "SaveAsync"
            },
            methodNames);
        Assert.IsFalse(methodNames.Any(name => name.Contains("Update", StringComparison.OrdinalIgnoreCase) || name.Contains("Delete", StringComparison.OrdinalIgnoreCase) || name.Contains("Continue", StringComparison.OrdinalIgnoreCase) || name.Contains("Release", StringComparison.OrdinalIgnoreCase) || name.Contains("Rollback", StringComparison.OrdinalIgnoreCase) && !name.Contains("RollbackSupportReceipt", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task SourceApplyRegression_SourceApplyReceiptStoreIdempotentSameHashAndRejectsDifferentHash()
    {
        var receipt = ValidReceipt();

        await _store.SaveAsync(receipt);
        await _store.SaveAsync(receipt);

        await ExpectSqlExceptionAsync(() => _store.SaveAsync(receipt with { SourceApplyReceiptHash = H("changed-receipt-hash") }));
    }

    [TestMethod]
    public async Task SourceApplyRegression_SourceApplyReceiptSqlBoundaryRejectsDirectUnsafeTextAndAuthorityClaims()
    {
        await ExpectSqlExceptionAsync(() => InsertReceiptDirectAsync(ValidReceipt() with { EvidenceReferences = ["rawPrompt leaked"] }));
        await ExpectSqlExceptionAsync(() => InsertReceiptDirectAsync(ValidReceipt() with { BoundaryMaxims = ["release approved"] }));
    }

    [TestMethod]
    public async Task SourceApplyRegression_SourceApplyReceiptSqlBoundaryBlocksUpdateAndDelete()
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

    private async Task InsertReceiptDirectAsync(SourceApplyReceipt receipt)
    {
        const string sql = """
            INSERT INTO governance.SourceApplyReceipt
            (
                SourceApplyReceiptId, ProjectId, ControlledSourceApplyRequestId, SourceApplyRequestId, SourceApplyRequestHash,
                SourceApplyDryRunReceiptId, SourceApplyDryRunReceiptHash, SourceApplyGateEvaluationId, SourceApplyGateEvaluationHash,
                PatchArtifactId, PatchHash, ChangeSetHash, RollbackSupportReceiptId, RollbackSupportReceiptHash,
                SourceBaselineHash, WorkspaceBoundaryHash, ExpectedBranch, ExpectedCleanWorktreeHash, ObservedBranch,
                ObservedCleanWorktreeHashBeforeApply, ObservedCleanWorktreeHashAfterApply, MutationOccurred, ApplySucceeded,
                PartialApplyOccurred, FileResultsJson, IssueCodesJson, AppliedAtUtc, SourceApplyReceiptHash,
                EvidenceReferencesJson, BoundaryMaximsJson, BoundaryText
            )
            VALUES
            (
                @SourceApplyReceiptId, @ProjectId, @ControlledSourceApplyRequestId, @SourceApplyRequestId, @SourceApplyRequestHash,
                @SourceApplyDryRunReceiptId, @SourceApplyDryRunReceiptHash, @SourceApplyGateEvaluationId, @SourceApplyGateEvaluationHash,
                @PatchArtifactId, @PatchHash, @ChangeSetHash, @RollbackSupportReceiptId, @RollbackSupportReceiptHash,
                @SourceBaselineHash, @WorkspaceBoundaryHash, @ExpectedBranch, @ExpectedCleanWorktreeHash, @ObservedBranch,
                @ObservedCleanWorktreeHashBeforeApply, @ObservedCleanWorktreeHashAfterApply, @MutationOccurred, @ApplySucceeded,
                @PartialApplyOccurred, @FileResultsJson, @IssueCodesJson, @AppliedAtUtc, @SourceApplyReceiptHash,
                @EvidenceReferencesJson, @BoundaryMaximsJson, @BoundaryText
            );
            """;
        await ExecuteSqlAsync(sql, new
        {
            receipt.SourceApplyReceiptId,
            receipt.ProjectId,
            receipt.ControlledSourceApplyRequestId,
            receipt.SourceApplyRequestId,
            receipt.SourceApplyRequestHash,
            receipt.SourceApplyDryRunReceiptId,
            receipt.SourceApplyDryRunReceiptHash,
            receipt.SourceApplyGateEvaluationId,
            receipt.SourceApplyGateEvaluationHash,
            receipt.PatchArtifactId,
            receipt.PatchHash,
            receipt.ChangeSetHash,
            receipt.RollbackSupportReceiptId,
            receipt.RollbackSupportReceiptHash,
            receipt.SourceBaselineHash,
            receipt.WorkspaceBoundaryHash,
            receipt.ExpectedBranch,
            receipt.ExpectedCleanWorktreeHash,
            receipt.ObservedBranch,
            receipt.ObservedCleanWorktreeHashBeforeApply,
            receipt.ObservedCleanWorktreeHashAfterApply,
            receipt.MutationOccurred,
            receipt.ApplySucceeded,
            receipt.PartialApplyOccurred,
            FileResultsJson = JsonSerializer.Serialize(receipt.FileResults, JsonOptions),
            IssueCodesJson = JsonSerializer.Serialize(receipt.IssueCodes, JsonOptions),
            receipt.AppliedAtUtc,
            receipt.SourceApplyReceiptHash,
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
            if (!string.IsNullOrWhiteSpace(batch))
            {
                await connection.ExecuteAsync(new CommandDefinition(batch, commandType: CommandType.Text));
            }
        }
    }

    private async Task ExecuteSqlAsync(string sql, object parameters)
    {
        using var connection = ServiceProvider.GetRequiredService<IDbConnectionFactory>().CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, commandType: CommandType.Text));
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
        const string sql = """
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

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
