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
        await ApplyMigrationAsync("migrate_project_collaboration.sql");
        await DropWorkbenchMigrationObjectsAsync();
        await ApplyMigrationAsync("migrate_workbench_project_start.sql");
        await ApplyMigrationAsync("migrate_workbench_repository_setup.sql");
        await ApplyMigrationAsync("migrate_workbench_agent_runs.sql");
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
    public async Task Start_ReplayReturnsTheStoredResultAfterRenameAndSessionTakeover()
    {
        var actorUserId = await SeedActorAsync();
        var operationId = Guid.NewGuid();
        var service = CreateService(new NoOpProjectStartFailureInjector());
        var command = new StartProjectCommand(1, actorUserId, operationId, "Original project name");
        var first = await service.StartAsync(command);

        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync("""
                UPDATE dbo.Projects SET Name=N'Renamed later' WHERE TenantId=1 AND Id=@ProjectId;
                UPDATE dbo.WorkbenchWriteLeases SET RevokedAtUtc=SYSUTCDATETIME()
                WHERE TenantId=1 AND ProjectId=@ProjectId AND RevokedAtUtc IS NULL;
                UPDATE dbo.WorkbenchSessions SET Status=N'Historical', ClosedAtUtc=SYSUTCDATETIME()
                WHERE TenantId=1 AND ProjectId=@ProjectId AND Id=@OriginalSessionId;
                DECLARE @NewSession TABLE (Id BIGINT);
                INSERT dbo.WorkbenchSessions(TenantId, ProjectId, Status, CreatedByActorUserId)
                OUTPUT inserted.Id INTO @NewSession
                VALUES (1, @ProjectId, N'Active', @ActorUserId);
                INSERT dbo.WorkbenchWriteLeases
                    (TenantId, ProjectId, WorkbenchSessionId, HolderActorUserId, LeaseEpoch, LeaseTokenHash,
                     AcquiredAtUtc, HeartbeatAtUtc, ExpiresAtUtc)
                SELECT 1, @ProjectId, Id, @ActorUserId, 2, REPLICATE('a', 64),
                       SYSUTCDATETIME(), SYSUTCDATETIME(), DATEADD(MINUTE, 30, SYSUTCDATETIME())
                FROM @NewSession;
                """, new { first.ProjectId, OriginalSessionId = first.WorkbenchSessionId, ActorUserId = actorUserId });
        }

        var replay = await service.StartAsync(command);

        Assert.IsTrue(replay.IsReplay);
        Assert.AreEqual(first.ProjectId, replay.ProjectId);
        Assert.AreEqual("Original project name", replay.Name);
        Assert.AreEqual(first.WorkbenchSessionId, replay.WorkbenchSessionId);
        Assert.AreEqual(first.LeaseEpoch, replay.LeaseEpoch);
        Assert.AreEqual(first.CreatedAtUtc, replay.CreatedAtUtc);
    }

    [TestMethod]
    public async Task Open_ResumesForTheHolderAndTakeoverFencesTheOldEpoch()
    {
        var ownerUserId = await SeedActorAsync();
        var start = await CreateService(new NoOpProjectStartFailureInjector()).StartAsync(
            new StartProjectCommand(1, ownerUserId, Guid.NewGuid(), "Lease proof"));
        var entry = new WorkbenchProjectEntryService(ServiceProvider.GetRequiredService<IDbConnectionFactory>());

        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.ExecuteAsync(
                "DELETE dbo.ProjectUnderstandings WHERE TenantId=1 AND ProjectId=@ProjectId;",
                new { start.ProjectId });
        }

        var resumed = await entry.OpenAsync(new OpenWorkbenchProjectCommand(
            1, ownerUserId, start.ProjectId, Guid.NewGuid(), TakeOver: false));
        Assert.IsTrue(resumed.WasResumed);
        Assert.AreEqual(start.WorkbenchSessionId, resumed.WorkbenchSessionId);
        Assert.AreEqual(1L, resumed.LeaseEpoch);
        await using (var connection = new SqlConnection(ConnectionString))
        {
            var understandingJson = await connection.QuerySingleAsync<string>(
                "SELECT UnderstandingJson FROM dbo.ProjectUnderstandings WHERE TenantId=1 AND ProjectId=@ProjectId;",
                new { start.ProjectId });
            var understanding = ProjectUnderstandingDocumentCodec.Deserialize(understandingJson);
            Assert.AreEqual(ProjectUnderstandingContract.SchemaVersion, understanding.SchemaVersion);
            Assert.AreEqual(0, understanding.Facts.Count);
        }

        var secondUserId = await SeedAdditionalActorAsync("takeover");
        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.ExecuteAsync("""
                INSERT dbo.ProjectMembers(TenantId, ProjectId, UserId, ProjectRole, Status, AddedByUserId)
                VALUES (1, @ProjectId, @UserId, N'Contributor', N'Active', @OwnerUserId);
                """, new { start.ProjectId, UserId = secondUserId, OwnerUserId = ownerUserId });
        }

        await Assert.ThrowsExactlyAsync<WorkbenchLeaseTakeoverRequiredException>(() =>
            entry.OpenAsync(new OpenWorkbenchProjectCommand(1, secondUserId, start.ProjectId, Guid.NewGuid(), TakeOver: false)));

        var takenOver = await entry.OpenAsync(new OpenWorkbenchProjectCommand(
            1, secondUserId, start.ProjectId, Guid.NewGuid(), TakeOver: true));
        Assert.IsTrue(takenOver.WasTakenOver);
        Assert.AreEqual(2L, takenOver.LeaseEpoch);
        Assert.AreNotEqual(start.WorkbenchSessionId, takenOver.WorkbenchSessionId);
        Assert.IsFalse(await entry.ValidateAndRenewCurrentWriteLeaseAsync(
            1, ownerUserId, start.ProjectId, start.WorkbenchSessionId, start.LeaseEpoch));
        Assert.IsTrue(await entry.ValidateAndRenewCurrentWriteLeaseAsync(
            1, secondUserId, start.ProjectId, takenOver.WorkbenchSessionId, takenOver.LeaseEpoch));
    }

    [TestMethod]
    public async Task LeaseValidation_RenewsTheCurrentFenceAndRequiresAnActiveActorAndTenantMembership()
    {
        var actorUserId = await SeedActorAsync();
        var start = await CreateService(new NoOpProjectStartFailureInjector()).StartAsync(
            new StartProjectCommand(1, actorUserId, Guid.NewGuid(), "Lease renewal proof"));
        var entry = new WorkbenchProjectEntryService(ServiceProvider.GetRequiredService<IDbConnectionFactory>());

        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync("""
            UPDATE dbo.WorkbenchWriteLeases
            SET HeartbeatAtUtc=DATEADD(MINUTE, -29, SYSUTCDATETIME()),
                ExpiresAtUtc=DATEADD(MINUTE, 1, SYSUTCDATETIME())
            WHERE TenantId=1 AND ProjectId=@ProjectId AND WorkbenchSessionId=@WorkbenchSessionId;
            """, new { start.ProjectId, start.WorkbenchSessionId });

        Assert.IsTrue(await entry.ValidateAndRenewCurrentWriteLeaseAsync(
            1, actorUserId, start.ProjectId, start.WorkbenchSessionId, start.LeaseEpoch));

        var renewed = await connection.QuerySingleAsync<LeaseTimes>("""
            SELECT HeartbeatAtUtc, ExpiresAtUtc
            FROM dbo.WorkbenchWriteLeases
            WHERE TenantId=1 AND ProjectId=@ProjectId AND WorkbenchSessionId=@WorkbenchSessionId;
            """, new { start.ProjectId, start.WorkbenchSessionId });
        Assert.IsTrue(renewed.HeartbeatAtUtc > DateTime.UtcNow.AddMinutes(-1));
        Assert.IsTrue(renewed.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(29));

        await connection.ExecuteAsync(
            "DELETE dbo.TenantUsers WHERE TenantId=1 AND UserId=@ActorUserId;",
            new { ActorUserId = actorUserId });
        Assert.IsFalse(await entry.ValidateAndRenewCurrentWriteLeaseAsync(
            1, actorUserId, start.ProjectId, start.WorkbenchSessionId, start.LeaseEpoch));

        await connection.ExecuteAsync("""
            INSERT dbo.TenantUsers(TenantId, UserId, Role) VALUES (1, @ActorUserId, N'Owner');
            UPDATE dbo.Users SET IsActive=0 WHERE Id=@ActorUserId;
            """, new { ActorUserId = actorUserId });
        Assert.IsFalse(await entry.ValidateAndRenewCurrentWriteLeaseAsync(
            1, actorUserId, start.ProjectId, start.WorkbenchSessionId, start.LeaseEpoch));
    }

    [TestMethod]
    public async Task Open_ConcealsAProjectFromASameTenantNonMember()
    {
        var ownerUserId = await SeedActorAsync();
        var start = await CreateService(new NoOpProjectStartFailureInjector()).StartAsync(
            new StartProjectCommand(1, ownerUserId, Guid.NewGuid(), "Membership proof"));
        var nonMemberUserId = await SeedAdditionalActorAsync("nonmember");
        var entry = new WorkbenchProjectEntryService(ServiceProvider.GetRequiredService<IDbConnectionFactory>());

        await Assert.ThrowsExactlyAsync<WorkbenchProjectNotAccessibleException>(() =>
            entry.OpenAsync(new OpenWorkbenchProjectCommand(
                1, nonMemberUserId, start.ProjectId, Guid.NewGuid(), TakeOver: true)));

        await using var connection = new SqlConnection(ConnectionString);
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(1) FROM dbo.ClientOperations
            WHERE TenantId=1 AND ActorUserId=@ActorUserId AND OperationKind=N'OpenWorkbenchProject';
            """, new { ActorUserId = nonMemberUserId }));
    }

    [TestMethod]
    public async Task Migration_UsesOnlyTheNormativeLifecycleAndReadinessVocabulary()
    {
        var actorUserId = await SeedActorAsync();
        var start = await CreateService(new NoOpProjectStartFailureInjector()).StartAsync(
            new StartProjectCommand(1, actorUserId, Guid.NewGuid(), "Vocabulary proof"));
        await using var connection = new SqlConnection(ConnectionString);

        await connection.ExecuteAsync("""
            INSERT dbo.ProjectLifecyclePhases(TenantId, ProjectId, Revision, Phase, ChangedByActorUserId)
            VALUES (1, @ProjectId, 2, N'Delivery', @ActorUserId);
            INSERT dbo.ProjectReadinessAssessments
                (TenantId, ProjectId, Revision, ExecutionReadiness, ReasonCode, Summary, AssessedByActorUserId)
            VALUES (1, @ProjectId, 2, N'ValidationRequired', N'ValidationRequired', N'Validation is required.', @ActorUserId);
            """, new { start.ProjectId, ActorUserId = actorUserId });

        await Assert.ThrowsExactlyAsync<SqlException>(() => connection.ExecuteAsync("""
            INSERT dbo.ProjectLifecyclePhases(TenantId, ProjectId, Revision, Phase, ChangedByActorUserId)
            VALUES (1, @ProjectId, 3, N'Planning', @ActorUserId);
            """, new { start.ProjectId, ActorUserId = actorUserId }));
        await Assert.ThrowsExactlyAsync<SqlException>(() => connection.ExecuteAsync("""
            INSERT dbo.ProjectReadinessAssessments
                (TenantId, ProjectId, Revision, ExecutionReadiness, ReasonCode, Summary, AssessedByActorUserId)
            VALUES (1, @ProjectId, 3, N'Blocked', N'Blocked', N'Invalid vocabulary.', @ActorUserId);
            """, new { start.ProjectId, ActorUserId = actorUserId }));
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

    private async Task<int> SeedAdditionalActorAsync(string suffix)
    {
        await using var connection = new SqlConnection(ConnectionString);
        var actorUserId = await connection.ExecuteScalarAsync<int>("""
            INSERT dbo.Users(Email, DisplayName, IsActive)
            OUTPUT inserted.Id
            VALUES (@Email, @DisplayName, 1);
            """, new { Email = $"workbench-{suffix}@irondev.local", DisplayName = $"Workbench {suffix}" });
        await connection.ExecuteAsync(
            "INSERT dbo.TenantUsers(TenantId, UserId, Role) VALUES (1, @ActorUserId, N'Member');",
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

    private async Task DropWorkbenchMigrationObjectsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync("""
            IF COL_LENGTH('dbo.ClientOperations', 'ResultAgentRunId') IS NOT NULL
                EXEC sys.sp_executesql N'UPDATE dbo.ClientOperations SET ResultAgentRunId=NULL;';
            DROP TABLE IF EXISTS dbo.TicketProposalCommitmentDependencies;
            DROP TABLE IF EXISTS dbo.TicketProposalCommitmentTickets;
            DROP TABLE IF EXISTS dbo.TicketProposalCommitments;
            DROP TABLE IF EXISTS dbo.TicketProposalSetRevisions;
            DROP TABLE IF EXISTS dbo.TicketProposalSets;
            DROP TABLE IF EXISTS dbo.WorkbenchCommandRejections;
            DROP TABLE IF EXISTS dbo.WorkbenchOutboxEvents;
            DROP TABLE IF EXISTS dbo.WorkbenchBusinessAnalystInvocationAudits;
            DROP TABLE IF EXISTS dbo.WorkbenchBusinessAnalystToolCallAudits;
            DROP TABLE IF EXISTS dbo.WorkbenchBusinessAnalystPreparations;
            DROP TABLE IF EXISTS dbo.WorkbenchAgentRunAttempts;
            DROP TABLE IF EXISTS dbo.ProjectRenameProposals;
            DROP TABLE IF EXISTS dbo.ProjectUnderstandings;
            DROP TABLE IF EXISTS dbo.WorkbenchAgentRuns;
            IF OBJECT_ID('dbo.ClientOperations', 'U') IS NOT NULL
            BEGIN
                IF EXISTS (SELECT 1 FROM sys.foreign_keys
                           WHERE parent_object_id=OBJECT_ID('dbo.ClientOperations')
                             AND name='FK_ClientOperations_RepositoryProvisioningAttempt')
                    ALTER TABLE dbo.ClientOperations
                        DROP CONSTRAINT FK_ClientOperations_RepositoryProvisioningAttempt;
                IF EXISTS (SELECT 1 FROM sys.foreign_keys
                           WHERE parent_object_id=OBJECT_ID('dbo.ClientOperations')
                             AND name='FK_ClientOperations_RepositoryProvisioningReceipt')
                    ALTER TABLE dbo.ClientOperations
                        DROP CONSTRAINT FK_ClientOperations_RepositoryProvisioningReceipt;
                IF EXISTS (SELECT 1 FROM sys.foreign_keys
                           WHERE parent_object_id=OBJECT_ID('dbo.ClientOperations')
                             AND name='FK_ClientOperations_RepositoryProvisioningAttemptAuthority')
                    ALTER TABLE dbo.ClientOperations
                        DROP CONSTRAINT FK_ClientOperations_RepositoryProvisioningAttemptAuthority;
                IF EXISTS (SELECT 1 FROM sys.foreign_keys
                           WHERE parent_object_id=OBJECT_ID('dbo.ClientOperations')
                             AND name='FK_ClientOperations_RepositoryProvisioningReceiptAuthority')
                    ALTER TABLE dbo.ClientOperations
                        DROP CONSTRAINT FK_ClientOperations_RepositoryProvisioningReceiptAuthority;
                IF EXISTS (SELECT 1 FROM sys.foreign_keys
                           WHERE parent_object_id=OBJECT_ID('dbo.ClientOperations')
                             AND name='FK_ClientOperations_SandboxQualificationAttemptAuthority')
                    ALTER TABLE dbo.ClientOperations
                        DROP CONSTRAINT FK_ClientOperations_SandboxQualificationAttemptAuthority;
                IF EXISTS (SELECT 1 FROM sys.foreign_keys
                           WHERE parent_object_id=OBJECT_ID('dbo.ClientOperations')
                             AND name='FK_ClientOperations_SandboxEvidenceManifestAuthority')
                    ALTER TABLE dbo.ClientOperations
                        DROP CONSTRAINT FK_ClientOperations_SandboxEvidenceManifestAuthority;
            END;
            DROP TABLE IF EXISTS dbo.SandboxEvidenceManifests;
            DROP TABLE IF EXISTS dbo.SandboxQualificationAttempts;
            DROP TABLE IF EXISTS dbo.RepositoryProvisioningReceipts;
            DROP TABLE IF EXISTS dbo.RepositoryProvisioningAttempts;
            DROP TABLE IF EXISTS dbo.ClientOperations;
            DROP TABLE IF EXISTS dbo.RepositorySetupConfirmations;
            DROP TABLE IF EXISTS dbo.ProjectExecutionProfileRevisions;
            DROP TABLE IF EXISTS dbo.ProjectExecutionProfiles;
            DROP TABLE IF EXISTS dbo.RepositoryBindingRevisions;
            DROP TABLE IF EXISTS dbo.RepositoryBindings;
            DROP TABLE IF EXISTS dbo.WorkbenchWriteLeases;
            DROP TABLE IF EXISTS dbo.WorkbenchSessions;
            DROP TABLE IF EXISTS dbo.ProjectReadinessAssessments;
            DROP TABLE IF EXISTS dbo.ProjectLifecyclePhases;
            """);
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

    private sealed class LeaseTimes
    {
        public DateTime HeartbeatAtUtc { get; init; }
        public DateTime ExpiresAtUtc { get; init; }
    }
}
