using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using IronDev.Core.Agents;
using IronDev.Core.Agents.Audit;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class AgentRunsApiContractTests : ApiTestBase
{
    private static readonly DateTimeOffset RunOneCreatedAt = new(2026, 6, 11, 1, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RunTwoCreatedAt = new(2026, 6, 11, 2, 0, 0, TimeSpan.Zero);

    [TestInitialize]
    public async Task AgentRunsInitialize()
    {
        await DropAgentRunAuditSchemaAsync();
        await ApplyAgentRunAuditMigrationAsync();
    }

    [TestCleanup]
    public async Task AgentRunsCleanup()
    {
        await DropAgentRunAuditSchemaAsync();
    }

    [TestMethod]
    public async Task AgentRunsApi_List_IsReadOnly()
    {
        await AppendEnvelopeAsync(BuildEnvelope("agent-run-list-1", 581, RunOneCreatedAt));
        await AppendEnvelopeAsync(BuildEnvelope("agent-run-list-2", 581, RunTwoCreatedAt));
        var before = await CountAuditRowsAsync();
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync("/api/v1/agent-runs?projectId=581&take=10&skip=0");
        var json = await ReadJsonAsync(response);
        var after = await CountAuditRowsAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual(before, after, "Read-only list endpoint must not append audit rows.");
        Assert.AreEqual("succeeded", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("humanApprovalRequired").GetBoolean());
        Assert.IsTrue(json.RootElement.GetProperty("boundary").GetProperty("readOnlyInspection").GetBoolean());
        Assert.AreEqual(2, json.RootElement.GetProperty("data").GetProperty("items").GetArrayLength());
        Assert.AreEqual("agent-run-list-2", json.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("agentRunId").GetString());
    }

    [TestMethod]
    public async Task AgentRunsApi_Detail_ReturnsExistingRunWithoutExecutionPermission()
    {
        await AppendEnvelopeAsync(BuildEnvelope("agent-run-detail-1", 582, RunOneCreatedAt));
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync("/api/v1/agent-runs/agent-run-detail-1?projectId=582");
        var json = await ReadJsonAsync(response);
        var responseText = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, responseText);
        Assert.AreEqual("agent-run-detail-1", json.RootElement.GetProperty("runId").GetString());
        Assert.AreEqual("agent-run-detail-1", json.RootElement.GetProperty("data").GetProperty("agentRunId").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("boundary").GetProperty("endpointAccessIsExecutionPermission").GetBoolean());
        AssertNoMisleadingAuthorityLanguage(responseText);
    }

    [TestMethod]
    public async Task AgentRunsApi_Audit_IsEvidenceNotApproval()
    {
        await AppendEnvelopeAsync(BuildEnvelope("agent-run-audit-1", 583, RunOneCreatedAt));
        var before = await CountAuditRowsAsync();
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync("/api/v1/agent-runs/agent-run-audit-1/audit?projectId=583");
        var json = await ReadJsonAsync(response);
        var after = await CountAuditRowsAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual(before, after, "Audit endpoint must not append audit rows.");
        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual("agent-run-audit-1", data.GetProperty("agentRunId").GetString());
        Assert.IsFalse(data.GetProperty("auditIsApproval").GetBoolean());
        Assert.IsFalse(data.GetProperty("evidenceIsPermission").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("boundary").GetProperty("auditIsApproval").GetBoolean());
        Assert.IsTrue(data.GetProperty("evidenceReferences").GetArrayLength() > 0);
    }

    [TestMethod]
    public async Task AgentRunsApi_UnknownRun_ReturnsNotFound()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync("/api/v1/agent-runs/missing-run?projectId=584");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual("not_found", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
    }

    [TestMethod]
    public async Task AgentRunsApi_InvalidProjectId_ReturnsValidationError()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync("/api/v1/agent-runs?projectId=0");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual("validation_error", json.RootElement.GetProperty("status").GetString());
        Assert.AreEqual("projectId", json.RootElement.GetProperty("errors")[0].GetProperty("field").GetString());
    }

    [TestMethod]
    public async Task AgentRunsApi_UnsupportedFilter_IsRejected()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync("/api/v1/agent-runs?projectId=585&surprise=true");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual("unsupported_filter", json.RootElement.GetProperty("errors")[0].GetProperty("category").GetString());
    }

    [TestMethod]
    public async Task AgentRunsApi_PageSize_IsBounded()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync("/api/v1/agent-runs?projectId=586&take=500");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
        Assert.IsTrue(json.RootElement.GetProperty("errors").EnumerateArray().Any(error =>
            error.GetProperty("code").GetString() == AgentRunAuditQueryService.TakeOutOfRange));
    }

    [TestMethod]
    public async Task AgentRunsApi_RejectsCrossProjectAccess()
    {
        await AppendEnvelopeAsync(BuildEnvelope("agent-run-project-587", 587, RunOneCreatedAt));
        using var client = await AuthedClientAsync();

        var detail = await client.GetAsync("/api/v1/agent-runs/agent-run-project-587?projectId=588");
        var list = await client.GetAsync("/api/v1/agent-runs?projectId=588");
        var listJson = await ReadJsonAsync(list);

        Assert.AreEqual(HttpStatusCode.NotFound, detail.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, list.StatusCode, listJson.RootElement.ToString());
        Assert.AreEqual(0, listJson.RootElement.GetProperty("data").GetProperty("items").GetArrayLength());
    }

    [TestMethod]
    public async Task AgentRunsApi_UnauthenticatedRequest_IsRejected()
    {
        var response = await Client.GetAsync("/api/v1/agent-runs?projectId=589");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task AgentRunsApi_DoesNotExposeHiddenReasoningOrAuthorityLanguage()
    {
        await AppendEnvelopeAsync(BuildEnvelope("agent-run-language-1", 590, RunOneCreatedAt));
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync("/api/v1/agent-runs/agent-run-language-1?projectId=590");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.IsFalse(text.Contains("chain-of-thought", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("hidden reasoning", StringComparison.OrdinalIgnoreCase));
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public async Task AgentRunsApi_DoesNotExposeRawPrivateReasoning_WhenAuditEnvelopeJsonContainsPrivateReasoning()
    {
        await InsertEnvelopeDirectlyAsync(BuildEnvelopeWithPrivateReasoning("agent-run-private-1", 591, RunOneCreatedAt));
        using var client = await AuthedClientAsync();

        var detail = await client.GetAsync("/api/v1/agent-runs/agent-run-private-1?projectId=591");
        var audit = await client.GetAsync("/api/v1/agent-runs/agent-run-private-1/audit?projectId=591");
        var detailText = await detail.Content.ReadAsStringAsync();
        var auditText = await audit.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, detail.StatusCode, detailText);
        Assert.AreEqual(HttpStatusCode.OK, audit.StatusCode, auditText);
        StringAssert.Contains(detailText, "[redacted: sensitive audit text]");
        AssertNoPrivateReasoningLeak(detailText);
        AssertNoPrivateReasoningLeak(auditText);
        AssertNoMisleadingAuthorityLanguage(detailText);
        AssertNoMisleadingAuthorityLanguage(auditText);
    }

    [TestMethod]
    public void AgentRunsApi_ControllerDoesNotExposeWriteExecutionOrPromotionPaths()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers", "AgentRunsV1Controller.cs"));

        var forbiddenTokens = new[]
        {
            "[HttpPost",
            "[HttpPut",
            "[HttpPatch",
            "[HttpDelete",
            "IAgentRunAuditEnvelopeStore",
            "IStoredManualIndependentCriticAgentService",
            "IStoredManualMemoryImprovementAgentService",
            "ManualTesterAgentToolExecutionService",
            "ToolExecutionAuditStore",
            "ApplyCopy",
            "PromoteCollectiveMemory",
            "ProcessStartInfo",
            "File.Copy",
            "File.Delete"
        };

        foreach (var token in forbiddenTokens)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found in read-only controller: {token}");
    }

    private static async Task<HttpClient> AuthedClientAsync()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        return GetAuthedClient(tenantToken);
    }

    private static async Task AppendEnvelopeAsync(AgentRunAuditEnvelope envelope)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAgentRunAuditEnvelopeStore>();
        var result = store.Append(envelope, DateTimeOffset.UtcNow);

        Assert.IsTrue(
            result.Status is AgentRunAuditEnvelopeAppendStatus.Appended or AgentRunAuditEnvelopeAppendStatus.AlreadyExists,
            string.Join(Environment.NewLine, result.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
    }

    private static AgentRunAuditEnvelope BuildEnvelope(string agentRunId, int projectId, DateTimeOffset createdAt)
    {
        var agentDefinition = AgentDefinitionCatalog.IndependentCriticAgent;

        return new AgentRunAuditEnvelope
        {
            Run = new AgentRunRecord
            {
                AgentRunId = agentRunId,
                TenantId = AssignedTenantId.ToString(),
                ProjectId = projectId.ToString(),
                CampaignId = "campaign-pr58",
                RunId = $"correlation-{agentRunId}",
                AgentId = agentDefinition.AgentId,
                AgentName = agentDefinition.Name,
                Status = IronDev.Core.Agents.Audit.AgentRunStatus.Completed,
                TriggerType = AgentRunTriggerType.ManualUserRequest,
                RequestedByUserId = "user-1",
                RequestSummary = "Inspect the existing audit envelope through API v1.",
                Purpose = "Read-only API inspection contract test.",
                CreatedAtUtc = createdAt,
                StartedAtUtc = createdAt.AddMinutes(1),
                CompletedAtUtc = createdAt.AddMinutes(2)
            },
            AgentDefinitionSnapshot = agentDefinition,
            Inputs =
            [
                new AgentRunInputRef
                {
                    InputRefId = $"input-{agentRunId}",
                    AgentRunId = agentRunId,
                    RefType = "AgentRunAuditEnvelope",
                    RefId = $"input-ref-{agentRunId}",
                    Source = "api contract test",
                    Summary = "Safe public input summary for inspection.",
                    Sha256 = new string('a', 64),
                    IsAuthoritativeForAction = false,
                    ContainsRawPrivateReasoning = false
                }
            ],
            Outputs =
            [
                new AgentRunOutputRef
                {
                    OutputRefId = $"output-{agentRunId}",
                    AgentRunId = agentRunId,
                    RefType = "CriticReviewResult",
                    RefId = $"review-{agentRunId}",
                    Summary = "Review-only output summary for inspection.",
                    EvidenceRefs = [$"evidence-{agentRunId}"],
                    IsReviewOnly = true,
                    IsProposalOnly = false,
                    CreatesAuthority = false,
                    CreatesRuntimeAction = false,
                    ContainsRawPrivateReasoning = false
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
                    Summary = "CreateReport capability was used for review-only output.",
                    BoundaryDecisionId = $"boundary-{agentRunId}",
                    EvidenceRef = $"evidence-{agentRunId}",
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
                    Reason = "Output is review-only and grants no authority.",
                    SourceRefId = $"output-{agentRunId}",
                    EvidenceRefs = [$"evidence-{agentRunId}"],
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
                    Summary = "Evidence was inspected through safe public summary only.",
                    EvidenceRefs = [$"evidence-{agentRunId}"],
                    ContainsRawPrivateReasoning = false,
                    GrantsAuthority = false,
                    GrantsApproval = false,
                    GrantsMemoryPromotion = false,
                    RecordedAtUtc = createdAt.AddMinutes(2)
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
                    OccurredAtUtc = createdAt.AddMinutes(2),
                    Summary = "Manual review run completed before API inspection.",
                    ContainsRawPrivateReasoning = false,
                    EvidenceRefs = [$"evidence-{agentRunId}"]
                }
            ]
        };
    }

    private static AgentRunAuditEnvelope BuildEnvelopeWithPrivateReasoning(string agentRunId, int projectId, DateTimeOffset createdAt)
    {
        const string privateText = "PRIVATE_MARKER chain-of-thought hidden reasoning raw prompt scratchpad private reasoning";
        var envelope = BuildEnvelope(agentRunId, projectId, createdAt);

        return envelope with
        {
            Run = envelope.Run with
            {
                RequestSummary = privateText,
                Purpose = privateText
            },
            Inputs =
            [
                envelope.Inputs[0] with
                {
                    Source = privateText,
                    Summary = privateText,
                    ContainsRawPrivateReasoning = true
                }
            ],
            Outputs =
            [
                envelope.Outputs[0] with
                {
                    Summary = privateText,
                    EvidenceRefs = [privateText],
                    ContainsRawPrivateReasoning = true
                }
            ],
            CapabilityUses =
            [
                envelope.CapabilityUses[0] with
                {
                    Summary = privateText,
                    BoundaryDecisionId = $"boundary-{agentRunId}",
                    EvidenceRef = privateText
                }
            ],
            BoundaryDecisions =
            [
                envelope.BoundaryDecisions[0] with
                {
                    Reason = privateText,
                    SourceRefId = privateText,
                    EvidenceRefs = [privateText]
                }
            ],
            ThoughtLedger =
            [
                envelope.ThoughtLedger[0] with
                {
                    Summary = privateText,
                    EvidenceRefs = [privateText],
                    Assumptions = [privateText],
                    RejectedAlternatives = [privateText],
                    Risks = [privateText],
                    RequiredFollowUps = [privateText],
                    ContainsRawPrivateReasoning = true
                }
            ],
            Steps =
            [
                envelope.Steps[0] with
                {
                    Summary = privateText,
                    EvidenceRefs = [privateText],
                    ContainsRawPrivateReasoning = true
                }
            ]
        };
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    private static void AssertNoMisleadingAuthorityLanguage(string text)
    {
        var forbidden = new[]
        {
            "request approved",
            "source applied",
            "memory promoted",
            "critic governed",
            "audit approved",
            "model permitted",
            "governedByCritic",
            "authority\":\"model"
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
                EnvelopeSha256 = new string('d', 64),
                EnvelopeJson = json,
                AppendedAtUtc = DateTimeOffset.UtcNow.UtcDateTime
            });
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
}



