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
public sealed class ManualCriticApiContractTests : ApiTestBase
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 6, 11, 4, 0, 0, TimeSpan.Zero);

    [TestInitialize]
    public async Task ManualCriticInitialize()
    {
        await DropAgentRunAuditSchemaAsync();
        await ApplyAgentRunAuditMigrationAsync();
    }

    [TestCleanup]
    public async Task ManualCriticCleanup()
    {
        await DropAgentRunAuditSchemaAsync();
    }

    [TestMethod]
    public async Task ManualCriticApi_CreateReview_CreatesAdvisoryEvidenceOnly()
    {
        using var client = await AuthedClientAsync();
        var before = await CountAuditRowsAsync();

        var response = await client.PostAsJsonAsync("/api/v1/manual-critic/reviews", ValidRequest(601, "create-advisory"));
        var json = await ReadJsonAsync(response);
        var after = await CountAuditRowsAsync();
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual(before + 1, after, "POST may append only the stored manual critic audit envelope.");
        Assert.AreEqual("succeeded", json.RootElement.GetProperty("status").GetString());
        Assert.IsTrue(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
        Assert.IsTrue(json.RootElement.GetProperty("humanApprovalRequired").GetBoolean());
        Assert.AreEqual("manual-independent-critic-601-ticket-ticket-601-create-advisory", json.RootElement.GetProperty("runId").GetString());

        var boundary = json.RootElement.GetProperty("boundary");
        Assert.IsFalse(boundary.GetProperty("criticIsGovernance").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("criticIsApproval").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("auditIsApproval").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("sourceApplied").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("memoryPromoted").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("toolExecuted").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("modelOutputIsAuthority").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("endpointAccessIsExecutionPermission").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequiredForSourceApply").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequiredForMemoryPromotion").GetBoolean());

        var data = json.RootElement.GetProperty("data");
        Assert.IsTrue(data.GetProperty("advisoryOnly").GetBoolean());
        Assert.IsTrue(data.GetProperty("requiresHumanReview").GetBoolean());
        Assert.AreEqual(1, data.GetProperty("findingCount").GetInt32());
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public async Task ManualCriticApi_GetReview_IsReadOnlyInspection()
    {
        using var client = await AuthedClientAsync();
        var create = await client.PostAsJsonAsync("/api/v1/manual-critic/reviews", ValidRequest(602, "get-readonly"));
        var createJson = await ReadJsonAsync(create);
        var runId = createJson.RootElement.GetProperty("runId").GetString();
        var before = await CountAuditRowsAsync();

        var response = await client.GetAsync($"/api/v1/manual-critic/reviews/{runId}?projectId=602");
        var json = await ReadJsonAsync(response);
        var after = await CountAuditRowsAsync();
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual(before, after, "GET must not append audit rows.");
        Assert.AreEqual("succeeded", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
        Assert.IsTrue(json.RootElement.GetProperty("data").GetProperty("advisoryOnly").GetBoolean());
        Assert.IsTrue(json.RootElement.GetProperty("data").GetProperty("reviewOnlyOutput").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("data").GetProperty("createsAuthority").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("data").GetProperty("createsRuntimeAction").GetBoolean());
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public async Task ManualCriticApi_UnknownReview_ReturnsNotFound()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync("/api/v1/manual-critic/reviews/missing-run?projectId=603");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual("not_found", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
    }

    [TestMethod]
    public async Task ManualCriticApi_RejectsCrossProjectAccess()
    {
        using var client = await AuthedClientAsync();
        var create = await client.PostAsJsonAsync("/api/v1/manual-critic/reviews", ValidRequest(604, "cross-project"));
        var createJson = await ReadJsonAsync(create);
        var runId = createJson.RootElement.GetProperty("runId").GetString();

        var response = await client.GetAsync($"/api/v1/manual-critic/reviews/{runId}?projectId=605");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task ManualCriticApi_RejectsUnsupportedAuthorityFields()
    {
        using var client = await AuthedClientAsync();
        var before = await CountAuditRowsAsync();
        var request = ValidRequest(606, "unsupported-authority") with
        {
            Extra = new Dictionary<string, object?>
            {
                ["approved"] = true,
                ["sourceApplied"] = true
            }
        };

        var response = await client.PostAsJsonAsync("/api/v1/manual-critic/reviews", request.ToBody());
        var json = await ReadJsonAsync(response);
        var after = await CountAuditRowsAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual(before, after, "Rejected requests must not append audit rows.");
        Assert.AreEqual("validation_error", json.RootElement.GetProperty("status").GetString());
        Assert.IsTrue(json.RootElement.GetProperty("errors").EnumerateArray().Any(error =>
            error.GetProperty("category").GetString() == "unsupported_field"));
    }

    [TestMethod]
    public async Task ManualCriticApi_RejectsOversizedContent()
    {
        using var client = await AuthedClientAsync();
        var request = ValidRequest(607, "oversized") with
        {
            Content = new string('x', 12_001)
        };

        var response = await client.PostAsJsonAsync("/api/v1/manual-critic/reviews", request.ToBody());
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual("validation_error", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
    }

    [TestMethod]
    public async Task ManualCriticApi_RejectsHiddenReasoning()
    {
        using var client = await AuthedClientAsync();
        var before = await CountAuditRowsAsync();
        var request = ValidRequest(608, "hidden-reasoning") with
        {
            Content = "chain-of-thought PRIVATE_MARKER should never enter the manual critic API."
        };

        var response = await client.PostAsJsonAsync("/api/v1/manual-critic/reviews", request.ToBody());
        var json = await ReadJsonAsync(response);
        var after = await CountAuditRowsAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual(before, after, "Hidden reasoning rejection must happen before audit append.");
        AssertNoPrivateReasoningLeak(json.RootElement.ToString());
    }

    [TestMethod]
    public async Task ManualCriticApi_DoesNotExposeHiddenReasoning_WhenStoredAuditContainsPrivateReasoning()
    {
        await InsertEnvelopeDirectlyAsync(BuildEnvelopeWithPrivateReasoning("manual-independent-critic-private-1", 609));
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync("/api/v1/manual-critic/reviews/manual-independent-critic-private-1?projectId=609");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        StringAssert.Contains(text, "[redacted: sensitive critic audit text]");
        AssertNoPrivateReasoningLeak(text);
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public async Task ManualCriticApi_UnauthenticatedRequestsAreRejected()
    {
        var get = await Client.GetAsync("/api/v1/manual-critic/reviews/missing?projectId=610");
        var post = await Client.PostAsJsonAsync("/api/v1/manual-critic/reviews", ValidRequest(610, "unauthenticated").ToBody());

        Assert.AreEqual(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, post.StatusCode);
    }

    [TestMethod]
    public async Task ManualCriticApi_DoesNotCreateApprovalApplyPromotionOrToolExecution()
    {
        using var client = await AuthedClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/manual-critic/reviews", ValidRequest(611, "boundary-proof"));
        var text = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(text);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        var boundary = json.RootElement.GetProperty("boundary");
        Assert.IsFalse(boundary.GetProperty("criticIsApproval").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("criticIsGovernance").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("auditIsApproval").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("sourceApplied").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("memoryPromoted").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("toolExecuted").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("modelOutputIsAuthority").GetBoolean());
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public void ManualCriticApi_ControllerDoesNotReferenceForbiddenServices()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers", "ManualCriticReviewsV1Controller.cs"));

        var forbiddenTokens = new[]
        {
            "[HttpPut",
            "[HttpPatch",
            "[HttpDelete",
            "IStoredManualMemoryImprovementAgentService",
            "IManualMemoryImprovementAgentService",
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
            "DELETE FROM"
        };

        foreach (var token in forbiddenTokens)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found in manual critic API controller: {token}");

        StringAssert.Contains(text, "IStoredManualIndependentCriticAgentService");
        StringAssert.Contains(text, "IAgentRunAuditQueryService");
    }

    [TestMethod]
    public void ManualCriticApi_DocumentationPreservesBoundaries()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "API_MANUAL_CRITIC_V1.md"));

        StringAssert.Contains(text, "Manual Critic API v1");
        StringAssert.Contains(text, "POST `/api/v1/manual-critic/reviews`");
        StringAssert.Contains(text, "GET `/api/v1/manual-critic/reviews/{agentRunId}?projectId={projectId}`");
        StringAssert.Contains(text, "Critic review is advisory only.");
        StringAssert.Contains(text, "Critic review is not governance.");
        StringAssert.Contains(text, "Critic review is not approval.");
        StringAssert.Contains(text, "Audit evidence is not approval.");
        StringAssert.Contains(text, "API access is not execution permission.");
        StringAssert.Contains(text, "Human review remains required for source apply.");
        StringAssert.Contains(text, "Human review remains required for memory promotion.");
    }

    private static async Task<HttpClient> AuthedClientAsync()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        return GetAuthedClient(tenantToken);
    }

    private static ManualCriticApiRequestBody ValidRequest(int projectId, string correlationId) =>
        new(
            projectId,
            "Ticket",
            $"ticket-{projectId}",
            "Review ticket acceptance criteria",
            "Acceptance criteria need clearer validation evidence before humans rely on this review.",
            [$"ticket-{projectId}", $"evidence-{projectId}"],
            "This is public ticket context only.",
            "Medium",
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
        var agentDefinition = AgentDefinitionCatalog.IndependentCriticAgent;

        return new AgentRunAuditEnvelope
        {
            Run = new AgentRunRecord
            {
                AgentRunId = agentRunId,
                TenantId = AssignedTenantId.ToString(),
                ProjectId = projectId.ToString(),
                CampaignId = "campaign-pr59",
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
                    RefType = "Ticket",
                    RefId = "ticket-private",
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
                    OutputRefId = $"output-{agentRunId}",
                    AgentRunId = agentRunId,
                    RefType = "CriticReviewResult",
                    RefId = $"critic-review-{agentRunId}",
                    Summary = privateText,
                    EvidenceRefs = [privateText],
                    IsReviewOnly = true,
                    IsProposalOnly = false,
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
                    Capability = AgentCapability.CreateReport,
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

    private static void AssertNoMisleadingAuthorityLanguage(string text)
    {
        var forbidden = new[]
        {
            "approved\":true",
            "governed\":true",
            "applied\":true",
            "promoted\":true",
            "executionPermitted\":true",
            "authority\":\"critic",
            "authority\":\"model",
            "sourceApplied\":true",
            "memoryPromoted\":true",
            "toolExecuted\":true",
            "criticIsGovernance\":true",
            "criticIsApproval\":true",
            "auditIsApproval\":true",
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

    private sealed record ManualCriticApiRequestBody(
        int ProjectId,
        string SubjectType,
        string SubjectId,
        string Summary,
        string Content,
        IReadOnlyList<string> EvidenceRefs,
        string Context,
        string SeverityHint,
        string CorrelationId)
    {
        [JsonIgnore]
        public Dictionary<string, object?> Extra { get; init; } = [];

        public Dictionary<string, object?> ToBody()
        {
            var body = new Dictionary<string, object?>
            {
                ["projectId"] = ProjectId,
                ["subjectType"] = SubjectType,
                ["subjectId"] = SubjectId,
                ["summary"] = Summary,
                ["content"] = Content,
                ["evidenceRefs"] = EvidenceRefs,
                ["context"] = Context,
                ["severityHint"] = SeverityHint,
                ["correlationId"] = CorrelationId
            };

            foreach (var pair in Extra)
                body[pair.Key] = pair.Value;

            return body;
        }
    }
}
