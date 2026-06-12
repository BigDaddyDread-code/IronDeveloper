using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using IronDev.Core.Agents;
using IronDev.Core.Governance;
using IronDev.Data;

namespace IronDev.Api.Controllers;

public sealed class SqlToolGateApiStore : IToolGateApiStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IToolGateDecisionStore _decisionStore;
    private readonly IDbConnectionFactory _connectionFactory;

    public SqlToolGateApiStore(IToolGateDecisionStore decisionStore, IDbConnectionFactory connectionFactory)
    {
        _decisionStore = decisionStore;
        _connectionFactory = connectionFactory;
    }

    public ToolGateApiStoreSaveResult Save(ToolGateApiStoredDecision decision)
    {
        var tenantId = StableGuid($"tenant::{decision.TenantId}");
        var projectId = StableGuid($"project::{decision.TenantId}::{decision.ProjectId}");
        var durableDecisionId = StableGuid($"tool-gate::{decision.TenantId}::{decision.ProjectId}::{decision.Decision.GateDecisionId}");
        var durableToolRequestId = StableGuid($"tool-request::{decision.TenantId}::{decision.ProjectId}::{decision.Decision.ToolRequestId}");
        var durableEventId = StableGuid($"tool-gate-event::{decision.TenantId}::{decision.ProjectId}::{decision.Decision.GateDecisionId}");
        var existing = _decisionStore.GetAsync(tenantId, projectId, durableDecisionId).GetAwaiter().GetResult();
        if (existing is not null)
        {
            return new ToolGateApiStoreSaveResult { Created = false };
        }

        var evidence = BuildEvidence(decision);
        var evidenceJson = JsonSerializer.Serialize(evidence, JsonOptions);
        var correlationId = ToCorrelationGuid(decision.CorrelationId);
        var reasonCode = FirstReasonCode(decision);
        var storeDecision = MapDecision(decision.Decision.Decision);

        _decisionStore.RecordAsync(new ToolGateDecisionRecordRequest(
            tenantId,
            projectId,
            durableToolRequestId,
            storeDecision,
            "tool-request-gate",
            1,
            "system",
            "tool-gate-api-v1",
            reasonCode,
            evidenceJson,
            correlationId,
            null,
            decision.CreatedAtUtc == default ? DateTimeOffset.UtcNow : decision.CreatedAtUtc,
            durableDecisionId,
            durableEventId)).GetAwaiter().GetResult();

        return new ToolGateApiStoreSaveResult { Created = true };
    }

    public ToolGateApiStoredDecision? Get(string tenantId, string projectId, string gateDecisionId)
    {
        var durableDecisionId = StableGuid($"tool-gate::{tenantId}::{projectId}::{gateDecisionId}");
        var read = _decisionStore.GetAsync(
            StableGuid($"tenant::{tenantId}"),
            StableGuid($"project::{tenantId}::{projectId}"),
            durableDecisionId).GetAwaiter().GetResult();

        if (read is null)
        {
            return null;
        }

        var evidence = JsonSerializer.Deserialize<ToolGateDecisionEvidenceDocument>(read.EvidenceJson, JsonOptions);
        if (evidence is null)
        {
            return null;
        }

        return new ToolGateApiStoredDecision
        {
            TenantId = evidence.TenantId,
            ProjectId = evidence.ProjectId,
            ToolRequestRecord = evidence.ToolRequestRecord,
            Decision = new AgentToolExecutionGateDecision
            {
                GateDecisionId = evidence.PublicGateDecisionId,
                ToolRequestId = evidence.ToolRequestId,
                Decision = ParseEnum<AgentToolExecutionGateDecisionType>(evidence.GateDecision),
                ToolKind = ParseEnum<AgentToolKind>(evidence.ToolKind),
                RequestType = ParseEnum<AgentToolRequestType>(evidence.RequestType),
                RiskLevel = ParseEnum<AgentToolRiskLevel>(evidence.RiskLevel),
                EvaluatedAtUtc = evidence.EvaluatedAtUtc,
                Reasons = evidence.Reasons,
                Issues = evidence.Issues,
                GrantsExecution = false,
                ExecutesTool = false,
                MutatesSource = false,
                CallsExternalSystem = false,
                SubmitsGitHubReview = false,
                PersistsResult = false,
                PromotesMemory = false,
                CreatesCollectiveMemory = false,
                WritesWeaviate = false,
                RequiresExecutor = false
            },
            CallerEvidenceRefs = evidence.CallerEvidenceRefs,
            Reason = evidence.RequestReason,
            CorrelationId = evidence.CorrelationId,
            CreatedAtUtc = read.CreatedAtUtc,
            ContainsRawPrivateReasoning = false,
            Warnings = evidence.Warnings
        };
    }

    public int Count()
    {
        using var connection = _connectionFactory.CreateConnection();
        return connection.ExecuteScalar<int>("SELECT COUNT(1) FROM governance.ToolGateDecision");
    }

    private static ToolGateDecisionEvidenceDocument BuildEvidence(ToolGateApiStoredDecision decision) =>
        new()
        {
            SchemaVersion = 1,
            TenantId = decision.TenantId,
            ProjectId = decision.ProjectId,
            PublicGateDecisionId = decision.Decision.GateDecisionId,
            ToolRequestId = decision.Decision.ToolRequestId,
            ToolRequestRecord = decision.ToolRequestRecord,
            GateDecision = decision.Decision.Decision.ToString(),
            ToolKind = decision.Decision.ToolKind.ToString(),
            RequestType = decision.Decision.RequestType.ToString(),
            RiskLevel = decision.Decision.RiskLevel.ToString(),
            EvaluatedAtUtc = decision.Decision.EvaluatedAtUtc,
            Reasons = decision.Decision.Reasons,
            Issues = decision.Decision.Issues,
            CallerEvidenceRefs = decision.CallerEvidenceRefs,
            RequestReason = decision.Reason,
            CorrelationId = decision.CorrelationId,
            Warnings = BuildWarnings(decision.Warnings)
        };

    private static IReadOnlyList<string> BuildWarnings(IReadOnlyList<string> warnings)
    {
        var combined = new List<string>(warnings)
        {
            "Tool gate decision is durable SQL-backed evidence, not approval or execution permission.",
            "Gate decision does not execute tools, apply source changes, or promote memory."
        };

        return combined.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string MapDecision(AgentToolExecutionGateDecisionType decision) => decision switch
    {
        AgentToolExecutionGateDecisionType.Allowed => nameof(ToolGateDecisionValue.Passed),
        AgentToolExecutionGateDecisionType.Blocked => nameof(ToolGateDecisionValue.Blocked),
        AgentToolExecutionGateDecisionType.RequiresApproval => nameof(ToolGateDecisionValue.RequiresApproval),
        _ => nameof(ToolGateDecisionValue.Blocked)
    };

    private static string FirstReasonCode(ToolGateApiStoredDecision decision) =>
        decision.Decision.Issues.FirstOrDefault()?.Code
        ?? decision.Decision.Reasons.FirstOrDefault()?.Code
        ?? "TOOL_GATE_DECISION_RECORDED";

    private static Guid? ToCorrelationGuid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Guid.TryParse(value, out var parsed) ? parsed : StableGuid($"correlation::{value}");
    }

    private static Guid StableGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, guidBytes.Length);
        return new Guid(guidBytes);
    }

    private static TEnum ParseEnum<TEnum>(string value)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : default;

    private sealed record ToolGateDecisionEvidenceDocument
    {
        public int SchemaVersion { get; init; }
        public string TenantId { get; init; } = string.Empty;
        public string ProjectId { get; init; } = string.Empty;
        public string PublicGateDecisionId { get; init; } = string.Empty;
        public string ToolRequestId { get; init; } = string.Empty;
        public ToolRequestApiStoredRecord ToolRequestRecord { get; init; } = null!;
        public string GateDecision { get; init; } = string.Empty;
        public string ToolKind { get; init; } = string.Empty;
        public string RequestType { get; init; } = string.Empty;
        public string RiskLevel { get; init; } = string.Empty;
        public DateTimeOffset EvaluatedAtUtc { get; init; }
        public IReadOnlyList<AgentToolExecutionGateReason> Reasons { get; init; } = [];
        public IReadOnlyList<AgentToolExecutionGateIssue> Issues { get; init; } = [];
        public IReadOnlyList<string> CallerEvidenceRefs { get; init; } = [];
        public string RequestReason { get; init; } = string.Empty;
        public string CorrelationId { get; init; } = string.Empty;
        public IReadOnlyList<string> Warnings { get; init; } = [];
    }
}
