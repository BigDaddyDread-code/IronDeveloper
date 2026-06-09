using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Data;
using IronDev.Infrastructure.AgentMemory;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class SqlAgentLocalMemoryStoreAgentMemoryTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);

    private SqlAgentLocalMemoryStore _store = null!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropAgentMemorySchemaAsync();
        _store = new SqlAgentLocalMemoryStore(
            ServiceProvider.GetRequiredService<IDbConnectionFactory>(),
            new AgentMemoryContractValidator());
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
    public async Task SqlAgentLocalMemoryStore_CreatesRequiredTablesIndexesTriggersAndView()
    {
        await _store.EnsureSchemaAsync();

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var objectCount = await connection.QuerySingleAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.objects
            WHERE object_id IN
            (
                OBJECT_ID('agent.AgentLocalMemoryItem', 'U'),
                OBJECT_ID('agent.AgentLocalMemoryEvidenceRef', 'U'),
                OBJECT_ID('agent.AgentLocalMemoryEvent', 'U'),
                OBJECT_ID('agent.vwAgentLocalMemoryCurrentState', 'V'),
                OBJECT_ID('agent.TR_AgentLocalMemoryItem_BlockUpdateDelete', 'TR'),
                OBJECT_ID('agent.TR_AgentLocalMemoryEvidenceRef_BlockUpdateDelete', 'TR'),
                OBJECT_ID('agent.TR_AgentLocalMemoryEvent_BlockUpdateDelete', 'TR')
            );
            """);

        var indexCount = await connection.QuerySingleAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.indexes
            WHERE name IN
            (
                'IX_AgentLocalMemoryItem_Scope',
                'IX_AgentLocalMemoryItem_CampaignAgent',
                'IX_AgentLocalMemoryEvent_MemoryItem_CreatedAt',
                'IX_AgentLocalMemoryEvidenceRef_MemoryItem'
            );
            """);

        Assert.AreEqual(7, objectCount);
        Assert.AreEqual(4, indexCount);
    }

    [TestMethod]
    public async Task SqlAgentLocalMemoryStore_RejectsSilentUpdateDeleteOnMemoryTables()
    {
        await _store.CreateAsync(BuildMemoryItem());

        await AssertSqlFailsAsync("UPDATE agent.AgentLocalMemoryItem SET Title = 'mutated' WHERE MemoryItemId = 'memory-1';");
        await AssertSqlFailsAsync("DELETE FROM agent.AgentLocalMemoryItem WHERE MemoryItemId = 'memory-1';");
        await AssertSqlFailsAsync("UPDATE agent.AgentLocalMemoryEvidenceRef SET SourceId = 'mutated' WHERE MemoryItemId = 'memory-1';");
        await AssertSqlFailsAsync("DELETE FROM agent.AgentLocalMemoryEvidenceRef WHERE MemoryItemId = 'memory-1';");
        await AssertSqlFailsAsync("UPDATE agent.AgentLocalMemoryEvent SET EventReason = 'mutated' WHERE MemoryItemId = 'memory-1';");
        await AssertSqlFailsAsync("DELETE FROM agent.AgentLocalMemoryEvent WHERE MemoryItemId = 'memory-1';");
    }

    [TestMethod]
    public async Task SqlAgentLocalMemoryStore_CanCreateValidObservedEpisodicMemoryWithEvidence()
    {
        await _store.CreateAsync(BuildMemoryItem());

        var loaded = await _store.GetOwnMemoryItemAsync(BuildScope(), "memory-1");

        Assert.IsNotNull(loaded);
        Assert.AreEqual("memory-1", loaded.MemoryItemId);
        Assert.AreEqual(AgentMemoryType.Episodic, loaded.MemoryType);
        Assert.AreEqual(MemoryAuthorityLevel.ObservedOnly, loaded.AuthorityLevel);
        Assert.AreEqual(MemoryLifecycleStatus.Active, loaded.Status);
        Assert.HasCount(1, loaded.EvidenceRefs);
        Assert.AreEqual(EvidenceType.TestResult, loaded.EvidenceRefs[0].EvidenceType);
    }

    [TestMethod]
    public async Task SqlAgentLocalMemoryStore_CanCreateFailedAttemptWithBuildResultEvidence()
    {
        var item = BuildMemoryItem("memory-build-failure") with
        {
            MemoryType = AgentMemoryType.FailedAttempt,
            Title = "Build failed after namespace fix",
            Summary = "Builder tried fix A and build still failed.",
            EvidenceRefs =
            [
                BuildEvidence("evidence-build") with { EvidenceType = EvidenceType.BuildResult }
            ]
        };

        await _store.CreateAsync(item);

        var loaded = await _store.GetOwnMemoryItemAsync(BuildScope(), item.MemoryItemId);

        Assert.IsNotNull(loaded);
        Assert.AreEqual(AgentMemoryType.FailedAttempt, loaded.MemoryType);
        Assert.AreEqual(EvidenceType.BuildResult, loaded.EvidenceRefs.Single().EvidenceType);
    }

    [TestMethod]
    public async Task SqlAgentLocalMemoryStore_CandidatePatternRequiresEvidenceAndKnownLimitations()
    {
        var valid = BuildCandidatePattern("memory-candidate-valid");

        await _store.CreateAsync(valid);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _store.CreateAsync(BuildCandidatePattern("memory-candidate-no-limitations") with
            {
                KnownLimitations = null
            }));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _store.CreateAsync(BuildCandidatePattern("memory-candidate-no-evidence") with
            {
                EvidenceRefs = Array.Empty<EvidenceRef>()
            }));
    }

    [TestMethod]
    public async Task SqlAgentLocalMemoryStore_CreationInsertsCreatedEventAndEvidenceRefs()
    {
        await _store.CreateAsync(BuildMemoryItem());

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var evidenceCount = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM agent.AgentLocalMemoryEvidenceRef WHERE MemoryItemId = 'memory-1';");
        var createdEventCount = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM agent.AgentLocalMemoryEvent WHERE MemoryItemId = 'memory-1' AND EventType = 1;");

        Assert.AreEqual(1, evidenceCount);
        Assert.AreEqual(1, createdEventCount);
    }

    [TestMethod]
    public async Task SqlAgentLocalMemoryStore_CreationIsTransactionalWhenEvidenceInsertFails()
    {
        var item = BuildMemoryItem("memory-transactional") with
        {
            EvidenceRefs =
            [
                BuildEvidence("duplicate-evidence"),
                BuildEvidence("duplicate-evidence") with { SourceId = "source-2" }
            ]
        };

        await Assert.ThrowsExactlyAsync<SqlException>(() => _store.CreateAsync(item));

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var itemCount = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM agent.AgentLocalMemoryItem WHERE MemoryItemId = 'memory-transactional';");
        var evidenceCount = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM agent.AgentLocalMemoryEvidenceRef WHERE MemoryItemId = 'memory-transactional';");
        var eventCount = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM agent.AgentLocalMemoryEvent WHERE MemoryItemId = 'memory-transactional';");

        Assert.AreEqual(0, itemCount);
        Assert.AreEqual(0, evidenceCount);
        Assert.AreEqual(0, eventCount);
    }

    [TestMethod]
    public async Task SqlAgentLocalMemoryStore_InvalidLocalMemoryCreationIsRejected()
    {
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _store.CreateAsync(BuildMemoryItem("memory-confidence") with { Confidence = 1.1m }));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _store.CreateAsync(BuildMemoryItem("memory-accepted") with { AuthorityLevel = MemoryAuthorityLevel.Accepted }));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _store.CreateAsync(BuildMemoryItem("memory-system-rule") with { AuthorityLevel = MemoryAuthorityLevel.SystemRule }));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _store.CreateAsync(BuildMemoryItem("memory-proposed") with { AuthorityLevel = MemoryAuthorityLevel.Proposed }));
    }

    [TestMethod]
    public async Task SqlAgentLocalMemoryStore_AgentCanReadOwnMemoryButNotOtherScopes()
    {
        var item = BuildMemoryItem();
        await _store.CreateAsync(item);

        var own = await _store.GetOwnMemoryItemAsync(item.Scope, item.MemoryItemId);
        Assert.IsNotNull(own);

        var forbiddenScopes = new[]
        {
            item.Scope with { AgentId = "critic-agent" },
            item.Scope with { RunId = "run-2" },
            item.Scope with { CampaignId = "campaign-2" },
            item.Scope with { ProjectId = "project-2" },
            item.Scope with { TenantId = "tenant-2" }
        };

        foreach (var scope in forbiddenScopes)
        {
            var loaded = await _store.GetOwnMemoryItemAsync(scope, item.MemoryItemId);
            var queried = await _store.QueryOwnMemoryAsync(scope, new AgentLocalMemoryQuery());

            Assert.IsNull(loaded, $"Scope should not read memory: {scope}");
            Assert.IsEmpty(queried);
        }
    }

    [TestMethod]
    public async Task SqlAgentLocalMemoryStore_CanAppendLifecycleEventsAndReconstructHistory()
    {
        var item = BuildMemoryItem();
        await _store.CreateAsync(item);
        await _store.AddEventAsync(item.Scope, BuildEvent(item.MemoryItemId, AgentLocalMemoryEventType.Superseded, 1));
        await _store.AddEventAsync(item.Scope, BuildEvent(item.MemoryItemId, AgentLocalMemoryEventType.Expired, 2));
        await _store.AddEventAsync(item.Scope, BuildEvent(item.MemoryItemId, AgentLocalMemoryEventType.Invalidated, 3));
        await _store.AddEventAsync(item.Scope, BuildEvent(item.MemoryItemId, AgentLocalMemoryEventType.ProposedForReview, 4));

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var eventTypes = (await connection.QueryAsync<int>(
            """
            SELECT EventType
            FROM agent.AgentLocalMemoryEvent
            WHERE MemoryItemId = @MemoryItemId
            ORDER BY CreatedAtUtc, MemoryEventId;
            """,
            new { item.MemoryItemId })).ToArray();

        var latest = await connection.QuerySingleAsync<int>(
            "SELECT CurrentEventType FROM agent.vwAgentLocalMemoryCurrentState WHERE MemoryItemId = @MemoryItemId;",
            new { item.MemoryItemId });

        CollectionAssert.AreEqual(
            new[]
            {
                (int)AgentLocalMemoryEventType.Created,
                (int)AgentLocalMemoryEventType.Superseded,
                (int)AgentLocalMemoryEventType.Expired,
                (int)AgentLocalMemoryEventType.Invalidated,
                (int)AgentLocalMemoryEventType.ProposedForReview
            },
            eventTypes);
        Assert.AreEqual((int)AgentLocalMemoryEventType.ProposedForReview, latest);

        var loaded = await _store.GetOwnMemoryItemAsync(item.Scope, item.MemoryItemId);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(MemoryLifecycleStatus.ProposedForReview, loaded.Status);
    }

    [TestMethod]
    public async Task SqlAgentLocalMemoryStore_AppendingEventDoesNotMutateOriginalMemoryItem()
    {
        var item = BuildMemoryItem();
        await _store.CreateAsync(item);

        await _store.AddEventAsync(item.Scope, BuildEvent(item.MemoryItemId, AgentLocalMemoryEventType.Expired, 1));

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var row = await connection.QuerySingleAsync<dynamic>(
            """
            SELECT Title, Summary, CreatedAtUtc
            FROM agent.AgentLocalMemoryItem
            WHERE MemoryItemId = @MemoryItemId;
            """,
            new { item.MemoryItemId });

        Assert.AreEqual(item.Title, (string)row.Title);
        Assert.AreEqual(item.Summary, (string)row.Summary);
        Assert.AreEqual(item.CreatedAt.UtcDateTime, (DateTime)row.CreatedAtUtc);
    }

    [TestMethod]
    public async Task SqlAgentLocalMemoryStore_EventAppendRequiresOwningScope()
    {
        var item = BuildMemoryItem();
        await _store.CreateAsync(item);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _store.AddEventAsync(item.Scope with { AgentId = "critic-agent" }, BuildEvent(item.MemoryItemId, AgentLocalMemoryEventType.Expired, 1)));
    }

    [TestMethod]
    public async Task SqlAgentLocalMemoryStore_RejectedAndAcceptedEventsAreNotLocalMemoryMutationPaths()
    {
        var item = BuildMemoryItem();
        await _store.CreateAsync(item);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _store.AddEventAsync(item.Scope, BuildEvent(item.MemoryItemId, AgentLocalMemoryEventType.Rejected, 1)));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _store.AddEventAsync(item.Scope, BuildEvent(item.MemoryItemId, AgentLocalMemoryEventType.Accepted, 2)));
    }

    [TestMethod]
    public void SqlAgentLocalMemoryStore_DoesNotExposeSilentMutationOrCrossAgentQueryMethods()
    {
        var methodNames = typeof(IAgentLocalMemoryStore)
            .GetMethods()
            .Select(method => method.Name)
            .ToArray();

        var forbiddenFragments = new[]
        {
            "UpdateMemoryItem",
            "DeleteMemoryItem",
            "UpdateEvidenceRef",
            "DeleteEvidenceRef",
            "UpdateMemoryEvent",
            "DeleteMemoryEvent",
            "QueryCampaignMemory",
            "QueryAllAgentMemory",
            "SearchMemoryAcrossAgents",
            "SearchAllMemory"
        };

        foreach (var forbidden in forbiddenFragments)
        {
            Assert.IsFalse(methodNames.Any(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)),
                $"Agent local memory store must not expose forbidden method '{forbidden}'.");
        }
    }

    private async Task AssertSqlFailsAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await Assert.ThrowsExactlyAsync<SqlException>(() => connection.ExecuteAsync(sql));
    }

    private async Task DropAgentMemorySchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            IF OBJECT_ID('agent.vwAgentLocalMemoryCurrentState', 'V') IS NOT NULL
                DROP VIEW agent.vwAgentLocalMemoryCurrentState;
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

    private static AgentLocalMemoryItem BuildMemoryItem(string memoryItemId = "memory-1") =>
        new()
        {
            MemoryItemId = memoryItemId,
            Scope = BuildScope(),
            MemoryType = AgentMemoryType.Episodic,
            AuthorityLevel = MemoryAuthorityLevel.ObservedOnly,
            Title = "Observed build failure",
            Summary = "TesterAgent observed a reproducible build failure.",
            EvidenceRefs = [BuildEvidence()],
            Confidence = 0.8m,
            Status = MemoryLifecycleStatus.Active,
            CreatedAt = Now
        };

    private static AgentLocalMemoryItem BuildCandidatePattern(string memoryItemId) =>
        BuildMemoryItem(memoryItemId) with
        {
            MemoryType = AgentMemoryType.CandidatePattern,
            AuthorityLevel = MemoryAuthorityLevel.CandidatePattern,
            Title = "Potential package restore pattern",
            Summary = "Missing namespace failures may require restore inspection first.",
            KnownLimitations = "Observed in one run only. Not accepted memory."
        };

    private static AgentLocalMemoryEventRecord BuildEvent(
        string memoryItemId,
        AgentLocalMemoryEventType eventType,
        int minutesAfterCreated) =>
        new()
        {
            MemoryEventId = $"event-{eventType}-{minutesAfterCreated}",
            MemoryItemId = memoryItemId,
            EventType = eventType,
            EventReason = $"Lifecycle event {eventType}.",
            CreatedAt = Now.AddMinutes(minutesAfterCreated),
            CreatedByAgentId = "builder-agent"
        };

    private static AgentMemoryScope BuildScope() =>
        new()
        {
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RunId = "run-1",
            AgentId = "builder-agent"
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

