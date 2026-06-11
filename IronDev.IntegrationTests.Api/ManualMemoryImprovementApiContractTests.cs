using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using IronDev.Core.Agents;
using IronDev.Core.Agents.Audit;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class ManualMemoryImprovementApiContractTests : ApiTestBase
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 6, 12, 4, 0, 0, TimeSpan.Zero);

    [TestInitialize]
    public async Task ManualMemoryImprovementInitialize()
    {
        await DropAgentRunAuditSchemaAsync();
        await ApplyAgentRunAuditMigrationAsync();
    }

    [TestCleanup]
    public async Task ManualMemoryImprovementCleanup()
    {
        await DropAgentRunAuditSchemaAsync();
    }

    [TestMethod]
    public async Task ManualMemoryImprovementApi_Create_IsProposalOnly()
    {
        using var client = await AuthedClientAsync();
        var before = await CountAuditRowsAsync();

        var response = await client.PostAsJsonAsync("/api/v1/manual-memory-improvements", ValidRequest(701, "proposal-only"));
        var json = await ReadJsonAsync(response);
        var after = await CountAuditRowsAsync();
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual(before + 1, after, "POST may append only the stored manual memory-improvement audit envelope.");
        Assert.AreEqual("succeeded", json.RootElement.GetProperty("status").GetString());
        Assert.IsTrue(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
        Assert.IsTrue(json.RootElement.GetProperty("humanApprovalRequired").GetBoolean());
        Assert.AreEqual("manual-memory-improvement-701-agentrunauditenvelope-audit-701-proposal-only", json.RootElement.GetProperty("runId").GetString());

        var boundary = json.RootElement.GetProperty("boundary");
        AssertProposalOnlyBoundary(boundary);

        var data = json.RootElement.GetProperty("data");
        Assert.IsTrue(data.GetProperty("proposalOnly").GetBoolean());
        Assert.IsTrue(data.GetProperty("requiresHumanReview").GetBoolean());
        Assert.AreEqual(1, data.GetProperty("patternCount").GetInt32());
        Assert.AreEqual(1, data.GetProperty("proposalCount").GetInt32());
        Assert.IsTrue(data.GetProperty("proposals")[0].GetProperty("isProposalOnly").GetBoolean());
        Assert.IsFalse(data.GetProperty("proposals")[0].GetProperty("createsCollectiveMemory").GetBoolean());
        Assert.IsFalse(data.GetProperty("proposals")[0].GetProperty("promotesMemory").GetBoolean());
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public async Task ManualMemoryImprovementApi_Get_IsReadOnlyInspection()
    {
        using var client = await AuthedClientAsync();
        var create = await client.PostAsJsonAsync("/api/v1/manual-memory-improvements", ValidRequest(702, "get-readonly"));
        var createJson = await ReadJsonAsync(create);
        var runId = createJson.RootElement.GetProperty("runId").GetString();
        var before = await CountAuditRowsAsync();

        var response = await client.GetAsync($"/api/v1/manual-memory-improvements/{runId}?projectId=702");
        var json = await ReadJsonAsync(response);
        var after = await CountAuditRowsAsync();
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual(before, after, "GET must not append audit rows.");
        Assert.AreEqual("succeeded", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
        Assert.IsTrue(json.RootElement.GetProperty("data").GetProperty("proposalOnly").GetBoolean());
        Assert.IsTrue(json.RootElement.GetProperty("data").GetProperty("proposalOnlyOutput").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("data").GetProperty("createsAuthority").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("data").GetProperty("createsRuntimeAction").GetBoolean());
        AssertProposalOnlyBoundary(json.RootElement.GetProperty("boundary"));
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public async Task ManualMemoryImprovementApi_UnknownRun_ReturnsNotFound()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync("/api/v1/manual-memory-improvements/missing-run?projectId=703");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual("not_found", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
    }

    [TestMethod]
    public async Task ManualMemoryImprovementApi_RejectsCrossProjectAccess()
    {
        using var client = await AuthedClientAsync();
        var create = await client.PostAsJsonAsync("/api/v1/manual-memory-improvements", ValidRequest(704, "cross-project"));
        var createJson = await ReadJsonAsync(create);
        var runId = createJson.RootElement.GetProperty("runId").GetString();

        var response = await client.GetAsync($"/api/v1/manual-memory-improvements/{runId}?projectId=705");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task ManualMemoryImprovementApi_RejectsPromotionFields()
    {
        using var client = await AuthedClientAsync();
        var before = await CountAuditRowsAsync();
        var request = ValidRequest(706, "promotion-fields") with
        {
            Extra = new Dictionary<string, object?>
            {
                ["promote"] = true,
                ["collectiveMemoryId"] = "collective-706",
                ["saveToMemory"] = true
            }
        };

        var response = await client.PostAsJsonAsync("/api/v1/manual-memory-improvements", request.ToBody());
        var json = await ReadJsonAsync(response);
        var after = await CountAuditRowsAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual(before, after, "Rejected promotion-shaped requests must not append audit rows.");
        Assert.AreEqual("validation_error", json.RootElement.GetProperty("status").GetString());
        Assert.IsTrue(json.RootElement.GetProperty("errors").EnumerateArray().Any(error =>
            error.GetProperty("category").GetString() == "unsupported_field"));
    }

    [TestMethod]
    public async Task ManualMemoryImprovementApi_RejectsOversizedContent()
    {
        using var client = await AuthedClientAsync();
        var request = ValidRequest(707, "oversized") with
        {
            Content = new string('x', 12_001)
        };

        var response = await client.PostAsJsonAsync("/api/v1/manual-memory-improvements", request.ToBody());
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual("validation_error", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
    }

    [TestMethod]
    public async Task ManualMemoryImprovementApi_DoesNotExposeHiddenReasoning()
    {
        using var client = await AuthedClientAsync();
        var before = await CountAuditRowsAsync();
        var request = ValidRequest(708, "hidden-reasoning") with
        {
            Content = "chain-of-thought PRIVATE_MARKER should never enter the manual memory-improvement API."
        };

        var response = await client.PostAsJsonAsync("/api/v1/manual-memory-improvements", request.ToBody());
        var json = await ReadJsonAsync(response);
        var after = await CountAuditRowsAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual(before, after, "Hidden reasoning rejection must happen before audit append.");
        AssertNoPrivateReasoningLeak(json.RootElement.ToString());
    }

    [TestMethod]
    public async Task ManualMemoryImprovementApi_DoesNotExposeHiddenReasoning_WhenStoredAuditContainsPrivateReasoning()
    {
        await InsertEnvelopeDirectlyAsync(BuildEnvelopeWithPrivateReasoning("manual-memory-improvement-private-1", 709));
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync("/api/v1/manual-memory-improvements/manual-memory-improvement-private-1?projectId=709");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        StringAssert.Contains(text, "[redacted: sensitive memory-improvement audit text]");
        AssertNoPrivateReasoningLeak(text);
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public async Task ManualMemoryImprovementApi_UnauthenticatedRequestsAreRejected()
    {
        var get = await Client.GetAsync("/api/v1/manual-memory-improvements/missing?projectId=710");
        var post = await Client.PostAsJsonAsync("/api/v1/manual-memory-improvements", ValidRequest(710, "unauthenticated").ToBody());

        Assert.AreEqual(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, post.StatusCode);
    }

    [TestMethod]
    public async Task ManualMemoryImprovementApi_DoesNotPromoteMemoryWriteCollectiveVectorApproveApplyOrExecute()
    {
        using var client = await AuthedClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/manual-memory-improvements", ValidRequest(711, "boundary-proof"));
        var text = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(text);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        var boundary = json.RootElement.GetProperty("boundary");
        AssertProposalOnlyBoundary(boundary);
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public void ManualMemoryImprovementApi_ControllerDoesNotReferenceForbiddenServices()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers", "ManualMemoryImprovementsV1Controller.cs"));

        var forbiddenTokens = new[]
        {
            "[HttpPut",
            "[HttpPatch",
            "[HttpDelete",
            "IStoredManualIndependentCriticAgentService",
            "IManualIndependentCriticAgentService",
            "ManualTesterAgentToolExecutionService",
            "ToolExecutionAuditStore",
            "AgentToolExecutionGate",
            "IWorkspaceApply",
            "ApplyCopy",
            "ProcessStartInfo",
            "File.Copy",
            "File.Delete",
            "SqlConnection",
            "INSERT INTO",
            "UPDATE ",
            "DELETE FROM",
            "ICollectiveMemoryPromotion",
            "CollectiveMemoryPromotionService",
            "WeaviateSemanticMemoryService",
            "IWeaviate",
            "MemoryIndexQueue"
        };

        foreach (var token in forbiddenTokens)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found in manual memory-improvement API controller: {token}");

        StringAssert.Contains(text, "IStoredManualMemoryImprovementAgentService");
        StringAssert.Contains(text, "IAgentRunAuditQueryService");
    }

    [TestMethod]
    public void ManualMemoryImprovementApi_DocumentationPreservesBoundaries()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "API_MANUAL_MEMORY_IMPROVEMENT_V1.md"));

        StringAssert.Contains(text, "Manual Memory Improvement API v1");
        StringAssert.Contains(text, "POST `/api/v1/manual-memory-improvements`");
        StringAssert.Contains(text, "GET `/api/v1/manual-memory-improvements/{agentRunId}?projectId={projectId}`");
        StringAssert.Contains(text, "Memory improvement is proposal-only.");
        StringAssert.Contains(text, "Memory improvement is not promotion.");
        StringAssert.Contains(text, "Memory proposal is not promotion.");
        StringAssert.Contains(text, "Memory safe is not approval.");
        StringAssert.Contains(text, "Candidate is not memory.");
        StringAssert.Contains(text, "Retrieval match is not memory candidate.");
        StringAssert.Contains(text, "Audit evidence is not approval.");
        StringAssert.Contains(text, "API response status is not governance.");
        StringAssert.Contains(text, "Human review remains required for memory promotion.");
        StringAssert.Contains(text, "Human review remains required for source apply.");
    }

    private static async Task<HttpClient> AuthedClientAsync()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        return GetAuthedClient(tenantToken);
    }

    private static ManualMemoryImprovementApiRequestBody ValidRequest(int projectId, string correlationId) =>
        new(
            projectId,
            "AgentRunAuditEnvelope",
            $"audit-{projectId}",
            "Identify repeated manual correction pattern",
            "Multiple reviewed runs show the same manual correction pattern and need a proposal-only memory improvement draft.",
            [$"audit-{projectId}", $"manual-correction-{projectId}"],
            "This is public audit summary context only.",
            "RepeatedManualCorrection",
            correlationId);

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    private static async Task<int> CountAuditRowsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<int>("""
            IF OBJECT_ID('agent.AgentRunAuditEnvelope', 'U') IS NULL
                SELECT 0;
            ELSE
                SELECT COUNT(*) FROM agent.AgentRunAuditEnvelope;
            """);
    }

    private static async Task InsertEnvelopeDirectlyAsync(AgentRunAuditEnvelope envelope)
    {
        var json = JsonSerializer.Serialize(envelope);

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
                envelope.Run.AgentName,
                AgentKind = (int)envelope.AgentDefinitionSnapshot.Kind,
                ExecutionMode = (int)envelope.AgentDefinitionSnapshot.ExecutionMode,
                Status = (int)envelope.Run.Status,
                TriggerType = (int)envelope.Run.TriggerType,
                CreatedAtUtc = envelope.Run.CreatedAtUtc.UtcDateTime,
                CompletedAtUtc = envelope.Run.CompletedAtUtc?.UtcDateTime,
                EnvelopeSha256 = new string('e', 64),
                EnvelopeJson = json,
                AppendedAtUtc = DateTimeOffset.UtcNow.UtcDateTime
            });
    }

    private static AgentRunAuditEnvelope BuildEnvelopeWithPrivateReasoning(string agentRunId, int projectId)
    {
        var privateText = "PRIVATE_MARKER chain-of-thought hidden reasoning raw prompt scratchpad private reasoning";
        var agentDefinition = AgentDefinitionCatalog.MemoryImprovementAgent;

        return new AgentRunAuditEnvelope
        {
            Run = new AgentRunRecord
            {
                AgentRunId = agentRunId,
                TenantId = AssignedTenantId.ToString(),
                ProjectId = projectId.ToString(),
                CampaignId = "campaign-pr60",
                RunId = $"correlation-{agentRunId}",
                AgentId = agentDefinition.AgentId,
                AgentName = agentDefinition.Name,
                Status = IronDev.Core.Agents.Audit.AgentRunStatus.CompletedWithWarnings,
                TriggerType = AgentRunTriggerType.ManualUserRequest,
                RequestedByUserId = "user-1",
                RequestSummary = privateText,
                Purpose = privateText,
                CreatedAtUtc = CreatedAt,
                StartedAtUtc = CreatedAt,
                CompletedAtUtc = CreatedAt
            },
            AgentDefinitionSnapshot = agentDefinition,
            Inputs =
            [
                new AgentRunInputRef
                {
                    InputRefId = $"input-{agentRunId}",
                    AgentRunId = agentRunId,
                    RefType = "AgentRunAuditEnvelope",
                    RefId = "audit-private",
                    Source = privateText,
                    Summary = privateText,
                    Sha256 = new string('a', 64),
                    IsAuthoritativeForAction = false,
                    ContainsRawPrivateReasoning = true
                }
            ],
            Outputs =
            [
                new AgentRunOutputRef
                {
                    OutputRefId = $"output-detection-{agentRunId}",
                    AgentRunId = agentRunId,
                    RefType = "MemoryImprovementDetectionResult",
                    RefId = $"memory-detection-{agentRunId}",
                    Summary = privateText,
                    EvidenceRefs = [privateText],
                    IsReviewOnly = false,
                    IsProposalOnly = true,
                    CreatesAuthority = false,
                    CreatesRuntimeAction = false,
                    ContainsRawPrivateReasoning = true
                },
                new AgentRunOutputRef
                {
                    OutputRefId = $"output-proposal-{agentRunId}",
                    AgentRunId = agentRunId,
                    RefType = "MemoryImprovementProposalDraft",
                    RefId = $"memory-proposal-draft-{agentRunId}-001",
                    Summary = privateText,
                    EvidenceRefs = [privateText],
                    IsReviewOnly = false,
                    IsProposalOnly = true,
                    CreatesAuthority = false,
                    CreatesRuntimeAction = false,
                    ContainsRawPrivateReasoning = true
                }
            ],
            CapabilityUses =
            [
                new AgentCapabilityUseRecord
                {
                    CapabilityUseId = $"capability-{agentRunId}",
                    AgentRunId = agentRunId,
                    Capability = AgentCapability.CreateMemoryProposal,
                    Outcome = AgentCapabilityUseOutcome.Allowed,
                    Summary = privateText,
                    BoundaryDecisionId = $"boundary-{agentRunId}",
                    EvidenceRef = privateText,
                    WasDeclaredOnAgent = true,
                    WasForbiddenOnAgent = false
                }
            ],
            BoundaryDecisions =
            [
                new AgentBoundaryDecision
                {
                    BoundaryDecisionId = $"boundary-{agentRunId}",
                    AgentRunId = agentRunId,
                    BoundaryType = AgentBoundaryDecisionType.OutputValidation,
                    Decision = "allowed",
                    Reason = privateText,
                    SourceRefId = privateText,
                    EvidenceRefs = [privateText],
                    GrantsAuthority = false,
                    GrantsHumanApproval = false,
                    GrantsPolicyApproval = false,
                    GrantsMemoryPromotion = false
                }
            ],
            ThoughtLedger =
            [
                new IronDev.Core.Agents.Audit.ThoughtLedgerEntry
                {
                    ThoughtLedgerEntryId = $"thought-{agentRunId}",
                    AgentRunId = agentRunId,
                    EntryType = ThoughtLedgerEntryType.EvidenceUsed,
                    Summary = privateText,
                    EvidenceRefs = [privateText],
                    Assumptions = [privateText],
                    RejectedAlternatives = [privateText],
                    Risks = [privateText],
                    RequiredFollowUps = [privateText],
                    ContainsRawPrivateReasoning = true,
                    GrantsAuthority = false,
                    GrantsApproval = false,
                    GrantsMemoryPromotion = false,
                    RecordedAtUtc = CreatedAt
                }
            ],
            Steps =
            [
                new AgentRunStep
                {
                    StepId = $"step-{agentRunId}",
                    AgentRunId = agentRunId,
                    Sequence = 1,
                    StepType = AgentRunStepType.Completed,
                    OccurredAtUtc = CreatedAt,
                    Summary = privateText,
                    EvidenceRefs = [privateText],
                    ContainsRawPrivateReasoning = true
                }
            ]
        };
    }

    private static void AssertProposalOnlyBoundary(JsonElement boundary)
    {
        Assert.IsFalse(boundary.GetProperty("memoryImprovementIsPromotion").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("memoryProposalIsPromotion").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("memorySafeIsApproval").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("candidateIsMemory").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("retrievalMatchIsMemoryCandidate").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("auditIsApproval").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("sourceApplied").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("memoryPromoted").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("collectiveMemoryWritten").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("vectorAuthorityWritten").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("toolExecuted").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("modelOutputIsAuthority").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("endpointAccessIsExecutionPermission").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("apiResponseStatusIsGovernance").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequiredForMemoryPromotion").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequiredForSourceApply").GetBoolean());
    }

    private static void AssertNoMisleadingAuthorityLanguage(string text)
    {
        var forbidden = new[]
        {
            "approved\":true",
            "governed\":true",
            "applied\":true",
            "promoted\":true",
            "executionPermitted\":true",
            "authority\":\"memory",
            "authority\":\"model",
            "sourceApplied\":true",
            "memoryPromoted\":true",
            "collectiveMemoryWritten\":true",
            "vectorAuthorityWritten\":true",
            "toolExecuted\":true",
            "memoryImprovementIsPromotion\":true",
            "memoryProposalIsPromotion\":true",
            "memorySafeIsApproval\":true",
            "candidateIsMemory\":true",
            "retrievalMatchIsMemoryCandidate\":true",
            "auditIsApproval\":true",
            "apiResponseStatusIsGovernance\":true",
            "modelOutputIsAuthority\":true"
        };

        foreach (var token in forbidden)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Response contained misleading authority language: {token}");
    }

    private static void AssertNoPrivateReasoningLeak(string text)
    {
        var forbidden = new[]
        {
            "PRIVATE_MARKER",
            "chain-of-thought",
            "hidden reasoning",
            "raw prompt",
            "scratchpad",
            "private reasoning"
        };

        foreach (var token in forbidden)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Response contained private reasoning marker: {token}");
    }

    private static async Task DropAgentRunAuditSchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("""
            IF OBJECT_ID('agent.TR_AgentRunAuditEnvelope_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentRunAuditEnvelope_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentRunAuditEnvelope', 'U') IS NOT NULL
                DROP TABLE agent.AgentRunAuditEnvelope;
            IF SCHEMA_ID('agent') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.objects WHERE schema_id = SCHEMA_ID('agent'))
                DROP SCHEMA agent;
            """);
    }

    private static async Task ApplyAgentRunAuditMigrationAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot(),
            "Database",
            "migrate_agent_run_audit_envelope.sql")));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private sealed record ManualMemoryImprovementApiRequestBody(
        int ProjectId,
        string SourceType,
        string SourceId,
        string Summary,
        string Content,
        IReadOnlyList<string> EvidenceRefs,
        string Context,
        string CandidateType,
        string CorrelationId)
    {
        [JsonIgnore]
        public Dictionary<string, object?> Extra { get; init; } = [];

        public Dictionary<string, object?> ToBody()
        {
            var body = new Dictionary<string, object?>
            {
                ["projectId"] = ProjectId,
                ["sourceType"] = SourceType,
                ["sourceId"] = SourceId,
                ["summary"] = Summary,
                ["content"] = Content,
                ["evidenceRefs"] = EvidenceRefs,
                ["context"] = Context,
                ["candidateType"] = CandidateType,
                ["correlationId"] = CorrelationId
            };

            foreach (var pair in Extra)
                body[pair.Key] = pair.Value;

            return body;
        }
    }
}
