using System.Data;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;

namespace IronDev.IntegrationTests.AgentMemory;

[TestClass]
[TestCategory("RequiresRealDatabase")]
[TestCategory("LongRunning")]
[TestCategory("Store")]
public sealed partial class ProjectCanonMemoryLifecycleSqlTests : IntegrationTestBase
{
    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await RecreateLifecycleSchemaAsync();
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        try
        {
            await RecreateLifecycleSchemaAsync();
        }
        finally
        {
            await base.TestCleanup();
        }
    }

    [TestMethod]
    public async Task ArchiveOfCurrentLeaf_RemovesCurrentProjectionAndPreservesHistory()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var seed = await InsertSeedAsync(connection);
        var stableMemoryId = Guid.NewGuid();
        var firstVersionId = Guid.NewGuid();
        var replacementVersionId = Guid.NewGuid();
        var archiveVersionId = Guid.NewGuid();

        await CreateIdentityAsync(connection, stableMemoryId, seed);
        await AppendVersionAsync(connection, firstVersionId, stableMemoryId, seed, "Current", null, null, "first");
        Assert.AreEqual(firstVersionId, await CurrentVersionAsync(connection, stableMemoryId, seed));

        await AppendVersionAsync(connection, replacementVersionId, stableMemoryId, seed, "Current", firstVersionId, null, "replacement");
        Assert.AreEqual(replacementVersionId, await CurrentVersionAsync(connection, stableMemoryId, seed));

        await AppendVersionAsync(connection, archiveVersionId, stableMemoryId, seed, "Archived", replacementVersionId, DateTime.UtcNow, "archive");
        Assert.IsNull(await CurrentVersionAsync(connection, stableMemoryId, seed));

        var history = (await connection.QueryAsync(
            "memory.usp_ProjectCanonMemory_ListHistory",
            new { seed.TenantId, seed.ProjectId, StableMemoryId = stableMemoryId },
            commandType: CommandType.StoredProcedure)).Select(row => (Guid)row.VersionId).ToArray();
        CollectionAssert.AreEquivalent(new[] { firstVersionId, replacementVersionId, archiveVersionId }, history);

        await AssertSqlFailsAsync(() => connection.ExecuteAsync(
            "UPDATE memory.ProjectCanonMemoryVersion SET Content = N'rewritten' WHERE VersionId = @VersionId",
            new { VersionId = firstVersionId }));
        await AssertSqlFailsAsync(() => connection.ExecuteAsync(
            "DELETE FROM memory.ProjectCanonMemory WHERE StableMemoryId = @StableMemoryId",
            new { StableMemoryId = stableMemoryId }));
    }

    [TestMethod]
    public async Task DirectInsert_CannotSupersedeAVersionFromAnotherMemoryScope()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var seed = await InsertSeedAsync(connection);
        var otherProjectId = await connection.ExecuteScalarAsync<int>(
            "INSERT dbo.Projects(TenantId, Name) OUTPUT INSERTED.Id VALUES (@TenantId, N'Other project')",
            new { seed.TenantId });
        var firstMemoryId = Guid.NewGuid();
        var otherMemoryId = Guid.NewGuid();
        var firstVersionId = Guid.NewGuid();

        await CreateIdentityAsync(connection, firstMemoryId, seed);
        await AppendVersionAsync(connection, firstVersionId, firstMemoryId, seed, "Current", null, null, "first");
        await CreateIdentityAsync(connection, otherMemoryId, seed with { ProjectId = otherProjectId });

        await AssertSqlFailsAsync(() => connection.ExecuteAsync(
            """
            INSERT memory.ProjectCanonMemoryVersion
                (VersionId, StableMemoryId, TenantId, ProjectId, Content, ContentHash, Status,
                 CreatedByUserId, CreatedAtUtc, SourceEvidence, SupersedesVersionId,
                 EffectiveFromUtc, RetiredAtUtc, PromotionReceiptId)
            VALUES
                (@VersionId, @StableMemoryId, @TenantId, @ProjectId, N'cross-scope archive',
                 REPLICATE('A', 64), N'Archived', @UserId, SYSUTCDATETIME(), N'{"source":"sql-test"}',
                 @SupersedesVersionId, NULL, SYSUTCDATETIME(), NEWID())
            """,
            new
            {
                VersionId = Guid.NewGuid(),
                StableMemoryId = otherMemoryId,
                seed.TenantId,
                ProjectId = otherProjectId,
                seed.UserId,
                SupersedesVersionId = firstVersionId
            }));
    }

    [TestMethod]
    public async Task SecondRootCurrent_IsRefused()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var seed = await InsertSeedAsync(connection);
        var stableMemoryId = Guid.NewGuid();
        await CreateIdentityAsync(connection, stableMemoryId, seed);
        await AppendVersionAsync(connection, Guid.NewGuid(), stableMemoryId, seed, "Current", null, null, "first");

        await AssertSqlFailsAsync(() => AppendVersionAsync(
            connection, Guid.NewGuid(), stableMemoryId, seed, "Current", null, null, "second root"));
        Assert.AreEqual(1, await VersionCountAsync(connection, stableMemoryId));
    }

    [TestMethod]
    public async Task SupersedingNonLeafVersion_IsRefused()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var seed = await InsertSeedAsync(connection);
        var stableMemoryId = Guid.NewGuid();
        var firstVersionId = Guid.NewGuid();
        var currentVersionId = Guid.NewGuid();
        await CreateIdentityAsync(connection, stableMemoryId, seed);
        await AppendVersionAsync(connection, firstVersionId, stableMemoryId, seed, "Current", null, null, "first");
        await AppendVersionAsync(connection, currentVersionId, stableMemoryId, seed, "Current", firstVersionId, null, "current");

        await AssertSqlFailsAsync(() => AppendVersionAsync(
            connection, Guid.NewGuid(), stableMemoryId, seed, "Current", firstVersionId, null, "invalid branch"));
        Assert.AreEqual(currentVersionId, await CurrentVersionAsync(connection, stableMemoryId, seed));
    }

    [TestMethod]
    public async Task SecondSuccessor_IsRefused()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var seed = await InsertSeedAsync(connection);
        var stableMemoryId = Guid.NewGuid();
        var firstVersionId = Guid.NewGuid();
        await CreateIdentityAsync(connection, stableMemoryId, seed);
        await AppendVersionAsync(connection, firstVersionId, stableMemoryId, seed, "Current", null, null, "first");
        await AppendVersionAsync(connection, Guid.NewGuid(), stableMemoryId, seed, "Current", firstVersionId, null, "successor");

        await AssertSqlFailsAsync(() => AppendVersionAsync(
            connection, Guid.NewGuid(), stableMemoryId, seed, "Archived", firstVersionId, DateTime.UtcNow, "second successor"));
        Assert.AreEqual(2, await VersionCountAsync(connection, stableMemoryId));
    }

    [TestMethod]
    public async Task ConcurrentInitialVersions_OnlyOneSucceeds()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var seed = await InsertSeedAsync(connection);
        var stableMemoryId = Guid.NewGuid();
        await CreateIdentityAsync(connection, stableMemoryId, seed);

        var results = await Task.WhenAll(
            TryAppendVersionAsync(stableMemoryId, seed, Guid.NewGuid(), null, "initial-a"),
            TryAppendVersionAsync(stableMemoryId, seed, Guid.NewGuid(), null, "initial-b"));

        Assert.AreEqual(1, results.Count(success => success));
        Assert.AreEqual(1, await VersionCountAsync(connection, stableMemoryId));
    }

    [TestMethod]
    public async Task ConcurrentSuccessors_OnlyOneSucceeds()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var seed = await InsertSeedAsync(connection);
        var stableMemoryId = Guid.NewGuid();
        var firstVersionId = Guid.NewGuid();
        await CreateIdentityAsync(connection, stableMemoryId, seed);
        await AppendVersionAsync(connection, firstVersionId, stableMemoryId, seed, "Current", null, null, "first");

        var results = await Task.WhenAll(
            TryAppendVersionAsync(stableMemoryId, seed, Guid.NewGuid(), firstVersionId, "successor-a"),
            TryAppendVersionAsync(stableMemoryId, seed, Guid.NewGuid(), firstVersionId, "successor-b"));

        Assert.AreEqual(1, results.Count(success => success));
        Assert.AreEqual(2, await VersionCountAsync(connection, stableMemoryId));
    }

    private static Task CreateIdentityAsync(SqlConnection connection, Guid stableMemoryId, Seed seed) =>
        connection.ExecuteAsync(
            "memory.usp_ProjectCanonMemory_CreateIdentity",
            new
            {
                StableMemoryId = stableMemoryId,
                seed.TenantId,
                seed.ProjectId,
                Title = $"Canon {stableMemoryId:N}",
                CreatedByUserId = seed.UserId,
                CreatedAtUtc = DateTime.UtcNow
            },
            commandType: CommandType.StoredProcedure);

    private static Task AppendVersionAsync(
        SqlConnection connection,
        Guid versionId,
        Guid stableMemoryId,
        Seed seed,
        string status,
        Guid? supersedesVersionId,
        DateTime? retiredAtUtc,
        string content) =>
        connection.ExecuteAsync(
            "memory.usp_ProjectCanonMemory_AppendVersion",
            new
            {
                VersionId = versionId,
                StableMemoryId = stableMemoryId,
                seed.TenantId,
                seed.ProjectId,
                Content = content,
                ContentHash = new string('A', 64),
                Status = status,
                CreatedByUserId = seed.UserId,
                CreatedAtUtc = DateTime.UtcNow,
                SourceEvidence = "{\"source\":\"sql-test\"}",
                SupersedesVersionId = supersedesVersionId,
                EffectiveFromUtc = status == "Current" ? (DateTime?)DateTime.UtcNow : null,
                RetiredAtUtc = retiredAtUtc,
                PromotionReceiptId = Guid.NewGuid()
            },
            commandType: CommandType.StoredProcedure);

    private static Task<Guid?> CurrentVersionAsync(SqlConnection connection, Guid stableMemoryId, Seed seed) =>
        connection.QuerySingleOrDefaultAsync<Guid?>(
            "SELECT VersionId FROM memory.vw_CurrentProjectCanonMemory WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND StableMemoryId = @StableMemoryId",
            new { seed.TenantId, seed.ProjectId, StableMemoryId = stableMemoryId });

    private static Task<int> VersionCountAsync(SqlConnection connection, Guid stableMemoryId) =>
        connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM memory.ProjectCanonMemoryVersion WHERE StableMemoryId = @StableMemoryId",
            new { StableMemoryId = stableMemoryId });

    private async Task<bool> TryAppendVersionAsync(
        Guid stableMemoryId,
        Seed seed,
        Guid versionId,
        Guid? supersedesVersionId,
        string content)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        try
        {
            await AppendVersionAsync(connection, versionId, stableMemoryId, seed, "Current", supersedesVersionId, null, content);
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }

    private static async Task<Seed> InsertSeedAsync(SqlConnection connection)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var tenantId = await connection.ExecuteScalarAsync<int>(
            "INSERT dbo.Tenants(Name, Slug) OUTPUT INSERTED.Id VALUES (@Name, @Slug)",
            new { Name = $"CLN25 {suffix}", Slug = $"cln25-{suffix}" });
        var userId = await connection.ExecuteScalarAsync<int>(
            "INSERT dbo.Users(Email, DisplayName) OUTPUT INSERTED.Id VALUES (@Email, N'CLN25 User')",
            new { Email = $"cln25-{suffix}@irondev.local" });
        var projectId = await connection.ExecuteScalarAsync<int>(
            "INSERT dbo.Projects(TenantId, Name) OUTPUT INSERTED.Id VALUES (@TenantId, N'CLN25 Project')",
            new { TenantId = tenantId });
        return new Seed(tenantId, projectId, userId);
    }

    private async Task RecreateLifecycleSchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        if (await connection.ExecuteScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'dbo.ProjectMembers', N'U') IS NULL THEN 1 ELSE 0 END") == 1)
        {
            var collaborationSql = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "Database", "migrate_project_collaboration.sql"));
            foreach (var batch in Regex.Split(collaborationSql, @"(?im)^\s*GO\s*$").Select(value => value.Trim()).Where(value => value.Length > 0))
                await connection.ExecuteAsync(batch);
        }
        await connection.ExecuteAsync("""
            IF SCHEMA_ID(N'memory') IS NULL EXEC(N'CREATE SCHEMA memory');
            IF OBJECT_ID(N'memory.usp_ProjectCanonMemory_ListHistory', N'P') IS NOT NULL DROP PROCEDURE memory.usp_ProjectCanonMemory_ListHistory;
            IF OBJECT_ID(N'memory.usp_ProjectCanonMemory_GetCurrent', N'P') IS NOT NULL DROP PROCEDURE memory.usp_ProjectCanonMemory_GetCurrent;
            IF OBJECT_ID(N'memory.usp_ProjectCanonMemory_AppendVersion', N'P') IS NOT NULL DROP PROCEDURE memory.usp_ProjectCanonMemory_AppendVersion;
            IF OBJECT_ID(N'memory.usp_ProjectCanonMemory_CreateIdentity', N'P') IS NOT NULL DROP PROCEDURE memory.usp_ProjectCanonMemory_CreateIdentity;
            IF OBJECT_ID(N'memory.vw_CurrentProjectCanonMemory', N'V') IS NOT NULL DROP VIEW memory.vw_CurrentProjectCanonMemory;
            IF OBJECT_ID(N'memory.ProjectCanonMemoryVersion', N'U') IS NOT NULL DROP TABLE memory.ProjectCanonMemoryVersion;
            IF OBJECT_ID(N'memory.ProjectCanonMemory', N'U') IS NOT NULL DROP TABLE memory.ProjectCanonMemory;
            """);

        var sql = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "Database", "migrate_project_canon_memory_lifecycle.sql"));
        foreach (var batch in Regex.Split(sql, @"(?im)^\s*GO\s*$").Select(value => value.Trim()).Where(value => value.Length > 0))
            await connection.ExecuteAsync(batch);
    }

    private static async Task AssertSqlFailsAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (SqlException)
        {
            return;
        }

        Assert.Fail("Expected SQL Server to refuse the lifecycle mutation.");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed record Seed(int TenantId, int ProjectId, int UserId);
}
