using System.Text.Json;

namespace IronDev.Core.Policy;

public static class ApprovalPackageStatuses
{
    public const string Draft = "Draft";
    public const string ReadyForReview = "ReadyForReview";
    public const string Superseded = "Superseded";
    public const string Cancelled = "Cancelled";
    public const string Expired = "Expired";

    public static IReadOnlyList<string> All { get; } =
    [
        Draft,
        ReadyForReview,
        Superseded,
        Cancelled,
        Expired
    ];

    public static IReadOnlyList<string> Forbidden { get; } =
    [
        "Approved",
        "Authorized",
        "ExecutionAllowed",
        "PolicySatisfied",
        "ReleaseReady",
        "CanShip",
        "SourceApplyAllowed",
        "MemoryPromotionAllowed"
    ];

    public static bool IsAllowed(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && All.Any(status => string.Equals(status, value.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool IsForbidden(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Forbidden.Any(status => string.Equals(status, value.Trim(), StringComparison.OrdinalIgnoreCase));
}

public static class ApprovalPackageEvidenceTypes
{
    public const string GovernanceEvent = "GovernanceEvent";
    public const string ToolRequest = "ToolRequest";
    public const string ToolGateDecision = "ToolGateDecision";
    public const string PolicyDecisionEvent = "PolicyDecisionEvent";
    public const string DogfoodReceipt = "DogfoodReceipt";
    public const string ThoughtLedgerReference = "ThoughtLedgerReference";
    public const string RunReport = "RunReport";
    public const string ValidationOutput = "ValidationOutput";
    public const string HumanNote = "HumanNote";

    public static IReadOnlyList<string> All { get; } =
    [
        GovernanceEvent,
        ToolRequest,
        ToolGateDecision,
        PolicyDecisionEvent,
        DogfoodReceipt,
        ThoughtLedgerReference,
        RunReport,
        ValidationOutput,
        HumanNote
    ];

    public static IReadOnlyList<string> Forbidden { get; } =
    [
        "Approval",
        "ApprovalDecisionSatisfied",
        "ExecutionPermission",
        "PolicySatisfied",
        "ReleaseApproval",
        "MemoryPromotionApproval",
        "SourceApplyApproval"
    ];

    public static bool IsAllowed(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && All.Any(type => string.Equals(type, value.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool IsForbidden(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Forbidden.Any(type => string.Equals(type, value.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed record ApprovalPackage
{
    public required Guid ApprovalPackageId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string PackageName { get; init; }
    public required int PackageVersion { get; init; }
    public required string Status { get; init; }
    public required string ApprovalScope { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public string? ActionName { get; init; }
    public required Guid SourceEvaluationId { get; init; }
    public required string SourceEvaluationOutcome { get; init; }
    public required IReadOnlyList<ApprovalPackageRequirement> Requirements { get; init; }
    public required IReadOnlyList<ApprovalPackageEvidenceReference> EvidenceReferences { get; init; }
    public Guid? SupersedesPackageId { get; init; }
    public required string CreatedByActorType { get; init; }
    public required string CreatedByActorId { get; init; }
    public required int MetadataVersion { get; init; }
    public required string MetadataJson { get; init; }
    public required bool GrantsApproval { get; init; }
    public required bool GrantsExecution { get; init; }
    public required bool MutatesSource { get; init; }
    public required bool PromotesMemory { get; init; }
    public required bool StartsWorkflow { get; init; }
    public required bool SatisfiesPolicy { get; init; }
    public required bool TransfersAuthority { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record ApprovalPackageCreateRequest
{
    public required Guid ProjectId { get; init; }
    public required string PackageName { get; init; }
    public required int PackageVersion { get; init; }
    public required string Status { get; init; }
    public required string ApprovalScope { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public string? ActionName { get; init; }
    public required Guid SourceEvaluationId { get; init; }
    public required string SourceEvaluationOutcome { get; init; }
    public required IReadOnlyList<ApprovalPackageRequirement> Requirements { get; init; }
    public required IReadOnlyList<ApprovalPackageEvidenceReference> EvidenceReferences { get; init; }
    public Guid? SupersedesPackageId { get; init; }
    public required string CreatedByActorType { get; init; }
    public required string CreatedByActorId { get; init; }
    public required int MetadataVersion { get; init; }
    public required string MetadataJson { get; init; }
}

public sealed record ApprovalPackageSummary
{
    public required Guid ApprovalPackageId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string PackageName { get; init; }
    public required int PackageVersion { get; init; }
    public required string Status { get; init; }
    public required string ApprovalScope { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required string SourceEvaluationOutcome { get; init; }
    public required int RequirementCount { get; init; }
    public required int EvidenceReferenceCount { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record ApprovalPackageItem
{
    public required string ItemId { get; init; }
    public required string ApprovalScope { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public string? ActionName { get; init; }
    public required string ItemLabel { get; init; }
    public string? ItemSummary { get; init; }
    public required IReadOnlyList<ApprovalPackageEvidenceReference> EvidenceReferences { get; init; }
}

public sealed record ApprovalPackageRequirement
{
    public required string ApprovalScope { get; init; }
    public required string ApprovalType { get; init; }
    public required string RiskLevel { get; init; }
    public required IReadOnlyList<string> RequiredApproverTypes { get; init; }
    public int? QuorumCount { get; init; }
    public required string RequirementCode { get; init; }
    public string? RequirementReason { get; init; }
    public Guid? SourceRuleId { get; init; }
}

public sealed record ApprovalPackageEvidenceReference
{
    public required string EvidenceType { get; init; }
    public required string EvidenceId { get; init; }
    public string? EvidenceLabel { get; init; }
    public string? EvidenceSummary { get; init; }
    public Guid? GovernanceEventId { get; init; }
}

public sealed record ApprovalPackageValidationIssue
{
    public required string Code { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}

public sealed record ApprovalPackageValidationResult
{
    public required IReadOnlyList<ApprovalPackageValidationIssue> Issues { get; init; }
    public bool IsValid => Issues.Count == 0;

    public static ApprovalPackageValidationResult From(IReadOnlyList<ApprovalPackageValidationIssue> issues) =>
        new() { Issues = issues };
}

public static class ApprovalPackageValidator
{
    private const int MaxMetadataJsonLength = 8192;

    private static readonly string[] PrivateReasoningMarkers =
    [
        "hiddenReasoning",
        "hidden reasoning",
        "chainOfThought",
        "chain of thought",
        "chain-of-thought",
        "privateReasoning",
        "private reasoning",
        "scratchpad",
        "rawPrompt",
        "raw prompt",
        "rawCompletion",
        "raw completion",
        "rawToolOutput",
        "raw tool output",
        "entirePatch",
        "entire patch"
    ];

    private static readonly string[] AuthorityWordingMarkers =
    [
        "approved",
        "authorized",
        "canExecute",
        "CanExecute",
        "can_execute",
        "can execute",
        "ExecutionAllowed",
        "execution_allowed",
        "execution allowed",
        "ready to run",
        "permission granted",
        "policySatisfied",
        "PolicySatisfied",
        "policy_satisfied",
        "policy satisfied",
        "releaseReady",
        "ReleaseReady",
        "release_ready",
        "release ready",
        "canShip",
        "CanShip",
        "can_ship",
        "can ship",
        "sourceApplyAllowed",
        "SourceApplyAllowed",
        "source_apply_allowed",
        "source apply allowed",
        "applyAllowed",
        "ApplyAllowed",
        "apply_allowed",
        "apply allowed",
        "memoryPromotionAllowed",
        "MemoryPromotionAllowed",
        "memory_promotion_allowed",
        "memory promotion allowed",
        "promotionAllowed",
        "PromotionAllowed",
        "promotion_allowed",
        "promotion allowed",
        "approval granted",
        "grants approval",
        "grants execution",
        "satisfies policy",
        "starts workflow",
        "continues workflow",
        "transfers authority"
    ];

    private static readonly HashSet<string> AlwaysForbiddenMetadataProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "approved",
        "authorized",
        "canExecute",
        "executionAllowed",
        "policySatisfied",
        "releaseReady",
        "canShip",
        "sourceApplyAllowed",
        "applyAllowed",
        "memoryPromotionAllowed",
        "promotionAllowed",
        "approvalGranted",
        "permissionGranted",
        "readyToRun"
    };

    private static readonly HashSet<string> AuthorityFlagProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "grantsApproval",
        "grantsExecution",
        "mutatesSource",
        "promotesMemory",
        "startsWorkflow",
        "satisfiesPolicy",
        "transfersAuthority"
    };

    public static ApprovalPackageValidationResult ValidateCreate(ApprovalPackageCreateRequest? request)
    {
        var issues = new List<ApprovalPackageValidationIssue>();
        if (request is null)
        {
            Add(issues, "REQUEST_REQUIRED", "request", "Approval package create request is required.");
            return ApprovalPackageValidationResult.From(issues);
        }

        ValidateCommon(
            issues,
            request.ProjectId,
            request.PackageName,
            request.PackageVersion,
            request.Status,
            request.ApprovalScope,
            request.SubjectType,
            request.SubjectId,
            request.SourceEvaluationId,
            request.SourceEvaluationOutcome,
            request.Requirements,
            request.EvidenceReferences,
            request.CreatedByActorType,
            request.CreatedByActorId,
            request.MetadataVersion,
            request.MetadataJson);

        return ApprovalPackageValidationResult.From(issues);
    }

    public static ApprovalPackageValidationResult Validate(ApprovalPackage? package)
    {
        var issues = new List<ApprovalPackageValidationIssue>();
        if (package is null)
        {
            Add(issues, "PACKAGE_REQUIRED", "package", "Approval package is required.");
            return ApprovalPackageValidationResult.From(issues);
        }

        if (package.ApprovalPackageId == Guid.Empty)
        {
            Add(issues, "APPROVAL_PACKAGE_ID_REQUIRED", nameof(package.ApprovalPackageId), "Approval package ID is required.");
        }

        if (package.CreatedUtc == default)
        {
            Add(issues, "CREATED_UTC_REQUIRED", nameof(package.CreatedUtc), "Created UTC timestamp is required.");
        }

        ValidateCommon(
            issues,
            package.ProjectId,
            package.PackageName,
            package.PackageVersion,
            package.Status,
            package.ApprovalScope,
            package.SubjectType,
            package.SubjectId,
            package.SourceEvaluationId,
            package.SourceEvaluationOutcome,
            package.Requirements,
            package.EvidenceReferences,
            package.CreatedByActorType,
            package.CreatedByActorId,
            package.MetadataVersion,
            package.MetadataJson);

        ValidateAuthorityFlags(issues, package);

        return ApprovalPackageValidationResult.From(issues);
    }

    public static bool IsAllowedStatus(string? value) => ApprovalPackageStatuses.IsAllowed(value);

    public static bool IsForbiddenStatus(string? value) => ApprovalPackageStatuses.IsForbidden(value);

    public static bool IsAllowedEvidenceType(string? value) => ApprovalPackageEvidenceTypes.IsAllowed(value);

    public static bool IsForbiddenEvidenceType(string? value) => ApprovalPackageEvidenceTypes.IsForbidden(value);

    private static void ValidateCommon(
        List<ApprovalPackageValidationIssue> issues,
        Guid projectId,
        string? packageName,
        int packageVersion,
        string? status,
        string? approvalScope,
        string? subjectType,
        string? subjectId,
        Guid sourceEvaluationId,
        string? sourceEvaluationOutcome,
        IReadOnlyList<ApprovalPackageRequirement>? requirements,
        IReadOnlyList<ApprovalPackageEvidenceReference>? evidenceReferences,
        string? createdByActorType,
        string? createdByActorId,
        int metadataVersion,
        string? metadataJson)
    {
        if (projectId == Guid.Empty)
        {
            Add(issues, "PROJECT_ID_REQUIRED", "ProjectId", "Project ID is required.");
        }

        if (string.IsNullOrWhiteSpace(packageName))
        {
            Add(issues, "PACKAGE_NAME_REQUIRED", "PackageName", "Package name is required.");
        }

        if (packageVersion <= 0)
        {
            Add(issues, "PACKAGE_VERSION_REQUIRED", "PackageVersion", "Package version must be positive.");
        }

        ValidateStatus(issues, status);
        ValidateApprovalScope(issues, approvalScope);

        if (string.IsNullOrWhiteSpace(subjectType))
        {
            Add(issues, "SUBJECT_TYPE_REQUIRED", "SubjectType", "Subject type is required.");
        }

        if (string.IsNullOrWhiteSpace(subjectId))
        {
            Add(issues, "SUBJECT_ID_REQUIRED", "SubjectId", "Subject ID is required.");
        }

        if (sourceEvaluationId == Guid.Empty)
        {
            Add(issues, "SOURCE_EVALUATION_ID_REQUIRED", "SourceEvaluationId", "Source evaluation ID is required.");
        }

        ValidateSourceEvaluationOutcome(issues, sourceEvaluationOutcome, requirements);
        ValidateRequirements(issues, requirements);
        ValidateEvidenceReferences(issues, evidenceReferences);

        if (string.IsNullOrWhiteSpace(createdByActorType))
        {
            Add(issues, "ACTOR_TYPE_REQUIRED", "CreatedByActorType", "Created-by actor type is required.");
        }

        if (string.IsNullOrWhiteSpace(createdByActorId))
        {
            Add(issues, "ACTOR_ID_REQUIRED", "CreatedByActorId", "Created-by actor ID is required.");
        }

        ValidateMetadata(issues, metadataVersion, metadataJson);
    }

    private static void ValidateStatus(List<ApprovalPackageValidationIssue> issues, string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            Add(issues, "STATUS_REQUIRED", "Status", "Package status is required.");
            return;
        }

        if (ApprovalPackageStatuses.IsForbidden(status) || ContainsAny(status, AuthorityWordingMarkers))
        {
            Add(issues, "STATUS_FORBIDDEN", "Status", "Package status cannot imply approval, execution, policy satisfaction, release readiness, source apply, or memory promotion.");
            return;
        }

        if (!ApprovalPackageStatuses.IsAllowed(status))
        {
            Add(issues, "STATUS_UNKNOWN", "Status", "Package status is not part of the bounded vocabulary.");
        }
    }

    private static void ValidateApprovalScope(List<ApprovalPackageValidationIssue> issues, string? approvalScope)
    {
        if (string.IsNullOrWhiteSpace(approvalScope))
        {
            Add(issues, "APPROVAL_SCOPE_REQUIRED", "ApprovalScope", "Approval scope is required.");
            return;
        }

        if (!ProjectApprovalRuleScopes.IsAllowed(approvalScope))
        {
            Add(issues, "APPROVAL_SCOPE_UNKNOWN", "ApprovalScope", "Approval scope is not part of the bounded vocabulary.");
        }
    }

    private static void ValidateSourceEvaluationOutcome(
        List<ApprovalPackageValidationIssue> issues,
        string? sourceEvaluationOutcome,
        IReadOnlyList<ApprovalPackageRequirement>? requirements)
    {
        if (string.IsNullOrWhiteSpace(sourceEvaluationOutcome))
        {
            Add(issues, "SOURCE_EVALUATION_OUTCOME_REQUIRED", "SourceEvaluationOutcome", "Source evaluation outcome is required.");
            return;
        }

        if (ContainsAny(sourceEvaluationOutcome, AuthorityWordingMarkers))
        {
            Add(issues, "SOURCE_EVALUATION_OUTCOME_AUTHORITY", "SourceEvaluationOutcome", "Source evaluation outcome cannot imply approval, execution, policy satisfaction, release readiness, source apply, or memory promotion.");
            return;
        }

        if (!ApprovalRequirementOutcomes.All.Any(outcome => string.Equals(outcome, sourceEvaluationOutcome.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            Add(issues, "SOURCE_EVALUATION_OUTCOME_UNKNOWN", "SourceEvaluationOutcome", "Source evaluation outcome is not part of the bounded evaluator vocabulary.");
            return;
        }

        if (string.Equals(sourceEvaluationOutcome, ApprovalRequirementOutcomes.ApprovalRequired, StringComparison.OrdinalIgnoreCase)
            && (requirements is null || requirements.Count == 0))
        {
            Add(issues, "APPROVAL_REQUIRED_WITHOUT_REQUIREMENTS", "Requirements", "ApprovalRequired packages must carry at least one approval requirement.");
        }
    }

    private static void ValidateRequirements(List<ApprovalPackageValidationIssue> issues, IReadOnlyList<ApprovalPackageRequirement>? requirements)
    {
        if (requirements is null)
        {
            Add(issues, "REQUIREMENTS_REQUIRED", "Requirements", "Requirement collection is required.");
            return;
        }

        for (var index = 0; index < requirements.Count; index++)
        {
            ValidateRequirement(issues, requirements[index], $"Requirements[{index}]");
        }
    }

    private static void ValidateRequirement(List<ApprovalPackageValidationIssue> issues, ApprovalPackageRequirement? requirement, string path)
    {
        if (requirement is null)
        {
            Add(issues, "REQUIREMENT_REQUIRED", path, "Approval package requirement is required.");
            return;
        }

        ValidateApprovalScope(issues, requirement.ApprovalScope);

        var sensitive = ProjectApprovalRuleScopes.IsSensitive(requirement.ApprovalScope);
        var approvalTypeAllowed = ProjectApprovalRuleApprovalTypes.IsAllowed(requirement.ApprovalType);
        var normalizedApprovalType = approvalTypeAllowed ? ProjectApprovalRuleApprovalTypes.Normalize(requirement.ApprovalType) : null;

        if (string.IsNullOrWhiteSpace(requirement.ApprovalType))
        {
            Add(issues, "REQUIREMENT_APPROVAL_TYPE_REQUIRED", $"{path}.ApprovalType", "Requirement approval type is required.");
        }
        else if (!approvalTypeAllowed)
        {
            Add(issues, "REQUIREMENT_APPROVAL_TYPE_UNKNOWN", $"{path}.ApprovalType", "Requirement approval type is not part of the bounded vocabulary.");
        }

        if (string.IsNullOrWhiteSpace(requirement.RiskLevel))
        {
            Add(issues, "REQUIREMENT_RISK_LEVEL_REQUIRED", $"{path}.RiskLevel", "Requirement risk level is required.");
        }
        else if (!ProjectApprovalRuleRiskLevels.IsAllowed(requirement.RiskLevel))
        {
            Add(issues, "REQUIREMENT_RISK_LEVEL_UNKNOWN", $"{path}.RiskLevel", "Requirement risk level is not part of the bounded vocabulary.");
        }

        ValidateApprovers(issues, requirement.RequiredApproverTypes, normalizedApprovalType, sensitive, path);
        ValidateQuorum(issues, normalizedApprovalType, requirement.RequiredApproverTypes ?? [], requirement.QuorumCount, path);
        ValidateRequirementText(issues, requirement.RequirementCode, $"{path}.RequirementCode", required: true);
        ValidateRequirementText(issues, requirement.RequirementReason, $"{path}.RequirementReason", required: false);

        if (sensitive && string.Equals(normalizedApprovalType, ProjectApprovalRuleApprovalTypes.None, StringComparison.OrdinalIgnoreCase))
        {
            Add(issues, "REQUIREMENT_SENSITIVE_NONE_FORBIDDEN", $"{path}.ApprovalType", "Sensitive requirements cannot use ApprovalType=None.");
        }
    }

    private static void ValidateApprovers(
        List<ApprovalPackageValidationIssue> issues,
        IReadOnlyList<string>? approverTypes,
        string? normalizedApprovalType,
        bool sensitive,
        string path)
    {
        if (approverTypes is null)
        {
            Add(issues, "REQUIREMENT_APPROVERS_REQUIRED", $"{path}.RequiredApproverTypes", "Requirement approver collection is required.");
            return;
        }

        if (normalizedApprovalType is not null
            && ProjectApprovalRuleApprovalTypes.RequiresApprovers(normalizedApprovalType)
            && approverTypes.Count == 0)
        {
            Add(issues, "REQUIREMENT_APPROVERS_REQUIRED", $"{path}.RequiredApproverTypes", "Requirement approval type requires at least one approver type.");
        }

        var normalized = new List<string>();
        for (var index = 0; index < approverTypes.Count; index++)
        {
            var approverType = approverTypes[index];
            if (string.IsNullOrWhiteSpace(approverType))
            {
                Add(issues, "REQUIREMENT_APPROVER_BLANK", $"{path}.RequiredApproverTypes[{index}]", "Requirement approver type cannot be blank.");
                continue;
            }

            if (ProjectApprovalRuleApproverTypes.IsForbidden(approverType))
            {
                Add(issues, "REQUIREMENT_APPROVER_FORBIDDEN", $"{path}.RequiredApproverTypes[{index}]", "Requirement approver type is evidence or infrastructure, not an approver.");
                continue;
            }

            if (!ProjectApprovalRuleApproverTypes.IsAllowed(approverType))
            {
                Add(issues, "REQUIREMENT_APPROVER_UNKNOWN", $"{path}.RequiredApproverTypes[{index}]", "Requirement approver type is not part of the bounded vocabulary.");
                continue;
            }

            normalized.Add(ProjectApprovalRuleApproverTypes.Normalize(approverType));
        }

        if (sensitive && normalized.Any(ProjectApprovalRuleApproverTypes.IsAutomated))
        {
            Add(issues, "REQUIREMENT_SENSITIVE_AUTOMATED_APPROVER", $"{path}.RequiredApproverTypes", "Sensitive requirements cannot use System or Agent approver types.");
        }

        if (sensitive && !normalized.Any(ProjectApprovalRuleApproverTypes.IsHumanClass))
        {
            Add(issues, "REQUIREMENT_SENSITIVE_HUMAN_APPROVER_REQUIRED", $"{path}.RequiredApproverTypes", "Sensitive requirements require a human approver class.");
        }
    }

    private static void ValidateQuorum(
        List<ApprovalPackageValidationIssue> issues,
        string? normalizedApprovalType,
        IReadOnlyList<string> approverTypes,
        int? quorumCount,
        string path)
    {
        if (string.Equals(normalizedApprovalType, ProjectApprovalRuleApprovalTypes.Quorum, StringComparison.OrdinalIgnoreCase))
        {
            if (quorumCount is null or <= 0)
            {
                Add(issues, "REQUIREMENT_QUORUM_REQUIRED", $"{path}.QuorumCount", "Quorum requirements must set a positive quorum count.");
                return;
            }

            if (quorumCount.Value > approverTypes.Count)
            {
                Add(issues, "REQUIREMENT_QUORUM_EXCEEDS_APPROVERS", $"{path}.QuorumCount", "Quorum count cannot exceed approver type count.");
            }

            return;
        }

        if (quorumCount.HasValue)
        {
            Add(issues, "REQUIREMENT_QUORUM_NOT_ALLOWED", $"{path}.QuorumCount", "Non-quorum requirements cannot set quorum count.");
        }
    }

    private static void ValidateRequirementText(List<ApprovalPackageValidationIssue> issues, string? value, string field, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                Add(issues, "REQUIREMENT_TEXT_REQUIRED", field, "Requirement text is required.");
            }

            return;
        }

        if (ContainsAny(value, PrivateReasoningMarkers))
        {
            Add(issues, "REQUIREMENT_PRIVATE_REASONING", field, "Requirement text cannot contain hidden or private reasoning markers.");
        }

        if (ContainsAny(value, AuthorityWordingMarkers))
        {
            Add(issues, "REQUIREMENT_AUTHORITY_WORDING", field, "Requirement text cannot imply approval, execution, policy satisfaction, release readiness, source apply, or memory promotion.");
        }
    }

    private static void ValidateEvidenceReferences(List<ApprovalPackageValidationIssue> issues, IReadOnlyList<ApprovalPackageEvidenceReference>? evidenceReferences)
    {
        if (evidenceReferences is null)
        {
            Add(issues, "EVIDENCE_REFERENCES_REQUIRED", "EvidenceReferences", "Evidence reference collection is required.");
            return;
        }

        for (var index = 0; index < evidenceReferences.Count; index++)
        {
            ValidateEvidenceReference(issues, evidenceReferences[index], $"EvidenceReferences[{index}]");
        }
    }

    private static void ValidateEvidenceReference(List<ApprovalPackageValidationIssue> issues, ApprovalPackageEvidenceReference? evidenceReference, string path)
    {
        if (evidenceReference is null)
        {
            Add(issues, "EVIDENCE_REFERENCE_REQUIRED", path, "Evidence reference is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(evidenceReference.EvidenceType))
        {
            Add(issues, "EVIDENCE_TYPE_REQUIRED", $"{path}.EvidenceType", "Evidence type is required.");
        }
        else if (ApprovalPackageEvidenceTypes.IsForbidden(evidenceReference.EvidenceType))
        {
            Add(issues, "EVIDENCE_TYPE_FORBIDDEN", $"{path}.EvidenceType", "Evidence type cannot represent approval, execution permission, policy satisfaction, release approval, source apply approval, or memory promotion approval.");
        }
        else if (!ApprovalPackageEvidenceTypes.IsAllowed(evidenceReference.EvidenceType))
        {
            Add(issues, "EVIDENCE_TYPE_UNKNOWN", $"{path}.EvidenceType", "Evidence type is not part of the bounded vocabulary.");
        }

        if (string.IsNullOrWhiteSpace(evidenceReference.EvidenceId))
        {
            Add(issues, "EVIDENCE_ID_REQUIRED", $"{path}.EvidenceId", "Evidence ID is required.");
        }

        ValidateOptionalEvidenceText(issues, evidenceReference.EvidenceLabel, $"{path}.EvidenceLabel");
        ValidateOptionalEvidenceText(issues, evidenceReference.EvidenceSummary, $"{path}.EvidenceSummary");
    }

    private static void ValidateOptionalEvidenceText(List<ApprovalPackageValidationIssue> issues, string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (ContainsAny(value, PrivateReasoningMarkers))
        {
            Add(issues, "EVIDENCE_PRIVATE_REASONING", field, "Evidence text cannot contain hidden or private reasoning markers.");
        }

        if (ContainsAny(value, AuthorityWordingMarkers))
        {
            Add(issues, "EVIDENCE_AUTHORITY_WORDING", field, "Evidence text cannot imply approval, execution, policy satisfaction, release readiness, source apply, or memory promotion.");
        }
    }

    private static void ValidateMetadata(List<ApprovalPackageValidationIssue> issues, int metadataVersion, string? metadataJson)
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

    private static void ScanMetadataElement(List<ApprovalPackageValidationIssue> issues, JsonElement element, string path)
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

                if (ContainsAny(value, AuthorityWordingMarkers))
                {
                    Add(issues, "METADATA_AUTHORITY_WORDING", path, "Metadata string cannot contain approval/execution authority wording.");
                }

                break;
        }
    }

    private static void ValidateAuthorityFlags(List<ApprovalPackageValidationIssue> issues, ApprovalPackage package)
    {
        if (package.GrantsApproval)
        {
            Add(issues, "AUTHORITY_FLAG_TRUE", nameof(package.GrantsApproval), "Approval packages cannot grant approval.");
        }

        if (package.GrantsExecution)
        {
            Add(issues, "AUTHORITY_FLAG_TRUE", nameof(package.GrantsExecution), "Approval packages cannot grant execution.");
        }

        if (package.MutatesSource)
        {
            Add(issues, "AUTHORITY_FLAG_TRUE", nameof(package.MutatesSource), "Approval packages cannot mutate source.");
        }

        if (package.PromotesMemory)
        {
            Add(issues, "AUTHORITY_FLAG_TRUE", nameof(package.PromotesMemory), "Approval packages cannot promote memory.");
        }

        if (package.StartsWorkflow)
        {
            Add(issues, "AUTHORITY_FLAG_TRUE", nameof(package.StartsWorkflow), "Approval packages cannot start workflow.");
        }

        if (package.SatisfiesPolicy)
        {
            Add(issues, "AUTHORITY_FLAG_TRUE", nameof(package.SatisfiesPolicy), "Approval packages cannot satisfy policy.");
        }

        if (package.TransfersAuthority)
        {
            Add(issues, "AUTHORITY_FLAG_TRUE", nameof(package.TransfersAuthority), "Approval packages cannot transfer authority.");
        }
    }

    private static bool ContainsAny(string value, IEnumerable<string> markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static void Add(List<ApprovalPackageValidationIssue> issues, string code, string field, string message) =>
        issues.Add(new ApprovalPackageValidationIssue
        {
            Code = code,
            Field = field,
            Message = message
        });
}
