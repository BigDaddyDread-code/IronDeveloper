namespace IronDev.Core.Governance;

public sealed record PolicySatisfactionValidationIssue(string Code, string Field, string Message);

public sealed record PolicySatisfactionValidationResult(IReadOnlyList<PolicySatisfactionValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public static class PolicySatisfactionValidation
{
    public static PolicySatisfactionValidationResult Validate(PolicySatisfactionRecord? record)
    {
        var issues = new List<PolicySatisfactionValidationIssue>();

        if (record is null)
        {
            Add(issues, "POLICY_SATISFACTION_RECORD_REQUIRED", "record", "Policy satisfaction record is required.");
            return new PolicySatisfactionValidationResult(issues);
        }

        if (record.PolicySatisfactionId == Guid.Empty)
        {
            Add(issues, "POLICY_SATISFACTION_ID_REQUIRED", nameof(record.PolicySatisfactionId), "Policy satisfaction ID is required.");
        }

        if (record.ProjectId == Guid.Empty)
        {
            Add(issues, "PROJECT_ID_REQUIRED", nameof(record.ProjectId), "Project ID is required.");
        }

        ValidateText(issues, record.PolicyCode, nameof(record.PolicyCode), "POLICY_CODE_REQUIRED", "Policy code is required.");
        ValidateText(issues, record.PolicyVersion, nameof(record.PolicyVersion), "POLICY_VERSION_REQUIRED", "Policy version is required.");
        ValidateText(issues, record.SubjectKind, nameof(record.SubjectKind), "SUBJECT_KIND_REQUIRED", "Subject kind is required.");
        ValidateText(issues, record.SubjectId, nameof(record.SubjectId), "SUBJECT_ID_REQUIRED", "Subject ID is required.");
        ValidateText(issues, record.SubjectHash, nameof(record.SubjectHash), "SUBJECT_HASH_REQUIRED", "Subject hash is required.");
        ValidateText(issues, record.CapabilityCode, nameof(record.CapabilityCode), "CAPABILITY_CODE_REQUIRED", "Capability code is required.");
        ValidateText(issues, record.ApprovalRequirementHash, nameof(record.ApprovalRequirementHash), "APPROVAL_REQUIREMENT_HASH_REQUIRED", "Approval requirement hash is required.");
        ValidateText(issues, record.CorrelationId, nameof(record.CorrelationId), "CORRELATION_ID_REQUIRED", "Correlation ID is required.");
        ValidateText(issues, record.CausationId, nameof(record.CausationId), "CAUSATION_ID_REQUIRED", "Causation ID is required.");

        if (record.AcceptedApprovalId == Guid.Empty)
        {
            Add(issues, "ACCEPTED_APPROVAL_ID_REQUIRED", nameof(record.AcceptedApprovalId), "Accepted approval ID is required.");
        }

        if (record.ApprovalEvaluatedAtUtc == default)
        {
            Add(issues, "APPROVAL_EVALUATED_AT_UTC_REQUIRED", nameof(record.ApprovalEvaluatedAtUtc), "Approval evaluated timestamp is required.");
        }

        if (record.SatisfiedAtUtc == default)
        {
            Add(issues, "SATISFIED_AT_UTC_REQUIRED", nameof(record.SatisfiedAtUtc), "Satisfied timestamp is required.");
        }

        if (record.ExpiresAtUtc.HasValue && record.ExpiresAtUtc.Value <= record.SatisfiedAtUtc)
        {
            Add(issues, "EXPIRES_AT_UTC_INVALID", nameof(record.ExpiresAtUtc), "Expiry timestamp must be after satisfied timestamp.");
        }

        ValidateRequiredList(issues, record.EvidenceReferences, nameof(record.EvidenceReferences), "EVIDENCE_REFERENCES_REQUIRED", "At least one evidence reference is required.");
        ValidateRequiredList(issues, record.BoundaryMaxims, nameof(record.BoundaryMaxims), "BOUNDARY_MAXIMS_REQUIRED", "At least one boundary maxim is required.");

        return new PolicySatisfactionValidationResult(issues);
    }

    public static PolicySatisfactionValidationResult ValidateSubject(PolicySatisfactionSubject? subject)
    {
        var issues = new List<PolicySatisfactionValidationIssue>();

        if (subject is null)
        {
            Add(issues, "POLICY_SATISFACTION_SUBJECT_REQUIRED", "subject", "Policy satisfaction subject is required.");
            return new PolicySatisfactionValidationResult(issues);
        }

        if (subject.ProjectId == Guid.Empty)
        {
            Add(issues, "PROJECT_ID_REQUIRED", nameof(subject.ProjectId), "Project ID is required.");
        }

        ValidateText(issues, subject.SubjectKind, nameof(subject.SubjectKind), "SUBJECT_KIND_REQUIRED", "Subject kind is required.");
        ValidateText(issues, subject.SubjectId, nameof(subject.SubjectId), "SUBJECT_ID_REQUIRED", "Subject ID is required.");
        ValidateText(issues, subject.SubjectHash, nameof(subject.SubjectHash), "SUBJECT_HASH_REQUIRED", "Subject hash is required.");
        ValidateText(issues, subject.CapabilityCode, nameof(subject.CapabilityCode), "CAPABILITY_CODE_REQUIRED", "Capability code is required.");

        return new PolicySatisfactionValidationResult(issues);
    }

    private static void ValidateText(
        List<PolicySatisfactionValidationIssue> issues,
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
        List<PolicySatisfactionValidationIssue> issues,
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

    private static void Add(List<PolicySatisfactionValidationIssue> issues, string code, string field, string message) =>
        issues.Add(new PolicySatisfactionValidationIssue(code, field, message));
}
