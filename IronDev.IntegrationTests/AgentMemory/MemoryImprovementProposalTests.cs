using System.Reflection;
using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Data;
using IronDev.Infrastructure.AgentMemory;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class MemoryImprovementProposalTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);

    private SqlAgentLocalMemoryStore _memoryStore = null!;
    private SqlAgentMemoryInfluenceStore _influenceStore = null!;
    private SqlAgentMemoryHandoffStore _handoffStore = null!;
    private SqlMemoryImprovementProposalService _proposalService = null!;

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
    public async Task MemoryImprovementProposalMigration_CreatesTablesIndexesAndTriggers()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var objectCount = await connection.QuerySingleAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.objects
            WHERE object_id IN
            (
                OBJECT_ID('agent.AgentMemoryImprovementProposal', 'U'),
                OBJECT_ID('agent.AgentMemoryImprovementProposalEvent', 'U'),
                OBJECT_ID('agent.TR_AgentMemoryImprovementProposal_BlockUpdateDelete', 'TR'),
                OBJECT_ID('agent.TR_AgentMemoryImprovementProposalEvent_BlockUpdateDelete', 'TR'),
                OBJECT_ID('agent.TR_AgentMemoryImprovementProposal_ValidateSources', 'TR'),
                OBJECT_ID('agent.TR_AgentMemoryImprovementProposalEvent_ValidateInsert', 'TR')
            );
            """);

        var indexCount = await connection.QuerySingleAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.indexes
            WHERE name IN
            (
                'IX_AgentMemoryImprovementProposal_ScopeCreated',
                'IX_AgentMemoryImprovementProposal_TypeCreated',
                'IX_AgentMemoryImprovementProposalEvent_ProposalCreated'
            );
            """);

        Assert.AreEqual(6, objectCount);
        Assert.AreEqual(3, indexCount);
    }

    [TestMethod]
    public void MemoryImprovementProposalService_DoesNotOwnSchemaCreationOrWeaviate()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "IronDev.Infrastructure",
            "AgentMemory",
            "SqlMemoryImprovementProposalService.cs"));

        Assert.IsFalse(source.Contains("CREATE TABLE", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("CREATE TRIGGER", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("Weaviate", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task MemoryImprovementProposal_TablesAreAppendOnly()
    {
        await CreateMemoryAsync("memory-append-only");
        await _proposalService.CreateAsync(BuildProposalDraft("proposal-append-only", MemorySource("memory-append-only")));

        await AssertSqlFailsAsync("UPDATE agent.AgentMemoryImprovementProposal SET Title = 'mutated' WHERE ProposalId = 'proposal-append-only';");
        await AssertSqlFailsAsync("DELETE FROM agent.AgentMemoryImprovementProposal WHERE ProposalId = 'proposal-append-only';");
        await AssertSqlFailsAsync("UPDATE agent.AgentMemoryImprovementProposalEvent SET Reason = 'mutated' WHERE ProposalId = 'proposal-append-only';");
        await AssertSqlFailsAsync("DELETE FROM agent.AgentMemoryImprovementProposalEvent WHERE ProposalId = 'proposal-append-only';");
    }

    [TestMethod]
    public async Task MemoryImprovementProposal_CanCreateFromScopedMemoryInfluenceAndHandoffSources()
    {
        await CreateMemoryAsync("memory-source");
        await _influenceStore.RecordAsync(BuildScope(), BuildInfluenceDraft("influence-source", "memory-source"));
        await _handoffStore.CreateAsync(BuildScope(), BuildHandoffDraft("handoff-outgoing", "tester-agent", ["memory-source"]));

        var testerScope = BuildScope(agentId: "tester-agent");
        await _proposalService.CreateAsync(BuildProposalDraft("proposal-memory", MemorySource("memory-source")) with
        {
            ThoughtLedgerEntryId = "thought-proposal",
            CorrelationId = "correlation-proposal"
        });
        await _proposalService.CreateAsync(BuildProposalDraft("proposal-influence", InfluenceSource("influence-source")));
        await _proposalService.CreateAsync(BuildProposalDraft("proposal-outgoing-handoff", HandoffSource("handoff-outgoing")));
        await _proposalService.CreateAsync(BuildProposalDraft("proposal-incoming-handoff", HandoffSource("handoff-outgoing"), testerScope) with
        {
            ProposedByAgentId = "tester-agent"
        });
        await _proposalService.CreateAsync(BuildProposalDraft("proposal-run-finding", RunFindingSource(RunMemoryFindingType.CandidatePatternMemory)));

        var proposals = await _proposalService.QueryAsync("tenant-1", "project-1", "campaign-1", "run-1", new MemoryImprovementProposalQuery { Take = 10 });
        var memoryProposal = proposals.Single(item => item.ProposalId == "proposal-memory");
        var events = await _proposalService.GetEventsAsync(BuildScope(), "proposal-memory");

        Assert.AreEqual(MemoryImprovementProposalStatus.Submitted, memoryProposal.CurrentStatus);
        Assert.AreEqual("thought-proposal", memoryProposal.ThoughtLedgerEntryId);
        Assert.AreEqual("correlation-proposal", memoryProposal.CorrelationId);
        Assert.HasCount(1, memoryProposal.EvidenceRefs);
        Assert.HasCount(1, memoryProposal.Sources);
        Assert.HasCount(1, events);
        Assert.AreEqual(MemoryImprovementProposalEventType.Submitted, events[0].EventType);
        Assert.IsTrue(proposals.Any(item => item.ProposalId == "proposal-incoming-handoff"));
        Assert.IsTrue(proposals.Any(item => item.ProposalId == "proposal-run-finding"));
    }

    [DataTestMethod]
    [DataRow("critic-agent", "tenant-1", "project-1", "campaign-1", "run-1")]
    [DataRow("builder-agent", "tenant-2", "project-1", "campaign-1", "run-1")]
    [DataRow("builder-agent", "tenant-1", "project-2", "campaign-1", "run-1")]
    [DataRow("builder-agent", "tenant-1", "project-1", "campaign-2", "run-1")]
    [DataRow("builder-agent", "tenant-1", "project-1", "campaign-1", "run-2")]
    public async Task MemoryImprovementProposal_CannotCreateFromMemoryOutsideExactScope(
        string ownerAgentId,
        string tenantId,
        string projectId,
        string campaignId,
        string runId)
    {
        var foreignScope = BuildScope(tenantId, projectId, campaignId, runId, ownerAgentId);
        await CreateMemoryAsync("memory-foreign", foreignScope);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _proposalService.CreateAsync(BuildProposalDraft($"proposal-foreign-{ownerAgentId}-{tenantId}-{projectId}-{campaignId}-{runId}", MemorySource("memory-foreign"))));
    }

    [TestMethod]
    public async Task MemoryImprovementProposal_CannotCreateFromForeignInfluenceOrUnrelatedHandoff()
    {
        var criticScope = BuildScope(agentId: "critic-agent");
        await CreateMemoryAsync("memory-critic", criticScope);
        await _influenceStore.RecordAsync(criticScope, BuildInfluenceDraft("influence-critic", "memory-critic"));

        await CreateMemoryAsync("memory-builder");
        await _handoffStore.CreateAsync(BuildScope(), BuildHandoffDraft("handoff-builder-tester", "tester-agent", ["memory-builder"]));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _proposalService.CreateAsync(BuildProposalDraft("proposal-foreign-influence", InfluenceSource("influence-critic"))));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _proposalService.CreateAsync(BuildProposalDraft("proposal-unrelated-handoff", HandoffSource("handoff-builder-tester"), criticScope) with
            {
                ProposedByAgentId = "critic-agent"
            }));
    }

    [TestMethod]
    public async Task MemoryImprovementProposal_DirectSqlSourceValidationBlocksForeignMemory()
    {
        await CreateMemoryAsync("memory-direct-scope");

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await Assert.ThrowsExactlyAsync<SqlException>(() => connection.ExecuteAsync(
            """
            INSERT INTO agent.AgentMemoryImprovementProposal
            (
                ProposalId,
                TenantId,
                ProjectId,
                CampaignId,
                RunId,
                AgentId,
                ProposalType,
                Title,
                Summary,
                SourcesJson,
                EvidenceRefsJson,
                Confidence,
                ProposedByUserId,
                CreatedAtUtc
            )
            VALUES
            (
                'proposal-direct-foreign-memory',
                'tenant-1',
                'project-1',
                'campaign-1',
                'run-1',
                'critic-agent',
                1,
                'Direct proposal',
                'Direct SQL tried to reference another agent memory item.',
                '[{"memoryItemId":"memory-direct-scope"}]',
                '[{"evidenceId":"evidence-direct","evidenceType":10,"sourceId":"run-report"}]',
                0.7000,
                'human-reviewer',
                @CreatedAtUtc
            );
            """,
            new { CreatedAtUtc = Now.UtcDateTime }));
    }

    [TestMethod]
    public async Task MemoryImprovementProposal_LifecycleAllowsTerminalEventsAndBlocksFurtherEvents()
    {
        await CreateMemoryAsync("memory-lifecycle");

        var terminalEvents = new[]
        {
            MemoryImprovementProposalEventType.Withdrawn,
            MemoryImprovementProposalEventType.Rejected,
            MemoryImprovementProposalEventType.AcceptedForFutureImplementation,
            MemoryImprovementProposalEventType.Superseded
        };

        foreach (var terminalEvent in terminalEvents)
        {
            var proposalId = $"proposal-{terminalEvent}";
            await _proposalService.CreateAsync(BuildProposalDraft(proposalId, MemorySource("memory-lifecycle")));
            await _proposalService.AddEventAsync(BuildScope(), BuildProposalEvent(proposalId, terminalEvent));

            var proposal = (await _proposalService.QueryAsync("tenant-1", "project-1", "campaign-1", "run-1", new MemoryImprovementProposalQuery
            {
                Status = (MemoryImprovementProposalStatus)terminalEvent,
                Take = 20
            })).Single(item => item.ProposalId == proposalId);
            var events = await _proposalService.GetEventsAsync(BuildScope(), proposalId);

            Assert.AreEqual((MemoryImprovementProposalStatus)terminalEvent, proposal.CurrentStatus);
            Assert.HasCount(2, events);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                _proposalService.AddEventAsync(BuildScope(), BuildProposalEvent(proposalId, MemoryImprovementProposalEventType.Rejected, "event-after-terminal")));
        }
    }

    [TestMethod]
    public async Task MemoryImprovementProposal_CannotAddSecondSubmittedEvent()
    {
        await CreateMemoryAsync("memory-second-submitted");
        await _proposalService.CreateAsync(BuildProposalDraft("proposal-second-submitted", MemorySource("memory-second-submitted")));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _proposalService.AddEventAsync(BuildScope(), BuildProposalEvent("proposal-second-submitted", MemoryImprovementProposalEventType.Submitted)));

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await Assert.ThrowsExactlyAsync<SqlException>(() => connection.ExecuteAsync(
            """
            INSERT INTO agent.AgentMemoryImprovementProposalEvent
            (
                ProposalEventId,
                ProposalId,
                EventType,
                Reason,
                CreatedAtUtc,
                CreatedByUserId
            )
            VALUES
            (
                'proposal-event-second-submitted-direct',
                'proposal-second-submitted',
                1,
                'Direct second submitted event.',
                @CreatedAtUtc,
                'human-reviewer'
            );
            """,
            new { CreatedAtUtc = Now.AddMinutes(1).UtcDateTime }));
    }

    [TestMethod]
    public async Task MemoryImprovementProposal_DoesNotPromoteOrMutateSourceMemory()
    {
        await CreateMemoryAsync("memory-no-promotion");
        var beforeEvents = await CountMemoryEventsAsync("memory-no-promotion");

        await _proposalService.CreateAsync(BuildProposalDraft("proposal-no-promotion", MemorySource("memory-no-promotion")));
        await _proposalService.AddEventAsync(BuildScope(), BuildProposalEvent(
            "proposal-no-promotion",
            MemoryImprovementProposalEventType.AcceptedForFutureImplementation));

        var memory = await _memoryStore.GetOwnMemoryItemAsync(BuildScope(), "memory-no-promotion");
        var afterEvents = await CountMemoryEventsAsync("memory-no-promotion");
        var acceptedOrSystemRuleCount = await CountAcceptedOrSystemRuleMemoryAsync();

        Assert.IsNotNull(memory);
        Assert.AreEqual(MemoryAuthorityLevel.ObservedOnly, memory.AuthorityLevel);
        Assert.AreEqual(MemoryLifecycleStatus.Active, memory.Status);
        Assert.AreEqual(beforeEvents, afterEvents);
        Assert.AreEqual(0, acceptedOrSystemRuleCount);
    }

    [TestMethod]
    public async Task MemoryImprovementProposal_RejectedEventDoesNotMutateSourceMemory()
    {
        await CreateMemoryAsync("memory-rejected-no-mutation");
        await _proposalService.CreateAsync(BuildProposalDraft("proposal-rejected-no-mutation", MemorySource("memory-rejected-no-mutation")));

        await _proposalService.AddEventAsync(BuildScope(), BuildProposalEvent(
            "proposal-rejected-no-mutation",
            MemoryImprovementProposalEventType.Rejected));

        var memory = await _memoryStore.GetOwnMemoryItemAsync(BuildScope(), "memory-rejected-no-mutation");

        Assert.IsNotNull(memory);
        Assert.AreEqual(MemoryAuthorityLevel.ObservedOnly, memory.AuthorityLevel);
        Assert.AreEqual(MemoryLifecycleStatus.Active, memory.Status);
    }

    [TestMethod]
    public async Task MemoryImprovementProposal_QueryIsScopedAndFilterable()
    {
        await CreateMemoryAsync("memory-query");
        await CreateMemoryAsync("memory-query-other-agent", BuildScope(agentId: "critic-agent"));
        await CreateMemoryAsync("memory-query-other-run", BuildScope(runId: "run-2"));

        await _proposalService.CreateAsync(BuildProposalDraft("proposal-query-1", MemorySource("memory-query")));
        await _proposalService.CreateAsync(BuildProposalDraft("proposal-query-2", MemorySource("memory-query")) with
        {
            ProposalType = MemoryImprovementProposalType.MarkMemoryInvalid
        });
        await _proposalService.CreateAsync(BuildProposalDraft("proposal-query-critic", MemorySource("memory-query-other-agent"), BuildScope(agentId: "critic-agent")) with
        {
            ProposedByAgentId = "critic-agent"
        });
        await _proposalService.CreateAsync(BuildProposalDraft("proposal-query-other-run", MemorySource("memory-query-other-run"), BuildScope(runId: "run-2")));
        await _proposalService.AddEventAsync(BuildScope(), BuildProposalEvent("proposal-query-2", MemoryImprovementProposalEventType.Rejected));

        var runProposals = await _proposalService.QueryAsync("tenant-1", "project-1", "campaign-1", "run-1", new MemoryImprovementProposalQuery { Take = 0 });
        var rejected = await _proposalService.QueryAsync("tenant-1", "project-1", "campaign-1", "run-1", new MemoryImprovementProposalQuery { Status = MemoryImprovementProposalStatus.Rejected });
        var markInvalid = await _proposalService.QueryAsync("tenant-1", "project-1", "campaign-1", "run-1", new MemoryImprovementProposalQuery { ProposalType = MemoryImprovementProposalType.MarkMemoryInvalid });
        var critic = await _proposalService.QueryAsync("tenant-1", "project-1", "campaign-1", "run-1", new MemoryImprovementProposalQuery { AgentId = "critic-agent" });
        var takeOne = await _proposalService.QueryAsync("tenant-1", "project-1", "campaign-1", "run-1", new MemoryImprovementProposalQuery { Take = 1 });

        Assert.IsTrue(runProposals.All(item => item.Scope.RunId == "run-1"));
        Assert.IsFalse(runProposals.Any(item => item.ProposalId == "proposal-query-other-run"));
        Assert.HasCount(1, rejected);
        Assert.AreEqual("proposal-query-2", rejected[0].ProposalId);
        Assert.HasCount(1, markInvalid);
        Assert.AreEqual("proposal-query-2", markInvalid[0].ProposalId);
        Assert.HasCount(1, critic);
        Assert.AreEqual("proposal-query-critic", critic[0].ProposalId);
        Assert.HasCount(1, takeOne);
        Assert.IsEmpty(await _proposalService.QueryAsync("tenant-2", "project-1", "campaign-1", "run-1", new MemoryImprovementProposalQuery()));
        Assert.IsEmpty(await _proposalService.QueryAsync("tenant-1", "project-2", "campaign-1", "run-1", new MemoryImprovementProposalQuery()));
        Assert.IsEmpty(await _proposalService.QueryAsync("tenant-1", "project-1", "campaign-2", "run-1", new MemoryImprovementProposalQuery()));
        Assert.IsEmpty(await _proposalService.QueryAsync("tenant-1", "project-1", "campaign-1", "run-3", new MemoryImprovementProposalQuery()));
    }

    [TestMethod]
    public async Task MemoryImprovementProposal_InvalidDraftsAreRejected()
    {
        await CreateMemoryAsync("memory-validation");

        var invalidDrafts = new[]
        {
            BuildProposalDraft("", MemorySource("memory-validation")),
            BuildProposalDraft("proposal-missing-title", MemorySource("memory-validation")) with { Title = "" },
            BuildProposalDraft("proposal-missing-summary", MemorySource("memory-validation")) with { Summary = "" },
            BuildProposalDraft("proposal-missing-source", MemorySource("memory-validation")) with { Sources = [] },
            BuildProposalDraft("proposal-blank-source", MemorySource("memory-validation")) with { Sources = [new MemoryImprovementProposalSource()] },
            BuildProposalDraft("proposal-missing-evidence", MemorySource("memory-validation")) with { EvidenceRefs = [] },
            BuildProposalDraft("proposal-missing-evidence-id", MemorySource("memory-validation")) with { EvidenceRefs = [BuildEvidence("")] },
            BuildProposalDraft("proposal-bad-evidence-type", MemorySource("memory-validation")) with { EvidenceRefs = [BuildEvidence("bad-type") with { EvidenceType = (EvidenceType)999 }] },
            BuildProposalDraft("proposal-missing-evidence-source", MemorySource("memory-validation")) with { EvidenceRefs = [BuildEvidence("missing-source") with { SourceId = "" }] },
            BuildProposalDraft("proposal-low-confidence", MemorySource("memory-validation")) with { Confidence = -0.1m },
            BuildProposalDraft("proposal-high-confidence", MemorySource("memory-validation")) with { Confidence = 1.1m },
            BuildProposalDraft("proposal-bad-type", MemorySource("memory-validation")) with { ProposalType = (MemoryImprovementProposalType)999 },
            BuildProposalDraft("proposal-missing-proposer", MemorySource("memory-validation")) with { ProposedByAgentId = null, ProposedByUserId = null },
            BuildProposalDraft("proposal-wrong-agent", MemorySource("memory-validation")) with { ProposedByAgentId = "critic-agent" },
            BuildProposalDraft("proposal-bad-finding", RunFindingSource(RunMemoryFindingType.CandidatePatternMemory)) with { Sources = [new MemoryImprovementProposalSource { RunMemoryFindingType = "UnknownFinding" }] },
            BuildProposalDraft("proposal-raw-reasoning", MemorySource("memory-validation")) with { ProposalJson = "{\"rawPrompt\":\"do not store\"}" }
        };

        foreach (var draft in invalidDrafts)
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _proposalService.CreateAsync(draft));
        }
    }

    [TestMethod]
    public async Task MemoryImprovementProposal_InvalidEventDraftsAreRejected()
    {
        await CreateMemoryAsync("memory-event-validation");
        await _proposalService.CreateAsync(BuildProposalDraft("proposal-event-validation", MemorySource("memory-event-validation")));

        var invalidEvents = new[]
        {
            BuildProposalEvent("proposal-event-validation", MemoryImprovementProposalEventType.Rejected) with { ProposalEventId = "" },
            BuildProposalEvent("", MemoryImprovementProposalEventType.Rejected),
            BuildProposalEvent("proposal-event-validation", (MemoryImprovementProposalEventType)999),
            BuildProposalEvent("proposal-event-validation", MemoryImprovementProposalEventType.Rejected) with { CreatedByUserId = null, CreatedByAgentId = null },
            BuildProposalEvent("proposal-event-validation", MemoryImprovementProposalEventType.Rejected) with { EventJson = "{\"scratchpad\":\"private\"}" }
        };

        foreach (var invalidEvent in invalidEvents)
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _proposalService.AddEventAsync(BuildScope(), invalidEvent));
        }
    }

    [TestMethod]
    public void MemoryImprovementProposal_ServiceIsNotExposedThroughAgentMemorySiloAndModelsExcludeRawReasoning()
    {
        var siloMethods = typeof(IAgentMemorySilo).GetMethods(BindingFlags.Instance | BindingFlags.Public);
        Assert.IsFalse(siloMethods.Any(method =>
            method.Name.Contains("Proposal", StringComparison.OrdinalIgnoreCase) ||
            method.ReturnType == typeof(MemoryImprovementProposalRecord) ||
            method.ReturnType == typeof(IMemoryImprovementProposalService)));

        var bannedNames = new[]
        {
            "RawPrompt",
            "RawCompletion",
            "ChainOfThought",
            "Scratchpad",
            "PrivateReasoning"
        };

        var proposalTypes = new[]
        {
            typeof(MemoryImprovementProposalDraft),
            typeof(MemoryImprovementProposalRecord),
            typeof(MemoryImprovementProposalEventDraft),
            typeof(MemoryImprovementProposalEventRecord),
            typeof(MemoryImprovementProposalSource)
        };

        foreach (var proposalType in proposalTypes)
        {
            var propertyNames = proposalType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => property.Name)
                .ToArray();

            foreach (var bannedName in bannedNames)
            {
                Assert.IsFalse(
                    propertyNames.Any(name => name.Contains(bannedName, StringComparison.OrdinalIgnoreCase)),
                    $"Memory improvement proposal model {proposalType.Name} must not expose raw private reasoning field '{bannedName}'.");
            }
        }
    }

    private async Task CreateMemoryAsync(string memoryItemId, AgentMemoryScope? scope = null)
    {
        await _memoryStore.CreateAsync(BuildMemoryItem(memoryItemId, scope ?? BuildScope()));
    }

    private async Task<int> CountMemoryEventsAsync(string memoryItemId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM agent.AgentLocalMemoryEvent WHERE MemoryItemId = @MemoryItemId;",
            new { MemoryItemId = memoryItemId });
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
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_memory_improvement_proposals.sql")));
    }

    private async Task DropAgentMemorySchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
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

        throw new InvalidOperationException("Could not locate repository root for memory improvement proposal tests.");
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
        MemoryImprovementProposalEventType eventType,
        string? eventId = null) =>
        new()
        {
            ProposalEventId = eventId ?? $"event-{proposalId}-{eventType}",
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

    private static MemoryImprovementProposalSource InfluenceSource(string influenceId) =>
        new() { InfluenceId = influenceId, DecisionId = "decision-influence" };

    private static MemoryImprovementProposalSource HandoffSource(string handoffId) =>
        new() { HandoffMemorySliceId = handoffId, ThoughtLedgerEntryId = "thought-handoff" };

    private static MemoryImprovementProposalSource RunFindingSource(RunMemoryFindingType findingType) =>
        new() { RunMemoryFindingType = findingType.ToString(), ThoughtLedgerEntryId = "thought-finding" };

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
