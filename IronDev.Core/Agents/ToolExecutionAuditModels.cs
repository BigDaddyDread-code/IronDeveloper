using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Agents.Concrete;

namespace IronDev.Core.Agents.Audit;

public enum ToolExecutionAuditAppendStatus
{
    Appended = 1,
    AlreadyExists = 2,
    Conflict = 3,
    Rejected = 4
}

public sealed record ToolExecutionAuditScope
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public string? CampaignId { get; init; }
    public string? RunId { get; init; }
}

public sealed record ToolExecutionAuditActor
{
    public required AgentKind AgentKind { get; init; }
    public required string AgentId { get; init; }
    public required string AgentName { get; init; }
}

public sealed record ToolExecutionAuditTool
{
    public required string ToolRequestId { get; init; }
    public required AgentToolKind ToolKind { get; init; }
    public required AgentToolRequestType RequestType { get; init; }
}

public sealed record ToolExecutionAuditGate
{
    public required string GateDecisionId { get; init; }
}

public sealed record ToolExecutionAuditOutcome
{
    public required string Status { get; init; }
    public required bool Succeeded { get; init; }
}

public sealed record ToolExecutionAuditEvidence
{
    public required string EvidenceRef { get; init; }
}

public sealed record ToolExecutionAuditPayload
{
    public required string PayloadKind { get; init; }
    public required string PayloadJson { get; init; }
    public required string PayloadSha256 { get; init; }
}

public sealed record ToolExecutionAuditRecord
{
    public required string ToolExecutionAuditId { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public string? CampaignId { get; init; }
    public string? RunId { get; init; }
    public required string AgentRunId { get; init; }
    public required string ManualExecutionId { get; init; }
    public required string ToolRequestId { get; init; }
    public required string GateDecisionId { get; init; }
    public required AgentToolKind ToolKind { get; init; }
    public required AgentToolRequestType RequestType { get; init; }
    public required AgentKind AgentKind { get; init; }
    public required string AgentId { get; init; }
    public required string AgentName { get; init; }
    public required string Status { get; init; }
    public required bool Succeeded { get; init; }
    public required string PayloadKind { get; init; }
    public required string PayloadJson { get; init; }
    public required string PayloadSha256 { get; init; }
    public required string AuditEnvelopeJson { get; init; }
    public required string AuditEnvelopeSha256 { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public bool ContainsRawPrivateReasoning { get; init; }
    public bool ContainsSecret { get; init; }
    public bool ClaimsApproval { get; init; }
    public bool ClaimsPolicyApproval { get; init; }
    public bool ClaimsHumanApproval { get; init; }
    public bool ClaimsMemoryPromotion { get; init; }
    public bool ExecutesTool { get; init; }
    public bool MutatesSource { get; init; }
    public bool AppliesPatch { get; init; }
    public bool WritesFiles { get; init; }
    public bool DeletesFiles { get; init; }
    public bool RunsGit { get; init; }
    public bool CallsExternalSystem { get; init; }
    public bool SubmitsGitHubReview { get; init; }
    public bool CreatesPullRequest { get; init; }
    public bool PromotesMemory { get; init; }
    public bool CreatesCollectiveMemory { get; init; }
    public bool WritesWeaviate { get; init; }
}

public sealed record ToolExecutionAuditAppendRequest
{
    public required ToolExecutionAuditRecord Record { get; init; }
}

public sealed record ToolExecutionAuditIssue
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

public sealed record ToolExecutionAuditAppendResult
{
    public required ToolExecutionAuditAppendStatus Status { get; init; }
    public string ToolExecutionAuditId { get; init; } = string.Empty;
    public string PayloadSha256 { get; init; } = string.Empty;
    public string AuditEnvelopeSha256 { get; init; } = string.Empty;
    public IReadOnlyList<ToolExecutionAuditIssue> Issues { get; init; } = [];
}

public sealed record ToolExecutionAuditQuery
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string ToolExecutionAuditId { get; init; }
}

public sealed record ToolExecutionAuditReadResult
{
    public required bool Found { get; init; }
    public ToolExecutionAuditRecord? Record { get; init; }
    public IReadOnlyList<ToolExecutionAuditIssue> Issues { get; init; } = [];
}

public sealed record ToolExecutionAuditRunQuery
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string RunId { get; init; }
    public int Take { get; init; } = 100;
}

public interface IToolExecutionAuditStore
{
    Task<ToolExecutionAuditAppendResult> AppendAsync(ToolExecutionAuditAppendRequest request, CancellationToken cancellationToken = default);

    Task<ToolExecutionAuditReadResult> GetAsync(ToolExecutionAuditQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ToolExecutionAuditRecord>> ListByRunAsync(ToolExecutionAuditRunQuery query, CancellationToken cancellationToken = default);
}

public sealed class ToolExecutionAuditValidator
{
    public const string ToolAuditIdRequired = "TOOL_AUDIT_ID_REQUIRED";
    public const string ToolAuditScopeRequired = "TOOL_AUDIT_SCOPE_REQUIRED";
    public const string ToolAuditAgentRequired = "TOOL_AUDIT_AGENT_REQUIRED";
    public const string ToolAuditToolRequestRequired = "TOOL_AUDIT_TOOL_REQUEST_REQUIRED";
    public const string ToolAuditGateRequired = "TOOL_AUDIT_GATE_REQUIRED";
    public const string ToolAuditPayloadRequired = "TOOL_AUDIT_PAYLOAD_REQUIRED";
    public const string ToolAuditPayloadKindInvalid = "TOOL_AUDIT_PAYLOAD_KIND_INVALID";
    public const string ToolAuditToolKindInvalid = "TOOL_AUDIT_TOOL_KIND_INVALID";
    public const string ToolAuditRequestTypeInvalid = "TOOL_AUDIT_REQUEST_TYPE_INVALID";
    public const string ToolAuditToolRequestMismatch = "TOOL_AUDIT_TOOL_REQUEST_MISMATCH";
    public const string ToolAuditHashRequired = "TOOL_AUDIT_HASH_REQUIRED";
    public const string ToolAuditHashInvalid = "TOOL_AUDIT_HASH_INVALID";
    public const string ToolAuditEvidenceRequired = "TOOL_AUDIT_EVIDENCE_REQUIRED";
    public const string ToolAuditRawReasoningBlocked = "TOOL_AUDIT_RAW_REASONING_BLOCKED";
    public const string ToolAuditSecretBlocked = "TOOL_AUDIT_SECRET_BLOCKED";
    public const string ToolAuditApprovalClaimBlocked = "TOOL_AUDIT_APPROVAL_CLAIM_BLOCKED";
    public const string ToolAuditMemoryPromotionClaimBlocked = "TOOL_AUDIT_MEMORY_PROMOTION_CLAIM_BLOCKED";
    public const string ToolAuditUnsafeEffectBlocked = "TOOL_AUDIT_UNSAFE_EFFECT_BLOCKED";
    public const string ToolAuditPayloadTextUnsafe = "TOOL_AUDIT_PAYLOAD_TEXT_UNSAFE";
    public const string ToolAuditEnvelopeTextUnsafe = "TOOL_AUDIT_ENVELOPE_TEXT_UNSAFE";
    public const string ToolAuditStoreAppendFailed = "TOOL_AUDIT_STORE_APPEND_FAILED";
    public const string ToolAuditStoreConflict = "TOOL_AUDIT_STORE_CONFLICT";

    private const string SeverityError = "error";

    public IReadOnlyList<ToolExecutionAuditIssue> Validate(ToolExecutionAuditRecord? record)
    {
        var issues = new List<ToolExecutionAuditIssue>();
        if (record is null)
        {
            issues.Add(Issue(ToolAuditPayloadRequired, "Tool execution audit record is required.", "Record"));
            return issues;
        }

        Require(record.ToolExecutionAuditId, ToolAuditIdRequired, "ToolExecutionAuditId", "Tool execution audit ID is required.", issues);
        Require(record.TenantId, ToolAuditScopeRequired, "TenantId", "Tenant ID is required.", issues);
        Require(record.ProjectId, ToolAuditScopeRequired, "ProjectId", "Project ID is required.", issues);
        Require(record.AgentRunId, ToolAuditAgentRequired, "AgentRunId", "Agent run ID is required.", issues);
        Require(record.ManualExecutionId, ToolAuditAgentRequired, "ManualExecutionId", "Manual execution ID is required.", issues);
        Require(record.AgentId, ToolAuditAgentRequired, "AgentId", "Agent ID is required.", issues);
        Require(record.AgentName, ToolAuditAgentRequired, "AgentName", "Agent name is required.", issues);
        Require(record.ToolRequestId, ToolAuditToolRequestRequired, "ToolRequestId", "Tool request ID is required.", issues);
        Require(record.GateDecisionId, ToolAuditGateRequired, "GateDecisionId", "Gate decision ID is required.", issues);
        Require(record.PayloadJson, ToolAuditPayloadRequired, "PayloadJson", "Payload JSON is required.", issues);
        Require(record.AuditEnvelopeJson, ToolAuditPayloadRequired, "AuditEnvelopeJson", "Audit envelope JSON is required.", issues);

        if (!IsSupportedPayloadKind(record.PayloadKind))
            issues.Add(Issue(ToolAuditPayloadKindInvalid, $"Unsupported tool audit payload kind: {record.PayloadKind}.", "PayloadKind"));

        if (record.ToolKind is not AgentToolKind.TestRun and not AgentToolKind.PatchProposal)
            issues.Add(Issue(ToolAuditToolKindInvalid, $"Unsupported tool kind for audit storage: {record.ToolKind}.", "ToolKind"));

        if (record.RequestType is not AgentToolRequestType.TestExecutionRequest and not AgentToolRequestType.PatchProposalRequest)
            issues.Add(Issue(ToolAuditRequestTypeInvalid, $"Unsupported request type for audit storage: {record.RequestType}.", "RequestType"));

        if (!IsToolRequestShapeSupported(record))
            issues.Add(Issue(ToolAuditToolRequestMismatch, "Tool audit record must match supported tool kind, request type, agent kind, and payload kind.", "ToolKind"));

        ValidateHash(record.PayloadSha256, record.PayloadJson, "PayloadSha256", issues);
        ValidateHash(record.AuditEnvelopeSha256, record.AuditEnvelopeJson, "AuditEnvelopeSha256", issues);

        if (record.EvidenceRefs.Count == 0 || record.EvidenceRefs.Any(string.IsNullOrWhiteSpace))
            issues.Add(Issue(ToolAuditEvidenceRequired, "At least one non-empty evidence reference is required.", "EvidenceRefs"));

        if (record.ContainsRawPrivateReasoning)
            issues.Add(Issue(ToolAuditRawReasoningBlocked, "Tool audit record cannot contain raw private reasoning.", "ContainsRawPrivateReasoning"));

        if (record.ContainsSecret)
            issues.Add(Issue(ToolAuditSecretBlocked, "Tool audit record cannot contain secrets.", "ContainsSecret"));

        if (record.ClaimsApproval || record.ClaimsPolicyApproval || record.ClaimsHumanApproval)
            issues.Add(Issue(ToolAuditApprovalClaimBlocked, "Tool audit record cannot claim approval authority.", "ClaimsApproval"));

        if (record.ClaimsMemoryPromotion || record.PromotesMemory || record.CreatesCollectiveMemory)
            issues.Add(Issue(ToolAuditMemoryPromotionClaimBlocked, "Tool audit record cannot claim memory promotion.", "ClaimsMemoryPromotion"));

        if (record.ExecutesTool ||
            record.MutatesSource ||
            record.AppliesPatch ||
            record.WritesFiles ||
            record.DeletesFiles ||
            record.RunsGit ||
            record.CallsExternalSystem ||
            record.SubmitsGitHubReview ||
            record.CreatesPullRequest ||
            record.WritesWeaviate)
        {
            issues.Add(Issue(ToolAuditUnsafeEffectBlocked, "Tool audit record cannot claim unsafe execution effects.", "UnsafeFlags"));
        }

        if (ContainsUnsafeText(record.PayloadJson))
            issues.Add(Issue(ToolAuditPayloadTextUnsafe, "Tool audit payload text contains unsafe authority, raw-reasoning, secret, or promotion markers.", "PayloadJson"));

        if (ContainsUnsafeText(record.AuditEnvelopeJson))
            issues.Add(Issue(ToolAuditEnvelopeTextUnsafe, "Tool audit envelope text contains unsafe authority, raw-reasoning, secret, or promotion markers.", "AuditEnvelopeJson"));

        return issues;
    }

    private static bool IsToolRequestShapeSupported(ToolExecutionAuditRecord record) =>
        record switch
        {
            {
                ToolKind: AgentToolKind.TestRun,
                RequestType: AgentToolRequestType.TestExecutionRequest,
                AgentKind: AgentKind.TestingAgent,
                PayloadKind: ToolExecutionAuditRecordFactory.ManualTesterPayloadKind
            } => true,
            {
                ToolKind: AgentToolKind.PatchProposal,
                RequestType: AgentToolRequestType.PatchProposalRequest,
                AgentKind: AgentKind.ImplementationAgent,
                PayloadKind: ToolExecutionAuditRecordFactory.ManualImplementationPatchProposalPayloadKind
            } => true,
            _ => false
        };

    private static bool IsSupportedPayloadKind(string payloadKind) =>
        string.Equals(payloadKind, ToolExecutionAuditRecordFactory.ManualTesterPayloadKind, StringComparison.Ordinal) ||
        string.Equals(payloadKind, ToolExecutionAuditRecordFactory.ManualImplementationPatchProposalPayloadKind, StringComparison.Ordinal);

    private static void ValidateHash(
        string hash,
        string content,
        string field,
        List<ToolExecutionAuditIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            issues.Add(Issue(ToolAuditHashRequired, $"{field} is required.", field));
            return;
        }

        if (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c)) || hash.Any(char.IsUpper))
        {
            issues.Add(Issue(ToolAuditHashInvalid, $"{field} must be a lowercase SHA-256 hex string.", field));
            return;
        }

        if (!string.IsNullOrWhiteSpace(content) &&
            !string.Equals(hash, ToolExecutionAuditRecordFactory.Sha256(content), StringComparison.Ordinal))
        {
            issues.Add(Issue(ToolAuditHashInvalid, $"{field} does not match the current content.", field));
        }
    }

    private static bool ContainsUnsafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.ToLowerInvariant();
        return UnsafeMarkers.Any(text.Contains);
    }

    private static readonly string[] UnsafeMarkers =
    [
        "raw prompt:",
        "rawprompt:",
        "raw completion:",
        "rawcompletion:",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "scratchpad:",
        "scratch pad:",
        "private reasoning:",
        "privatereasoning:",
        "raw private reasoning:",
        "hidden reasoning:",
        "hidden deliberation:",
        "grant authority",
        "grants authority",
        "authoritative for action",
        "bypass governance",
        "override policy",
        "promote memory",
        "promoted memory",
        "accepted memory",
        "create collectivememory",
        "creates collectivememory",
        "secret=",
        "api_key",
        "apikey",
        "password="
    ];

    private static void Require(
        string? value,
        string code,
        string field,
        string message,
        List<ToolExecutionAuditIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add(Issue(code, message, field));
    }

    public static ToolExecutionAuditIssue Issue(
        string code,
        string message,
        string field = "") =>
        new()
        {
            Code = code,
            Severity = SeverityError,
            Message = message,
            Field = field
        };
}

public static class ToolExecutionAuditRecordFactory
{
    public const string ManualTesterPayloadKind = "ManualTesterAgentToolExecution";
    public const string ManualImplementationPatchProposalPayloadKind = "ManualImplementationPatchProposal";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    static ToolExecutionAuditRecordFactory()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public static ToolExecutionAuditRecord FromManualTesterResult(
        ManualTesterAgentToolExecutionResult result,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Output is null || result.AuditEnvelope is null)
            throw new ArgumentException("Tester result must include output and audit envelope.", nameof(result));

        if (result.Status is not ManualTesterAgentToolExecutionStatus.Succeeded and not ManualTesterAgentToolExecutionStatus.Failed)
            throw new ArgumentException("Only completed tester execution results can be persisted.", nameof(result));

        if (HasUnsafeTesterOutput(result.Output))
            throw new ArgumentException("Tester result output contains unsafe flags.", nameof(result));

        var envelope = result.AuditEnvelope;
        var payloadJson = Serialize(result);
        var envelopeJson = Serialize(envelope);
        var evidenceRefs = CollectEvidenceRefs(
            result.Output.EvidenceRefs,
            envelope.Outputs.SelectMany(output => output.EvidenceRefs),
            envelope.Steps.SelectMany(step => step.EvidenceRefs),
            envelope.BoundaryDecisions.SelectMany(decision => decision.EvidenceRefs),
            envelope.ThoughtLedger.SelectMany(entry => entry.EvidenceRefs));

        return new ToolExecutionAuditRecord
        {
            ToolExecutionAuditId = $"tool-audit-{result.ManualExecutionId}",
            TenantId = envelope.Run.TenantId,
            ProjectId = envelope.Run.ProjectId,
            CampaignId = envelope.Run.CampaignId,
            RunId = envelope.Run.RunId,
            AgentRunId = envelope.Run.AgentRunId,
            ManualExecutionId = result.ManualExecutionId,
            ToolRequestId = RequireValue(result.ToolRequestId, nameof(result.ToolRequestId)),
            GateDecisionId = RequireValue(result.GateDecisionId, nameof(result.GateDecisionId)),
            ToolKind = AgentToolKind.TestRun,
            RequestType = AgentToolRequestType.TestExecutionRequest,
            AgentKind = AgentKind.TestingAgent,
            AgentId = envelope.Run.AgentId,
            AgentName = envelope.Run.AgentName,
            Status = result.Status.ToString(),
            Succeeded = result.Succeeded,
            PayloadKind = ManualTesterPayloadKind,
            PayloadJson = payloadJson,
            PayloadSha256 = Sha256(payloadJson),
            AuditEnvelopeJson = envelopeJson,
            AuditEnvelopeSha256 = Sha256(envelopeJson),
            EvidenceRefs = evidenceRefs,
            CreatedAtUtc = createdAtUtc
        };
    }

    public static ToolExecutionAuditRecord FromManualImplementationPatchProposalResult(
        ManualImplementationPatchProposalResult result,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Succeeded || result.Status is not ManualImplementationPatchProposalStatus.Succeeded)
            throw new ArgumentException("Only successful implementation patch proposal results can be persisted.", nameof(result));

        if (result.Output is null || result.AuditEnvelope is null)
            throw new ArgumentException("Implementation patch proposal result must include output and audit envelope.", nameof(result));

        if (!result.Output.Proposal.IsProposalOnly || HasUnsafeImplementationOutput(result.Output))
            throw new ArgumentException("Implementation patch proposal output must be proposal-only and safe.", nameof(result));

        var envelope = result.AuditEnvelope;
        var payloadJson = Serialize(result);
        var envelopeJson = Serialize(envelope);
        var evidenceRefs = CollectEvidenceRefs(
            result.Output.EvidenceRefs,
            result.Output.Proposal.EvidenceRefs,
            result.Output.Proposal.FileChanges.SelectMany(change => change.EvidenceRefs),
            result.Output.Proposal.FileChanges.SelectMany(change => change.Hunks).SelectMany(hunk => hunk.EvidenceRefs),
            envelope.Outputs.SelectMany(output => output.EvidenceRefs),
            envelope.Steps.SelectMany(step => step.EvidenceRefs),
            envelope.BoundaryDecisions.SelectMany(decision => decision.EvidenceRefs),
            envelope.ThoughtLedger.SelectMany(entry => entry.EvidenceRefs));

        return new ToolExecutionAuditRecord
        {
            ToolExecutionAuditId = $"tool-audit-{result.ManualProposalId}",
            TenantId = envelope.Run.TenantId,
            ProjectId = envelope.Run.ProjectId,
            CampaignId = envelope.Run.CampaignId,
            RunId = envelope.Run.RunId,
            AgentRunId = envelope.Run.AgentRunId,
            ManualExecutionId = result.ManualProposalId,
            ToolRequestId = RequireValue(result.ToolRequestId, nameof(result.ToolRequestId)),
            GateDecisionId = RequireValue(result.GateDecisionId, nameof(result.GateDecisionId)),
            ToolKind = AgentToolKind.PatchProposal,
            RequestType = AgentToolRequestType.PatchProposalRequest,
            AgentKind = AgentKind.ImplementationAgent,
            AgentId = envelope.Run.AgentId,
            AgentName = envelope.Run.AgentName,
            Status = result.Status.ToString(),
            Succeeded = result.Succeeded,
            PayloadKind = ManualImplementationPatchProposalPayloadKind,
            PayloadJson = payloadJson,
            PayloadSha256 = Sha256(payloadJson),
            AuditEnvelopeJson = envelopeJson,
            AuditEnvelopeSha256 = Sha256(envelopeJson),
            EvidenceRefs = evidenceRefs,
            CreatedAtUtc = createdAtUtc
        };
    }

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    public static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string RequireValue(string? value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{field} is required.")
            : value;

    private static IReadOnlyList<string> CollectEvidenceRefs(params IEnumerable<string>[] refs) =>
        refs.SelectMany(value => value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static bool HasUnsafeTesterOutput(ManualTesterAgentToolExecutionOutput output) =>
        output.ContainsRawPrivateReasoning ||
        output.MutatesSource ||
        output.CallsExternalSystem ||
        output.SubmitsGitHubReview ||
        output.PromotesMemory ||
        output.CreatesCollectiveMemory ||
        output.WritesWeaviate;

    private static bool HasUnsafeImplementationOutput(ManualImplementationPatchProposalOutput output) =>
        output.ContainsRawPrivateReasoning ||
        output.MutatesSource ||
        output.AppliesPatch ||
        output.WritesFiles ||
        output.DeletesFiles ||
        output.RunsGit ||
        output.CallsExternalSystem ||
        output.SubmitsGitHubReview ||
        output.CreatesPullRequest ||
        output.PromotesMemory ||
        output.CreatesCollectiveMemory ||
        output.WritesWeaviate ||
        output.Proposal.CreatesAuthority ||
        output.Proposal.CreatesRuntimeAction ||
        output.Proposal.MutatesSource ||
        output.Proposal.AppliesPatch ||
        output.Proposal.FileChanges.Any(change => change.WritesFile || change.DeletesFile || change.AppliesPatch) ||
        output.Proposal.FileChanges.SelectMany(change => change.Hunks).Any(hunk => hunk.ContainsRawPrivateReasoning || hunk.ContainsSecret || hunk.ClaimsApplied);
}
