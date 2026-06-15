namespace IronDev.Core.Governance;

public sealed class ApprovalSatisfactionEvaluator : IApprovalSatisfactionEvaluator
{
    public ApprovalSatisfactionEvaluation Evaluate(
        ApprovalRequirement requirement,
        AcceptedApprovalRecord? acceptedApproval)
    {
        var issues = new List<ApprovalSatisfactionIssue>();

        ValidateRequirement(requirement, issues);

        if (acceptedApproval is null)
        {
            Add(issues, "ACCEPTED_APPROVAL_REQUIRED", "acceptedApproval", "Accepted approval record is required.");
            return Evaluation(false, null, issues);
        }

        var approvalValidation = AcceptedApprovalValidation.Validate(acceptedApproval);
        foreach (var issue in approvalValidation.Issues)
        {
            Add(issues, issue.Code, issue.Field, issue.Message);
        }

        if (!approvalValidation.IsValid)
        {
            return Evaluation(false, acceptedApproval.AcceptedApprovalId, issues);
        }

        AddIfMismatch(issues, acceptedApproval.ProjectId, requirement.ProjectId, nameof(requirement.ProjectId), "PROJECT_ID_MISMATCH", "Project ID must match exactly.");
        AddIfMismatch(issues, acceptedApproval.ApprovalTargetKind, requirement.ApprovalTargetKind, nameof(requirement.ApprovalTargetKind), "APPROVAL_TARGET_KIND_MISMATCH", "Approval target kind must match exactly.");
        AddIfMismatch(issues, acceptedApproval.ApprovalTargetId, requirement.ApprovalTargetId, nameof(requirement.ApprovalTargetId), "APPROVAL_TARGET_ID_MISMATCH", "Approval target ID must match exactly.");
        AddIfMismatch(issues, acceptedApproval.ApprovalTargetHash, requirement.ApprovalTargetHash, nameof(requirement.ApprovalTargetHash), "APPROVAL_TARGET_HASH_MISMATCH", "Approval target hash must match exactly.");
        AddIfMismatch(issues, acceptedApproval.CapabilityCode, requirement.CapabilityCode, nameof(requirement.CapabilityCode), "CAPABILITY_CODE_MISMATCH", "Capability code must match exactly.");
        AddIfMismatch(issues, acceptedApproval.ApprovalPurpose, requirement.ApprovalPurpose, nameof(requirement.ApprovalPurpose), "APPROVAL_PURPOSE_MISMATCH", "Approval purpose must match exactly.");

        if (acceptedApproval.ExpiresAtUtc.HasValue && acceptedApproval.ExpiresAtUtc.Value <= requirement.EvaluatedAtUtc)
        {
            Add(issues, "ACCEPTED_APPROVAL_EXPIRED", nameof(acceptedApproval.ExpiresAtUtc), "Accepted approval record is expired at the evaluation time.");
        }

        AddMissingRequiredValues(
            issues,
            requirement.RequiredEvidenceReferences,
            acceptedApproval.EvidenceReferences,
            nameof(requirement.RequiredEvidenceReferences),
            "REQUIRED_EVIDENCE_REFERENCE_MISSING",
            "Required evidence reference is missing from accepted approval record.");

        AddMissingRequiredValues(
            issues,
            requirement.RequiredBoundaryMaxims,
            acceptedApproval.BoundaryMaxims,
            nameof(requirement.RequiredBoundaryMaxims),
            "REQUIRED_BOUNDARY_MAXIM_MISSING",
            "Required boundary maxim is missing from accepted approval record.");

        return Evaluation(issues.Count == 0, acceptedApproval.AcceptedApprovalId, issues);
    }

    private static void ValidateRequirement(ApprovalRequirement requirement, List<ApprovalSatisfactionIssue> issues)
    {
        if (requirement.ProjectId == Guid.Empty)
        {
            Add(issues, "PROJECT_ID_REQUIRED", nameof(requirement.ProjectId), "Project ID is required.");
        }

        ValidateText(issues, requirement.ApprovalTargetKind, nameof(requirement.ApprovalTargetKind), "APPROVAL_TARGET_KIND_REQUIRED", "Approval target kind is required.");
        ValidateText(issues, requirement.ApprovalTargetId, nameof(requirement.ApprovalTargetId), "APPROVAL_TARGET_ID_REQUIRED", "Approval target ID is required.");
        ValidateText(issues, requirement.ApprovalTargetHash, nameof(requirement.ApprovalTargetHash), "APPROVAL_TARGET_HASH_REQUIRED", "Approval target hash is required.");
        ValidateText(issues, requirement.CapabilityCode, nameof(requirement.CapabilityCode), "CAPABILITY_CODE_REQUIRED", "Capability code is required.");
        ValidateText(issues, requirement.ApprovalPurpose, nameof(requirement.ApprovalPurpose), "APPROVAL_PURPOSE_REQUIRED", "Approval purpose is required.");

        if (requirement.EvaluatedAtUtc == default)
        {
            Add(issues, "EVALUATED_AT_UTC_REQUIRED", nameof(requirement.EvaluatedAtUtc), "Evaluation timestamp is required.");
        }
    }

    private static void ValidateText(
        List<ApprovalSatisfactionIssue> issues,
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

    private static void AddIfMismatch(
        List<ApprovalSatisfactionIssue> issues,
        Guid actual,
        Guid expected,
        string field,
        string code,
        string message)
    {
        if (actual != expected)
        {
            Add(issues, code, field, message);
        }
    }

    private static void AddIfMismatch(
        List<ApprovalSatisfactionIssue> issues,
        string actual,
        string expected,
        string field,
        string code,
        string message)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            Add(issues, code, field, message);
        }
    }

    private static void AddMissingRequiredValues(
        List<ApprovalSatisfactionIssue> issues,
        IReadOnlyList<string>? requiredValues,
        IReadOnlyList<string> actualValues,
        string field,
        string code,
        string message)
    {
        if (requiredValues is null || requiredValues.Count == 0)
        {
            return;
        }

        var actual = new HashSet<string>(actualValues, StringComparer.Ordinal);
        foreach (var requiredValue in requiredValues)
        {
            if (!actual.Contains(requiredValue))
            {
                Add(issues, code, field, message);
            }
        }
    }

    private static ApprovalSatisfactionEvaluation Evaluation(bool isSatisfied, Guid? acceptedApprovalId, IReadOnlyList<ApprovalSatisfactionIssue> issues) =>
        new()
        {
            IsSatisfied = isSatisfied,
            AcceptedApprovalId = acceptedApprovalId,
            Issues = issues
        };

    private static void Add(List<ApprovalSatisfactionIssue> issues, string code, string field, string message) =>
        issues.Add(new ApprovalSatisfactionIssue(code, field, message));
}
