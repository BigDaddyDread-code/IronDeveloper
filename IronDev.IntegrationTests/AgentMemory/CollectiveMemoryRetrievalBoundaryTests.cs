using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Core.AgentMemory.Collective;
using IronDev.Data;
using IronDev.Infrastructure.AgentMemory;
using Microsoft.Data.SqlClient;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class CollectiveMemoryRetrievalBoundaryTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
    public void CollectiveMemoryRetrievalContractTypesExist()
    {
        Assert.IsNotNull(typeof(CollectiveMemoryRetrievalQuery));
        Assert.IsNotNull(typeof(CollectiveMemoryRetrievalMatch));
        Assert.IsNotNull(typeof(CollectiveMemoryRetrievalResult));
        Assert.IsNotNull(typeof(CollectiveMemoryRetrievalIssue));
        Assert.IsNotNull(typeof(CollectiveMemoryRetrievalMode));
        Assert.IsNotNull(typeof(CollectiveMemoryRetrievalAuthorityFilter));
        Assert.IsNotNull(typeof(ICollectiveMemoryRetrievalService));
        Assert.IsNotNull(typeof(SqlCollectiveMemoryRetrievalService));
    }

    [TestMethod]
    public async Task RetrievesAcceptedCollectiveMemoryByText()
    {
        await InsertMemoryAsync("accepted-sql-authority", title: "SQL governed memory authority", authority: CollectiveMemoryAuthorityLevel.Accepted, eventType: CollectiveMemoryEventType.Accepted);

        var result = await CreateService().RetrieveAsync(BuildQuery(text: "SQL governed"));

        Assert.AreEqual(1, result.Matches.Count);
        Assert.AreEqual("accepted-sql-authority", result.Matches[0].Memory.CollectiveMemoryId);
    }

    [TestMethod]
    public async Task RetrievesReviewedCollectiveMemoryWhenFilterAllowsReviewed()
    {
        await InsertMemoryAsync("reviewed-memory", title: "Reviewed runbook", authority: CollectiveMemoryAuthorityLevel.Reviewed, eventType: CollectiveMemoryEventType.Reviewed);

        var result = await CreateService().RetrieveAsync(BuildQuery(text: "Reviewed"));

        Assert.AreEqual(1, result.Matches.Count);
        Assert.AreEqual(CollectiveMemoryAuthorityLevel.Reviewed, result.Matches[0].Memory.AuthorityLevel);
    }

    [TestMethod]
    public async Task DefaultFilterExcludesCandidatesRejectedDeprecatedAndExpired()
    {
        await InsertMemoryAsync("candidate-memory", title: "Candidate memory", authority: CollectiveMemoryAuthorityLevel.Candidate);
        await InsertMemoryAsync("rejected-memory", title: "Rejected memory", authority: CollectiveMemoryAuthorityLevel.Rejected, eventType: CollectiveMemoryEventType.Rejected);
        await InsertMemoryAsync("deprecated-memory", title: "Deprecated memory", authority: CollectiveMemoryAuthorityLevel.Deprecated, eventType: CollectiveMemoryEventType.Deprecated);
        await InsertMemoryAsync("expired-memory", title: "Expired memory", authority: CollectiveMemoryAuthorityLevel.Accepted, eventType: CollectiveMemoryEventType.Accepted, expiresAt: Now.AddDays(-1));
        await InsertMemoryAsync("accepted-memory", title: "Accepted memory", authority: CollectiveMemoryAuthorityLevel.Accepted, eventType: CollectiveMemoryEventType.Accepted);

        var result = await CreateService().RetrieveAsync(BuildQuery(text: "memory"));

        CollectionAssert.AreEquivalent(
            new[] { "accepted-memory" },
            result.Matches.Select(match => match.Memory.CollectiveMemoryId).ToArray());
    }

    [TestMethod]
    public async Task IncludeCandidatesIncludesCandidateMemory()
    {
        await InsertMemoryAsync("candidate-memory", title: "Candidate retrieval memory", authority: CollectiveMemoryAuthorityLevel.Candidate);

        var result = await CreateService().RetrieveAsync(BuildQuery(
            text: "Candidate retrieval",
            authorityFilter: CollectiveMemoryRetrievalAuthorityFilter.IncludeCandidates));

        Assert.AreEqual(1, result.Matches.Count);
        Assert.AreEqual(CollectiveMemoryAuthorityLevel.Candidate, result.Matches[0].Memory.AuthorityLevel);
    }

    [TestMethod]
    public async Task IncludeRejectedAndDeprecatedIncludesRejectedAndDeprecatedMemory()
    {
        await InsertMemoryAsync("rejected-memory", title: "Rejected retrieval memory", authority: CollectiveMemoryAuthorityLevel.Rejected, eventType: CollectiveMemoryEventType.Rejected);
        await InsertMemoryAsync("deprecated-memory", title: "Deprecated retrieval memory", authority: CollectiveMemoryAuthorityLevel.Deprecated, eventType: CollectiveMemoryEventType.Deprecated);

        var result = await CreateService().RetrieveAsync(BuildQuery(
            text: "retrieval memory",
            authorityFilter: CollectiveMemoryRetrievalAuthorityFilter.IncludeRejectedAndDeprecated));

        CollectionAssert.AreEquivalent(
            new[] { "deprecated-memory", "rejected-memory" },
            result.Matches.Select(match => match.Memory.CollectiveMemoryId).Order(StringComparer.Ordinal).ToArray());
    }

    [TestMethod]
    public async Task ScopeFilterPreventsCrossTenantAndCrossProjectLeakage()
    {
        await InsertMemoryAsync("tenant-one-memory", title: "Scoped memory", tenantId: "tenant-1", projectId: "project-1", authority: CollectiveMemoryAuthorityLevel.Accepted, eventType: CollectiveMemoryEventType.Accepted);
        await InsertMemoryAsync("tenant-two-memory", title: "Scoped memory", tenantId: "tenant-2", projectId: "project-1", authority: CollectiveMemoryAuthorityLevel.Accepted, eventType: CollectiveMemoryEventType.Accepted);
        await InsertMemoryAsync("project-two-memory", title: "Scoped memory", tenantId: "tenant-1", projectId: "project-2", authority: CollectiveMemoryAuthorityLevel.Accepted, eventType: CollectiveMemoryEventType.Accepted);

        var result = await CreateService().RetrieveAsync(BuildQuery(text: "Scoped"));

        Assert.AreEqual(1, result.Matches.Count);
        Assert.AreEqual("tenant-one-memory", result.Matches[0].Memory.CollectiveMemoryId);
    }

    [TestMethod]
    public async Task OptionalScopeFiltersWork()
    {
        await InsertMemoryAsync("domain-match", title: "Optional scope memory", knowledgeDomainId: "domain-1", componentId: "component-1", repositoryId: "repo-1", authority: CollectiveMemoryAuthorityLevel.Accepted, eventType: CollectiveMemoryEventType.Accepted);
        await InsertMemoryAsync("domain-miss", title: "Optional scope memory", knowledgeDomainId: "domain-2", componentId: "component-1", repositoryId: "repo-1", authority: CollectiveMemoryAuthorityLevel.Accepted, eventType: CollectiveMemoryEventType.Accepted);
        await InsertMemoryAsync("component-miss", title: "Optional scope memory", knowledgeDomainId: "domain-1", componentId: "component-2", repositoryId: "repo-1", authority: CollectiveMemoryAuthorityLevel.Accepted, eventType: CollectiveMemoryEventType.Accepted);
        await InsertMemoryAsync("repo-miss", title: "Optional scope memory", knowledgeDomainId: "domain-1", componentId: "component-1", repositoryId: "repo-2", authority: CollectiveMemoryAuthorityLevel.Accepted, eventType: CollectiveMemoryEventType.Accepted);

        var result = await CreateService().RetrieveAsync(BuildQuery(
            text: "Optional scope",
            scope: BuildScope() with { KnowledgeDomainId = "domain-1", ComponentId = "component-1", RepositoryId = "repo-1" }));

        Assert.AreEqual(1, result.Matches.Count);
        Assert.AreEqual("domain-match", result.Matches[0].Memory.CollectiveMemoryId);
    }

    [TestMethod]
    public async Task TakeIsClampedAndRankFieldsArePopulated()
    {
        await InsertMemoryAsync("accepted-memory", title: "Ranked memory", authority: CollectiveMemoryAuthorityLevel.Accepted, eventType: CollectiveMemoryEventType.Accepted);

        var result = await CreateService().RetrieveAsync(BuildQuery(text: "Ranked", take: 500));
        var match = result.Matches.Single();

        Assert.AreEqual(50, result.Query.Take);
        Assert.IsTrue(match.RankScore is >= 0m and <= 1m);
        Assert.IsFalse(string.IsNullOrWhiteSpace(match.RankReason));
        CollectionAssert.Contains(match.SourceIds.ToArray(), "source-accepted-memory-1");
        CollectionAssert.Contains(match.EvidenceIds.ToArray(), "evidence-accepted-memory-1");
    }

    [TestMethod]
    public async Task RetrievalMatchAlwaysCarriesAdvisoryAuthorityFlagsAndWarnings()
    {
        await InsertMemoryAsync("accepted-memory", title: "Advisory memory", authority: CollectiveMemoryAuthorityLevel.Accepted, eventType: CollectiveMemoryEventType.Accepted);

        var match = (await CreateService().RetrieveAsync(BuildQuery(text: "Advisory"))).Matches.Single();

        Assert.IsFalse(match.IsAuthoritativeForAction);
        Assert.IsTrue(match.RequiresConscienceBeforeUse);
        Assert.IsTrue(match.RequiresPolicyApprovalForAction);
        Assert.IsTrue(match.Warnings.Any(warning => warning.Contains("advisory only", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(match.Warnings.Any(warning => warning.Contains("does not grant policy approval", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(match.Warnings.Any(warning => warning.Contains("does not grant tool execution approval", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task StronglyStableRanksAboveEmergingWhenOtherFactorsEqual()
    {
        await InsertMemoryAsync("strong-memory", title: "Ranking comparison memory", authority: CollectiveMemoryAuthorityLevel.Accepted, eventType: CollectiveMemoryEventType.Accepted, evidenceCount: 3, sourceTypeCount: 2, lastConfirmedAt: Now);
        await InsertMemoryAsync("emerging-memory", title: "Ranking comparison memory", authority: CollectiveMemoryAuthorityLevel.Accepted, eventType: CollectiveMemoryEventType.Accepted, evidenceCount: 1, sourceTypeCount: 1, contradictions: [BuildContradiction("emerging-memory")]);

        var result = await CreateService().RetrieveAsync(BuildQuery(text: "Ranking comparison", includeContradicted: true));

        Assert.AreEqual("strong-memory", result.Matches[0].Memory.CollectiveMemoryId);
        Assert.IsTrue(result.Matches[0].RankScore > result.Matches[1].RankScore);
    }

    [TestMethod]
    public async Task AcceptedRanksAboveCandidateWhenOtherFactorsEqual()
    {
        await InsertMemoryAsync("accepted-memory", title: "Authority ranking memory", authority: CollectiveMemoryAuthorityLevel.Accepted, eventType: CollectiveMemoryEventType.Accepted, evidenceCount: 2);
        await InsertMemoryAsync("candidate-memory", title: "Authority ranking memory", authority: CollectiveMemoryAuthorityLevel.Candidate, evidenceCount: 2);

        var result = await CreateService().RetrieveAsync(BuildQuery(
            text: "Authority ranking",
            authorityFilter: CollectiveMemoryRetrievalAuthorityFilter.IncludeCandidates));

        Assert.AreEqual("accepted-memory", result.Matches[0].Memory.CollectiveMemoryId);
    }

    [TestMethod]
    public async Task HighContradictionAndUnstableStabilityAddWarnings()
    {
        await InsertMemoryAsync("contradicted-memory", title: "Contradicted memory", authority: CollectiveMemoryAuthorityLevel.Accepted, eventType: CollectiveMemoryEventType.Accepted, contradictions: [BuildContradiction("contradicted-memory", weight: 5m)]);

        var match = (await CreateService().RetrieveAsync(BuildQuery(text: "Contradicted", includeContradicted: true))).Matches.Single();

        Assert.IsTrue(match.Warnings.Any(warning => warning.Contains("contradiction pressure", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(match.Warnings.Any(warning => warning.Contains("Unknown or Unstable", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task MinimumStabilityBandFiltersLowerBands()
    {
        await InsertMemoryAsync("stable-memory", title: "Stability filter memory", authority: CollectiveMemoryAuthorityLevel.Accepted, eventType: CollectiveMemoryEventType.Accepted, evidenceCount: 3, sourceTypeCount: 2, lastConfirmedAt: Now);
        await InsertMemoryAsync("candidate-memory", title: "Stability filter memory", authority: CollectiveMemoryAuthorityLevel.Candidate, evidenceCount: 1);

        var result = await CreateService().RetrieveAsync(BuildQuery(
            text: "Stability filter",
            authorityFilter: CollectiveMemoryRetrievalAuthorityFilter.IncludeCandidates,
            minimumStabilityBand: CollectiveMemoryStabilityBand.Stable));

        Assert.AreEqual(1, result.Matches.Count);
        Assert.AreEqual("stable-memory", result.Matches[0].Memory.CollectiveMemoryId);
    }

    [TestMethod]
    public async Task ExpiredMemoryExcludedByDefaultAndIncludedOnlyWhenRequested()
    {
        await InsertMemoryAsync("expired-memory", title: "Expired retrieval memory", authority: CollectiveMemoryAuthorityLevel.Accepted, eventType: CollectiveMemoryEventType.Accepted, expiresAt: Now.AddDays(-1));

        var defaultResult = await CreateService().RetrieveAsync(BuildQuery(text: "Expired retrieval"));
        var includedResult = await CreateService().RetrieveAsync(BuildQuery(text: "Expired retrieval", includeExpired: true));

        Assert.AreEqual(0, defaultResult.Matches.Count);
        Assert.AreEqual(1, includedResult.Matches.Count);
        Assert.AreEqual("expired-memory", includedResult.Matches[0].Memory.CollectiveMemoryId);
    }

    [TestMethod]
    public async Task RetrievalDoesNotMutateCollectiveMemoryOrAppendEvents()
    {
        await InsertMemoryAsync("accepted-memory", title: "Read only memory", authority: CollectiveMemoryAuthorityLevel.Accepted, eventType: CollectiveMemoryEventType.Accepted);
        var beforeItems = await CountAsync("agent.CollectiveMemoryItem");
        var beforeEvents = await CountAsync("agent.CollectiveMemoryEvent");

        _ = await CreateService().RetrieveAsync(BuildQuery(text: "Read only"));

        Assert.AreEqual(beforeItems, await CountAsync("agent.CollectiveMemoryItem"));
        Assert.AreEqual(beforeEvents, await CountAsync("agent.CollectiveMemoryEvent"));
    }

    [TestMethod]
    public void RetrievalImplementationDoesNotWriteSqlOrCallPromotionWeaviateToolsOrConscience()
    {
        var text = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "IronDev.Infrastructure",
            "AgentMemory",
            "SqlCollectiveMemoryRetrievalService.cs"));

        foreach (var forbidden in new[] { "INSERT ", "UPDATE ", "DELETE ", "MERGE ", "CREATE ", "ALTER ", "DROP ", "EXEC ", "usp_", "Weaviate", "ICollectiveMemoryPromotionService", "SqlCollectiveMemoryPromotionService", "ITool", "AgentSkillExecution", "IConscience", "ConscienceMemoryGovernance" })
        {
            Assert.IsFalse(text.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"Retrieval implementation contains forbidden token: {forbidden}");
        }
    }

    [TestMethod]
    public void RetrievalBoundaryTypesAreNotUsedByRuntimeAgentsToolsOrConscience()
    {
        foreach (var file in EnumerateProductionFiles().Where(file => !IsAllowedRetrievalProductionFile(file)))
        {
            var text = File.ReadAllText(file);

            foreach (var token in new[]
            {
                "ICollectiveMemoryRetrievalService",
                "SqlCollectiveMemoryRetrievalService",
                "CollectiveMemoryRetrievalResult",
                "CollectiveMemoryRetrievalMatch"
            })
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal),
                    $"Runtime production file uses collective-memory retrieval token '{token}': {file}");
            }
        }
    }

    [TestMethod]
    public void CollectiveMemoryRetrievalDatabaseScanFindsNoRetrievalSchemaProcedureOrBoost()
    {
        var databaseDirectory = Path.Combine(RepositoryRoot, "Database");

        if (!Directory.Exists(databaseDirectory))
            return;

        foreach (var file in Directory.EnumerateFiles(databaseDirectory, "*", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);

            foreach (var forbidden in new[]
            {
                "WeaviateCollectiveMemory",
                "RuntimeCollectiveMemoryRetrieval",
                "CollectiveMemoryRuntimeRetriever",
                "CollectiveMemoryToolExecution",
                "CollectiveMemoryConscienceIntegration",
                "RetrievalBoost",
                "AutoCollectiveMemoryRetrieval"
            })
            {
                Assert.IsFalse(text.Contains(forbidden, StringComparison.Ordinal),
                    $"Forbidden collective retrieval database token '{forbidden}' found in {file}.");
            }
        }
    }

    private SqlCollectiveMemoryRetrievalService CreateService() =>
        new(new TestConnectionFactory(ConnectionString));

    private async Task InsertMemoryAsync(
        string collectiveMemoryId,
        string title,
        string tenantId = "tenant-1",
        string projectId = "project-1",
        string? knowledgeDomainId = "memory-governance",
        string? componentId = "collective-memory",
        string? repositoryId = "IronDeveloper",
        CollectiveMemoryAuthorityLevel authority = CollectiveMemoryAuthorityLevel.Accepted,
        CollectiveMemoryEventType? eventType = null,
        int evidenceCount = 1,
        int sourceTypeCount = 1,
        DateTimeOffset? lastConfirmedAt = null,
        DateTimeOffset? expiresAt = null,
        IReadOnlyList<CollectiveMemoryContradictionRef>? contradictions = null)
    {
        var sourceRefs = Enumerable.Range(1, Math.Max(1, evidenceCount))
            .Select(index => BuildSource(
                $"source-{collectiveMemoryId}-{index}",
                sourceTypeCount >= 2 && index % 2 == 0
                    ? CollectiveMemorySourceType.MemoryExecutionAudit
                    : CollectiveMemorySourceType.RunMemoryReport,
                tenantId,
                projectId))
            .ToArray();
        var evidenceRefs = Enumerable.Range(1, Math.Max(0, evidenceCount))
            .Select(index => BuildEvidence($"evidence-{collectiveMemoryId}-{index}", sourceRefs[Math.Min(index - 1, sourceRefs.Length - 1)].SourceId))
            .ToArray();

        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT INTO agent.CollectiveMemoryItem
            (
                CollectiveMemoryId, TenantId, ProjectId, KnowledgeDomainId, ComponentId, RepositoryId,
                MemoryType, AuthorityLevel, Title, Summary, SourcesJson, EvidenceRefsJson,
                ContradictionsJson, SupersedesJson, Confidence, CreatedAtUtc, LastReviewedAtUtc,
                LastConfirmedAtUtc, ExpiresAtUtc, DecisionId, ThoughtLedgerEntryId, CorrelationId,
                CollectiveMemoryJson, ContentHashSha256
            )
            VALUES
            (
                @CollectiveMemoryId, @TenantId, @ProjectId, @KnowledgeDomainId, @ComponentId, @RepositoryId,
                @MemoryType, @AuthorityLevel, @Title, @Summary, @SourcesJson, @EvidenceRefsJson,
                @ContradictionsJson, N'[]', @Confidence, @CreatedAtUtc, @LastReviewedAtUtc,
                @LastConfirmedAtUtc, @ExpiresAtUtc, @DecisionId, @ThoughtLedgerEntryId, @CorrelationId,
                @CollectiveMemoryJson, NULL
            )
            """,
            new
            {
                CollectiveMemoryId = collectiveMemoryId,
                TenantId = tenantId,
                ProjectId = projectId,
                KnowledgeDomainId = knowledgeDomainId,
                ComponentId = componentId,
                RepositoryId = repositoryId,
                MemoryType = (int)CollectiveMemoryType.ArchitectureDecision,
                AuthorityLevel = (int)authority,
                Title = title,
                Summary = $"{title} summary states SQL remains the governed memory source.",
                SourcesJson = JsonSerializer.Serialize(sourceRefs, JsonOptions),
                EvidenceRefsJson = JsonSerializer.Serialize(evidenceRefs, JsonOptions),
                ContradictionsJson = JsonSerializer.Serialize(contradictions ?? [], JsonOptions),
                Confidence = 0.8m,
                CreatedAtUtc = Now,
                LastReviewedAtUtc = authority is CollectiveMemoryAuthorityLevel.Accepted or CollectiveMemoryAuthorityLevel.Reviewed ? Now : (DateTimeOffset?)null,
                LastConfirmedAtUtc = lastConfirmedAt,
                ExpiresAtUtc = expiresAt,
                DecisionId = authority is CollectiveMemoryAuthorityLevel.Accepted or CollectiveMemoryAuthorityLevel.Reviewed or CollectiveMemoryAuthorityLevel.Rejected ? $"decision-{collectiveMemoryId}" : null,
                ThoughtLedgerEntryId = $"thought-{collectiveMemoryId}",
                CorrelationId = $"correlation-{collectiveMemoryId}",
                CollectiveMemoryJson = JsonSerializer.Serialize(new { claim = title, collectiveMemoryId }, JsonOptions)
            });

        if (eventType.HasValue)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO agent.CollectiveMemoryEvent
                (
                    CollectiveMemoryEventId, CollectiveMemoryId, EventType, Reason, CreatedAtUtc,
                    CreatedByUserId, CreatedByAgentId, DecisionId, ThoughtLedgerEntryId, CorrelationId, EventJson
                )
                VALUES
                (
                    @CollectiveMemoryEventId, @CollectiveMemoryId, @EventType, @Reason, @CreatedAtUtc,
                    @CreatedByUserId, NULL, @DecisionId, @ThoughtLedgerEntryId, @CorrelationId, NULL
                )
                """,
                new
                {
                    CollectiveMemoryEventId = $"event-{collectiveMemoryId}-{eventType.Value}",
                    CollectiveMemoryId = collectiveMemoryId,
                    EventType = (int)eventType.Value,
                    Reason = "Seeded governed collective memory event.",
                    CreatedAtUtc = Now,
                    CreatedByUserId = "human-reviewer-1",
                    DecisionId = $"decision-{collectiveMemoryId}",
                    ThoughtLedgerEntryId = $"thought-{collectiveMemoryId}",
                    CorrelationId = $"correlation-{collectiveMemoryId}"
                });
        }
    }

    private static CollectiveMemoryRetrievalQuery BuildQuery(
        string? text = null,
        CollectiveMemoryScope? scope = null,
        CollectiveMemoryRetrievalAuthorityFilter authorityFilter = CollectiveMemoryRetrievalAuthorityFilter.ReviewedOrAccepted,
        CollectiveMemoryStabilityBand? minimumStabilityBand = null,
        bool includeExpired = false,
        bool includeContradicted = false,
        int take = 10) =>
        new()
        {
            Scope = scope ?? BuildScope(),
            Text = text,
            AuthorityFilter = authorityFilter,
            MinimumStabilityBand = minimumStabilityBand,
            IncludeExpired = includeExpired,
            IncludeContradicted = includeContradicted,
            Take = take
        };

    private static CollectiveMemoryScope BuildScope() =>
        new()
        {
            TenantId = "tenant-1",
            ProjectId = "project-1"
        };

    private static CollectiveMemorySourceRef BuildSource(
        string sourceId,
        CollectiveMemorySourceType sourceType,
        string tenantId,
        string projectId) =>
        new()
        {
            SourceType = sourceType,
            SourceId = sourceId,
            TenantId = tenantId,
            ProjectId = projectId,
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
            Summary = "Governed retrieval evidence.",
            Weight = 0.8m,
            CapturedAt = Now
        };

    private static CollectiveMemoryContradictionRef BuildContradiction(string collectiveMemoryId, decimal weight = 1.0m) =>
        new()
        {
            ContradictionId = $"contradiction-{collectiveMemoryId}",
            Source = BuildSource($"contradiction-source-{collectiveMemoryId}", CollectiveMemorySourceType.CodeReviewFinding, "tenant-1", "project-1"),
            Summary = "Counter-evidence exists and requires review.",
            Weight = weight,
            ObservedAt = Now
        };

    private async Task<int> CountAsync(string tableName)
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {tableName}");
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

    private static bool IsAllowedRetrievalProductionFile(string file)
    {
        var coreCollectivePrefix = Path.Combine("IronDev.Core", "AgentMemory", "Collective")
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var relative = Path.GetRelativePath(RepositoryRoot, file);

        return relative.StartsWith(coreCollectivePrefix, StringComparison.OrdinalIgnoreCase) ||
            relative.EndsWith(Path.Combine("IronDev.Infrastructure", "AgentMemory", "SqlCollectiveMemoryRetrievalService.cs"), StringComparison.OrdinalIgnoreCase);
    }

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
