using System.Reflection;
using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Data;
using IronDev.Infrastructure.AgentMemory;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AgentMemoryInfluenceRecordTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);

    private AgentMemorySiloService _siloService = null!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropAgentMemorySchemaAsync();
        await ApplyAgentMemoryMigrationsAsync();

        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        var store = new SqlAgentLocalMemoryStore(connectionFactory, new AgentMemoryContractValidator());
        var influenceStore = new SqlAgentMemoryInfluenceStore(connectionFactory);
        var handoffStore = new SqlAgentMemoryHandoffStore(connectionFactory);

        _siloService = new AgentMemorySiloService(store, influenceStore, handoffStore);
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        try
        {
            await DropAgentMemorySchemaAsync();
        }
        finally
        {
            await base.TestCleanup();
        }
    }

    [TestMethod]
    public async Task AgentMemoryInfluence_MigrationCreatesTableIndexesAndTriggers()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var objectCount = await connection.QuerySingleAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.objects
            WHERE object_id IN
            (
                OBJECT_ID('agent.AgentMemoryInfluenceRecord', 'U'),
                OBJECT_ID('agent.TR_AgentMemoryInfluenceRecord_BlockUpdateDelete', 'TR'),
                OBJECT_ID('agent.TR_AgentMemoryInfluenceRecord_ValidateScope', 'TR')
            );
            """);

        var indexCount = await connection.QuerySingleAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.indexes
            WHERE name IN
            (
                'IX_AgentMemoryInfluenceRecord_ScopeCreated',
                'IX_AgentMemoryInfluenceRecord_Memory',
                'IX_AgentMemoryInfluenceRecord_Decision'
            );
            """);

        Assert.AreEqual(3, objectCount);
        Assert.AreEqual(3, indexCount);
    }

    [TestMethod]
    public async Task AgentMemoryInfluence_IsAppendOnly()
    {
        var silo = _siloService.Open(BuildContext());
        await silo.CreateAsync(BuildMemoryDraft("memory-append-only"));
        await silo.RecordInfluenceAsync(BuildInfluenceDraft("influence-append-only", "memory-append-only"));

        await AssertSqlFailsAsync("UPDATE agent.AgentMemoryInfluenceRecord SET InfluenceSummary = 'mutated' WHERE InfluenceId = 'influence-append-only';");
        await AssertSqlFailsAsync("DELETE FROM agent.AgentMemoryInfluenceRecord WHERE InfluenceId = 'influence-append-only';");
    }

    [TestMethod]
    public void AgentMemoryInfluence_SiloMethodsDoNotAcceptScope()
    {
        var influenceMethodNames = new[]
        {
            nameof(IAgentMemorySilo.RecordInfluenceAsync),
            nameof(IAgentMemorySilo.QueryInfluencesAsync),
            nameof(IAgentMemorySilo.GetInfluencesForMemoryAsync),
            nameof(IAgentMemorySilo.GetInfluencesForDecisionAsync)
        };

        foreach (var method in typeof(IAgentMemorySilo).GetMethods().Where(method => influenceMethodNames.Contains(method.Name)))
        {
            Assert.IsFalse(method.GetParameters().Any(parameter => parameter.ParameterType == typeof(AgentMemoryScope)),
                $"IAgentMemorySilo.{method.Name} must not accept AgentMemoryScope; influence access is silo-bound.");
        }
    }

    [TestMethod]
    public void AgentMemoryInfluence_DraftAndQueryModelsCannotCarryScope()
    {
        var forbidden = new[] { "TenantId", "ProjectId", "CampaignId", "RunId", "AgentId", "Scope", "AgentMemoryScope", "MemoryAuthorityLevel", "MemoryLifecycleStatus" };
        var draftProperties = typeof(MemoryInfluenceDraft).GetProperties().Select(property => property.Name).ToArray();
        var queryProperties = typeof(MemoryInfluenceQuery).GetProperties().Select(property => property.Name).ToArray();

        foreach (var propertyName in forbidden)
        {
            Assert.IsFalse(draftProperties.Contains(propertyName, StringComparer.Ordinal),
                $"MemoryInfluenceDraft must not expose caller-supplied scope/authority/status property '{propertyName}'.");
            Assert.IsFalse(queryProperties.Contains(propertyName, StringComparer.Ordinal),
                $"MemoryInfluenceQuery must not expose caller-supplied scope/agent property '{propertyName}'.");
        }
    }

    [TestMethod]
    public async Task AgentMemoryInfluence_AgentCanRecordAndQueryOwnInfluence()
    {
        var silo = _siloService.Open(BuildContext());
        await silo.CreateAsync(BuildMemoryDraft("memory-own-influence"));
        await silo.RecordInfluenceAsync(BuildInfluenceDraft("influence-own", "memory-own-influence") with
        {
            InfluenceType = MemoryInfluenceType.SelectedAction,
            DecisionId = "decision-own",
            AffectedArtifactType = "proposal",
            AffectedArtifactId = "proposal-1"
        });

        var byMemory = await silo.GetInfluencesForMemoryAsync("memory-own-influence");
        var byDecision = await silo.GetInfluencesForDecisionAsync("decision-own");
        var byType = await silo.QueryInfluencesAsync(new MemoryInfluenceQuery { InfluenceType = MemoryInfluenceType.SelectedAction });

        Assert.HasCount(1, byMemory);
        Assert.HasCount(1, byDecision);
        Assert.HasCount(1, byType);
        Assert.AreEqual("influence-own", byMemory[0].InfluenceId);
        Assert.AreEqual(MemoryAuthorityLevel.ObservedOnly, byMemory[0].MemoryAuthorityLevelAtInfluence);
        Assert.AreEqual(MemoryLifecycleStatus.Active, byMemory[0].MemoryStatusAtInfluence);
        Assert.HasCount(1, byMemory[0].EvidenceRefs);
        Assert.AreEqual("proposal", byMemory[0].AffectedArtifactType);
        Assert.AreEqual("proposal-1", byMemory[0].AffectedArtifactId);
    }

    [TestMethod]
    public async Task AgentMemoryInfluence_SnapshotsProposedForReviewMemoryStatus()
    {
        var silo = _siloService.Open(BuildContext());
        await silo.CreateAsync(BuildMemoryDraft("memory-proposed-influence"));
        await silo.AddEventAsync(BuildEventDraft("memory-proposed-influence", AgentLocalMemoryEventType.ProposedForReview, 1));

        await silo.RecordInfluenceAsync(BuildInfluenceDraft("influence-proposed", "memory-proposed-influence") with
        {
            InfluenceType = MemoryInfluenceType.EscalationTriggered
        });

        var influences = await silo.GetInfluencesForMemoryAsync("memory-proposed-influence");

        Assert.HasCount(1, influences);
        Assert.AreEqual(MemoryLifecycleStatus.ProposedForReview, influences[0].MemoryStatusAtInfluence);
    }

    [TestMethod]
    public async Task AgentMemoryInfluence_AgentBCannotRecordAgainstAgentAMemory()
    {
        var agentA = _siloService.Open(BuildContext(agentId: "builder-agent"));
        var agentB = _siloService.Open(BuildContext(agentId: "critic-agent"));
        await agentA.CreateAsync(BuildMemoryDraft("memory-agent-a"));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            agentB.RecordInfluenceAsync(BuildInfluenceDraft("influence-wrong-agent", "memory-agent-a")));

        Assert.IsEmpty(await agentA.GetInfluencesForMemoryAsync("memory-agent-a"));
        Assert.IsEmpty(await agentB.GetInfluencesForMemoryAsync("memory-agent-a"));
    }

    [TestMethod]
    public async Task AgentMemoryInfluence_AgentBCannotReadAgentAInfluence()
    {
        var agentA = _siloService.Open(BuildContext(agentId: "builder-agent"));
        var agentB = _siloService.Open(BuildContext(agentId: "critic-agent"));
        await agentA.CreateAsync(BuildMemoryDraft("memory-agent-a"));
        await agentA.RecordInfluenceAsync(BuildInfluenceDraft("influence-agent-a", "memory-agent-a") with { DecisionId = "decision-agent-a" });

        Assert.HasCount(1, await agentA.GetInfluencesForMemoryAsync("memory-agent-a"));
        Assert.IsEmpty(await agentB.GetInfluencesForMemoryAsync("memory-agent-a"));
        Assert.IsEmpty(await agentB.GetInfluencesForDecisionAsync("decision-agent-a"));
        Assert.IsEmpty(await agentB.QueryInfluencesAsync(new MemoryInfluenceQuery { Take = 100 }));
    }

    [TestMethod]
    public async Task AgentMemoryInfluence_DoesNotLeakAcrossTenantProjectCampaignOrRun()
    {
        var agentA = _siloService.Open(BuildContext());
        await agentA.CreateAsync(BuildMemoryDraft("memory-scoped"));
        await agentA.RecordInfluenceAsync(BuildInfluenceDraft("influence-scoped", "memory-scoped") with { DecisionId = "decision-scoped" });

        var forbiddenContexts = new[]
        {
            BuildContext(tenantId: "tenant-2"),
            BuildContext(projectId: "project-2"),
            BuildContext(campaignId: "campaign-2"),
            BuildContext(runId: "run-2")
        };

        foreach (var context in forbiddenContexts)
        {
            var silo = _siloService.Open(context);
            Assert.IsEmpty(await silo.GetInfluencesForMemoryAsync("memory-scoped"));
            Assert.IsEmpty(await silo.GetInfluencesForDecisionAsync("decision-scoped"));
        }
    }

    [TestMethod]
    public async Task AgentMemoryInfluence_RejectsInvalidDrafts()
    {
        var silo = _siloService.Open(BuildContext());
        await silo.CreateAsync(BuildMemoryDraft("memory-validation"));

        var invalidDrafts = new[]
        {
            BuildInfluenceDraft("", "memory-validation"),
            BuildInfluenceDraft("influence-missing-memory", ""),
            BuildInfluenceDraft("influence-missing-decision", "memory-validation") with { DecisionId = "" },
            BuildInfluenceDraft("influence-missing-summary", "memory-validation") with { InfluenceSummary = "" },
            BuildInfluenceDraft("influence-missing-evidence", "memory-validation") with { EvidenceRefs = Array.Empty<EvidenceRef>() },
            BuildInfluenceDraft("influence-missing-evidence-id", "memory-validation") with { EvidenceRefs = [BuildEvidence("")] },
            BuildInfluenceDraft("influence-unsupported-evidence-type", "memory-validation") with { EvidenceRefs = [BuildEvidence("bad-type") with { EvidenceType = (EvidenceType)999 }] },
            BuildInfluenceDraft("influence-missing-evidence-source", "memory-validation") with { EvidenceRefs = [BuildEvidence("missing-source") with { SourceId = "" }] },
            BuildInfluenceDraft("influence-low-confidence", "memory-validation") with { Confidence = -0.1m },
            BuildInfluenceDraft("influence-high-confidence", "memory-validation") with { Confidence = 1.1m },
            BuildInfluenceDraft("influence-unsupported-type", "memory-validation") with { InfluenceType = (MemoryInfluenceType)999 }
        };

        foreach (var draft in invalidDrafts)
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => silo.RecordInfluenceAsync(draft));
        }
    }

    [TestMethod]
    public async Task AgentMemoryInfluence_RejectsDuplicateInfluenceId()
    {
        var silo = _siloService.Open(BuildContext());
        await silo.CreateAsync(BuildMemoryDraft("memory-duplicate"));
        await silo.RecordInfluenceAsync(BuildInfluenceDraft("influence-duplicate", "memory-duplicate"));

        await Assert.ThrowsExactlyAsync<SqlException>(() =>
            silo.RecordInfluenceAsync(BuildInfluenceDraft("influence-duplicate", "memory-duplicate") with { DecisionId = "decision-2" }));
    }

    [TestMethod]
    public async Task AgentMemoryInfluence_CannotRecordAgainstTerminalMemory()
    {
        var silo = _siloService.Open(BuildContext());
        await silo.CreateAsync(BuildMemoryDraft("memory-expired"));
        await silo.AddEventAsync(BuildEventDraft("memory-expired", AgentLocalMemoryEventType.Expired, 1));

        await silo.CreateAsync(BuildMemoryDraft("memory-invalidated"));
        await silo.AddEventAsync(BuildEventDraft("memory-invalidated", AgentLocalMemoryEventType.Invalidated, 1));

        await silo.CreateAsync(BuildMemoryDraft("memory-superseded"));
        await silo.AddEventAsync(BuildEventDraft("memory-superseded", AgentLocalMemoryEventType.Superseded, 1));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            silo.RecordInfluenceAsync(BuildInfluenceDraft("influence-expired", "memory-expired")));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            silo.RecordInfluenceAsync(BuildInfluenceDraft("influence-invalidated", "memory-invalidated")));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            silo.RecordInfluenceAsync(BuildInfluenceDraft("influence-superseded", "memory-superseded")));
    }

    [TestMethod]
    public async Task AgentMemoryInfluence_CannotRecordAgainstTimeExpiredMemory()
    {
        var silo = _siloService.Open(BuildContext());
        await silo.CreateAsync(BuildMemoryDraft("memory-time-expired") with
        {
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1)
        });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            silo.RecordInfluenceAsync(BuildInfluenceDraft("influence-time-expired", "memory-time-expired")));
    }

    [DataTestMethod]
    [DataRow("AgentId", "critic-agent")]
    [DataRow("RunId", "run-2")]
    [DataRow("CampaignId", "campaign-2")]
    [DataRow("ProjectId", "project-2")]
    [DataRow("TenantId", "tenant-2")]
    public async Task AgentMemoryInfluence_DirectSqlScopeMismatchBlocked(string columnName, string badValue)
    {
        var silo = _siloService.Open(BuildContext());
        await silo.CreateAsync(BuildMemoryDraft("memory-direct-scope"));

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await Assert.ThrowsExactlyAsync<SqlException>(() => InsertDirectInfluenceAsync(
            connection,
            "influence-direct-scope",
            "memory-direct-scope",
            overrides: new Dictionary<string, string> { [columnName] = badValue }));
    }

    [TestMethod]
    public async Task AgentMemoryInfluence_DirectSqlEmptyEvidenceBlocked()
    {
        var silo = _siloService.Open(BuildContext());
        await silo.CreateAsync(BuildMemoryDraft("memory-direct-evidence"));

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await Assert.ThrowsExactlyAsync<SqlException>(() => InsertDirectInfluenceAsync(
            connection,
            "influence-direct-evidence",
            "memory-direct-evidence",
            evidenceRefsJson: "[]"));
    }

    [TestMethod]
    public async Task AgentMemoryInfluence_DirectSqlTerminalMemoryBlocked()
    {
        var silo = _siloService.Open(BuildContext());
        await silo.CreateAsync(BuildMemoryDraft("memory-direct-terminal"));
        await silo.AddEventAsync(BuildEventDraft("memory-direct-terminal", AgentLocalMemoryEventType.Expired, 1));

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await Assert.ThrowsExactlyAsync<SqlException>(() => InsertDirectInfluenceAsync(
            connection,
            "influence-direct-terminal",
            "memory-direct-terminal"));
    }

    [TestMethod]
    public async Task AgentMemoryInfluence_DirectSqlTimeExpiredMemoryBlocked()
    {
        var silo = _siloService.Open(BuildContext());
        await silo.CreateAsync(BuildMemoryDraft("memory-direct-time-expired") with
        {
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1)
        });

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await Assert.ThrowsExactlyAsync<SqlException>(() => InsertDirectInfluenceAsync(
            connection,
            "influence-direct-time-expired",
            "memory-direct-time-expired"));
    }

    private static async Task InsertDirectInfluenceAsync(
        SqlConnection connection,
        string influenceId,
        string memoryItemId,
        IReadOnlyDictionary<string, string>? overrides = null,
        string evidenceRefsJson = "[{\"evidenceId\":\"direct-evidence\",\"evidenceType\":2,\"sourceId\":\"direct-source\"}]")
    {
        string Value(string column, string defaultValue) =>
            overrides is not null && overrides.TryGetValue(column, out var value) ? value : defaultValue;

        await connection.ExecuteAsync(
            """
            INSERT INTO agent.AgentMemoryInfluenceRecord
            (
                InfluenceId,
                TenantId,
                ProjectId,
                CampaignId,
                RunId,
                AgentId,
                MemoryItemId,
                DecisionId,
                InfluenceType,
                InfluenceSummary,
                Confidence,
                MemoryAuthorityLevelAtInfluence,
                MemoryLifecycleStatusAtInfluence,
                EvidenceRefsJson,
                CreatedAtUtc
            )
            VALUES
            (
                @InfluenceId,
                @TenantId,
                @ProjectId,
                @CampaignId,
                @RunId,
                @AgentId,
                @MemoryItemId,
                'decision-direct',
                1,
                'Direct SQL influence attempt.',
                0.7,
                1,
                1,
                @EvidenceRefsJson,
                @CreatedAtUtc
            );
            """,
            new
            {
                InfluenceId = influenceId,
                TenantId = Value("TenantId", "tenant-1"),
                ProjectId = Value("ProjectId", "project-1"),
                CampaignId = Value("CampaignId", "campaign-1"),
                RunId = Value("RunId", "run-1"),
                AgentId = Value("AgentId", "builder-agent"),
                MemoryItemId = memoryItemId,
                EvidenceRefsJson = evidenceRefsJson,
                CreatedAtUtc = Now.UtcDateTime
            });
    }

    private async Task AssertSqlFailsAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await Assert.ThrowsExactlyAsync<SqlException>(() => connection.ExecuteAsync(sql));
    }

    private async Task ApplyAgentMemoryMigrationsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_local_memory.sql")));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_memory_influence.sql")));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_memory_stored_procedures.sql")));
    }

    private async Task DropAgentMemorySchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
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
                DROP TABLE agent.AgentMemoryExecutionAudit;            IF OBJECT_ID('agent.TR_AgentMemoryInfluenceRecord_ValidateScope', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryInfluenceRecord_ValidateScope;
            IF OBJECT_ID('agent.TR_AgentMemoryInfluenceRecord_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryInfluenceRecord_BlockUpdateDelete;
            IF OBJECT_ID('agent.TR_AgentMemoryHandoffSlice_ValidateSourceMemory', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryHandoffSlice_ValidateSourceMemory;
            IF OBJECT_ID('agent.TR_AgentMemoryHandoffSlice_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryHandoffSlice_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentMemoryHandoffSlice', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryHandoffSlice;
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root for agent memory influence tests.");
    }

    private static AgentMemorySiloContext BuildContext(
        string tenantId = "tenant-1",
        string projectId = "project-1",
        string campaignId = "campaign-1",
        string runId = "run-1",
        string agentId = "builder-agent") =>
        new()
        {
            TenantId = tenantId,
            ProjectId = projectId,
            CampaignId = campaignId,
            RunId = runId,
            AgentId = agentId,
            WorkflowId = "workflow-1",
            TicketId = "ticket-1",
            CorrelationId = "correlation-1"
        };

    private static AgentLocalMemoryDraft BuildMemoryDraft(string memoryItemId) =>
        new()
        {
            MemoryItemId = memoryItemId,
            MemoryType = AgentMemoryType.Episodic,
            AuthorityLevel = MemoryAuthorityLevel.ObservedOnly,
            Title = "Observed build failure",
            Summary = "TesterAgent observed a reproducible build failure.",
            EvidenceRefs = [BuildEvidence()],
            Confidence = 0.8m,
            CreatedAt = Now
        };

    private static AgentLocalMemoryEventDraft BuildEventDraft(
        string memoryItemId,
        AgentLocalMemoryEventType eventType,
        int minutesAfterCreated) =>
        new()
        {
            MemoryEventId = $"event-{memoryItemId}-{eventType}-{minutesAfterCreated}",
            MemoryItemId = memoryItemId,
            EventType = eventType,
            EventReason = $"Lifecycle event {eventType}.",
            CreatedAt = Now.AddMinutes(minutesAfterCreated),
            DecisionId = "decision-event"
        };

    private static MemoryInfluenceDraft BuildInfluenceDraft(string influenceId, string memoryItemId) =>
        new()
        {
            InfluenceId = influenceId,
            MemoryItemId = memoryItemId,
            DecisionId = "decision-1",
            InfluenceType = MemoryInfluenceType.AvoidedAction,
            InfluenceSummary = "Memory prevented repeating a previously failed path.",
            EvidenceRefs = [BuildEvidence("influence-evidence")],
            Confidence = 0.7m,
            CreatedAt = Now.AddMinutes(10),
            ThoughtLedgerEntryId = "thought-1",
            CorrelationId = "correlation-1",
            InfluenceJson = "{}"
        };

    private static EvidenceRef BuildEvidence(string evidenceId = "evidence-1") =>
        new()
        {
            EvidenceId = evidenceId,
            EvidenceType = EvidenceType.TestResult,
            SourceId = $"source-{evidenceId}",
            SourceUri = $"workspace://run-1/{evidenceId}.json",
            Summary = "Focused test result captured during the run.",
            CapturedAt = Now
        };
}
