using System.Data;
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
[TestCategory("Governance")]
[TestCategory("GovernanceEvent")]
[TestCategory("Store")]
[TestCategory("RequiresRealDatabase")]
[TestCategory("LongRunning")]
[TestCategory("Boundary")]
[TestCategory("Contract")]
public sealed partial class GovernanceEventAppendOnlyDatabaseConstraintTests : IntegrationTestBase
{
    private const string ReceiptPath = "Docs/receipts/H04_GOVERNANCE_EVENT_APPEND_ONLY_DB_CONSTRAINT_TESTS.md";
    private const string AppendProcedureName = "AppendGovernanceEvent";
    private const string GetProcedureName = "GetGovernanceEvent";
    private const string ListForProjectProcedureName = "ListGovernanceEventsForProject";
    private const string ListForCorrelationProcedureName = "ListGovernanceEventsForCorrelation";
    private const string ListForSubjectProcedureName = "ListGovernanceEventsForSubject";
    private const string ListCausedByProcedureName = "ListGovernanceEventsCausedBy";

    private static readonly Regex SqlBlockCommentRegex = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex SqlLineCommentRegex = new(@"--.*?$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex GovernanceEventMutationRegex = new(
        @"\b(INSERT\s+INTO|UPDATE|DELETE\s+FROM|MERGE)\s+(?:\[?governance\]?\.)?\[?GovernanceEvent\]?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private SqlGovernanceEventStore _store = default!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await ApplyGovernanceEventMigrationAsync();

        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        _store = new SqlGovernanceEventStore(connectionFactory);
    }

    [TestMethod]
    public async Task GovernanceEventStore_AppendCreatesImmutableEventThroughSupportedSurface()
    {
        var request = NewRequest(nameof(GovernanceEventStore_AppendCreatesImmutableEventThroughSupportedSurface));

        var appended = await _store.AppendAsync(request);
        var rowCount = await CountEventsAsync(appended.EventId);
        var read = await _store.GetAsync(appended.EventId);

        Assert.AreEqual(1, rowCount);
        Assert.IsNotNull(read);
        Assert.AreEqual(appended.EventId, read.EventId);
        Assert.AreEqual(request.ProjectId, read.ProjectId);
        Assert.AreEqual(request.EventType, read.EventType);
        Assert.AreEqual(request.ActorType, read.ActorType);
        Assert.AreEqual(request.ActorId, read.ActorId);
        Assert.AreEqual(request.CorrelationId, read.CorrelationId);
        Assert.AreEqual(request.CausationId, read.CausationId);
        Assert.AreEqual(request.SubjectType, read.SubjectType);
        Assert.AreEqual(request.SubjectId, read.SubjectId);
        Assert.AreEqual(GovernanceEventSchemaVersions.Current, read.PayloadVersion);
        Assert.AreEqual(request.PayloadJson, read.PayloadJson);
        Assert.AreNotEqual(default, read.CreatedUtc);
    }

    [TestMethod]
    public async Task GovernanceEventReadPaths_DoNotMutateStoredEvent()
    {
        var request = NewRequest(nameof(GovernanceEventReadPaths_DoNotMutateStoredEvent));
        var appended = await _store.AppendAsync(request);
        var before = await ReadSnapshotAsync(appended.EventId);

        _ = await _store.GetAsync(appended.EventId);
        _ = await _store.ListForProjectAsync(new GovernanceEventsForProjectQuery { ProjectId = request.ProjectId, Take = 10 });
        _ = await _store.ListForCorrelationAsync(new GovernanceEventsForCorrelationQuery { CorrelationId = request.CorrelationId!.Value, Take = 10 });
        _ = await _store.ListForSubjectAsync(new GovernanceEventsForSubjectQuery
        {
            ProjectId = request.ProjectId,
            SubjectType = request.SubjectType!,
            SubjectId = request.SubjectId!,
            Take = 10
        });
        _ = await _store.ListCausedByAsync(new GovernanceEventsCausedByQuery { CausationId = request.CausationId!.Value, Take = 10 });

        var after = await ReadSnapshotAsync(appended.EventId);

        AssertSnapshotsEqual(before, after);
    }

    [TestMethod]
    public void GovernanceEventStore_DoesNotExposeUpdateOrDeleteMethods()
    {
        var methods = typeof(IGovernanceEventStore)
            .GetMethods()
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                "AppendAsync",
                "GetAsync",
                "ListCausedByAsync",
                "ListForCorrelationAsync",
                "ListForProjectAsync",
                "ListForSubjectAsync"
            },
            methods);

        AssertDoesNotContainAny(methods, "UpdateAsync", "DeleteAsync", "UpsertAsync", "SaveAsync", "MutateAsync", "ReplayAsync", "BackfillAsync");
    }

    [TestMethod]
    public async Task GovernanceEventSqlSurface_DoesNotExposeUpdateOrDeleteProcedures()
    {
        var modules = await GovernanceEventProcedureDefinitionsAsync();
        var forbiddenProcedureNames = await ForbiddenGovernanceEventProcedureNamesAsync();
        var allowedProcedureNames = new[]
        {
            AppendProcedureName,
            GetProcedureName,
            ListForProjectProcedureName,
            ListForCorrelationProcedureName,
            ListForSubjectProcedureName,
            ListCausedByProcedureName
        };

        CollectionAssert.AreEquivalent(allowedProcedureNames, modules.Select(module => module.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.AreEqual(0, forbiddenProcedureNames.Count, $"No sanctioned governance-event mutation procedure expected. Found: {string.Join(", ", forbiddenProcedureNames)}");

        foreach (var module in modules)
        {
            Assert.IsFalse(module.Name.Contains("Update", StringComparison.OrdinalIgnoreCase), $"No sanctioned update procedure expected: {module.Name}");
            Assert.IsFalse(module.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase), $"No sanctioned delete procedure expected: {module.Name}");
            Assert.IsFalse(module.Name.Contains("Replay", StringComparison.OrdinalIgnoreCase), $"No replay procedure expected: {module.Name}");
            Assert.IsFalse(module.Name.Contains("Backfill", StringComparison.OrdinalIgnoreCase), $"No backfill procedure expected: {module.Name}");
        }

        foreach (var module in modules.Where(module => !string.Equals(module.Name, AppendProcedureName, StringComparison.Ordinal)))
        {
            var mutations = GovernanceEventMutations(module.Definition);
            Assert.AreEqual(0, mutations.Length, $"Read procedure must not mutate governance events: {module.Name}. Mutations: {string.Join(", ", mutations)}");
        }
    }

    [TestMethod]
    public async Task GovernanceEventAppendProcedure_DoesNotUpdateOrDeleteExistingEvents()
    {
        var appendDefinition = (await GovernanceEventProcedureDefinitionsAsync()).Single(module => module.Name == AppendProcedureName).Definition;
        var normalized = StripSqlComments(appendDefinition);
        var mutations = GovernanceEventMutations(appendDefinition);

        StringAssert.Contains(normalized, "INSERT INTO governance.GovernanceEvent");
        Assert.IsTrue(mutations.Any(mutation => mutation.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)), "Append procedure must insert a new governance event.");
        Assert.IsFalse(mutations.Any(mutation => mutation.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)), "Append procedure must not update existing governance events.");
        Assert.IsFalse(mutations.Any(mutation => mutation.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)), "Append procedure must not delete existing governance events.");
        Assert.IsFalse(mutations.Any(mutation => mutation.StartsWith("MERGE", StringComparison.OrdinalIgnoreCase)), "Append procedure must not merge governance events.");
    }

    [TestMethod]
    public async Task GovernanceEventDirectDmlBoundary_IsRecordedHonestly()
    {
        var request = NewRequest(nameof(GovernanceEventDirectDmlBoundary_IsRecordedHonestly));
        var appended = await _store.AppendAsync(request);
        var before = await ReadSnapshotAsync(appended.EventId);

        var updateBlocked = await ProbeDirectUpdateAsync(appended.EventId);
        var deleteBlocked = await ProbeDirectDeleteAsync(appended.EventId);

        var after = await ReadSnapshotAsync(appended.EventId);
        AssertSnapshotsEqual(before, after);

        var receipt = ReceiptText();
        if (updateBlocked && deleteBlocked)
        {
            StringAssert.Contains(receipt, "H04 observed direct UPDATE and DELETE blocked by the configured test database trigger path.");
            StringAssert.Contains(receipt, "This is database-level direct-DML evidence for the configured test identity, not proof that every privileged SQL identity is unable to disable or bypass database protections.");
        }
        else
        {
            StringAssert.Contains(receipt, "H04 proves the supported governance-event write/read surface is append-only.");
            StringAssert.Contains(receipt, "H04 does not prove privileged direct-table DML is impossible.");
            StringAssert.Contains(receipt, "A later schema/permission/trigger hardening slice is required if true database-level immutability is required.");
        }
    }

    [TestMethod]
    public void Receipt_RecordsAppendOnlyBoundaryAndLimitations()
    {
        var receipt = ReceiptText();

        AssertContainsAll(
            receipt,
            "H04 does not add a SQL migration.",
            "H04 does not alter the governance-event table.",
            "H04 does not alter stored procedures.",
            "H04 does not add triggers.",
            "H04 does not change permissions.",
            "H04 does not replay events.",
            "H04 does not backfill events.",
            "H04 does not mutate existing governance events.",
            "H04 does not add API/CLI/UI behavior.",
            "H04 does not change workflow/source-apply/rollback/release/deployment authority.",
            "Append-only storage preserves evidence only.",
            "An append-only event is not necessarily a true event.",
            "An immutable lie is still a lie.",
            "Append-only storage is not approval.",
            "Append-only storage is not policy satisfaction.",
            "Append-only storage is not source-apply authority.",
            "Append-only storage is not workflow continuation authority.",
            "Append-only storage is not release readiness.",
            "Append-only storage is not deployment readiness.",
            "H05 - Receipt table/index review.");
    }

    private static GovernanceEventAppendRequest NewRequest(string subjectId)
    {
        var token = Guid.NewGuid().ToString("N");
        return new()
        {
            ProjectId = Guid.NewGuid(),
            EventType = "governance.event.append-only-test",
            ActorType = "integration-test",
            ActorId = $"h04-{token}",
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            SubjectType = "h04-governance-event",
            SubjectId = $"{subjectId}-{token}",
            PayloadVersion = GovernanceEventSchemaVersions.Current,
            PayloadJson = $$"""{"schema":"governance.event.created.v1","purpose":"h04-append-only-proof","token":"{{token}}"}"""
        };
    }

    private async Task<int> CountEventsAsync(Guid eventId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM governance.GovernanceEvent WHERE EventId = @EventId",
            new { EventId = eventId });
    }

    private async Task<GovernanceEventSnapshot> ReadSnapshotAsync(Guid eventId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        var row = await connection.QuerySingleAsync<GovernanceEventSnapshot>(
            """
            SELECT
                EventId,
                ProjectId,
                EventType,
                ActorType,
                ActorId,
                CorrelationId,
                CausationId,
                SubjectType,
                SubjectId,
                PayloadVersion,
                PayloadJson,
                CreatedUtc
            FROM governance.GovernanceEvent
            WHERE EventId = @EventId;
            """,
            new { EventId = eventId });

        return row;
    }

    private async Task<IReadOnlyList<SqlModuleDefinition>> GovernanceEventProcedureDefinitionsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var modules = await connection.QueryAsync<SqlModuleDefinition>(
            """
            SELECT
                p.name AS Name,
                m.definition AS Definition
            FROM sys.procedures p
            INNER JOIN sys.sql_modules m ON m.object_id = p.object_id
            WHERE p.schema_id = SCHEMA_ID(N'governance')
              AND p.name IN
              (
                  N'AppendGovernanceEvent',
                  N'GetGovernanceEvent',
                  N'ListGovernanceEventsForProject',
                  N'ListGovernanceEventsForCorrelation',
                  N'ListGovernanceEventsForSubject',
                  N'ListGovernanceEventsCausedBy'
              )
            ORDER BY p.name;
            """);

        return modules.ToArray();
    }

    private async Task<IReadOnlyList<string>> ForbiddenGovernanceEventProcedureNamesAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var names = await connection.QueryAsync<string>(
            """
            SELECT p.name
            FROM sys.procedures p
            WHERE p.schema_id = SCHEMA_ID(N'governance')
              AND p.name LIKE N'%GovernanceEvent%'
              AND
              (
                  p.name LIKE N'%Update%' OR
                  p.name LIKE N'%Delete%' OR
                  p.name LIKE N'%Upsert%' OR
                  p.name LIKE N'%Replay%' OR
                  p.name LIKE N'%Backfill%' OR
                  p.name LIKE N'%Repair%'
              )
            ORDER BY p.name;
            """);

        return names.ToArray();
    }

    private async Task<bool> ProbeDirectUpdateAsync(Guid eventId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            await connection.ExecuteAsync(
                "UPDATE governance.GovernanceEvent SET ActorId = N'h04-direct-dml-update' WHERE EventId = @EventId",
                new { EventId = eventId },
                transaction);
            return false;
        }
        catch (SqlException)
        {
            return true;
        }
        finally
        {
            await RollbackQuietlyAsync(transaction);
        }
    }

    private async Task<bool> ProbeDirectDeleteAsync(Guid eventId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            await connection.ExecuteAsync(
                "DELETE FROM governance.GovernanceEvent WHERE EventId = @EventId",
                new { EventId = eventId },
                transaction);
            return false;
        }
        catch (SqlException)
        {
            return true;
        }
        finally
        {
            await RollbackQuietlyAsync(transaction);
        }
    }

    private async Task ApplyGovernanceEventMigrationAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var migration = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "Database", "migrate_governance_event.sql"));
        foreach (var batch in SplitSqlBatches(migration))
            await connection.ExecuteAsync(batch);
    }

    private static IReadOnlyList<string> SplitSqlBatches(string sql) =>
        Regex.Split(
                sql.Replace("\r\n", "\n", StringComparison.Ordinal),
                @"(?im)^\s*GO\s*$")
            .Select(batch => batch.Trim())
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .ToArray();

    private static string[] GovernanceEventMutations(string sql)
    {
        var stripped = StripSqlComments(sql);
        return GovernanceEventMutationRegex.Matches(stripped)
            .Select(match => Regex.Replace(match.Value, @"\s+", " ").Trim())
            .ToArray();
    }

    private static string StripSqlComments(string sql)
    {
        var withoutBlockComments = SqlBlockCommentRegex.Replace(sql, string.Empty);
        return SqlLineCommentRegex.Replace(withoutBlockComments, string.Empty);
    }

    private static async Task RollbackQuietlyAsync(SqlTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync();
        }
        catch (InvalidOperationException)
        {
        }
        catch (SqlException)
        {
        }
    }

    private static void AssertSnapshotsEqual(GovernanceEventSnapshot expected, GovernanceEventSnapshot actual)
    {
        Assert.AreEqual(expected.EventId, actual.EventId);
        Assert.AreEqual(expected.ProjectId, actual.ProjectId);
        Assert.AreEqual(expected.EventType, actual.EventType);
        Assert.AreEqual(expected.ActorType, actual.ActorType);
        Assert.AreEqual(expected.ActorId, actual.ActorId);
        Assert.AreEqual(expected.CorrelationId, actual.CorrelationId);
        Assert.AreEqual(expected.CausationId, actual.CausationId);
        Assert.AreEqual(expected.SubjectType, actual.SubjectType);
        Assert.AreEqual(expected.SubjectId, actual.SubjectId);
        Assert.AreEqual(expected.PayloadVersion, actual.PayloadVersion);
        Assert.AreEqual(expected.PayloadJson, actual.PayloadJson);
        Assert.AreEqual(expected.CreatedUtc, actual.CreatedUtc);
    }

    private static void AssertContainsAll(string text, params string[] expected)
    {
        foreach (var value in expected)
            StringAssert.Contains(text, value);
    }

    private static void AssertDoesNotContainAny(IReadOnlyCollection<string> values, params string[] forbidden)
    {
        foreach (var token in forbidden)
            Assert.IsFalse(values.Contains(token, StringComparer.Ordinal), $"Unexpected member found: {token}");
    }

    private static string ReceiptText() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), ReceiptPath));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed record SqlModuleDefinition
    {
        public required string Name { get; init; }
        public required string Definition { get; init; }
    }

    private sealed record GovernanceEventSnapshot
    {
        public required Guid EventId { get; init; }
        public required Guid ProjectId { get; init; }
        public required string EventType { get; init; }
        public required string ActorType { get; init; }
        public required string ActorId { get; init; }
        public Guid? CorrelationId { get; init; }
        public Guid? CausationId { get; init; }
        public string? SubjectType { get; init; }
        public string? SubjectId { get; init; }
        public required int PayloadVersion { get; init; }
        public required string PayloadJson { get; init; }
        public required DateTimeOffset CreatedUtc { get; init; }
    }
}
