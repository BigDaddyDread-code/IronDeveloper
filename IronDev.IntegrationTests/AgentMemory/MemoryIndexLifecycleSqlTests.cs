using System.Data;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;

namespace IronDev.IntegrationTests.AgentMemory;

[TestClass]
[TestCategory("RequiresRealDatabase")]
[TestCategory("LongRunning")]
[TestCategory("Store")]
public sealed class MemoryIndexLifecycleSqlTests : IntegrationTestBase
{
    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await RecreateSchemaAsync();
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        try { await DropSchemaAsync(); }
        finally { await base.TestCleanup(); }
    }

    [TestMethod]
    public async Task RequestedReindexLifecycle_ReachesRebuiltCurrentStateAndPreservesHistory()
    {
        await using var connection = await OpenAsync();
        var seed = await InsertSeedAsync(connection);
        var sourceId = Guid.NewGuid().ToString("N");
        var at = DateTime.UtcNow;

        await RecordAsync(connection, seed, sourceId, "v1", "SourceCreated", at);
        await RecordAsync(connection, seed, sourceId, "v1", "EmbeddingQueued", at.AddTicks(1));
        await RecordAsync(connection, seed, sourceId, "v1", "EmbeddingCompleted", at.AddTicks(2));
        await RecordAsync(connection, seed, sourceId, "v1", "StaleDetected", at.AddTicks(3));
        await RecordAsync(connection, seed, sourceId, "v1", "ReindexRequested", at.AddTicks(4));
        await RecordAsync(connection, seed, sourceId, "v1", "EmbeddingQueued", at.AddTicks(5));
        await RecordAsync(connection, seed, sourceId, "v1", "EmbeddingCompleted", at.AddTicks(6));
        await RecordAsync(connection, seed, sourceId, "v1", "ReindexCompleted", at.AddTicks(7));
        await RecordAsync(connection, seed, sourceId, "v1", "DerivedIndexRebuilt", at.AddTicks(8));

        Assert.AreEqual("DerivedIndexRebuilt", await CurrentEventAsync(connection, seed, sourceId));
        Assert.AreEqual(9, await EventCountAsync(connection, seed, sourceId));
    }

    [TestMethod]
    public async Task ReindexCompletedWithoutRequest_IsRefused()
    {
        await using var connection = await OpenAsync();
        var seed = await InsertSeedAsync(connection);
        var sourceId = Guid.NewGuid().ToString("N");
        var at = DateTime.UtcNow;
        await RecordAsync(connection, seed, sourceId, "v1", "SourceCreated", at);
        await RecordAsync(connection, seed, sourceId, "v1", "EmbeddingQueued", at.AddTicks(1));
        await RecordAsync(connection, seed, sourceId, "v1", "EmbeddingCompleted", at.AddTicks(2));

        await AssertSqlFailsAsync(() => RecordAsync(connection, seed, sourceId, "v1", "ReindexCompleted", at.AddTicks(3)));
        Assert.AreEqual("EmbeddingCompleted", await CurrentEventAsync(connection, seed, sourceId));
    }

    [TestMethod]
    public async Task EventsMustMatchActiveSourceVersion_AndUpdatesMustIntroduceANewVersion()
    {
        await using var connection = await OpenAsync();
        var seed = await InsertSeedAsync(connection);
        var sourceId = Guid.NewGuid().ToString("N");
        var at = DateTime.UtcNow;
        await RecordAsync(connection, seed, sourceId, "v1", "SourceCreated", at);
        await RecordAsync(connection, seed, sourceId, "v1", "EmbeddingQueued", at.AddTicks(1));
        await RecordAsync(connection, seed, sourceId, "v1", "EmbeddingCompleted", at.AddTicks(2));

        await AssertSqlFailsAsync(() => RecordAsync(connection, seed, sourceId, "v1", "SourceUpdated", at.AddTicks(3)));
        await RecordAsync(connection, seed, sourceId, "v2", "SourceUpdated", at.AddTicks(4));
        await AssertSqlFailsAsync(() => RecordAsync(connection, seed, sourceId, "v1", "EmbeddingQueued", at.AddTicks(5)));
        await RecordAsync(connection, seed, sourceId, "v2", "EmbeddingQueued", at.AddTicks(6));
    }

    [TestMethod]
    public async Task ArchiveThenDerivedDelete_IsTerminalAndAppendOnly()
    {
        await using var connection = await OpenAsync();
        var seed = await InsertSeedAsync(connection);
        var sourceId = Guid.NewGuid().ToString("N");
        var at = DateTime.UtcNow;
        await RecordAsync(connection, seed, sourceId, "v1", "SourceCreated", at);
        await RecordAsync(connection, seed, sourceId, "v1", "SourceArchived", at.AddTicks(1));
        await RecordAsync(connection, seed, sourceId, "v1", "DerivedIndexDeleted", at.AddTicks(2));

        await AssertSqlFailsAsync(() => RecordAsync(connection, seed, sourceId, "v1", "SourceArchived", at.AddTicks(3)));
        await AssertSqlFailsAsync(() => connection.ExecuteAsync(
            "UPDATE dbo.MemoryIndexLifecycleEvents SET EventType=N'SourceCreated' WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND SourceEntityId=@SourceId",
            new { seed.TenantId, seed.ProjectId, SourceId = sourceId }));
        await AssertSqlFailsAsync(() => connection.ExecuteAsync(
            "DELETE dbo.MemoryIndexLifecycleEvents WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND SourceEntityId=@SourceId",
            new { seed.TenantId, seed.ProjectId, SourceId = sourceId }));
        Assert.AreEqual("DerivedIndexDeleted", await CurrentEventAsync(connection, seed, sourceId));
    }

    [TestMethod]
    public async Task ConcurrentSourceCreated_OnlyOneSucceeds()
    {
        await using var seedConnection = await OpenAsync();
        var seed = await InsertSeedAsync(seedConnection);
        var sourceId = Guid.NewGuid().ToString("N");
        var at = DateTime.UtcNow;
        var results = await Task.WhenAll(
            TryRecordAsync(seed, sourceId, "v1", "SourceCreated", at),
            TryRecordAsync(seed, sourceId, "v1", "SourceCreated", at.AddTicks(1)));

        Assert.AreEqual(1, results.Count(result => result));
        Assert.AreEqual(1, await EventCountAsync(seedConnection, seed, sourceId));
    }

    [TestMethod]
    public async Task DerivedEventRequiresProvider_AndTimestampsAreMonotonic()
    {
        await using var connection = await OpenAsync();
        var seed = await InsertSeedAsync(connection);
        var sourceId = Guid.NewGuid().ToString("N");
        var at = DateTime.UtcNow;
        await RecordAsync(connection, seed, sourceId, "v1", "SourceCreated", at);
        await AssertSqlFailsAsync(() => RecordAsync(connection, seed, sourceId, "v1", "EmbeddingQueued", at.AddTicks(1), provider: null));
        await AssertSqlFailsAsync(() => RecordAsync(connection, seed, sourceId, "v1", "SourceArchived", at.AddSeconds(-1)));
    }

    private async Task<bool> TryRecordAsync(Seed seed, string sourceId, string version, string eventType, DateTime occurredAtUtc)
    {
        await using var connection = await OpenAsync();
        try { await RecordAsync(connection, seed, sourceId, version, eventType, occurredAtUtc); return true; }
        catch (SqlException) { return false; }
    }

    private static Task RecordAsync(
        SqlConnection connection, Seed seed, string sourceId, string version, string eventType,
        DateTime occurredAtUtc, string? provider = "SqlTest") =>
        connection.ExecuteAsync(
            "dbo.usp_MemoryIndexLifecycleEvent_Record",
            new
            {
                seed.TenantId,
                seed.ProjectId,
                SourceEntityType = "ProjectContextDocument",
                SourceEntityId = sourceId,
                SourceVersionId = version,
                EventType = eventType,
                SourceContentHash = new string('A', 64),
                ProviderName = eventType.StartsWith("Source", StringComparison.Ordinal) ? null : provider,
                CorrelationId = Guid.NewGuid(),
                ActorUserId = seed.UserId,
                OccurredAtUtc = occurredAtUtc,
                DetailJson = "{\"source\":\"sql-test\"}"
            },
            commandType: CommandType.StoredProcedure);

    private static Task<string?> CurrentEventAsync(SqlConnection connection, Seed seed, string sourceId) =>
        connection.QuerySingleOrDefaultAsync<string?>(
            "SELECT EventType FROM dbo.vw_CurrentMemoryIndexLifecycle WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND SourceEntityId=@SourceId",
            new { seed.TenantId, seed.ProjectId, SourceId = sourceId });

    private static Task<int> EventCountAsync(SqlConnection connection, Seed seed, string sourceId) =>
        connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.MemoryIndexLifecycleEvents WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND SourceEntityId=@SourceId",
            new { seed.TenantId, seed.ProjectId, SourceId = sourceId });

    private static async Task<Seed> InsertSeedAsync(SqlConnection connection)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var tenantId = await connection.ExecuteScalarAsync<int>(
            "INSERT dbo.Tenants(Name, Slug) OUTPUT INSERTED.Id VALUES (@Name, @Slug)",
            new { Name = $"CLN27 {suffix}", Slug = $"cln27-{suffix}" });
        var userId = await connection.ExecuteScalarAsync<int>(
            "INSERT dbo.Users(Email, DisplayName) OUTPUT INSERTED.Id VALUES (@Email, N'CLN27 User')",
            new { Email = $"cln27-{suffix}@irondev.local" });
        var projectId = await connection.ExecuteScalarAsync<int>(
            "INSERT dbo.Projects(TenantId, Name) OUTPUT INSERTED.Id VALUES (@TenantId, N'CLN27 Project')",
            new { TenantId = tenantId });
        return new Seed(tenantId, projectId, userId);
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private async Task RecreateSchemaAsync()
    {
        await DropSchemaAsync();
        await using var connection = await OpenAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "Database", "migrate_memory_index_lifecycle.sql"));
        foreach (var batch in Regex.Split(sql, @"(?im)^\s*GO\s*$").Select(value => value.Trim()).Where(value => value.Length > 0))
            await connection.ExecuteAsync(batch);
    }

    private async Task DropSchemaAsync()
    {
        await using var connection = await OpenAsync();
        await connection.ExecuteAsync("""
            IF OBJECT_ID(N'dbo.usp_MemoryIndexLifecycleEvent_Record', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_MemoryIndexLifecycleEvent_Record;
            IF OBJECT_ID(N'dbo.vw_CurrentMemoryIndexLifecycle', N'V') IS NOT NULL DROP VIEW dbo.vw_CurrentMemoryIndexLifecycle;
            IF OBJECT_ID(N'dbo.MemoryIndexLifecycleEvents', N'U') IS NOT NULL DROP TABLE dbo.MemoryIndexLifecycleEvents;
            """);
    }

    private static async Task AssertSqlFailsAsync(Func<Task> action)
    {
        try { await action(); }
        catch (SqlException) { return; }
        Assert.Fail("Expected SQL Server to refuse the lifecycle operation.");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed record Seed(int TenantId, int ProjectId, int UserId);
}
