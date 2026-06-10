using System.Reflection;
using System.Text.RegularExpressions;
using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Core.AgentMemory.Execution;
using IronDev.Core.Agents.Skills;
using IronDev.Data;
using IronDev.Infrastructure.AgentMemory;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class MemoryStoredProcedurePermissionTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropAgentMemorySchemaAsync();
        await ApplyAgentMemoryMigrationsAsync();
        await EnsureRuntimeTestUserAsync();
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        try
        {
            await DropAgentMemorySchemaAsync();
        }
        catch
        {
            // Test cleanup should not hide the original assertion failure.
        }

        await base.TestCleanup();
    }

    [TestMethod]
    public async Task MemoryStoredProcedurePermission_MigrationCreatesProceduresRolePermissionsAndTriggers()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var procedureCount = await connection.QuerySingleAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.procedures
            WHERE schema_id = SCHEMA_ID('agent')
              AND name IN
              (
                'usp_AgentLocalMemory_Create',
                'usp_AgentLocalMemory_AddEvent',
                'usp_AgentMemoryInfluence_Create',
                'usp_AgentMemoryHandoff_Create',
                'usp_MemoryImprovementProposal_Create',
                'usp_MemoryImprovementProposal_AddEvent',
                'usp_MemoryIndexQueue_Create',
                'usp_MemoryIndexEvent_Add',
                'usp_MemoryExecutionAudit_Create'
              );
            """);

        var roleExists = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM sys.database_principals WHERE name = 'IronDevMemoryRuntimeRole' AND type = 'R';");

        var executeGrantCount = await connection.QuerySingleAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.database_permissions p
            INNER JOIN sys.objects o ON o.object_id = p.major_id
            WHERE p.grantee_principal_id = DATABASE_PRINCIPAL_ID('IronDevMemoryRuntimeRole')
              AND p.permission_name = 'EXECUTE'
              AND p.state_desc IN ('GRANT', 'GRANT_WITH_GRANT_OPTION')
              AND o.schema_id = SCHEMA_ID('agent')
              AND o.name LIKE 'usp_%';
            """);

        var denyCount = await connection.QuerySingleAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.database_permissions p
            INNER JOIN sys.objects o ON o.object_id = p.major_id
            WHERE p.grantee_principal_id = DATABASE_PRINCIPAL_ID('IronDevMemoryRuntimeRole')
              AND p.state_desc = 'DENY'
              AND p.permission_name IN ('INSERT', 'UPDATE', 'DELETE')
              AND o.name IN
              (
                'AgentLocalMemoryItem',
                'AgentLocalMemoryEvidenceRef',
                'AgentLocalMemoryEvent',
                'AgentMemoryInfluenceRecord',
                'AgentMemoryHandoffSlice',
                'AgentMemoryImprovementProposal',
                'AgentMemoryImprovementProposalEvent',
                'AgentMemoryIndexQueue',
                'AgentMemoryIndexEvent',
                'AgentMemoryExecutionAudit'
              );
            """);

        var appendOnlyTriggerCount = await connection.QuerySingleAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.triggers
            WHERE name IN
            (
                'TR_AgentLocalMemoryItem_BlockUpdateDelete',
                'TR_AgentLocalMemoryEvidenceRef_BlockUpdateDelete',
                'TR_AgentLocalMemoryEvent_BlockUpdateDelete',
                'TR_AgentMemoryInfluenceRecord_BlockUpdateDelete',
                'TR_AgentMemoryHandoffSlice_BlockUpdateDelete',
                'TR_AgentMemoryImprovementProposal_BlockUpdateDelete',
                'TR_AgentMemoryImprovementProposalEvent_BlockUpdateDelete',
                'TR_AgentMemoryIndexQueue_BlockUpdateDelete',
                'TR_AgentMemoryIndexEvent_BlockUpdateDelete',
                'TR_AgentMemoryExecutionAudit_BlockUpdateDelete'
            );
            """);

        Assert.AreEqual(9, procedureCount);
        Assert.AreEqual(1, roleExists);
        Assert.AreEqual(9, executeGrantCount);
        Assert.AreEqual(30, denyCount);
        Assert.AreEqual(10, appendOnlyTriggerCount);
    }

    [TestMethod]
    public async Task MemoryStoredProcedurePermission_RestrictedRoleCanExecuteProcedureButCannotDirectlyMutateTables()
    {
        await ExpectSqlFailsAsync(
            """
            EXECUTE AS USER = 'IronDevMemoryRuntimeTestUser';
            INSERT INTO agent.AgentLocalMemoryItem
            (
                MemoryItemId,
                TenantId,
                ProjectId,
                CampaignId,
                RunId,
                AgentId,
                MemoryType,
                AuthorityLevel,
                Title,
                Summary,
                Confidence,
                CreatedAtUtc
            )
            VALUES
            (
                'direct-denied',
                'tenant-1',
                'project-1',
                'campaign-1',
                'run-1',
                'builder-agent',
                3,
                1,
                'Direct denied',
                'Direct table insert should fail.',
                0.9,
                SYSUTCDATETIME()
            );
            REVERT;
            """);

        await ExecuteSqlAsync(
            """
            EXECUTE AS USER = 'IronDevMemoryRuntimeTestUser';
            EXEC agent.usp_AgentLocalMemory_Create
                @MemoryItemId = 'proc-allowed',
                @TenantId = 'tenant-1',
                @ProjectId = 'project-1',
                @CampaignId = 'campaign-1',
                @RunId = 'run-1',
                @AgentId = 'builder-agent',
                @MemoryType = 3,
                @AuthorityLevel = 1,
                @Title = 'Procedure allowed',
                @Summary = 'Procedure write should pass through approved boundary.',
                @Confidence = 0.9,
                @CreatedAtUtc = '2026-06-10T00:00:00',
                @CreatedByAgentId = 'builder-agent',
                @EvidenceRefsJson = '[{"evidenceId":"evidence-proc","evidenceType":1,"sourceId":"source-proc"}]';
            REVERT;
            """);

        await ExpectSqlFailsAsync(
            """
            EXECUTE AS USER = 'IronDevMemoryRuntimeTestUser';
            UPDATE agent.AgentLocalMemoryItem SET Title = 'mutated' WHERE MemoryItemId = 'proc-allowed';
            REVERT;
            """);

        await ExpectSqlFailsAsync(
            """
            EXECUTE AS USER = 'IronDevMemoryRuntimeTestUser';
            DELETE FROM agent.AgentLocalMemoryItem WHERE MemoryItemId = 'proc-allowed';
            REVERT;
            """);

        await ExpectSqlFailsAsync(
            """
            EXECUTE AS USER = 'IronDevMemoryRuntimeTestUser';
            ALTER SCHEMA agent TRANSFER dbo.__does_not_exist__;
            REVERT;
            """);

        var count = await QuerySingleAsync<int>("SELECT COUNT(*) FROM agent.AgentLocalMemoryItem WHERE MemoryItemId = 'proc-allowed';");
        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public async Task MemoryStoredProcedurePermission_RuntimeStoresWriteThroughProcedures()
    {
        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        var scope = BuildScope();
        var evidence = BuildEvidence("evidence-store", "source-store");

        var memoryStore = new SqlAgentLocalMemoryStore(connectionFactory, new AgentMemoryContractValidator());
        var influenceStore = new SqlAgentMemoryInfluenceStore(connectionFactory);
        var handoffStore = new SqlAgentMemoryHandoffStore(connectionFactory);
        var proposalService = new SqlMemoryImprovementProposalService(connectionFactory);
        var indexStore = new SqlMemoryIndexQueueStore(connectionFactory);
        var auditStore = new SqlMemoryExecutionAuditStore(connectionFactory);

        await memoryStore.CreateAsync(BuildMemoryItem("memory-store", scope, evidence));
        await influenceStore.RecordAsync(scope, BuildInfluenceDraft("influence-store", "memory-store", evidence));
        await handoffStore.CreateAsync(scope, BuildHandoffDraft("handoff-store", "memory-store", "influence-store", evidence));
        await proposalService.CreateAsync(BuildProposalDraft("proposal-store", scope, evidence));
        await proposalService.AddEventAsync(scope, BuildProposalEvent("proposal-event-store", "proposal-store"));
        await indexStore.QueueAsync(BuildProjection("index-store", evidence));
        await indexStore.AddEventAsync("index-store", MemoryIndexEventType.Indexed, weaviateObjectId: "weaviate-index-store");
        await auditStore.AppendAsync(BuildAuditDraft(scope));
        await memoryStore.AddEventAsync(scope, BuildMemoryEvent("memory-event-store", "memory-store"));

        Assert.AreEqual(1, await QuerySingleAsync<int>("SELECT COUNT(*) FROM agent.AgentLocalMemoryItem WHERE MemoryItemId = 'memory-store';"));
        Assert.AreEqual(2, await QuerySingleAsync<int>("SELECT COUNT(*) FROM agent.AgentLocalMemoryEvent WHERE MemoryItemId = 'memory-store';"));
        Assert.AreEqual(1, await QuerySingleAsync<int>("SELECT COUNT(*) FROM agent.AgentMemoryInfluenceRecord WHERE InfluenceId = 'influence-store';"));
        Assert.AreEqual(1, await QuerySingleAsync<int>("SELECT COUNT(*) FROM agent.AgentMemoryHandoffSlice WHERE HandoffMemorySliceId = 'handoff-store';"));
        Assert.AreEqual(1, await QuerySingleAsync<int>("SELECT COUNT(*) FROM agent.AgentMemoryImprovementProposal WHERE ProposalId = 'proposal-store';"));
        Assert.AreEqual(2, await QuerySingleAsync<int>("SELECT COUNT(*) FROM agent.AgentMemoryImprovementProposalEvent WHERE ProposalId = 'proposal-store';"));
        Assert.AreEqual(1, await QuerySingleAsync<int>("SELECT COUNT(*) FROM agent.AgentMemoryIndexQueue WHERE IndexRecordId = 'index-store';"));
        Assert.AreEqual(2, await QuerySingleAsync<int>("SELECT COUNT(*) FROM agent.AgentMemoryIndexEvent WHERE IndexRecordId = 'index-store';"));
        Assert.AreEqual(1, await QuerySingleAsync<int>("SELECT COUNT(*) FROM agent.AgentMemoryExecutionAudit WHERE DecisionId = 'decision-store';"));
    }

    [TestMethod]
    public void MemoryStoredProcedurePermission_RuntimeStoresDoNotContainDirectGovernedWritesOrRuntimeDdl()
    {
        var files = Directory.GetFiles(Path.Combine(FindRepositoryRoot(), "IronDev.Infrastructure", "AgentMemory"), "*.cs");
        string[] forbidden =
        [
            "INSERT INTO agent.AgentLocalMemoryItem",
            "INSERT INTO agent.AgentLocalMemoryEvidenceRef",
            "INSERT INTO agent.AgentLocalMemoryEvent",
            "INSERT INTO agent.AgentMemoryInfluenceRecord",
            "INSERT INTO agent.AgentMemoryHandoffSlice",
            "INSERT INTO agent.AgentMemoryImprovementProposal",
            "INSERT INTO agent.AgentMemoryImprovementProposalEvent",
            "INSERT INTO agent.AgentMemoryIndexQueue",
            "INSERT INTO agent.AgentMemoryIndexEvent",
            "INSERT INTO agent.AgentMemoryExecutionAudit",
            "CREATE TABLE",
            "CREATE TRIGGER",
            "CREATE PROCEDURE",
            "ALTER TABLE",
            "DROP TABLE",
            "DROP TRIGGER",
            "DROP PROCEDURE",
            "MERGE"
        ];

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            foreach (var token in forbidden)
            {
                var containsForbiddenToken = token == "MERGE"
                    ? Regex.IsMatch(source, @"\bMERGE\b", RegexOptions.IgnoreCase)
                    : source.Contains(token, StringComparison.OrdinalIgnoreCase);

                Assert.IsFalse(containsForbiddenToken, $"{Path.GetFileName(file)} must not contain {token}.");
            }
        }
    }

    private async Task ApplyAgentMemoryMigrationsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_local_memory.sql")));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_memory_influence.sql")));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_memory_handoff.sql")));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_memory_improvement_proposals.sql")));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_memory_indexing.sql")));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_memory_execution_audit.sql")));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_memory_stored_procedures.sql")));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_memory_permissions.sql")));
    }

    private async Task EnsureRuntimeTestUserAsync() =>
        await ExecuteSqlAsync(
            """
            IF USER_ID('IronDevMemoryRuntimeTestUser') IS NULL
                CREATE USER IronDevMemoryRuntimeTestUser WITHOUT LOGIN;

            IF NOT EXISTS
            (
                SELECT 1
                FROM sys.database_role_members
                WHERE role_principal_id = DATABASE_PRINCIPAL_ID('IronDevMemoryRuntimeRole')
                  AND member_principal_id = DATABASE_PRINCIPAL_ID('IronDevMemoryRuntimeTestUser')
            )
            BEGIN
                ALTER ROLE IronDevMemoryRuntimeRole ADD MEMBER IronDevMemoryRuntimeTestUser;
            END
            """);

    private async Task DropAgentMemorySchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            IF USER_ID('IronDevMemoryRuntimeTestUser') IS NOT NULL
                DROP USER IronDevMemoryRuntimeTestUser;
            IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'IronDevMemoryRuntimeRole' AND type = 'R')
                DROP ROLE IronDevMemoryRuntimeRole;
            IF OBJECT_ID('agent.usp_MemoryExecutionAudit_Create', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_MemoryExecutionAudit_Create;
            IF OBJECT_ID('agent.usp_MemoryIndexEvent_Add', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_MemoryIndexEvent_Add;
            IF OBJECT_ID('agent.usp_MemoryIndexQueue_Create', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_MemoryIndexQueue_Create;
            IF OBJECT_ID('agent.usp_MemoryImprovementProposal_AddEvent', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_MemoryImprovementProposal_AddEvent;
            IF OBJECT_ID('agent.usp_MemoryImprovementProposal_Create', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_MemoryImprovementProposal_Create;
            IF OBJECT_ID('agent.usp_AgentMemoryHandoff_Create', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_AgentMemoryHandoff_Create;
            IF OBJECT_ID('agent.usp_AgentMemoryInfluence_Create', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_AgentMemoryInfluence_Create;
            IF OBJECT_ID('agent.usp_AgentLocalMemory_AddEvent', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_AgentLocalMemory_AddEvent;
            IF OBJECT_ID('agent.usp_AgentLocalMemory_Create', 'P') IS NOT NULL
                DROP PROCEDURE agent.usp_AgentLocalMemory_Create;
            IF OBJECT_ID('agent.TR_AgentMemoryExecutionAudit_ValidateInsert', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryExecutionAudit_ValidateInsert;
            IF OBJECT_ID('agent.TR_AgentMemoryExecutionAudit_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryExecutionAudit_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentMemoryExecutionAudit', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryExecutionAudit;
            IF OBJECT_ID('agent.TR_AgentMemoryIndexEvent_ValidateInsert', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryIndexEvent_ValidateInsert;
            IF OBJECT_ID('agent.TR_AgentMemoryIndexQueue_ValidateProjection', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryIndexQueue_ValidateProjection;
            IF OBJECT_ID('agent.TR_AgentMemoryIndexEvent_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryIndexEvent_BlockUpdateDelete;
            IF OBJECT_ID('agent.TR_AgentMemoryIndexQueue_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryIndexQueue_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentMemoryIndexEvent', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryIndexEvent;
            IF OBJECT_ID('agent.AgentMemoryIndexQueue', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryIndexQueue;
            IF OBJECT_ID('agent.TR_AgentMemoryImprovementProposalEvent_ValidateInsert', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryImprovementProposalEvent_ValidateInsert;
            IF OBJECT_ID('agent.TR_AgentMemoryImprovementProposal_ValidateSources', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryImprovementProposal_ValidateSources;
            IF OBJECT_ID('agent.TR_AgentMemoryImprovementProposalEvent_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryImprovementProposalEvent_BlockUpdateDelete;
            IF OBJECT_ID('agent.TR_AgentMemoryImprovementProposal_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryImprovementProposal_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentMemoryImprovementProposalEvent', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryImprovementProposalEvent;
            IF OBJECT_ID('agent.AgentMemoryImprovementProposal', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryImprovementProposal;
            IF OBJECT_ID('agent.TR_AgentMemoryHandoffSlice_ValidateSourceMemory', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryHandoffSlice_ValidateSourceMemory;
            IF OBJECT_ID('agent.TR_AgentMemoryHandoffSlice_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryHandoffSlice_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentMemoryHandoffSlice', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryHandoffSlice;
            IF OBJECT_ID('agent.TR_AgentMemoryInfluenceRecord_ValidateScope', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryInfluenceRecord_ValidateScope;
            IF OBJECT_ID('agent.TR_AgentMemoryInfluenceRecord_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryInfluenceRecord_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentMemoryInfluenceRecord', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryInfluenceRecord;
            IF OBJECT_ID('agent.vwAgentLocalMemoryCurrentState', 'V') IS NOT NULL
                DROP VIEW agent.vwAgentLocalMemoryCurrentState;
            IF OBJECT_ID('agent.TR_AgentLocalMemoryEvent_ValidateInsert', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentLocalMemoryEvent_ValidateInsert;
            IF OBJECT_ID('agent.TR_AgentLocalMemoryEvidenceRef_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentLocalMemoryEvidenceRef_BlockUpdateDelete;
            IF OBJECT_ID('agent.TR_AgentLocalMemoryEvent_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentLocalMemoryEvent_BlockUpdateDelete;
            IF OBJECT_ID('agent.TR_AgentLocalMemoryItem_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentLocalMemoryItem_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentLocalMemoryEvidenceRef', 'U') IS NOT NULL
                DROP TABLE agent.AgentLocalMemoryEvidenceRef;
            IF OBJECT_ID('agent.AgentLocalMemoryEvent', 'U') IS NOT NULL
                DROP TABLE agent.AgentLocalMemoryEvent;
            IF OBJECT_ID('agent.AgentLocalMemoryItem', 'U') IS NOT NULL
                DROP TABLE agent.AgentLocalMemoryItem;
            IF SCHEMA_ID('agent') IS NOT NULL
                DROP SCHEMA agent;
            """);
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(sql);
    }

    private async Task<T> QuerySingleAsync<T>(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.QuerySingleAsync<T>(sql);
    }

    private async Task ExpectSqlFailsAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        try
        {
            await connection.ExecuteAsync(sql);
        }
        catch (SqlException)
        {
            await TryRevertAsync(connection);
            return;
        }

        await TryRevertAsync(connection);
        Assert.Fail("Expected SQL command to fail.");
    }

    private static async Task TryRevertAsync(SqlConnection connection)
    {
        try
        {
            await connection.ExecuteAsync("REVERT;");
        }
        catch
        {
            // Ignore when no impersonation context is active.
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root for memory stored procedure tests.");
    }

    private static AgentMemoryScope BuildScope() =>
        new()
        {
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RunId = "run-1",
            AgentId = "builder-agent"
        };

    private static EvidenceRef BuildEvidence(string evidenceId, string sourceId) =>
        new()
        {
            EvidenceId = evidenceId,
            EvidenceType = EvidenceType.ToolOutput,
            SourceId = sourceId,
            Summary = "Stored procedure boundary evidence.",
            CapturedAt = Now
        };

    private static AgentLocalMemoryItem BuildMemoryItem(string memoryItemId, AgentMemoryScope scope, EvidenceRef evidence) =>
        new()
        {
            MemoryItemId = memoryItemId,
            Scope = scope,
            MemoryType = AgentMemoryType.Observation,
            AuthorityLevel = MemoryAuthorityLevel.ObservedOnly,
            Title = "Observed stored procedure boundary",
            Summary = "The memory write path should use approved stored procedures.",
            EvidenceRefs = [evidence],
            Confidence = 0.9m,
            Status = MemoryLifecycleStatus.Active,
            CreatedAt = Now
        };

    private static AgentLocalMemoryEventRecord BuildMemoryEvent(string eventId, string memoryItemId) =>
        new()
        {
            MemoryEventId = eventId,
            MemoryItemId = memoryItemId,
            EventType = AgentLocalMemoryEventType.ProposedForReview,
            EventReason = "Stored procedure event boundary test.",
            CreatedAt = Now.AddMinutes(1),
            CreatedByAgentId = "builder-agent"
        };

    private static MemoryInfluenceDraft BuildInfluenceDraft(string influenceId, string memoryItemId, EvidenceRef evidence) =>
        new()
        {
            InfluenceId = influenceId,
            MemoryItemId = memoryItemId,
            DecisionId = "decision-store",
            InfluenceType = MemoryInfluenceType.SelectedAction,
            InfluenceSummary = "Memory influenced the governed stored procedure test.",
            EvidenceRefs = [evidence],
            Confidence = 0.8m,
            CreatedAt = Now,
            AffectedArtifactType = "test",
            AffectedArtifactId = "stored-procedure-boundary"
        };

    private static HandoffMemorySliceDraft BuildHandoffDraft(string handoffId, string memoryItemId, string influenceId, EvidenceRef evidence) =>
        new()
        {
            HandoffMemorySliceId = handoffId,
            TargetAgentId = "tester-agent",
            MemoryItemIds = [memoryItemId],
            InfluenceIds = [influenceId],
            Summary = "Handoff through stored procedure boundary.",
            AllowedUse = HandoffMemoryAllowedUse.ContextOnly,
            EvidenceRefs = [evidence],
            Confidence = 0.8m,
            CreatedAt = Now,
            DecisionId = "decision-store"
        };

    private static MemoryImprovementProposalDraft BuildProposalDraft(string proposalId, AgentMemoryScope scope, EvidenceRef evidence) =>
        new()
        {
            ProposalId = proposalId,
            Scope = scope,
            ProposalType = MemoryImprovementProposalType.PromoteObservedMemory,
            Title = "Stored procedure proposal",
            Summary = "Proposal should be persisted through the approved procedure boundary.",
            Sources =
            [
                new MemoryImprovementProposalSource
                {
                    MemoryItemId = "memory-store",
                    InfluenceId = "influence-store",
                    HandoffMemorySliceId = "handoff-store"
                }
            ],
            EvidenceRefs = [evidence],
            Confidence = 0.8m,
            CreatedAt = Now,
            ProposedByAgentId = scope.AgentId
        };

    private static MemoryImprovementProposalEventDraft BuildProposalEvent(string eventId, string proposalId) =>
        new()
        {
            ProposalEventId = eventId,
            ProposalId = proposalId,
            EventType = MemoryImprovementProposalEventType.Rejected,
            Reason = "Stored procedure event boundary test.",
            CreatedAt = Now.AddMinutes(1),
            CreatedByUserId = "reviewer-1"
        };

    private static MemoryIndexProjection BuildProjection(string indexRecordId, EvidenceRef evidence) =>
        new()
        {
            IndexRecordId = indexRecordId,
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RunId = "run-1",
            AgentId = "builder-agent",
            ArtifactType = MemoryIndexArtifactType.RunMemoryReport,
            ArtifactId = "run-1",
            AuthorityLevel = MemoryIndexAuthorityLevel.ObservedProjection,
            Title = "Stored procedure index projection",
            Summary = "Index projection should be queued through approved procedures.",
            EvidenceRefs = [evidence],
            CreatedAt = Now,
            SourceHashSha256 = new string('a', 64)
        };

    private static MemoryExecutionAuditDraft BuildAuditDraft(AgentMemoryScope scope)
    {
        var evidence = new MemoryExecutionEvidence
        {
            IsMemoryBacked = true,
            GovernanceCheckId = "governance-store",
            DecisionId = "decision-store",
            GateDecision = MemoryExecutionGateDecision.Allowed,
            GovernanceDecision = MemoryGovernanceDecision.Allow,
            MemoryItemIds = ["memory-store"],
            InfluenceIds = ["influence-store"],
            HandoffMemorySliceIds = ["handoff-store"]
        };

        return new MemoryExecutionAuditDraft
        {
            Request = new AgentSkillExecutionRequest
            {
                RequestedByAgent = scope.AgentId,
                ProjectId = scope.ProjectId,
                RunId = scope.RunId,
                SkillRequestContext = BuildSkillContext(scope),
                MemoryExecutionContext = new MemoryBackedExecutionContext
                {
                    Scope = scope,
                    ActionType = MemoryGovernanceActionType.ContextUse,
                    DecisionId = "decision-store",
                    ReferencedArtifacts =
                    [
                        new MemoryBackedExecutionReference
                        {
                            MemoryItemId = "memory-store",
                            InfluenceId = "influence-store",
                            HandoffMemorySliceId = "handoff-store"
                        }
                    ],
                    ToolName = "workspace.read_apply_context"
                }
            },
            Result = new AgentSkillExecutionResult
            {
                ExecutionId = "execution-store",
                ContextId = "context-store",
                RequestId = "request-store",
                ReviewId = "review-store",
                SkillId = "workspace.read_apply_context",
                Status = AgentSkillExecutionStatuses.Succeeded,
                Summary = "Memory-backed execution succeeded.",
                Executed = true,
                ReadOnlyExecution = true,
                SourceMutated = false,
                WorkspaceMutated = false,
                ExternalSystemCalled = false,
                TicketCreated = false,
                MemoryWritten = false,
                ApprovalGranted = false,
                ShellCommandRun = false,
                MemoryEvidence = evidence,
                EvidencePaths = ["evidence/store.json"]
            },
            GateResult = new MemoryExecutionGateResult
            {
                Decision = MemoryExecutionGateDecision.Allowed,
                MayProceedToPolicyGate = true,
                Summary = "Memory execution allowed.",
                Evidence = evidence,
                GovernanceResult = new MemoryGovernanceCheckResult
                {
                    GovernanceCheckId = "governance-store",
                    Scope = scope,
                    DecisionId = "decision-store",
                    ActionType = MemoryGovernanceActionType.ContextUse,
                    Decision = MemoryGovernanceDecision.Allow,
                    Issues = [],
                    CheckedAt = Now
                }
            },
            Outcome = MemoryExecutionAuditOutcome.ExecutedSucceeded,
            CreatedAt = Now
        };
    }

    private static AgentSkillRequestContext BuildSkillContext(AgentMemoryScope scope) =>
        new()
        {
            ContextId = "context-store",
            RequestId = "request-store",
            ReviewId = "review-store",
            ProjectId = scope.ProjectId,
            AgentName = scope.AgentId,
            SkillId = "workspace.read_apply_context",
            Purpose = "Stored procedure boundary test.",
            SkillKnown = true,
            Decision = "allowed",
            ReviewStatus = "approved_for_execution",
            RiskTier = "read_only",
            Category = "workspace",
            HumanReviewRequired = false,
            HumanApprovalRequired = false,
            PolicyAllowed = true,
            PolicyBlocked = false,
            DangerousCapability = false,
            ExecutionCanStartFromContext = true,
            ApprovalCanBeGrantedByContext = false,
            SourceMutationAllowed = false,
            WorkspaceMutationAllowed = false,
            ExternalSystemAllowed = false,
            CreatesTicketAllowed = false,
            WritesMemoryAllowed = false,
            RecommendedNextAction = AgentSkillRequestContextRecommendedActions.ExecuteApprovedRequest
        };
}
