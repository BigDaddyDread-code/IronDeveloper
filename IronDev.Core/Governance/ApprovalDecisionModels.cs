using System.Text.Json;

namespace IronDev.Core.Governance;

public enum ApprovalDecisionValue
{
    Approved = 1,
    Rejected = 2,
    Revoked = 3,
    Expired = 4
}

public static class ApprovalDecisionScopes
{
    public const string ToolExecution = "tool_execution";
    public const string SourceApply = "source_apply";
    public const string MemoryPromotion = "memory_promotion";
    public const string ProposalAcceptance = "proposal_acceptance";
    public const string ReleaseReadiness = "release_readiness";
    public const string ExternalSideEffect = "external_side_effect";
    public const string DestructiveOperation = "destructive_operation";
}

public sealed record ApprovalDecisionRecordRequest
{
    public required Guid ProjectId { get; init; }
    public required string ApprovalScope { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required string Decision { get; init; }
    public required string ReasonCode { get; init; }
    public string? Reason { get; init; }
    public required string DecidedByActorType { get; init; }
    public required string DecidedByActorId { get; init; }
    public Guid? SupersedesApprovalDecisionId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required int EvidenceVersion { get; init; }
    public required string EvidenceJson { get; init; }
    public Guid? ApprovalDecisionId { get; init; }
    public Guid? GovernanceEventId { get; init; }
    public DateTimeOffset? CreatedUtc { get; init; }
}

public sealed record ApprovalDecisionReadModel
{
    public required Guid ApprovalDecisionId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid GovernanceEventId { get; init; }
    public required string ApprovalScope { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required string Decision { get; init; }
    public required string ReasonCode { get; init; }
    public string? Reason { get; init; }
    public required string DecidedByActorType { get; init; }
    public required string DecidedByActorId { get; init; }
    public Guid? SupersedesApprovalDecisionId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required int EvidenceVersion { get; init; }
    public required string EvidenceJson { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record ApprovalDecisionSummary
{
    public required Guid ApprovalDecisionId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid GovernanceEventId { get; init; }
    public required string ApprovalScope { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required string Decision { get; init; }
    public required string ReasonCode { get; init; }
    public required string DecidedByActorType { get; init; }
    public required string DecidedByActorId { get; init; }
    public Guid? SupersedesApprovalDecisionId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record ApprovalDecisionsForSubjectQuery
{
    public required Guid ProjectId { get; init; }
    public required string ApprovalScope { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public int Take { get; init; } = ApprovalDecisionValidator.DefaultTake;
}

public sealed record ApprovalDecisionsForProjectQuery
{
    public required Guid ProjectId { get; init; }
    public int Take { get; init; } = ApprovalDecisionValidator.DefaultTake;
}

public sealed record ApprovalDecisionsForCorrelationQuery
{
    public required Guid ProjectId { get; init; }
    public required Guid CorrelationId { get; init; }
    public int Take { get; init; } = ApprovalDecisionValidator.DefaultTake;
}

public sealed record ApprovalDecisionValidationIssue(string Code, string Message);

public sealed record ApprovalDecisionValidationResult(IReadOnlyList<ApprovalDecisionValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public interface IApprovalDecisionStore
{
    Task<ApprovalDecisionReadModel> RecordAsync(ApprovalDecisionRecordRequest request, CancellationToken cancellationToken = default);

    Task<ApprovalDecisionReadModel?> GetAsync(Guid approvalDecisionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApprovalDecisionSummary>> ListForSubjectAsync(ApprovalDecisionsForSubjectQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApprovalDecisionSummary>> ListForProjectAsync(ApprovalDecisionsForProjectQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApprovalDecisionSummary>> ListForCorrelationAsync(ApprovalDecisionsForCorrelationQuery query, CancellationToken cancellationToken = default);
}

public sealed class ApprovalDecisionValidator
{
    public const int DefaultTake = 100;
    public const int MaxTake = 500;
    public const int MaxEvidenceJsonLength = 32_000;

    private static readonly string[] ValidDecisions =
    {
        nameof(ApprovalDecisionValue.Approved),
        nameof(ApprovalDecisionValue.Rejected),
        nameof(ApprovalDecisionValue.Revoked),
        nameof(ApprovalDecisionValue.Expired)
    };

    private static readonly string[] AllowedActorTypes =
    {
        "human",
        "system_test_fixture"
    };

    private static readonly string[] SensitiveScopes =
    {
        ApprovalDecisionScopes.SourceApply,
        ApprovalDecisionScopes.MemoryPromotion,
        ApprovalDecisionScopes.ReleaseReadiness,
        ApprovalDecisionScopes.ExternalSideEffect,
        ApprovalDecisionScopes.DestructiveOperation
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
        "approved for execution",
        "execution permission",
        "execution permitted",
        "permission granted to execute",
        "authorized for execution",
        "ready to run",
        "ready-to-run",
        "executable without gate",
        "tool executed",
        "start workflow",
        "workflow started",
        "source applied",
        "apply patch",
        "memory promoted",
        "collective memory accepted",
        "create pull request",
        "submit github review",
        "a2a handoff approved"
    };

    public ApprovalDecisionValidationResult ValidateRecord(ApprovalDecisionRecordRequest? request)
    {
        var issues = new List<ApprovalDecisionValidationIssue>();

        if (request is null)
        {
            issues.Add(new ApprovalDecisionValidationIssue("APPROVAL_DECISION_REQUEST_REQUIRED", "Approval decision request is required."));
            return new ApprovalDecisionValidationResult(issues);
        }

        if (request.ProjectId == Guid.Empty)
            issues.Add(new ApprovalDecisionValidationIssue("PROJECT_REQUIRED", "ProjectId is required."));

        ValidateRequiredText(request.ApprovalScope, "APPROVAL_SCOPE_REQUIRED", "ApprovalScope is required.", issues);
        ValidateRequiredText(request.SubjectType, "SUBJECT_TYPE_REQUIRED", "SubjectType is required.", issues);
        ValidateRequiredText(request.SubjectId, "SUBJECT_ID_REQUIRED", "SubjectId is required.", issues);
        ValidateRequiredText(request.ReasonCode, "REASON_CODE_REQUIRED", "ReasonCode is required.", issues);
        ValidateRequiredText(request.DecidedByActorId, "ACTOR_ID_REQUIRED", "DecidedByActorId is required.", issues);

        if (string.IsNullOrWhiteSpace(request.Decision) || !ValidDecisions.Any(value => string.Equals(value, request.Decision, StringComparison.OrdinalIgnoreCase)))
            issues.Add(new ApprovalDecisionValidationIssue("DECISION_INVALID", "Decision must be Approved, Rejected, Revoked, or Expired."));

        if (ContainsForbiddenDecisionName(request.Decision))
            issues.Add(new ApprovalDecisionValidationIssue("DECISION_EXECUTION_LANGUAGE_FORBIDDEN", "Decision must not use execution, runtime, source apply, or memory promotion language."));

        if (string.IsNullOrWhiteSpace(request.DecidedByActorType) || !AllowedActorTypes.Any(value => string.Equals(value, request.DecidedByActorType, StringComparison.OrdinalIgnoreCase)))
            issues.Add(new ApprovalDecisionValidationIssue("ACTOR_TYPE_INVALID", "DecidedByActorType must be human or system_test_fixture."));

        var normalizedDecision = NormalizeDecision(request.Decision);
        var normalizedScope = NormalizeText(request.ApprovalScope);
        var normalizedActorType = NormalizeActorType(request.DecidedByActorType);

        if ((string.Equals(normalizedDecision, nameof(ApprovalDecisionValue.Revoked), StringComparison.Ordinal)
                || string.Equals(normalizedDecision, nameof(ApprovalDecisionValue.Expired), StringComparison.Ordinal))
            && request.SupersedesApprovalDecisionId is null)
        {
            issues.Add(new ApprovalDecisionValidationIssue("SUPERSEDES_REQUIRED", "Revoked and Expired approval decisions must reference a prior approval decision."));
        }

        if (string.Equals(normalizedDecision, nameof(ApprovalDecisionValue.Approved), StringComparison.Ordinal)
            && SensitiveScopes.Any(scope => string.Equals(scope, normalizedScope, StringComparison.OrdinalIgnoreCase))
            && !string.Equals(normalizedActorType, "human", StringComparison.Ordinal))
        {
            issues.Add(new ApprovalDecisionValidationIssue("SENSITIVE_APPROVAL_REQUIRES_HUMAN", "Sensitive approval scopes require a human actor."));
        }

        if (request.EvidenceVersion <= 0)
            issues.Add(new ApprovalDecisionValidationIssue("EVIDENCE_VERSION_INVALID", "EvidenceVersion must be positive."));

        ValidateUnsafeText(request.ApprovalScope, "APPROVAL_SCOPE_UNSAFE", "ApprovalScope must not claim execution, source mutation, or memory promotion authority.", issues);
        ValidateUnsafeText(request.SubjectType, "SUBJECT_TYPE_UNSAFE", "SubjectType must not claim execution, source mutation, or memory promotion authority.", issues);
        ValidateUnsafeText(request.SubjectId, "SUBJECT_ID_UNSAFE", "SubjectId must not claim execution, source mutation, or memory promotion authority.", issues);
        ValidateUnsafeText(request.ReasonCode, "REASON_CODE_UNSAFE", "ReasonCode must not claim execution, source mutation, or memory promotion authority.", issues);
        ValidateUnsafeText(request.Reason, "REASON_UNSAFE", "Reason must not contain raw/private reasoning or execution authority claims.", issues);
        ValidateEvidenceJson(request.EvidenceJson, issues);

        return new ApprovalDecisionValidationResult(issues);
    }

    public ApprovalDecisionValidationResult ValidateSubjectQuery(ApprovalDecisionsForSubjectQuery query)
    {
        var issues = new List<ApprovalDecisionValidationIssue>();
        ValidateProject(query.ProjectId, issues);
        ValidateRequiredText(query.ApprovalScope, "APPROVAL_SCOPE_REQUIRED", "ApprovalScope is required.", issues);
        ValidateRequiredText(query.SubjectType, "SUBJECT_TYPE_REQUIRED", "SubjectType is required.", issues);
        ValidateRequiredText(query.SubjectId, "SUBJECT_ID_REQUIRED", "SubjectId is required.", issues);
        ValidateTake(query.Take, issues);
        return new ApprovalDecisionValidationResult(issues);
    }

    public ApprovalDecisionValidationResult ValidateProjectQuery(ApprovalDecisionsForProjectQuery query)
    {
        var issues = new List<ApprovalDecisionValidationIssue>();
        ValidateProject(query.ProjectId, issues);
        ValidateTake(query.Take, issues);
        return new ApprovalDecisionValidationResult(issues);
    }

    public ApprovalDecisionValidationResult ValidateCorrelationQuery(ApprovalDecisionsForCorrelationQuery query)
    {
        var issues = new List<ApprovalDecisionValidationIssue>();
        ValidateProject(query.ProjectId, issues);
        if (query.CorrelationId == Guid.Empty)
            issues.Add(new ApprovalDecisionValidationIssue("CORRELATION_REQUIRED", "CorrelationId is required."));
        ValidateTake(query.Take, issues);
        return new ApprovalDecisionValidationResult(issues);
    }

    public static int NormalizeTake(int take) => Math.Clamp(take <= 0 ? DefaultTake : take, 1, MaxTake);

    public static string NormalizeDecision(string decision)
    {
        var match = ValidDecisions.FirstOrDefault(value => string.Equals(value, decision, StringComparison.OrdinalIgnoreCase));
        return match ?? NormalizeText(decision);
    }

    public static string NormalizeActorType(string actorType)
    {
        var match = AllowedActorTypes.FirstOrDefault(value => string.Equals(value, actorType, StringComparison.OrdinalIgnoreCase));
        return match ?? NormalizeText(actorType);
    }

    public static string NormalizeText(string value) => value.Trim();

    private static void ValidateProject(Guid projectId, List<ApprovalDecisionValidationIssue> issues)
    {
        if (projectId == Guid.Empty)
            issues.Add(new ApprovalDecisionValidationIssue("PROJECT_REQUIRED", "ProjectId is required."));
    }

    private static void ValidateRequiredText(string? value, string code, string message, List<ApprovalDecisionValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add(new ApprovalDecisionValidationIssue(code, message));
    }

    private static void ValidateTake(int take, List<ApprovalDecisionValidationIssue> issues)
    {
        if (take < 0)
            issues.Add(new ApprovalDecisionValidationIssue("TAKE_INVALID", "Take must not be negative."));
    }

    private static void ValidateUnsafeText(string? value, string code, string message, List<ApprovalDecisionValidationIssue> issues)
    {
        if (ContainsUnsafeText(value))
            issues.Add(new ApprovalDecisionValidationIssue(code, message));
    }

    private static void ValidateEvidenceJson(string evidenceJson, List<ApprovalDecisionValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(evidenceJson))
        {
            issues.Add(new ApprovalDecisionValidationIssue("EVIDENCE_REQUIRED", "EvidenceJson is required."));
            return;
        }

        if (evidenceJson.Length > MaxEvidenceJsonLength)
            issues.Add(new ApprovalDecisionValidationIssue("EVIDENCE_TOO_LARGE", "EvidenceJson exceeds the maximum allowed length."));

        if (ContainsUnsafeText(evidenceJson))
            issues.Add(new ApprovalDecisionValidationIssue("EVIDENCE_UNSAFE", "EvidenceJson must not contain raw/private reasoning, execution, source mutation, or memory promotion claims."));

        try
        {
            using var document = JsonDocument.Parse(evidenceJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                issues.Add(new ApprovalDecisionValidationIssue("EVIDENCE_OBJECT_REQUIRED", "EvidenceJson must be a JSON object."));
                return;
            }

            var hasSchema = document.RootElement.TryGetProperty("schema", out var schema)
                && schema.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(schema.GetString());
            var hasSchemaVersion = document.RootElement.TryGetProperty("schemaVersion", out var schemaVersion)
                && schemaVersion.ValueKind == JsonValueKind.Number
                && schemaVersion.GetInt32() > 0;

            if (!hasSchema && !hasSchemaVersion)
                issues.Add(new ApprovalDecisionValidationIssue("EVIDENCE_SCHEMA_REQUIRED", "EvidenceJson must include schema or positive schemaVersion."));

            RejectTruthy(document.RootElement, issues, "grantsExecution", "EVIDENCE_GRANTS_EXECUTION_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "executionPermission", "EVIDENCE_EXECUTION_PERMISSION_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "mutatesSource", "EVIDENCE_SOURCE_MUTATION_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "promotesMemory", "EVIDENCE_MEMORY_PROMOTION_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "createsPullRequest", "EVIDENCE_EXTERNAL_EFFECT_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "startsWorkflow", "EVIDENCE_WORKFLOW_FORBIDDEN");
        }
        catch (JsonException)
        {
            issues.Add(new ApprovalDecisionValidationIssue("EVIDENCE_JSON_INVALID", "EvidenceJson must be valid JSON."));
        }
    }

    private static void RejectTruthy(JsonElement element, List<ApprovalDecisionValidationIssue> issues, string propertyName, string code)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True)
            issues.Add(new ApprovalDecisionValidationIssue(code, $"EvidenceJson must not set {propertyName} to true."));
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

        return value.Contains("Executable", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ReadyToRun", StringComparison.OrdinalIgnoreCase)
            || value.Contains("AuthorizedToRun", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ExecutionGranted", StringComparison.OrdinalIgnoreCase)
            || value.Contains("PermissionGranted", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Applied", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Promoted", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Released", StringComparison.OrdinalIgnoreCase)
            || value.Contains("GatePassed", StringComparison.OrdinalIgnoreCase)
            || value.Contains("PolicySatisfied", StringComparison.OrdinalIgnoreCase)
            || value.Contains("AutoApproved", StringComparison.OrdinalIgnoreCase);
    }
}
