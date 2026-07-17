using System.Text.RegularExpressions;
using Dapper;
using IronDev.Core.Workbench;
using IronDev.Data;
using IronDev.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class WorkbenchProjectStartTests : IntegrationTestBase
{
    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await ApplyMigrationAsync("migrate_user_mutation_attribution.sql");
        await ApplyMigrationAsync("migrate_workbench_project_start.sql");
    }

    [TestMethod]
    public async Task Start_IsAtomicRepositoryIndependentAndIdempotent()
    {
        var actorUserId = await SeedActorAsync();
        var operationId = Guid.NewGuid();
        var service = CreateService(new NoOpProjectStartFailureInjector());
        var command = new StartProjectCommand(1, actorUserId, operationId, "Idea / any technology: phase one");

        var first = await service.StartAsync(command);
        var replay = await service.StartAsync(command);

        Assert.AreEqual(first.ProjectId, replay.ProjectId);
        Assert.AreEqual(first.WorkbenchSessionId, replay.WorkbenchSessionId);
        Assert.IsFalse(first.IsReplay);
        Assert.IsTrue(replay.IsReplay);
        Assert.AreEqual(ProjectLifecyclePhases.Shaping, first.ProjectLifecyclePhase);
        Assert.AreEqual(ProjectExecutionReadinessStates.NotConfigured, first.ExecutionReadiness);
        Assert.AreEqual(1L, first.LeaseEpoch);
        Assert.IsNull(first.RepositoryBinding);

        await using var connection = new SqlConnection(ConnectionString);
        var aggregate = await connection.QuerySingleAsync<AggregateCounts>("""
            SELECT
                (SELECT COUNT(1) FROM dbo.Projects WHERE TenantId=1 AND Id=@ProjectId AND LocalPath IS NULL) AS Projects,
                (SELECT COUNT(1) FROM dbo.ProjectMembers WHERE TenantId=1 AND ProjectId=@ProjectId AND UserId=@ActorUserId AND ProjectRole=N'Owner' AND Status=N'Active') AS OwnerMemberships,
                (SELECT COUNT(1) FROM dbo.ProjectUnderstandings WHERE TenantId=1 AND ProjectId=@ProjectId AND Revision=1) AS Understandings,
                (SELECT COUNT(1) FROM dbo.ProjectReadinessAssessments WHERE TenantId=1 AND ProjectId=@ProjectId AND Revision=1 AND ExecutionReadiness=N'NotConfigured') AS ReadinessAssessments,
                (SELECT COUNT(1) FROM dbo.WorkbenchSessions WHERE TenantId=1 AND ProjectId=@ProjectId AND Id=@WorkbenchSessionId) AS Sessions,
                (SELECT COUNT(1) FROM dbo.WorkbenchWriteLeases WHERE TenantId=1 AND ProjectId=@ProjectId AND WorkbenchSessionId=@WorkbenchSessionId AND LeaseEpoch=1) AS Leases,
                (SELECT COUNT(1) FROM dbo.ClientOperations WHERE TenantId=1 AND ActorUserId=@ActorUserId AND ClientOperationId=@ClientOperationId AND Status=N'Completed') AS Operations,
                (SELECT COUNT(1) FROM dbo.WorkbenchOutboxEvents WHERE TenantId=1 AND ProjectId=@ProjectId AND ClientOperationId=@ClientOperationId) AS OutboxEvents;
            """, new
        {
            first.ProjectId,
            ActorUserId = actorUserId,
            first.WorkbenchSessionId,
            ClientOperationId = operationId
        });

        Assert.AreEqual(1, aggregate.Projects);
        Assert.AreEqual(1, aggregate.OwnerMemberships);
        Assert.AreEqual(1, aggregate.Understandings);
        Assert.AreEqual(1, aggregate.ReadinessAssessments);
        Assert.AreEqual(1, aggregate.Sessions);
        Assert.AreEqual(1, aggregate.Leases);
        Assert.AreEqual(1, aggregate.Operations);
        Assert.AreEqual(2, aggregate.OutboxEvents);
    }

    [TestMethod]
    public async Task Start_RejectsChangedPayloadForTheSameOperationId()
    {
        var actorUserId = await SeedActorAsync();
        var operationId = Guid.NewGuid();
        var service = CreateService(new NoOpProjectStartFailureInjector());

        await service.StartAsync(new StartProjectCommand(1, actorUserId, operationId, "First idea"));

        await Assert.ThrowsExactlyAsync<ProjectStartOperationMismatchException>(() =>
            service.StartAsync(new StartProjectCommand(1, actorUserId, operationId, "Changed idea")));

        await using var connection = new SqlConnection(ConnectionString);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM dbo.Projects WHERE TenantId=1;"));
    }

    [TestMethod]
    public async Task Start_RollsBackEveryRecordWhenARequiredWriteFails()
    {
        var actorUserId = await SeedActorAsync();
        var service = CreateService(new ThrowAt(ProjectStartFailurePoint.OwnerMembershipCreated));

        await Assert.ThrowsExactlyAsync<InjectedProjectStartFailure>(() =>
            service.StartAsync(new StartProjectCommand(1, actorUserId, Guid.NewGuid(), "Rollback proof")));

        await using var connection = new SqlConnection(ConnectionString);
        var counts = await connection.QuerySingleAsync<RollbackCounts>("""
            SELECT
                (SELECT COUNT(1) FROM dbo.Projects WHERE TenantId=1) AS Projects,
                (SELECT COUNT(1) FROM dbo.ProjectMembers WHERE TenantId=1) AS Members,
                (SELECT COUNT(1) FROM dbo.ClientOperations WHERE TenantId=1) AS Operations;
            """);
        Assert.AreEqual(0, counts.Projects);
        Assert.AreEqual(0, counts.Members);
        Assert.AreEqual(0, counts.Operations);
    }

    private IProjectStartService CreateService(IProjectStartFailureInjector injector) =>
        new ProjectStartService(ServiceProvider.GetRequiredService<IDbConnectionFactory>(), injector);

    private async Task<int> SeedActorAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("""
            SET IDENTITY_INSERT dbo.Tenants ON;
            INSERT dbo.Tenants(Id, Name, Slug) VALUES (1, N'Workbench Test', N'workbench-test');
            SET IDENTITY_INSERT dbo.Tenants OFF;
            """);
        var actorUserId = await connection.ExecuteScalarAsync<int>("""
            INSERT dbo.Users(Email, DisplayName, IsActive)
            OUTPUT inserted.Id
            VALUES (N'workbench-test@irondev.local', N'Workbench Tester', 1);
            """);
        await connection.ExecuteAsync(
            "INSERT dbo.TenantUsers(TenantId, UserId, Role) VALUES (1, @ActorUserId, N'Owner');",
            new { ActorUserId = actorUserId });
        return actorUserId;
    }

    private async Task ApplyMigrationAsync(string fileName)
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "Database", fileName));
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (var batch in Regex.Split(sql.Replace("\r\n", "\n", StringComparison.Ordinal), @"(?im)^\s*GO\s*$"))
        {
            if (!string.IsNullOrWhiteSpace(batch)) await connection.ExecuteAsync(batch);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class ThrowAt(ProjectStartFailurePoint target) : IProjectStartFailureInjector
    {
        public void ThrowIfRequested(ProjectStartFailurePoint point)
        {
            if (point == target) throw new InjectedProjectStartFailure();
        }
    }

    private sealed class InjectedProjectStartFailure : Exception;

    private sealed class AggregateCounts
    {
        public int Projects { get; init; }
        public int OwnerMemberships { get; init; }
        public int Understandings { get; init; }
        public int ReadinessAssessments { get; init; }
        public int Sessions { get; init; }
        public int Leases { get; init; }
        public int Operations { get; init; }
        public int OutboxEvents { get; init; }
    }

    private sealed class RollbackCounts
    {
        public int Projects { get; init; }
        public int Members { get; init; }
        public int Operations { get; init; }
    }
}
