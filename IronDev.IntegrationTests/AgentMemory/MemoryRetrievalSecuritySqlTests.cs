using Dapper;
using IronDev.AI;
using IronDev.Core.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace IronDev.IntegrationTests.AgentMemory;

public sealed partial class ProjectCanonMemoryLifecycleSqlTests
{
    [TestMethod]
    public async Task SqlPromptAssembly_UsesCurrentCanonAndExcludesLegacySelfAssertedAuthorityAndOtherTenantData()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var seed = await InsertSeedAsync(connection);
        TenantContext.TenantId = seed.TenantId;
        await GrantMembershipAsync(connection, seed);

        var stableMemoryId = Guid.NewGuid();
        await CreateIdentityAsync(connection, stableMemoryId, seed);
        await AppendVersionAsync(connection, Guid.NewGuid(), stableMemoryId, seed, "Current", null, null,
            "GOVERNED-CANON-CONTENT is the current durable project rule.");

        await connection.ExecuteAsync("""
            INSERT dbo.ProjectContextDocuments
                (TenantId, ProjectId, DocumentType, AuthorityLevel, Status, Title, Content)
            VALUES
                (@TenantId, @ProjectId, N'Legacy', N'Binding', N'Active', N'Legacy binding', N'LEGACY-BINDING-MUST-NOT-APPEAR in prompt assembly.'),
                (@TenantId, @ProjectId, N'Observation', N'ObservedFact', N'Active', N'Observed context', N'OBSERVED-FACT-MAY-APPEAR as quoted context.');
            """, seed);

        var other = await InsertSeedAsync(connection);
        await connection.ExecuteAsync("""
            INSERT dbo.ProjectContextDocuments
                (TenantId, ProjectId, DocumentType, AuthorityLevel, Status, Title, Content)
            VALUES (@TenantId, @ProjectId, N'Observation', N'ObservedFact', N'Active', N'Other tenant', N'OTHER-TENANT-SECRET-MUST-NOT-APPEAR');
            """, other);

        var context = MemoryRetrievalRequestContext.ForProjectChat(seed.TenantId, seed.ProjectId, seed.UserId, "SqlPromptAssemblyTest");
        var builder = ServiceProvider.GetRequiredService<IPromptContextBuilder>();
        var prompt = await builder.BuildAsync(seed.ProjectId, 0, "Summarize current project context", context);

        StringAssert.Contains(prompt, "GOVERNED-CANON-CONTENT");
        StringAssert.Contains(prompt, "project-canon:Binding");
        StringAssert.Contains(prompt, "OBSERVED-FACT-MAY-APPEAR");
        Assert.IsFalse(prompt.Contains("LEGACY-BINDING-MUST-NOT-APPEAR", StringComparison.Ordinal));
        Assert.IsFalse(prompt.Contains("OTHER-TENANT-SECRET-MUST-NOT-APPEAR", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SqlPromptAssembly_UnauthorizedExplicitContextFailsClosed()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var seed = await InsertSeedAsync(connection);
        TenantContext.TenantId = seed.TenantId;

        var context = MemoryRetrievalRequestContext.ForProjectChat(seed.TenantId, seed.ProjectId, seed.UserId, "UnauthorizedSqlTest");
        var builder = ServiceProvider.GetRequiredService<IPromptContextBuilder>();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => builder.BuildAsync(seed.ProjectId, 0, "Missing context must fail closed", null!));
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(
            () => builder.BuildAsync(seed.ProjectId, 0, "Do not retrieve memory", context));
    }

    private static Task GrantMembershipAsync(SqlConnection connection, Seed seed) =>
        connection.ExecuteAsync("""
            INSERT dbo.TenantUsers(TenantId, UserId, Role) VALUES (@TenantId, @UserId, N'Owner');
            INSERT dbo.ProjectMembers(TenantId, ProjectId, UserId, ProjectRole, AddedByUserId)
            VALUES (@TenantId, @ProjectId, @UserId, N'Owner', @UserId);
            """, seed);
}
