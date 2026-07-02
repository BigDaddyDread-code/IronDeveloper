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
[TestCategory("PolicySatisfactionSqlStore")]
public sealed class PolicySatisfactionSqlStoreTests : IntegrationTestBase
{
    private static readonly DateTimeOffset ApprovalEvaluatedAtUtc = new(2026, 6, 16, 11, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SatisfiedAtUtc = new(2026, 6, 16, 11, 1, 0, TimeSpan.Zero);

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropPolicySatisfactionAsync();
        await ApplySqlFileAsync("Database", "migrate_policy_satisfaction.sql");
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        await DropPolicySatisfactionAsync();
        await base.TestCleanup();
    }

    [TestMethod]
    public async Task PolicySatisfactionSqlStore_CanInsertAndReadPolicySatisfaction()
    {
        var record = ValidRecord();

        await Store().SaveAsync(record);
        var read = await Store().GetAsync(record.ProjectId, record.PolicySatisfactionId);

        Assert.IsNotNull(read);
        AssertRecord(record, read);
    }

    [TestMethod]
    public async Task PolicySatisfactionSqlStore_RejectsInvalidRecordShape()
    {
        foreach (var invalid in new[]
        {
            ValidRecord() with { PolicyCode = " " },
            ValidRecord() with { PolicyVersion = " " },
            ValidRecord() with { SubjectHash = " " },
            ValidRecord() with { AcceptedApprovalId = Guid.Empty },
            ValidRecord() with { ApprovalRequirementHash = " " },
            ValidRecord() with { EvidenceReferences = [] },
            ValidRecord() with { BoundaryMaxims = [] }
        })
        {
            await AssertThrowsAsync<ArgumentException>(() => Store().SaveAsync(invalid));
        }
    }

    [TestMethod]
    public async Task PolicySatisfactionSqlStore_IsProjectScoped()
    {
        var policySatisfactionId = Guid.NewGuid();
        var firstProject = Guid.NewGuid();
        var secondProject = Guid.NewGuid();
        var first = ValidRecord() with { PolicySatisfactionId = policySatisfactionId, ProjectId = firstProject };
        var second = ValidRecord() with { PolicySatisfactionId = Guid.NewGuid(), ProjectId = secondProject };

        await Store().SaveAsync(first);
        await Store().SaveAsync(second);

        Assert.IsNotNull(await Store().GetAsync(firstProject, policySatisfactionId));
        Assert.IsNull(await Store().GetAsync(secondProject, policySatisfactionId));
    }

    [TestMethod]
    public async Task PolicySatisfactionSqlStore_CanListBySubject()
    {
        var projectId = Guid.NewGuid();
        var matchingFirst = ValidRecord() with { ProjectId = projectId, PolicySatisfactionId = Guid.NewGuid(), SubjectKind = "patch-artifact", SubjectId = "subject-1", SatisfiedAtUtc = SatisfiedAtUtc.AddMinutes(1), ExpiresAtUtc = SatisfiedAtUtc.AddDays(1) };
        var matchingSecond = ValidRecord() with { ProjectId = projectId, PolicySatisfactionId = Guid.NewGuid(), SubjectKind = "patch-artifact", SubjectId = "subject-1", SatisfiedAtUtc = SatisfiedAtUtc.AddMinutes(2), ExpiresAtUtc = SatisfiedAtUtc.AddDays(1) };
        var otherSubject = ValidRecord() with { ProjectId = projectId, PolicySatisfactionId = Guid.NewGuid(), SubjectKind = "patch-artifact", SubjectId = "subject-2" };
        var otherProject = ValidRecord() with { ProjectId = Guid.NewGuid(), PolicySatisfactionId = Guid.NewGuid(), SubjectKind = "patch-artifact", SubjectId = "subject-1" };

        await Store().SaveAsync(matchingFirst);
        await Store().SaveAsync(matchingSecond);
        await Store().SaveAsync(otherSubject);
        await Store().SaveAsync(otherProject);

        var results = await Store().ListBySubjectAsync(projectId, "patch-artifact", "subject-1");

        CollectionAssert.AreEquivalent(new[] { matchingFirst.PolicySatisfactionId, matchingSecond.PolicySatisfactionId }, results.Select(result => result.PolicySatisfactionId).ToArray());
    }

    [TestMethod]
    public async Task PolicySatisfactionSqlStore_CanListByAcceptedApproval()
    {
        var projectId = Guid.NewGuid();
        var acceptedApprovalId = Guid.NewGuid();
        var matchingFirst = ValidRecord() with { ProjectId = projectId, PolicySatisfactionId = Guid.NewGuid(), AcceptedApprovalId = acceptedApprovalId };
        var matchingSecond = ValidRecord() with { ProjectId = projectId, PolicySatisfactionId = Guid.NewGuid(), AcceptedApprovalId = acceptedApprovalId };
        var otherApproval = ValidRecord() with { ProjectId = projectId, PolicySatisfactionId = Guid.NewGuid(), AcceptedApprovalId = Guid.NewGuid() };
        var otherProject = ValidRecord() with { ProjectId = Guid.NewGuid(), PolicySatisfactionId = Guid.NewGuid(), AcceptedApprovalId = acceptedApprovalId };

        await Store().SaveAsync(matchingFirst);
        await Store().SaveAsync(matchingSecond);
        await Store().SaveAsync(otherApproval);
        await Store().SaveAsync(otherProject);

        var results = await Store().ListByAcceptedApprovalAsync(projectId, acceptedApprovalId);

        CollectionAssert.AreEquivalent(new[] { matchingFirst.PolicySatisfactionId, matchingSecond.PolicySatisfactionId }, results.Select(result => result.PolicySatisfactionId).ToArray());
    }

    [TestMethod]
    public async Task PolicySatisfactionSqlStore_CanListByProjectAndCorrelation()
    {
        var projectId = Guid.NewGuid();
        var correlation = "correlation-pr175";
        var matchingFirst = ValidRecord() with { ProjectId = projectId, PolicySatisfactionId = Guid.NewGuid(), CorrelationId = correlation };
        var matchingSecond = ValidRecord() with { ProjectId = projectId, PolicySatisfactionId = Guid.NewGuid(), CorrelationId = correlation };
        var otherProject = ValidRecord() with { ProjectId = Guid.NewGuid(), PolicySatisfactionId = Guid.NewGuid(), CorrelationId = correlation };
        var otherCorrelation = ValidRecord() with { ProjectId = projectId, PolicySatisfactionId = Guid.NewGuid(), CorrelationId = "other-correlation" };

        await Store().SaveAsync(matchingFirst);
        await Store().SaveAsync(matchingSecond);
        await Store().SaveAsync(otherProject);
        await Store().SaveAsync(otherCorrelation);

        var results = await Store().ListByProjectAndCorrelationAsync(projectId, correlation);

        CollectionAssert.AreEquivalent(new[] { matchingFirst.PolicySatisfactionId, matchingSecond.PolicySatisfactionId }, results.Select(result => result.PolicySatisfactionId).ToArray());
    }

    [TestMethod]
    public async Task PolicySatisfactionSqlStore_RejectsDuplicatePolicySatisfactionId()
    {
        var record = ValidRecord();

        await Store().SaveAsync(record);

        await AssertThrowsAsync<SqlException>(() => Store().SaveAsync(record));
    }

    [TestMethod]
    public void PolicySatisfactionSqlStore_DoesNotExposeUpdateOrDelete()
    {
        var forbidden = new[] { "Update", "Delete", "Remove", "Overwrite", "Upsert" };
        var methods = typeof(IPolicySatisfactionStore)
            .GetMethods()
            .Concat(typeof(SqlPolicySatisfactionStore).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

        foreach (var method in methods)
        foreach (var token in forbidden)
        {
            Assert.IsFalse(method.Name.Contains(token, StringComparison.OrdinalIgnoreCase), $"Unexpected method: {method.Name}");
        }

        StringAssert.Contains(File.ReadAllText(SqlMigrationPath()), "TR_PolicySatisfaction_BlockUpdateDelete");
    }

    [TestMethod]
    public void PolicySatisfactionSqlStore_UsesPolicySatisfactionValidation()
    {
        var store = File.ReadAllText(StoreSourcePath());

        StringAssert.Contains(store, "PolicySatisfactionValidation.Validate");
    }

    [TestMethod]
    public async Task PolicySatisfactionSqlStore_PreservesEvidenceReferences()
    {
        var record = ValidRecord() with { EvidenceReferences = ["accepted-approval:one", "approval-satisfaction:two"] };

        await Store().SaveAsync(record);
        var read = await Store().GetAsync(record.ProjectId, record.PolicySatisfactionId);

        CollectionAssert.AreEqual(record.EvidenceReferences.ToArray(), read!.EvidenceReferences.ToArray());
    }

    [TestMethod]
    public async Task PolicySatisfactionSqlStore_PreservesBoundaryMaxims()
    {
        var record = ValidRecord() with
        {
            BoundaryMaxims =
            [
                "Policy satisfaction SQL store is not policy satisfaction creation.",
                "Persisted policy satisfaction is not source apply."
            ]
        };

        await Store().SaveAsync(record);
        var read = await Store().GetAsync(record.ProjectId, record.PolicySatisfactionId);

        CollectionAssert.AreEqual(record.BoundaryMaxims.ToArray(), read!.BoundaryMaxims.ToArray());
    }

    [TestMethod]
    public async Task PolicySatisfactionSqlStore_BlocksUnsafeDirectSqlMaterialAndMutation()
    {
        var record = ValidRecord();
        await Store().SaveAsync(record);

        await AssertSqlFailsAsync("UPDATE governance.PolicySatisfaction SET CapabilityCode = N'changed' WHERE PolicySatisfactionId = @id", new SqlParameter("@id", record.PolicySatisfactionId));
        await AssertSqlFailsAsync("DELETE FROM governance.PolicySatisfaction WHERE PolicySatisfactionId = @id", new SqlParameter("@id", record.PolicySatisfactionId));
        await AssertSqlFailsAsync(DirectInsertSql(), DirectInsertParameters("[\"rawPrompt leaked\"]", JsonSerializer.Serialize(record.BoundaryMaxims)));
        await AssertSqlFailsAsync(DirectInsertSql(), DirectInsertParameters(JsonSerializer.Serialize(record.EvidenceReferences), "[\"applies source\"]"));
    }

    [TestMethod]
    public void PolicySatisfactionSqlStore_MigrationAndInventoryAreRegistered()
    {
        var manifest = File.ReadAllText(Path.Combine(RepoRoot(), "Database", "migrations.json"));
        var inventory = File.ReadAllText(Path.Combine(RepoRoot(), "Database", "sql-inventory.json"));
        var verifier = File.ReadAllText(Path.Combine(RepoRoot(), "Database", "verify-migrations.ps1"));
        var sql = File.ReadAllText(SqlMigrationPath());

        StringAssert.Contains(manifest, "Database/migrate_policy_satisfaction.sql");
        StringAssert.Contains(inventory, "database.migrate-policy-satisfaction");
        StringAssert.Contains(inventory, "runtime.policy-satisfaction-store");
        StringAssert.Contains(verifier, "governance.PolicySatisfaction table");
        StringAssert.Contains(sql, "governance.usp_PolicySatisfaction_Save");
        StringAssert.Contains(sql, "governance.usp_PolicySatisfaction_ListBySubject");
        StringAssert.Contains(sql, "governance.usp_PolicySatisfaction_ListByAcceptedApproval");
        StringAssert.Contains(sql, "governance.usp_PolicySatisfaction_ListByProjectAndCorrelation");
        StringAssert.Contains(sql, "TR_PolicySatisfaction_BlockUpdateDelete");
    }

    [TestMethod]
    public void PolicySatisfactionSqlStore_DoesNotAuthorizeDryRunPatchApplyWorkflowOrRelease()
    {
        foreach (var file in Pr175ProductionFiles())
        {
            var text = File.ReadAllText(file);
            AssertNoForbiddenTokens(
                text,
                "RunDryRunAsync",
                "CreatePatchArtifactAsync",
                "ApplySourceAsync",
                "ContinueWorkflowAsync",
                "ApproveReleaseAsync",
                "ReleaseReady = true",
                "CanApplySource = true");
        }
    }

    [TestMethod]
    public void PolicySatisfactionSqlStore_HasReceiptBoundaryLanguage()
    {
        var receipt = File.ReadAllText(ReceiptPath());

        foreach (var statement in new[]
        {
            "Policy satisfaction SQL store is not policy satisfaction creation.",
            "Persisted policy satisfaction is not dry-run execution.",
            "Persisted policy satisfaction is not patch artifact creation.",
            "Persisted policy satisfaction is not source apply.",
            "Persisted policy satisfaction is not rollback.",
            "Persisted policy satisfaction is not workflow continuation.",
            "Persisted policy satisfaction is not release readiness.",
            "Persisted policy satisfaction does not authorize execution by itself."
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    private IPolicySatisfactionStore Store() =>
        new SqlPolicySatisfactionStore(ServiceProvider.GetRequiredService<IDbConnectionFactory>());

    private static PolicySatisfactionRecord ValidRecord() =>
        new()
        {
            PolicySatisfactionId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            PolicyCode = "source-apply-policy",
            PolicyVersion = "2026-06-16.v1",
            SubjectKind = "patch-artifact",
            SubjectId = "patch-artifact-pr175",
            SubjectHash = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            CapabilityCode = "SOURCE_APPLY",
            AcceptedApprovalId = Guid.NewGuid(),
            ApprovalRequirementHash = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            ApprovalEvaluatedAtUtc = ApprovalEvaluatedAtUtc,
            SatisfiedAtUtc = SatisfiedAtUtc,
            ExpiresAtUtc = SatisfiedAtUtc.AddDays(7),
            CorrelationId = "correlation-pr175",
            CausationId = "approval-satisfaction-pr175",
            EvidenceReferences = ["accepted-approval:accepted-approval-pr175", "approval-satisfaction:evaluation-pr175"],
            BoundaryMaxims =
            [
                "Policy satisfaction SQL store is not policy satisfaction creation.",
                "Persisted policy satisfaction is not source apply."
            ]
        };

    private static void AssertRecord(PolicySatisfactionRecord expected, PolicySatisfactionRecord actual)
    {
        Assert.AreEqual(expected.PolicySatisfactionId, actual.PolicySatisfactionId);
        Assert.AreEqual(expected.ProjectId, actual.ProjectId);
        Assert.AreEqual(expected.PolicyCode, actual.PolicyCode);
        Assert.AreEqual(expected.PolicyVersion, actual.PolicyVersion);
        Assert.AreEqual(expected.SubjectKind, actual.SubjectKind);
        Assert.AreEqual(expected.SubjectId, actual.SubjectId);
        Assert.AreEqual(expected.SubjectHash, actual.SubjectHash);
        Assert.AreEqual(expected.CapabilityCode, actual.CapabilityCode);
        Assert.AreEqual(expected.AcceptedApprovalId, actual.AcceptedApprovalId);
        Assert.AreEqual(expected.ApprovalRequirementHash, actual.ApprovalRequirementHash);
        Assert.AreEqual(expected.ApprovalEvaluatedAtUtc, actual.ApprovalEvaluatedAtUtc);
        Assert.AreEqual(expected.SatisfiedAtUtc, actual.SatisfiedAtUtc);
        Assert.AreEqual(expected.ExpiresAtUtc, actual.ExpiresAtUtc);
        Assert.AreEqual(expected.CorrelationId, actual.CorrelationId);
        Assert.AreEqual(expected.CausationId, actual.CausationId);
        CollectionAssert.AreEqual(expected.EvidenceReferences.ToArray(), actual.EvidenceReferences.ToArray());
        CollectionAssert.AreEqual(expected.BoundaryMaxims.ToArray(), actual.BoundaryMaxims.ToArray());
    }

    private static string DirectInsertSql() =>
        @"INSERT INTO governance.PolicySatisfaction
          (PolicySatisfactionId, ProjectId, PolicyCode, PolicyVersion, SubjectKind, SubjectId, SubjectHash, CapabilityCode, AcceptedApprovalId, ApprovalRequirementHash, ApprovalEvaluatedAtUtc, SatisfiedAtUtc, ExpiresAtUtc, CorrelationId, CausationId, EvidenceReferencesJson, BoundaryMaximsJson)
          VALUES (NEWID(), NEWID(), N'source-apply-policy', N'2026-06-16.v1', N'patch-artifact', N'direct-subject', N'sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc', N'SOURCE_APPLY', NEWID(), N'sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd', SYSUTCDATETIME(), SYSUTCDATETIME(), DATEADD(day, 1, SYSUTCDATETIME()), N'direct-correlation', N'direct-causation', @evidenceJson, @boundaryJson)";

    private static SqlParameter[] DirectInsertParameters(string evidenceJson, string boundaryJson) =>
    [
        new SqlParameter("@evidenceJson", evidenceJson),
        new SqlParameter("@boundaryJson", boundaryJson)
    ];

    private async Task ApplySqlFileAsync(params string[] pathParts)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(RepoRoot(), Path.Combine(pathParts)));
        foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(batch))
            {
                continue;
            }

            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task DropPolicySatisfactionAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            @"IF OBJECT_ID(N'governance.usp_PolicySatisfaction_Save', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PolicySatisfaction_Save;
              IF OBJECT_ID(N'governance.usp_PolicySatisfaction_Get', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PolicySatisfaction_Get;
              IF OBJECT_ID(N'governance.usp_PolicySatisfaction_ListBySubject', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PolicySatisfaction_ListBySubject;
              IF OBJECT_ID(N'governance.usp_PolicySatisfaction_ListByAcceptedApproval', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PolicySatisfaction_ListByAcceptedApproval;
              IF OBJECT_ID(N'governance.usp_PolicySatisfaction_ListByProjectAndCorrelation', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PolicySatisfaction_ListByProjectAndCorrelation;
              IF OBJECT_ID(N'governance.TR_PolicySatisfaction_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER governance.TR_PolicySatisfaction_ValidateInsert;
              IF OBJECT_ID(N'governance.TR_PolicySatisfaction_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_PolicySatisfaction_BlockUpdateDelete;
              IF OBJECT_ID(N'governance.PolicySatisfaction', N'U') IS NOT NULL DROP TABLE governance.PolicySatisfaction;",
            connection);
        await command.ExecuteNonQueryAsync();
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

    private static IReadOnlyList<string> Pr175ProductionFiles()
    {
        var root = RepoRoot();
        return
        [
            Path.Combine(root, "Database", "migrate_policy_satisfaction.sql"),
            Path.Combine(root, "IronDev.Core", "Governance", "IPolicySatisfactionStore.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlPolicySatisfactionStore.cs")
        ];
    }

    private static string SqlMigrationPath() =>
        Path.Combine(RepoRoot(), "Database", "migrate_policy_satisfaction.sql");

    private static string StoreSourcePath() =>
        Path.Combine(RepoRoot(), "IronDev.Infrastructure", "Governance", "SqlPolicySatisfactionStore.cs");

    private static string ReceiptPath() =>
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR175_POLICY_SATISFACTION_SQL_STORE.md");

    private static string RepoRoot()
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

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static void AssertNoForbiddenTokens(string text, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Unexpected token: {token}");
        }
    }
}
