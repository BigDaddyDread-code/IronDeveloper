using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using IronDev.Core.Governance;
using IronDev.Data;
using IronDev.Infrastructure.Governance;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("AcceptedApprovalSqlStore")]
public sealed class AcceptedApprovalSqlStoreTests : IntegrationTestBase
{
    private static readonly DateTimeOffset AcceptedAtUtc = new(2026, 6, 16, 8, 0, 0, TimeSpan.Zero);

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropAcceptedApprovalAsync();
        await ApplySqlFileAsync("Database", "migrate_accepted_approval.sql");
    }

    [TestMethod]
    public async Task AcceptedApprovalSqlStore_CanInsertAndReadAcceptedApproval()
    {
        var record = ValidRecord();

        await Store().SaveAsync(record);
        var read = await Store().GetAsync(record.ProjectId, record.AcceptedApprovalId);

        Assert.IsNotNull(read);
        AssertRecord(record, read);
    }

    [TestMethod]
    public async Task AcceptedApprovalSqlStore_RejectsInvalidRecordShape()
    {
        foreach (var invalid in new[]
        {
            ValidRecord() with { ApprovalTargetHash = " " },
            ValidRecord() with { ApprovedByActorId = " " },
            ValidRecord() with { EvidenceReferences = [] },
            ValidRecord() with { BoundaryMaxims = [] }
        })
        {
            await AssertThrowsAsync<ArgumentException>(() => Store().SaveAsync(invalid));
        }
    }

    [TestMethod]
    public async Task AcceptedApprovalSqlStore_IsProjectScoped()
    {
        var acceptedApprovalId = Guid.NewGuid();
        var firstProject = Guid.NewGuid();
        var secondProject = Guid.NewGuid();
        var first = ValidRecord() with { AcceptedApprovalId = acceptedApprovalId, ProjectId = firstProject };
        var second = ValidRecord() with { AcceptedApprovalId = Guid.NewGuid(), ProjectId = secondProject };

        await Store().SaveAsync(first);
        await Store().SaveAsync(second);

        Assert.IsNotNull(await Store().GetAsync(firstProject, acceptedApprovalId));
        Assert.IsNull(await Store().GetAsync(secondProject, acceptedApprovalId));
    }

    [TestMethod]
    public async Task AcceptedApprovalSqlStore_CanListByTarget()
    {
        var projectId = Guid.NewGuid();
        var matchingFirst = ValidRecord() with { ProjectId = projectId, AcceptedApprovalId = Guid.NewGuid(), ApprovalTargetId = "target-1", AcceptedAtUtc = AcceptedAtUtc.AddMinutes(1) };
        var matchingSecond = ValidRecord() with { ProjectId = projectId, AcceptedApprovalId = Guid.NewGuid(), ApprovalTargetId = "target-1", AcceptedAtUtc = AcceptedAtUtc.AddMinutes(2) };
        var otherTarget = ValidRecord() with { ProjectId = projectId, AcceptedApprovalId = Guid.NewGuid(), ApprovalTargetId = "target-2" };
        var otherProject = ValidRecord() with { ProjectId = Guid.NewGuid(), AcceptedApprovalId = Guid.NewGuid(), ApprovalTargetId = "target-1" };

        await Store().SaveAsync(matchingFirst);
        await Store().SaveAsync(matchingSecond);
        await Store().SaveAsync(otherTarget);
        await Store().SaveAsync(otherProject);

        var results = await Store().ListByTargetAsync(projectId, matchingFirst.ApprovalTargetKind, "target-1");

        CollectionAssert.AreEquivalent(new[] { matchingFirst.AcceptedApprovalId, matchingSecond.AcceptedApprovalId }, results.Select(result => result.AcceptedApprovalId).ToArray());
    }

    [TestMethod]
    public async Task AcceptedApprovalSqlStore_CanListByCorrelation()
    {
        var correlation = "correlation-shared";
        var matchingFirst = ValidRecord() with { AcceptedApprovalId = Guid.NewGuid(), CorrelationId = correlation };
        var matchingSecond = ValidRecord() with { AcceptedApprovalId = Guid.NewGuid(), ProjectId = Guid.NewGuid(), CorrelationId = correlation };
        var other = ValidRecord() with { AcceptedApprovalId = Guid.NewGuid(), CorrelationId = "other-correlation" };

        await Store().SaveAsync(matchingFirst);
        await Store().SaveAsync(matchingSecond);
        await Store().SaveAsync(other);

        var results = await Store().ListByCorrelationAsync(correlation);

        CollectionAssert.AreEquivalent(new[] { matchingFirst.AcceptedApprovalId, matchingSecond.AcceptedApprovalId }, results.Select(result => result.AcceptedApprovalId).ToArray());
    }

    [TestMethod]
    public async Task AcceptedApprovalSqlStore_RejectsDuplicateAcceptedApprovalId()
    {
        var record = ValidRecord();

        await Store().SaveAsync(record);

        await AssertThrowsAsync<SqlException>(() => Store().SaveAsync(record));
    }

    [TestMethod]
    public void AcceptedApprovalSqlStore_DoesNotExposeUpdateOrDelete()
    {
        var forbidden = new[] { "Update", "Delete", "Remove", "Overwrite", "Upsert" };
        var methods = typeof(IAcceptedApprovalStore)
            .GetMethods()
            .Concat(typeof(SqlAcceptedApprovalStore).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

        foreach (var method in methods)
        foreach (var token in forbidden)
        {
            Assert.IsFalse(method.Name.Contains(token, StringComparison.OrdinalIgnoreCase), $"Unexpected method: {method.Name}");
        }
    }

    [TestMethod]
    public void AcceptedApprovalSqlStore_DoesNotSatisfyPolicyOrApplySource()
    {
        foreach (var file in Pr169ProductionFiles())
        {
            var text = File.ReadAllText(file);
            AssertNoForbiddenTokens(
                text,
                "SatisfyPolicy",
                "PolicySatisfied",
                "CanApplySource",
                "ApplySource",
                "ContinueWorkflow",
                "ApproveRelease",
                "ReleaseReady",
                "RunDryRun",
                "CreatePatchArtifact");
        }
    }

    [TestMethod]
    public void AcceptedApprovalSqlStore_HasReceiptBoundaryLanguage()
    {
        var receipt = File.ReadAllText(ReceiptPath());

        foreach (var statement in new[]
        {
            "Accepted approval SQL store is not approval creation.",
            "Persisted approval is not policy satisfaction.",
            "Persisted approval is not dry-run execution.",
            "Persisted approval is not patch artifact creation.",
            "Persisted approval is not source apply.",
            "Persisted approval is not workflow continuation.",
            "Persisted approval is not release readiness."
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    [TestMethod]
    public void AcceptedApprovalSqlStore_UsesAcceptedApprovalValidation()
    {
        var store = File.ReadAllText(Path.Combine(RepoRoot(), "IronDev.Infrastructure", "Governance", "SqlAcceptedApprovalStore.cs"));

        StringAssert.Contains(store, "AcceptedApprovalValidation.Validate");
    }

    [TestMethod]
    public async Task AcceptedApprovalSqlStore_PreservesBoundaryMaxims()
    {
        var record = ValidRecord() with { BoundaryMaxims = ["first boundary", "second boundary"] };

        await Store().SaveAsync(record);
        var read = await Store().GetAsync(record.ProjectId, record.AcceptedApprovalId);

        CollectionAssert.AreEqual(record.BoundaryMaxims.ToArray(), read!.BoundaryMaxims.ToArray());
    }

    [TestMethod]
    public async Task AcceptedApprovalSqlStore_PreservesEvidenceReferences()
    {
        var record = ValidRecord() with { EvidenceReferences = ["approval-package:one", "governance-event:two"] };

        await Store().SaveAsync(record);
        var read = await Store().GetAsync(record.ProjectId, record.AcceptedApprovalId);

        CollectionAssert.AreEqual(record.EvidenceReferences.ToArray(), read!.EvidenceReferences.ToArray());
    }

    [TestMethod]
    public async Task AcceptedApprovalSqlStore_DirectSqlBlocksUnsafeMaterialAndMutation()
    {
        var record = ValidRecord();
        await Store().SaveAsync(record);

        await AssertSqlFailsAsync("UPDATE governance.AcceptedApproval SET CapabilityCode = N'changed' WHERE AcceptedApprovalId = @id", new SqlParameter("@id", record.AcceptedApprovalId));
        await AssertSqlFailsAsync("DELETE FROM governance.AcceptedApproval WHERE AcceptedApprovalId = @id", new SqlParameter("@id", record.AcceptedApprovalId));
        await AssertSqlFailsAsync(DirectInsertSql(), DirectInsertParameters("[\"rawPrompt leaked\"]", JsonSerializer.Serialize(record.BoundaryMaxims)));
        await AssertSqlFailsAsync(DirectInsertSql(), DirectInsertParameters(JsonSerializer.Serialize(record.EvidenceReferences), "[\"grants execution\"]"));
    }

    [TestMethod]
    public void AcceptedApprovalSqlStore_MigrationAndInventoryAreRegistered()
    {
        var root = RepoRoot();
        var manifest = File.ReadAllText(Path.Combine(root, "Database", "migrations.json"));
        var inventory = File.ReadAllText(Path.Combine(root, "Database", "sql-inventory.json"));
        var verifier = File.ReadAllText(Path.Combine(root, "Database", "verify-migrations.ps1"));
        var sql = File.ReadAllText(Path.Combine(root, "Database", "migrate_accepted_approval.sql"));

        StringAssert.Contains(manifest, "Database/migrate_accepted_approval.sql");
        StringAssert.Contains(inventory, "database.migrate-accepted-approval");
        StringAssert.Contains(inventory, "runtime.accepted-approval-store");
        StringAssert.Contains(verifier, "governance.AcceptedApproval table");
        StringAssert.Contains(sql, "governance.usp_AcceptedApproval_Save");
        StringAssert.Contains(sql, "TR_AcceptedApproval_BlockUpdateDelete");
    }

    private IAcceptedApprovalStore Store() =>
        new SqlAcceptedApprovalStore(ServiceProvider.GetRequiredService<IDbConnectionFactory>());

    private static AcceptedApprovalRecord ValidRecord() =>
        new()
        {
            AcceptedApprovalId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            ApprovalTargetKind = AcceptedApprovalTargetKinds.PatchArtifact,
            ApprovalTargetId = "patch-artifact-123",
            ApprovalTargetHash = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            CapabilityCode = "L4_ACCEPTED_APPROVAL_RECORD",
            ApprovalPurpose = AcceptedApprovalPurposes.PolicySatisfactionInput,
            ApprovedByActorId = "human-operator-1",
            ApprovedByActorDisplayName = "Human Operator",
            AcceptedAtUtc = AcceptedAtUtc,
            ExpiresAtUtc = AcceptedAtUtc.AddDays(7),
            CorrelationId = "correlation-123",
            CausationId = "approval-package-123",
            EvidenceReferences = ["approval-package:approval-package-123"],
            BoundaryMaxims =
            [
                "Accepted approval SQL store is not approval creation.",
                "Persisted approval is not policy satisfaction."
            ]
        };

    private static void AssertRecord(AcceptedApprovalRecord expected, AcceptedApprovalRecord actual)
    {
        Assert.AreEqual(expected.AcceptedApprovalId, actual.AcceptedApprovalId);
        Assert.AreEqual(expected.ProjectId, actual.ProjectId);
        Assert.AreEqual(expected.ApprovalTargetKind, actual.ApprovalTargetKind);
        Assert.AreEqual(expected.ApprovalTargetId, actual.ApprovalTargetId);
        Assert.AreEqual(expected.ApprovalTargetHash, actual.ApprovalTargetHash);
        Assert.AreEqual(expected.CapabilityCode, actual.CapabilityCode);
        Assert.AreEqual(expected.ApprovalPurpose, actual.ApprovalPurpose);
        Assert.AreEqual(expected.ApprovedByActorId, actual.ApprovedByActorId);
        Assert.AreEqual(expected.ApprovedByActorDisplayName, actual.ApprovedByActorDisplayName);
        Assert.AreEqual(expected.AcceptedAtUtc, actual.AcceptedAtUtc);
        Assert.AreEqual(expected.ExpiresAtUtc, actual.ExpiresAtUtc);
        Assert.AreEqual(expected.CorrelationId, actual.CorrelationId);
        Assert.AreEqual(expected.CausationId, actual.CausationId);
        CollectionAssert.AreEqual(expected.EvidenceReferences.ToArray(), actual.EvidenceReferences.ToArray());
        CollectionAssert.AreEqual(expected.BoundaryMaxims.ToArray(), actual.BoundaryMaxims.ToArray());
    }

    private static string DirectInsertSql() =>
        @"INSERT INTO governance.AcceptedApproval
          (AcceptedApprovalId, ProjectId, ApprovalTargetKind, ApprovalTargetId, ApprovalTargetHash, CapabilityCode, ApprovalPurpose, ApprovedByActorId, ApprovedByActorDisplayName, AcceptedAtUtc, ExpiresAtUtc, CorrelationId, CausationId, EvidenceReferencesJson, BoundaryMaximsJson)
          VALUES (NEWID(), NEWID(), N'patch-artifact', N'direct-target', N'sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb', N'L4_ACCEPTED_APPROVAL_RECORD', N'policy-satisfaction-input', N'human-operator', N'Human Operator', SYSUTCDATETIME(), DATEADD(day, 1, SYSUTCDATETIME()), N'direct-correlation', N'direct-causation', @evidenceJson, @boundaryJson)";

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

    private async Task DropAcceptedApprovalAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            @"IF OBJECT_ID(N'governance.usp_AcceptedApproval_Save', N'P') IS NOT NULL DROP PROCEDURE governance.usp_AcceptedApproval_Save;
              IF OBJECT_ID(N'governance.usp_AcceptedApproval_Get', N'P') IS NOT NULL DROP PROCEDURE governance.usp_AcceptedApproval_Get;
              IF OBJECT_ID(N'governance.usp_AcceptedApproval_ListByTarget', N'P') IS NOT NULL DROP PROCEDURE governance.usp_AcceptedApproval_ListByTarget;
              IF OBJECT_ID(N'governance.usp_AcceptedApproval_ListByCorrelation', N'P') IS NOT NULL DROP PROCEDURE governance.usp_AcceptedApproval_ListByCorrelation;
              IF OBJECT_ID(N'governance.TR_AcceptedApproval_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER governance.TR_AcceptedApproval_ValidateInsert;
              IF OBJECT_ID(N'governance.TR_AcceptedApproval_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_AcceptedApproval_BlockUpdateDelete;
              IF OBJECT_ID(N'governance.AcceptedApproval', N'U') IS NOT NULL DROP TABLE governance.AcceptedApproval;",
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

    private static IReadOnlyList<string> Pr169ProductionFiles()
    {
        var root = RepoRoot();
        return
        [
            Path.Combine(root, "Database", "migrate_accepted_approval.sql"),
            Path.Combine(root, "IronDev.Core", "Governance", "IAcceptedApprovalStore.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlAcceptedApprovalStore.cs")
        ];
    }

    private static string ReceiptPath() =>
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR169_ACCEPTED_APPROVAL_SQL_STORE.md");

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
