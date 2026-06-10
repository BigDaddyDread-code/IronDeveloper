using System.Data;
using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Core.AgentMemory.Collective;
using IronDev.Data;
using IronDev.Infrastructure.AgentMemory;
using Microsoft.Data.SqlClient;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class CollectiveMemoryPromotionTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropAgentSchemaObjectsAsync();
        await ApplyMigrationAsync("migrate_collective_memory.sql");
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        await DropAgentSchemaObjectsAsync();
        await base.TestCleanup();
    }

    [TestMethod]
    public async Task CollectiveMemoryMigration_CreatesTablesViewTriggersAndProcedures()
    {
        await using var connection = new SqlConnection(ConnectionString);

        var tableCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.tables
            WHERE schema_id = SCHEMA_ID('agent')
              AND name IN ('CollectiveMemoryItem', 'CollectiveMemoryEvent')
            """);
        var viewCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.views WHERE schema_id = SCHEMA_ID('agent') AND name = 'vwCollectiveMemoryCurrentState'");
        var triggerCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.triggers
            WHERE name IN
            (
                'TR_CollectiveMemoryItem_BlockUpdateDelete',
                'TR_CollectiveMemoryEvent_BlockUpdateDelete',
                'TR_CollectiveMemoryItem_ValidateInsert',
                'TR_CollectiveMemoryEvent_ValidateInsert'
            )
            """);
        var procedureCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.procedures
            WHERE schema_id = SCHEMA_ID('agent')
              AND name IN ('usp_CollectiveMemory_CreateFromManualPromotion', 'usp_CollectiveMemory_AddEvent')
            """);

        Assert.AreEqual(2, tableCount);
        Assert.AreEqual(1, viewCount);
        Assert.AreEqual(4, triggerCount);
        Assert.AreEqual(2, procedureCount);
    }

    [TestMethod]
    public async Task CollectiveMemoryPermissions_RuntimeCanExecuteProceduresButCannotMutateTables()
    {
        await DropAgentSchemaObjectsAsync();
        await ApplyFullMemoryMigrationsAsync();
        await EnsureRuntimeTestUserAsync();

        await using var connection = new SqlConnection(ConnectionString);

        await ExecuteAsRuntimeAsync(connection,
            """
            EXEC agent.usp_CollectiveMemory_CreateFromManualPromotion
                @CollectiveMemoryId = N'collective-runtime-1',
                @TenantId = N'tenant-1',
                @ProjectId = N'project-1',
                @KnowledgeDomainId = N'memory-governance',
                @ComponentId = N'collective-memory',
                @RepositoryId = N'IronDeveloper',
                @MemoryType = 2,
                @AuthorityLevel = 3,
                @Title = N'SQL remains governed memory authority',
                @Summary = N'Runtime role may execute the approved manual-promotion procedure only.',
                @SourcesJson = N'[{ "sourceType": 7, "sourceId": "human-decision-1" }]',
                @EvidenceRefsJson = N'[{ "evidenceId": "evidence-1", "evidenceType": 4, "sourceId": "human-decision-1" }]',
                @ContradictionsJson = N'[]',
                @SupersedesJson = N'[]',
                @Confidence = 0.8000,
                @CreatedAtUtc = '2026-06-10T00:00:00',
                @LastReviewedAtUtc = '2026-06-10T00:00:00',
                @DecisionId = N'decision-runtime-1',
                @CreatedEventId = N'collective-runtime-1-created',
                @DecisionEventId = N'collective-runtime-1-accepted',
                @DecisionEventType = 2,
                @Reason = N'Runtime role executes approved procedure.',
                @CreatedByUserId = N'human-reviewer-1';
            """);

        await ExecuteAsRuntimeAsync(connection,
            """
            EXEC agent.usp_CollectiveMemory_AddEvent
                @CollectiveMemoryEventId = N'collective-runtime-1-reviewed',
                @CollectiveMemoryId = N'collective-runtime-1',
                @TenantId = N'tenant-1',
                @ProjectId = N'project-1',
                @EventType = 8,
                @Reason = N'Review audit event.',
                @CreatedAtUtc = '2026-06-10T00:00:01',
                @CreatedByUserId = N'human-reviewer-1',
                @DecisionId = N'decision-runtime-review-1';
            """);

        await AssertSqlFailsAsync(connection, () => ExecuteAsRuntimeAsync(connection,
            """
            INSERT INTO agent.CollectiveMemoryItem
            (
                CollectiveMemoryId, TenantId, ProjectId, MemoryType, AuthorityLevel, Title, Summary,
                SourcesJson, EvidenceRefsJson, ContradictionsJson, SupersedesJson, Confidence, CreatedAtUtc
            )
            VALUES
            (
                N'direct-runtime-item', N'tenant-1', N'project-1', 2, 1, N'Direct insert', N'Direct insert is denied.',
                N'[]', N'[]', N'[]', N'[]', 0.5, SYSUTCDATETIME()
            );
            """));

        await AssertSqlFailsAsync(connection, () => ExecuteAsRuntimeAsync(connection,
            "UPDATE agent.CollectiveMemoryItem SET Summary = N'changed' WHERE CollectiveMemoryId = N'collective-runtime-1';"));
        await AssertSqlFailsAsync(connection, () => ExecuteAsRuntimeAsync(connection,
            "DELETE FROM agent.CollectiveMemoryItem WHERE CollectiveMemoryId = N'collective-runtime-1';"));
        await AssertSqlFailsAsync(connection, () => ExecuteAsRuntimeAsync(connection,
            """
            INSERT INTO agent.CollectiveMemoryEvent
            (
                CollectiveMemoryEventId, CollectiveMemoryId, EventType, Reason, CreatedAtUtc, CreatedByUserId
            )
            VALUES
            (
                N'direct-runtime-event', N'collective-runtime-1', 8, N'Direct event insert denied.', SYSUTCDATETIME(), N'human-reviewer-1'
            );
            """));
    }

    [TestMethod]
    public async Task CollectiveMemoryPermissions_RuntimeCannotCreateAcceptedCollectiveMemoryWithAgentOnlyActor()
    {
        await DropAgentSchemaObjectsAsync();
        await ApplyFullMemoryMigrationsAsync();
        await EnsureRuntimeTestUserAsync();

        await using var connection = new SqlConnection(ConnectionString);

        await AssertSqlFailsAsync(connection, () => ExecuteAsRuntimeAsync(connection,
            """
            EXEC agent.usp_CollectiveMemory_CreateFromManualPromotion
                @CollectiveMemoryId = N'collective-agent-only-accepted',
                @TenantId = N'tenant-1',
                @ProjectId = N'project-1',
                @KnowledgeDomainId = N'memory-governance',
                @ComponentId = N'collective-memory',
                @RepositoryId = N'IronDeveloper',
                @MemoryType = 2,
                @AuthorityLevel = 3,
                @Title = N'Agent-only accepted memory must fail',
                @Summary = N'Accepted CollectiveMemory cannot be created by an agent-only actor.',
                @SourcesJson = N'[{ "sourceType": 7, "sourceId": "human-decision-1" }]',
                @EvidenceRefsJson = N'[{ "evidenceId": "evidence-1", "evidenceType": 4, "sourceId": "human-decision-1" }]',
                @ContradictionsJson = N'[]',
                @SupersedesJson = N'[]',
                @Confidence = 0.8000,
                @CreatedAtUtc = '2026-06-10T00:00:00',
                @LastReviewedAtUtc = '2026-06-10T00:00:00',
                @DecisionId = N'decision-agent-only-1',
                @CreatedEventId = N'collective-agent-only-accepted-created',
                @DecisionEventId = N'collective-agent-only-accepted-event',
                @DecisionEventType = 2,
                @Reason = N'Agent-only accepted promotion must be denied.',
                @CreatedByUserId = NULL,
                @CreatedByAgentId = N'some-agent';
            """));

        var itemCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM agent.CollectiveMemoryItem WHERE CollectiveMemoryId = N'collective-agent-only-accepted'");
        var eventCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM agent.CollectiveMemoryEvent WHERE CollectiveMemoryId = N'collective-agent-only-accepted'");

        Assert.AreEqual(0, itemCount);
        Assert.AreEqual(0, eventCount);
    }

    [TestMethod]
    public async Task ManualAccept_ReadyAggregateCreatesAcceptedCollectiveMemoryWithAuditEvents()
    {
        var service = CreatePromotionService();
        var store = CreateStore();

        var result = await service.PromoteAsync(BuildRequest());
        var item = await store.GetAsync(BuildScope(), "collective-memory-1");
        var events = await store.GetEventsAsync(BuildScope(), "collective-memory-1");

        Assert.AreEqual(CollectiveMemoryPromotionOutcome.AcceptedCreated, result.Outcome);
        Assert.IsTrue(result.CreatedCollectiveMemory);
        Assert.AreEqual("collective-memory-1", result.CollectiveMemoryId);
        Assert.IsNotNull(item);
        Assert.AreEqual(CollectiveMemoryAuthorityLevel.Accepted, item!.AuthorityLevel);
        Assert.AreEqual(CollectiveMemoryStatus.Active, item.Status);
        Assert.AreEqual(CollectiveMemoryReviewState.ApprovedForAcceptance, item.ReviewState);
        Assert.AreEqual("decision-1", item.DecisionId);
        Assert.IsNotNull(item.LastReviewedAt);
        CollectionAssert.Contains(events.Select(e => e.EventType).ToArray(), CollectiveMemoryEventType.Created);
        CollectionAssert.Contains(events.Select(e => e.EventType).ToArray(), CollectiveMemoryEventType.Accepted);
    }

    [TestMethod]
    public async Task ManualReject_RecordsRejectedCollectiveMemoryWithoutActiveAuthority()
    {
        var service = CreatePromotionService();
        var store = CreateStore();

        var result = await service.PromoteAsync(BuildRequest(CollectiveMemoryPromotionDecision.Reject) with
        {
            Reason = "Reviewer rejected this candidate."
        });
        var item = await store.GetAsync(BuildScope(), "collective-memory-1");
        var events = await store.GetEventsAsync(BuildScope(), "collective-memory-1");

        Assert.AreEqual(CollectiveMemoryPromotionOutcome.RejectedRecorded, result.Outcome);
        Assert.IsTrue(result.CreatedCollectiveMemory);
        Assert.IsNotNull(item);
        Assert.AreEqual(CollectiveMemoryAuthorityLevel.Rejected, item!.AuthorityLevel);
        Assert.AreEqual(CollectiveMemoryStatus.Rejected, item.Status);
        Assert.AreNotEqual(CollectiveMemoryStatus.Active, item.Status);
        CollectionAssert.Contains(events.Select(e => e.EventType).ToArray(), CollectiveMemoryEventType.Rejected);
    }

    [TestMethod]
    public async Task ManualReject_WithoutReasonBlocks()
    {
        var result = await CreatePromotionService().PromoteAsync(BuildRequest(CollectiveMemoryPromotionDecision.Reject) with
        {
            Reason = null
        });

        AssertBlocked(result, "COLLECTIVE_PROMOTION_REASON_REQUIRED");
        Assert.AreEqual(0, await CountCollectiveMemoryItemsAsync());
    }

    [TestMethod]
    public async Task ManualAccept_BlocksWhenAggregationHasErrors()
    {
        var request = BuildRequest() with
        {
            AggregationResult = BuildAggregationResult(issues:
            [
                new CollectiveMemoryAggregationIssue
                {
                    Code = "AGG_ERROR",
                    Severity = "Error",
                    Message = "Aggregation failed."
                }
            ])
        };

        var result = await CreatePromotionService().PromoteAsync(request);

        AssertBlocked(result, "COLLECTIVE_PROMOTION_AGGREGATION_HAS_ERRORS");
        Assert.AreEqual(0, await CountCollectiveMemoryItemsAsync());
    }

    [TestMethod]
    public async Task ManualAccept_BlocksWhenAggregationIsNotReady()
    {
        var result = await CreatePromotionService().PromoteAsync(BuildRequest() with
        {
            AggregationResult = BuildAggregationResult(readiness: CollectiveMemoryEvidenceReadiness.NeedsMoreSources)
        });

        AssertBlocked(result, "COLLECTIVE_PROMOTION_NOT_READY_FOR_HUMAN_REVIEW");
        Assert.AreEqual(0, await CountCollectiveMemoryItemsAsync());
    }

    [TestMethod]
    public async Task ManualAccept_BlocksWhenAggregationCandidateIdMismatches()
    {
        var result = await CreatePromotionService().PromoteAsync(BuildRequest() with
        {
            AggregationResult = BuildAggregationResult(collectiveMemoryId: "different-collective-memory")
        });

        AssertBlocked(result, "COLLECTIVE_PROMOTION_AGGREGATION_CANDIDATE_MISMATCH");
        Assert.AreEqual(0, await CountCollectiveMemoryItemsAsync());
    }

    [TestMethod]
    public async Task ManualAccept_BlocksWithoutDecisionIdActorEvidenceOrValidCandidate()
    {
        var service = CreatePromotionService();

        var withoutDecision = await service.PromoteAsync(BuildRequest() with { DecisionId = string.Empty });
        var withoutActor = await service.PromoteAsync(BuildRequest() with { DecidedByUserId = null });
        var withoutEvidence = await service.PromoteAsync(BuildRequest() with { Candidate = BuildCandidate() with { EvidenceRefs = [] } });
        var invalidCandidate = await service.PromoteAsync(BuildRequest() with { Candidate = BuildCandidate() with { Title = string.Empty } });

        AssertBlocked(withoutDecision, "COLLECTIVE_PROMOTION_DECISION_ID_REQUIRED");
        AssertBlocked(withoutActor, "COLLECTIVE_PROMOTION_HUMAN_ACTOR_REQUIRED");
        AssertBlocked(withoutEvidence, CollectiveMemoryContractValidator.EvidenceRequired);
        AssertBlocked(invalidCandidate, CollectiveMemoryContractValidator.TitleRequired);
        Assert.AreEqual(0, await CountCollectiveMemoryItemsAsync());
    }

    [TestMethod]
    public async Task ManualAccept_BlocksRawPrivateReasoningMarker()
    {
        var result = await CreatePromotionService().PromoteAsync(BuildRequest() with
        {
            Candidate = BuildCandidate() with { Summary = "ChainOfThought: never persist this." }
        });

        AssertBlocked(result, CollectiveMemoryContractValidator.RawPrivateReasoningBlocked);
        Assert.AreEqual(0, await CountCollectiveMemoryItemsAsync());
    }

    [TestMethod]
    public async Task Defer_DoesNotCreateAcceptedCollectiveMemory()
    {
        var result = await CreatePromotionService().PromoteAsync(BuildRequest(CollectiveMemoryPromotionDecision.Defer) with
        {
            Reason = "Needs more review."
        });

        Assert.AreEqual(CollectiveMemoryPromotionOutcome.Deferred, result.Outcome);
        Assert.IsFalse(result.CreatedCollectiveMemory);
        Assert.AreEqual(0, await CountCollectiveMemoryItemsAsync());
    }

    [TestMethod]
    public async Task AggregationAlone_DoesNotCreateCollectiveMemory()
    {
        var aggregator = new CollectiveMemoryEvidenceAggregator();
        var result = aggregator.Aggregate(new CollectiveMemoryAggregationInput
        {
            AggregationId = "aggregation-alone",
            Candidate = BuildCandidate(),
            EvidenceContributions =
            [
                new CollectiveMemoryEvidenceContribution
                {
                    ContributionId = "support-1",
                    ContributionType = CollectiveMemoryEvidenceContributionType.SupportsClaim,
                    Source = BuildSource("source-1", CollectiveMemorySourceType.RunMemoryReport),
                    Evidence = BuildEvidence("evidence-1", "source-1")
                },
                new CollectiveMemoryEvidenceContribution
                {
                    ContributionId = "support-2",
                    ContributionType = CollectiveMemoryEvidenceContributionType.SupportsClaim,
                    Source = BuildSource("source-2", CollectiveMemorySourceType.MemoryExecutionAudit),
                    Evidence = BuildEvidence("evidence-2", "source-2")
                }
            ]
        });

        Assert.AreEqual(CollectiveMemoryEvidenceReadiness.ReadyForHumanReview, result.Aggregate.Readiness);
        Assert.AreEqual(0, await CountCollectiveMemoryItemsAsync());
    }

    [TestMethod]
    public async Task ProposalLocalMemoryAndIndexing_DoNotCreateCollectiveMemoryAutomatically()
    {
        await DropAgentSchemaObjectsAsync();
        await ApplyFullMemoryMigrationsAsync();

        await using var connection = new SqlConnection(ConnectionString);
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM agent.CollectiveMemoryItem");

        Assert.AreEqual(0, count);
        AssertRuntimeStaticBoundary();
    }

    [TestMethod]
    public async Task CollectiveMemoryTablesAreAppendOnly()
    {
        await CreatePromotionService().PromoteAsync(BuildRequest());
        await using var connection = new SqlConnection(ConnectionString);

        await AssertSqlFailsAsync(connection, () => connection.ExecuteAsync(
            "UPDATE agent.CollectiveMemoryItem SET Summary = N'changed' WHERE CollectiveMemoryId = N'collective-memory-1'"));
        await AssertSqlFailsAsync(connection, () => connection.ExecuteAsync(
            "DELETE FROM agent.CollectiveMemoryItem WHERE CollectiveMemoryId = N'collective-memory-1'"));
        await AssertSqlFailsAsync(connection, () => connection.ExecuteAsync(
            "UPDATE agent.CollectiveMemoryEvent SET Reason = N'changed' WHERE CollectiveMemoryId = N'collective-memory-1'"));
        await AssertSqlFailsAsync(connection, () => connection.ExecuteAsync(
            "DELETE FROM agent.CollectiveMemoryEvent WHERE CollectiveMemoryId = N'collective-memory-1'"));
    }

    [TestMethod]
    public void CollectiveMemoryReadStoreHasNoWriteOrRuntimeRetrievalMethods()
    {
        var methodNames = typeof(ICollectiveMemoryStore)
            .GetMethods()
            .Select(method => method.Name)
            .ToArray();

        CollectionAssert.AreEquivalent(new[] { "GetAsync", "QueryAsync", "GetEventsAsync" }, methodNames);
        CollectionAssert.DoesNotContain(methodNames, "CreateAsync");
        CollectionAssert.DoesNotContain(methodNames, "SaveAsync");
        CollectionAssert.DoesNotContain(methodNames, "UpdateAsync");
        CollectionAssert.DoesNotContain(methodNames, "PromoteAsync");
        CollectionAssert.DoesNotContain(methodNames, "RetrieveForAgentAsync");
        CollectionAssert.DoesNotContain(methodNames, "SearchForRuntimeAsync");
    }

    [TestMethod]
    public void CollectiveMemoryRuntimeBoundary_NoRetrievalWeaviateAttractorOrRuntimeUse()
    {
        var forbiddenTypeNames = new[]
        {
            "ICollectiveMemoryRetrievalService",
            "CollectiveMemoryRetrievalService",
            "WeaviateCollectiveMemory",
            "RetrievalBoost",
            "SqlCollectiveMemoryStabilityStore",
            "RuntimeCollectiveMemoryScorer"
        };

        var typeNames = new[]
            {
                typeof(CollectiveMemoryItem).Assembly,
                typeof(SqlCollectiveMemoryStore).Assembly
            }
            .SelectMany(assembly => assembly.GetTypes())
            .Select(type => type.Name)
            .ToArray();

        foreach (var forbidden in forbiddenTypeNames)
            CollectionAssert.DoesNotContain(typeNames, forbidden);

        AssertRuntimeStaticBoundary();
    }

    private SqlCollectiveMemoryPromotionService CreatePromotionService() =>
        new(new TestConnectionFactory(ConnectionString));

    private SqlCollectiveMemoryStore CreateStore() =>
        new(new TestConnectionFactory(ConnectionString));

    private static CollectiveMemoryPromotionRequest BuildRequest(
        CollectiveMemoryPromotionDecision decision = CollectiveMemoryPromotionDecision.Accept) =>
        new()
        {
            PromotionRequestId = $"promotion-{decision.ToString().ToLowerInvariant()}-1",
            Candidate = BuildCandidate(),
            AggregationResult = BuildAggregationResult(),
            Decision = decision,
            DecisionId = "decision-1",
            DecidedAt = Now,
            DecidedByUserId = "human-reviewer-1",
            Reason = decision == CollectiveMemoryPromotionDecision.Accept
                ? "Human reviewer accepted this governed collective-memory candidate."
                : "Human reviewer rejected this governed collective-memory candidate.",
            ThoughtLedgerEntryId = "thought-ledger-1",
            CorrelationId = "correlation-1"
        };

    private static CollectiveMemoryAggregationResult BuildAggregationResult(
        string collectiveMemoryId = "collective-memory-1",
        CollectiveMemoryEvidenceReadiness readiness = CollectiveMemoryEvidenceReadiness.ReadyForHumanReview,
        IReadOnlyList<CollectiveMemoryAggregationIssue>? issues = null) =>
        new()
        {
            Aggregate = new CollectiveMemoryEvidenceAggregate
            {
                AggregationId = "aggregation-1",
                CollectiveMemoryId = collectiveMemoryId,
                Scope = BuildScope(),
                SupportingEvidenceCount = 2,
                WeakSupportingEvidenceCount = 0,
                NeutralEvidenceCount = 0,
                ContradictingEvidenceCount = 0,
                WeakContradictingEvidenceCount = 0,
                UniqueSourceCount = 2,
                UniqueSourceTypeCount = 2,
                SupportWeight = 2.0m,
                ContradictionWeight = 0m,
                EvidenceQuality = CollectiveMemoryEvidenceQuality.Strong,
                EvidenceCoverage = CollectiveMemoryEvidenceCoverage.MultipleIndependentSourceTypes,
                ConflictLevel = CollectiveMemoryEvidenceConflictLevel.None,
                Readiness = readiness,
                AggregatedAt = Now,
                EvidenceContributionIds = ["support-1", "support-2"],
                ContradictionContributionIds = [],
                ReviewWarnings = ["Ready for human review only; this does not grant authority."]
            },
            Issues = issues ?? []
        };

    private static CollectiveMemoryItem BuildCandidate() =>
        new()
        {
            CollectiveMemoryId = "collective-memory-1",
            Scope = BuildScope(),
            MemoryType = CollectiveMemoryType.ArchitectureDecision,
            AuthorityLevel = CollectiveMemoryAuthorityLevel.Candidate,
            Status = CollectiveMemoryStatus.Proposed,
            ReviewState = CollectiveMemoryReviewState.NeedsHumanReview,
            Title = "SQL remains memory authority",
            Summary = "SQL remains the governed source of truth for memory authority.",
            Sources =
            [
                BuildSource("source-1", CollectiveMemorySourceType.RunMemoryReport),
                BuildSource("source-2", CollectiveMemorySourceType.MemoryExecutionAudit)
            ],
            EvidenceRefs =
            [
                BuildEvidence("evidence-1", "source-1"),
                BuildEvidence("evidence-2", "source-2")
            ],
            Confidence = 0.82m,
            CreatedAt = Now,
            CollectiveMemoryJson = "{\"claim\":\"SQL remains memory authority\"}"
        };

    private static CollectiveMemoryScope BuildScope() =>
        new()
        {
            TenantId = "tenant-1",
            ProjectId = "project-1",
            KnowledgeDomainId = "memory-governance",
            ComponentId = "collective-memory",
            RepositoryId = "IronDeveloper"
        };

    private static CollectiveMemorySourceRef BuildSource(
        string sourceId,
        CollectiveMemorySourceType sourceType) =>
        new()
        {
            SourceType = sourceType,
            SourceId = sourceId,
            TenantId = "tenant-1",
            ProjectId = "project-1",
            RunId = "run-1",
            AgentId = "builder-agent",
            DecisionId = "decision-source-1",
            EvidenceUri = $"memory://{sourceId}",
            ObservedAt = Now
        };

    private static CollectiveMemoryEvidenceRef BuildEvidence(string evidenceId, string sourceId) =>
        new()
        {
            EvidenceId = evidenceId,
            EvidenceType = EvidenceType.RunReport,
            SourceId = sourceId,
            Summary = "Governed memory evidence.",
            Weight = 0.8m,
            CapturedAt = Now
        };

    private async Task<int> CountCollectiveMemoryItemsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM agent.CollectiveMemoryItem");
    }

    private static void AssertBlocked(CollectiveMemoryPromotionResult result, string expectedCode)
    {
        Assert.AreEqual(CollectiveMemoryPromotionOutcome.Blocked, result.Outcome);
        Assert.IsFalse(result.CreatedCollectiveMemory);
        Assert.IsTrue(result.Issues.Any(issue => string.Equals(issue.Code, expectedCode, StringComparison.Ordinal)),
            $"Expected issue '{expectedCode}' but got: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
    }

    private static async Task AssertSqlFailsAsync(SqlConnection connection, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (SqlException)
        {
            return;
        }

        Assert.Fail("Expected SQL operation to fail.");
    }

    private async Task ApplyFullMemoryMigrationsAsync()
    {
        await ApplyMigrationAsync("migrate_agent_local_memory.sql");
        await ApplyMigrationAsync("migrate_agent_memory_influence.sql");
        await ApplyMigrationAsync("migrate_agent_memory_handoff.sql");
        await ApplyMigrationAsync("migrate_agent_memory_improvement_proposals.sql");
        await ApplyMigrationAsync("migrate_agent_memory_indexing.sql");
        await ApplyMigrationAsync("migrate_agent_memory_execution_audit.sql");
        await ApplyMigrationAsync("migrate_agent_memory_stored_procedures.sql");
        await ApplyMigrationAsync("migrate_collective_memory.sql");
        await ApplyMigrationAsync("migrate_agent_memory_permissions.sql");
    }

    private async Task ApplyMigrationAsync(string fileName)
    {
        await using var connection = new SqlConnection(ConnectionString);
        var script = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot, "Database", fileName));
        await connection.ExecuteAsync(script);
    }

    private async Task DropAgentSchemaObjectsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(
            """
            IF SCHEMA_ID('agent') IS NULL
                RETURN;

            DECLARE @sql NVARCHAR(MAX) = N'';

            SELECT @sql = @sql + N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(parent_object_id_schema.schema_id)) + N'.' + QUOTENAME(parent_object_id_schema.name) + N' DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';'
            FROM sys.foreign_keys fk
            INNER JOIN sys.objects parent_object_id_schema
                ON parent_object_id_schema.object_id = fk.parent_object_id
            WHERE SCHEMA_NAME(parent_object_id_schema.schema_id) = N'agent';

            EXEC sp_executesql @sql;

            SET @sql = N'';
            SELECT @sql = @sql + N'DROP VIEW agent.' + QUOTENAME(name) + N';'
            FROM sys.views
            WHERE schema_id = SCHEMA_ID(N'agent');
            EXEC sp_executesql @sql;

            SET @sql = N'';
            SELECT @sql = @sql + N'DROP PROCEDURE agent.' + QUOTENAME(name) + N';'
            FROM sys.procedures
            WHERE schema_id = SCHEMA_ID(N'agent');
            EXEC sp_executesql @sql;

            SET @sql = N'';
            SELECT @sql = @sql + N'DROP TABLE agent.' + QUOTENAME(name) + N';'
            FROM sys.tables
            WHERE schema_id = SCHEMA_ID(N'agent')
            ORDER BY name DESC;
            EXEC sp_executesql @sql;
            """);
    }

    private async Task EnsureRuntimeTestUserAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(
            """
            IF DATABASE_PRINCIPAL_ID(N'IronDevMemoryRuntimeTestUser') IS NULL
                CREATE USER IronDevMemoryRuntimeTestUser WITHOUT LOGIN;

            IF IS_ROLEMEMBER(N'IronDevMemoryRuntimeRole', N'IronDevMemoryRuntimeTestUser') = 0
                ALTER ROLE IronDevMemoryRuntimeRole ADD MEMBER IronDevMemoryRuntimeTestUser;
            """);
    }

    private static async Task ExecuteAsRuntimeAsync(SqlConnection connection, string sql)
    {
        await connection.ExecuteAsync($"""
            EXECUTE AS USER = N'IronDevMemoryRuntimeTestUser';
            BEGIN TRY
                {sql}
                REVERT;
            END TRY
            BEGIN CATCH
                REVERT;
                THROW;
            END CATCH
            """);
    }

    private static void AssertRuntimeStaticBoundary()
    {
        var forbiddenTokens = new[]
        {
            "ICollectiveMemoryRetrievalService",
            "CollectiveMemoryRetrievalService",
            "WeaviateCollectiveMemory",
            "RetrievalBoost",
            "SqlCollectiveMemoryStabilityStore",
            "migrate_collective_memory_stability",
            "usp_CollectiveMemoryStability",
            "RuntimeCollectiveMemoryScorer"
        };

        foreach (var file in EnumerateProductionFiles())
        {
            var text = File.ReadAllText(file);

            foreach (var token in forbiddenTokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal),
                    $"Forbidden runtime collective-memory token '{token}' was found in {file}.");
            }
        }

        foreach (var file in EnumerateRuntimeFiles())
        {
            var text = File.ReadAllText(file);
            Assert.IsFalse(text.Contains("CollectiveMemoryItem", StringComparison.Ordinal),
                $"Runtime service uses CollectiveMemoryItem outside promotion/store: {file}");
            Assert.IsFalse(text.Contains("ICollectiveMemoryStore", StringComparison.Ordinal),
                $"Runtime service uses collective memory store: {file}");
            Assert.IsFalse(text.Contains("ICollectiveMemoryPromotionService", StringComparison.Ordinal),
                $"Runtime service uses collective memory promotion service: {file}");
        }
    }

    private static IEnumerable<string> EnumerateProductionFiles()
    {
        foreach (var root in new[] { "IronDev.Core", "IronDev.Infrastructure", "IronDev.Api", "IronDev.Client", "tools" })
        {
            var path = Path.Combine(RepositoryRoot, root);
            if (!Directory.Exists(path))
                continue;

            foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(RepositoryRoot, file);
                if (relative.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                    relative.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static IEnumerable<string> EnumerateRuntimeFiles() =>
        EnumerateProductionFiles()
            .Where(file => !file.EndsWith(Path.Combine("IronDev.Core", "AgentMemory", "Collective", "CollectiveMemoryPromotionModels.cs"), StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.EndsWith(Path.Combine("IronDev.Infrastructure", "AgentMemory", "SqlCollectiveMemoryStore.cs"), StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.EndsWith(Path.Combine("IronDev.Infrastructure", "AgentMemory", "SqlCollectiveMemoryPromotionService.cs"), StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains(Path.Combine("IronDev.Core", "AgentMemory", "Collective"), StringComparison.OrdinalIgnoreCase));

    private static string FindRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;

        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "IronDev.slnx")))
                return current;

            var parent = Directory.GetParent(current);
            if (parent is null)
                break;

            current = parent.FullName;
        }

        throw new DirectoryNotFoundException("Could not find repository root from test base directory.");
    }

    private sealed class TestConnectionFactory : IDbConnectionFactory
    {
        private readonly string _connectionString;

        public TestConnectionFactory(string connectionString)
        {
            _connectionString = connectionString;
        }

        public IDbConnection CreateConnection() => new SqlConnection(_connectionString);
    }
}
