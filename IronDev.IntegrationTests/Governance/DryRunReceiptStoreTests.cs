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

[TestCategory("RequiresRealDatabase")]
[TestCategory("LongRunning")]
[TestClass]
[TestCategory("DryRunReceiptStore")]
[TestCategory("RealDatabaseDryRunReceiptStoreSmoke")]
public sealed class DryRunReceiptStoreTests : IntegrationTestBase
{
    private static readonly DateTimeOffset StartedAtUtc = new(2026, 6, 16, 11, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CompletedAtUtc = new(2026, 6, 16, 11, 3, 0, TimeSpan.Zero);
    private SqlControlledDryRunReceiptStore _store = default!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropControlledDryRunReceiptAsync();
        await ApplySqlFileAsync("Database", "migrate_controlled_dry_run_receipt.sql");

        _store = new SqlControlledDryRunReceiptStore(ServiceProvider.GetRequiredService<IDbConnectionFactory>());
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        await DropControlledDryRunReceiptAsync();
        await base.TestCleanup();
    }

    [TestMethod]
    public async Task DryRunReceiptStore_CanInsertAndReadReceipt()
    {
        var audit = ValidAudit();

        await _store.SaveAsync(audit);
        var read = await _store.GetAsync(audit.ProjectId, audit.DryRunExecutionAuditId);

        Assert.IsNotNull(read);
        AssertAudit(audit, read);
    }

    [TestMethod]
    public async Task DryRunReceiptStore_RejectsInvalidAuditShape()
    {
        foreach (var invalid in new[]
        {
            ValidAudit() with { PolicySatisfactionHash = " " },
            ValidAudit() with { SubjectHash = " " },
            ValidAudit() with { WorkspaceBoundaryHash = " " },
            ValidAudit() with { ValidationPlanHash = " " },
            ValidAudit() with { ExecutionReportHash = " " },
            ValidAudit() with { AuditHash = " " },
            ValidAudit() with { CommandAudits = [] },
            ValidAudit() with { EvidenceReferences = [] },
            ValidAudit() with { BoundaryMaxims = [] }
        })
        {
            await AssertThrowsAsync<ArgumentException>(() => _store.SaveAsync(invalid));
        }

        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM governance.ControlledDryRunReceipt"));
    }

    [TestMethod]
    public async Task DryRunReceiptStore_IsProjectScoped()
    {
        var auditId = Guid.NewGuid();
        var firstProject = Guid.NewGuid();
        var secondProject = Guid.NewGuid();
        var first = ValidAudit() with { DryRunExecutionAuditId = auditId, ProjectId = firstProject, AuditHash = "sha256:audit-first" };
        var second = ValidAudit() with { DryRunExecutionAuditId = Guid.NewGuid(), ProjectId = secondProject, AuditHash = "sha256:audit-second" };

        await _store.SaveAsync(first);
        await _store.SaveAsync(second);

        Assert.IsNotNull(await _store.GetAsync(firstProject, auditId));
        Assert.IsNull(await _store.GetAsync(secondProject, auditId));
    }

    [TestMethod]
    public async Task DryRunReceiptStore_CanListByRequest()
    {
        var projectId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var matchingFirst = ValidAudit("request-a") with { ProjectId = projectId, ControlledDryRunRequestId = requestId, CompletedAtUtc = CompletedAtUtc.AddMinutes(1) };
        var matchingSecond = ValidAudit("request-b") with { ProjectId = projectId, ControlledDryRunRequestId = requestId, CompletedAtUtc = CompletedAtUtc.AddMinutes(2) };
        var otherRequest = ValidAudit("request-c") with { ProjectId = projectId, ControlledDryRunRequestId = Guid.NewGuid() };
        var otherProject = ValidAudit("request-d") with { ProjectId = Guid.NewGuid(), ControlledDryRunRequestId = requestId };

        await SaveAllAsync(matchingFirst, matchingSecond, otherRequest, otherProject);

        var results = await _store.ListByRequestAsync(projectId, requestId);

        CollectionAssert.AreEquivalent(new[] { matchingFirst.DryRunExecutionAuditId, matchingSecond.DryRunExecutionAuditId }, results.Select(result => result.DryRunExecutionAuditId).ToArray());
    }

    [TestMethod]
    public async Task DryRunReceiptStore_CanListByPolicySatisfaction()
    {
        var projectId = Guid.NewGuid();
        var policySatisfactionId = Guid.NewGuid();
        var matchingFirst = ValidAudit("policy-a") with { ProjectId = projectId, PolicySatisfactionId = policySatisfactionId };
        var matchingSecond = ValidAudit("policy-b") with { ProjectId = projectId, PolicySatisfactionId = policySatisfactionId };
        var otherPolicy = ValidAudit("policy-c") with { ProjectId = projectId, PolicySatisfactionId = Guid.NewGuid() };
        var otherProject = ValidAudit("policy-d") with { ProjectId = Guid.NewGuid(), PolicySatisfactionId = policySatisfactionId };

        await SaveAllAsync(matchingFirst, matchingSecond, otherPolicy, otherProject);

        var results = await _store.ListByPolicySatisfactionAsync(projectId, policySatisfactionId);

        CollectionAssert.AreEquivalent(new[] { matchingFirst.DryRunExecutionAuditId, matchingSecond.DryRunExecutionAuditId }, results.Select(result => result.DryRunExecutionAuditId).ToArray());
    }

    [TestMethod]
    public async Task DryRunReceiptStore_CanListBySubject()
    {
        var projectId = Guid.NewGuid();
        var matchingFirst = ValidAudit("subject-a") with { ProjectId = projectId, SubjectKind = "patch-proposal", SubjectId = "subject-1" };
        var matchingSecond = ValidAudit("subject-b") with { ProjectId = projectId, SubjectKind = "patch-proposal", SubjectId = "subject-1" };
        var otherSubject = ValidAudit("subject-c") with { ProjectId = projectId, SubjectKind = "patch-proposal", SubjectId = "subject-2" };
        var otherProject = ValidAudit("subject-d") with { ProjectId = Guid.NewGuid(), SubjectKind = "patch-proposal", SubjectId = "subject-1" };

        await SaveAllAsync(matchingFirst, matchingSecond, otherSubject, otherProject);

        var results = await _store.ListBySubjectAsync(projectId, "patch-proposal", "subject-1");

        CollectionAssert.AreEquivalent(new[] { matchingFirst.DryRunExecutionAuditId, matchingSecond.DryRunExecutionAuditId }, results.Select(result => result.DryRunExecutionAuditId).ToArray());
    }

    [TestMethod]
    public async Task DryRunReceiptStore_CanListByAuditHash()
    {
        var projectId = Guid.NewGuid();
        var auditHash = "sha256:shared-audit-hash";
        var matching = ValidAudit("audit-a") with { ProjectId = projectId, AuditHash = auditHash };
        var otherHash = ValidAudit("audit-b") with { ProjectId = projectId, AuditHash = "sha256:other-audit-hash" };
        var otherProject = ValidAudit("audit-c") with { ProjectId = Guid.NewGuid(), AuditHash = auditHash };

        await SaveAllAsync(matching, otherHash, otherProject);

        var results = await _store.ListByAuditHashAsync(projectId, auditHash);

        CollectionAssert.AreEqual(new[] { matching.DryRunExecutionAuditId }, results.Select(result => result.DryRunExecutionAuditId).ToArray());
    }

    [TestMethod]
    public async Task DryRunReceiptStore_RejectsDuplicateAuditId()
    {
        var audit = ValidAudit();
        var duplicateId = ValidAudit("duplicate-id") with { DryRunExecutionAuditId = audit.DryRunExecutionAuditId };

        await _store.SaveAsync(audit);

        await AssertThrowsAsync<SqlException>(() => _store.SaveAsync(duplicateId));
    }

    [TestMethod]
    public async Task DryRunReceiptStore_RejectsDuplicateAuditHashWithinProject()
    {
        var projectId = Guid.NewGuid();
        var first = ValidAudit("hash-a") with { ProjectId = projectId, AuditHash = "sha256:duplicate-audit-hash" };
        var second = ValidAudit("hash-b") with { ProjectId = projectId, AuditHash = "sha256:duplicate-audit-hash" };
        var otherProject = ValidAudit("hash-c") with { ProjectId = Guid.NewGuid(), AuditHash = "sha256:duplicate-audit-hash" };

        await _store.SaveAsync(first);
        await AssertThrowsAsync<SqlException>(() => _store.SaveAsync(second));
        await _store.SaveAsync(otherProject);
    }

    [TestMethod]
    public void DryRunReceiptStore_DoesNotExposeUpdateOrDelete()
    {
        var forbidden = new[] { "Update", "Delete", "Remove", "Overwrite", "Upsert" };
        var methods = typeof(IControlledDryRunReceiptStore)
            .GetMethods()
            .Concat(typeof(SqlControlledDryRunReceiptStore).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

        foreach (var method in methods)
        foreach (var token in forbidden)
        {
            Assert.IsFalse(method.Name.Contains(token, StringComparison.OrdinalIgnoreCase), $"Unexpected method: {method.Name}");
        }

        StringAssert.Contains(File.ReadAllText(SqlMigrationPath()), "TR_ControlledDryRunReceipt_BlockUpdateDelete");
    }

    [TestMethod]
    public void DryRunReceiptStore_UsesAuditValidationBeforeSave()
    {
        var store = File.ReadAllText(StoreSourcePath());

        StringAssert.Contains(store, "ControlledDryRunExecutionAuditValidation.Validate");
    }

    [TestMethod]
    public async Task DryRunReceiptStore_PreservesCommandAudits()
    {
        var audit = ValidAudit() with
        {
            CommandAudits =
            [
                ValidCommand("command-1") with { StandardOutputSummary = "first command passed" },
                ValidCommand("command-2") with { ExitCode = 1, TimedOut = true, StandardErrorSummary = "second command failed safely" }
            ]
        };

        await _store.SaveAsync(audit);
        var read = await _store.GetAsync(audit.ProjectId, audit.DryRunExecutionAuditId);

        Assert.IsNotNull(read);
        Assert.AreEqual(2, read.CommandAudits.Count);
        AssertCommand(audit.CommandAudits[0], read.CommandAudits[0]);
        AssertCommand(audit.CommandAudits[1], read.CommandAudits[1]);
    }

    [TestMethod]
    public async Task DryRunReceiptStore_PreservesEvidenceAndBoundary()
    {
        var audit = ValidAudit() with
        {
            EvidenceReferences = ["controlled-dry-run-request:one", "policy-satisfaction:two"],
            BoundaryMaxims = ["Persisted dry-run receipt is not dry-run execution.", "Dry-run receipt storage records evidence only."]
        };

        await _store.SaveAsync(audit);
        var read = await _store.GetAsync(audit.ProjectId, audit.DryRunExecutionAuditId);

        Assert.IsNotNull(read);
        CollectionAssert.AreEqual(audit.EvidenceReferences.ToArray(), read.EvidenceReferences.ToArray());
        CollectionAssert.AreEqual(audit.BoundaryMaxims.ToArray(), read.BoundaryMaxims.ToArray());
        Assert.AreEqual(audit.Boundary, read.Boundary);
    }

    [TestMethod]
    public async Task DryRunReceiptStore_BlocksUnsafeDirectSqlMaterialAndMutation()
    {
        var audit = ValidAudit();
        await _store.SaveAsync(audit);

        await AssertSqlFailsAsync("UPDATE governance.ControlledDryRunReceipt SET AuditHash = N'sha256:changed' WHERE DryRunExecutionAuditId = @id", new SqlParameter("@id", audit.DryRunExecutionAuditId));
        await AssertSqlFailsAsync("DELETE FROM governance.ControlledDryRunReceipt WHERE DryRunExecutionAuditId = @id", new SqlParameter("@id", audit.DryRunExecutionAuditId));
        await AssertSqlFailsAsync(DirectInsertSql(), DirectInsertParameters("direct-raw", CommandJson("rawPrompt leaked"), JsonSerializer.Serialize(audit.EvidenceReferences), JsonSerializer.Serialize(audit.BoundaryMaxims), audit.Boundary));
        await AssertSqlFailsAsync(DirectInsertSql(), DirectInsertParameters("direct-authority", CommandJson("safe summary"), "[\"source applied\"]", JsonSerializer.Serialize(audit.BoundaryMaxims), audit.Boundary));
    }

    [TestMethod]
    public void DryRunReceiptStore_MigrationAndInventoryAreRegistered()
    {
        var manifest = File.ReadAllText(Path.Combine(RepoRoot(), "Database", "migrations.json"));
        var inventory = File.ReadAllText(Path.Combine(RepoRoot(), "Database", "sql-inventory.json"));
        var verifier = File.ReadAllText(Path.Combine(RepoRoot(), "Database", "verify-migrations.ps1"));
        var sql = File.ReadAllText(SqlMigrationPath());

        StringAssert.Contains(manifest, "Database/migrate_controlled_dry_run_receipt.sql");
        StringAssert.Contains(inventory, "database.migrate-controlled-dry-run-receipt");
        StringAssert.Contains(inventory, "runtime.controlled-dry-run-receipt-store");
        StringAssert.Contains(verifier, "governance.ControlledDryRunReceipt table");
        StringAssert.Contains(verifier, "governance.usp_ControlledDryRunReceipt_Save procedure");
        StringAssert.Contains(sql, "governance.ControlledDryRunReceipt");
        StringAssert.Contains(sql, "governance.usp_ControlledDryRunReceipt_Save");
        StringAssert.Contains(sql, "governance.usp_ControlledDryRunReceipt_Get");
        StringAssert.Contains(sql, "governance.usp_ControlledDryRunReceipt_ListByRequest");
        StringAssert.Contains(sql, "governance.usp_ControlledDryRunReceipt_ListByPolicySatisfaction");
        StringAssert.Contains(sql, "governance.usp_ControlledDryRunReceipt_ListBySubject");
        StringAssert.Contains(sql, "governance.usp_ControlledDryRunReceipt_ListByAuditHash");
        StringAssert.Contains(sql, "TR_ControlledDryRunReceipt_ValidateInsert");
        StringAssert.Contains(sql, "TR_ControlledDryRunReceipt_BlockUpdateDelete");
    }

    [TestMethod]
    public void DryRunReceiptStore_DoesNotExecuteDryRun()
    {
        foreach (var token in new[]
        {
            "IControlledDryRunExecutor",
            "DisposableWorkspaceControlledDryRunExecutor",
            "RunDryRunAsync",
            "ControlledDryRunProcessRunner"
        })
        {
            AssertNoProductionToken(token);
        }
    }

    [TestMethod]
    public void DryRunReceiptStore_DoesNotCreatePatchArtifactOrApplySource()
    {
        foreach (var token in new[]
        {
            "CreatePatchArtifactAsync",
            "PatchArtifactStore",
            "PatchArtifactId = Guid.NewGuid",
            "ApplySourceAsync",
            "SourceApplyService",
            "ControlledSourceApply"
        })
        {
            AssertNoProductionToken(token);
        }
    }

    [TestMethod]
    public void DryRunReceiptStore_DoesNotContinueWorkflowOrApproveRelease()
    {
        foreach (var token in new[]
        {
            "ContinueWorkflowAsync",
            "ApproveReleaseAsync",
            "ReleaseReady = true",
            "CanApproveRelease = true"
        })
        {
            AssertNoProductionToken(token);
        }
    }

    [TestMethod]
    public void DryRunReceiptStore_DoesNotAddApiCliUi()
    {
        foreach (var file in Pr184ChangedFiles())
        {
            var relative = Path.GetRelativePath(RepoRoot(), file);
            foreach (var token in new[] { "Controller", "Program.cs", "Cli", "Tauri", "UI" })
            {
                Assert.IsFalse(relative.Contains(token, StringComparison.OrdinalIgnoreCase), $"PR184 must not add {token}: {relative}");
            }
        }
    }

    [TestMethod]
    public void DryRunReceiptStore_DoesNotCallModelsAgentsMemoryRetrieval()
    {
        foreach (var token in new[]
        {
            "LLM",
            "model call",
            "AgentDispatch",
            "PromoteMemory",
            "ActivateRetrieval",
            "ToolExecution"
        })
        {
            AssertNoProductionToken(token);
        }
    }

    [TestMethod]
    public void DryRunReceiptStore_ReceiptStatesBoundary()
    {
        var receipt = File.ReadAllText(ReceiptPath());

        foreach (var statement in new[]
        {
            "PR184 adds the Dry-run Receipt Store.",
            "This PR persists supplied controlled dry-run execution audit receipts.",
            "This PR does not execute dry-runs.",
            "This PR does not create dry-run audits from executor output.",
            "This PR does not create patch artifacts.",
            "This PR does not apply source.",
            "This PR does not execute rollback.",
            "This PR does not continue workflow.",
            "This PR does not approve release.",
            "This PR does not add API.",
            "This PR does not add CLI.",
            "This PR does not add UI.",
            "Persisted dry-run receipt is not dry-run execution.",
            "Persisted dry-run receipt is not patch artifact creation.",
            "Persisted dry-run receipt is not source apply.",
            "Persisted dry-run receipt is not rollback.",
            "Persisted dry-run receipt is not workflow continuation.",
            "Persisted dry-run receipt is not release readiness.",
            "Persisted dry-run receipt does not authorize source mutation by itself.",
            "Dry-run receipt storage records evidence only.",
            "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate",
            "The next Block R target is Dry-run Receipt Read API.",
            "PR185 - Dry-run Receipt Read API",
            "PR184 puts the cage-run receipt in the vault. It does not package or spend it."
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    private async Task SaveAllAsync(params ControlledDryRunExecutionAudit[] audits)
    {
        foreach (var audit in audits)
        {
            await _store.SaveAsync(audit);
        }
    }

    private static ControlledDryRunExecutionAudit ValidAudit(string suffix = "main") => new()
    {
        DryRunExecutionAuditId = Guid.NewGuid(),
        ProjectId = Guid.NewGuid(),
        ControlledDryRunRequestId = Guid.NewGuid(),
        PolicySatisfactionId = Guid.NewGuid(),
        PolicySatisfactionHash = $"sha256:policy-satisfaction-{suffix}",
        SubjectKind = "PatchProposal",
        SubjectId = $"patch-proposal-{suffix}",
        SubjectHash = $"sha256:subject-{suffix}",
        WorkspaceId = $"workspace-{suffix}",
        WorkspaceKind = "disposable workspace",
        WorkspaceBoundaryHash = $"sha256:workspace-boundary-{suffix}",
        SourceSnapshotReference = $"source-snapshot:{suffix}",
        ValidationPlanId = $"validation-plan-{suffix}",
        ValidationPlanHash = $"sha256:validation-plan-{suffix}",
        StartedAtUtc = StartedAtUtc,
        CompletedAtUtc = CompletedAtUtc,
        DryRunCompleted = true,
        DryRunSucceeded = true,
        ExecutionReportHash = $"sha256:execution-report-{suffix}",
        AuditHash = $"sha256:audit-{suffix}",
        CommandAudits = [ValidCommand($"command-{suffix}")],
        EvidenceReferences = [$"controlled-dry-run-request:{suffix}"],
        BoundaryMaxims = ["Dry-run receipt storage records evidence only."],
        Boundary = ControlledDryRunExecutionAuditBoundaryText.Boundary
    };

    private static ControlledDryRunCommandAudit ValidCommand(string commandId) => new()
    {
        CommandId = commandId,
        WorkingDirectory = "workspace/write-root",
        Executable = "dotnet",
        CommandHash = $"sha256:{commandId}",
        ExitCode = 0,
        TimedOut = false,
        StandardOutputSummaryHash = $"sha256:stdout-{commandId}",
        StandardErrorSummaryHash = $"sha256:stderr-{commandId}",
        StandardOutputSummary = "tests passed",
        StandardErrorSummary = "none"
    };

    private static void AssertAudit(ControlledDryRunExecutionAudit expected, ControlledDryRunExecutionAudit actual)
    {
        Assert.AreEqual(expected.DryRunExecutionAuditId, actual.DryRunExecutionAuditId);
        Assert.AreEqual(expected.ProjectId, actual.ProjectId);
        Assert.AreEqual(expected.ControlledDryRunRequestId, actual.ControlledDryRunRequestId);
        Assert.AreEqual(expected.PolicySatisfactionId, actual.PolicySatisfactionId);
        Assert.AreEqual(expected.PolicySatisfactionHash, actual.PolicySatisfactionHash);
        Assert.AreEqual(expected.SubjectKind, actual.SubjectKind);
        Assert.AreEqual(expected.SubjectId, actual.SubjectId);
        Assert.AreEqual(expected.SubjectHash, actual.SubjectHash);
        Assert.AreEqual(expected.WorkspaceId, actual.WorkspaceId);
        Assert.AreEqual(expected.WorkspaceKind, actual.WorkspaceKind);
        Assert.AreEqual(expected.WorkspaceBoundaryHash, actual.WorkspaceBoundaryHash);
        Assert.AreEqual(expected.SourceSnapshotReference, actual.SourceSnapshotReference);
        Assert.AreEqual(expected.ValidationPlanId, actual.ValidationPlanId);
        Assert.AreEqual(expected.ValidationPlanHash, actual.ValidationPlanHash);
        Assert.AreEqual(expected.StartedAtUtc, actual.StartedAtUtc);
        Assert.AreEqual(expected.CompletedAtUtc, actual.CompletedAtUtc);
        Assert.AreEqual(expected.DryRunCompleted, actual.DryRunCompleted);
        Assert.AreEqual(expected.DryRunSucceeded, actual.DryRunSucceeded);
        Assert.AreEqual(expected.ExecutionReportHash, actual.ExecutionReportHash);
        Assert.AreEqual(expected.AuditHash, actual.AuditHash);
        Assert.AreEqual(expected.Boundary, actual.Boundary);
        CollectionAssert.AreEqual(expected.EvidenceReferences.ToArray(), actual.EvidenceReferences.ToArray());
        CollectionAssert.AreEqual(expected.BoundaryMaxims.ToArray(), actual.BoundaryMaxims.ToArray());
        Assert.AreEqual(expected.CommandAudits.Count, actual.CommandAudits.Count);
        AssertCommand(expected.CommandAudits[0], actual.CommandAudits[0]);
    }

    private static void AssertCommand(ControlledDryRunCommandAudit expected, ControlledDryRunCommandAudit actual)
    {
        Assert.AreEqual(expected.CommandId, actual.CommandId);
        Assert.AreEqual(expected.WorkingDirectory, actual.WorkingDirectory);
        Assert.AreEqual(expected.Executable, actual.Executable);
        Assert.AreEqual(expected.CommandHash, actual.CommandHash);
        Assert.AreEqual(expected.ExitCode, actual.ExitCode);
        Assert.AreEqual(expected.TimedOut, actual.TimedOut);
        Assert.AreEqual(expected.StandardOutputSummaryHash, actual.StandardOutputSummaryHash);
        Assert.AreEqual(expected.StandardErrorSummaryHash, actual.StandardErrorSummaryHash);
        Assert.AreEqual(expected.StandardOutputSummary, actual.StandardOutputSummary);
        Assert.AreEqual(expected.StandardErrorSummary, actual.StandardErrorSummary);
    }

    private static string DirectInsertSql() =>
        @"INSERT INTO governance.ControlledDryRunReceipt
          (DryRunExecutionAuditId, ProjectId, ControlledDryRunRequestId, PolicySatisfactionId, PolicySatisfactionHash, SubjectKind, SubjectId, SubjectHash, WorkspaceId, WorkspaceKind, WorkspaceBoundaryHash, SourceSnapshotReference, ValidationPlanId, ValidationPlanHash, StartedAtUtc, CompletedAtUtc, DryRunCompleted, DryRunSucceeded, ExecutionReportHash, AuditHash, CommandAuditsJson, EvidenceReferencesJson, BoundaryMaximsJson, BoundaryText)
          VALUES (NEWID(), NEWID(), NEWID(), NEWID(), N'sha256:policy-direct', N'PatchProposal', @subjectId, N'sha256:subject-direct', N'workspace-direct', N'disposable workspace', N'sha256:workspace-direct', N'source-snapshot:direct', N'validation-plan-direct', N'sha256:validation-direct', SYSUTCDATETIME(), DATEADD(minute, 1, SYSUTCDATETIME()), 1, 1, N'sha256:execution-report-direct', CONCAT(N'sha256:audit-', @subjectId), @commandAuditsJson, @evidenceReferencesJson, @boundaryMaximsJson, @boundaryText)";

    private static SqlParameter[] DirectInsertParameters(string subjectId, string commandJson, string evidenceJson, string boundaryJson, string boundaryText) =>
    [
        new SqlParameter("@subjectId", subjectId),
        new SqlParameter("@commandAuditsJson", commandJson),
        new SqlParameter("@evidenceReferencesJson", evidenceJson),
        new SqlParameter("@boundaryMaximsJson", boundaryJson),
        new SqlParameter("@boundaryText", boundaryText)
    ];

    private static string CommandJson(string outputSummary) =>
        JsonSerializer.Serialize(new[]
        {
            new
            {
                commandId = "command-direct",
                workingDirectory = "workspace/write-root",
                executable = "dotnet",
                commandHash = "sha256:command-direct",
                exitCode = 0,
                timedOut = false,
                standardOutputSummaryHash = "sha256:stdout-direct",
                standardErrorSummaryHash = "sha256:stderr-direct",
                standardOutputSummary = outputSummary,
                standardErrorSummary = "none"
            }
        });

    private async Task ApplySqlFileAsync(params string[] pathParts)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(new[] { RepoRoot() }.Concat(pathParts).ToArray()));
        foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(batch))
            {
                continue;
            }

            await connection.ExecuteAsync(batch);
        }
    }

    private async Task DropControlledDryRunReceiptAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            IF OBJECT_ID(N'governance.usp_ControlledDryRunReceipt_Save', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ControlledDryRunReceipt_Save;
            IF OBJECT_ID(N'governance.usp_ControlledDryRunReceipt_Get', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ControlledDryRunReceipt_Get;
            IF OBJECT_ID(N'governance.usp_ControlledDryRunReceipt_ListByRequest', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ControlledDryRunReceipt_ListByRequest;
            IF OBJECT_ID(N'governance.usp_ControlledDryRunReceipt_ListByPolicySatisfaction', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ControlledDryRunReceipt_ListByPolicySatisfaction;
            IF OBJECT_ID(N'governance.usp_ControlledDryRunReceipt_ListBySubject', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ControlledDryRunReceipt_ListBySubject;
            IF OBJECT_ID(N'governance.usp_ControlledDryRunReceipt_ListByAuditHash', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ControlledDryRunReceipt_ListByAuditHash;
            IF OBJECT_ID(N'governance.TR_ControlledDryRunReceipt_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER governance.TR_ControlledDryRunReceipt_ValidateInsert;
            IF OBJECT_ID(N'governance.TR_ControlledDryRunReceipt_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_ControlledDryRunReceipt_BlockUpdateDelete;
            IF OBJECT_ID(N'governance.ControlledDryRunReceipt', N'U') IS NOT NULL DROP TABLE governance.ControlledDryRunReceipt;
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

    private static async Task AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        Assert.Fail($"Expected exception of type {typeof(TException).Name}.");
    }

    private static void AssertNoProductionToken(string token)
    {
        foreach (var file in Pr184ProductionFiles())
        {
            Assert.IsFalse(File.ReadAllText(file).Contains(token, StringComparison.Ordinal), $"Unexpected production token {token} in {file}.");
        }
    }

    private static string[] Pr184ProductionFiles()
    {
        var root = RepoRoot();
        return
        [
            Path.Combine(root, "Database", "migrate_controlled_dry_run_receipt.sql"),
            Path.Combine(root, "IronDev.Core", "Governance", "IControlledDryRunReceiptStore.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlControlledDryRunReceiptStore.cs")
        ];
    }

    private static string[] Pr184ChangedFiles()
    {
        var root = RepoRoot();
        return
        [
            Path.Combine(root, "Database", "migrate_controlled_dry_run_receipt.sql"),
            Path.Combine(root, "Database", "migrations.json"),
            Path.Combine(root, "Database", "sql-inventory.json"),
            Path.Combine(root, "Database", "verify-migrations.ps1"),
            Path.Combine(root, "IronDev.Core", "Governance", "IControlledDryRunReceiptStore.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlControlledDryRunReceiptStore.cs"),
            Path.Combine(root, "Docs", "receipts", "PR184_DRY_RUN_RECEIPT_STORE.md"),
            Path.Combine(root, "IronDev.IntegrationTests", "Governance", "DryRunReceiptStoreTests.cs")
        ];
    }

    private static string SqlMigrationPath() =>
        Path.Combine(RepoRoot(), "Database", "migrate_controlled_dry_run_receipt.sql");

    private static string StoreSourcePath() =>
        Path.Combine(RepoRoot(), "IronDev.Infrastructure", "Governance", "SqlControlledDryRunReceiptStore.cs");

    private static string ReceiptPath() =>
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR184_DRY_RUN_RECEIPT_STORE.md");

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