using System.Text.Json;

namespace IronDev.Core.Governance;

public enum PolicyDecisionValue
{
    NoPolicyBlock = 1,
    Blocked = 2,
    RequiresApproval = 3,
    NotApplicable = 4
}

public static class PolicyDecisionScopes
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
}

public sealed record PolicyDecisionRecordRequest
{
    public required Guid ProjectId { get; init; }
    public required string PolicyScope { get; init; }
    public required string PolicyName { get; init; }
    public required int PolicyVersion { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required string Decision { get; init; }
    public required string RequirementCode { get; init; }
    public required string ReasonCode { get; init; }
    public string? Reason { get; init; }
    public required string DecidedByActorType { get; init; }
    public required string DecidedByActorId { get; init; }
    public Guid? RelatedToolRequestId { get; init; }
    public Guid? RelatedToolGateDecisionId { get; init; }
    public Guid? RelatedApprovalDecisionId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required int EvidenceVersion { get; init; }
    public required string EvidenceJson { get; init; }
    public Guid? PolicyDecisionEventId { get; init; }
    public Guid? GovernanceEventId { get; init; }
    public DateTimeOffset? CreatedUtc { get; init; }
}

public sealed record PolicyDecisionReadModel
{
    public required Guid PolicyDecisionEventId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid GovernanceEventId { get; init; }
    public required string PolicyScope { get; init; }
    public required string PolicyName { get; init; }
    public required int PolicyVersion { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required string Decision { get; init; }
    public required string RequirementCode { get; init; }
    public required string ReasonCode { get; init; }
    public string? Reason { get; init; }
    public required string DecidedByActorType { get; init; }
    public required string DecidedByActorId { get; init; }
    public Guid? RelatedToolRequestId { get; init; }
    public Guid? RelatedToolGateDecisionId { get; init; }
    public Guid? RelatedApprovalDecisionId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required int EvidenceVersion { get; init; }
    public required string EvidenceJson { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record PolicyDecisionSummary
{
    public required Guid PolicyDecisionEventId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid GovernanceEventId { get; init; }
    public required string PolicyScope { get; init; }
    public required string PolicyName { get; init; }
    public required int PolicyVersion { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required string Decision { get; init; }
    public required string RequirementCode { get; init; }
    public required string ReasonCode { get; init; }
    public required string DecidedByActorType { get; init; }
    public required string DecidedByActorId { get; init; }
    public Guid? RelatedToolRequestId { get; init; }
    public Guid? RelatedToolGateDecisionId { get; init; }
    public Guid? RelatedApprovalDecisionId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record PolicyDecisionsForSubjectQuery
{
    public required Guid ProjectId { get; init; }
    public required string PolicyScope { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public int Take { get; init; } = PolicyDecisionValidator.DefaultTake;
}

public sealed record PolicyDecisionsForProjectQuery
{
    public required Guid ProjectId { get; init; }
    public int Take { get; init; } = PolicyDecisionValidator.DefaultTake;
}

public sealed record PolicyDecisionsForCorrelationQuery
{
    public required Guid ProjectId { get; init; }
    public required Guid CorrelationId { get; init; }
    public int Take { get; init; } = PolicyDecisionValidator.DefaultTake;
}

public sealed record PolicyDecisionValidationIssue(string Code, string Message);

public sealed record PolicyDecisionValidationResult(IReadOnlyList<PolicyDecisionValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public interface IPolicyDecisionEventStore
{
    Task<PolicyDecisionReadModel> RecordAsync(PolicyDecisionRecordRequest request, CancellationToken cancellationToken = default);

    Task<PolicyDecisionReadModel?> GetAsync(Guid policyDecisionEventId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolicyDecisionSummary>> ListForSubjectAsync(PolicyDecisionsForSubjectQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolicyDecisionSummary>> ListForProjectAsync(PolicyDecisionsForProjectQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolicyDecisionSummary>> ListForCorrelationAsync(PolicyDecisionsForCorrelationQuery query, CancellationToken cancellationToken = default);
}

public sealed class PolicyDecisionValidator
{
    public const int DefaultTake = 100;
    public const int MaxTake = 500;
    public const int MaxEvidenceJsonLength = 32_000;

    private static readonly string[] ValidDecisions =
    {
        nameof(PolicyDecisionValue.NoPolicyBlock),
        nameof(PolicyDecisionValue.Blocked),
        nameof(PolicyDecisionValue.RequiresApproval),
        nameof(PolicyDecisionValue.NotApplicable)
    };

    private static readonly string[] UnsafeMarkers =
    {
        "raw prompt",
        "raw_prompt",
        "rawprompt",
        "raw completion",
        "raw_completion",
        "rawcompletion",
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
        "approved by policy",
        "approval granted",
        "approval satisfied",
        "authorized for execution",
        "execution permission",
        "execution permitted",
        "permission granted",
        "ready to run",
        "ready-to-run",
        "can execute",
        "apply allowed",
        "promotion allowed",
        "release approved",
        "policy satisfied",
        "tool executed",
        "start workflow",
        "workflow started",
        "source applied",
        "apply patch",
        "memory promoted",
        "collective memory accepted",
        "create pull request",
        "submit github review",
        "authority transferred"
    };

    public PolicyDecisionValidationResult ValidateRecord(PolicyDecisionRecordRequest? request)
    {
        var issues = new List<PolicyDecisionValidationIssue>();

        if (request is null)
        {
            issues.Add(new PolicyDecisionValidationIssue("POLICY_DECISION_REQUEST_REQUIRED", "Policy decision request is required."));
            return new PolicyDecisionValidationResult(issues);
        }

        if (request.ProjectId == Guid.Empty)
            issues.Add(new PolicyDecisionValidationIssue("PROJECT_REQUIRED", "ProjectId is required."));

        ValidateRequiredText(request.PolicyScope, "POLICY_SCOPE_REQUIRED", "PolicyScope is required.", issues);
        ValidateRequiredText(request.PolicyName, "POLICY_NAME_REQUIRED", "PolicyName is required.", issues);
        ValidateRequiredText(request.SubjectType, "SUBJECT_TYPE_REQUIRED", "SubjectType is required.", issues);
        ValidateRequiredText(request.SubjectId, "SUBJECT_ID_REQUIRED", "SubjectId is required.", issues);
        ValidateRequiredText(request.RequirementCode, "REQUIREMENT_CODE_REQUIRED", "RequirementCode is required.", issues);
        ValidateRequiredText(request.ReasonCode, "REASON_CODE_REQUIRED", "ReasonCode is required.", issues);
        ValidateRequiredText(request.DecidedByActorType, "ACTOR_TYPE_REQUIRED", "DecidedByActorType is required.", issues);
        ValidateRequiredText(request.DecidedByActorId, "ACTOR_ID_REQUIRED", "DecidedByActorId is required.", issues);

        if (request.PolicyVersion <= 0)
            issues.Add(new PolicyDecisionValidationIssue("POLICY_VERSION_INVALID", "PolicyVersion must be positive."));

        if (string.IsNullOrWhiteSpace(request.Decision) || !ValidDecisions.Any(value => string.Equals(value, request.Decision, StringComparison.OrdinalIgnoreCase)))
            issues.Add(new PolicyDecisionValidationIssue("DECISION_INVALID", "Decision must be NoPolicyBlock, Blocked, RequiresApproval, or NotApplicable."));

        if (ContainsForbiddenDecisionName(request.Decision))
            issues.Add(new PolicyDecisionValidationIssue("DECISION_AUTHORITY_LANGUAGE_FORBIDDEN", "Decision must not use approval, authorization, execution, source apply, or memory promotion language."));

        if (request.EvidenceVersion <= 0)
            issues.Add(new PolicyDecisionValidationIssue("EVIDENCE_VERSION_INVALID", "EvidenceVersion must be positive."));

        ValidateUnsafeText(request.PolicyScope, "POLICY_SCOPE_UNSAFE", "PolicyScope must not claim approval, execution, source mutation, or memory promotion authority.", issues);
        ValidateUnsafeText(request.PolicyName, "POLICY_NAME_UNSAFE", "PolicyName must not claim approval, execution, source mutation, or memory promotion authority.", issues);
        ValidateUnsafeText(request.SubjectType, "SUBJECT_TYPE_UNSAFE", "SubjectType must not claim approval, execution, source mutation, or memory promotion authority.", issues);
        ValidateUnsafeText(request.SubjectId, "SUBJECT_ID_UNSAFE", "SubjectId must not claim approval, execution, source mutation, or memory promotion authority.", issues);
        ValidateUnsafeText(request.RequirementCode, "REQUIREMENT_CODE_UNSAFE", "RequirementCode must not claim approval, execution, source mutation, or memory promotion authority.", issues);
        ValidateUnsafeText(request.ReasonCode, "REASON_CODE_UNSAFE", "ReasonCode must not claim approval, execution, source mutation, or memory promotion authority.", issues);
        ValidateUnsafeText(request.Reason, "REASON_UNSAFE", "Reason must not contain raw/private reasoning or authority claims.", issues);
        ValidateEvidenceJson(request.EvidenceJson, issues);

        return new PolicyDecisionValidationResult(issues);
    }

    public PolicyDecisionValidationResult ValidateSubjectQuery(PolicyDecisionsForSubjectQuery query)
    {
        var issues = new List<PolicyDecisionValidationIssue>();
        ValidateProject(query.ProjectId, issues);
        ValidateRequiredText(query.PolicyScope, "POLICY_SCOPE_REQUIRED", "PolicyScope is required.", issues);
        ValidateRequiredText(query.SubjectType, "SUBJECT_TYPE_REQUIRED", "SubjectType is required.", issues);
        ValidateRequiredText(query.SubjectId, "SUBJECT_ID_REQUIRED", "SubjectId is required.", issues);
        ValidateTake(query.Take, issues);
        return new PolicyDecisionValidationResult(issues);
    }

    public PolicyDecisionValidationResult ValidateProjectQuery(PolicyDecisionsForProjectQuery query)
    {
        var issues = new List<PolicyDecisionValidationIssue>();
        ValidateProject(query.ProjectId, issues);
        ValidateTake(query.Take, issues);
        return new PolicyDecisionValidationResult(issues);
    }

    public PolicyDecisionValidationResult ValidateCorrelationQuery(PolicyDecisionsForCorrelationQuery query)
    {
        var issues = new List<PolicyDecisionValidationIssue>();
        ValidateProject(query.ProjectId, issues);
        if (query.CorrelationId == Guid.Empty)
            issues.Add(new PolicyDecisionValidationIssue("CORRELATION_REQUIRED", "CorrelationId is required."));
        ValidateTake(query.Take, issues);
        return new PolicyDecisionValidationResult(issues);
    }

    public static int NormalizeTake(int take) => Math.Clamp(take <= 0 ? DefaultTake : take, 1, MaxTake);

    public static string NormalizeDecision(string decision)
    {
        var match = ValidDecisions.FirstOrDefault(value => string.Equals(value, decision, StringComparison.OrdinalIgnoreCase));
        return match ?? NormalizeText(decision);
    }

    public static string NormalizeText(string value) => value.Trim();

    private static void ValidateProject(Guid projectId, List<PolicyDecisionValidationIssue> issues)
    {
        if (projectId == Guid.Empty)
            issues.Add(new PolicyDecisionValidationIssue("PROJECT_REQUIRED", "ProjectId is required."));
    }

    private static void ValidateRequiredText(string? value, string code, string message, List<PolicyDecisionValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add(new PolicyDecisionValidationIssue(code, message));
    }

    private static void ValidateTake(int take, List<PolicyDecisionValidationIssue> issues)
    {
        if (take < 0)
            issues.Add(new PolicyDecisionValidationIssue("TAKE_INVALID", "Take must not be negative."));
    }

    private static void ValidateUnsafeText(string? value, string code, string message, List<PolicyDecisionValidationIssue> issues)
    {
        if (ContainsUnsafeText(value))
            issues.Add(new PolicyDecisionValidationIssue(code, message));
    }

    private static void ValidateEvidenceJson(string evidenceJson, List<PolicyDecisionValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(evidenceJson))
        {
            issues.Add(new PolicyDecisionValidationIssue("EVIDENCE_REQUIRED", "EvidenceJson is required."));
            return;
        }

        if (evidenceJson.Length > MaxEvidenceJsonLength)
            issues.Add(new PolicyDecisionValidationIssue("EVIDENCE_TOO_LARGE", "EvidenceJson exceeds the maximum allowed length."));

        if (ContainsUnsafeText(evidenceJson))
            issues.Add(new PolicyDecisionValidationIssue("EVIDENCE_UNSAFE", "EvidenceJson must not contain raw/private reasoning, approval, execution, source mutation, or memory promotion claims."));

        try
        {
            using var document = JsonDocument.Parse(evidenceJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                issues.Add(new PolicyDecisionValidationIssue("EVIDENCE_OBJECT_REQUIRED", "EvidenceJson must be a JSON object."));
                return;
            }

            var hasSchema = document.RootElement.TryGetProperty("schema", out var schema)
                && schema.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(schema.GetString());
            var hasSchemaVersion = document.RootElement.TryGetProperty("schemaVersion", out var schemaVersion)
                && schemaVersion.ValueKind == JsonValueKind.Number
                && schemaVersion.GetInt32() > 0;

            if (!hasSchema && !hasSchemaVersion)
                issues.Add(new PolicyDecisionValidationIssue("EVIDENCE_SCHEMA_REQUIRED", "EvidenceJson must include schema or positive schemaVersion."));

            RejectTruthy(document.RootElement, issues, "grantsApproval", "EVIDENCE_APPROVAL_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "grantsExecution", "EVIDENCE_EXECUTION_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "executionPermission", "EVIDENCE_EXECUTION_PERMISSION_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "mutatesSource", "EVIDENCE_SOURCE_MUTATION_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "promotesMemory", "EVIDENCE_MEMORY_PROMOTION_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "startsWorkflow", "EVIDENCE_WORKFLOW_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "satisfiesPolicy", "EVIDENCE_POLICY_SATISFACTION_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "transfersAuthority", "EVIDENCE_AUTHORITY_TRANSFER_FORBIDDEN");
        }
        catch (JsonException)
        {
            issues.Add(new PolicyDecisionValidationIssue("EVIDENCE_JSON_INVALID", "EvidenceJson must be valid JSON."));
        }
    }

    private static void RejectTruthy(JsonElement element, List<PolicyDecisionValidationIssue> issues, string propertyName, string code)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True)
            issues.Add(new PolicyDecisionValidationIssue(code, $"EvidenceJson must not set {propertyName} to true."));
    }

    private static bool ContainsUnsafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return UnsafeMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsForbiddenDecisionName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Contains("Allowed", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Approved", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Authorized", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Executable", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ReadyToRun", StringComparison.OrdinalIgnoreCase)
            || value.Contains("PermissionGranted", StringComparison.OrdinalIgnoreCase)
            || value.Contains("PolicySatisfied", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ApprovalSatisfied", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ExecutionGranted", StringComparison.OrdinalIgnoreCase)
            || value.Contains("CanExecute", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ApplyAllowed", StringComparison.OrdinalIgnoreCase)
            || value.Contains("PromotionAllowed", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ReleaseApproved", StringComparison.OrdinalIgnoreCase);
    }
}
