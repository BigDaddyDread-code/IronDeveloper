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
[TestCategory("PatchArtifactStore")]
[TestCategory("RealDatabasePatchArtifactStoreSmoke")]
public sealed class PatchArtifactStoreTests : IntegrationTestBase
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 6, 16, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAtUtc = new(2026, 6, 17, 15, 0, 0, TimeSpan.Zero);
    private SqlPatchArtifactStore _store = default!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropPatchArtifactAsync();
        await ApplySqlFileAsync("Database", "migrate_patch_artifact.sql");

        _store = new SqlPatchArtifactStore(ServiceProvider.GetRequiredService<IDbConnectionFactory>());
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        await DropPatchArtifactAsync();
        await base.TestCleanup();
    }

    [TestMethod]
    public async Task PatchArtifactStore_CanInsertAndReadPatchArtifact()
    {
        var artifact = ValidArtifact();

        await _store.SaveAsync(artifact);
        var read = await _store.GetAsync(artifact.ProjectId, artifact.PatchArtifactId);

        Assert.IsNotNull(read);
        AssertArtifact(artifact, read);
    }

    [TestMethod]
    public async Task PatchArtifactStore_RejectsInvalidPatchArtifactShape()
    {
        foreach (var invalid in new[]
        {
            ValidArtifact() with { DryRunReceiptHash = " " },
            ValidArtifact() with { DryRunAuditHash = " " },
            ValidArtifact() with { PolicySatisfactionHash = " " },
            ValidArtifact() with { SubjectHash = " " },
            ValidArtifact() with { SourceBaselineHash = " " },
            ValidArtifact() with { WorkspaceBoundaryHash = " " },
            ValidArtifact() with { ValidationPlanHash = " " },
            ValidArtifact() with { PatchHash = " " },
            ValidArtifact() with { ChangeSetHash = " " },
            ValidArtifact() with { FileChanges = [] },
            ValidArtifact() with { EvidenceReferences = [] },
            ValidArtifact() with { BoundaryMaxims = [] }
        })
        {
            await AssertThrowsAsync<ArgumentException>(() => _store.SaveAsync(invalid));
        }

        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM governance.PatchArtifact"));
    }

    [TestMethod]
    public async Task PatchArtifactStore_IsProjectScoped()
    {
        var artifactId = Guid.NewGuid();
        var firstProject = Guid.NewGuid();
        var secondProject = Guid.NewGuid();
        var first = ValidArtifact("scope-a") with { PatchArtifactId = artifactId, ProjectId = firstProject };
        var second = ValidArtifact("scope-b") with { ProjectId = secondProject };

        await _store.SaveAsync(first);
        await _store.SaveAsync(second);

        Assert.IsNotNull(await _store.GetAsync(firstProject, artifactId));
        Assert.IsNull(await _store.GetAsync(secondProject, artifactId));
    }

    [TestMethod]
    public async Task PatchArtifactStore_CanListByDryRunReceiptHash()
    {
        var projectId = Guid.NewGuid();
        var hash = "sha256:shared-dry-run-receipt";
        var matching = ValidArtifact("receipt-a") with { ProjectId = projectId, DryRunReceiptHash = hash };
        var otherHash = ValidArtifact("receipt-b") with { ProjectId = projectId, DryRunReceiptHash = "sha256:other-receipt" };
        var otherProject = ValidArtifact("receipt-c") with { ProjectId = Guid.NewGuid(), DryRunReceiptHash = hash };

        await SaveAllAsync(matching, otherHash, otherProject);

        var results = await _store.ListByDryRunReceiptHashAsync(projectId, hash);

        CollectionAssert.AreEqual(new[] { matching.PatchArtifactId }, results.Select(result => result.PatchArtifactId).ToArray());
    }

    [TestMethod]
    public async Task PatchArtifactStore_CanListByDryRunAuditHash()
    {
        var projectId = Guid.NewGuid();
        var hash = "sha256:shared-dry-run-audit";
        var matching = ValidArtifact("audit-a") with { ProjectId = projectId, DryRunAuditHash = hash };
        var otherHash = ValidArtifact("audit-b") with { ProjectId = projectId, DryRunAuditHash = "sha256:other-audit" };
        var otherProject = ValidArtifact("audit-c") with { ProjectId = Guid.NewGuid(), DryRunAuditHash = hash };

        await SaveAllAsync(matching, otherHash, otherProject);

        var results = await _store.ListByDryRunAuditHashAsync(projectId, hash);

        CollectionAssert.AreEqual(new[] { matching.PatchArtifactId }, results.Select(result => result.PatchArtifactId).ToArray());
    }

    [TestMethod]
    public async Task PatchArtifactStore_CanListByControlledDryRunRequest()
    {
        var projectId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var matching = ValidArtifact("request-a") with { ProjectId = projectId, ControlledDryRunRequestId = requestId };
        var otherRequest = ValidArtifact("request-b") with { ProjectId = projectId, ControlledDryRunRequestId = Guid.NewGuid() };
        var otherProject = ValidArtifact("request-c") with { ProjectId = Guid.NewGuid(), ControlledDryRunRequestId = requestId };

        await SaveAllAsync(matching, otherRequest, otherProject);

        var results = await _store.ListByControlledDryRunRequestAsync(projectId, requestId);

        CollectionAssert.AreEqual(new[] { matching.PatchArtifactId }, results.Select(result => result.PatchArtifactId).ToArray());
    }

    [TestMethod]
    public async Task PatchArtifactStore_CanListBySubject()
    {
        var projectId = Guid.NewGuid();
        var matching = ValidArtifact("subject-a") with { ProjectId = projectId, SubjectKind = "PatchProposal", SubjectId = "proposal-1" };
        var otherSubject = ValidArtifact("subject-b") with { ProjectId = projectId, SubjectKind = "PatchProposal", SubjectId = "proposal-2" };
        var otherProject = ValidArtifact("subject-c") with { ProjectId = Guid.NewGuid(), SubjectKind = "PatchProposal", SubjectId = "proposal-1" };

        await SaveAllAsync(matching, otherSubject, otherProject);

        var results = await _store.ListBySubjectAsync(projectId, "PatchProposal", "proposal-1");

        CollectionAssert.AreEqual(new[] { matching.PatchArtifactId }, results.Select(result => result.PatchArtifactId).ToArray());
    }

    [TestMethod]
    public async Task PatchArtifactStore_CanListByPatchHash()
    {
        var projectId = Guid.NewGuid();
        var hash = "sha256:shared-patch";
        var matching = ValidArtifact("patch-a") with { ProjectId = projectId, PatchHash = hash };
        var otherHash = ValidArtifact("patch-b") with { ProjectId = projectId, PatchHash = "sha256:other-patch" };
        var otherProject = ValidArtifact("patch-c") with { ProjectId = Guid.NewGuid(), PatchHash = hash };

        await SaveAllAsync(matching, otherHash, otherProject);

        var results = await _store.ListByPatchHashAsync(projectId, hash);

        CollectionAssert.AreEqual(new[] { matching.PatchArtifactId }, results.Select(result => result.PatchArtifactId).ToArray());
    }

    [TestMethod]
    public async Task PatchArtifactStore_CanListBySourceBaselineHash()
    {
        var projectId = Guid.NewGuid();
        var hash = "sha256:shared-source-baseline";
        var matching = ValidArtifact("baseline-a") with { ProjectId = projectId, SourceBaselineHash = hash };
        var otherHash = ValidArtifact("baseline-b") with { ProjectId = projectId, SourceBaselineHash = "sha256:other-baseline" };
        var otherProject = ValidArtifact("baseline-c") with { ProjectId = Guid.NewGuid(), SourceBaselineHash = hash };

        await SaveAllAsync(matching, otherHash, otherProject);

        var results = await _store.ListBySourceBaselineHashAsync(projectId, hash);

        CollectionAssert.AreEqual(new[] { matching.PatchArtifactId }, results.Select(result => result.PatchArtifactId).ToArray());
    }

    [TestMethod]
    public async Task PatchArtifactStore_RejectsDuplicatePatchArtifactId()
    {
        var artifact = ValidArtifact();
        var duplicateId = ValidArtifact("duplicate-id") with { PatchArtifactId = artifact.PatchArtifactId };

        await _store.SaveAsync(artifact);

        await AssertThrowsAsync<SqlException>(() => _store.SaveAsync(duplicateId));
    }

    [TestMethod]
    public async Task PatchArtifactStore_RejectsDuplicatePatchHashWithinProject()
    {
        var projectId = Guid.NewGuid();
        var first = ValidArtifact("hash-a") with { ProjectId = projectId, PatchHash = "sha256:duplicate-patch" };
        var second = ValidArtifact("hash-b") with { ProjectId = projectId, PatchHash = "sha256:duplicate-patch" };
        var otherProject = ValidArtifact("hash-c") with { ProjectId = Guid.NewGuid(), PatchHash = "sha256:duplicate-patch" };

        await _store.SaveAsync(first);
        await AssertThrowsAsync<SqlException>(() => _store.SaveAsync(second));
        await _store.SaveAsync(otherProject);
    }

    [TestMethod]
    public void PatchArtifactStore_DoesNotExposeUpdateOrDelete()
    {
        var forbidden = new[] { "Update", "Delete", "Remove", "Overwrite", "Upsert" };
        var methods = typeof(IPatchArtifactStore)
            .GetMethods()
            .Concat(typeof(SqlPatchArtifactStore).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

        foreach (var method in methods)
        foreach (var token in forbidden)
        {
            Assert.IsFalse(method.Name.Contains(token, StringComparison.OrdinalIgnoreCase), $"Unexpected method: {method.Name}");
        }

        StringAssert.Contains(File.ReadAllText(SqlMigrationPath()), "TR_PatchArtifact_BlockUpdateDelete");
    }

    [TestMethod]
    public void PatchArtifactStore_UsesPatchArtifactValidationBeforeSave()
    {
        var store = File.ReadAllText(StoreSourcePath());

        StringAssert.Contains(store, "PatchArtifactValidation.Validate");
    }

    [TestMethod]
    public async Task PatchArtifactStore_PreservesFileChanges()
    {
        var artifact = ValidArtifact() with
        {
            FileChanges =
            [
                CreateChange("src/new-file.cs"),
                ModifyChange("src/existing-file.cs"),
                DeleteChange("src/old-file.cs"),
                RenameChange("src/new-name.cs", "src/old-name.cs")
            ]
        };

        await _store.SaveAsync(artifact);
        var read = await _store.GetAsync(artifact.ProjectId, artifact.PatchArtifactId);

        Assert.IsNotNull(read);
        Assert.AreEqual(4, read.FileChanges.Count);
        for (var index = 0; index < artifact.FileChanges.Count; index++)
        {
            AssertFileChange(artifact.FileChanges[index], read.FileChanges[index]);
        }
    }

    [TestMethod]
    public async Task PatchArtifactStore_PreservesEvidenceAndBoundary()
    {
        var artifact = ValidArtifact() with
        {
            EvidenceReferences = ["dry-run-receipt:one", "policy-satisfaction:two"],
            BoundaryMaxims = ["Patch artifact storage records proposed change packages only.", "Patch artifact storage does not spend dry-run receipts."]
        };

        await _store.SaveAsync(artifact);
        var read = await _store.GetAsync(artifact.ProjectId, artifact.PatchArtifactId);

        Assert.IsNotNull(read);
        CollectionAssert.AreEqual(artifact.EvidenceReferences.ToArray(), read.EvidenceReferences.ToArray());
        CollectionAssert.AreEqual(artifact.BoundaryMaxims.ToArray(), read.BoundaryMaxims.ToArray());
        Assert.AreEqual(artifact.Boundary, read.Boundary);
    }

    [TestMethod]
    public async Task PatchArtifactStore_BlocksUnsafeDirectSqlMaterialAndMutation()
    {
        var artifact = ValidArtifact();
        await _store.SaveAsync(artifact);

        await AssertSqlFailsAsync("UPDATE governance.PatchArtifact SET PatchHash = N'sha256:changed' WHERE PatchArtifactId = @id", new SqlParameter("@id", artifact.PatchArtifactId));
        await AssertSqlFailsAsync("DELETE FROM governance.PatchArtifact WHERE PatchArtifactId = @id", new SqlParameter("@id", artifact.PatchArtifactId));
        await AssertSqlFailsAsync(DirectInsertSql(), DirectInsertParameters("direct-raw", JsonSerializer.Serialize(new[] { CreateChange("src/raw.cs") with { NormalizedDiff = "raw prompt leaked" } }), JsonSerializer.Serialize(artifact.EvidenceReferences), JsonSerializer.Serialize(artifact.BoundaryMaxims), artifact.Boundary));
        await AssertSqlFailsAsync(DirectInsertSql(), DirectInsertParameters("direct-authority", JsonSerializer.Serialize(artifact.FileChanges), "[\"source applied\"]", JsonSerializer.Serialize(artifact.BoundaryMaxims), artifact.Boundary));
    }

    [TestMethod]
    public void PatchArtifactStore_MigrationAndInventoryAreRegistered()
    {
        var manifest = File.ReadAllText(Path.Combine(RepoRoot(), "Database", "migrations.json"));
        var inventory = File.ReadAllText(Path.Combine(RepoRoot(), "Database", "sql-inventory.json"));
        var verifier = File.ReadAllText(Path.Combine(RepoRoot(), "Database", "verify-migrations.ps1"));
        var sql = File.ReadAllText(SqlMigrationPath());

        StringAssert.Contains(manifest, "Database/migrate_patch_artifact.sql");
        StringAssert.Contains(inventory, "database.migrate-patch-artifact");
        StringAssert.Contains(inventory, "runtime.patch-artifact-store");
        StringAssert.Contains(verifier, "governance.PatchArtifact table");
        StringAssert.Contains(verifier, "governance.usp_PatchArtifact_Save procedure");
        StringAssert.Contains(sql, "governance.PatchArtifact");
        StringAssert.Contains(sql, "governance.usp_PatchArtifact_Save");
        StringAssert.Contains(sql, "governance.usp_PatchArtifact_Get");
        StringAssert.Contains(sql, "governance.usp_PatchArtifact_ListByDryRunReceiptHash");
        StringAssert.Contains(sql, "governance.usp_PatchArtifact_ListByDryRunAuditHash");
        StringAssert.Contains(sql, "governance.usp_PatchArtifact_ListByControlledDryRunRequest");
        StringAssert.Contains(sql, "governance.usp_PatchArtifact_ListBySubject");
        StringAssert.Contains(sql, "governance.usp_PatchArtifact_ListByPatchHash");
        StringAssert.Contains(sql, "governance.usp_PatchArtifact_ListBySourceBaselineHash");
        StringAssert.Contains(sql, "TR_PatchArtifact_ValidateInsert");
        StringAssert.Contains(sql, "TR_PatchArtifact_BlockUpdateDelete");
    }

    [TestMethod]
    public void PatchArtifactStore_DoesNotCreatePatchArtifact()
    {
        foreach (var token in new[]
        {
            "CreatePatchArtifactAsync",
            "PatchArtifactCreator",
            "BuildPatchArtifactFromDryRun",
            "IControlledDryRunReceiptStore",
            "IControlledDryRunExecutor"
        })
        {
            AssertNoProductionToken(token);
        }
    }

    [TestMethod]
    public void PatchArtifactStore_DoesNotApplySourceOrContinueWorkflow()
    {
        foreach (var token in new[]
        {
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
    public void PatchArtifactStore_DoesNotAddApiCliUi()
    {
        foreach (var file in Pr188ChangedFiles())
        {
            var relative = Path.GetRelativePath(RepoRoot(), file);
            foreach (var token in new[] { "Controller", "Program.cs", "Cli", "Tauri", "UI" })
            {
                Assert.IsFalse(relative.Contains(token, StringComparison.OrdinalIgnoreCase), $"PR188 must not add {token}: {relative}");
            }
        }
    }

    [TestMethod]
    public void PatchArtifactStore_DoesNotCallModelsAgentsMemoryRetrieval()
    {
        foreach (var token in new[]
        {
            "LLM",
            "model call",
            "AgentDispatch",
            "ToolExecution",
            "PromoteMemory",
            "ActivateRetrieval"
        })
        {
            AssertNoProductionToken(token);
        }
    }

    [TestMethod]
    public void PatchArtifactStore_ReceiptStatesBoundary()
    {
        var receipt = File.ReadAllText(ReceiptPath());

        foreach (var statement in new[]
        {
            "PR188 adds the Patch Artifact Store.",
            "This PR persists supplied PatchArtifact records.",
            "This PR does not create patch artifacts.",
            "This PR does not derive patch artifacts from dry-run receipts.",
            "This PR does not apply source.",
            "This PR does not execute rollback.",
            "This PR does not continue workflow.",
            "This PR does not approve release.",
            "This PR does not add API.",
            "This PR does not add CLI.",
            "This PR does not add UI.",
            "Persisted patch artifact is not source apply.",
            "Persisted patch artifact is not rollback.",
            "Persisted patch artifact is not workflow continuation.",
            "Persisted patch artifact is not release readiness.",
            "Persisted patch artifact does not authorize source mutation by itself.",
            "Patch artifact storage records proposed change packages only.",
            "Patch artifact storage does not create proposed change packages.",
            "Patch artifact storage does not spend dry-run receipts.",
            "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate",
            "The next Block S target is Patch Artifact Read API.",
            "PR189 - Patch Artifact Read API",
            "PR188 puts the package in the vault. It does not ship or apply it."
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    private async Task SaveAllAsync(params PatchArtifact[] artifacts)
    {
        foreach (var artifact in artifacts)
        {
            await _store.SaveAsync(artifact);
        }
    }

    private static PatchArtifact ValidArtifact(string suffix = "main") => new()
    {
        PatchArtifactId = Guid.NewGuid(),
        ProjectId = Guid.NewGuid(),
        PatchArtifactKind = "UnifiedDiffPackage",
        ControlledDryRunRequestId = Guid.NewGuid(),
        DryRunExecutionAuditId = Guid.NewGuid(),
        DryRunAuditHash = $"sha256:dry-run-audit-{suffix}",
        DryRunReceiptHash = $"sha256:dry-run-receipt-{suffix}",
        PolicySatisfactionId = Guid.NewGuid(),
        PolicySatisfactionHash = $"sha256:policy-satisfaction-{suffix}",
        SubjectKind = "PatchProposal",
        SubjectId = $"patch-proposal-{suffix}",
        SubjectHash = $"sha256:subject-{suffix}",
        SourceSnapshotReference = $"source-snapshot:{suffix}",
        SourceBaselineHash = $"sha256:source-baseline-{suffix}",
        WorkspaceBoundaryHash = $"sha256:workspace-boundary-{suffix}",
        ValidationPlanId = $"validation-plan-{suffix}",
        ValidationPlanHash = $"sha256:validation-plan-{suffix}",
        PatchHash = $"sha256:patch-{suffix}",
        ChangeSetHash = $"sha256:change-set-{suffix}",
        FileChanges = [ModifyChange($"src/{suffix}.cs")],
        CreatedAtUtc = CreatedAtUtc,
        ExpiresAtUtc = ExpiresAtUtc,
        EvidenceReferences = [$"controlled-dry-run-receipt:{suffix}"],
        BoundaryMaxims = ["Patch artifact storage records proposed change packages only."],
        Boundary = PatchArtifactBoundaryText.Boundary
    };

    private static PatchArtifactFileChange CreateChange(string path) => new()
    {
        Path = path,
        ChangeKind = "Create",
        AfterContentHash = $"sha256:after-{path}",
        DiffHash = $"sha256:diff-{path}",
        NormalizedDiff = $"create {path}",
        IsBinary = false
    };

    private static PatchArtifactFileChange ModifyChange(string path) => new()
    {
        Path = path,
        ChangeKind = "Modify",
        BeforeContentHash = $"sha256:before-{path}",
        AfterContentHash = $"sha256:after-{path}",
        DiffHash = $"sha256:diff-{path}",
        NormalizedDiff = $"modify {path}",
        IsBinary = false
    };

    private static PatchArtifactFileChange DeleteChange(string path) => new()
    {
        Path = path,
        ChangeKind = "Delete",
        BeforeContentHash = $"sha256:before-{path}",
        DiffHash = $"sha256:diff-{path}",
        NormalizedDiff = $"delete {path}",
        IsBinary = false
    };

    private static PatchArtifactFileChange RenameChange(string path, string previousPath) => new()
    {
        Path = path,
        PreviousPath = previousPath,
        ChangeKind = "Rename",
        BeforeContentHash = $"sha256:before-{previousPath}",
        AfterContentHash = $"sha256:after-{path}",
        DiffHash = $"sha256:diff-{path}",
        NormalizedDiff = $"rename {previousPath} to {path}",
        IsBinary = false
    };

    private static void AssertArtifact(PatchArtifact expected, PatchArtifact actual)
    {
        Assert.AreEqual(expected.PatchArtifactId, actual.PatchArtifactId);
        Assert.AreEqual(expected.ProjectId, actual.ProjectId);
        Assert.AreEqual(expected.PatchArtifactKind, actual.PatchArtifactKind);
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
        Assert.AreEqual(expected.ValidationPlanId, actual.ValidationPlanId);
        Assert.AreEqual(expected.ValidationPlanHash, actual.ValidationPlanHash);
        Assert.AreEqual(expected.PatchHash, actual.PatchHash);
        Assert.AreEqual(expected.ChangeSetHash, actual.ChangeSetHash);
        Assert.AreEqual(expected.CreatedAtUtc, actual.CreatedAtUtc);
        Assert.AreEqual(expected.ExpiresAtUtc, actual.ExpiresAtUtc);
        Assert.AreEqual(expected.Boundary, actual.Boundary);
        CollectionAssert.AreEqual(expected.EvidenceReferences.ToArray(), actual.EvidenceReferences.ToArray());
        CollectionAssert.AreEqual(expected.BoundaryMaxims.ToArray(), actual.BoundaryMaxims.ToArray());
        Assert.AreEqual(expected.FileChanges.Count, actual.FileChanges.Count);
        AssertFileChange(expected.FileChanges[0], actual.FileChanges[0]);
    }

    private static void AssertFileChange(PatchArtifactFileChange expected, PatchArtifactFileChange actual)
    {
        Assert.AreEqual(expected.Path, actual.Path);
        Assert.AreEqual(expected.PreviousPath, actual.PreviousPath);
        Assert.AreEqual(expected.ChangeKind, actual.ChangeKind);
        Assert.AreEqual(expected.BeforeContentHash, actual.BeforeContentHash);
        Assert.AreEqual(expected.AfterContentHash, actual.AfterContentHash);
        Assert.AreEqual(expected.DiffHash, actual.DiffHash);
        Assert.AreEqual(expected.NormalizedDiff, actual.NormalizedDiff);
        Assert.AreEqual(expected.IsBinary, actual.IsBinary);
    }

    private static string DirectInsertSql() =>
        @"INSERT INTO governance.PatchArtifact
          (PatchArtifactId, ProjectId, PatchArtifactKind, ControlledDryRunRequestId, DryRunExecutionAuditId, DryRunAuditHash, DryRunReceiptHash, PolicySatisfactionId, PolicySatisfactionHash, SubjectKind, SubjectId, SubjectHash, SourceSnapshotReference, SourceBaselineHash, WorkspaceBoundaryHash, ValidationPlanId, ValidationPlanHash, PatchHash, ChangeSetHash, FileChangesJson, EvidenceReferencesJson, BoundaryMaximsJson, BoundaryText, CreatedAtUtc, ExpiresAtUtc)
          VALUES (NEWID(), NEWID(), N'UnifiedDiffPackage', NEWID(), NEWID(), CONCAT(N'sha256:direct-audit-', @subjectId), CONCAT(N'sha256:direct-receipt-', @subjectId), NEWID(), N'sha256:policy-direct', N'PatchProposal', @subjectId, N'sha256:subject-direct', N'source-snapshot:direct', N'sha256:source-baseline-direct', N'sha256:workspace-direct', N'validation-plan-direct', N'sha256:validation-direct', CONCAT(N'sha256:patch-', @subjectId), CONCAT(N'sha256:change-set-', @subjectId), @fileChangesJson, @evidenceReferencesJson, @boundaryMaximsJson, @boundaryText, SYSUTCDATETIME(), DATEADD(day, 1, SYSUTCDATETIME()))";

    private static SqlParameter[] DirectInsertParameters(string subjectId, string fileChangesJson, string evidenceJson, string boundaryJson, string boundaryText) =>
    [
        new SqlParameter("@subjectId", subjectId),
        new SqlParameter("@fileChangesJson", fileChangesJson),
        new SqlParameter("@evidenceReferencesJson", evidenceJson),
        new SqlParameter("@boundaryMaximsJson", boundaryJson),
        new SqlParameter("@boundaryText", boundaryText)
    ];

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

    private async Task DropPatchArtifactAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            IF OBJECT_ID(N'governance.usp_PatchArtifact_Save', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PatchArtifact_Save;
            IF OBJECT_ID(N'governance.usp_PatchArtifact_Get', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PatchArtifact_Get;
            IF OBJECT_ID(N'governance.usp_PatchArtifact_ListByDryRunReceiptHash', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PatchArtifact_ListByDryRunReceiptHash;
            IF OBJECT_ID(N'governance.usp_PatchArtifact_ListByDryRunAuditHash', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PatchArtifact_ListByDryRunAuditHash;
            IF OBJECT_ID(N'governance.usp_PatchArtifact_ListByControlledDryRunRequest', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PatchArtifact_ListByControlledDryRunRequest;
            IF OBJECT_ID(N'governance.usp_PatchArtifact_ListBySubject', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PatchArtifact_ListBySubject;
            IF OBJECT_ID(N'governance.usp_PatchArtifact_ListByPatchHash', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PatchArtifact_ListByPatchHash;
            IF OBJECT_ID(N'governance.usp_PatchArtifact_ListBySourceBaselineHash', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PatchArtifact_ListBySourceBaselineHash;
            IF OBJECT_ID(N'governance.TR_PatchArtifact_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER governance.TR_PatchArtifact_ValidateInsert;
            IF OBJECT_ID(N'governance.TR_PatchArtifact_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_PatchArtifact_BlockUpdateDelete;
            IF OBJECT_ID(N'governance.PatchArtifact', N'U') IS NOT NULL DROP TABLE governance.PatchArtifact;
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
        foreach (var file in Pr188ProductionFiles())
        {
            Assert.IsFalse(File.ReadAllText(file).Contains(token, StringComparison.Ordinal), $"Unexpected production token {token} in {file}.");
        }
    }

    private static string[] Pr188ProductionFiles()
    {
        var root = RepoRoot();
        return
        [
            Path.Combine(root, "Database", "migrate_patch_artifact.sql"),
            Path.Combine(root, "IronDev.Core", "Governance", "IPatchArtifactStore.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlPatchArtifactStore.cs")
        ];
    }

    private static string[] Pr188ChangedFiles()
    {
        var root = RepoRoot();
        return
        [
            Path.Combine(root, "Database", "migrate_patch_artifact.sql"),
            Path.Combine(root, "Database", "migrations.json"),
            Path.Combine(root, "Database", "sql-inventory.json"),
            Path.Combine(root, "Database", "verify-migrations.ps1"),
            Path.Combine(root, "IronDev.Core", "Governance", "IPatchArtifactStore.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlPatchArtifactStore.cs"),
            Path.Combine(root, "Docs", "receipts", "PR188_PATCH_ARTIFACT_STORE.md"),
            Path.Combine(root, "IronDev.IntegrationTests", "Governance", "PatchArtifactStoreTests.cs")
        ];
    }

    private static string SqlMigrationPath() =>
        Path.Combine(RepoRoot(), "Database", "migrate_patch_artifact.sql");

    private static string StoreSourcePath() =>
        Path.Combine(RepoRoot(), "IronDev.Infrastructure", "Governance", "SqlPatchArtifactStore.cs");

    private static string ReceiptPath() =>
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR188_PATCH_ARTIFACT_STORE.md");

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
