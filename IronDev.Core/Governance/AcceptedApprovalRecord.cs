namespace IronDev.Core.Governance;

public static class AcceptedApprovalTargetKinds
{
    public const string PatchArtifact = "patch-artifact";
    public const string SourceApplyRequest = "source-apply-request";
    public const string WorkflowContinuationRequest = "workflow-continuation-request";
    public const string ReleaseReadinessDecision = "release-readiness-decision";
    public const string PolicySatisfactionRequest = "policy-satisfaction-request";

    public static IReadOnlyList<string> Known { get; } =
    [
        PatchArtifact,
        SourceApplyRequest,
        WorkflowContinuationRequest,
        ReleaseReadinessDecision,
        PolicySatisfactionRequest
    ];
}

public static class AcceptedApprovalPurposes
{
    public const string PolicySatisfactionInput = "policy-satisfaction-input";
    public const string SourceApplyInput = "source-apply-input";
    public const string WorkflowContinuationInput = "workflow-continuation-input";
    public const string ReleaseReadinessInput = "release-readiness-input";

    public static IReadOnlyList<string> Known { get; } =
    [
        PolicySatisfactionInput,
        SourceApplyInput,
        WorkflowContinuationInput,
        ReleaseReadinessInput
    ];
}

public sealed record AcceptedApprovalTarget
{
    public required string ApprovalTargetKind { get; init; }
    public required string ApprovalTargetId { get; init; }
    public required string ApprovalTargetHash { get; init; }
    public required Guid ProjectId { get; init; }
    public required string CapabilityCode { get; init; }
}

public sealed record AcceptedApprovalRecord
{
    public required Guid AcceptedApprovalId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string ApprovalTargetKind { get; init; }
    public required string ApprovalTargetId { get; init; }
    public required string ApprovalTargetHash { get; init; }
    public required string CapabilityCode { get; init; }
    public required string ApprovalPurpose { get; init; }
    public required string ApprovedByActorId { get; init; }
    public string? ApprovedByActorDisplayName { get; init; }
    public required DateTimeOffset AcceptedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required string CorrelationId { get; init; }
    public required string CausationId { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
}

public sealed record AcceptedApprovalValidationIssue(string Code, string Field, string Message);

public sealed record AcceptedApprovalValidationResult(IReadOnlyList<AcceptedApprovalValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public static class AcceptedApprovalValidation
{
    public static AcceptedApprovalValidationResult Validate(AcceptedApprovalRecord? record)
    {
        var issues = new List<AcceptedApprovalValidationIssue>();

        if (record is null)
        {
            Add(issues, "ACCEPTED_APPROVAL_RECORD_REQUIRED", "record", "Accepted approval record is required.");
            return new AcceptedApprovalValidationResult(issues);
        }

        if (record.AcceptedApprovalId == Guid.Empty)
        {
            Add(issues, "ACCEPTED_APPROVAL_ID_REQUIRED", nameof(record.AcceptedApprovalId), "Accepted approval ID is required.");
        }

        if (record.ProjectId == Guid.Empty)
        {
            Add(issues, "PROJECT_ID_REQUIRED", nameof(record.ProjectId), "Project ID is required.");
        }

        ValidateText(issues, record.ApprovalTargetKind, nameof(record.ApprovalTargetKind), "APPROVAL_TARGET_KIND_REQUIRED", "Approval target kind is required.");
        ValidateText(issues, record.ApprovalTargetId, nameof(record.ApprovalTargetId), "APPROVAL_TARGET_ID_REQUIRED", "Approval target ID is required.");
        ValidateText(issues, record.ApprovalTargetHash, nameof(record.ApprovalTargetHash), "APPROVAL_TARGET_HASH_REQUIRED", "Approval target hash is required.");
        ValidateText(issues, record.CapabilityCode, nameof(record.CapabilityCode), "CAPABILITY_CODE_REQUIRED", "Capability code is required.");
        ValidateText(issues, record.ApprovalPurpose, nameof(record.ApprovalPurpose), "APPROVAL_PURPOSE_REQUIRED", "Approval purpose is required.");
        ValidateText(issues, record.ApprovedByActorId, nameof(record.ApprovedByActorId), "APPROVED_BY_ACTOR_ID_REQUIRED", "Approved-by actor ID is required.");
        ValidateText(issues, record.CorrelationId, nameof(record.CorrelationId), "CORRELATION_ID_REQUIRED", "Correlation ID is required.");
        ValidateText(issues, record.CausationId, nameof(record.CausationId), "CAUSATION_ID_REQUIRED", "Causation ID is required.");

        if (record.AcceptedAtUtc == default)
        {
            Add(issues, "ACCEPTED_AT_UTC_REQUIRED", nameof(record.AcceptedAtUtc), "Accepted timestamp is required.");
        }

        if (record.ExpiresAtUtc.HasValue && record.ExpiresAtUtc.Value <= record.AcceptedAtUtc)
        {
            Add(issues, "EXPIRES_AT_UTC_INVALID", nameof(record.ExpiresAtUtc), "Expiry timestamp must be after accepted timestamp.");
        }

        ValidateRequiredList(issues, record.EvidenceReferences, nameof(record.EvidenceReferences), "EVIDENCE_REFERENCES_REQUIRED", "At least one evidence reference is required.");
        ValidateRequiredList(issues, record.BoundaryMaxims, nameof(record.BoundaryMaxims), "BOUNDARY_MAXIMS_REQUIRED", "At least one boundary maxim is required.");

        return new AcceptedApprovalValidationResult(issues);
    }

    public static AcceptedApprovalValidationResult ValidateTarget(AcceptedApprovalTarget? target)
    {
        var issues = new List<AcceptedApprovalValidationIssue>();

        if (target is null)
        {
            Add(issues, "ACCEPTED_APPROVAL_TARGET_REQUIRED", "target", "Accepted approval target is required.");
            return new AcceptedApprovalValidationResult(issues);
        }

        ValidateText(issues, target.ApprovalTargetKind, nameof(target.ApprovalTargetKind), "APPROVAL_TARGET_KIND_REQUIRED", "Approval target kind is required.");
        ValidateText(issues, target.ApprovalTargetId, nameof(target.ApprovalTargetId), "APPROVAL_TARGET_ID_REQUIRED", "Approval target ID is required.");
        ValidateText(issues, target.ApprovalTargetHash, nameof(target.ApprovalTargetHash), "APPROVAL_TARGET_HASH_REQUIRED", "Approval target hash is required.");
        if (target.ProjectId == Guid.Empty)
        {
            Add(issues, "PROJECT_ID_REQUIRED", nameof(target.ProjectId), "Project ID is required.");
        }

        ValidateText(issues, target.CapabilityCode, nameof(target.CapabilityCode), "CAPABILITY_CODE_REQUIRED", "Capability code is required.");

        return new AcceptedApprovalValidationResult(issues);
    }

    private static void ValidateText(
        List<AcceptedApprovalValidationIssue> issues,
        string? value,
        string field,
        string code,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(issues, code, field, message);
        }
    }

    private static void ValidateRequiredList(
        List<AcceptedApprovalValidationIssue> issues,
        IReadOnlyList<string>? values,
        string field,
        string code,
        string message)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
        {
            Add(issues, code, field, message);
        }
    }

    private static void Add(List<AcceptedApprovalValidationIssue> issues, string code, string field, string message) =>
        issues.Add(new AcceptedApprovalValidationIssue(code, field, message));
}
