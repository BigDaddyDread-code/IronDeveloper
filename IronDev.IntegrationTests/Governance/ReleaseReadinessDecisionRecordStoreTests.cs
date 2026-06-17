using System.Diagnostics;
using System.Reflection;
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
[TestCategory("ReleaseReadinessDecisionRecordStore")]
[TestCategory("RealDatabaseReleaseReadinessDecisionRecordStoreSmoke")]
public sealed class ReleaseReadinessDecisionRecordStoreTests : IntegrationTestBase
{
    private static readonly DateTimeOffset DecidedAtUtc = new(2026, 6, 17, 10, 30, 0, TimeSpan.Zero);
    private SqlReleaseReadinessDecisionRecordStore _store = default!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropReleaseReadinessDecisionRecordAsync();
        await ApplySqlFileAsync("Database", "migrate_release_readiness_decision_record.sql");
        _store = new SqlReleaseReadinessDecisionRecordStore(ServiceProvider.GetRequiredService<IDbConnectionFactory>());
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        await DropReleaseReadinessDecisionRecordAsync();
        await base.TestCleanup();
    }

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordStore_SaveAndGetRoundTripsCompleteRecord()
    {
        var record = ValidRecord("roundtrip");

        await _store.SaveAsync(record);
        var read = await _store.GetAsync(record.ProjectId, record.ReleaseReadinessDecisionRecordId);

        AssertRecord(record, read);
    }

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordStore_GetByHashRoundTripsCompleteRecord()
    {
        var record = ValidRecord("hash");

        await _store.SaveAsync(record);
        var read = await _store.GetByRecordHashAsync(record.ProjectId, record.ReleaseReadinessDecisionRecordHash);

        AssertRecord(record, read);
    }

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordStore_ListByReleaseReadinessReportIsProjectScoped()
    {
        var projectId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var matching = Rehash(ValidRecord("report-a") with { ProjectId = projectId, ReleaseReadinessReportId = reportId });
        var otherProject = Rehash(ValidRecord("report-b") with { ProjectId = Guid.NewGuid(), ReleaseReadinessReportId = reportId });
        var otherReport = Rehash(ValidRecord("report-c") with { ProjectId = projectId, ReleaseReadinessReportId = Guid.NewGuid() });

        await SaveAllAsync(matching, otherProject, otherReport);

        AssertSingle(matching, await _store.ListByReleaseReadinessReportAsync(projectId, reportId));
    }

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordStore_ListByWorkflowRunIsProjectScoped()
    {
        var projectId = Guid.NewGuid();
        var workflowRunId = "workflow-run-list";
        var matching = Rehash(ValidRecord("run-a") with { ProjectId = projectId, WorkflowRunId = workflowRunId });
        var otherProject = Rehash(ValidRecord("run-b") with { ProjectId = Guid.NewGuid(), WorkflowRunId = workflowRunId });
        var otherRun = Rehash(ValidRecord("run-c") with { ProjectId = projectId, WorkflowRunId = "workflow-run-other" });

        await SaveAllAsync(matching, otherProject, otherRun);

        AssertSingle(matching, await _store.ListByWorkflowRunAsync(projectId, workflowRunId));
    }

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordStore_ListBySubjectIsProjectScoped()
    {
        var projectId = Guid.NewGuid();
        var subjectKind = "ReleasePackage";
        var subjectId = "release-package-list";
        var matching = Rehash(ValidRecord("subject-a") with { ProjectId = projectId, SubjectKind = subjectKind, SubjectId = subjectId });
        var otherProject = Rehash(ValidRecord("subject-b") with { ProjectId = Guid.NewGuid(), SubjectKind = subjectKind, SubjectId = subjectId });
        var otherSubject = Rehash(ValidRecord("subject-c") with { ProjectId = projectId, SubjectKind = subjectKind, SubjectId = "release-package-other" });

        await SaveAllAsync(matching, otherProject, otherSubject);

        AssertSingle(matching, await _store.ListBySubjectAsync(projectId, subjectKind, subjectId));
    }

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordStore_RejectsInvalidRecordBeforePersistence()
    {
        var invalid = ValidRecord("invalid") with { WorkflowRunId = " " };

        var ex = await AssertThrowsAsync<ArgumentException>(() => _store.SaveAsync(invalid));

        StringAssert.Contains(ex.Message, "WorkflowRunIdRequired");
        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM governance.ReleaseReadinessDecisionRecord"));
    }

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordStore_RejectsReleaseApprovalBeforePersistence()
        => await AssertInvalidAsync(
            Rehash(ValidRecord("release-approval") with { ReleaseApproved = true }),
            "ReleaseApprovedRejected");

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordStore_RejectsDeploymentApprovalBeforePersistence()
        => await AssertInvalidAsync(
            Rehash(ValidRecord("deployment-approval") with { DeploymentApproved = true }),
            "DeploymentApprovedRejected");

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordStore_RejectsMergeApprovalBeforePersistence()
        => await AssertInvalidAsync(
            Rehash(ValidRecord("merge-approval") with { MergeApproved = true }),
            "MergeApprovedRejected");

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordStore_RejectsReleaseExecutionBeforePersistence()
        => await AssertInvalidAsync(
            Rehash(ValidRecord("release-execution") with { ReleaseExecutedByDecision = true }),
            "ReleaseExecutedByDecisionRejected");

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordStore_RejectsSourceApplyRollbackWorkflowGitExecutionBeforePersistence()
    {
        await AssertInvalidAsync(Rehash(ValidRecord("source-apply") with { SourceApplyExecutedByDecision = true }), "SourceApplyExecutedByDecisionRejected");
        await AssertInvalidAsync(Rehash(ValidRecord("rollback") with { RollbackExecutedByDecision = true }), "RollbackExecutedByDecisionRejected");
        await AssertInvalidAsync(Rehash(ValidRecord("workflow") with { WorkflowMutatedByDecision = true }), "WorkflowMutatedByDecisionRejected");
        await AssertInvalidAsync(Rehash(ValidRecord("git") with { GitOperationExecutedByDecision = true }), "GitOperationExecutedByDecisionRejected");
    }

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordStore_RejectsMissingHumanReviewBeforePersistence()
        => await AssertInvalidAsync(
            Rehash(ValidRecord("missing-human-review") with { HumanReviewRequiredForReleaseApproval = false }),
            "HumanReviewRequiredForReleaseApprovalRequired");

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordStore_RejectsHashMismatchBeforePersistence()
        => await AssertInvalidAsync(
            ValidRecord("hash-mismatch") with { ReleaseReadinessDecisionRecordHash = H("different") },
            "ReleaseReadinessDecisionRecordHashMismatch");

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordStore_SaveSameRecordTwiceIsIdempotent()
    {
        var record = ValidRecord("idempotent");

        await _store.SaveAsync(record);
        await _store.SaveAsync(record);

        Assert.AreEqual(1, await ScalarAsync<int>(
            "SELECT COUNT(1) FROM governance.ReleaseReadinessDecisionRecord WHERE ProjectId = @projectId AND ReleaseReadinessDecisionRecordId = @id",
            new { projectId = record.ProjectId, id = record.ReleaseReadinessDecisionRecordId }));
    }

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordStore_SaveSameIdDifferentHashIsRejected()
    {
        var record = ValidRecord("conflict-a");
        var conflict = Rehash(ValidRecord("conflict-b") with { ProjectId = record.ProjectId, ReleaseReadinessDecisionRecordId = record.ReleaseReadinessDecisionRecordId });

        await _store.SaveAsync(record);

        await AssertThrowsAsync<SqlException>(() => _store.SaveAsync(conflict));
    }

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordStore_SaveSameHashDifferentIdIsRejected()
    {
        var record = ValidRecord("same-hash-a");
        await _store.SaveAsync(record);

        await AssertSqlFailsAsync(StoredProcedureSaveSql(), DirectProcedureParameters(record with { ReleaseReadinessDecisionRecordId = Guid.NewGuid() }));
    }

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordStore_SameHashDifferentProjectIsAllowedBySqlScope()
    {
        var record = ValidRecord("same-hash-different-project");
        await _store.SaveAsync(record);

        await ExecuteSqlAsync(StoredProcedureSaveSql(), DirectProcedureParameters(record with { ProjectId = Guid.NewGuid() }));

        Assert.AreEqual(2, await ScalarAsync<int>(
            "SELECT COUNT(1) FROM governance.ReleaseReadinessDecisionRecord WHERE ReleaseReadinessDecisionRecordHash = @hash",
            new { hash = record.ReleaseReadinessDecisionRecordHash }));
    }

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordStore_DirectSqlUpdateIsBlocked()
    {
        var record = ValidRecord("update");
        await _store.SaveAsync(record);

        await AssertSqlFailsAsync(
            "UPDATE governance.ReleaseReadinessDecisionRecord SET WorkflowRunId = N'changed' WHERE ProjectId = @projectId AND ReleaseReadinessDecisionRecordId = @id",
            new SqlParameter("@projectId", record.ProjectId),
            new SqlParameter("@id", record.ReleaseReadinessDecisionRecordId));
    }

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordStore_DirectSqlDeleteIsBlocked()
    {
        var record = ValidRecord("delete");
        await _store.SaveAsync(record);

        await AssertSqlFailsAsync(
            "DELETE FROM governance.ReleaseReadinessDecisionRecord WHERE ProjectId = @projectId AND ReleaseReadinessDecisionRecordId = @id",
            new SqlParameter("@projectId", record.ProjectId),
            new SqlParameter("@id", record.ReleaseReadinessDecisionRecordId));
    }

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordStore_DirectSqlInsertWithAuthorityClaimIsBlocked()
    {
        await AssertSqlFailsAsync(DirectInsertSql(), DirectInsertParameters("authority", "release approved"));
    }

    [TestMethod]
    public async Task ReleaseReadinessDecisionRecordStore_DirectSqlInsertWithPrivateRawMaterialIsBlocked()
    {
        await AssertSqlFailsAsync(DirectInsertSql(), DirectInsertParameters("private", "private reasoning leaked"));
    }

    [TestMethod]
    public void ReleaseReadinessDecisionRecordStore_RuntimeRoleCanExecuteSaveAndReadProcedures()
    {
        var sql = File.ReadAllText(SqlMigrationPath());

        StringAssert.Contains(sql, "GRANT EXECUTE ON OBJECT::governance.usp_ReleaseReadinessDecisionRecord_Save");
        StringAssert.Contains(sql, "GRANT EXECUTE ON OBJECT::governance.usp_ReleaseReadinessDecisionRecord_Get");
        StringAssert.Contains(sql, "GRANT EXECUTE ON OBJECT::governance.usp_ReleaseReadinessDecisionRecord_GetByHash");
        StringAssert.Contains(sql, "GRANT EXECUTE ON OBJECT::governance.usp_ReleaseReadinessDecisionRecord_ListByReleaseReadinessReport");
        StringAssert.Contains(sql, "GRANT EXECUTE ON OBJECT::governance.usp_ReleaseReadinessDecisionRecord_ListByWorkflowRun");
        StringAssert.Contains(sql, "GRANT EXECUTE ON OBJECT::governance.usp_ReleaseReadinessDecisionRecord_ListBySubject");
    }

    [TestMethod]
    public void ReleaseReadinessDecisionRecordStore_RuntimeRoleCannotInsertUpdateDeleteTableDirectly()
    {
        var sql = File.ReadAllText(SqlMigrationPath());

        StringAssert.Contains(sql, "DENY INSERT, UPDATE, DELETE ON OBJECT::governance.ReleaseReadinessDecisionRecord");
    }

    [TestMethod]
    public void ReleaseReadinessDecisionRecordStore_RuntimeRoleCannotAlterSchema()
    {
        var sql = File.ReadAllText(SqlMigrationPath());

        StringAssert.Contains(sql, "DENY ALTER ON SCHEMA::governance");
    }

    [TestMethod]
    public void ReleaseReadinessDecisionRecordStore_InterfaceIsAppendOnlyReadSurface()
    {
        var names = typeof(IReleaseReadinessDecisionRecordStore)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .OrderBy(name => name)
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                nameof(IReleaseReadinessDecisionRecordStore.GetAsync),
                nameof(IReleaseReadinessDecisionRecordStore.GetByRecordHashAsync),
                nameof(IReleaseReadinessDecisionRecordStore.ListByReleaseReadinessReportAsync),
                nameof(IReleaseReadinessDecisionRecordStore.ListBySubjectAsync),
                nameof(IReleaseReadinessDecisionRecordStore.ListByWorkflowRunAsync),
                nameof(IReleaseReadinessDecisionRecordStore.SaveAsync),
            },
            names);

        foreach (var forbidden in new[] { "Update", "Delete", "Approve", "Decide", "Execute", "Continue", "Deploy", "Merge" })
        {
            Assert.IsFalse(names.Any(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)), $"Unexpected method token: {forbidden}");
        }
    }

    [TestMethod]
    public void ReleaseReadinessDecisionRecordStore_MigrationManifestInventoryVerifierAndReceiptAreUpdated()
    {
        var manifest = File.ReadAllText(Path.Combine(RepoRoot(), "Database", "migrations.json"));
        var inventory = File.ReadAllText(Path.Combine(RepoRoot(), "Database", "sql-inventory.json"));
        var verifier = File.ReadAllText(Path.Combine(RepoRoot(), "Database", "verify-migrations.ps1"));
        var sql = File.ReadAllText(SqlMigrationPath());
        var receipt = File.ReadAllText(ReceiptPath());

        StringAssert.Contains(manifest, "Database/migrate_release_readiness_decision_record.sql");
        StringAssert.Contains(inventory, "database.migrate-release-readiness-decision-record");
        StringAssert.Contains(inventory, "runtime.release-readiness-decision-record-store");
        StringAssert.Contains(verifier, "governance.ReleaseReadinessDecisionRecord table");
        StringAssert.Contains(sql, "CREATE TABLE governance.ReleaseReadinessDecisionRecord");
        StringAssert.Contains(sql, "TR_ReleaseReadinessDecisionRecord_ValidateInsert");
        StringAssert.Contains(sql, "TR_ReleaseReadinessDecisionRecord_BlockUpdateDelete");
        StringAssert.Contains(receipt, "PR218 adds release-readiness decision record storage only.");
        StringAssert.Contains(receipt, "Release readiness store is not release readiness.");
    }

    [TestMethod]
    public void ReleaseReadinessDecisionRecordStore_DoesNotAddApiCliUiRuntimeGateOrExecutionSurface()
    {
        foreach (var token in new[]
        {
            "HttpPost",
            "HttpPut",
            "HttpPatch",
            "HttpDelete",
            "ControllerBase",
            "ReleaseReadinessGate",
            "ReleaseReadinessEvaluator",
            "ReleaseExecutionService",
            "DeploymentExecutionService",
            "MergeExecutionService",
            "Process.Start",
            "ProcessStartInfo",
            "git commit",
            "git push",
            "git merge",
            "gh pr",
            "ControlledSourceApplyExecutor",
            "ControlledRollbackExecutor",
            "GovernedWorkflowContinuationService",
            "AgentDispatch",
            "ModelProvider",
            "ToolInvoker",
            "PromoteMemory",
            "ActivateRetrieval",
            "Weaviate",
            "Embedding",
        })
        {
            AssertNoProductionToken(token);
        }
    }

    [TestMethod]
    public void ReleaseReadinessDecisionRecordStore_ReceiptStatesBoundary()
    {
        var receipt = File.ReadAllText(ReceiptPath());

        foreach (var statement in new[]
        {
            "PR218 persists validated ReleaseReadinessDecisionRecord evidence.",
            "PR218 does not run a release-readiness gate.",
            "PR218 does not decide release readiness.",
            "PR218 does not approve release.",
            "PR218 does not approve deployment.",
            "PR218 does not approve merge.",
            "PR218 does not execute release.",
            "PR218 does not execute source apply.",
            "PR218 does not execute rollback.",
            "PR218 does not continue workflow.",
            "Release readiness store is not release readiness.",
            "Stored ReadyEvidenceSatisfied does not mean release approved.",
            "Human review remains required for release approval, deployment, and merge.",
            "PR218 puts the release-readiness decision receipt in the vault. It does not decide readiness.",
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    private async Task AssertInvalidAsync(ReleaseReadinessDecisionRecord invalid, string expectedCode)
    {
        var ex = await AssertThrowsAsync<ArgumentException>(() => _store.SaveAsync(invalid));
        StringAssert.Contains(ex.Message, expectedCode);
        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM governance.ReleaseReadinessDecisionRecord"));
    }

    private async Task SaveAllAsync(params ReleaseReadinessDecisionRecord[] records)
    {
        foreach (var record in records)
        {
            await _store.SaveAsync(record);
        }
    }

    private static ReleaseReadinessDecisionRecord ValidRecord(string suffix = "main") =>
        Rehash(new ReleaseReadinessDecisionRecord
        {
            ReleaseReadinessDecisionRecordId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            ReleaseReadinessReportId = Guid.NewGuid(),
            ReleaseReadinessReportHash = H($"release-readiness-report-{suffix}"),
            WorkflowRunId = $"workflow-run-{suffix}",
            WorkflowStepId = $"workflow-step-{suffix}",
            SubjectKind = "ReleasePackage",
            SubjectId = $"release-package-{suffix}",
            SubjectHash = H($"release-package-{suffix}"),
            DecisionStatus = ReleaseReadinessDecisionStatuses.ReadyEvidenceSatisfied,
            ReleaseReadinessEvidenceSatisfied = true,
            ReleaseApproved = false,
            DeploymentApproved = false,
            MergeApproved = false,
            SourceApplyExecutedByDecision = false,
            RollbackExecutedByDecision = false,
            WorkflowMutatedByDecision = false,
            GitOperationExecutedByDecision = false,
            ReleaseExecutedByDecision = false,
            HumanReviewRequiredForReleaseApproval = true,
            HumanReviewRequiredForDeployment = true,
            HumanReviewRequiredForMerge = true,
            Reasons =
            [
                new ReleaseReadinessDecisionReason
                {
                    Code = "ReportComplete",
                    Severity = ReleaseReadinessDecisionReasonSeverities.Info,
                    Field = "ReleaseReadinessReport",
                    Message = "Release readiness report evidence was complete.",
                },
                new ReleaseReadinessDecisionReason
                {
                    Code = "HumanReviewRequiredForReleaseApproval",
                    Severity = ReleaseReadinessDecisionReasonSeverities.Warning,
                    Field = "ReleaseApproval",
                    Message = "Human review remains required for release approval.",
                },
            ],
            EvidenceReferences = [$"release-readiness-report:{suffix}", $"workflow-transition-record:{suffix}"],
            BoundaryMaxims = ["Release readiness store is not release approval.", "Human review remains required."],
            DecidedAtUtc = DecidedAtUtc.AddMinutes(Math.Abs(suffix.GetHashCode(StringComparison.Ordinal)) % 1000),
            ReleaseReadinessDecisionRecordHash = H($"placeholder-{suffix}"),
            Boundary = ReleaseReadinessDecisionRecordBoundaryText.Boundary,
        });

    private static ReleaseReadinessDecisionRecord Rehash(ReleaseReadinessDecisionRecord record) =>
        record with { ReleaseReadinessDecisionRecordHash = ReleaseReadinessDecisionRecordHashing.ComputeHash(record) };

    private static string H(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void AssertSingle(ReleaseReadinessDecisionRecord expected, IReadOnlyList<ReleaseReadinessDecisionRecord> actual)
    {
        Assert.AreEqual(1, actual.Count);
        AssertRecord(expected, actual[0]);
    }

    private static void AssertRecord(ReleaseReadinessDecisionRecord expected, ReleaseReadinessDecisionRecord? actual)
    {
        Assert.IsNotNull(actual);
        Assert.AreEqual(expected.ReleaseReadinessDecisionRecordId, actual.ReleaseReadinessDecisionRecordId);
        Assert.AreEqual(expected.ProjectId, actual.ProjectId);
        Assert.AreEqual(expected.ReleaseReadinessReportId, actual.ReleaseReadinessReportId);
        Assert.AreEqual(expected.ReleaseReadinessReportHash, actual.ReleaseReadinessReportHash);
        Assert.AreEqual(expected.WorkflowRunId, actual.WorkflowRunId);
        Assert.AreEqual(expected.WorkflowStepId, actual.WorkflowStepId);
        Assert.AreEqual(expected.SubjectKind, actual.SubjectKind);
        Assert.AreEqual(expected.SubjectId, actual.SubjectId);
        Assert.AreEqual(expected.SubjectHash, actual.SubjectHash);
        Assert.AreEqual(expected.DecisionStatus, actual.DecisionStatus);
        Assert.AreEqual(expected.ReleaseReadinessEvidenceSatisfied, actual.ReleaseReadinessEvidenceSatisfied);
        Assert.AreEqual(expected.ReleaseApproved, actual.ReleaseApproved);
        Assert.AreEqual(expected.DeploymentApproved, actual.DeploymentApproved);
        Assert.AreEqual(expected.MergeApproved, actual.MergeApproved);
        Assert.AreEqual(expected.SourceApplyExecutedByDecision, actual.SourceApplyExecutedByDecision);
        Assert.AreEqual(expected.RollbackExecutedByDecision, actual.RollbackExecutedByDecision);
        Assert.AreEqual(expected.WorkflowMutatedByDecision, actual.WorkflowMutatedByDecision);
        Assert.AreEqual(expected.GitOperationExecutedByDecision, actual.GitOperationExecutedByDecision);
        Assert.AreEqual(expected.ReleaseExecutedByDecision, actual.ReleaseExecutedByDecision);
        Assert.AreEqual(expected.HumanReviewRequiredForReleaseApproval, actual.HumanReviewRequiredForReleaseApproval);
        Assert.AreEqual(expected.HumanReviewRequiredForDeployment, actual.HumanReviewRequiredForDeployment);
        Assert.AreEqual(expected.HumanReviewRequiredForMerge, actual.HumanReviewRequiredForMerge);
        Assert.AreEqual(expected.DecidedAtUtc, actual.DecidedAtUtc);
        Assert.AreEqual(expected.ReleaseReadinessDecisionRecordHash, actual.ReleaseReadinessDecisionRecordHash);
        Assert.AreEqual(expected.Boundary, actual.Boundary);
        Assert.AreEqual(expected.Reasons.Count, actual.Reasons.Count);
        Assert.AreEqual(expected.Reasons[0].Code, actual.Reasons[0].Code);
        Assert.AreEqual(expected.Reasons[0].Severity, actual.Reasons[0].Severity);
        Assert.AreEqual(expected.Reasons[0].Field, actual.Reasons[0].Field);
        Assert.AreEqual(expected.Reasons[0].Message, actual.Reasons[0].Message);
        CollectionAssert.AreEqual(expected.EvidenceReferences.ToArray(), actual.EvidenceReferences.ToArray());
        CollectionAssert.AreEqual(expected.BoundaryMaxims.ToArray(), actual.BoundaryMaxims.ToArray());
    }

    private static string DirectInsertSql() =>
        @"INSERT INTO governance.ReleaseReadinessDecisionRecord
          (ReleaseReadinessDecisionRecordId, ProjectId, ReleaseReadinessReportId, ReleaseReadinessReportHash, WorkflowRunId, WorkflowStepId, SubjectKind, SubjectId, SubjectHash, DecisionStatus, ReleaseReadinessEvidenceSatisfied, ReleaseApproved, DeploymentApproved, MergeApproved, SourceApplyExecutedByDecision, RollbackExecutedByDecision, WorkflowMutatedByDecision, GitOperationExecutedByDecision, ReleaseExecutedByDecision, HumanReviewRequiredForReleaseApproval, HumanReviewRequiredForDeployment, HumanReviewRequiredForMerge, ReasonsJson, EvidenceReferencesJson, BoundaryMaximsJson, DecidedAtUtc, ReleaseReadinessDecisionRecordHash, Boundary)
          VALUES (NEWID(), NEWID(), NEWID(), @reportHash, CONCAT(N'workflow-run-', @suffix), CONCAT(N'workflow-step-', @suffix), N'ReleasePackage', CONCAT(N'release-package-', @suffix), @subjectHash, N'ReadyEvidenceSatisfied', 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, @reasonsJson, N'[""release-readiness-report:evidence""]', N'[""store evidence only""]', SYSUTCDATETIME(), @recordHash, @boundary)";

    private static SqlParameter[] DirectInsertParameters(string suffix, string boundary) =>
    [
        new SqlParameter("@suffix", suffix),
        new SqlParameter("@reportHash", H($"direct-report-{suffix}")),
        new SqlParameter("@subjectHash", H($"direct-subject-{suffix}")),
        new SqlParameter("@recordHash", H($"direct-record-{suffix}")),
        new SqlParameter("@reasonsJson", @"[{""code"":""ReportComplete"",""severity"":""Info"",""field"":""ReleaseReadinessReport"",""message"":""Report evidence complete.""}]"),
        new SqlParameter("@boundary", boundary),
    ];

    private static string StoredProcedureSaveSql() =>
        """
        EXEC governance.usp_ReleaseReadinessDecisionRecord_Save
            @ReleaseReadinessDecisionRecordId,
            @ProjectId,
            @ReleaseReadinessReportId,
            @ReleaseReadinessReportHash,
            @WorkflowRunId,
            @WorkflowStepId,
            @SubjectKind,
            @SubjectId,
            @SubjectHash,
            @DecisionStatus,
            @ReleaseReadinessEvidenceSatisfied,
            @ReleaseApproved,
            @DeploymentApproved,
            @MergeApproved,
            @SourceApplyExecutedByDecision,
            @RollbackExecutedByDecision,
            @WorkflowMutatedByDecision,
            @GitOperationExecutedByDecision,
            @ReleaseExecutedByDecision,
            @HumanReviewRequiredForReleaseApproval,
            @HumanReviewRequiredForDeployment,
            @HumanReviewRequiredForMerge,
            @ReasonsJson,
            @EvidenceReferencesJson,
            @BoundaryMaximsJson,
            @DecidedAtUtc,
            @ReleaseReadinessDecisionRecordHash,
            @Boundary
        """;

    private static SqlParameter[] DirectProcedureParameters(ReleaseReadinessDecisionRecord record) =>
    [
        new SqlParameter("@ReleaseReadinessDecisionRecordId", record.ReleaseReadinessDecisionRecordId),
        new SqlParameter("@ProjectId", record.ProjectId),
        new SqlParameter("@ReleaseReadinessReportId", record.ReleaseReadinessReportId),
        new SqlParameter("@ReleaseReadinessReportHash", record.ReleaseReadinessReportHash),
        new SqlParameter("@WorkflowRunId", record.WorkflowRunId),
        new SqlParameter("@WorkflowStepId", record.WorkflowStepId),
        new SqlParameter("@SubjectKind", record.SubjectKind),
        new SqlParameter("@SubjectId", record.SubjectId),
        new SqlParameter("@SubjectHash", record.SubjectHash),
        new SqlParameter("@DecisionStatus", record.DecisionStatus),
        new SqlParameter("@ReleaseReadinessEvidenceSatisfied", record.ReleaseReadinessEvidenceSatisfied),
        new SqlParameter("@ReleaseApproved", record.ReleaseApproved),
        new SqlParameter("@DeploymentApproved", record.DeploymentApproved),
        new SqlParameter("@MergeApproved", record.MergeApproved),
        new SqlParameter("@SourceApplyExecutedByDecision", record.SourceApplyExecutedByDecision),
        new SqlParameter("@RollbackExecutedByDecision", record.RollbackExecutedByDecision),
        new SqlParameter("@WorkflowMutatedByDecision", record.WorkflowMutatedByDecision),
        new SqlParameter("@GitOperationExecutedByDecision", record.GitOperationExecutedByDecision),
        new SqlParameter("@ReleaseExecutedByDecision", record.ReleaseExecutedByDecision),
        new SqlParameter("@HumanReviewRequiredForReleaseApproval", record.HumanReviewRequiredForReleaseApproval),
        new SqlParameter("@HumanReviewRequiredForDeployment", record.HumanReviewRequiredForDeployment),
        new SqlParameter("@HumanReviewRequiredForMerge", record.HumanReviewRequiredForMerge),
        new SqlParameter("@ReasonsJson", """[{"code":"ReportComplete","severity":"Info","field":"ReleaseReadinessReport","message":"Report evidence complete."}]"""),
        new SqlParameter("@EvidenceReferencesJson", """["release-readiness-report:evidence"]"""),
        new SqlParameter("@BoundaryMaximsJson", """["store evidence only"]"""),
        new SqlParameter("@DecidedAtUtc", record.DecidedAtUtc),
        new SqlParameter("@ReleaseReadinessDecisionRecordHash", record.ReleaseReadinessDecisionRecordHash),
        new SqlParameter("@Boundary", "Release readiness store is not release approval."),
    ];

    private async Task ApplySqlFileAsync(params string[] pathParts)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(new[] { RepoRoot() }.Concat(pathParts).ToArray()));
        foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(batch))
            {
                await connection.ExecuteAsync(batch);
            }
        }
    }

    private async Task DropReleaseReadinessDecisionRecordAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            IF OBJECT_ID(N'governance.usp_ReleaseReadinessDecisionRecord_Save', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ReleaseReadinessDecisionRecord_Save;
            IF OBJECT_ID(N'governance.usp_ReleaseReadinessDecisionRecord_Get', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ReleaseReadinessDecisionRecord_Get;
            IF OBJECT_ID(N'governance.usp_ReleaseReadinessDecisionRecord_GetByHash', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ReleaseReadinessDecisionRecord_GetByHash;
            IF OBJECT_ID(N'governance.usp_ReleaseReadinessDecisionRecord_ListByReleaseReadinessReport', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ReleaseReadinessDecisionRecord_ListByReleaseReadinessReport;
            IF OBJECT_ID(N'governance.usp_ReleaseReadinessDecisionRecord_ListByWorkflowRun', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ReleaseReadinessDecisionRecord_ListByWorkflowRun;
            IF OBJECT_ID(N'governance.usp_ReleaseReadinessDecisionRecord_ListBySubject', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ReleaseReadinessDecisionRecord_ListBySubject;
            IF OBJECT_ID(N'governance.TR_ReleaseReadinessDecisionRecord_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER governance.TR_ReleaseReadinessDecisionRecord_ValidateInsert;
            IF OBJECT_ID(N'governance.TR_ReleaseReadinessDecisionRecord_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_ReleaseReadinessDecisionRecord_BlockUpdateDelete;
            IF OBJECT_ID(N'governance.ReleaseReadinessDecisionRecord', N'U') IS NOT NULL DROP TABLE governance.ReleaseReadinessDecisionRecord;
            """);
    }

    private async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<T>(sql, parameters) ?? throw new InvalidOperationException("Scalar query returned null.");
    }

    private async Task ExecuteSqlAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private async Task AssertSqlFailsAsync(string sql, params SqlParameter[] parameters)
    {
        await AssertThrowsAsync<SqlException>(() => ExecuteSqlAsync(sql, parameters));
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
        {
            Assert.IsFalse(File.ReadAllText(file).Contains(token, StringComparison.Ordinal), $"Unexpected production token {token} in {file}.");
        }
    }

    private static string[] ProductionFiles()
    {
        var root = RepoRoot();
        return
        [
            Path.Combine(root, "IronDev.Core", "Governance", "ReleaseReadinessDecisionRecordStore.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlReleaseReadinessDecisionRecordStore.cs"),
            Path.Combine(root, "Database", "migrate_release_readiness_decision_record.sql"),
        ];
    }

    private static string SqlMigrationPath() =>
        Path.Combine(RepoRoot(), "Database", "migrate_release_readiness_decision_record.sql");

    private static string ReceiptPath() =>
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR218_RELEASE_READINESS_STORE.md");

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
