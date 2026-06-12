using System.Text.Json;

namespace IronDev.Core.Policy;

public enum ProjectAutonomyLevel
{
    Conservative = 1,
    Balanced = 2,
    Experimental = 3
}

public enum ProjectAutonomyPolicyStatus
{
    Draft = 1,
    Active = 2,
    Retired = 3,
    Superseded = 4
}

public sealed record ProjectAutonomyPolicy
{
    public required Guid ProjectAutonomyPolicyId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string PolicyName { get; init; }
    public required int PolicyVersion { get; init; }
    public required string AutonomyLevel { get; init; }
    public required string Status { get; init; }
    public Guid? SupersedesPolicyId { get; init; }
    public required string CreatedByActorType { get; init; }
    public required string CreatedByActorId { get; init; }
    public required int MetadataVersion { get; init; }
    public required string MetadataJson { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record ProjectAutonomyPolicyCreateRequest
{
    public required Guid ProjectId { get; init; }
    public required string PolicyName { get; init; }
    public required int PolicyVersion { get; init; }
    public required string AutonomyLevel { get; init; }
    public required string Status { get; init; }
    public Guid? SupersedesPolicyId { get; init; }
    public required string CreatedByActorType { get; init; }
    public required string CreatedByActorId { get; init; }
    public required int MetadataVersion { get; init; }
    public required string MetadataJson { get; init; }
}

public sealed record ProjectAutonomyPolicySummary
{
    public required Guid ProjectAutonomyPolicyId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string PolicyName { get; init; }
    public required int PolicyVersion { get; init; }
    public required string AutonomyLevel { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record ProjectAutonomyPolicyValidationIssue(string Code, string Message);

public sealed record ProjectAutonomyPolicyValidationResult(IReadOnlyList<ProjectAutonomyPolicyValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public sealed class ProjectAutonomyPolicyValidator
{
    public const int MaxMetadataJsonLength = 16_000;

    private static readonly string[] ValidAutonomyLevels =
    [
        nameof(ProjectAutonomyLevel.Conservative),
        nameof(ProjectAutonomyLevel.Balanced),
        nameof(ProjectAutonomyLevel.Experimental)
    ];

    private static readonly string[] ValidStatuses =
    [
        nameof(ProjectAutonomyPolicyStatus.Draft),
        nameof(ProjectAutonomyPolicyStatus.Active),
        nameof(ProjectAutonomyPolicyStatus.Retired),
        nameof(ProjectAutonomyPolicyStatus.Superseded)
    ];

    private static readonly string[] ForbiddenAutonomyLevels =
    [
        "Free",
        "Unrestricted",
        "Autonomous",
        "FullAuto",
        "NoApproval",
        "GodMode",
        "Unlimited",
        "Unsafe"
    ];

    private static readonly string[] UnsafeTextMarkers =
    [
        "raw prompt",
        "raw_prompt",
        "rawprompt",
        "raw completion",
        "raw_completion",
        "rawcompletion",
        "raw tool output",
        "raw_tool_output",
        "rawtooloutput",
        "entire patch",
        "entire_patch",
        "entirepatch",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "scratchpad",
        "private reasoning",
        "privatereasoning",
        "hidden reasoning",
        "hiddenreasoning",
        "system prompt",
        "developer prompt",
        "agent can approve",
        "agentcanapprove",
        "auto approve",
        "autoapprove",
        "auto execute",
        "autoexecute",
        "authorized for execution",
        "execution allowed",
        "execution permission",
        "permission granted",
        "ready to run",
        "ready-to-run",
        "can execute",
        "source apply allowed",
        "memory promotion allowed",
        "release approved",
        "can ship",
        "policy satisfied",
        "approval satisfied",
        "start workflow",
        "workflow started",
        "source applied",
        "memory promoted",
        "collective memory accepted",
        "authority transferred"
    ];

    public ProjectAutonomyPolicyValidationResult ValidateCreate(ProjectAutonomyPolicyCreateRequest? request)
    {
        var issues = new List<ProjectAutonomyPolicyValidationIssue>();

        if (request is null)
        {
            issues.Add(new("PROJECT_AUTONOMY_POLICY_REQUEST_REQUIRED", "Project autonomy policy request is required."));
            return new ProjectAutonomyPolicyValidationResult(issues);
        }

        if (request.ProjectId == Guid.Empty)
            issues.Add(new("PROJECT_REQUIRED", "ProjectId is required."));

        ValidateRequiredText(request.PolicyName, "POLICY_NAME_REQUIRED", "PolicyName is required.", issues);
        ValidateUnsafeText(request.PolicyName, "POLICY_NAME_UNSAFE", "PolicyName must not claim approval, execution, source apply, workflow, memory promotion, release, or authority.", issues);

        if (request.PolicyVersion <= 0)
            issues.Add(new("POLICY_VERSION_INVALID", "PolicyVersion must be positive."));

        ValidateAutonomyLevel(request.AutonomyLevel, issues);
        ValidateStatus(request.Status, issues);
        ValidateRequiredText(request.CreatedByActorType, "ACTOR_TYPE_REQUIRED", "CreatedByActorType is required.", issues);
        ValidateUnsafeText(request.CreatedByActorType, "ACTOR_TYPE_UNSAFE", "CreatedByActorType must not claim approval, execution, source apply, workflow, memory promotion, release, or authority.", issues);
        ValidateRequiredText(request.CreatedByActorId, "ACTOR_ID_REQUIRED", "CreatedByActorId is required.", issues);
        ValidateUnsafeText(request.CreatedByActorId, "ACTOR_ID_UNSAFE", "CreatedByActorId must not claim approval, execution, source apply, workflow, memory promotion, release, or authority.", issues);
        ValidateMetadata(request.MetadataVersion, request.MetadataJson, issues);

        return new ProjectAutonomyPolicyValidationResult(issues);
    }

    public ProjectAutonomyPolicyValidationResult Validate(ProjectAutonomyPolicy? policy)
    {
        var issues = new List<ProjectAutonomyPolicyValidationIssue>();

        if (policy is null)
        {
            issues.Add(new("PROJECT_AUTONOMY_POLICY_REQUIRED", "Project autonomy policy is required."));
            return new ProjectAutonomyPolicyValidationResult(issues);
        }

        if (policy.ProjectAutonomyPolicyId == Guid.Empty)
            issues.Add(new("POLICY_ID_REQUIRED", "ProjectAutonomyPolicyId is required."));

        var createResult = ValidateCreate(new ProjectAutonomyPolicyCreateRequest
        {
            ProjectId = policy.ProjectId,
            PolicyName = policy.PolicyName,
            PolicyVersion = policy.PolicyVersion,
            AutonomyLevel = policy.AutonomyLevel,
            Status = policy.Status,
            SupersedesPolicyId = policy.SupersedesPolicyId,
            CreatedByActorType = policy.CreatedByActorType,
            CreatedByActorId = policy.CreatedByActorId,
            MetadataVersion = policy.MetadataVersion,
            MetadataJson = policy.MetadataJson
        });

        issues.AddRange(createResult.Issues);

        if (policy.CreatedUtc == default)
            issues.Add(new("CREATED_UTC_REQUIRED", "CreatedUtc is required."));

        return new ProjectAutonomyPolicyValidationResult(issues);
    }

    public static bool IsAllowedAutonomyLevel(string? value) =>
        ValidAutonomyLevels.Any(level => string.Equals(level, NormalizeText(value), StringComparison.Ordinal));

    public static bool IsForbiddenAutonomyLevel(string? value) =>
        ForbiddenAutonomyLevels.Any(level => string.Equals(level, NormalizeText(value), StringComparison.OrdinalIgnoreCase));

    public static bool IsAllowedStatus(string? value) =>
        ValidStatuses.Any(status => string.Equals(status, NormalizeText(value), StringComparison.Ordinal));

    public static string NormalizeAutonomyLevel(string value)
    {
        var match = ValidAutonomyLevels.FirstOrDefault(level => string.Equals(level, value, StringComparison.OrdinalIgnoreCase));
        return match ?? NormalizeText(value);
    }

    public static string NormalizeStatus(string value)
    {
        var match = ValidStatuses.FirstOrDefault(status => string.Equals(status, value, StringComparison.OrdinalIgnoreCase));
        return match ?? NormalizeText(value);
    }

    private static void ValidateAutonomyLevel(string? value, List<ProjectAutonomyPolicyValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new("AUTONOMY_LEVEL_REQUIRED", "AutonomyLevel is required."));
            return;
        }

        if (IsForbiddenAutonomyLevel(value))
            issues.Add(new("AUTONOMY_LEVEL_FORBIDDEN", "AutonomyLevel must not use unsafe authority vocabulary."));

        if (!IsAllowedAutonomyLevel(value))
            issues.Add(new("AUTONOMY_LEVEL_INVALID", "AutonomyLevel must be Conservative, Balanced, or Experimental."));

        ValidateUnsafeText(value, "AUTONOMY_LEVEL_UNSAFE", "AutonomyLevel must not claim approval, execution, source apply, workflow, memory promotion, release, or authority.", issues);
    }

    private static void ValidateStatus(string? value, List<ProjectAutonomyPolicyValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new("STATUS_REQUIRED", "Status is required."));
            return;
        }

        if (!IsAllowedStatus(value))
            issues.Add(new("STATUS_INVALID", "Status must be Draft, Active, Retired, or Superseded."));

        ValidateUnsafeText(value, "STATUS_UNSAFE", "Status must not claim approval, execution, source apply, workflow, memory promotion, release, or authority.", issues);
    }

    private static void ValidateMetadata(int metadataVersion, string metadataJson, List<ProjectAutonomyPolicyValidationIssue> issues)
    {
        if (metadataVersion <= 0)
            issues.Add(new("METADATA_VERSION_INVALID", "MetadataVersion must be positive."));

        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            issues.Add(new("METADATA_REQUIRED", "MetadataJson is required."));
            return;
        }

        if (metadataJson.Length > MaxMetadataJsonLength)
            issues.Add(new("METADATA_TOO_LARGE", "MetadataJson exceeds the maximum allowed length."));

        if (ContainsUnsafeText(metadataJson))
            issues.Add(new("METADATA_UNSAFE", "MetadataJson must not contain hidden/private reasoning or authority-granting markers."));

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                issues.Add(new("METADATA_OBJECT_REQUIRED", "MetadataJson must be a JSON object."));
                return;
            }

            var hasSchema = document.RootElement.TryGetProperty("schema", out var schema)
                && schema.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(schema.GetString());
            var hasSchemaVersion = document.RootElement.TryGetProperty("schemaVersion", out var schemaVersion)
                && schemaVersion.ValueKind == JsonValueKind.Number
                && schemaVersion.GetInt32() > 0;

            if (!hasSchema && !hasSchemaVersion)
                issues.Add(new("METADATA_SCHEMA_REQUIRED", "MetadataJson must include schema or positive schemaVersion."));

            RejectTruthy(document.RootElement, issues, "grantsApproval", "METADATA_GRANTS_APPROVAL_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "grantsExecution", "METADATA_GRANTS_EXECUTION_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "agentCanApprove", "METADATA_AGENT_APPROVAL_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "autoApprove", "METADATA_AUTO_APPROVE_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "autoExecute", "METADATA_AUTO_EXECUTE_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "executionAllowed", "METADATA_EXECUTION_ALLOWED_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "permissionGranted", "METADATA_PERMISSION_GRANTED_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "mutatesSource", "METADATA_SOURCE_MUTATION_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "sourceApplyAllowed", "METADATA_SOURCE_APPLY_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "promotesMemory", "METADATA_MEMORY_PROMOTION_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "memoryPromotionAllowed", "METADATA_MEMORY_PROMOTION_ALLOWED_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "startsWorkflow", "METADATA_WORKFLOW_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "satisfiesPolicy", "METADATA_POLICY_SATISFACTION_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "transfersAuthority", "METADATA_AUTHORITY_TRANSFER_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "releaseApproved", "METADATA_RELEASE_APPROVAL_FORBIDDEN");
        }
        catch (JsonException)
        {
            issues.Add(new("METADATA_JSON_INVALID", "MetadataJson must be valid JSON."));
        }
    }

    private static void ValidateRequiredText(string? value, string code, string message, List<ProjectAutonomyPolicyValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add(new(code, message));
    }

    private static void ValidateUnsafeText(string? value, string code, string message, List<ProjectAutonomyPolicyValidationIssue> issues)
    {
        if (ContainsUnsafeText(value))
            issues.Add(new(code, message));
    }

    private static void RejectTruthy(JsonElement element, List<ProjectAutonomyPolicyValidationIssue> issues, string propertyName, string code)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True)
            issues.Add(new(code, $"MetadataJson must not set {propertyName} to true."));
    }

    private static bool ContainsUnsafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return UnsafeTextMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeText(string? value) => value?.Trim() ?? string.Empty;
}
