using System.Text.Json;

namespace IronDev.Core.Policy;

public static class ProjectApprovalRuleStatuses
{
    public const string Draft = "Draft";
    public const string Active = "Active";
    public const string Retired = "Retired";
    public const string Superseded = "Superseded";

    public static IReadOnlyList<string> All { get; } =
    [
        Draft,
        Active,
        Retired,
        Superseded
    ];

    public static bool IsAllowed(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && All.Any(status => string.Equals(status, value.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string value) =>
        All.First(status => string.Equals(status, value.Trim(), StringComparison.OrdinalIgnoreCase));
}

public static class ProjectApprovalRuleScopes
{
    public const string ToolExecution = "tool_execution";
    public const string SourceApply = "source_apply";
    public const string MemoryPromotion = "memory_promotion";
    public const string ProposalAcceptance = "proposal_acceptance";
    public const string ReleaseReadiness = "release_readiness";
    public const string ExternalSideEffect = "external_side_effect";
    public const string DestructiveOperation = "destructive_operation";
    public const string DogfoodReceiptClassification = "dogfood_receipt_classification";
    public const string WorkflowStepRouting = "workflow_step_routing";
    public const string A2aHandoffValidation = "a2a_handoff_validation";

    public static IReadOnlyList<string> All { get; } =
    [
        ToolExecution,
        SourceApply,
        MemoryPromotion,
        ProposalAcceptance,
        ReleaseReadiness,
        ExternalSideEffect,
        DestructiveOperation,
        DogfoodReceiptClassification,
        WorkflowStepRouting,
        A2aHandoffValidation
    ];

    public static IReadOnlyList<string> Sensitive { get; } =
    [
        SourceApply,
        MemoryPromotion,
        ReleaseReadiness,
        ExternalSideEffect,
        DestructiveOperation
    ];

    public static bool IsAllowed(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && All.Any(scope => string.Equals(scope, value.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool IsSensitive(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Sensitive.Any(scope => string.Equals(scope, value.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string value) =>
        All.First(scope => string.Equals(scope, value.Trim(), StringComparison.OrdinalIgnoreCase));
}

public static class ProjectApprovalRuleRiskLevels
{
    public const string Low = "Low";
    public const string Medium = "Medium";
    public const string High = "High";
    public const string Critical = "Critical";

    public static IReadOnlyList<string> All { get; } =
    [
        Low,
        Medium,
        High,
        Critical
    ];

    public static bool IsAllowed(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && All.Any(riskLevel => string.Equals(riskLevel, value.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string value) =>
        All.First(riskLevel => string.Equals(riskLevel, value.Trim(), StringComparison.OrdinalIgnoreCase));
}

public static class ProjectApprovalRuleApprovalTypes
{
    public const string None = "None";
    public const string Single = "Single";
    public const string AnyOf = "AnyOf";
    public const string AllOf = "AllOf";
    public const string Quorum = "Quorum";
    public const string HumanOnly = "HumanOnly";

    public static IReadOnlyList<string> All { get; } =
    [
        None,
        Single,
        AnyOf,
        AllOf,
        Quorum,
        HumanOnly
    ];

    public static IReadOnlyList<string> RequiringApprovers { get; } =
    [
        Single,
        AnyOf,
        AllOf,
        Quorum,
        HumanOnly
    ];

    public static bool IsAllowed(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && All.Any(approvalType => string.Equals(approvalType, value.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool RequiresApprovers(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && RequiringApprovers.Any(approvalType => string.Equals(approvalType, value.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string value) =>
        All.First(approvalType => string.Equals(approvalType, value.Trim(), StringComparison.OrdinalIgnoreCase));
}

public static class ProjectApprovalRuleApproverTypes
{
    public const string Human = "Human";
    public const string ProjectLead = "ProjectLead";
    public const string MemoryOwner = "MemoryOwner";
    public const string SecurityOwner = "SecurityOwner";
    public const string ReleaseOwner = "ReleaseOwner";
    public const string Operator = "Operator";
    public const string System = "System";
    public const string Agent = "Agent";

    public static IReadOnlyList<string> All { get; } =
    [
        Human,
        ProjectLead,
        MemoryOwner,
        SecurityOwner,
        ReleaseOwner,
        Operator,
        System,
        Agent
    ];

    public static IReadOnlyList<string> HumanClass { get; } =
    [
        Human,
        ProjectLead,
        MemoryOwner,
        SecurityOwner,
        ReleaseOwner,
        Operator
    ];

    public static IReadOnlyList<string> Automated { get; } =
    [
        System,
        Agent
    ];

    public static IReadOnlyList<string> Forbidden { get; } =
    [
        "Model",
        "LLM",
        "Critic",
        "Retriever",
        "VectorStore",
        "Workflow",
        "LangGraph",
        "A2A",
        "DogfoodReceipt",
        "GateDecision",
        "PolicyDecision"
    ];

    public static bool IsAllowed(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && All.Any(approverType => string.Equals(approverType, value.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool IsHumanClass(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && HumanClass.Any(approverType => string.Equals(approverType, value.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool IsAutomated(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Automated.Any(approverType => string.Equals(approverType, value.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool IsForbidden(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Forbidden.Any(approverType => string.Equals(approverType, value.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string value) =>
        All.First(approverType => string.Equals(approverType, value.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed record ProjectApprovalRule
{
    public required Guid ProjectApprovalRuleId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid ProjectAutonomyPolicyId { get; init; }
    public required string RuleName { get; init; }
    public required int RuleVersion { get; init; }
    public required string Status { get; init; }
    public required string ApprovalScope { get; init; }
    public string? SubjectTypePattern { get; init; }
    public string? ActionNamePattern { get; init; }
    public required string RiskLevel { get; init; }
    public required string ApprovalType { get; init; }
    public required IReadOnlyList<string> ApproverTypes { get; init; }
    public int? QuorumCount { get; init; }
    public Guid? SupersedesRuleId { get; init; }
    public required string CreatedByActorType { get; init; }
    public required string CreatedByActorId { get; init; }
    public required int MetadataVersion { get; init; }
    public required string MetadataJson { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record ProjectApprovalRuleCreateRequest
{
    public required Guid ProjectId { get; init; }
    public required Guid ProjectAutonomyPolicyId { get; init; }
    public required string RuleName { get; init; }
    public required int RuleVersion { get; init; }
    public required string Status { get; init; }
    public required string ApprovalScope { get; init; }
    public string? SubjectTypePattern { get; init; }
    public string? ActionNamePattern { get; init; }
    public required string RiskLevel { get; init; }
    public required string ApprovalType { get; init; }
    public required IReadOnlyList<string> ApproverTypes { get; init; }
    public int? QuorumCount { get; init; }
    public Guid? SupersedesRuleId { get; init; }
    public required string CreatedByActorType { get; init; }
    public required string CreatedByActorId { get; init; }
    public required int MetadataVersion { get; init; }
    public required string MetadataJson { get; init; }
}

public sealed record ProjectApprovalRuleSummary
{
    public required Guid ProjectApprovalRuleId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid ProjectAutonomyPolicyId { get; init; }
    public required string RuleName { get; init; }
    public required int RuleVersion { get; init; }
    public required string Status { get; init; }
    public required string ApprovalScope { get; init; }
    public required string RiskLevel { get; init; }
    public required string ApprovalType { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record ProjectApprovalRuleValidationIssue
{
    public required string Code { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}

public sealed record ProjectApprovalRuleValidationResult
{
    public required IReadOnlyList<ProjectApprovalRuleValidationIssue> Issues { get; init; }
    public bool IsValid => Issues.Count == 0;

    public static ProjectApprovalRuleValidationResult From(IReadOnlyList<ProjectApprovalRuleValidationIssue> issues) =>
        new() { Issues = issues };
}

public static class ProjectApprovalRuleValidator
{
    private const int MaxMetadataJsonLength = 8192;

    private static readonly string[] PrivateReasoningMarkers =
    [
        "hiddenReasoning",
        "hidden reasoning",
        "chainOfThought",
        "chain of thought",
        "scratchpad",
        "privateReasoning",
        "private reasoning",
        "rawPrompt",
        "raw prompt",
        "rawCompletion",
        "raw completion",
        "systemPrompt",
        "system prompt",
        "developerPrompt",
        "developer prompt"
    ];

    private static readonly string[] UnsafePositiveWordingMarkers =
    [
        "free",
        "unrestricted",
        "fully autonomous",
        "noApproval",
        "no approval",
        "autoApprove",
        "auto approve",
        "auto-approved",
        "autoExecute",
        "auto execute",
        "authorized for execution",
        "ready to run",
        "execution allowed",
        "permission granted",
        "canExecute",
        "can execute",
        "sourceApplyAllowed",
        "source apply allowed",
        "memoryPromotionAllowed",
        "memory promotion allowed",
        "releaseApproved",
        "release approved",
        "canShip",
        "can ship",
        "policySatisfied",
        "policy satisfied"
    ];

    private static readonly HashSet<string> AlwaysForbiddenMetadataProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "autoApprove",
        "autoExecute",
        "sourceApplyAllowed",
        "memoryPromotionAllowed",
        "releaseApproved",
        "canExecute",
        "canShip",
        "noApproval",
        "policySatisfied"
    };

    private static readonly HashSet<string> AuthorityFlagProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "grantsApproval",
        "grantsExecution",
        "mutatesSource",
        "promotesMemory",
        "startsWorkflow",
        "satisfiesPolicy",
        "transfersAuthority",
        "createsApproval",
        "createsPolicyDecision",
        "createsDogfoodReceipt"
    };

    public static ProjectApprovalRuleValidationResult ValidateCreate(ProjectApprovalRuleCreateRequest? request)
    {
        var issues = new List<ProjectApprovalRuleValidationIssue>();

        if (request is null)
        {
            Add(issues, "REQUEST_REQUIRED", "request", "Project approval rule create request is required.");
            return ProjectApprovalRuleValidationResult.From(issues);
        }

        ValidateCommon(
            issues,
            request.ProjectId,
            request.ProjectAutonomyPolicyId,
            request.RuleName,
            request.RuleVersion,
            request.Status,
            request.ApprovalScope,
            request.RiskLevel,
            request.ApprovalType,
            request.ApproverTypes,
            request.QuorumCount,
            request.CreatedByActorType,
            request.CreatedByActorId,
            request.MetadataVersion,
            request.MetadataJson);

        return ProjectApprovalRuleValidationResult.From(issues);
    }

    public static ProjectApprovalRuleValidationResult Validate(ProjectApprovalRule? rule)
    {
        var issues = new List<ProjectApprovalRuleValidationIssue>();

        if (rule is null)
        {
            Add(issues, "RULE_REQUIRED", "rule", "Project approval rule is required.");
            return ProjectApprovalRuleValidationResult.From(issues);
        }

        if (rule.ProjectApprovalRuleId == Guid.Empty)
        {
            Add(issues, "PROJECT_APPROVAL_RULE_ID_REQUIRED", nameof(rule.ProjectApprovalRuleId), "Project approval rule ID is required.");
        }

        if (rule.CreatedUtc == default)
        {
            Add(issues, "CREATED_UTC_REQUIRED", nameof(rule.CreatedUtc), "Created UTC timestamp is required.");
        }

        ValidateCommon(
            issues,
            rule.ProjectId,
            rule.ProjectAutonomyPolicyId,
            rule.RuleName,
            rule.RuleVersion,
            rule.Status,
            rule.ApprovalScope,
            rule.RiskLevel,
            rule.ApprovalType,
            rule.ApproverTypes,
            rule.QuorumCount,
            rule.CreatedByActorType,
            rule.CreatedByActorId,
            rule.MetadataVersion,
            rule.MetadataJson);

        return ProjectApprovalRuleValidationResult.From(issues);
    }

    public static bool IsAllowedApprovalScope(string? value) => ProjectApprovalRuleScopes.IsAllowed(value);

    public static bool IsSensitiveApprovalScope(string? value) => ProjectApprovalRuleScopes.IsSensitive(value);

    public static bool IsAllowedApprovalType(string? value) => ProjectApprovalRuleApprovalTypes.IsAllowed(value);

    public static bool IsAllowedApproverType(string? value) => ProjectApprovalRuleApproverTypes.IsAllowed(value);

    public static bool IsForbiddenApproverType(string? value) => ProjectApprovalRuleApproverTypes.IsForbidden(value);

    public static bool IsAllowedRiskLevel(string? value) => ProjectApprovalRuleRiskLevels.IsAllowed(value);

    public static bool IsAllowedStatus(string? value) => ProjectApprovalRuleStatuses.IsAllowed(value);

    private static void ValidateCommon(
        List<ProjectApprovalRuleValidationIssue> issues,
        Guid projectId,
        Guid projectAutonomyPolicyId,
        string? ruleName,
        int ruleVersion,
        string? status,
        string? approvalScope,
        string? riskLevel,
        string? approvalType,
        IReadOnlyList<string>? approverTypes,
        int? quorumCount,
        string? createdByActorType,
        string? createdByActorId,
        int metadataVersion,
        string? metadataJson)
    {
        if (projectId == Guid.Empty)
        {
            Add(issues, "PROJECT_ID_REQUIRED", "ProjectId", "Project ID is required.");
        }

        if (projectAutonomyPolicyId == Guid.Empty)
        {
            Add(issues, "PROJECT_AUTONOMY_POLICY_ID_REQUIRED", "ProjectAutonomyPolicyId", "Project autonomy policy ID is required.");
        }

        if (string.IsNullOrWhiteSpace(ruleName))
        {
            Add(issues, "RULE_NAME_REQUIRED", "RuleName", "Rule name is required.");
        }

        if (ruleVersion <= 0)
        {
            Add(issues, "RULE_VERSION_REQUIRED", "RuleVersion", "Rule version must be positive.");
        }

        var statusAllowed = ProjectApprovalRuleStatuses.IsAllowed(status);
        if (string.IsNullOrWhiteSpace(status))
        {
            Add(issues, "STATUS_REQUIRED", "Status", "Rule status is required.");
        }
        else if (!statusAllowed)
        {
            Add(issues, "STATUS_UNKNOWN", "Status", "Rule status is not part of the bounded vocabulary.");
        }

        var scopeAllowed = ProjectApprovalRuleScopes.IsAllowed(approvalScope);
        var sensitiveScope = ProjectApprovalRuleScopes.IsSensitive(approvalScope);
        if (string.IsNullOrWhiteSpace(approvalScope))
        {
            Add(issues, "APPROVAL_SCOPE_REQUIRED", "ApprovalScope", "Approval scope is required.");
        }
        else if (!scopeAllowed)
        {
            Add(issues, "APPROVAL_SCOPE_UNKNOWN", "ApprovalScope", "Approval scope is not part of the bounded vocabulary.");
        }

        if (string.IsNullOrWhiteSpace(riskLevel))
        {
            Add(issues, "RISK_LEVEL_REQUIRED", "RiskLevel", "Risk level is required.");
        }
        else if (!ProjectApprovalRuleRiskLevels.IsAllowed(riskLevel))
        {
            Add(issues, "RISK_LEVEL_UNKNOWN", "RiskLevel", "Risk level is not part of the bounded vocabulary.");
        }

        var approvalTypeAllowed = ProjectApprovalRuleApprovalTypes.IsAllowed(approvalType);
        var normalizedApprovalType = approvalTypeAllowed ? ProjectApprovalRuleApprovalTypes.Normalize(approvalType!) : null;
        if (string.IsNullOrWhiteSpace(approvalType))
        {
            Add(issues, "APPROVAL_TYPE_REQUIRED", "ApprovalType", "Approval type is required.");
        }
        else if (!approvalTypeAllowed)
        {
            Add(issues, "APPROVAL_TYPE_UNKNOWN", "ApprovalType", "Approval type is not part of the bounded vocabulary.");
        }

        var normalizedApprovers = NormalizeApprovers(issues, approverTypes);

        if (normalizedApprovalType is not null
            && ProjectApprovalRuleApprovalTypes.RequiresApprovers(normalizedApprovalType)
            && normalizedApprovers.Count == 0)
        {
            Add(issues, "APPROVERS_REQUIRED", "ApproverTypes", "This approval type requires at least one approver type.");
        }

        if (sensitiveScope && string.Equals(normalizedApprovalType, ProjectApprovalRuleApprovalTypes.None, StringComparison.OrdinalIgnoreCase))
        {
            Add(issues, "SENSITIVE_SCOPE_REQUIRES_APPROVAL", "ApprovalType", "Sensitive scopes cannot use ApprovalType=None.");
        }

        if (sensitiveScope && normalizedApprovers.Any(ProjectApprovalRuleApproverTypes.IsAutomated))
        {
            Add(issues, "SENSITIVE_SCOPE_REJECTS_AUTOMATED_APPROVER", "ApproverTypes", "Sensitive scopes cannot use System or Agent approver types.");
        }

        if (sensitiveScope && !normalizedApprovers.Any(ProjectApprovalRuleApproverTypes.IsHumanClass))
        {
            Add(issues, "SENSITIVE_SCOPE_REQUIRES_HUMAN_APPROVER", "ApproverTypes", "Sensitive scopes require a human approver class.");
        }

        ValidateQuorum(issues, normalizedApprovalType, normalizedApprovers, quorumCount);
        ValidateActor(issues, createdByActorType, createdByActorId);
        ValidateMetadata(issues, metadataVersion, metadataJson);
    }

    private static List<string> NormalizeApprovers(List<ProjectApprovalRuleValidationIssue> issues, IReadOnlyList<string>? approverTypes)
    {
        var normalized = new List<string>();
        foreach (var approverType in approverTypes ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(approverType))
            {
                Add(issues, "APPROVER_TYPE_BLANK", "ApproverTypes", "Approver type cannot be blank.");
                continue;
            }

            if (ProjectApprovalRuleApproverTypes.IsForbidden(approverType))
            {
                Add(issues, "APPROVER_TYPE_FORBIDDEN", "ApproverTypes", $"Approver type '{approverType}' is evidence or infrastructure, not an approver.");
                continue;
            }

            if (!ProjectApprovalRuleApproverTypes.IsAllowed(approverType))
            {
                Add(issues, "APPROVER_TYPE_UNKNOWN", "ApproverTypes", $"Approver type '{approverType}' is not part of the bounded vocabulary.");
                continue;
            }

            var canonical = ProjectApprovalRuleApproverTypes.Normalize(approverType);
            if (!normalized.Contains(canonical, StringComparer.OrdinalIgnoreCase))
            {
                normalized.Add(canonical);
            }
        }

        return normalized;
    }

    private static void ValidateQuorum(
        List<ProjectApprovalRuleValidationIssue> issues,
        string? normalizedApprovalType,
        IReadOnlyList<string> normalizedApprovers,
        int? quorumCount)
    {
        if (string.Equals(normalizedApprovalType, ProjectApprovalRuleApprovalTypes.Quorum, StringComparison.OrdinalIgnoreCase))
        {
            if (quorumCount is null or <= 0)
            {
                Add(issues, "QUORUM_COUNT_REQUIRED", "QuorumCount", "Quorum approval type requires a positive quorum count.");
                return;
            }

            if (quorumCount.Value > normalizedApprovers.Count)
            {
                Add(issues, "QUORUM_COUNT_EXCEEDS_APPROVERS", "QuorumCount", "Quorum count cannot exceed the approver type count.");
            }

            return;
        }

        if (quorumCount.HasValue)
        {
            Add(issues, "QUORUM_COUNT_NOT_ALLOWED", "QuorumCount", "Non-quorum approval types cannot set quorum count.");
        }
    }

    private static void ValidateActor(
        List<ProjectApprovalRuleValidationIssue> issues,
        string? createdByActorType,
        string? createdByActorId)
    {
        if (string.IsNullOrWhiteSpace(createdByActorType))
        {
            Add(issues, "ACTOR_TYPE_REQUIRED", "CreatedByActorType", "Created-by actor type is required.");
        }

        if (string.IsNullOrWhiteSpace(createdByActorId))
        {
            Add(issues, "ACTOR_ID_REQUIRED", "CreatedByActorId", "Created-by actor ID is required.");
        }
    }

    private static void ValidateMetadata(List<ProjectApprovalRuleValidationIssue> issues, int metadataVersion, string? metadataJson)
    {
        if (metadataVersion <= 0)
        {
            Add(issues, "METADATA_VERSION_REQUIRED", "MetadataVersion", "Metadata version must be positive.");
        }

        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            Add(issues, "METADATA_JSON_REQUIRED", "MetadataJson", "Metadata JSON is required.");
            return;
        }

        if (metadataJson.Length > MaxMetadataJsonLength)
        {
            Add(issues, "METADATA_TOO_LARGE", "MetadataJson", "Metadata JSON must stay small.");
        }

        if (ContainsAny(metadataJson, PrivateReasoningMarkers))
        {
            Add(issues, "METADATA_PRIVATE_REASONING", "MetadataJson", "Metadata JSON cannot contain hidden or private reasoning markers.");
        }

        if (ContainsAny(metadataJson, UnsafePositiveWordingMarkers))
        {
            Add(issues, "METADATA_UNSAFE_WORDING", "MetadataJson", "Metadata JSON cannot contain unsafe positive approval/execution wording.");
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                Add(issues, "METADATA_JSON_OBJECT_REQUIRED", "MetadataJson", "Metadata JSON must be an object.");
                return;
            }

            if (!document.RootElement.TryGetProperty("schema", out var schema)
                || schema.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(schema.GetString()))
            {
                Add(issues, "METADATA_SCHEMA_REQUIRED", "MetadataJson", "Metadata JSON requires a schema field.");
            }

            ScanMetadataElement(issues, document.RootElement, "MetadataJson");
        }
        catch (JsonException)
        {
            Add(issues, "METADATA_JSON_INVALID", "MetadataJson", "Metadata JSON is not valid JSON.");
        }
    }

    private static void ScanMetadataElement(List<ProjectApprovalRuleValidationIssue> issues, JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var propertyPath = $"{path}.{property.Name}";
                    if (ContainsAny(property.Name, PrivateReasoningMarkers))
                    {
                        Add(issues, "METADATA_PRIVATE_REASONING", propertyPath, "Metadata property cannot contain hidden or private reasoning markers.");
                    }

                    if (AlwaysForbiddenMetadataProperties.Contains(property.Name))
                    {
                        Add(issues, "METADATA_AUTHORITY_GRANT", propertyPath, "Metadata property name is an unsafe approval/execution grant.");
                    }
                    else if (AuthorityFlagProperties.Contains(property.Name) && property.Value.ValueKind != JsonValueKind.False)
                    {
                        Add(issues, "METADATA_AUTHORITY_GRANT", propertyPath, "Authority flag metadata must be explicitly false.");
                    }

                    ScanMetadataElement(issues, property.Value, propertyPath);
                }

                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    ScanMetadataElement(issues, item, $"{path}[{index}]");
                    index++;
                }

                break;

            case JsonValueKind.String:
                var value = element.GetString() ?? string.Empty;
                if (ContainsAny(value, PrivateReasoningMarkers))
                {
                    Add(issues, "METADATA_PRIVATE_REASONING", path, "Metadata string cannot contain hidden or private reasoning markers.");
                }

                if (ContainsAny(value, UnsafePositiveWordingMarkers))
                {
                    Add(issues, "METADATA_UNSAFE_WORDING", path, "Metadata string cannot contain unsafe positive approval/execution wording.");
                }

                break;
        }
    }

    private static bool ContainsAny(string value, IEnumerable<string> markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static void Add(List<ProjectApprovalRuleValidationIssue> issues, string code, string field, string message) =>
        issues.Add(new ProjectApprovalRuleValidationIssue
        {
            Code = code,
            Field = field,
            Message = message
        });
}
