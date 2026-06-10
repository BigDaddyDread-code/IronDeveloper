using System.Reflection;
using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Data;
using IronDev.Infrastructure.AgentMemory;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AgentMemorySiloServiceTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);

    private AgentMemorySiloService _siloService = null!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropAgentMemorySchemaAsync();
        await ApplyAgentMemoryMigrationAsync();

        var store = new SqlAgentLocalMemoryStore(
            ServiceProvider.GetRequiredService<IDbConnectionFactory>(),
            new AgentMemoryContractValidator());
        var influenceStore = new SqlAgentMemoryInfluenceStore(
            ServiceProvider.GetRequiredService<IDbConnectionFactory>());
        var handoffStore = new SqlAgentMemoryHandoffStore(
            ServiceProvider.GetRequiredService<IDbConnectionFactory>());

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
    public void AgentMemorySilo_OpenRejectsIncompleteContext()
    {
        var valid = BuildContext();

        var invalidContexts = new[]
        {
            valid with { TenantId = "" },
            valid with { ProjectId = "" },
            valid with { CampaignId = "" },
            valid with { RunId = "" },
            valid with { AgentId = "" }
        };

        foreach (var context in invalidContexts)
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => _siloService.Open(context));
        }
    }

    [TestMethod]
    public void AgentMemorySilo_OpenReturnsSiloWithExactScope()
    {
        var context = BuildContext();

        var silo = _siloService.Open(context);

        Assert.AreEqual(context.TenantId, silo.Scope.TenantId);
        Assert.AreEqual(context.ProjectId, silo.Scope.ProjectId);
        Assert.AreEqual(context.CampaignId, silo.Scope.CampaignId);
        Assert.AreEqual(context.RunId, silo.Scope.RunId);
        Assert.AreEqual(context.AgentId, silo.Scope.AgentId);
    }

    [TestMethod]
    public async Task AgentMemorySilo_AgentBCannotReadAgentAMemory()
    {
        var agentA = _siloService.Open(BuildContext(agentId: "builder-agent"));
        var agentB = _siloService.Open(BuildContext(agentId: "critic-agent"));

        await agentA.CreateAsync(BuildDraft("memory-a"));

        var own = await agentA.GetAsync("memory-a");
        var leakedById = await agentB.GetAsync("memory-a");
        var leakedByQuery = await agentB.QueryAsync(new AgentLocalMemoryQuery { IncludeExpired = true });

        Assert.IsNotNull(own);
        Assert.IsNull(leakedById);
        Assert.IsEmpty(leakedByQuery);
    }

    [TestMethod]
    public async Task AgentMemorySilo_AgentBCannotAppendEventToAgentAMemory()
    {
        var agentA = _siloService.Open(BuildContext(agentId: "builder-agent"));
        var agentB = _siloService.Open(BuildContext(agentId: "critic-agent"));

        await agentA.CreateAsync(BuildDraft("memory-a"));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            agentB.AddEventAsync(BuildEventDraft("memory-a", AgentLocalMemoryEventType.Expired, 1)));

        var agentAHistory = await agentA.GetEventHistoryAsync("memory-a");
        var agentBHistory = await agentB.GetEventHistoryAsync("memory-a");

        Assert.HasCount(1, agentAHistory);
        Assert.AreEqual(AgentLocalMemoryEventType.Created, agentAHistory[0].EventType);
        Assert.IsEmpty(agentBHistory);
    }

    [TestMethod]
    public async Task AgentMemorySilo_EventHistoryDoesNotLeakAcrossScope()
    {
        var agentA = _siloService.Open(BuildContext());
        await agentA.CreateAsync(BuildDraft("memory-history"));
        await agentA.AddEventAsync(BuildEventDraft("memory-history", AgentLocalMemoryEventType.Expired, 1));

        var forbiddenContexts = new[]
        {
            BuildContext(tenantId: "tenant-2"),
            BuildContext(projectId: "project-2"),
            BuildContext(campaignId: "campaign-2"),
            BuildContext(runId: "run-2"),
            BuildContext(agentId: "critic-agent")
        };

        foreach (var context in forbiddenContexts)
        {
            var hidden = await _siloService.Open(context).GetEventHistoryAsync("memory-history");
            Assert.IsEmpty(hidden, $"History leaked for context {context}.");
        }
    }

    [TestMethod]
    public async Task AgentMemorySilo_InsertsCreatedByAgentIdFromBoundScope()
    {
        var agentA = _siloService.Open(BuildContext(agentId: "builder-agent"));
        await agentA.CreateAsync(BuildDraft("memory-event-agent"));

        await agentA.AddEventAsync(BuildEventDraft("memory-event-agent", AgentLocalMemoryEventType.Expired, 1));

        var history = await agentA.GetEventHistoryAsync("memory-event-agent");

        Assert.HasCount(2, history);
        Assert.AreEqual("builder-agent", history[1].CreatedByAgentId);
    }

    [TestMethod]
    public async Task AgentMemorySilo_CannotCreateAcceptedOrSystemRuleMemory()
    {
        var silo = _siloService.Open(BuildContext());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            silo.CreateAsync(BuildDraft("memory-accepted") with { AuthorityLevel = MemoryAuthorityLevel.Accepted }));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            silo.CreateAsync(BuildDraft("memory-system-rule") with { AuthorityLevel = MemoryAuthorityLevel.SystemRule }));
    }

    [TestMethod]
    public async Task AgentMemorySilo_CannotAppendCreatedRejectedOrAcceptedEvents()
    {
        var silo = _siloService.Open(BuildContext());
        await silo.CreateAsync(BuildDraft("memory-event-block"));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            silo.AddEventAsync(BuildEventDraft("memory-event-block", AgentLocalMemoryEventType.Created, 1)));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            silo.AddEventAsync(BuildEventDraft("memory-event-block", AgentLocalMemoryEventType.Rejected, 2)));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            silo.AddEventAsync(BuildEventDraft("memory-event-block", AgentLocalMemoryEventType.Accepted, 3)));
    }

    [TestMethod]
    public async Task AgentMemorySilo_CandidatePatternStillRequiresEvidenceAndLimitations()
    {
        var silo = _siloService.Open(BuildContext());
        var valid = BuildCandidatePatternDraft("memory-candidate-valid");

        await silo.CreateAsync(valid);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            silo.CreateAsync(BuildCandidatePatternDraft("memory-candidate-no-evidence") with
            {
                EvidenceRefs = Array.Empty<EvidenceRef>()
            }));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            silo.CreateAsync(BuildCandidatePatternDraft("memory-candidate-no-limitations") with
            {
                KnownLimitations = null
            }));
    }

    [TestMethod]
    public async Task AgentMemorySilo_QueryExcludesTimeExpiredMemoryByDefault()
    {
        var silo = _siloService.Open(BuildContext());
        var draft = BuildDraft("memory-time-expired") with
        {
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        await silo.CreateAsync(draft);

        var activeOnly = await silo.QueryAsync(new AgentLocalMemoryQuery());
        var withExpired = await silo.QueryAsync(new AgentLocalMemoryQuery { IncludeExpired = true });

        Assert.IsFalse(activeOnly.Any(item => item.MemoryItemId == draft.MemoryItemId));
        Assert.IsTrue(withExpired.Any(item => item.MemoryItemId == draft.MemoryItemId));
    }

    [TestMethod]
    public async Task AgentMemorySilo_QueryExcludesEventExpiredMemoryByDefault()
    {
        var silo = _siloService.Open(BuildContext());
        await silo.CreateAsync(BuildDraft("memory-event-expired"));
        await silo.AddEventAsync(BuildEventDraft("memory-event-expired", AgentLocalMemoryEventType.Expired, 1));

        var activeOnly = await silo.QueryAsync(new AgentLocalMemoryQuery());
        var withExpired = await silo.QueryAsync(new AgentLocalMemoryQuery { IncludeExpired = true });

        Assert.IsFalse(activeOnly.Any(item => item.MemoryItemId == "memory-event-expired"));
        Assert.IsTrue(withExpired.Any(item => item.MemoryItemId == "memory-event-expired"));
    }

    [TestMethod]
    public void AgentMemorySilo_DraftModelsCannotCarryScope()
    {
        var memoryDraftProperties = typeof(AgentLocalMemoryDraft).GetProperties().Select(property => property.Name).ToArray();
        var eventDraftProperties = typeof(AgentLocalMemoryEventDraft).GetProperties().Select(property => property.Name).ToArray();
        var forbiddenMemoryDraftProperties = new[] { "TenantId", "ProjectId", "CampaignId", "RunId", "AgentId", "Scope", "AgentMemoryScope" };
        var forbiddenEventDraftProperties = new[] { "TenantId", "ProjectId", "CampaignId", "RunId", "AgentId", "CreatedByAgentId", "Scope", "AgentMemoryScope" };

        foreach (var forbidden in forbiddenMemoryDraftProperties)
        {
            Assert.IsFalse(memoryDraftProperties.Contains(forbidden, StringComparer.Ordinal),
                $"AgentLocalMemoryDraft must not expose caller-supplied scope property '{forbidden}'.");
        }

        foreach (var forbidden in forbiddenEventDraftProperties)
        {
            Assert.IsFalse(eventDraftProperties.Contains(forbidden, StringComparer.Ordinal),
                $"AgentLocalMemoryEventDraft must not expose caller-supplied scope/agent property '{forbidden}'.");
        }
    }

    [TestMethod]
    public void AgentMemorySilo_DoesNotExposeScopeTakingMethods()
    {
        foreach (var method in typeof(IAgentMemorySilo).GetMethods())
        {
            Assert.IsFalse(method.GetParameters().Any(parameter => parameter.ParameterType == typeof(AgentMemoryScope)),
                $"IAgentMemorySilo.{method.Name} must not accept AgentMemoryScope; the silo is already bound.");
        }
    }

    [TestMethod]
    public void AgentMemorySilo_ConcreteImplementationIsNotPublic()
    {
        var siloType = typeof(AgentMemorySiloService).Assembly.GetType("IronDev.Infrastructure.AgentMemory.AgentMemorySilo");

        Assert.IsNotNull(siloType);
        Assert.IsTrue(siloType.IsNotPublic, "Concrete AgentMemorySilo must not be public; callers must use IAgentMemorySiloService.");
        Assert.IsFalse(siloType.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Any(),
            "Concrete AgentMemorySilo must not expose a public constructor that accepts AgentMemoryScope.");
    }

    [TestMethod]
    public void AgentMemorySiloService_ExposesNoCrossAgentOrCampaignWideOperations()
    {
        var siloMethodNames = typeof(IAgentMemorySilo).GetMethods().Select(method => method.Name).ToArray();
        var serviceMethodNames = typeof(IAgentMemorySiloService).GetMethods().Select(method => method.Name).ToArray();
        var forbiddenFragments = new[]
        {
            "QueryCampaignMemory",
            "QueryAllAgentMemory",
            "SearchAcrossAgents",
            "SearchAllMemory",
            "GetAgentMemory",
            "OpenAgentMemory",
            "OpenOtherAgentSilo"
        };

        foreach (var forbidden in forbiddenFragments)
        {
            Assert.IsFalse(siloMethodNames.Any(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)),
                $"IAgentMemorySilo must not expose cross-agent operation '{forbidden}'.");
            Assert.IsFalse(serviceMethodNames.Any(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)),
                $"IAgentMemorySiloService must not expose cross-agent operation '{forbidden}'.");
        }
    }

    private async Task ApplyAgentMemoryMigrationAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_local_memory.sql")));
    }

    private async Task DropAgentMemorySchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            IF OBJECT_ID('agent.TR_AgentMemoryExecutionAudit_ValidateInsert', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryExecutionAudit_ValidateInsert;
            IF OBJECT_ID('agent.TR_AgentMemoryExecutionAudit_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryExecutionAudit_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentMemoryExecutionAudit', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryExecutionAudit;            IF OBJECT_ID('agent.vwAgentLocalMemoryCurrentState', 'V') IS NOT NULL
                DROP VIEW agent.vwAgentLocalMemoryCurrentState;
            IF OBJECT_ID('agent.TR_AgentMemoryHandoffSlice_ValidateSourceMemory', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryHandoffSlice_ValidateSourceMemory;
            IF OBJECT_ID('agent.TR_AgentMemoryHandoffSlice_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentMemoryHandoffSlice_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentMemoryHandoffSlice', 'U') IS NOT NULL
                DROP TABLE agent.AgentMemoryHandoffSlice;
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

        throw new InvalidOperationException("Could not locate repository root for agent memory silo tests.");
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

    private static AgentLocalMemoryDraft BuildDraft(string memoryItemId = "memory-1") =>
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

    private static AgentLocalMemoryDraft BuildCandidatePatternDraft(string memoryItemId) =>
        BuildDraft(memoryItemId) with
        {
            MemoryType = AgentMemoryType.CandidatePattern,
            AuthorityLevel = MemoryAuthorityLevel.CandidatePattern,
            Title = "Potential package restore pattern",
            Summary = "Missing namespace failures may require restore inspection first.",
            KnownLimitations = "Observed in one run only. Not accepted memory."
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
            DecisionId = "decision-1",
            ThoughtLedgerEntryId = "thought-1",
            EventJson = "{}"
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
