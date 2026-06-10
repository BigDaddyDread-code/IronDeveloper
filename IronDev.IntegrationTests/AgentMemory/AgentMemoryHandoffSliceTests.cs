using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Data;
using IronDev.Infrastructure.AgentMemory;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AgentMemoryHandoffSliceTests : IntegrationTestBase
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
    public async Task AgentMemoryHandoff_MigrationCreatesTableIndexesAndTriggers()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var objectCount = await connection.QuerySingleAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.objects
            WHERE object_id IN
            (
                OBJECT_ID('agent.AgentMemoryHandoffSlice', 'U'),
                OBJECT_ID('agent.TR_AgentMemoryHandoffSlice_BlockUpdateDelete', 'TR'),
                OBJECT_ID('agent.TR_AgentMemoryHandoffSlice_ValidateSourceMemory', 'TR')
            );
            """);

        var indexCount = await connection.QuerySingleAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.indexes
            WHERE name IN
            (
                'IX_AgentMemoryHandoffSlice_Incoming',
                'IX_AgentMemoryHandoffSlice_Outgoing',
                'IX_AgentMemoryHandoffSlice_Correlation'
            );
            """);

        Assert.AreEqual(3, objectCount);
        Assert.AreEqual(3, indexCount);
    }

    [TestMethod]
    public async Task AgentMemoryHandoff_IsAppendOnly()
    {
        var source = _siloService.Open(BuildContext(agentId: "builder-agent"));
        await source.CreateAsync(BuildMemoryDraft("memory-append-only"));
        await source.CreateHandoffAsync(BuildHandoffDraft("handoff-append-only", "tester-agent", ["memory-append-only"]));

        await AssertSqlFailsAsync("UPDATE agent.AgentMemoryHandoffSlice SET Summary = 'mutated' WHERE HandoffMemorySliceId = 'handoff-append-only';");
        await AssertSqlFailsAsync("DELETE FROM agent.AgentMemoryHandoffSlice WHERE HandoffMemorySliceId = 'handoff-append-only';");
    }

    [TestMethod]
    public void AgentMemoryHandoff_SiloMethodsAndDraftsDoNotCarrySourceScope()
    {
        var handoffMethodNames = new[]
        {
            nameof(IAgentMemorySilo.CreateHandoffAsync),
            nameof(IAgentMemorySilo.QueryIncomingHandoffsAsync),
            nameof(IAgentMemorySilo.QueryOutgoingHandoffsAsync)
        };

        foreach (var method in typeof(IAgentMemorySilo).GetMethods().Where(method => handoffMethodNames.Contains(method.Name)))
        {
            Assert.IsFalse(method.GetParameters().Any(parameter => parameter.ParameterType == typeof(AgentMemoryScope)),
                $"IAgentMemorySilo.{method.Name} must not accept AgentMemoryScope; handoff access is silo-bound.");
        }

        var forbidden = new[] { "TenantId", "ProjectId", "CampaignId", "RunId", "SourceAgentId", "Scope", "AgentMemoryScope", "AuthorityLevel", "LifecycleStatus" };
        var draftProperties = typeof(HandoffMemorySliceDraft).GetProperties().Select(property => property.Name).ToArray();
        var queryProperties = typeof(HandoffMemorySliceQuery).GetProperties().Select(property => property.Name).ToArray();

        foreach (var propertyName in forbidden)
        {
            Assert.IsFalse(draftProperties.Contains(propertyName, StringComparer.Ordinal),
                $"HandoffMemorySliceDraft must not expose caller-supplied source scope/authority/status property '{propertyName}'.");
        }

        foreach (var propertyName in new[] { "TenantId", "ProjectId", "CampaignId", "RunId", "Scope", "AgentMemoryScope" })
        {
            Assert.IsFalse(queryProperties.Contains(propertyName, StringComparer.Ordinal),
                $"HandoffMemorySliceQuery must not expose caller-supplied tenant/project/campaign/run scope property '{propertyName}'.");
        }
    }

    [TestMethod]
    public async Task AgentMemoryHandoff_SourceCreatesTargetReceivesAndSourceCanAuditOutgoing()
    {
        var source = _siloService.Open(BuildContext(agentId: "builder-agent"));
        var target = _siloService.Open(BuildContext(agentId: "tester-agent"));

        await source.CreateAsync(BuildMemoryDraft("memory-handoff"));
        await source.RecordInfluenceAsync(BuildInfluenceDraft("influence-handoff", "memory-handoff"));
        await source.CreateHandoffAsync(BuildHandoffDraft("handoff-happy", "tester-agent", ["memory-handoff"]) with
        {
            AllowedUse = HandoffMemoryAllowedUse.AvoidRepeat,
            InfluenceIds = ["influence-handoff"],
            DecisionId = "decision-handoff"
        });

        var incoming = await target.QueryIncomingHandoffsAsync(new HandoffMemorySliceQuery());
        var outgoing = await source.QueryOutgoingHandoffsAsync(new HandoffMemorySliceQuery());

        Assert.HasCount(1, incoming);
        Assert.HasCount(1, outgoing);
        Assert.AreEqual("handoff-happy", incoming[0].HandoffMemorySliceId);
        Assert.AreEqual("builder-agent", incoming[0].SourceAgentId);
        Assert.AreEqual("tester-agent", incoming[0].TargetAgentId);
        Assert.AreEqual(HandoffMemoryAllowedUse.AvoidRepeat, incoming[0].AllowedUse);
        Assert.AreEqual("decision-handoff", incoming[0].DecisionId);
        Assert.HasCount(1, incoming[0].MemoryItemIds);
        Assert.HasCount(1, incoming[0].EvidenceRefs);
        Assert.HasCount(1, incoming[0].MemorySnapshots);
        Assert.AreEqual("memory-handoff", incoming[0].MemorySnapshots[0].MemoryItemId);
        Assert.AreEqual(AgentMemoryType.Episodic, incoming[0].MemorySnapshots[0].MemoryType);
        Assert.AreEqual(MemoryAuthorityLevel.ObservedOnly, incoming[0].MemorySnapshots[0].AuthorityLevelAtHandoff);
        Assert.AreEqual(MemoryLifecycleStatus.Active, incoming[0].MemorySnapshots[0].StatusAtHandoff);
        Assert.HasCount(1, incoming[0].InfluenceIds!);
        Assert.AreEqual("influence-handoff", incoming[0].InfluenceIds![0]);
    }

    [TestMethod]
    public async Task AgentMemoryHandoff_IncomingAndOutgoingQueriesDoNotLeakAcrossAgentsOrScope()
    {
        var source = _siloService.Open(BuildContext(agentId: "builder-agent"));
        var target = _siloService.Open(BuildContext(agentId: "tester-agent"));
        await source.CreateAsync(BuildMemoryDraft("memory-isolated"));
        await source.CreateHandoffAsync(BuildHandoffDraft("handoff-isolated", "tester-agent", ["memory-isolated"]));

        Assert.HasCount(1, await target.QueryIncomingHandoffsAsync(new HandoffMemorySliceQuery()));
        Assert.IsEmpty(await _siloService.Open(BuildContext(agentId: "critic-agent")).QueryIncomingHandoffsAsync(new HandoffMemorySliceQuery()));
        Assert.IsEmpty(await target.QueryOutgoingHandoffsAsync(new HandoffMemorySliceQuery()));
        Assert.IsEmpty(await source.QueryIncomingHandoffsAsync(new HandoffMemorySliceQuery()));

        var forbiddenContexts = new[]
        {
            BuildContext(tenantId: "tenant-2", agentId: "tester-agent"),
            BuildContext(projectId: "project-2", agentId: "tester-agent"),
            BuildContext(campaignId: "campaign-2", agentId: "tester-agent"),
            BuildContext(runId: "run-2", agentId: "tester-agent")
        };

        foreach (var context in forbiddenContexts)
        {
            Assert.IsEmpty(await _siloService.Open(context).QueryIncomingHandoffsAsync(new HandoffMemorySliceQuery()),
                $"Handoff leaked for context {context}.");
        }
    }

    [DataTestMethod]
    [DataRow("critic-agent", "tenant-1", "project-1", "campaign-1", "run-1")]
    [DataRow("builder-agent", "tenant-2", "project-1", "campaign-1", "run-1")]
    [DataRow("builder-agent", "tenant-1", "project-2", "campaign-1", "run-1")]
    [DataRow("builder-agent", "tenant-1", "project-1", "campaign-2", "run-1")]
    [DataRow("builder-agent", "tenant-1", "project-1", "campaign-1", "run-2")]
    public async Task AgentMemoryHandoff_SourceCannotCreateFromMemoryOutsideBoundScope(
        string ownerAgentId,
        string tenantId,
        string projectId,
        string campaignId,
        string runId)
    {
        var foreignOwner = _siloService.Open(BuildContext(
            tenantId: tenantId,
            projectId: projectId,
            campaignId: campaignId,
            runId: runId,
            agentId: ownerAgentId));
        await foreignOwner.CreateAsync(BuildMemoryDraft("memory-foreign"));

        var source = _siloService.Open(BuildContext(agentId: "builder-agent"));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            source.CreateHandoffAsync(BuildHandoffDraft("handoff-foreign", "tester-agent", ["memory-foreign"])));
    }

    [TestMethod]
    public async Task AgentMemoryHandoff_BlocksTerminalAndTimeExpiredMemoryButAllowsProposedForReview()
    {
        var source = _siloService.Open(BuildContext(agentId: "builder-agent"));

        await source.CreateAsync(BuildMemoryDraft("memory-expired"));
        await source.AddEventAsync(BuildEventDraft("memory-expired", AgentLocalMemoryEventType.Expired, 1));
        await source.CreateAsync(BuildMemoryDraft("memory-invalidated"));
        await source.AddEventAsync(BuildEventDraft("memory-invalidated", AgentLocalMemoryEventType.Invalidated, 1));
        await source.CreateAsync(BuildMemoryDraft("memory-superseded"));
        await source.AddEventAsync(BuildEventDraft("memory-superseded", AgentLocalMemoryEventType.Superseded, 1));
        await source.CreateAsync(BuildMemoryDraft("memory-time-expired") with
        {
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await source.CreateAsync(BuildMemoryDraft("memory-proposed"));
        await source.AddEventAsync(BuildEventDraft("memory-proposed", AgentLocalMemoryEventType.ProposedForReview, 1));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            source.CreateHandoffAsync(BuildHandoffDraft("handoff-expired", "tester-agent", ["memory-expired"])));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            source.CreateHandoffAsync(BuildHandoffDraft("handoff-invalidated", "tester-agent", ["memory-invalidated"])));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            source.CreateHandoffAsync(BuildHandoffDraft("handoff-superseded", "tester-agent", ["memory-superseded"])));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            source.CreateHandoffAsync(BuildHandoffDraft("handoff-time-expired", "tester-agent", ["memory-time-expired"])));

        await source.CreateHandoffAsync(BuildHandoffDraft("handoff-proposed", "tester-agent", ["memory-proposed"]));
        var incoming = await _siloService.Open(BuildContext(agentId: "tester-agent")).QueryIncomingHandoffsAsync(new HandoffMemorySliceQuery());

        Assert.HasCount(1, incoming);
        Assert.AreEqual(MemoryLifecycleStatus.ProposedForReview, incoming[0].MemorySnapshots[0].StatusAtHandoff);
    }

    [TestMethod]
    public async Task AgentMemoryHandoff_RejectsInvalidDrafts()
    {
        var source = _siloService.Open(BuildContext(agentId: "builder-agent"));
        await source.CreateAsync(BuildMemoryDraft("memory-validation"));

        var invalidDrafts = new[]
        {
            BuildHandoffDraft("", "tester-agent", ["memory-validation"]),
            BuildHandoffDraft("handoff-missing-target", "", ["memory-validation"]),
            BuildHandoffDraft("handoff-self", "builder-agent", ["memory-validation"]),
            BuildHandoffDraft("handoff-empty-memory", "tester-agent", []),
            BuildHandoffDraft("handoff-blank-memory", "tester-agent", [""]),
            BuildHandoffDraft("handoff-missing-summary", "tester-agent", ["memory-validation"]) with { Summary = "" },
            BuildHandoffDraft("handoff-missing-evidence", "tester-agent", ["memory-validation"]) with { EvidenceRefs = Array.Empty<EvidenceRef>() },
            BuildHandoffDraft("handoff-missing-evidence-id", "tester-agent", ["memory-validation"]) with { EvidenceRefs = [BuildEvidence("")] },
            BuildHandoffDraft("handoff-bad-evidence-type", "tester-agent", ["memory-validation"]) with { EvidenceRefs = [BuildEvidence("bad-type") with { EvidenceType = (EvidenceType)999 }] },
            BuildHandoffDraft("handoff-missing-source", "tester-agent", ["memory-validation"]) with { EvidenceRefs = [BuildEvidence("missing-source") with { SourceId = "" }] },
            BuildHandoffDraft("handoff-bad-use", "tester-agent", ["memory-validation"]) with { AllowedUse = (HandoffMemoryAllowedUse)999 },
            BuildHandoffDraft("handoff-low-confidence", "tester-agent", ["memory-validation"]) with { Confidence = -0.1m },
            BuildHandoffDraft("handoff-high-confidence", "tester-agent", ["memory-validation"]) with { Confidence = 1.1m },
            BuildHandoffDraft("handoff-bad-expiry", "tester-agent", ["memory-validation"]) with { ExpiresAt = Now.AddMinutes(-1) }
        };

        foreach (var draft in invalidDrafts)
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => source.CreateHandoffAsync(draft));
        }
    }

    [TestMethod]
    public async Task AgentMemoryHandoff_RejectsDuplicateHandoffId()
    {
        var source = _siloService.Open(BuildContext(agentId: "builder-agent"));
        await source.CreateAsync(BuildMemoryDraft("memory-duplicate"));
        await source.CreateHandoffAsync(BuildHandoffDraft("handoff-duplicate", "tester-agent", ["memory-duplicate"]));

        await Assert.ThrowsExactlyAsync<SqlException>(() =>
            source.CreateHandoffAsync(BuildHandoffDraft("handoff-duplicate", "tester-agent", ["memory-duplicate"])));
    }

    [TestMethod]
    public async Task AgentMemoryHandoff_IncomingAndOutgoingQueriesRespectExpiry()
    {
        var source = _siloService.Open(BuildContext(agentId: "builder-agent"));
        var target = _siloService.Open(BuildContext(agentId: "tester-agent"));
        await source.CreateAsync(BuildMemoryDraft("memory-expiring-handoff"));
        await source.CreateHandoffAsync(BuildHandoffDraft("handoff-expired-by-time", "tester-agent", ["memory-expiring-handoff"]) with
        {
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1)
        });

        Assert.IsEmpty(await target.QueryIncomingHandoffsAsync(new HandoffMemorySliceQuery()));
        Assert.IsEmpty(await source.QueryOutgoingHandoffsAsync(new HandoffMemorySliceQuery()));
        Assert.HasCount(1, await target.QueryIncomingHandoffsAsync(new HandoffMemorySliceQuery { IncludeExpired = true }));
        Assert.HasCount(1, await source.QueryOutgoingHandoffsAsync(new HandoffMemorySliceQuery { IncludeExpired = true }));
    }

    [TestMethod]
    public async Task AgentMemoryHandoff_InfluenceIdsMustBelongToSourceScope()
    {
        var source = _siloService.Open(BuildContext(agentId: "builder-agent"));
        var other = _siloService.Open(BuildContext(agentId: "critic-agent"));
        await source.CreateAsync(BuildMemoryDraft("memory-source"));
        await other.CreateAsync(BuildMemoryDraft("memory-other"));
        await other.RecordInfluenceAsync(BuildInfluenceDraft("influence-other", "memory-other"));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            source.CreateHandoffAsync(BuildHandoffDraft("handoff-bad-influence", "tester-agent", ["memory-source"]) with
            {
                InfluenceIds = ["influence-other"]
            }));
    }

    [TestMethod]
    public async Task AgentMemoryHandoff_DirectSqlSourceScopeMismatchBlocked()
    {
        var source = _siloService.Open(BuildContext(agentId: "builder-agent"));
        await source.CreateAsync(BuildMemoryDraft("memory-direct-scope"));

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await Assert.ThrowsExactlyAsync<SqlException>(() => InsertDirectHandoffAsync(
            connection,
            "handoff-direct-scope",
            """["memory-direct-scope"]""",
            sourceAgentId: "critic-agent"));
    }

    [TestMethod]
    public async Task AgentMemoryHandoff_DirectSqlTerminalMemoryBlocked()
    {
        var source = _siloService.Open(BuildContext(agentId: "builder-agent"));
        await source.CreateAsync(BuildMemoryDraft("memory-direct-terminal"));
        await source.AddEventAsync(BuildEventDraft("memory-direct-terminal", AgentLocalMemoryEventType.Expired, 1));

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await Assert.ThrowsExactlyAsync<SqlException>(() => InsertDirectHandoffAsync(
            connection,
            "handoff-direct-terminal",
            """["memory-direct-terminal"]"""));
    }

    [TestMethod]
    public async Task AgentMemoryHandoff_DirectSqlTimeExpiredMemoryBlocked()
    {
        var source = _siloService.Open(BuildContext(agentId: "builder-agent"));
        await source.CreateAsync(BuildMemoryDraft("memory-direct-time-expired") with
        {
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1)
        });

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await Assert.ThrowsExactlyAsync<SqlException>(() => InsertDirectHandoffAsync(
            connection,
            "handoff-direct-time-expired",
            """["memory-direct-time-expired"]"""));
    }

    private static async Task InsertDirectHandoffAsync(
        SqlConnection connection,
        string handoffId,
        string memoryItemIdsJson,
        string sourceAgentId = "builder-agent")
    {
        await connection.ExecuteAsync(
            """
            INSERT INTO agent.AgentMemoryHandoffSlice
            (
                HandoffMemorySliceId,
                TenantId,
                ProjectId,
                CampaignId,
                RunId,
                SourceAgentId,
                TargetAgentId,
                MemoryItemIdsJson,
                MemorySnapshotsJson,
                Summary,
                AllowedUse,
                EvidenceRefsJson,
                Confidence,
                CreatedAtUtc
            )
            VALUES
            (
                @HandoffMemorySliceId,
                'tenant-1',
                'project-1',
                'campaign-1',
                'run-1',
                @SourceAgentId,
                'tester-agent',
                @MemoryItemIdsJson,
                '[{"memoryItemId":"direct-memory","memoryType":2,"authorityLevelAtHandoff":1,"statusAtHandoff":1,"title":"Direct","summary":"Direct snapshot.","confidence":0.7}]',
                'Direct SQL handoff attempt.',
                1,
                '[{"evidenceId":"direct-evidence","evidenceType":3,"sourceId":"direct-source"}]',
                0.7,
                @CreatedAtUtc
            );
            """,
            new
            {
                HandoffMemorySliceId = handoffId,
                SourceAgentId = sourceAgentId,
                MemoryItemIdsJson = memoryItemIdsJson,
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
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_memory_handoff.sql")));
    }

    private async Task DropAgentMemorySchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root for agent memory handoff tests.");
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
            CreatedByUserId = "human-reviewer",
            DecisionId = "decision-event",
            ThoughtLedgerEntryId = "thought-1",
            EventJson = "{}"
        };

    private static MemoryInfluenceDraft BuildInfluenceDraft(string influenceId, string memoryItemId) =>
        new()
        {
            InfluenceId = influenceId,
            MemoryItemId = memoryItemId,
            DecisionId = "decision-1",
            InfluenceType = MemoryInfluenceType.HandoffIncluded,
            InfluenceSummary = "Memory was selected for an explicit handoff.",
            EvidenceRefs = [BuildEvidence("influence-evidence")],
            Confidence = 0.7m,
            CreatedAt = Now.AddMinutes(5)
        };

    private static HandoffMemorySliceDraft BuildHandoffDraft(
        string handoffId,
        string targetAgentId,
        IReadOnlyList<string> memoryItemIds) =>
        new()
        {
            HandoffMemorySliceId = handoffId,
            TargetAgentId = targetAgentId,
            MemoryItemIds = memoryItemIds,
            Summary = "Builder hands bounded memory context to Tester.",
            AllowedUse = HandoffMemoryAllowedUse.ContextOnly,
            EvidenceRefs = [BuildEvidence("handoff-evidence")],
            Confidence = 0.7m,
            CreatedAt = Now.AddMinutes(10),
            CorrelationId = "correlation-1",
            HandoffJson = "{}"
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
