using System.Reflection;
using System.Diagnostics;
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
[TestCategory("RollbackSupportReceiptStore")]
[TestCategory("RealDatabaseRollbackSupportReceiptStoreSmoke")]
public sealed class RollbackSupportReceiptStoreTests : IntegrationTestBase
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 6, 17, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAtUtc = new(2026, 6, 18, 8, 0, 0, TimeSpan.Zero);
    private SqlRollbackSupportReceiptStore _store = default!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropRollbackSupportReceiptAsync();
        await ApplySqlFileAsync("Database", "migrate_rollback_support_receipt.sql");

        _store = new SqlRollbackSupportReceiptStore(ServiceProvider.GetRequiredService<IDbConnectionFactory>());
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        await DropRollbackSupportReceiptAsync();
        await base.TestCleanup();
    }

    [TestMethod]
    public async Task RollbackSupportReceiptStore_SaveAndGetRoundTripsReceipt()
    {
        var receipt = ValidReceipt();

        await _store.SaveAsync(receipt);
        var read = await _store.GetAsync(receipt.ProjectId, receipt.RollbackSupportReceiptId);

        Assert.IsNotNull(read);
        AssertReceipt(receipt, read);
    }

    [TestMethod]
    public async Task RollbackSupportReceiptStore_SaveRejectsInvalidReceiptBeforeSql()
    {
        var invalid = ValidReceipt() with { RollbackSupportReceiptHash = " " };

        var ex = await AssertThrowsAsync<ArgumentException>(() => _store.SaveAsync(invalid));

        StringAssert.Contains(ex.Message, "ROLLBACK_SUPPORT_RECEIPT_HASH_REQUIRED");
        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM governance.RollbackSupportReceipt"));
    }

    [TestMethod]
    public async Task RollbackSupportReceiptStore_RejectsUnsatisfiedRollbackGate()
    {
        var invalid = ValidReceipt() with { RollbackGateSatisfied = false };

        var ex = await AssertThrowsAsync<ArgumentException>(() => _store.SaveAsync(invalid));

        StringAssert.Contains(ex.Message, "ROLLBACK_GATE_NOT_SATISFIED");
        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM governance.RollbackSupportReceipt"));
    }

    [TestMethod]
    public async Task RollbackSupportReceiptStore_RejectsDuplicateReceiptId()
    {
        var receipt = ValidReceipt("duplicate-id-a");
        var duplicate = ValidReceipt("duplicate-id-b") with { RollbackSupportReceiptId = receipt.RollbackSupportReceiptId };

        await _store.SaveAsync(receipt);

        await AssertThrowsAsync<SqlException>(() => _store.SaveAsync(duplicate));
    }

    [TestMethod]
    public async Task RollbackSupportReceiptStore_RejectsDuplicateReceiptHashWithinProject()
    {
        var projectId = Guid.NewGuid();
        var first = ValidReceipt("hash-a") with { ProjectId = projectId, RollbackSupportReceiptHash = "sha256:shared-receipt" };
        var second = ValidReceipt("hash-b") with { ProjectId = projectId, RollbackSupportReceiptHash = "sha256:shared-receipt" };

        await _store.SaveAsync(first);

        await AssertThrowsAsync<SqlException>(() => _store.SaveAsync(second));
    }

    [TestMethod]
    public async Task RollbackSupportReceiptStore_RejectsDuplicateRollbackPlanWithinProject()
    {
        var projectId = Guid.NewGuid();
        var rollbackPlanId = Guid.NewGuid();
        var first = ValidReceipt("plan-a") with { ProjectId = projectId, RollbackPlanId = rollbackPlanId };
        var second = ValidReceipt("plan-b") with { ProjectId = projectId, RollbackPlanId = rollbackPlanId };

        await _store.SaveAsync(first);

        await AssertThrowsAsync<SqlException>(() => _store.SaveAsync(second));
    }

    [TestMethod]
    public async Task RollbackSupportReceiptStore_AllowsSameReceiptHashAcrossDifferentProjectsOnlyIfSafe()
    {
        var hash = "sha256:shared-across-projects";
        var first = ValidReceipt("project-a") with { ProjectId = Guid.NewGuid(), RollbackSupportReceiptHash = hash };
        var second = ValidReceipt("project-b") with { ProjectId = Guid.NewGuid(), RollbackSupportReceiptHash = hash };

        await _store.SaveAsync(first);
        await _store.SaveAsync(second);

        Assert.AreEqual(first.RollbackSupportReceiptId, (await _store.GetByReceiptHashAsync(first.ProjectId, hash))?.RollbackSupportReceiptId);
        Assert.AreEqual(second.RollbackSupportReceiptId, (await _store.GetByReceiptHashAsync(second.ProjectId, hash))?.RollbackSupportReceiptId);
    }

    [TestMethod]
    public async Task RollbackSupportReceiptStore_GetByReceiptHashIsProjectScoped()
    {
        var hash = "sha256:project-scoped-hash";
        var first = ValidReceipt("scope-a") with { ProjectId = Guid.NewGuid(), RollbackSupportReceiptHash = hash };
        var second = ValidReceipt("scope-b") with { ProjectId = Guid.NewGuid(), RollbackSupportReceiptHash = hash };

        await _store.SaveAsync(first);
        await _store.SaveAsync(second);

        Assert.AreEqual(first.RollbackSupportReceiptId, (await _store.GetByReceiptHashAsync(first.ProjectId, hash))?.RollbackSupportReceiptId);
        Assert.AreEqual(second.RollbackSupportReceiptId, (await _store.GetByReceiptHashAsync(second.ProjectId, hash))?.RollbackSupportReceiptId);
        Assert.IsNull(await _store.GetByReceiptHashAsync(Guid.NewGuid(), hash));
    }

    [TestMethod]
    public async Task RollbackSupportReceiptStore_ListByPatchArtifactIsProjectScoped()
    {
        var projectId = Guid.NewGuid();
        var patchArtifactId = Guid.NewGuid();
        var matching = ValidReceipt("patch-artifact-a") with { ProjectId = projectId, PatchArtifactId = patchArtifactId };
        var otherProject = ValidReceipt("patch-artifact-b") with { ProjectId = Guid.NewGuid(), PatchArtifactId = patchArtifactId };
        var otherPatch = ValidReceipt("patch-artifact-c") with { ProjectId = projectId, PatchArtifactId = Guid.NewGuid() };

        await SaveAllAsync(matching, otherProject, otherPatch);

        CollectionAssert.AreEqual(new[] { matching.RollbackSupportReceiptId }, (await _store.ListByPatchArtifactAsync(projectId, patchArtifactId)).Select(item => item.RollbackSupportReceiptId).ToArray());
    }

    [TestMethod]
    public async Task RollbackSupportReceiptStore_ListByPatchHashIsProjectScoped()
    {
        var projectId = Guid.NewGuid();
        var patchHash = "sha256:patch-scoped";
        var matching = ValidReceipt("patch-hash-a") with { ProjectId = projectId, PatchHash = patchHash };
        var otherProject = ValidReceipt("patch-hash-b") with { ProjectId = Guid.NewGuid(), PatchHash = patchHash };
        var otherPatch = ValidReceipt("patch-hash-c") with { ProjectId = projectId, PatchHash = "sha256:other-patch" };

        await SaveAllAsync(matching, otherProject, otherPatch);

        CollectionAssert.AreEqual(new[] { matching.RollbackSupportReceiptId }, (await _store.ListByPatchHashAsync(projectId, patchHash)).Select(item => item.RollbackSupportReceiptId).ToArray());
    }

    [TestMethod]
    public async Task RollbackSupportReceiptStore_ListByRollbackPlanIsProjectScoped()
    {
        var projectId = Guid.NewGuid();
        var rollbackPlanId = Guid.NewGuid();
        var matching = ValidReceipt("rollback-plan-a") with { ProjectId = projectId, RollbackPlanId = rollbackPlanId };
        var otherProject = ValidReceipt("rollback-plan-b") with { ProjectId = Guid.NewGuid(), RollbackPlanId = rollbackPlanId };
        var otherPlan = ValidReceipt("rollback-plan-c") with { ProjectId = projectId, RollbackPlanId = Guid.NewGuid() };

        await SaveAllAsync(matching, otherProject, otherPlan);

        CollectionAssert.AreEqual(new[] { matching.RollbackSupportReceiptId }, (await _store.ListByRollbackPlanAsync(projectId, rollbackPlanId)).Select(item => item.RollbackSupportReceiptId).ToArray());
    }

    [TestMethod]
    public async Task RollbackSupportReceiptStore_ListBySourceBaselineHashIsProjectScoped()
    {
        var projectId = Guid.NewGuid();
        var sourceBaselineHash = "sha256:baseline-scoped";
        var matching = ValidReceipt("baseline-a") with { ProjectId = projectId, SourceBaselineHash = sourceBaselineHash };
        var otherProject = ValidReceipt("baseline-b") with { ProjectId = Guid.NewGuid(), SourceBaselineHash = sourceBaselineHash };
        var otherBaseline = ValidReceipt("baseline-c") with { ProjectId = projectId, SourceBaselineHash = "sha256:other-baseline" };

        await SaveAllAsync(matching, otherProject, otherBaseline);

        CollectionAssert.AreEqual(new[] { matching.RollbackSupportReceiptId }, (await _store.ListBySourceBaselineHashAsync(projectId, sourceBaselineHash)).Select(item => item.RollbackSupportReceiptId).ToArray());
    }

    [TestMethod]
    public async Task RollbackSupportReceiptStore_RejectsPrivateRawMaterial()
    {
        foreach (var invalid in new[]
        {
            ValidReceipt("raw-evidence") with { EvidenceReferences = ["raw prompt leaked"] },
            ValidReceipt("raw-maxim") with { BoundaryMaxims = ["chain-of-thought leaked"] },
            ValidReceipt("raw-boundary") with { Boundary = "private reasoning leaked" },
            ValidReceipt("raw-branch") with { ExpectedBranch = "raw completion branch" },
            ValidReceipt("raw-snapshot") with { SourceSnapshotReference = "source snapshot raw tool output" },
            ValidReceipt("raw-subject") with { SubjectId = "subject secret leaked" }
        })
        {
            var ex = await AssertThrowsAsync<ArgumentException>(() => _store.SaveAsync(invalid));
            StringAssert.Contains(ex.Message, "PRIVATE_OR_RAW_MATERIAL_REJECTED");
        }
    }

    [TestMethod]
    public async Task RollbackSupportReceiptStore_RejectsAuthorityClaims()
    {
        foreach (var invalid in new[]
        {
            ValidReceipt("authority-source") with { EvidenceReferences = ["source applied"] },
            ValidReceipt("authority-rollback-executed") with { BoundaryMaxims = ["rollback executed"] },
            ValidReceipt("authority-rollback-succeeded") with { ExpectedBranch = "rollback succeeded branch" },
            ValidReceipt("authority-workflow") with { SourceSnapshotReference = "workflow continued" },
            ValidReceipt("authority-release") with { SubjectId = "release ready" }
        })
        {
            var ex = await AssertThrowsAsync<ArgumentException>(() => _store.SaveAsync(invalid));
            StringAssert.Contains(ex.Message, "AUTHORITY_CLAIM_REJECTED");
        }
    }

    [TestMethod]
    public async Task RollbackSupportReceiptStore_BlocksUnsafeDirectSqlInsert()
    {
        await AssertSqlFailsAsync(DirectInsertSql(), DirectInsertParameters("direct-raw", true, "subject raw prompt"));
        await AssertSqlFailsAsync(DirectInsertSql(), DirectInsertParameters("direct-unsatisfied", false, "subject-safe"));
    }

    [TestMethod]
    public async Task RollbackSupportReceiptStore_BlocksUpdateDelete()
    {
        var receipt = ValidReceipt();
        await _store.SaveAsync(receipt);

        await AssertSqlFailsAsync("UPDATE governance.RollbackSupportReceipt SET PatchHash = N'sha256:changed' WHERE RollbackSupportReceiptId = @id", new SqlParameter("@id", receipt.RollbackSupportReceiptId));
        await AssertSqlFailsAsync("DELETE FROM governance.RollbackSupportReceipt WHERE RollbackSupportReceiptId = @id", new SqlParameter("@id", receipt.RollbackSupportReceiptId));
    }

    [TestMethod]
    public void RollbackSupportReceiptStore_DoesNotExposeUpdateDeleteUpsert()
    {
        var forbidden = new[] { "Update", "Delete", "Upsert", "Overwrite", "MarkExecuted", "MarkSucceeded", "Apply", "Continue", "Approve" };
        var methods = typeof(IRollbackSupportReceiptStore)
            .GetMethods()
            .Concat(typeof(SqlRollbackSupportReceiptStore).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

        foreach (var method in methods)
        foreach (var token in forbidden)
            Assert.IsFalse(method.Name.Contains(token, StringComparison.OrdinalIgnoreCase), $"Unexpected method: {method.Name}");
    }

    [TestMethod]
    public void RollbackSupportReceiptStore_DoesNotEvaluateOrExecute()
    {
        foreach (var token in new[]
        {
            "RollbackGateEvaluator.Evaluate",
            "ExecuteRollback",
            "RollbackExecutor",
            "ApplySourceAsync",
            "SourceApplyService",
            "ControlledSourceApply",
            "ProcessStartInfo",
            "System.Diagnostics.Process",
            "git ",
            "File.WriteAllText",
            "Directory.CreateDirectory",
            "ContinueWorkflowAsync",
            "ApproveReleaseAsync"
        })
        {
            AssertNoProductionToken(token);
        }
    }

    [TestMethod]
    public void RollbackSupportReceiptStore_DoesNotAddApiCliUiRuntime()
    {
        foreach (var file in Pr196ChangedFiles())
        {
            var relative = Path.GetRelativePath(RepoRoot(), file);
            foreach (var token in new[] { "Controller", "Program.cs", "Cli", "Tauri", "UI", "IHostedService", "BackgroundService", "Scheduler" })
                Assert.IsFalse(relative.Contains(token, StringComparison.OrdinalIgnoreCase), $"PR196 must not add {token}: {relative}");
        }
    }

    [TestMethod]
    public void RollbackSupportReceiptStore_DoesNotCallModelsAgentsMemoryRetrieval()
    {
        foreach (var token in new[]
        {
            "LLM",
            "model call",
            "AgentDispatch",
            "ToolExecution",
            "PromoteMemory",
            "ActivateRetrieval",
            "Vector",
            "Embedding",
            "Weaviate"
        })
        {
            AssertNoProductionToken(token);
        }
    }

    [TestMethod]
    public void RollbackSupportReceiptStore_MigrationAndInventoryAreRegistered()
    {
        var manifest = File.ReadAllText(Path.Combine(RepoRoot(), "Database", "migrations.json"));
        var inventory = File.ReadAllText(Path.Combine(RepoRoot(), "Database", "sql-inventory.json"));
        var verifier = File.ReadAllText(Path.Combine(RepoRoot(), "Database", "verify-migrations.ps1"));
        var sql = File.ReadAllText(SqlMigrationPath());

        StringAssert.Contains(manifest, "Database/migrate_rollback_support_receipt.sql");
        StringAssert.Contains(inventory, "database.migrate-rollback-support-receipt");
        StringAssert.Contains(inventory, "runtime.rollback-support-receipt-store");
        StringAssert.Contains(verifier, "governance.RollbackSupportReceipt table");
        StringAssert.Contains(verifier, "governance.usp_RollbackSupportReceipt_Save procedure");
        StringAssert.Contains(sql, "governance.RollbackSupportReceipt");
        StringAssert.Contains(sql, "governance.usp_RollbackSupportReceipt_Save");
        StringAssert.Contains(sql, "governance.usp_RollbackSupportReceipt_Get");
        StringAssert.Contains(sql, "governance.usp_RollbackSupportReceipt_GetByReceiptHash");
        StringAssert.Contains(sql, "governance.usp_RollbackSupportReceipt_ListByPatchArtifact");
        StringAssert.Contains(sql, "governance.usp_RollbackSupportReceipt_ListByPatchHash");
        StringAssert.Contains(sql, "governance.usp_RollbackSupportReceipt_ListByRollbackPlan");
        StringAssert.Contains(sql, "governance.usp_RollbackSupportReceipt_ListBySourceBaselineHash");
        StringAssert.Contains(sql, "TR_RollbackSupportReceipt_ValidateInsert");
        StringAssert.Contains(sql, "TR_RollbackSupportReceipt_BlockUpdateDelete");
    }

    [TestMethod]
    public void RollbackSupportReceiptStore_RuntimeRoleCannotMutateTableDirectly()
    {
        var sql = File.ReadAllText(SqlMigrationPath());

        StringAssert.Contains(sql, "GRANT EXECUTE ON OBJECT::governance.usp_RollbackSupportReceipt_Save");
        StringAssert.Contains(sql, "DENY INSERT, UPDATE, DELETE ON OBJECT::governance.RollbackSupportReceipt");
        StringAssert.Contains(sql, "DENY ALTER ON SCHEMA::governance");
    }

    [TestMethod]
    public void RollbackSupportReceiptStore_ReceiptStatesBoundary()
    {
        var receipt = File.ReadAllText(ReceiptPath());

        foreach (var statement in new[]
        {
            "PR196 adds the Rollback Receipt Store.",
            "This PR stores rollback-support receipts.",
            "This PR is data/store/test/receipt only.",
            "This PR does not execute rollback.",
            "This PR does not prove rollback execution succeeded.",
            "This PR does not apply source.",
            "This PR does not mutate source.",
            "This PR does not inspect branch.",
            "This PR does not inspect worktree.",
            "This PR does not run git.",
            "This PR does not create source-apply requests.",
            "This PR does not create source-apply receipts.",
            "This PR does not continue workflow.",
            "This PR does not approve release.",
            "This PR does not add API.",
            "This PR does not add CLI.",
            "This PR does not add UI.",
            "Rollback support receipt is not rollback execution.",
            "Rollback support receipt is not rollback success.",
            "Rollback support receipt is not source apply.",
            "Rollback support receipt is not workflow continuation.",
            "Rollback support receipt is not release readiness.",
            "Rollback support receipt does not authorize source mutation by itself.",
            "Rollback support receipt records that rollback support existed for a patch artifact.",
            "Real source apply must still pass the source-apply gate before mutation.",
            "Rollback support receipt binds rollback plan hash, rollback gate evaluation hash, patch artifact id, patch hash, change-set hash, dry-run audit hash, dry-run receipt hash, policy satisfaction hash, subject hash, source snapshot reference, source baseline hash, workspace boundary hash, expected branch, expected clean worktree hash, receipt hash, evidence references, and boundary maxims.",
            "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate",
            "The next Block U target is Rollback Read API.",
            "PR197 - Rollback Read API"
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    private async Task SaveAllAsync(params RollbackSupportReceipt[] receipts)
    {
        foreach (var receipt in receipts)
            await _store.SaveAsync(receipt);
    }

    private static RollbackSupportReceipt ValidReceipt(string suffix = "main") => new()
    {
        RollbackSupportReceiptId = Guid.NewGuid(),
        ProjectId = Guid.NewGuid(),
        RollbackPlanId = Guid.NewGuid(),
        RollbackPlanHash = $"sha256:rollback-plan-{suffix}",
        RollbackGateSatisfied = true,
        RollbackGateEvaluationHash = $"sha256:rollback-gate-evaluation-{suffix}",
        PatchArtifactId = Guid.NewGuid(),
        PatchHash = $"sha256:patch-{suffix}",
        ChangeSetHash = $"sha256:change-set-{suffix}",
        ControlledDryRunRequestId = Guid.NewGuid(),
        DryRunExecutionAuditId = Guid.NewGuid(),
        DryRunAuditHash = $"sha256:dry-run-audit-{suffix}",
        DryRunReceiptHash = $"sha256:dry-run-receipt-{suffix}",
        PolicySatisfactionId = Guid.NewGuid(),
        PolicySatisfactionHash = $"sha256:policy-satisfaction-{suffix}",
        SubjectKind = "PatchArtifact",
        SubjectId = $"patch-artifact-{suffix}",
        SubjectHash = $"sha256:subject-{suffix}",
        SourceSnapshotReference = $"source-snapshot:{suffix}",
        SourceBaselineHash = $"sha256:source-baseline-{suffix}",
        WorkspaceBoundaryHash = $"sha256:workspace-boundary-{suffix}",
        ExpectedBranch = $"main-{suffix}",
        ExpectedCleanWorktreeHash = $"sha256:clean-worktree-{suffix}",
        RollbackSupportReceiptHash = $"sha256:rollback-support-receipt-{suffix}",
        CreatedAtUtc = CreatedAtUtc,
        ExpiresAtUtc = ExpiresAtUtc,
        EvidenceReferences = [$"rollback-gate-evaluation:{suffix}", $"rollback-plan:{suffix}"],
        BoundaryMaxims = ["Rollback support receipt records rollback support only."],
        Boundary = RollbackSupportReceiptBoundaryText.Boundary
    };

    private static void AssertReceipt(RollbackSupportReceipt expected, RollbackSupportReceipt actual)
    {
        Assert.AreEqual(expected.RollbackSupportReceiptId, actual.RollbackSupportReceiptId);
        Assert.AreEqual(expected.ProjectId, actual.ProjectId);
        Assert.AreEqual(expected.RollbackPlanId, actual.RollbackPlanId);
        Assert.AreEqual(expected.RollbackPlanHash, actual.RollbackPlanHash);
        Assert.AreEqual(expected.RollbackGateSatisfied, actual.RollbackGateSatisfied);
        Assert.AreEqual(expected.RollbackGateEvaluationHash, actual.RollbackGateEvaluationHash);
        Assert.AreEqual(expected.PatchArtifactId, actual.PatchArtifactId);
        Assert.AreEqual(expected.PatchHash, actual.PatchHash);
        Assert.AreEqual(expected.ChangeSetHash, actual.ChangeSetHash);
        Assert.AreEqual(expected.ControlledDryRunRequestId, actual.ControlledDryRunRequestId);
        Assert.AreEqual(expected.DryRunExecutionAuditId, actual.DryRunExecutionAuditId);
        Assert.AreEqual(expected.DryRunAuditHash, actual.DryRunAuditHash);
        Assert.AreEqual(expected.DryRunReceiptHash, actual.DryRunReceiptHash);
        Assert.AreEqual(expected.PolicySatisfactionId, actual.PolicySatisfactionId);
        Assert.AreEqual(expected.PolicySatisfactionHash, actual.PolicySatisfactionHash);
        Assert.AreEqual(expected.SubjectKind, actual.SubjectKind);
        Assert.AreEqual(expected.SubjectId, actual.SubjectId);
        Assert.AreEqual(expected.SubjectHash, actual.SubjectHash);
        Assert.AreEqual(expected.SourceSnapshotReference, actual.SourceSnapshotReference);
        Assert.AreEqual(expected.SourceBaselineHash, actual.SourceBaselineHash);
        Assert.AreEqual(expected.WorkspaceBoundaryHash, actual.WorkspaceBoundaryHash);
        Assert.AreEqual(expected.ExpectedBranch, actual.ExpectedBranch);
        Assert.AreEqual(expected.ExpectedCleanWorktreeHash, actual.ExpectedCleanWorktreeHash);
        Assert.AreEqual(expected.RollbackSupportReceiptHash, actual.RollbackSupportReceiptHash);
        Assert.AreEqual(expected.CreatedAtUtc, actual.CreatedAtUtc);
        Assert.AreEqual(expected.ExpiresAtUtc, actual.ExpiresAtUtc);
        CollectionAssert.AreEqual(expected.EvidenceReferences.ToArray(), actual.EvidenceReferences.ToArray());
        CollectionAssert.AreEqual(expected.BoundaryMaxims.ToArray(), actual.BoundaryMaxims.ToArray());
        Assert.AreEqual(expected.Boundary, actual.Boundary);
    }

    private static string DirectInsertSql() =>
        @"INSERT INTO governance.RollbackSupportReceipt
          (RollbackSupportReceiptId, ProjectId, RollbackPlanId, RollbackPlanHash, RollbackGateSatisfied, RollbackGateEvaluationHash, PatchArtifactId, PatchHash, ChangeSetHash, ControlledDryRunRequestId, DryRunExecutionAuditId, DryRunAuditHash, DryRunReceiptHash, PolicySatisfactionId, PolicySatisfactionHash, SubjectKind, SubjectId, SubjectHash, SourceSnapshotReference, SourceBaselineHash, WorkspaceBoundaryHash, ExpectedBranch, ExpectedCleanWorktreeHash, RollbackSupportReceiptHash, CreatedAtUtc, ExpiresAtUtc, EvidenceReferencesJson, BoundaryMaximsJson, BoundaryText)
          VALUES (NEWID(), NEWID(), NEWID(), CONCAT(N'sha256:rollback-plan-', @suffix), @satisfied, CONCAT(N'sha256:gate-', @suffix), NEWID(), CONCAT(N'sha256:patch-', @suffix), CONCAT(N'sha256:change-set-', @suffix), NEWID(), NEWID(), CONCAT(N'sha256:audit-', @suffix), CONCAT(N'sha256:receipt-', @suffix), NEWID(), CONCAT(N'sha256:policy-', @suffix), N'PatchArtifact', @subjectId, CONCAT(N'sha256:subject-', @suffix), CONCAT(N'source-snapshot:', @suffix), CONCAT(N'sha256:baseline-', @suffix), CONCAT(N'sha256:workspace-', @suffix), CONCAT(N'main-', @suffix), CONCAT(N'sha256:clean-', @suffix), CONCAT(N'sha256:rollback-support-', @suffix), SYSUTCDATETIME(), DATEADD(day, 1, SYSUTCDATETIME()), N'[""rollback-gate:evidence""]', N'[""support only""]', N'Rollback support receipt is not rollback execution.')";

    private static SqlParameter[] DirectInsertParameters(string suffix, bool satisfied, string subjectId) =>
    [
        new SqlParameter("@suffix", suffix),
        new SqlParameter("@satisfied", satisfied),
        new SqlParameter("@subjectId", subjectId)
    ];

    private async Task ApplySqlFileAsync(params string[] pathParts)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(new[] { RepoRoot() }.Concat(pathParts).ToArray()));
        foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(batch))
                continue;

            await connection.ExecuteAsync(batch);
        }
    }

    private async Task DropRollbackSupportReceiptAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            IF OBJECT_ID(N'governance.usp_RollbackSupportReceipt_Save', N'P') IS NOT NULL DROP PROCEDURE governance.usp_RollbackSupportReceipt_Save;
            IF OBJECT_ID(N'governance.usp_RollbackSupportReceipt_Get', N'P') IS NOT NULL DROP PROCEDURE governance.usp_RollbackSupportReceipt_Get;
            IF OBJECT_ID(N'governance.usp_RollbackSupportReceipt_GetByReceiptHash', N'P') IS NOT NULL DROP PROCEDURE governance.usp_RollbackSupportReceipt_GetByReceiptHash;
            IF OBJECT_ID(N'governance.usp_RollbackSupportReceipt_ListByPatchArtifact', N'P') IS NOT NULL DROP PROCEDURE governance.usp_RollbackSupportReceipt_ListByPatchArtifact;
            IF OBJECT_ID(N'governance.usp_RollbackSupportReceipt_ListByPatchHash', N'P') IS NOT NULL DROP PROCEDURE governance.usp_RollbackSupportReceipt_ListByPatchHash;
            IF OBJECT_ID(N'governance.usp_RollbackSupportReceipt_ListByRollbackPlan', N'P') IS NOT NULL DROP PROCEDURE governance.usp_RollbackSupportReceipt_ListByRollbackPlan;
            IF OBJECT_ID(N'governance.usp_RollbackSupportReceipt_ListBySourceBaselineHash', N'P') IS NOT NULL DROP PROCEDURE governance.usp_RollbackSupportReceipt_ListBySourceBaselineHash;
            IF OBJECT_ID(N'governance.TR_RollbackSupportReceipt_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER governance.TR_RollbackSupportReceipt_ValidateInsert;
            IF OBJECT_ID(N'governance.TR_RollbackSupportReceipt_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_RollbackSupportReceipt_BlockUpdateDelete;
            IF OBJECT_ID(N'governance.RollbackSupportReceipt', N'U') IS NOT NULL DROP TABLE governance.RollbackSupportReceipt;
            """);
    }

    private async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<T>(sql, parameters) ?? throw new InvalidOperationException("Scalar query returned null.");
    }

    private async Task AssertSqlFailsAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await AssertThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
    }

    private static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }

        Assert.Fail($"Expected exception of type {typeof(TException).Name}.");
        throw new UnreachableException();
    }

    private static void AssertNoProductionToken(string token)
    {
        foreach (var file in ProductionFiles())
            Assert.IsFalse(File.ReadAllText(file).Contains(token, StringComparison.Ordinal), $"Unexpected production token {token} in {file}.");
    }

    private static string[] ProductionFiles()
    {
        var root = RepoRoot();
        return
        [
            Path.Combine(root, "Database", "migrate_rollback_support_receipt.sql"),
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackSupportReceipt.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackSupportReceiptValidation.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "IRollbackSupportReceiptStore.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlRollbackSupportReceiptStore.cs")
        ];
    }

    private static string[] Pr196ChangedFiles()
    {
        var root = RepoRoot();
        return
        [
            Path.Combine(root, "Database", "migrate_rollback_support_receipt.sql"),
            Path.Combine(root, "Database", "migrations.json"),
            Path.Combine(root, "Database", "sql-inventory.json"),
            Path.Combine(root, "Database", "verify-migrations.ps1"),
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackSupportReceipt.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackSupportReceiptValidation.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "IRollbackSupportReceiptStore.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlRollbackSupportReceiptStore.cs"),
            Path.Combine(root, "Docs", "receipts", "PR196_ROLLBACK_RECEIPT_STORE.md"),
            Path.Combine(root, "IronDev.IntegrationTests", "Governance", "RollbackSupportReceiptStoreTests.cs")
        ];
    }

    private static string SqlMigrationPath() =>
        Path.Combine(RepoRoot(), "Database", "migrate_rollback_support_receipt.sql");

    private static string ReceiptPath() =>
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR196_ROLLBACK_RECEIPT_STORE.md");

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
