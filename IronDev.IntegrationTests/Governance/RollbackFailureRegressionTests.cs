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
[TestCategory("RollbackFailureRegression")]
[TestCategory("RollbackExecutionReceiptStore")]
[TestCategory("ControlledRollbackExecutor")]
[TestCategory("PR209")]
public sealed class RollbackFailureRegressionTests : IntegrationTestBase
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
    public async Task RollbackFailureRegression_SuccessfulRollbackPersistsDurableReceiptThroughSqlStore()
    {
        var workspace = Workspace();
        var fixture = RollbackFixture.Create(workspace,
        [
            Op.Modify("src/modified.txt", "pr209-before-modified", "pr209-after-modified"),
            Op.Create("src/created.txt", "pr209-created-content"),
            Op.Delete("src/deleted.txt", "pr209-deleted-content"),
            Op.Rename("src/old-name.txt", "src/new-name.txt", "pr209-rename-content"),
            Op.Noop("src/noop.txt", "pr209-noop-content")
        ]);
        fixture.WriteAppliedState();

        var result = await new ControlledRollbackExecutor(_store).RollbackAsync(fixture.Request);

        Assert.IsTrue(result.Succeeded, IssueText(result));
        Assert.IsTrue(result.MutationOccurred);
        Assert.IsFalse(result.PartialRollbackOccurred);
        Assert.IsNotNull(result.Receipt);
        Assert.AreEqual("pr209-before-modified", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "modified.txt"), Encoding.UTF8));
        Assert.IsFalse(File.Exists(Path.Combine(workspace, "src", "created.txt")));
        Assert.AreEqual("pr209-deleted-content", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "deleted.txt"), Encoding.UTF8));
        Assert.IsFalse(File.Exists(Path.Combine(workspace, "src", "new-name.txt")));
        Assert.AreEqual("pr209-rename-content", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "old-name.txt"), Encoding.UTF8));
        Assert.AreEqual("pr209-noop-content", await File.ReadAllTextAsync(Path.Combine(workspace, "src", "noop.txt"), Encoding.UTF8));

        var saved = await _store.GetAsync(result.Receipt!.ProjectId, result.Receipt.RollbackExecutionReceiptId);
        AssertReceiptRoundTrips(result.Receipt, saved);
        AssertReceiptRoundTrips(result.Receipt, await _store.GetByReceiptHashAsync(result.Receipt.ProjectId, result.Receipt.RollbackExecutionReceiptHash));
        AssertSingle(result.Receipt, await _store.ListBySourceApplyReceiptAsync(result.Receipt.ProjectId, result.Receipt.SourceApplyReceiptId));
        AssertSingle(result.Receipt, await _store.ListByRollbackPlanAsync(result.Receipt.ProjectId, result.Receipt.RollbackPlanId));
        AssertSingle(result.Receipt, await _store.ListByRollbackSupportReceiptAsync(result.Receipt.ProjectId, result.Receipt.RollbackSupportReceiptId));
        AssertSingle(result.Receipt, await _store.ListByPatchArtifactAsync(result.Receipt.ProjectId, result.Receipt.PatchArtifactId));

        Assert.IsTrue(saved!.FileResults.Any(file => file.Restored));
        Assert.IsTrue(saved.FileResults.Any(file => file.Deleted));
        Assert.IsTrue(saved.FileResults.Any(file => file.Recreated));
        Assert.IsTrue(saved.FileResults.Any(file => file.RenamedBack));
        Assert.IsTrue(saved.FileResults.Any(file => file.Noop));
        Assert.AreEqual(result.Receipt.RollbackExecutionReceiptHash, saved.RollbackExecutionReceiptHash);
        CollectionAssert.AreEquivalent(result.Receipt.EvidenceReferences.ToArray(), saved.EvidenceReferences.ToArray());
        CollectionAssert.AreEquivalent(result.Receipt.BoundaryMaxims.ToArray(), saved.BoundaryMaxims.ToArray());
        Assert.AreEqual(result.Receipt.Boundary, saved.Boundary);
    }

    [TestMethod]
    public async Task RollbackFailureRegression_RejectedPreflightDoesNotPersistReceipt()
    {
        var workspace = Workspace();
        var fixture = RollbackFixture.Create(workspace, [Op.Modify("src/file.txt", "pr209-before", "pr209-after")]);
        fixture.WriteAppliedState();
        var before = await File.ReadAllTextAsync(Path.Combine(workspace, "src", "file.txt"), Encoding.UTF8);
        var bad = fixture.Request with { ObservedCleanWorktreeHashBeforeRollback = H("wrong-clean-worktree") };

        var result = await new ControlledRollbackExecutor(_store).RollbackAsync(bad);

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.MutationOccurred);
        Assert.IsNull(result.Receipt);
        Assert.AreEqual(before, await File.ReadAllTextAsync(Path.Combine(workspace, "src", "file.txt"), Encoding.UTF8));
        Assert.AreEqual(0, (await _store.ListBySourceApplyReceiptAsync(fixture.Request.ProjectId, fixture.Request.SourceApplyReceipt.SourceApplyReceiptId)).Count);
        Assert.AreEqual(0, (await _store.ListByRollbackPlanAsync(fixture.Request.ProjectId, fixture.Request.RollbackPlan.RollbackPlanId)).Count);
        Assert.AreEqual(0, (await _store.ListByRollbackSupportReceiptAsync(fixture.Request.ProjectId, fixture.Request.RollbackSupportReceipt.RollbackSupportReceiptId)).Count);
        Assert.AreEqual(0, (await _store.ListByPatchArtifactAsync(fixture.Request.ProjectId, fixture.Request.PatchArtifact.PatchArtifactId)).Count);
    }

    [TestMethod]
    public async Task RollbackFailureRegression_PartialRollbackPersistsPartialReceiptWithFullPlan()
    {
        var workspace = Workspace();
        var fixture = RollbackFixture.Create(workspace,
        [
            Op.Modify("src/a-first.txt", "pr209-one-before", "pr209-one-after"),
            Op.Modify("src/m-second.txt", "pr209-two-before", "pr209-two-after"),
            Op.Modify("src/z-third.txt", "pr209-three-before", "pr209-three-after")
        ]);
        fixture.WriteAppliedState();
        var blocked = Path.Combine(workspace, "src", "m-second.txt");
        File.SetAttributes(blocked, FileAttributes.ReadOnly);

        ControlledRollbackExecutionResult result;
        try
        {
            result = await new ControlledRollbackExecutor(_store).RollbackAsync(fixture.Request);
        }
        finally
        {
            File.SetAttributes(blocked, FileAttributes.Normal);
        }

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.MutationOccurred);
        Assert.IsTrue(result.PartialRollbackOccurred);
        Assert.IsNotNull(result.Receipt);

        var saved = await _store.GetAsync(result.Receipt!.ProjectId, result.Receipt.RollbackExecutionReceiptId);
        Assert.IsNotNull(saved);
        Assert.IsFalse(saved!.RollbackSucceeded);
        Assert.IsTrue(saved.MutationOccurred);
        Assert.IsTrue(saved.PartialRollbackOccurred);
        Assert.AreEqual(3, saved.FileResults.Count);
        Assert.IsTrue(saved.FileResults.Any(file => file.Path == "src/a-first.txt" && file.MutationApplied && file.Restored));
        Assert.IsTrue(saved.FileResults.Any(file => file.Path == "src/m-second.txt" && !file.MutationApplied && file.IssueCodes.Contains("RollbackFailed")));
        Assert.IsTrue(saved.FileResults.Any(file => file.Path == "src/z-third.txt" && !file.MutationApplied));
        Assert.IsFalse(Serialized(saved).Contains("WorkflowCanContinue", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(Serialized(saved).Contains("ReleaseReady", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(Serialized(saved).Contains("GitCommitted", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(Serialized(saved).Contains("RollbackCleanupComplete", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task RollbackFailureRegression_PersistedReceiptContainsHashesNotRollbackContent()
    {
        var workspace = Workspace();
        var fixture = RollbackFixture.Create(workspace, [Op.Modify("src/content.txt", "pr209-original-content-alpha", "pr209-applied-content-beta")]);
        fixture.WriteAppliedState();

        var result = await new ControlledRollbackExecutor(_store).RollbackAsync(fixture.Request);
        var saved = await _store.GetAsync(result.Receipt!.ProjectId, result.Receipt.RollbackExecutionReceiptId);
        var serialized = Serialized(saved!);

        Assert.IsFalse(serialized.Contains("pr209-original-content-alpha", StringComparison.Ordinal));
        Assert.IsFalse(serialized.Contains("pr209-applied-content-beta", StringComparison.Ordinal));
        foreach (var token in new[] { "raw prompt", "raw completion", "raw tool output", "chain-of-thought", "private reasoning", "system prompt", "developer prompt", "password", "api_key", "secret", "bearer" })
        {
            Assert.IsFalse(serialized.Contains(token, StringComparison.OrdinalIgnoreCase), $"Persisted receipt leaked forbidden token: {token}");
        }

        Assert.IsTrue(saved!.FileResults.All(file => file.FileResultHash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(saved.RollbackExecutionReceiptHash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task RollbackFailureRegression_StoreRemainsAppendOnlyAndRejectsAuthorityClaims()
    {
        var workspace = Workspace();
        var fixture = RollbackFixture.Create(workspace, [Op.Modify("src/file.txt", "pr209-before", "pr209-after")]);
        fixture.WriteAppliedState();
        var result = await new ControlledRollbackExecutor(_store).RollbackAsync(fixture.Request);
        var receipt = result.Receipt!;

        await _store.SaveAsync(receipt);
        await ExpectSqlExceptionAsync(() => _store.SaveAsync(receipt with { RollbackExecutionReceiptHash = H("different-receipt-hash") }));
        await ExpectSqlExceptionAsync(() => ExecuteSqlAsync("UPDATE governance.RollbackExecutionReceipt SET ObservedBranch = @ObservedBranch WHERE RollbackExecutionReceiptId = @RollbackExecutionReceiptId", new { ObservedBranch = "other", receipt.RollbackExecutionReceiptId }));
        await ExpectSqlExceptionAsync(() => ExecuteSqlAsync("DELETE FROM governance.RollbackExecutionReceipt WHERE RollbackExecutionReceiptId = @RollbackExecutionReceiptId", new { receipt.RollbackExecutionReceiptId }));
        await ExpectSqlExceptionAsync(() => InsertReceiptDirectAsync(ValidDirectReceipt() with { EvidenceReferences = ["rawPrompt leaked"] }));
        await ExpectSqlExceptionAsync(() => InsertReceiptDirectAsync(ValidDirectReceipt() with { BoundaryMaxims = ["release approved"] }));
    }

    [TestMethod]
    public void RollbackFailureRegression_DoesNotAddApiCliUiRuntimeWorkflowReleaseGitAgentsMemoryRetrieval()
    {
        var root = RepositoryRoot();
        var scannedRoots = new[]
        {
            Path.Combine(root, "IronDev.Api"),
            Path.Combine(root, "tools", "IronDev.Cli"),
            Path.Combine(root, "IronDev.TauriShell", "src"),
            Path.Combine(root, "IronDev.Infrastructure"),
            Path.Combine(root, "IronDev.Core")
        };

        var hits = scannedRoots
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories))
            .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains(Path.Combine("bin"), StringComparison.OrdinalIgnoreCase) && !file.Contains(Path.Combine("obj"), StringComparison.OrdinalIgnoreCase))
            .Where(file => File.ReadAllText(file).Contains("RollbackFailureRegression", StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(0, hits.Length, "PR209 integration marker must stay out of production/API/CLI/UI/runtime files: " + string.Join(", ", hits));
    }

    [TestMethod]
    public void RollbackFailureRegression_ReceiptDocumentsBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR209_ROLLBACK_FAILURE_REGRESSION_TESTS.md"));

        StringAssert.Contains(receipt, "PR209 adds rollback failure regression tests only.");
        StringAssert.Contains(receipt, "PR209 does not add rollback behaviour.");
        StringAssert.Contains(receipt, "PR209 does not add rollback action kinds.");
        StringAssert.Contains(receipt, "PR209 does not add source mutation capability.");
        StringAssert.Contains(receipt, "PR209 does not add SQL.");
        StringAssert.Contains(receipt, "PR209 does not add API.");
        StringAssert.Contains(receipt, "PR209 does not add CLI.");
        StringAssert.Contains(receipt, "PR209 does not add UI.");
        StringAssert.Contains(receipt, "PR209 does not add runtime execution.");
        StringAssert.Contains(receipt, "PR209 does not continue workflow.");
        StringAssert.Contains(receipt, "PR209 does not approve release.");
        StringAssert.Contains(receipt, "PR209 does not infer release readiness.");
        StringAssert.Contains(receipt, "PR209 does not declare rollback cleanup.");
        StringAssert.Contains(receipt, "Rollback failure evidence is not rollback success.");
        StringAssert.Contains(receipt, "EvidenceConsistent is not RollbackSucceeded.");
        StringAssert.Contains(receipt, "RollbackSucceeded is not ReleaseReady.");
        StringAssert.Contains(receipt, "Human review remains required.");
    }

    [TestMethod]
    public async Task RollbackFailureRegression_PreflightMismatchWritesNoReceiptAndDoesNotMutate()
    {
        var cases = new (string Name, Func<ControlledRollbackExecutionRequest, ControlledRollbackExecutionRequest> Mutate)[]
        {
            ("rollback plan invalid", request => request with { RollbackPlan = request.RollbackPlan with { RollbackPlanHash = "not-a-sha256-hash" } }),
            ("rollback support receipt mismatch", request => request with { RollbackSupportReceipt = request.RollbackSupportReceipt with { RollbackPlanHash = H("wrong-rollback-plan") } }),
            ("rollback support receipt expired", request => request with { RollbackSupportReceipt = request.RollbackSupportReceipt with { ExpiresAtUtc = request.RequestedAtUtc.AddMinutes(-1) } }),
            ("source apply receipt mismatch", request => request with { SourceApplyReceipt = request.SourceApplyReceipt with { PatchHash = H("wrong-patch") } }),
            ("source apply receipt no mutation", request => request with { SourceApplyReceipt = request.SourceApplyReceipt with { MutationOccurred = false } }),
            ("source apply receipt failed without partial", request => request with { SourceApplyReceipt = request.SourceApplyReceipt with { ApplySucceeded = false, PartialApplyOccurred = false, IssueCodes = ["ApplyFailed"] } }),
            ("source apply request mismatch", request => request with { SourceApplyRequest = request.SourceApplyRequest with { PatchHash = H("wrong-patch") } }),
            ("patch artifact mismatch", request => request with { PatchArtifact = request.PatchArtifact with { PatchHash = H("wrong-patch") } }),
            ("patch artifact expired", request => request with { PatchArtifact = request.PatchArtifact with { ExpiresAtUtc = request.RequestedAtUtc.AddMinutes(-1) } }),
            ("source baseline mismatch", request => request with { ObservedSourceBaselineHash = H("wrong-baseline") }),
            ("workspace boundary mismatch", request => request with { ApprovedWorkspaceBoundaryHash = H("wrong-workspace") }),
            ("expected branch mismatch", request => request with { SourceApplyRequest = request.SourceApplyRequest with { ExpectedBranch = "release" } }),
            ("observed branch mismatch", request => request with { ObservedBranch = "release" }),
            ("clean worktree before rollback mismatch", request => request with { ObservedCleanWorktreeHashBeforeRollback = H("wrong-clean-before-rollback") }),
            ("path escape", request => request with { RollbackPlan = request.RollbackPlan with { FileActions = [request.RollbackPlan.FileActions[0] with { Path = "../escape.txt", RollbackActionHash = H("escape-action") }] } }),
            ("git path", request => request with { RollbackPlan = request.RollbackPlan with { FileActions = [request.RollbackPlan.FileActions[0] with { Path = ".git/config", RollbackActionHash = H("git-action") }] } }),
            ("rooted path", request => request with { RollbackPlan = request.RollbackPlan with { FileActions = [request.RollbackPlan.FileActions[0] with { Path = Path.GetFullPath(Path.Combine(request.WorkspaceRoot, "rooted.txt")), RollbackActionHash = H("rooted-action") }] } }),
            ("backslash path", request => request with { RollbackPlan = request.RollbackPlan with { FileActions = [request.RollbackPlan.FileActions[0] with { Path = "src\\backslash.txt", RollbackActionHash = H("backslash-action") }] } }),
            ("control character path", request => request with { RollbackPlan = request.RollbackPlan with { FileActions = [request.RollbackPlan.FileActions[0] with { Path = "src/bad\u0001path.txt", RollbackActionHash = H("control-action") }] } }),
            ("approved rollback content hash mismatch", request => request with { ApprovedContents = [request.ApprovedContents[0] with { ContentHash = H("wrong-approved-content") }] }),
            ("missing approved rollback content", request => request with { ApprovedContents = [] }),
            ("missing patch artifact file change", request => request with { PatchArtifact = request.PatchArtifact with { FileChanges = [] } }),
            ("missing source apply file result", request => request with { SourceApplyReceipt = request.SourceApplyReceipt with { FileResults = [] } }),
            ("unsupported rollback action kind", request => request with { RollbackPlan = request.RollbackPlan with { FileActions = [request.RollbackPlan.FileActions[0] with { PlannedActionKind = "TeleportFile", RollbackActionHash = H("unsupported-action") }] } }),
            ("duplicate rollback action", request => request with { RollbackPlan = request.RollbackPlan with { FileActions = [request.RollbackPlan.FileActions[0], request.RollbackPlan.FileActions[0]] } })
        };

        foreach (var item in cases)
        {
            var workspace = Workspace();
            var fixture = RollbackFixture.Create(workspace, [Op.Modify("src/file.txt", "pr209-before", "pr209-after")]);
            fixture.WriteAppliedState();
            var file = Path.Combine(workspace, "src", "file.txt");
            var before = await File.ReadAllTextAsync(file, Encoding.UTF8);

            var result = await new ControlledRollbackExecutor(_store).RollbackAsync(item.Mutate(fixture.Request));

            AssertPreflightRejected(result, item.Name);
            Assert.AreEqual(before, await File.ReadAllTextAsync(file, Encoding.UTF8), item.Name);
            Assert.AreEqual(0, (await _store.ListBySourceApplyReceiptAsync(fixture.Request.ProjectId, fixture.Request.SourceApplyReceipt.SourceApplyReceiptId)).Count, item.Name);
        }
    }

    [TestMethod]
    public async Task RollbackFailureRegression_CurrentHashMismatchWritesNoReceiptAndDoesNotMutate()
    {
        var workspace = Workspace();
        var fixture = RollbackFixture.Create(workspace, [Op.Modify("src/file.txt", "pr209-before", "pr209-after")]);
        fixture.WriteAppliedState();
        var file = Path.Combine(workspace, "src", "file.txt");
        await File.WriteAllTextAsync(file, "pr209-unexpected-current", Encoding.UTF8);

        var result = await new ControlledRollbackExecutor(_store).RollbackAsync(fixture.Request);

        AssertPreflightRejected(result, "current hash mismatch");
        Assert.AreEqual("pr209-unexpected-current", await File.ReadAllTextAsync(file, Encoding.UTF8));
        Assert.AreEqual(0, (await _store.ListBySourceApplyReceiptAsync(fixture.Request.ProjectId, fixture.Request.SourceApplyReceipt.SourceApplyReceiptId)).Count);
    }

    [TestMethod]
    public async Task RollbackFailureRegression_PartialRollbackReceiptDoesNotGrantWorkflowReleaseGitOrCleanupAuthority()
    {
        var workspace = Workspace();
        var fixture = RollbackFixture.Create(workspace,
        [
            Op.Modify("src/a-first.txt", "pr209-one-before", "pr209-one-after"),
            Op.Modify("src/m-second.txt", "pr209-two-before", "pr209-two-after"),
            Op.Modify("src/z-third.txt", "pr209-three-before", "pr209-three-after")
        ]);
        fixture.WriteAppliedState();
        var blocked = Path.Combine(workspace, "src", "m-second.txt");
        File.SetAttributes(blocked, FileAttributes.ReadOnly);

        ControlledRollbackExecutionResult result;
        try
        {
            result = await new ControlledRollbackExecutor(_store).RollbackAsync(fixture.Request);
        }
        finally
        {
            File.SetAttributes(blocked, FileAttributes.Normal);
        }

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.MutationOccurred);
        Assert.IsTrue(result.PartialRollbackOccurred);
        Assert.IsNotNull(result.Receipt);
        Assert.AreEqual(3, result.Receipt!.FileResults.Count);
        Assert.IsTrue(result.Receipt.FileResults.Any(file => file.MutationApplied));
        Assert.IsTrue(result.Receipt.FileResults.Any(file => file.IssueCodes.Contains("RollbackFailed")));
        Assert.IsTrue(result.Receipt.FileResults.Any(file => !file.MutationApplied));

        var saved = await _store.GetAsync(result.Receipt.ProjectId, result.Receipt.RollbackExecutionReceiptId);
        var serialized = Serialized(saved!);
        foreach (var token in new[] { "WorkflowCanContinue", "SafeToContinue", "ReleaseReady", "ReleaseApproved", "RollbackCleanedUp", "CrashCleanedUp", "RollbackFixed", "SourceSafe", "CanApplyAgain", "AutoContinue", "AutoRelease", "git commit", "git push", "pull request created" })
        {
            Assert.IsFalse(serialized.Contains(token, StringComparison.OrdinalIgnoreCase), token);
        }
        StringAssert.Contains(saved!.Boundary, "does not declare the crash cleaned up");
    }

    [TestMethod]
    public async Task RollbackFailureRegression_AuditHonestFailureDoesNotInferWorkflowOrReleaseReadiness()
    {
        var workspace = Workspace();
        var fixture = RollbackFixture.Create(workspace,
        [
            Op.Modify("src/a-first.txt", "pr209-one-before", "pr209-one-after"),
            Op.Modify("src/m-second.txt", "pr209-two-before", "pr209-two-after")
        ]);
        fixture.WriteAppliedState();
        var blocked = Path.Combine(workspace, "src", "m-second.txt");
        File.SetAttributes(blocked, FileAttributes.ReadOnly);

        ControlledRollbackExecutionResult result;
        try
        {
            result = await new ControlledRollbackExecutor(_store).RollbackAsync(fixture.Request);
        }
        finally
        {
            File.SetAttributes(blocked, FileAttributes.Normal);
        }

        var report = new RollbackExecutionAuditor().Audit(BuildAuditRequest(fixture.Request, result.Receipt!));

        Assert.IsFalse(report.RollbackSucceeded);
        Assert.IsTrue(report.MutationOccurred);
        Assert.IsTrue(report.PartialRollbackOccurred);
        Assert.IsFalse(report.WorkflowBoundaryAllowsContinuation);
        Assert.IsFalse(report.ReleaseBoundaryInfersReadiness);
        Assert.IsTrue(report.HumanReviewRequired);
    }

    [TestMethod]
    public async Task RollbackFailureRegression_AuditDetectsDishonestFailureTruthTable()
    {
        var workspace = Workspace();
        var fixture = RollbackFixture.Create(workspace, [Op.Modify("src/file.txt", "pr209-before", "pr209-after")]);
        fixture.WriteAppliedState();
        var result = await new ControlledRollbackExecutor(_store).RollbackAsync(fixture.Request);
        var receipt = result.Receipt!;

        var badReceipts = new[]
        {
            receipt with { RollbackSucceeded = true, MutationOccurred = false, IssueCodes = ["ImpossibleSuccess"] },
            receipt with { RollbackSucceeded = true, PartialRollbackOccurred = true, IssueCodes = ["ImpossiblePartialSuccess"] },
            receipt with { MutationOccurred = false, PartialRollbackOccurred = true, IssueCodes = ["ImpossiblePartialNoMutation"] },
            receipt with { PartialRollbackOccurred = true, RollbackSucceeded = false, FileResults = [receipt.FileResults[0]], IssueCodes = ["PartialRollback"] }
        };

        foreach (var bad in badReceipts.Select(RehashReceipt))
        {
            var report = new RollbackExecutionAuditor().Audit(BuildAuditRequest(fixture.Request, bad));
            Assert.IsFalse(report.EvidenceConsistent, AuditIssueText(report));
            Assert.IsFalse(report.WorkflowBoundaryAllowsContinuation);
            Assert.IsFalse(report.ReleaseBoundaryInfersReadiness);
            Assert.IsTrue(report.HumanReviewRequired);
        }
    }

    [TestMethod]
    public async Task RollbackFailureRegression_StoreFailureAfterMutationIsLoudAndDoesNotClaimSuccess()
    {
        var workspace = Workspace();
        var fixture = RollbackFixture.Create(workspace, [Op.Modify("src/file.txt", "pr209-before", "pr209-after")]);
        fixture.WriteAppliedState();
        var file = Path.Combine(workspace, "src", "file.txt");

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            new ControlledRollbackExecutor(new ThrowingRollbackExecutionReceiptStore()).RollbackAsync(fixture.Request));

        StringAssert.Contains(exception.Message, "Simulated receipt persistence failure");
        Assert.AreEqual("pr209-before", await File.ReadAllTextAsync(file, Encoding.UTF8));
    }

    [TestMethod]
    public async Task RollbackFailureRegression_SqlRejectsRawPrivateAndAuthorityClaims()
    {
        foreach (var token in new[]
        {
            "raw prompt leaked",
            "rawPrompt leaked",
            "raw completion leaked",
            "rawCompletion leaked",
            "raw tool output leaked",
            "rawToolOutput leaked",
            "chain-of-thought leaked",
            "chainOfThought leaked",
            "private reasoning leaked",
            "system prompt leaked",
            "developer prompt leaked",
            "password leaked",
            "secret leaked",
            "api_key leaked",
            "bearer token leaked",
            "workflow continued",
            "release approved",
            "release ready",
            "policy satisfied",
            "approval granted",
            "source applied",
            "gitcommitted",
            "gitpushed",
            "pull request created",
            "memory promoted",
            "retrieval activated"
        })
        {
            await ExpectSqlExceptionAsync(() => InsertReceiptDirectAsync(ValidDirectReceipt() with { EvidenceReferences = [token] }));
        }
    }

    [TestMethod]
    public void RollbackFailureRegression_NoAccidentalAuthorityFields()
    {
        var root = RepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackExecutionReceipt.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackExecutionAudit.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "ControlledRollbackExecutor.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlRollbackExecutionReceiptStore.cs")
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var token in new[] { "WorkflowCanContinue", "SafeToContinue", "ReleaseReady", "ReleaseApproved", "RollbackCleanedUp", "CrashCleanedUp", "RollbackFixed", "SourceSafe", "CanApplyAgain", "AutoContinue", "AutoRelease" })
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Forbidden authority token found in {file}: {token}");
            }
        }
    }
    private static void AssertPreflightRejected(ControlledRollbackExecutionResult result, string because)
    {
        Assert.IsFalse(result.Succeeded, because + ": " + IssueText(result));
        Assert.IsFalse(result.MutationOccurred, because);
        Assert.IsFalse(result.PartialRollbackOccurred, because);
        Assert.IsNull(result.Receipt, because);
    }

    private static RollbackExecutionAuditRequest BuildAuditRequest(ControlledRollbackExecutionRequest request, RollbackExecutionReceipt receipt) => new()
    {
        RollbackExecutionAuditRequestId = Guid.NewGuid(),
        ProjectId = request.ProjectId,
        RollbackExecutionReceipt = receipt,
        RollbackPlan = request.RollbackPlan,
        RollbackSupportReceipt = request.RollbackSupportReceipt,
        SourceApplyReceipt = request.SourceApplyReceipt,
        SourceApplyRequest = request.SourceApplyRequest,
        PatchArtifact = request.PatchArtifact,
        AuditedAtUtc = request.RequestedAtUtc.AddMinutes(5),
        EvidenceReferences = ["rollback-failure-regression-audit-evidence"],
        BoundaryMaxims = ["Rollback failure evidence is not workflow continuation."],
        Boundary = RollbackExecutionAuditBoundaryText.Boundary
    };

    private static string AuditIssueText(RollbackExecutionAuditReport report) =>
        string.Join("; ", report.Issues.Select(issue => $"{issue.Code}:{issue.Field}"));

    private static RollbackExecutionReceipt RehashReceipt(RollbackExecutionReceipt receipt) =>
        receipt with { RollbackExecutionReceiptHash = RollbackExecutionReceiptHashing.ComputeReceiptHash(receipt) };

    private sealed class ThrowingRollbackExecutionReceiptStore : IRollbackExecutionReceiptStore
    {
        public Task SaveAsync(RollbackExecutionReceipt receipt, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated receipt persistence failure.");

        public Task<RollbackExecutionReceipt?> GetAsync(Guid projectId, Guid rollbackExecutionReceiptId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RollbackExecutionReceipt?> GetByReceiptHashAsync(Guid projectId, string rollbackExecutionReceiptHash, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<RollbackExecutionReceipt>> ListBySourceApplyReceiptAsync(Guid projectId, Guid sourceApplyReceiptId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<RollbackExecutionReceipt>> ListByRollbackPlanAsync(Guid projectId, Guid rollbackPlanId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<RollbackExecutionReceipt>> ListByRollbackSupportReceiptAsync(Guid projectId, Guid rollbackSupportReceiptId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<RollbackExecutionReceipt>> ListByPatchArtifactAsync(Guid projectId, Guid patchArtifactId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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

    private static void AssertReceiptRoundTrips(RollbackExecutionReceipt expected, RollbackExecutionReceipt? actual)
    {
        Assert.IsNotNull(actual);
        Assert.AreEqual(expected.RollbackExecutionReceiptId, actual!.RollbackExecutionReceiptId);
        Assert.AreEqual(expected.RollbackExecutionReceiptHash, actual.RollbackExecutionReceiptHash);
        Assert.AreEqual(expected.MutationOccurred, actual.MutationOccurred);
        Assert.AreEqual(expected.RollbackSucceeded, actual.RollbackSucceeded);
        Assert.AreEqual(expected.PartialRollbackOccurred, actual.PartialRollbackOccurred);
        Assert.AreEqual(expected.FileResults.Count, actual.FileResults.Count);
        CollectionAssert.AreEquivalent(expected.FileResults.Select(file => file.FileResultHash).ToArray(), actual.FileResults.Select(file => file.FileResultHash).ToArray());
        CollectionAssert.AreEquivalent(expected.IssueCodes.ToArray(), actual.IssueCodes.ToArray());
    }

    private static void AssertSingle(RollbackExecutionReceipt expected, IReadOnlyList<RollbackExecutionReceipt> actual)
    {
        Assert.AreEqual(1, actual.Count);
        Assert.AreEqual(expected.RollbackExecutionReceiptId, actual[0].RollbackExecutionReceiptId);
        Assert.AreEqual(expected.RollbackExecutionReceiptHash, actual[0].RollbackExecutionReceiptHash);
    }

    private static async Task ExpectSqlExceptionAsync(Func<Task> action)
    {
        try { await action(); }
        catch (SqlException) { return; }
        Assert.Fail("Expected SqlException.");
    }

    private static string Serialized(object value) => JsonSerializer.Serialize(value, JsonOptions);

    private static string IssueText(ControlledRollbackExecutionResult result) =>
        string.Join("; ", result.Issues.Select(issue => $"{issue.Code}:{issue.Field}:{issue.Message}"));

    private static RollbackExecutionReceipt ValidDirectReceipt()
    {
        var fileResult = new RollbackExecutionReceiptFileResult
        {
            Path = "src/direct.txt",
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
            RolledBackAtUtc = new DateTimeOffset(2026, 6, 17, 15, 0, 0, TimeSpan.Zero),
            RollbackExecutionReceiptHash = "sha256:pending",
            EvidenceReferences = ["rollback-execution-evidence"],
            BoundaryMaxims = ["RollbackExecutionReceipt is mutation evidence only."],
            Boundary = RollbackExecutionBoundaryText.Boundary
        };
        return receipt with { RollbackExecutionReceiptHash = RollbackExecutionReceiptHashing.ComputeReceiptHash(receipt) };
    }

    private static string Workspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "irondev-pr209-rollback-write-" + Guid.NewGuid().ToString("N"));
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
        private readonly string _workspace;
        private readonly IReadOnlyList<Op> _ops;

        private RollbackFixture(string workspace, ControlledRollbackExecutionRequest request, IReadOnlyList<Op> ops)
        {
            _workspace = workspace;
            Request = request;
            _ops = ops;
        }

        public ControlledRollbackExecutionRequest Request { get; }

        public static RollbackFixture Create(string workspace, IReadOnlyList<Op> ops)
        {
            var projectId = Guid.NewGuid();
            var now = new DateTimeOffset(2026, 6, 17, 16, 0, 0, TimeSpan.Zero);
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

            var fileOperations = ops.Select(FileOp).ToArray();
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
                FileOperations = fileOperations,
                RequestedAtUtc = now,
                ExpiresAtUtc = now.AddHours(1),
                SourceApplyRequestHash = H("source-apply-request"),
                EvidenceReferences = ["source-apply-request-evidence"],
                BoundaryMaxims = ["Source apply request is not apply."],
                Boundary = SourceApplyRequestBoundaryText.Boundary
            };

            var rollbackActions = ops.Select(Action).ToArray();
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
                FileActions = rollbackActions,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddHours(1),
                EvidenceReferences = ["rollback-plan-evidence"],
                BoundaryMaxims = ["Rollback plan is not rollback execution."],
                Boundary = RollbackPlanBoundaryText.Boundary
            };

            var rollbackSupport = new RollbackSupportReceipt
            {
                RollbackSupportReceiptId = rollbackSupportId,
                ProjectId = projectId,
                RollbackPlanId = rollbackPlan.RollbackPlanId,
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
                BoundaryMaxims = ["Rollback support receipt is not rollback execution."],
                Boundary = RollbackSupportReceiptBoundaryText.Boundary
            };

            var sourceApplyFileResults = ops.Select(BuildSourceApplyFileResult).ToArray();
            var sourceApplyReceipt = new SourceApplyReceipt
            {
                SourceApplyReceiptId = sourceApplyReceiptId,
                ProjectId = projectId,
                ControlledSourceApplyRequestId = Guid.NewGuid(),
                SourceApplyRequestId = sourceApplyRequest.SourceApplyRequestId,
                SourceApplyRequestHash = sourceApplyRequest.SourceApplyRequestHash,
                SourceApplyDryRunReceiptId = Guid.NewGuid(),
                SourceApplyDryRunReceiptHash = H("source-apply-dry-run-receipt"),
                SourceApplyGateEvaluationId = sourceApplyRequest.SourceApplyGateEvaluationId,
                SourceApplyGateEvaluationHash = sourceApplyRequest.SourceApplyGateEvaluationHash,
                PatchArtifactId = patch.PatchArtifactId,
                PatchHash = patch.PatchHash,
                ChangeSetHash = patch.ChangeSetHash,
                RollbackSupportReceiptId = rollbackSupport.RollbackSupportReceiptId,
                RollbackSupportReceiptHash = rollbackSupport.RollbackSupportReceiptHash,
                SourceBaselineHash = patch.SourceBaselineHash,
                WorkspaceBoundaryHash = patch.WorkspaceBoundaryHash,
                ExpectedBranch = sourceApplyRequest.ExpectedBranch,
                ExpectedCleanWorktreeHash = sourceApplyRequest.ExpectedCleanWorktreeHash,
                ObservedBranch = "main",
                ObservedCleanWorktreeHashBeforeApply = cleanBeforeApplyHash,
                ObservedCleanWorktreeHashAfterApply = cleanAfterApplyHash,
                MutationOccurred = true,
                ApplySucceeded = true,
                PartialApplyOccurred = false,
                FileResults = sourceApplyFileResults,
                IssueCodes = ["NoIssues"],
                AppliedAtUtc = now.AddMinutes(5),
                SourceApplyReceiptHash = "sha256:pending",
                EvidenceReferences = ["source-apply-receipt-evidence"],
                BoundaryMaxims = ["SourceApplyReceipt is mutation evidence, not release approval."],
                Boundary = SourceApplyReceiptBoundaryText.Boundary
            };
            sourceApplyReceipt = sourceApplyReceipt with { SourceApplyReceiptHash = SourceApplyReceiptHashing.ComputeReceiptHash(sourceApplyReceipt) };

            var request = new ControlledRollbackExecutionRequest
            {
                ControlledRollbackExecutionRequestId = Guid.NewGuid(),
                ProjectId = projectId,
                RollbackPlan = rollbackPlan,
                RollbackSupportReceipt = rollbackSupport,
                SourceApplyRequest = sourceApplyRequest,
                SourceApplyReceipt = sourceApplyReceipt,
                PatchArtifact = patch,
                WorkspaceRoot = workspace,
                ApprovedWorkspaceBoundaryHash = workspaceHash,
                ObservedBranch = "main",
                ObservedSourceBaselineHash = baselineHash,
                ObservedCleanWorktreeHashBeforeRollback = cleanAfterApplyHash,
                ApprovedContents = ops.Where(op => op.Kind is "Modify" or "Delete" or "Rename").Select(Content).ToArray(),
                RequestedAtUtc = now.AddMinutes(10),
                EvidenceReferences = ["controlled-rollback-execution-request-evidence"],
                BoundaryMaxims = ["Controlled rollback execution writes evidence only."]
            };

            return new RollbackFixture(workspace, request, ops);
        }

        public void WriteAppliedState()
        {
            foreach (var op in _ops)
            {
                switch (op.Kind)
                {
                    case "Create":
                    case "Modify":
                        Write(op.Path, op.After!);
                        break;
                    case "Delete":
                        Delete(op.Path);
                        break;
                    case "Rename":
                        Delete(op.PreviousPath!);
                        Write(op.Path, op.After!);
                        break;
                    case "Noop":
                        Write(op.Path, op.After!);
                        break;
                }
            }
        }

        private void Write(string path, string content)
        {
            var fullPath = Path.Combine(_workspace, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content, Encoding.UTF8);
        }

        private void Delete(string path)
        {
            var fullPath = Path.Combine(_workspace, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath)) File.Delete(fullPath);
        }

        private static PatchArtifactFileChange Change(Op op) => new()
        {
            Path = op.Path,
            PreviousPath = op.PreviousPath,
            ChangeKind = op.Kind,
            BeforeContentHash = op.Before is null ? null : H(op.Before),
            AfterContentHash = op.After is null ? null : H(op.After),
            DiffHash = H("diff:" + op.Path),
            NormalizedDiff = "diff safe " + op.Path,
            IsBinary = false
        };

        private static SourceApplyRequestFileOperation FileOp(Op op)
        {
            var changeHash = op.Kind == "Noop" ? H("noop-change:" + op.Path) : PatchArtifactHashing.ComputeFileChangeHash(Change(op));
            return new SourceApplyRequestFileOperation
            {
                Path = op.Path,
                PreviousPath = op.PreviousPath,
                OperationKind = op.Kind switch
                {
                    "Create" => SourceApplyRequestFileOperationKinds.CreateFile,
                    "Modify" => SourceApplyRequestFileOperationKinds.ModifyFile,
                    "Delete" => SourceApplyRequestFileOperationKinds.DeleteFile,
                    "Rename" => SourceApplyRequestFileOperationKinds.RenameFile,
                    _ => SourceApplyRequestFileOperationKinds.Noop
                },
                BeforeContentHash = op.Before is null ? null : H(op.Before),
                AfterContentHash = op.After is null ? null : H(op.After),
                DiffHash = op.Kind == "Noop" ? null : H("diff:" + op.Path),
                PatchArtifactChangeHash = changeHash,
                OperationHash = H("source-operation:" + op.Path + ":" + op.Kind)
            };
        }

        private static RollbackPlanFileAction Action(Op op) => new()
        {
            Path = op.Path,
            PreviousPath = op.Kind == "Rename" ? op.PreviousPath : null,
            PlannedActionKind = op.Kind switch
            {
                "Create" => RollbackPlanFileActionKinds.DeleteCreatedFile,
                "Modify" => RollbackPlanFileActionKinds.RestoreModifiedFile,
                "Delete" => RollbackPlanFileActionKinds.RecreateDeletedFile,
                "Rename" => RollbackPlanFileActionKinds.RenameBack,
                _ => RollbackPlanFileActionKinds.Noop
            },
            RestoreContentHash = op.Kind is "Modify" or "Delete" or "Rename" ? H(op.Before!) : null,
            DeleteContentHash = op.Kind == "Create" ? H(op.After!) : null,
            ExpectedCurrentContentHash = op.Kind switch
            {
                "Delete" => H("missing:" + op.Path),
                "Noop" => H(op.After!),
                _ => H(op.After!)
            },
            RollbackActionHash = H("rollback-action:" + op.Path + ":" + op.Kind),
            IsBinary = false
        };

        private static ControlledRollbackContent Content(Op op) => new()
        {
            Path = op.Path,
            ContentHash = H(op.Before!),
            Content = op.Before!
        };

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
}
