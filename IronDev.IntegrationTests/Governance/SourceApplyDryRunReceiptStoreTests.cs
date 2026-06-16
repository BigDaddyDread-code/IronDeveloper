using System.Diagnostics;
using System.Reflection;
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
[TestCategory("SourceApplyDryRunReceiptStore")]
[TestCategory("SourceApplyDryRunReceiptValidation")]
[TestCategory("RealDatabaseSourceApplyDryRunReceiptStoreSmoke")]
public sealed class SourceApplyDryRunReceiptStoreTests : IntegrationTestBase
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 6, 17, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAtUtc = new(2026, 6, 18, 8, 0, 0, TimeSpan.Zero);
    private SqlSourceApplyDryRunReceiptStore _store = default!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropSourceApplyDryRunReceiptAsync();
        await ApplySqlFileAsync("Database", "migrate_source_apply_dry_run_receipt.sql");
        _store = new SqlSourceApplyDryRunReceiptStore(ServiceProvider.GetRequiredService<IDbConnectionFactory>());
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        await DropSourceApplyDryRunReceiptAsync();
        await base.TestCleanup();
    }

    [TestMethod]
    public async Task SourceApplyDryRunReceiptStore_SaveGetAndHashReadRoundTrip()
    {
        var receipt = ValidReceipt();

        await _store.SaveAsync(receipt);

        AssertReceipt(receipt, await _store.GetAsync(receipt.ProjectId, receipt.SourceApplyDryRunReceiptId));
        AssertReceipt(receipt, await _store.GetByReceiptHashAsync(receipt.ProjectId, receipt.SourceApplyDryRunReceiptHash));
    }

    [TestMethod]
    public async Task SourceApplyDryRunReceiptStore_ListsByGovernedEvidenceReferences()
    {
        var receipt = ValidReceipt("list-a");
        await _store.SaveAsync(receipt);
        await _store.SaveAsync(ValidReceipt("list-b"));

        AssertSingle(receipt, await _store.ListBySourceApplyRequestAsync(receipt.ProjectId, receipt.SourceApplyRequestId));
        AssertSingle(receipt, await _store.ListBySourceApplyGateEvaluationAsync(receipt.ProjectId, receipt.SourceApplyGateEvaluationId));
        AssertSingle(receipt, await _store.ListByPatchArtifactAsync(receipt.ProjectId, receipt.PatchArtifactId));
        AssertSingle(receipt, await _store.ListByRollbackSupportReceiptAsync(receipt.ProjectId, receipt.RollbackSupportReceiptId));
    }

    [TestMethod]
    public async Task SourceApplyDryRunReceiptStore_ReadsAreProjectScoped()
    {
        var receipt = ValidReceipt("scope");
        var wrongProject = Guid.NewGuid();
        await _store.SaveAsync(receipt);

        Assert.IsNull(await _store.GetAsync(wrongProject, receipt.SourceApplyDryRunReceiptId));
        Assert.IsNull(await _store.GetByReceiptHashAsync(wrongProject, receipt.SourceApplyDryRunReceiptHash));
        Assert.AreEqual(0, (await _store.ListBySourceApplyRequestAsync(wrongProject, receipt.SourceApplyRequestId)).Count);
    }

    [TestMethod]
    public async Task SourceApplyDryRunReceiptStore_AllowsUnsatisfiedDryRunReceiptAsEvidence()
    {
        var receipt = ValidReceipt("unsatisfied") with
        {
            DryRunSatisfied = false,
            FileResults = [ValidFileResult("unsatisfied-file") with { PreconditionsSatisfied = false, IssueCodes = ["CURRENT_FILE_HASH_MISMATCH"] }]
        };

        await _store.SaveAsync(receipt);
        var read = await _store.GetAsync(receipt.ProjectId, receipt.SourceApplyDryRunReceiptId);

        Assert.IsNotNull(read);
        Assert.IsFalse(read.DryRunSatisfied);
        Assert.IsFalse(read.FileResults[0].PreconditionsSatisfied);
        CollectionAssert.Contains(read.FileResults[0].IssueCodes.ToArray(), "CURRENT_FILE_HASH_MISMATCH");
    }

    [TestMethod]
    public async Task SourceApplyDryRunReceiptStore_RejectsDuplicateReceiptIdAndProjectHash()
    {
        var first = ValidReceipt("duplicate-a");
        await _store.SaveAsync(first);

        await AssertThrowsAsync<SqlException>(() => _store.SaveAsync(ValidReceipt("duplicate-b") with { SourceApplyDryRunReceiptId = first.SourceApplyDryRunReceiptId }));
        await AssertThrowsAsync<SqlException>(() => _store.SaveAsync(ValidReceipt("duplicate-c") with { ProjectId = first.ProjectId, SourceApplyDryRunReceiptHash = first.SourceApplyDryRunReceiptHash }));
    }

    [TestMethod]
    public async Task SourceApplyDryRunReceiptStore_BlocksUpdateDeleteAndUnsafeDirectSql()
    {
        var receipt = ValidReceipt("direct-sql");
        await _store.SaveAsync(receipt);

        await AssertSqlFailsAsync("UPDATE governance.SourceApplyDryRunReceipt SET ExpectedBranch = N'changed' WHERE SourceApplyDryRunReceiptId = @id", new SqlParameter("@id", receipt.SourceApplyDryRunReceiptId));
        await AssertSqlFailsAsync("DELETE FROM governance.SourceApplyDryRunReceipt WHERE SourceApplyDryRunReceiptId = @id", new SqlParameter("@id", receipt.SourceApplyDryRunReceiptId));
        await AssertSqlFailsAsync(DirectInsertSql(), DirectInsertParameters("private-reasoning", "private reasoning leaked"));
        await AssertSqlFailsAsync(DirectInsertSql(), DirectInsertParameters("source-mutated", "source mutated by dry run"));
    }

    [TestMethod]
    public void SourceApplyDryRunReceiptValidation_AcceptsSatisfiedAndUnsatisfiedReceipts()
    {
        Assert.IsTrue(SourceApplyDryRunReceiptValidation.Validate(ValidReceipt("valid-satisfied")).IsValid);

        var unsatisfied = ValidReceipt("valid-unsatisfied") with
        {
            DryRunSatisfied = false,
            FileResults = [ValidFileResult("valid-unsatisfied-file") with { PreconditionsSatisfied = false, IssueCodes = ["TARGET_MISSING"] }]
        };

        Assert.IsTrue(SourceApplyDryRunReceiptValidation.Validate(unsatisfied).IsValid);
    }

    [TestMethod]
    public void SourceApplyDryRunReceiptValidation_RejectsInvalidFileAndUnsafeText()
    {
        var invalidFile = ValidReceipt("invalid-file") with
        {
            SourceApplyDryRunReceiptHash = " ",
            ExpiresAtUtc = CreatedAtUtc.AddMinutes(-1),
            FileResults = [ValidFileResult("dup"), ValidFileResult("dup") with { OperationKind = "ApplyFile" }]
        };
        var unsafeReceipt = ValidReceipt("unsafe") with { ExpectedBranch = "raw prompt leaked" };
        var authorityReceipt = ValidReceipt("authority") with { BoundaryMaxims = ["source mutated"] };

        var codes = Codes(invalidFile);
        CollectionAssert.Contains(codes, "SOURCE_APPLY_DRY_RUN_RECEIPT_HASH_REQUIRED");
        CollectionAssert.Contains(codes, "EXPIRES_AT_UTC_INVALID");
        CollectionAssert.Contains(codes, "FILE_OPERATION_KIND_INVALID");
        CollectionAssert.Contains(Codes(unsafeReceipt), "PRIVATE_OR_RAW_MATERIAL_REJECTED");
        CollectionAssert.Contains(Codes(authorityReceipt), "AUTHORITY_CLAIM_REJECTED");
    }

    [TestMethod]
    public void SourceApplyDryRunReceiptStore_InterfaceIsSaveAndReadOnlySurface()
    {
        var names = typeof(ISourceApplyDryRunReceiptStore)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .OrderBy(name => name)
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                nameof(ISourceApplyDryRunReceiptStore.GetAsync),
                nameof(ISourceApplyDryRunReceiptStore.GetByReceiptHashAsync),
                nameof(ISourceApplyDryRunReceiptStore.ListByPatchArtifactAsync),
                nameof(ISourceApplyDryRunReceiptStore.ListByRollbackSupportReceiptAsync),
                nameof(ISourceApplyDryRunReceiptStore.ListBySourceApplyGateEvaluationAsync),
                nameof(ISourceApplyDryRunReceiptStore.ListBySourceApplyRequestAsync),
                nameof(ISourceApplyDryRunReceiptStore.SaveAsync)
            },
            names);

        Assert.IsFalse(names.Any(name => name.Contains("Execute", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(names.Any(name => name.Contains("Approve", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(names.Any(name => name.Contains("Promote", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void SourceApplyDryRunReceiptStore_MigrationManifestInventoryVerifierAndReceiptAreUpdated()
    {
        var root = RepoRoot();
        var migration = File.ReadAllText(SqlMigrationPath());
        var manifest = File.ReadAllText(Path.Combine(root, "Database", "migrations.json"));
        var inventory = File.ReadAllText(Path.Combine(root, "Database", "sql-inventory.json"));
        var verifier = File.ReadAllText(Path.Combine(root, "Database", "verify-migrations.ps1"));
        var receipt = File.ReadAllText(ReceiptPath());

        StringAssert.Contains(migration, "CREATE TABLE governance.SourceApplyDryRunReceipt");
        StringAssert.Contains(migration, "TR_SourceApplyDryRunReceipt_ValidateInsert");
        StringAssert.Contains(migration, "TR_SourceApplyDryRunReceipt_BlockUpdateDelete");
        StringAssert.Contains(migration, "DENY INSERT, UPDATE, DELETE ON OBJECT::governance.SourceApplyDryRunReceipt");
        StringAssert.Contains(manifest, "migrate_source_apply_dry_run_receipt.sql");
        StringAssert.Contains(inventory, "database.migrate-source-apply-dry-run-receipt");
        StringAssert.Contains(inventory, "runtime.source-apply-dry-run-receipt-store");
        StringAssert.Contains(verifier, "governance.SourceApplyDryRunReceipt table");
        StringAssert.Contains(receipt, "PR202 files the rehearsal report. It does not claim the launch happened.");
        StringAssert.Contains(receipt, "dry-run receipt store only");
    }

    [TestMethod]
    public void SourceApplyDryRunReceiptStore_StaticBoundaryAvoidsExecutorsAndApiRuntime()
    {
        foreach (var token in new[] { "Process.Start", "File.WriteAllText", "File.Delete", "Directory.CreateDirectory", "IHostedService", "BackgroundService", "ControllerBase", "WebApplication", "memory promoted" })
            AssertNoProductionToken(token);
    }

    private static string[] Codes(SourceApplyDryRunReceipt receipt) =>
        SourceApplyDryRunReceiptValidation.Validate(receipt).Issues.Select(issue => issue.Code).ToArray();

    private static SourceApplyDryRunReceipt ValidReceipt(string suffix = "valid") => new()
    {
        SourceApplyDryRunReceiptId = Guid.NewGuid(),
        ProjectId = Guid.NewGuid(),
        SourceApplyDryRunRequestId = Guid.NewGuid(),
        SourceApplyDryRunRequestHash = $"sha256:dry-run-request-{suffix}",
        DryRunSatisfied = true,
        DryRunResultHash = $"sha256:dry-run-result-{suffix}",
        SourceApplyRequestId = Guid.NewGuid(),
        SourceApplyRequestHash = $"sha256:source-apply-request-{suffix}",
        SourceApplyGateEvaluationId = Guid.NewGuid(),
        SourceApplyGateEvaluationHash = $"sha256:source-apply-gate-{suffix}",
        PatchArtifactId = Guid.NewGuid(),
        PatchHash = $"sha256:patch-{suffix}",
        ChangeSetHash = $"sha256:change-set-{suffix}",
        RollbackSupportReceiptId = Guid.NewGuid(),
        RollbackSupportReceiptHash = $"sha256:rollback-support-{suffix}",
        SourceBaselineHash = $"sha256:source-baseline-{suffix}",
        WorkspaceBoundaryHash = $"sha256:workspace-boundary-{suffix}",
        ExpectedBranch = $"main-{suffix}",
        ExpectedCleanWorktreeHash = $"sha256:clean-worktree-{suffix}",
        FileResults = [ValidFileResult(suffix)],
        CreatedAtUtc = CreatedAtUtc,
        ExpiresAtUtc = ExpiresAtUtc,
        SourceApplyDryRunReceiptHash = $"sha256:source-apply-dry-run-receipt-{suffix}",
        EvidenceReferences = [$"source-apply-request:{suffix}", $"gate:{suffix}", $"patch:{suffix}", $"rollback-support:{suffix}"],
        BoundaryMaxims = ["Dry-run receipt is evidence only.", "Source apply still requires separate human-approved execution."],
        Boundary = SourceApplyDryRunReceiptBoundaryText.Boundary
    };

    private static SourceApplyDryRunReceiptFileResult ValidFileResult(string suffix = "valid") => new()
    {
        Path = $"src/{suffix}/Widget.cs",
        OperationKind = SourceApplyRequestFileOperationKinds.ModifyFile,
        PatchArtifactChangeHash = $"sha256:patch-change-{suffix}",
        OperationHash = $"sha256:operation-{suffix}",
        ExpectedBeforeContentHash = $"sha256:before-{suffix}",
        ExpectedAfterContentHash = $"sha256:after-{suffix}",
        ObservedCurrentContentHash = $"sha256:before-{suffix}",
        PreconditionsSatisfied = true,
        WouldCreate = false,
        WouldModify = true,
        WouldDelete = false,
        WouldRename = false,
        WouldNoop = false,
        IssueCodes = [],
        FileResultHash = $"sha256:file-result-{suffix}"
    };

    private static void AssertSingle(SourceApplyDryRunReceipt expected, IReadOnlyList<SourceApplyDryRunReceipt> actual)
    {
        Assert.AreEqual(1, actual.Count);
        AssertReceipt(expected, actual[0]);
    }

    private static void AssertReceipt(SourceApplyDryRunReceipt expected, SourceApplyDryRunReceipt? actual)
    {
        Assert.IsNotNull(actual);
        Assert.AreEqual(expected.SourceApplyDryRunReceiptId, actual.SourceApplyDryRunReceiptId);
        Assert.AreEqual(expected.ProjectId, actual.ProjectId);
        Assert.AreEqual(expected.SourceApplyDryRunReceiptHash, actual.SourceApplyDryRunReceiptHash);
        Assert.AreEqual(expected.DryRunSatisfied, actual.DryRunSatisfied);
        Assert.AreEqual(expected.SourceApplyRequestId, actual.SourceApplyRequestId);
        Assert.AreEqual(expected.SourceApplyGateEvaluationId, actual.SourceApplyGateEvaluationId);
        Assert.AreEqual(expected.PatchArtifactId, actual.PatchArtifactId);
        Assert.AreEqual(expected.RollbackSupportReceiptId, actual.RollbackSupportReceiptId);
        Assert.AreEqual(expected.CreatedAtUtc, actual.CreatedAtUtc);
        Assert.AreEqual(expected.ExpiresAtUtc, actual.ExpiresAtUtc);
        CollectionAssert.AreEqual(expected.EvidenceReferences.ToArray(), actual.EvidenceReferences.ToArray());
        CollectionAssert.AreEqual(expected.BoundaryMaxims.ToArray(), actual.BoundaryMaxims.ToArray());
        Assert.AreEqual(expected.Boundary, actual.Boundary);
        Assert.AreEqual(expected.FileResults.Count, actual.FileResults.Count);
        Assert.AreEqual(expected.FileResults[0].FileResultHash, actual.FileResults[0].FileResultHash);
    }

    private static string DirectInsertSql() =>
        @"INSERT INTO governance.SourceApplyDryRunReceipt
          (SourceApplyDryRunReceiptId, ProjectId, SourceApplyDryRunRequestId, SourceApplyDryRunRequestHash, DryRunSatisfied, DryRunResultHash, SourceApplyRequestId, SourceApplyRequestHash, SourceApplyGateEvaluationId, SourceApplyGateEvaluationHash, PatchArtifactId, PatchHash, ChangeSetHash, RollbackSupportReceiptId, RollbackSupportReceiptHash, SourceBaselineHash, WorkspaceBoundaryHash, ExpectedBranch, ExpectedCleanWorktreeHash, FileResultsJson, CreatedAtUtc, ExpiresAtUtc, SourceApplyDryRunReceiptHash, EvidenceReferencesJson, BoundaryMaximsJson, BoundaryText)
          VALUES (NEWID(), NEWID(), NEWID(), CONCAT(N'sha256:dry-run-request-', @suffix), 1, CONCAT(N'sha256:dry-run-result-', @suffix), NEWID(), CONCAT(N'sha256:source-apply-request-', @suffix), NEWID(), CONCAT(N'sha256:gate-', @suffix), NEWID(), CONCAT(N'sha256:patch-', @suffix), CONCAT(N'sha256:change-set-', @suffix), NEWID(), CONCAT(N'sha256:rollback-', @suffix), CONCAT(N'sha256:baseline-', @suffix), CONCAT(N'sha256:workspace-', @suffix), CONCAT(N'main-', @suffix), CONCAT(N'sha256:clean-', @suffix), @fileResults, SYSUTCDATETIME(), DATEADD(day, 1, SYSUTCDATETIME()), CONCAT(N'sha256:source-apply-dry-run-receipt-', @suffix), N'[""gate:evidence""]', N'[""dry-run evidence only""]', @boundaryText)";

    private static SqlParameter[] DirectInsertParameters(string suffix, string boundaryText) =>
    [
        new SqlParameter("@suffix", suffix),
        new SqlParameter("@fileResults", JsonSerializer.Serialize(new[] { ValidFileResult($"direct-{suffix}") })),
        new SqlParameter("@boundaryText", boundaryText)
    ];

    private async Task ApplySqlFileAsync(params string[] pathParts)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(new[] { RepoRoot() }.Concat(pathParts).ToArray()));
        foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(batch))
                await connection.ExecuteAsync(batch);
        }
    }

    private async Task DropSourceApplyDryRunReceiptAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            IF OBJECT_ID(N'governance.usp_SourceApplyDryRunReceipt_Save', N'P') IS NOT NULL DROP PROCEDURE governance.usp_SourceApplyDryRunReceipt_Save;
            IF OBJECT_ID(N'governance.usp_SourceApplyDryRunReceipt_Get', N'P') IS NOT NULL DROP PROCEDURE governance.usp_SourceApplyDryRunReceipt_Get;
            IF OBJECT_ID(N'governance.usp_SourceApplyDryRunReceipt_GetByReceiptHash', N'P') IS NOT NULL DROP PROCEDURE governance.usp_SourceApplyDryRunReceipt_GetByReceiptHash;
            IF OBJECT_ID(N'governance.usp_SourceApplyDryRunReceipt_ListBySourceApplyRequest', N'P') IS NOT NULL DROP PROCEDURE governance.usp_SourceApplyDryRunReceipt_ListBySourceApplyRequest;
            IF OBJECT_ID(N'governance.usp_SourceApplyDryRunReceipt_ListBySourceApplyGateEvaluation', N'P') IS NOT NULL DROP PROCEDURE governance.usp_SourceApplyDryRunReceipt_ListBySourceApplyGateEvaluation;
            IF OBJECT_ID(N'governance.usp_SourceApplyDryRunReceipt_ListByPatchArtifact', N'P') IS NOT NULL DROP PROCEDURE governance.usp_SourceApplyDryRunReceipt_ListByPatchArtifact;
            IF OBJECT_ID(N'governance.usp_SourceApplyDryRunReceipt_ListByRollbackSupportReceipt', N'P') IS NOT NULL DROP PROCEDURE governance.usp_SourceApplyDryRunReceipt_ListByRollbackSupportReceipt;
            IF OBJECT_ID(N'governance.TR_SourceApplyDryRunReceipt_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER governance.TR_SourceApplyDryRunReceipt_ValidateInsert;
            IF OBJECT_ID(N'governance.TR_SourceApplyDryRunReceipt_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_SourceApplyDryRunReceipt_BlockUpdateDelete;
            IF OBJECT_ID(N'governance.SourceApplyDryRunReceipt', N'U') IS NOT NULL DROP TABLE governance.SourceApplyDryRunReceipt;
            """);
    }

    private async Task AssertSqlFailsAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await AssertThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
    }

    private static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action) where TException : Exception
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
            Path.Combine(root, "Database", "migrate_source_apply_dry_run_receipt.sql"),
            Path.Combine(root, "IronDev.Core", "Governance", "SourceApplyDryRunReceipt.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "SourceApplyDryRunReceiptValidation.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "ISourceApplyDryRunReceiptStore.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlSourceApplyDryRunReceiptStore.cs")
        ];
    }

    private static string SqlMigrationPath() => Path.Combine(RepoRoot(), "Database", "migrate_source_apply_dry_run_receipt.sql");

    private static string ReceiptPath() => Path.Combine(RepoRoot(), "Docs", "receipts", "PR202_SOURCE_APPLY_RECEIPT_STORE.md");

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}





