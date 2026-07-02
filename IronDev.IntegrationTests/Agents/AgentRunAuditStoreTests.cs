using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using IronDev.Core.Agents;
using IronDev.Core.Agents.Audit;
using IronDev.Data;
using IronDev.Infrastructure.AgentRunAudit;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace IronDev.IntegrationTests.Agents;

[TestCategory("Store")]
[TestCategory("RequiresRealDatabase")]
[TestCategory("LongRunning")]
[TestClass]
public sealed class AgentRunAuditStoreTests : IntegrationTestBase
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 6, 11, 1, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private SqlAgentRunAuditEnvelopeStore _store = null!;
    private SqlAgentRunAuditEnvelopeReadRepository _readRepository = null!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropAgentRunAuditSchemaAsync();
        await ApplyAgentRunAuditMigrationAsync();

        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        _store = new SqlAgentRunAuditEnvelopeStore(connectionFactory);
        _readRepository = new SqlAgentRunAuditEnvelopeReadRepository(connectionFactory);
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        try
        {
            await DropAgentRunAuditSchemaAsync();
        }
        catch
        {
            // Cleanup should not hide the real assertion failure.
        }

        await base.TestCleanup();
    }

    [TestMethod]
    public void AgentRunAuditStore_AppendsEnvelopeAndSqlRepositoryReadsIt()
    {
        var envelope = BuildEnvelope("1", "agent-run-store-1");

        var result = _store.Append(envelope, CreatedAt.AddMinutes(5));
        var read = _readRepository.Get("1", "agent-run-store-1");
        var list = _readRepository.List("1");
        var queryService = new AgentRunAuditQueryService(_readRepository);
        var detail = queryService.GetAgentRun("1", "agent-run-store-1");

        Assert.AreEqual(AgentRunAuditEnvelopeAppendStatus.Appended, result.Status);
        Assert.AreEqual("agent-run-store-1", result.AgentRunId);
        AssertSha256(result.EnvelopeSha256);
        Assert.IsNotNull(read);
        Assert.AreEqual(envelope.Run.AgentRunId, read.Run.AgentRunId);
        Assert.AreEqual(envelope.ThoughtLedger.Single().ThoughtLedgerEntryId, read.ThoughtLedger.Single().ThoughtLedgerEntryId);
        Assert.AreEqual(1, list.Count);
        Assert.IsNotNull(detail.Run);
        Assert.AreEqual("agent-run-store-1", detail.Run.Run.AgentRunId);
    }

    [TestMethod]
    public void AgentRunAuditStore_DuplicateEnvelopeIsIdempotentButConflictingEnvelopeBlocks()
    {
        var envelope = BuildEnvelope("1", "agent-run-duplicate");
        var changed = envelope with
        {
            Run = envelope.Run with
            {
                RequestSummary = "Different request summary for the same agent run id."
            }
        };

        var first = _store.Append(envelope, CreatedAt);
        var second = _store.Append(envelope, CreatedAt.AddMinutes(1));
        var conflict = _store.Append(changed, CreatedAt.AddMinutes(2));

        Assert.AreEqual(AgentRunAuditEnvelopeAppendStatus.Appended, first.Status);
        Assert.AreEqual(AgentRunAuditEnvelopeAppendStatus.AlreadyExists, second.Status);
        Assert.AreEqual(first.EnvelopeSha256, second.EnvelopeSha256);
        Assert.AreEqual(AgentRunAuditEnvelopeAppendStatus.Conflict, conflict.Status);
        Assert.IsTrue(conflict.Issues.Any(issue => issue.Code == "AGENT_RUN_AUDIT_DUPLICATE_CONFLICT"));
    }

    [TestMethod]
    public void AgentRunAuditStore_RejectsUnsafeEnvelopeBeforeInsert()
    {
        var envelope = BuildEnvelope("1", "agent-run-unsafe") with
        {
            Outputs =
            [
                BuildOutput("agent-run-unsafe") with
                {
                    Summary = "Create a runtime action from this output.",
                    CreatesRuntimeAction = true
                }
            ]
        };

        var result = _store.Append(envelope, CreatedAt);

        Assert.AreEqual(AgentRunAuditEnvelopeAppendStatus.Rejected, result.Status);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == AgentRunAuditEnvelopeValidator.OutputRuntimeActionBlocked));
        Assert.AreEqual(0, CountRows());
    }

    [TestMethod]
    public async Task AgentRunAuditStore_SchemaBlocksUpdateAndDelete()
    {
        var result = _store.Append(BuildEnvelope("1", "agent-run-immutable"), CreatedAt);
        Assert.AreEqual(AgentRunAuditEnvelopeAppendStatus.Appended, result.Status);

        await ExpectSqlFailsAsync("UPDATE agent.AgentRunAuditEnvelope SET AgentName = 'changed' WHERE AgentRunId = 'agent-run-immutable';");
        await ExpectSqlFailsAsync("DELETE FROM agent.AgentRunAuditEnvelope WHERE AgentRunId = 'agent-run-immutable';");
    }

    [TestMethod]
    public async Task AgentRunAuditStore_DirectSqlUnsafeSafetyFlagsAreBlocked()
    {
        var unsafeFlags = new[]
        {
            "HasRawPrivateReasoning",
            "HasAuthorityClaim",
            "HasApprovalClaim",
            "HasMemoryPromotionClaim",
            "HasRuntimeActionOutput",
            "HasAuthorityCreatingOutput"
        };

        foreach (var flag in unsafeFlags)
            await ExpectSqlFailsAsync(BuildUnsafeFlagInsertSql($"agent-run-direct-{flag}", flag));

        Assert.AreEqual(0, CountRows());
    }

    [TestMethod]
    public async Task AgentRunAuditStore_ReadRepositoryDeserializesEnvelopeJsonInsteadOfProjectedColumns()
    {
        var envelope = BuildEnvelope("1", "agent-run-json-source");
        var json = JsonSerializer.Serialize(envelope, JsonOptions);

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO agent.AgentRunAuditEnvelope
            (
                TenantId,
                ProjectId,
                CampaignId,
                RunId,
                AgentRunId,
                AgentId,
                AgentName,
                AgentKind,
                ExecutionMode,
                Status,
                TriggerType,
                CreatedAtUtc,
                CompletedAtUtc,
                HasRawPrivateReasoning,
                HasAuthorityClaim,
                HasApprovalClaim,
                HasMemoryPromotionClaim,
                HasRuntimeActionOutput,
                HasAuthorityCreatingOutput,
                HasBlockedCapabilityAttempt,
                HasBoundaryBlock,
                EnvelopeSha256,
                EnvelopeJson,
                AppendedAtUtc
            )
            VALUES
            (
                @TenantId,
                @ProjectId,
                @CampaignId,
                @RunId,
                @AgentRunId,
                @AgentId,
                @AgentName,
                @AgentKind,
                @ExecutionMode,
                @Status,
                @TriggerType,
                @CreatedAtUtc,
                @CompletedAtUtc,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                @EnvelopeSha256,
                @EnvelopeJson,
                @AppendedAtUtc
            );
            """,
            new
            {
                envelope.Run.TenantId,
                envelope.Run.ProjectId,
                envelope.Run.CampaignId,
                envelope.Run.RunId,
                envelope.Run.AgentRunId,
                envelope.Run.AgentId,
                AgentName = "wrong-projected-agent-name",
                AgentKind = (int)envelope.AgentDefinitionSnapshot.Kind,
                ExecutionMode = (int)envelope.AgentDefinitionSnapshot.ExecutionMode,
                Status = (int)envelope.Run.Status,
                TriggerType = (int)envelope.Run.TriggerType,
                CreatedAtUtc = envelope.Run.CreatedAtUtc.UtcDateTime,
                CompletedAtUtc = envelope.Run.CompletedAtUtc?.UtcDateTime,
                EnvelopeSha256 = new string('c', 64),
                EnvelopeJson = json,
                AppendedAtUtc = CreatedAt.UtcDateTime
            });

        var read = _readRepository.Get("1", "agent-run-json-source");

        Assert.IsNotNull(read);
        Assert.AreEqual(envelope.Run.AgentName, read.Run.AgentName);
        Assert.AreNotEqual("wrong-projected-agent-name", read.Run.AgentName);
    }

    [TestMethod]
    public void AgentRunAuditStore_ApiDefaultRegistrationUsesSqlReadRepository()
    {
        var programText = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "IronDev.Api", "Program.cs"));

        StringAssert.Contains(programText, "SqlAgentRunAuditEnvelopeReadRepository");
        StringAssert.Contains(programText, "SqlAgentRunAuditEnvelopeStore");
        Assert.IsFalse(programText.Contains(
            "AddSingleton<IAgentRunAuditEnvelopeReadRepository, InMemoryAgentRunAuditEnvelopeReadRepository>",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public void AgentRunAuditStore_StaticBoundary_NoRuntimeControlOrManualAgentPaths()
    {
        var repositoryRoot = FindRepositoryRoot();
        var productionFiles = new[]
        {
            Path.Combine(repositoryRoot, "IronDev.Core", "Agents", "Audit", "AgentRunAuditStoreModels.cs"),
            Path.Combine(repositoryRoot, "IronDev.Infrastructure", "AgentRunAudit", "AgentRunAuditEnvelopeJson.cs"),
            Path.Combine(repositoryRoot, "IronDev.Infrastructure", "AgentRunAudit", "SqlAgentRunAuditEnvelopeStore.cs"),
            Path.Combine(repositoryRoot, "IronDev.Infrastructure", "AgentRunAudit", "SqlAgentRunAuditEnvelopeReadRepository.cs"),
            Path.Combine(repositoryRoot, "IronDev.Api", "Controllers", "AgentRunAuditController.cs")
        };
        var forbiddenTokens = new[]
        {
            "HttpPost",
            "HttpPut",
            "HttpPatch",
            "HttpDelete",
            "RunAgent",
            "ExecuteAgent",
            "ApproveAgent",
            "ScheduleAgent",
            "RetryAgent",
            "IAgentRuntime",
            "AgentRuntime",
            "AgentScheduler",
            "AgentOrchestrator",
            "AgentPromptRunner",
            "AgentToolRouter",
            "IChatCompletion",
            "OpenAI",
            "Anthropic",
            "Gemini",
            "Weaviate",
            "AddHostedService",
            "IHostedService",
            "BackgroundService",
            "ICollectiveMemoryPromotionService",
            "SqlCollectiveMemoryPromotionService",
            "IMemoryImprovementProposalStore",
            "SqlMemoryImprovementProposalStore",
            "ManualIndependentCriticAgentService",
            "ManualMemoryImprovementAgentService"
        };

        foreach (var file in productionFiles)
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbiddenTokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal),
                    $"Agent run audit store production file contains forbidden token '{token}': {file}");
            }
        }
    }

    private static AgentRunAuditEnvelope BuildEnvelope(string projectId, string agentRunId)
    {
        var definition = AgentDefinitionCatalog.IndependentCriticAgent;

        return new AgentRunAuditEnvelope
        {
            Run = new AgentRunRecord
            {
                AgentRunId = agentRunId,
                TenantId = "tenant-1",
                ProjectId = projectId,
                CampaignId = "campaign-1",
                RunId = $"run-{agentRunId}",
                AgentId = definition.AgentId,
                AgentName = definition.Name,
                RequestedByUserId = "user-1",
                TriggerType = IronDev.Core.Agents.Audit.AgentRunTriggerType.ManualUserRequest,
                Status = IronDev.Core.Agents.Audit.AgentRunStatus.CompletedWithWarnings,
                RequestSummary = "Inspect supplied audit evidence.",
                Purpose = "Expose read-only agent run audit evidence.",
                CreatedAtUtc = CreatedAt,
                StartedAtUtc = CreatedAt.AddSeconds(1),
                CompletedAtUtc = CreatedAt.AddSeconds(2)
            },
            AgentDefinitionSnapshot = definition,
            Inputs = [BuildInput(agentRunId)],
            Outputs = [BuildOutput(agentRunId)],
            Steps = [BuildStep(agentRunId)],
            CapabilityUses =
            [
                BuildCapability(agentRunId, AgentCapability.CreateReport, IronDev.Core.Agents.Audit.AgentCapabilityUseOutcome.Allowed, definition),
                BuildCapability(agentRunId, AgentCapability.RunTool, IronDev.Core.Agents.Audit.AgentCapabilityUseOutcome.Blocked, definition)
            ],
            BoundaryDecisions = [BuildBoundary(agentRunId)],
            ThoughtLedger = [BuildThought(agentRunId)]
        };
    }

    private static AgentRunInputRef BuildInput(string agentRunId) =>
        new()
        {
            InputRefId = $"input-{agentRunId}",
            AgentRunId = agentRunId,
            RefType = "AgentRunAuditEnvelope",
            RefId = $"audit-input-{agentRunId}",
            Source = "manual audit fixture",
            Summary = "Safe audit input summary.",
            Sha256 = new string('a', 64)
        };

    private static AgentRunOutputRef BuildOutput(string agentRunId) =>
        new()
        {
            OutputRefId = $"output-{agentRunId}",
            AgentRunId = agentRunId,
            RefType = "CriticReviewResult",
            RefId = $"critic-result-{agentRunId}",
            Summary = "Safe review-only output summary.",
            Sha256 = new string('b', 64),
            IsReviewOnly = true,
            IsProposalOnly = false,
            CreatesAuthority = false,
            CreatesRuntimeAction = false,
            ContainsRawPrivateReasoning = false,
            EvidenceRefs = ["critic-review.json"]
        };

    private static AgentRunStep BuildStep(string agentRunId) =>
        new()
        {
            StepId = $"step-{agentRunId}",
            AgentRunId = agentRunId,
            Sequence = 1,
            StepType = IronDev.Core.Agents.Audit.AgentRunStepType.OutputRecorded,
            OccurredAtUtc = CreatedAt.AddSeconds(2),
            Summary = "Recorded safe review-only output.",
            EvidenceRefs = ["critic-review.json"]
        };

    private static AgentCapabilityUseRecord BuildCapability(
        string agentRunId,
        AgentCapability capability,
        IronDev.Core.Agents.Audit.AgentCapabilityUseOutcome outcome,
        AgentDefinition definition) =>
        new()
        {
            CapabilityUseId = $"capability-{agentRunId}-{capability}",
            AgentRunId = agentRunId,
            Capability = capability,
            Outcome = outcome,
            Summary = $"{capability} was {outcome}.",
            BoundaryDecisionId = $"boundary-{agentRunId}",
            EvidenceRef = $"capability-evidence-{agentRunId}",
            WasDeclaredOnAgent = definition.Capabilities?.Contains(capability) == true,
            WasForbiddenOnAgent = definition.ForbiddenCapabilities?.Contains(capability) == true
        };

    private static AgentBoundaryDecision BuildBoundary(string agentRunId) =>
        new()
        {
            BoundaryDecisionId = $"boundary-{agentRunId}",
            AgentRunId = agentRunId,
            BoundaryType = IronDev.Core.Agents.Audit.AgentBoundaryDecisionType.Capability,
            Decision = "blocked",
            Reason = "Dangerous capability attempt stayed blocked by audit boundary.",
            SourceRefId = $"capability-{agentRunId}-RunTool",
            GrantsAuthority = false,
            GrantsHumanApproval = false,
            GrantsPolicyApproval = false,
            GrantsMemoryPromotion = false,
            EvidenceRefs = ["capability-evidence.json"]
        };

    private static IronDev.Core.Agents.Audit.ThoughtLedgerEntry BuildThought(string agentRunId) =>
        new()
        {
            ThoughtLedgerEntryId = $"thought-{agentRunId}",
            AgentRunId = agentRunId,
            EntryType = IronDev.Core.Agents.Audit.ThoughtLedgerEntryType.DecisionRationale,
            Summary = "Review finding is advisory and cannot authorize actions.",
            EvidenceRefs = ["critic-review.json"],
            Assumptions = ["Human decision remains separate from the audit record."],
            Risks = ["Humans still need to review follow-up actions."],
            RequiredFollowUps = ["Review the evidence package before acting."],
            RecordedAtUtc = CreatedAt.AddSeconds(2)
        };

    private int CountRows()
    {
        using var connection = new SqlConnection(ConnectionString);
        connection.Open();
        return connection.QuerySingle<int>("SELECT COUNT(*) FROM agent.AgentRunAuditEnvelope;");
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
            return;
        }

        Assert.Fail($"Expected SQL mutation to fail but it succeeded: {sql}");
    }

    private static string BuildUnsafeFlagInsertSql(string agentRunId, string unsafeFlag)
    {
        if (!AllowedUnsafeFlagNames.Contains(unsafeFlag))
            throw new ArgumentOutOfRangeException(nameof(unsafeFlag), unsafeFlag, "Unsupported unsafe audit flag.");

        static int Flag(string current, string expected) =>
            string.Equals(current, expected, StringComparison.Ordinal) ? 1 : 0;

        var envelopeJson = $"{{\"agentRunId\":\"{agentRunId}\",\"unsafeFlag\":\"{unsafeFlag}\"}}";

        return $"""
            INSERT INTO agent.AgentRunAuditEnvelope
            (
                TenantId,
                ProjectId,
                CampaignId,
                RunId,
                AgentRunId,
                AgentId,
                AgentName,
                AgentKind,
                ExecutionMode,
                Status,
                TriggerType,
                CreatedAtUtc,
                CompletedAtUtc,
                HasRawPrivateReasoning,
                HasAuthorityClaim,
                HasApprovalClaim,
                HasMemoryPromotionClaim,
                HasRuntimeActionOutput,
                HasAuthorityCreatingOutput,
                HasBlockedCapabilityAttempt,
                HasBoundaryBlock,
                EnvelopeSha256,
                EnvelopeJson,
                AppendedAtUtc
            )
            VALUES
            (
                N'tenant-1',
                N'1',
                N'campaign-1',
                N'run-{agentRunId}',
                N'{agentRunId}',
                N'critic-agent',
                N'IndependentCriticAgent',
                1,
                1,
                3,
                1,
                SYSUTCDATETIME(),
                SYSUTCDATETIME(),
                {Flag(unsafeFlag, "HasRawPrivateReasoning")},
                {Flag(unsafeFlag, "HasAuthorityClaim")},
                {Flag(unsafeFlag, "HasApprovalClaim")},
                {Flag(unsafeFlag, "HasMemoryPromotionClaim")},
                {Flag(unsafeFlag, "HasRuntimeActionOutput")},
                {Flag(unsafeFlag, "HasAuthorityCreatingOutput")},
                0,
                0,
                '{new string('d', 64)}',
                N'{envelopeJson}',
                SYSUTCDATETIME()
            );
            """;
    }

    private async Task ApplyAgentRunAuditMigrationAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_agent_run_audit_envelope.sql")));
    }

    private async Task DropAgentRunAuditSchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            IF OBJECT_ID('agent.TR_AgentRunAuditEnvelope_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentRunAuditEnvelope_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentRunAuditEnvelope', 'U') IS NOT NULL
                DROP TABLE agent.AgentRunAuditEnvelope;
            IF SCHEMA_ID('agent') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.objects WHERE schema_id = SCHEMA_ID('agent'))
                DROP SCHEMA agent;
            """);
    }

    private static void AssertSha256(string value)
    {
        Assert.AreEqual(64, value.Length);
        Assert.IsTrue(value.All(Uri.IsHexDigit));
    }

    private static readonly IReadOnlySet<string> AllowedUnsafeFlagNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "HasRawPrivateReasoning",
        "HasAuthorityClaim",
        "HasApprovalClaim",
        "HasMemoryPromotionClaim",
        "HasRuntimeActionOutput",
        "HasAuthorityCreatingOutput"
    };

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            PropertyNamingPolicy = null,
            DictionaryKeyPolicy = null,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
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

        throw new InvalidOperationException("Could not locate repository root for agent run audit store tests.");
    }
}

