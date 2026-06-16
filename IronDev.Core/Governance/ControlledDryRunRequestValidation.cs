namespace IronDev.Core.Governance;

public sealed record ControlledDryRunRequestValidationIssue(string Code, string Field, string Message);

public sealed record ControlledDryRunRequestValidationResult(IReadOnlyList<ControlledDryRunRequestValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public static class ControlledDryRunRequestValidation
{
    private static readonly string[] ExecutionAuthorityMarkers =
    [
        "dry-run executed",
        "runs dry-run",
        "patch artifact created",
        "creates patch artifact",
        "source applied",
        "applies source",
        "rollback executed",
        "continues workflow",
        "workflow continued",
        "approves release",
        "release ready",
        "release approved"
    ];

    private static readonly string[] PrivateMaterialMarkers =
    [
        "raw prompt",
        "raw completion",
        "raw tool output",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "scratchpad",
        "hidden reasoning",
        "private reasoning",
        "system prompt",
        "developer prompt",
        "password",
        "api_key",
        "secret",
        "private key",
        "bearer"
    ];

    public static ControlledDryRunRequestValidationResult Validate(ControlledDryRunRequest? request)
    {
        var issues = new List<ControlledDryRunRequestValidationIssue>();

        if (request is null)
        {
            Add(issues, "CONTROLLED_DRY_RUN_REQUEST_REQUIRED", "request", "Controlled dry-run request is required.");
            return new ControlledDryRunRequestValidationResult(issues);
        }

        if (request.ControlledDryRunRequestId == Guid.Empty)
        {
            Add(issues, "CONTROLLED_DRY_RUN_REQUEST_ID_REQUIRED", nameof(request.ControlledDryRunRequestId), "Controlled dry-run request ID is required.");
        }

        if (request.ProjectId == Guid.Empty)
        {
            Add(issues, "PROJECT_ID_REQUIRED", nameof(request.ProjectId), "Project ID is required.");
        }

        if (request.PolicySatisfactionId == Guid.Empty)
        {
            Add(issues, "POLICY_SATISFACTION_ID_REQUIRED", nameof(request.PolicySatisfactionId), "Policy satisfaction ID is required.");
        }

        ValidateText(issues, request.PolicySatisfactionHash, nameof(request.PolicySatisfactionHash), "POLICY_SATISFACTION_HASH_REQUIRED", "Policy satisfaction hash is required.");
        ValidateText(issues, request.SubjectKind, nameof(request.SubjectKind), "SUBJECT_KIND_REQUIRED", "Subject kind is required.");
        ValidateText(issues, request.SubjectId, nameof(request.SubjectId), "SUBJECT_ID_REQUIRED", "Subject ID is required.");
        ValidateText(issues, request.SubjectHash, nameof(request.SubjectHash), "SUBJECT_HASH_REQUIRED", "Subject hash is required.");
        ValidateText(issues, request.CapabilityCode, nameof(request.CapabilityCode), "CAPABILITY_CODE_REQUIRED", "Capability code is required.");
        ValidateText(issues, request.WorkspaceKind, nameof(request.WorkspaceKind), "WORKSPACE_KIND_REQUIRED", "Workspace kind is required.");
        ValidateText(issues, request.WorkspaceId, nameof(request.WorkspaceId), "WORKSPACE_ID_REQUIRED", "Workspace ID is required.");
        ValidateText(issues, request.WorkspaceBoundaryHash, nameof(request.WorkspaceBoundaryHash), "WORKSPACE_BOUNDARY_HASH_REQUIRED", "Workspace boundary hash is required.");
        ValidateText(issues, request.RequestedOperation, nameof(request.RequestedOperation), "REQUESTED_OPERATION_REQUIRED", "Requested operation is required.");
        ValidateText(issues, request.RequestedOperationHash, nameof(request.RequestedOperationHash), "REQUESTED_OPERATION_HASH_REQUIRED", "Requested operation hash is required.");
        ValidateText(issues, request.ValidationPlanKind, nameof(request.ValidationPlanKind), "VALIDATION_PLAN_KIND_REQUIRED", "Validation plan kind is required.");
        ValidateText(issues, request.ValidationPlanId, nameof(request.ValidationPlanId), "VALIDATION_PLAN_ID_REQUIRED", "Validation plan ID is required.");
        ValidateText(issues, request.ValidationPlanHash, nameof(request.ValidationPlanHash), "VALIDATION_PLAN_HASH_REQUIRED", "Validation plan hash is required.");
        ValidateText(issues, request.CorrelationId, nameof(request.CorrelationId), "CORRELATION_ID_REQUIRED", "Correlation ID is required.");
        ValidateText(issues, request.CausationId, nameof(request.CausationId), "CAUSATION_ID_REQUIRED", "Causation ID is required.");

        if (request.RequestedAtUtc == default)
        {
            Add(issues, "REQUESTED_AT_UTC_REQUIRED", nameof(request.RequestedAtUtc), "Requested timestamp is required.");
        }

        if (request.ExpiresAtUtc.HasValue && request.ExpiresAtUtc.Value <= request.RequestedAtUtc)
        {
            Add(issues, "EXPIRES_AT_UTC_INVALID", nameof(request.ExpiresAtUtc), "Expiry timestamp must be after requested timestamp.");
        }

        ValidateRequiredList(issues, request.EvidenceReferences, nameof(request.EvidenceReferences), "EVIDENCE_REFERENCES_REQUIRED", "At least one evidence reference is required.");
        ValidateRequiredList(issues, request.BoundaryMaxims, nameof(request.BoundaryMaxims), "BOUNDARY_MAXIMS_REQUIRED", "At least one boundary maxim is required.");

        ValidateSafeText(issues, request.PolicySatisfactionHash, nameof(request.PolicySatisfactionHash));
        ValidateSafeText(issues, request.SubjectKind, nameof(request.SubjectKind));
        ValidateSafeText(issues, request.SubjectId, nameof(request.SubjectId));
        ValidateSafeText(issues, request.SubjectHash, nameof(request.SubjectHash));
        ValidateSafeText(issues, request.CapabilityCode, nameof(request.CapabilityCode));
        ValidateSafeText(issues, request.WorkspaceKind, nameof(request.WorkspaceKind));
        ValidateSafeText(issues, request.WorkspaceId, nameof(request.WorkspaceId));
        ValidateSafeText(issues, request.WorkspaceBoundaryHash, nameof(request.WorkspaceBoundaryHash));
        ValidateSafeText(issues, request.RequestedOperation, nameof(request.RequestedOperation));
        ValidateSafeText(issues, request.RequestedOperationHash, nameof(request.RequestedOperationHash));
        ValidateSafeText(issues, request.ValidationPlanKind, nameof(request.ValidationPlanKind));
        ValidateSafeText(issues, request.ValidationPlanId, nameof(request.ValidationPlanId));
        ValidateSafeText(issues, request.ValidationPlanHash, nameof(request.ValidationPlanHash));
        ValidateSafeText(issues, request.CorrelationId, nameof(request.CorrelationId));
        ValidateSafeText(issues, request.CausationId, nameof(request.CausationId));
        ValidateSafeText(issues, request.Boundary, nameof(request.Boundary));
        ValidateSafeList(issues, request.EvidenceReferences, nameof(request.EvidenceReferences));
        ValidateSafeList(issues, request.BoundaryMaxims, nameof(request.BoundaryMaxims));

        return new ControlledDryRunRequestValidationResult(issues);
    }

    private static void ValidateText(
        List<ControlledDryRunRequestValidationIssue> issues,
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
        List<ControlledDryRunRequestValidationIssue> issues,
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

    private static void ValidateSafeList(
        List<ControlledDryRunRequestValidationIssue> issues,
        IReadOnlyList<string>? values,
        string field)
    {
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            ValidateSafeText(issues, value, field);
        }
    }

    private static void ValidateSafeText(
        List<ControlledDryRunRequestValidationIssue> issues,
        string? value,
        string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var marker in ExecutionAuthorityMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, "EXECUTION_AUTHORITY_CLAIM_REJECTED", field, $"Controlled dry-run request text must not claim execution authority: {marker}.");
            }
        }

        foreach (var marker in PrivateMaterialMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, "PRIVATE_OR_RAW_MATERIAL_REJECTED", field, $"Controlled dry-run request text must not contain private or raw material: {marker}.");
            }
        }
    }

    private static void Add(List<ControlledDryRunRequestValidationIssue> issues, string code, string field, string message) =>
        issues.Add(new ControlledDryRunRequestValidationIssue(code, field, message));
}
