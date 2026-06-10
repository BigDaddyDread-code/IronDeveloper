using System.Reflection;
using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Data;
using IronDev.Infrastructure.AgentMemory;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class MemoryIndexingBoundaryTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);

    private SqlAgentLocalMemoryStore _memoryStore = null!;
    private SqlAgentMemoryInfluenceStore _influenceStore = null!;
    private SqlAgentMemoryHandoffStore _handoffStore = null!;
    private SqlMemoryImprovementProposalService _proposalService = null!;
    private SqlMemoryIndexQueueStore _queueStore = null!;
    private MemoryIndexingService _indexingService = null!;
    private FakeWeaviateMemoryIndexer _indexer = null!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropAgentMemorySchemaAsync();
        await ApplyAgentMemoryMigrationsAsync();

        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        _memoryStore = new SqlAgentLocalMemoryStore(connectionFactory, new AgentMemoryContractValidator());
        _influenceStore = new SqlAgentMemoryInfluenceStore(connectionFactory);
        _handoffStore = new SqlAgentMemoryHandoffStore(connectionFactory);
        _proposalService = new SqlMemoryImprovementProposalService(connectionFactory);
        _queueStore = new SqlMemoryIndexQueueStore(connectionFactory);
        var projectionBuilder = new SqlMemoryIndexProjectionBuilder(connectionFactory);
        _indexer = new FakeWeaviateMemoryIndexer();
        _indexingService = new MemoryIndexingService(projectionBuilder, _queueStore, _indexer);
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
    public async Task MemoryIndexingBoundaryMigration_CreatesTablesIndexesAndTriggers()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var objectCount = await connection.QuerySingleAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.objects
            WHERE object_id IN
            (
                OBJECT_ID('agent.AgentMemoryIndexQueue', 'U'),
                OBJECT_ID('agent.AgentMemoryIndexEvent', 'U'),
                OBJECT_ID('agent.TR_AgentMemoryIndexQueue_BlockUpdateDelete', 'TR'),
                OBJECT_ID('agent.TR_AgentMemoryIndexEvent_BlockUpdateDelete', 'TR'),
                OBJECT_ID('agent.TR_AgentMemoryIndexQueue_ValidateProjection', 'TR'),
                OBJECT_ID('agent.TR_AgentMemoryIndexEvent_ValidateInsert', 'TR')
            );
            """);

        var indexCount = await connection.QuerySingleAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.indexes
            WHERE name IN
            (
                'IX_AgentMemoryIndexQueue_Pending',
                'IX_AgentMemoryIndexQueue_Scope',
                'IX_AgentMemoryIndexEvent_RecordCreated'
            );
            """);

        Assert.AreEqual(6, objectCount);
        Assert.AreEqual(3, indexCount);
    }

    [TestMethod]
    public async Task MemoryIndexingBoundary_QueueAndEventTablesAreAppendOnly()
    {
        await _queueStore.QueueAsync(BuildProjection("idx-append-only", MemoryIndexArtifactType.RunMemoryReport));

        await AssertSqlFailsAsync("UPDATE agent.AgentMemoryIndexQueue SET Title = 'mutated' WHERE IndexRecordId = 'idx-append-only';");
        await AssertSqlFailsAsync("DELETE FROM agent.AgentMemoryIndexQueue WHERE IndexRecordId = 'idx-append-only';");
        await AssertSqlFailsAsync("UPDATE agent.AgentMemoryIndexEvent SET Error = 'mutated' WHERE IndexRecordId = 'idx-append-only';");
        await AssertSqlFailsAsync("DELETE FROM agent.AgentMemoryIndexEvent WHERE IndexRecordId = 'idx-append-only';");
    }

    [TestMethod]
    public async Task MemoryIndexingBoundary_RejectsUnsafeProjectionInputs()
    {
        var cases = new[]
        {
            BuildProjection("idx-bad-artifact", (MemoryIndexArtifactType)999),
            BuildProjection("idx-no-evidence", MemoryIndexArtifactType.RunMemoryReport) with { EvidenceRefs = [] },
            BuildProjection("idx-bad-title", MemoryIndexArtifactType.RunMemoryReport) with { Title = "ChainOfThought leak" },
            BuildProjection("idx-bad-summary", MemoryIndexArtifactType.RunMemoryReport) with { Summary = "RawPrompt must not be indexed." },
            BuildProjection("idx-bad-metadata", MemoryIndexArtifactType.RunMemoryReport) with { Metadata = new Dictionary<string, string> { ["note"] = "Scratchpad value" } },
            BuildProjection("idx-bad-hash", MemoryIndexArtifactType.RunMemoryReport) with { SourceHashSha256 = "abc" }
        };

        foreach (var projection in cases)
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _queueStore.QueueAsync(projection));
        }
    }

    [TestMethod]
    public async Task MemoryIndexingBoundary_QueueRunCreatesAllowedProjectionTypesOnly()
    {
        await SeedRunArtifactsAsync();

        await _indexingService.QueueRunAsync("tenant-1", "project-1", "campaign-1", "run-1");

        var records = await _queueStore.QueryAsync("tenant-1", "project-1", "campaign-1", "run-1", null, 100);
        var artifactTypes = records.Select(item => item.ArtifactType).Distinct().ToArray();

        Assert.IsTrue(artifactTypes.Contains(MemoryIndexArtifactType.RunMemoryReport));
        Assert.IsTrue(artifactTypes.Contains(MemoryIndexArtifactType.AgentRunMemoryReport));
        Assert.IsTrue(artifactTypes.Contains(MemoryIndexArtifactType.MemoryInfluenceSummary));
        Assert.IsTrue(artifactTypes.Contains(MemoryIndexArtifactType.HandoffSummary));
        Assert.IsTrue(artifactTypes.Contains(MemoryIndexArtifactType.MemoryImprovementProposal));
        Assert.IsTrue(artifactTypes.Contains(MemoryIndexArtifactType.MemoryImprovementProposalEvent));
        Assert.IsFalse(Enum.GetNames<MemoryIndexArtifactType>().Any(name => name.Contains("AgentLocalMemoryItem", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(records.All(item => item.Status == MemoryIndexStatus.Pending));
        Assert.IsTrue(records.All(item => item.EvidenceRefs.Count > 0));
        Assert.IsTrue(records.All(item => IsSha256(item.SourceHashSha256)));
        Assert.IsTrue(records.All(item => item.TenantId == "tenant-1" && item.ProjectId == "project-1" && item.CampaignId == "campaign-1" && item.RunId == "run-1"));

        var queuedEventCount = await CountIndexEventsAsync(MemoryIndexEventType.Queued);
        Assert.AreEqual(records.Count, queuedEventCount);
    }

    [TestMethod]
    public async Task MemoryIndexingBoundary_ProcessPendingIndexesSuccessAndFailureWithEventsOnly()
    {
        await _queueStore.QueueAsync(BuildProjection("idx-process-success", MemoryIndexArtifactType.RunMemoryReport));
        await _queueStore.QueueAsync(BuildProjection("idx-process-failure", MemoryIndexArtifactType.AgentRunMemoryReport));
        _indexer.ShouldFail = projection => projection.IndexRecordId == "idx-process-failure";

        var processed = await _indexingService.ProcessPendingAsync("tenant-1", "project-1", 20);

        var records = await _queueStore.QueryAsync("tenant-1", "project-1", "campaign-1", "run-1", null, 20);
        var success = records.Single(item => item.IndexRecordId == "idx-process-success");
        var failure = records.Single(item => item.IndexRecordId == "idx-process-failure");

        Assert.AreEqual(2, processed);
        Assert.HasCount(2, _indexer.Indexed);
        Assert.AreEqual(MemoryIndexStatus.Indexed, success.Status);
        Assert.AreEqual("weaviate-idx-process-success", success.WeaviateObjectId);
        Assert.AreEqual(MemoryIndexStatus.Failed, failure.Status);
        Assert.AreEqual("fake failure", failure.LastError);
        Assert.AreEqual(2, await CountIndexEventsAsync(MemoryIndexEventType.Queued));
        Assert.AreEqual(1, await CountIndexEventsAsync(MemoryIndexEventType.Indexed));
        Assert.AreEqual(1, await CountIndexEventsAsync(MemoryIndexEventType.Failed));
    }

    [TestMethod]
    public async Task MemoryIndexingBoundary_ProposalAuthorityMappingDoesNotPromoteMemory()
    {
        await CreateMemoryAsync("memory-proposal-authority");

        await CreateProposalWithStatusAsync("proposal-submitted", MemoryImprovementProposalEventType.Submitted);
        await CreateProposalWithStatusAsync("proposal-accepted", MemoryImprovementProposalEventType.AcceptedForFutureImplementation);
        await CreateProposalWithStatusAsync("proposal-rejected", MemoryImprovementProposalEventType.Rejected);
        await CreateProposalWithStatusAsync("proposal-withdrawn", MemoryImprovementProposalEventType.Withdrawn);
        await CreateProposalWithStatusAsync("proposal-superseded", MemoryImprovementProposalEventType.Superseded);

        await _indexingService.QueueRunAsync("tenant-1", "project-1", "campaign-1", "run-1");

        var records = await _queueStore.QueryAsync("tenant-1", "project-1", "campaign-1", "run-1", null, 100);
        var proposals = records
            .Where(item => item.ArtifactType == MemoryIndexArtifactType.MemoryImprovementProposal)
            .ToDictionary(item => item.ArtifactId, item => item.AuthorityLevel);

        Assert.AreEqual(MemoryIndexAuthorityLevel.ReviewQueue, proposals["proposal-submitted"]);
        Assert.AreEqual(MemoryIndexAuthorityLevel.ReviewedPositive, proposals["proposal-accepted"]);
        Assert.AreEqual(MemoryIndexAuthorityLevel.Rejected, proposals["proposal-rejected"]);
        Assert.AreEqual(MemoryIndexAuthorityLevel.Deprecated, proposals["proposal-withdrawn"]);
        Assert.AreEqual(MemoryIndexAuthorityLevel.Deprecated, proposals["proposal-superseded"]);
        Assert.AreEqual(0, await CountAcceptedOrSystemRuleMemoryAsync());
        Assert.AreEqual(MemoryAuthorityLevel.ObservedOnly, await GetMemoryAuthorityAsync("memory-proposal-authority"));
    }

    [TestMethod]
    public async Task MemoryIndexingBoundary_EnforcesScopeIsolationForQueueAndProcessing()
    {
        await CreateMemoryAsync("memory-run-1");
        await CreateMemoryAsync("memory-run-2", BuildScope(runId: "run-2"));

        await _indexingService.QueueRunAsync("tenant-1", "project-1", "campaign-1", "run-1");
        await _queueStore.QueueAsync(BuildProjection("idx-foreign", MemoryIndexArtifactType.RunMemoryReport) with
        {
            TenantId = "tenant-2",
            ProjectId = "project-2",
            CampaignId = "campaign-2",
            RunId = "run-2",
            ArtifactId = "run-2"
        });

        var records = await _queueStore.QueryAsync("tenant-1", "project-1", "campaign-1", null, null, 100);
        Assert.IsTrue(records.All(item => item.RunId == "run-1"));
        Assert.IsFalse(records.Any(item => item.ArtifactId == "memory-run-2"));

        await _indexingService.ProcessPendingAsync("tenant-1", "project-1", 100);

        Assert.IsTrue(_indexer.Indexed.Count > 0);
        Assert.IsTrue(_indexer.Indexed.All(item => item.TenantId == "tenant-1" && item.ProjectId == "project-1"));

        var foreign = await _queueStore.QueryAsync("tenant-2", "project-2", "campaign-2", "run-2", null, 10);
        Assert.HasCount(1, foreign);
        Assert.AreEqual(MemoryIndexStatus.Pending, foreign[0].Status);
    }

    [TestMethod]
    public void MemoryIndexingBoundary_AgentAndGovernanceServicesDoNotExposeWeaviate()
    {
        var siloMethods = typeof(IAgentMemorySilo).GetMethods(BindingFlags.Instance | BindingFlags.Public);
        Assert.IsFalse(siloMethods.Any(method =>
            method.Name.Contains("Index", StringComparison.OrdinalIgnoreCase) ||
            method.Name.Contains("Weaviate", StringComparison.OrdinalIgnoreCase) ||
            method.ReturnType.Name.Contains("MemoryIndex", StringComparison.OrdinalIgnoreCase)));

        AssertFileDoesNotContain(Path.Combine("IronDev.Infrastructure", "AgentMemory", "AgentMemorySiloService.cs"), "Weaviate", "IMemoryIndexingService", "IWeaviateMemoryIndexer");
        AssertFileDoesNotContain(Path.Combine("IronDev.Infrastructure", "AgentMemory", "SqlConscienceMemoryGovernanceService.cs"), "Weaviate", "IMemoryIndexingService", "IWeaviateMemoryIndexer");
        AssertFileDoesNotContain(Path.Combine("IronDev.Infrastructure", "AgentMemory", "SqlMemoryImprovementProposalService.cs"), "Weaviate", "IMemoryIndexingService", "IWeaviateMemoryIndexer");
    }

    [TestMethod]
    public void MemoryIndexingBoundary_IndexModelsDoNotExposeRawReasoningPayloads()
    {
        var modelTypes = new[]
        {
            typeof(MemoryIndexProjection),
            typeof(MemoryIndexQueueRecord),
            typeof(WeaviateMemoryIndexResult)
        };

        var propertyNames = modelTypes
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Select(property => property.Name)
            .ToArray();

        string[] banned =
        [
            "RawPrompt",
            "Prompt",
            "RawCompletion",
            "Completion",
            "ChainOfThought",
            "Scratchpad",
            "PrivateReasoning"
        ];

        foreach (var bannedName in banned)
        {
            Assert.IsFalse(
                propertyNames.Any(name => name.Contains(bannedName, StringComparison.OrdinalIgnoreCase)),
                $"Memory index models must not expose raw private reasoning field '{bannedName}'.");
        }
    }

    private async Task SeedRunArtifactsAsync()
    {
        await CreateMemoryAsync("memory-indexed");
        await _influenceStore.RecordAsync(BuildScope(), BuildInfluenceDraft("influence-indexed", "memory-indexed"));
        await _handoffStore.CreateAsync(BuildScope(), BuildHandoffDraft("handoff-indexed", "tester-agent", ["memory-indexed"]));
        await _proposalService.CreateAsync(BuildProposalDraft("proposal-indexed", MemorySource("memory-indexed")));
        await _proposalService.AddEventAsync(BuildScope(), BuildProposalEvent("proposal-indexed", MemoryImprovementProposalEventType.AcceptedForFutureImplementation));
    }

    private async Task CreateProposalWithStatusAsync(string proposalId, MemoryImprovementProposalEventType status)
    {
        await _proposalService.CreateAsync(BuildProposalDraft(proposalId, MemorySource("memory-proposal-authority")));

        if (status != MemoryImprovementProposalEventType.Submitted)
            await _proposalService.AddEventAsync(BuildScope(), BuildProposalEvent(proposalId, status));
    }

    private async Task CreateMemoryAsync(string memoryItemId, AgentMemoryScope? scope = null) =>
        await _memoryStore.CreateAsync(BuildMemoryItem(memoryItemId, scope ?? BuildScope()));

    private async Task<int> CountIndexEventsAsync(MemoryIndexEventType eventType)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM agent.AgentMemoryIndexEvent WHERE EventType = @EventType;",
            new { EventType = (int)eventType });
    }

    private async Task<int> CountAcceptedOrSystemRuleMemoryAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.QuerySingleAsync<int>(
            """
            SELECT COUNT(*)
            FROM agent.AgentLocalMemoryItem
            WHERE AuthorityLevel IN (@Accepted, @SystemRule);
            """,
            new
            {
                Accepted = (int)MemoryAuthorityLevel.Accepted,
                SystemRule = (int)MemoryAuthorityLevel.SystemRule
            });
    }

    private async Task<MemoryAuthorityLevel> GetMemoryAuthorityAsync(string memoryItemId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var value = await connection.QuerySingleAsync<int>(
            "SELECT AuthorityLevel FROM agent.AgentLocalMemoryItem WHERE MemoryItemId = @MemoryItemId;",
            new { MemoryItemId = memoryItemId });
        return (MemoryAuthorityLevel)value;
    }

    private async Task AssertSqlFailsAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await Assert.ThrowsExactlyAsync<SqlException>(() => connection.ExecuteAsync(sql));
    }

    private static void AssertFileDoesNotContain(string relativePath, params string[] tokens)
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath));
        foreach (var token in tokens)
        {
            Assert.IsFalse(source.Contains(token, StringComparison.OrdinalIgnoreCase), $"{relativePath} must not contain {token}.");
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
    }

    private async Task DropAgentMemorySchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root for memory indexing boundary tests.");
    }

    private static AgentMemoryScope BuildScope(
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
            AgentId = agentId
        };

    private static AgentLocalMemoryItem BuildMemoryItem(string memoryItemId, AgentMemoryScope scope) =>
        new()
        {
            MemoryItemId = memoryItemId,
            Scope = scope,
            MemoryType = AgentMemoryType.Episodic,
            AuthorityLevel = MemoryAuthorityLevel.ObservedOnly,
            Title = "Observed build failure",
            Summary = "TesterAgent observed a reproducible build failure.",
            EvidenceRefs = [BuildEvidence($"evidence-{memoryItemId}")],
            Confidence = 0.8m,
            Status = MemoryLifecycleStatus.Active,
            CreatedAt = Now
        };

    private static MemoryInfluenceDraft BuildInfluenceDraft(string influenceId, string memoryItemId) =>
        new()
        {
            InfluenceId = influenceId,
            MemoryItemId = memoryItemId,
            DecisionId = $"decision-{influenceId}",
            InfluenceType = MemoryInfluenceType.ProposalCreated,
            InfluenceSummary = "Memory was used to justify creating a review proposal.",
            EvidenceRefs = [BuildEvidence($"evidence-{influenceId}")],
            Confidence = 0.7m,
            CreatedAt = Now.AddMinutes(5),
            ThoughtLedgerEntryId = $"thought-{influenceId}",
            CorrelationId = "correlation-1"
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
            EvidenceRefs = [BuildEvidence($"evidence-{handoffId}")],
            Confidence = 0.7m,
            CreatedAt = Now.AddMinutes(10),
            ThoughtLedgerEntryId = $"thought-{handoffId}",
            CorrelationId = "correlation-1",
            HandoffJson = "{}"
        };

    private static MemoryImprovementProposalDraft BuildProposalDraft(
        string proposalId,
        MemoryImprovementProposalSource source,
        AgentMemoryScope? scope = null)
    {
        var boundScope = scope ?? BuildScope();
        return new MemoryImprovementProposalDraft
        {
            ProposalId = proposalId,
            Scope = boundScope,
            ProposalType = MemoryImprovementProposalType.PromoteObservedMemory,
            Title = "Consider promoting observed memory",
            Summary = "The observed memory has enough evidence to deserve later human review.",
            Sources = [source],
            EvidenceRefs = [BuildEvidence($"evidence-{proposalId}") with { EvidenceType = EvidenceType.RunReport }],
            Confidence = 0.72m,
            CreatedAt = Now.AddMinutes(20),
            ProposedByAgentId = boundScope.AgentId,
            CorrelationId = "correlation-1",
            ThoughtLedgerEntryId = "thought-proposal",
            ProposalJson = "{}"
        };
    }

    private static MemoryImprovementProposalEventDraft BuildProposalEvent(
        string proposalId,
        MemoryImprovementProposalEventType eventType) =>
        new()
        {
            ProposalEventId = $"event-{proposalId}-{eventType}",
            ProposalId = proposalId,
            EventType = eventType,
            CreatedAt = Now.AddMinutes(30),
            Reason = $"Proposal reviewed as {eventType}.",
            CreatedByUserId = "human-reviewer",
            ThoughtLedgerEntryId = "thought-review",
            CorrelationId = "correlation-1",
            EventJson = "{}"
        };

    private static MemoryImprovementProposalSource MemorySource(string memoryItemId) =>
        new() { MemoryItemId = memoryItemId, ThoughtLedgerEntryId = "thought-memory" };

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

    private static MemoryIndexProjection BuildProjection(string indexRecordId, MemoryIndexArtifactType artifactType) =>
        new()
        {
            IndexRecordId = indexRecordId,
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RunId = "run-1",
            AgentId = "builder-agent",
            ArtifactType = artifactType,
            ArtifactId = indexRecordId.Replace("idx-", "artifact-", StringComparison.OrdinalIgnoreCase),
            AuthorityLevel = MemoryIndexAuthorityLevel.ObservedProjection,
            Title = "Safe memory index projection",
            Summary = "Projection summarizes governed memory evidence without raw reasoning.",
            EvidenceRefs = [BuildEvidence($"evidence-{indexRecordId}")],
            CreatedAt = Now,
            DecisionId = $"decision-{indexRecordId}",
            ThoughtLedgerEntryId = $"thought-{indexRecordId}",
            CorrelationId = "correlation-1",
            Metadata = new Dictionary<string, string> { ["projectionKind"] = artifactType.ToString() },
            SourceHashSha256 = new string('a', 64)
        };

    private static bool IsSha256(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length == 64 && value.All(Uri.IsHexDigit);

    private sealed class FakeWeaviateMemoryIndexer : IWeaviateMemoryIndexer
    {
        public List<MemoryIndexProjection> Indexed { get; } = [];

        public Func<MemoryIndexProjection, bool>? ShouldFail { get; set; }

        public Task<WeaviateMemoryIndexResult> IndexAsync(
            MemoryIndexProjection projection,
            CancellationToken cancellationToken = default)
        {
            Indexed.Add(projection);

            if (ShouldFail?.Invoke(projection) == true)
            {
                return Task.FromResult(new WeaviateMemoryIndexResult
                {
                    Success = false,
                    Error = "fake failure"
                });
            }

            return Task.FromResult(new WeaviateMemoryIndexResult
            {
                Success = true,
                WeaviateObjectId = $"weaviate-{projection.IndexRecordId}"
            });
        }
    }
}
