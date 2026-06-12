using System.Text.Json;

namespace IronDev.Core.Governance;

public enum ToolGateDecisionValue
{
    Passed = 1,
    Blocked = 2,
    RequiresApproval = 3
}

public sealed record ToolGateDecisionRecordRequest(
    Guid TenantId,
    Guid ProjectId,
    Guid ToolRequestId,
    string Decision,
    string GateName,
    int GateVersion,
    string ActorType,
    string ActorId,
    string ReasonCode,
    string EvidenceJson,
    Guid? CorrelationId = null,
    Guid? CausationId = null,
    DateTimeOffset? CreatedAtUtc = null,
    Guid? ToolGateDecisionId = null,
    Guid? GovernanceEventId = null);

public sealed record ToolGateDecisionReadModel(
    Guid ToolGateDecisionId,
    Guid TenantId,
    Guid ProjectId,
    Guid ToolRequestId,
    Guid GovernanceEventId,
    Guid CorrelationId,
    Guid CausationId,
    string Decision,
    string GateName,
    int GateVersion,
    string ActorType,
    string ActorId,
    string ReasonCode,
    string EvidenceJson,
    DateTimeOffset CreatedAtUtc);

public sealed record ToolGateDecisionSummary(
    Guid ToolGateDecisionId,
    Guid TenantId,
    Guid ProjectId,
    Guid ToolRequestId,
    Guid GovernanceEventId,
    Guid CorrelationId,
    Guid CausationId,
    string Decision,
    string GateName,
    int GateVersion,
    string ActorType,
    string ActorId,
    string ReasonCode,
    DateTimeOffset CreatedAtUtc);

public sealed record ToolGateDecisionProjectQuery(Guid TenantId, Guid ProjectId, int Take = ToolGateDecisionValidator.DefaultTake);

public sealed record ToolGateDecisionToolRequestQuery(Guid TenantId, Guid ProjectId, Guid ToolRequestId, int Take = ToolGateDecisionValidator.DefaultTake);

public sealed record ToolGateDecisionCorrelationQuery(Guid TenantId, Guid ProjectId, Guid CorrelationId, int Take = ToolGateDecisionValidator.DefaultTake);

public sealed record ToolGateDecisionValidationIssue(string Code, string Message);

public sealed record ToolGateDecisionValidationResult(IReadOnlyList<ToolGateDecisionValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public interface IToolGateDecisionStore
{
    Task<ToolGateDecisionReadModel> RecordAsync(ToolGateDecisionRecordRequest request, CancellationToken cancellationToken = default);

    Task<ToolGateDecisionReadModel?> GetAsync(Guid tenantId, Guid projectId, Guid toolGateDecisionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ToolGateDecisionSummary>> ListForToolRequestAsync(ToolGateDecisionToolRequestQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ToolGateDecisionSummary>> ListForProjectAsync(ToolGateDecisionProjectQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ToolGateDecisionSummary>> ListForCorrelationAsync(ToolGateDecisionCorrelationQuery query, CancellationToken cancellationToken = default);
}

public sealed class ToolGateDecisionValidator
{
    public const int DefaultTake = 100;
    public const int MaxTake = 500;
    public const int MaxEvidenceJsonLength = 32_000;

    private static readonly string[] ValidDecisions =
    {
        nameof(ToolGateDecisionValue.Passed),
        nameof(ToolGateDecisionValue.Blocked),
        nameof(ToolGateDecisionValue.RequiresApproval)
    };

    private static readonly string[] UnsafeMarkers =
    {
        "raw prompt",
        "raw_prompt",
        "raw completion",
        "raw_completion",
        "chain-of-thought",
        "chain of thought",
        "scratchpad",
        "private reasoning",
        "hidden reasoning",
        "system prompt",
        "developer prompt",
        "approval granted",
        "approved for execution",
        "execution permitted",
        "execution permission granted",
        "permission granted",
        "human approved",
        "human-approved",
        "authorized for execution",
        "ready to run",
        "ready-to-run",
        "executable without gate",
        "tool executed",
        "gate executed",
        "source applied",
        "apply patch",
        "memory promoted",
        "collective memory accepted",
        "create pull request",
        "submit github review"
    };

    public ToolGateDecisionValidationResult ValidateRecord(ToolGateDecisionRecordRequest? request)
    {
        var issues = new List<ToolGateDecisionValidationIssue>();

        if (request is null)
        {
            issues.Add(new ToolGateDecisionValidationIssue("TOOL_GATE_DECISION_REQUEST_REQUIRED", "Tool gate decision request is required."));
            return new ToolGateDecisionValidationResult(issues);
        }

        if (request.TenantId == Guid.Empty)
        {
            issues.Add(new ToolGateDecisionValidationIssue("TENANT_REQUIRED", "TenantId is required."));
        }

        if (request.ProjectId == Guid.Empty)
        {
            issues.Add(new ToolGateDecisionValidationIssue("PROJECT_REQUIRED", "ProjectId is required."));
        }

        if (request.ToolRequestId == Guid.Empty)
        {
            issues.Add(new ToolGateDecisionValidationIssue("TOOL_REQUEST_REQUIRED", "ToolRequestId is required."));
        }

        if (string.IsNullOrWhiteSpace(request.Decision) || !ValidDecisions.Any(value => string.Equals(value, request.Decision, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(new ToolGateDecisionValidationIssue("DECISION_INVALID", "Decision must be Passed, Blocked, or RequiresApproval."));
        }

        if (ContainsForbiddenDecisionName(request.Decision))
        {
            issues.Add(new ToolGateDecisionValidationIssue("DECISION_AUTHORITY_LANGUAGE_FORBIDDEN", "Decision must not use approval, authorization, execution, or permission-grant language."));
        }

        if (string.IsNullOrWhiteSpace(request.GateName))
        {
            issues.Add(new ToolGateDecisionValidationIssue("GATE_NAME_REQUIRED", "GateName is required."));
        }

        if (request.GateVersion <= 0)
        {
            issues.Add(new ToolGateDecisionValidationIssue("GATE_VERSION_INVALID", "GateVersion must be positive."));
        }

        if (string.IsNullOrWhiteSpace(request.ActorType))
        {
            issues.Add(new ToolGateDecisionValidationIssue("ACTOR_TYPE_REQUIRED", "ActorType is required."));
        }

        if (string.IsNullOrWhiteSpace(request.ActorId))
        {
            issues.Add(new ToolGateDecisionValidationIssue("ACTOR_ID_REQUIRED", "ActorId is required."));
        }

        if (string.IsNullOrWhiteSpace(request.ReasonCode))
        {
            issues.Add(new ToolGateDecisionValidationIssue("REASON_CODE_REQUIRED", "ReasonCode is required."));
        }

        ValidateEvidenceJson(request.EvidenceJson, issues);

        return new ToolGateDecisionValidationResult(issues);
    }

    public ToolGateDecisionValidationResult ValidateProjectQuery(ToolGateDecisionProjectQuery query)
    {
        var issues = new List<ToolGateDecisionValidationIssue>();
        ValidateScope(query.TenantId, query.ProjectId, issues);
        ValidateTake(query.Take, issues);
        return new ToolGateDecisionValidationResult(issues);
    }

    public ToolGateDecisionValidationResult ValidateToolRequestQuery(ToolGateDecisionToolRequestQuery query)
    {
        var issues = new List<ToolGateDecisionValidationIssue>();
        ValidateScope(query.TenantId, query.ProjectId, issues);
        if (query.ToolRequestId == Guid.Empty)
        {
            issues.Add(new ToolGateDecisionValidationIssue("TOOL_REQUEST_REQUIRED", "ToolRequestId is required."));
        }

        ValidateTake(query.Take, issues);
        return new ToolGateDecisionValidationResult(issues);
    }

    public ToolGateDecisionValidationResult ValidateCorrelationQuery(ToolGateDecisionCorrelationQuery query)
    {
        var issues = new List<ToolGateDecisionValidationIssue>();
        ValidateScope(query.TenantId, query.ProjectId, issues);
        if (query.CorrelationId == Guid.Empty)
        {
            issues.Add(new ToolGateDecisionValidationIssue("CORRELATION_REQUIRED", "CorrelationId is required."));
        }

        ValidateTake(query.Take, issues);
        return new ToolGateDecisionValidationResult(issues);
    }

    public static int NormalizeTake(int take) => Math.Clamp(take <= 0 ? DefaultTake : take, 1, MaxTake);

    public static string NormalizeDecision(string decision)
    {
        var match = ValidDecisions.FirstOrDefault(value => string.Equals(value, decision, StringComparison.OrdinalIgnoreCase));
        return match ?? decision.Trim();
    }

    private static void ValidateScope(Guid tenantId, Guid projectId, List<ToolGateDecisionValidationIssue> issues)
    {
        if (tenantId == Guid.Empty)
        {
            issues.Add(new ToolGateDecisionValidationIssue("TENANT_REQUIRED", "TenantId is required."));
        }

        if (projectId == Guid.Empty)
        {
            issues.Add(new ToolGateDecisionValidationIssue("PROJECT_REQUIRED", "ProjectId is required."));
        }
    }

    private static void ValidateTake(int take, List<ToolGateDecisionValidationIssue> issues)
    {
        if (take < 0)
        {
            issues.Add(new ToolGateDecisionValidationIssue("TAKE_INVALID", "Take must not be negative."));
        }
    }

    private static void ValidateEvidenceJson(string evidenceJson, List<ToolGateDecisionValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(evidenceJson))
        {
            issues.Add(new ToolGateDecisionValidationIssue("EVIDENCE_REQUIRED", "EvidenceJson is required."));
            return;
        }

        if (evidenceJson.Length > MaxEvidenceJsonLength)
        {
            issues.Add(new ToolGateDecisionValidationIssue("EVIDENCE_TOO_LARGE", "EvidenceJson exceeds the maximum allowed length."));
        }

        if (ContainsUnsafeText(evidenceJson))
        {
            issues.Add(new ToolGateDecisionValidationIssue("EVIDENCE_UNSAFE", "EvidenceJson must not contain raw/private reasoning, approval, execution, source mutation, or memory promotion claims."));
        }

        try
        {
            using var document = JsonDocument.Parse(evidenceJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                issues.Add(new ToolGateDecisionValidationIssue("EVIDENCE_OBJECT_REQUIRED", "EvidenceJson must be a JSON object."));
                return;
            }

            if (!document.RootElement.TryGetProperty("schemaVersion", out var schemaVersion) || schemaVersion.ValueKind != JsonValueKind.Number || schemaVersion.GetInt32() <= 0)
            {
                issues.Add(new ToolGateDecisionValidationIssue("EVIDENCE_SCHEMA_VERSION_REQUIRED", "EvidenceJson must include a positive schemaVersion."));
            }
        }
        catch (JsonException)
        {
            issues.Add(new ToolGateDecisionValidationIssue("EVIDENCE_JSON_INVALID", "EvidenceJson must be valid JSON."));
        }
    }

    private static bool ContainsUnsafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return UnsafeMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsForbiddenDecisionName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("Approved", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Authorized", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Executable", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ReadyToRun", StringComparison.OrdinalIgnoreCase)
            || value.Contains("HumanApproved", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ExecutionGranted", StringComparison.OrdinalIgnoreCase)
            || value.Contains("PermissionGranted", StringComparison.OrdinalIgnoreCase);
    }
}
