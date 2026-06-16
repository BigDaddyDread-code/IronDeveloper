namespace IronDev.Core.Governance;

public sealed class PolicyRequirementSatisfactionEvaluator : IPolicyRequirementSatisfactionEvaluator
{
    public PolicyRequirementSatisfactionEvaluation Evaluate(
        PolicyRequirement? requirement,
        ApprovalSatisfactionEvaluation? approvalEvaluation)
    {
        var issues = new List<PolicyRequirementSatisfactionIssue>();

        if (requirement is null)
        {
            Add(issues, "POLICY_REQUIREMENT_REQUIRED", "requirement", "Policy requirement is required.");
            return Evaluation(false, null, null, null, issues);
        }

        ValidateRequirement(requirement, issues);
        var policyRequirementHash = PolicyRequirementHash.Compute(requirement);

        if (approvalEvaluation is null)
        {
            Add(issues, "APPROVAL_SATISFACTION_EVALUATION_REQUIRED", "approvalEvaluation", "Approval satisfaction evaluation is required.");
            return Evaluation(false, null, requirement.ApprovalRequirementHash, policyRequirementHash, issues);
        }

        if (!approvalEvaluation.IsSatisfied)
        {
            Add(issues, "APPROVAL_REQUIREMENT_NOT_SATISFIED", nameof(approvalEvaluation.IsSatisfied), "Approval requirement is not satisfied.");
        }

        if (approvalEvaluation.Issues.Count > 0)
        {
            Add(issues, "APPROVAL_EVALUATION_HAS_ISSUES", nameof(approvalEvaluation.Issues), "Approval satisfaction evaluation has issues.");
        }

        if (approvalEvaluation.AcceptedApprovalId is null || approvalEvaluation.AcceptedApprovalId.Value == Guid.Empty)
        {
            Add(issues, "ACCEPTED_APPROVAL_ID_REQUIRED", nameof(approvalEvaluation.AcceptedApprovalId), "Accepted approval ID is required.");
        }

        ValidateText(issues, approvalEvaluation.Boundary, nameof(approvalEvaluation.Boundary), "APPROVAL_EVALUATION_BOUNDARY_REQUIRED", "Approval satisfaction boundary is required.");

        AddMissingRequiredValues(
            issues,
            requirement.RequiredEvidenceReferences,
            approvalEvaluation.EvidenceReferences,
            nameof(requirement.RequiredEvidenceReferences),
            "REQUIRED_EVIDENCE_REFERENCE_MISSING",
            "Required evidence reference is missing from approval satisfaction evaluation.");

        AddMissingRequiredValues(
            issues,
            requirement.RequiredBoundaryMaxims,
            approvalEvaluation.BoundaryMaxims,
            nameof(requirement.RequiredBoundaryMaxims),
            "REQUIRED_BOUNDARY_MAXIM_MISSING",
            "Required boundary maxim is missing from approval satisfaction evaluation.");

        return Evaluation(
            issues.Count == 0,
            approvalEvaluation.AcceptedApprovalId,
            requirement.ApprovalRequirementHash,
            policyRequirementHash,
            issues);
    }

    private static void ValidateRequirement(
        PolicyRequirement requirement,
        List<PolicyRequirementSatisfactionIssue> issues)
    {
        if (requirement.ProjectId == Guid.Empty)
        {
            Add(issues, "PROJECT_ID_REQUIRED", nameof(requirement.ProjectId), "Project ID is required.");
        }

        ValidateText(issues, requirement.PolicyCode, nameof(requirement.PolicyCode), "POLICY_CODE_REQUIRED", "Policy code is required.");
        ValidateText(issues, requirement.PolicyVersion, nameof(requirement.PolicyVersion), "POLICY_VERSION_REQUIRED", "Policy version is required.");
        ValidateText(issues, requirement.SubjectKind, nameof(requirement.SubjectKind), "SUBJECT_KIND_REQUIRED", "Subject kind is required.");
        ValidateText(issues, requirement.SubjectId, nameof(requirement.SubjectId), "SUBJECT_ID_REQUIRED", "Subject ID is required.");
        ValidateText(issues, requirement.SubjectHash, nameof(requirement.SubjectHash), "SUBJECT_HASH_REQUIRED", "Subject hash is required.");
        ValidateText(issues, requirement.CapabilityCode, nameof(requirement.CapabilityCode), "CAPABILITY_CODE_REQUIRED", "Capability code is required.");
        ValidateText(issues, requirement.ApprovalTargetKind, nameof(requirement.ApprovalTargetKind), "APPROVAL_TARGET_KIND_REQUIRED", "Approval target kind is required.");
        ValidateText(issues, requirement.ApprovalTargetId, nameof(requirement.ApprovalTargetId), "APPROVAL_TARGET_ID_REQUIRED", "Approval target ID is required.");
        ValidateText(issues, requirement.ApprovalTargetHash, nameof(requirement.ApprovalTargetHash), "APPROVAL_TARGET_HASH_REQUIRED", "Approval target hash is required.");
        ValidateText(issues, requirement.ApprovalPurpose, nameof(requirement.ApprovalPurpose), "APPROVAL_PURPOSE_REQUIRED", "Approval purpose is required.");
        ValidateText(issues, requirement.ApprovalRequirementHash, nameof(requirement.ApprovalRequirementHash), "APPROVAL_REQUIREMENT_HASH_REQUIRED", "Approval requirement hash is required.");

        if (requirement.EvaluatedAtUtc == default)
        {
            Add(issues, "EVALUATED_AT_UTC_REQUIRED", nameof(requirement.EvaluatedAtUtc), "Evaluation timestamp is required.");
        }

        if (requirement.ExpiresAtUtc.HasValue && requirement.ExpiresAtUtc.Value <= requirement.EvaluatedAtUtc)
        {
            Add(issues, "POLICY_REQUIREMENT_EXPIRED", nameof(requirement.ExpiresAtUtc), "Policy requirement is expired at the evaluation time.");
        }
    }

    private static void ValidateText(
        List<PolicyRequirementSatisfactionIssue> issues,
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

    private static void AddMissingRequiredValues(
        List<PolicyRequirementSatisfactionIssue> issues,
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
            if (string.IsNullOrWhiteSpace(requiredValue) || !actual.Contains(requiredValue))
            {
                Add(issues, code, field, message);
            }
        }
    }

    private static PolicyRequirementSatisfactionEvaluation Evaluation(
        bool isSatisfied,
        Guid? acceptedApprovalId,
        string? approvalRequirementHash,
        string? policyRequirementHash,
        IReadOnlyList<PolicyRequirementSatisfactionIssue> issues) =>
        new()
        {
            IsSatisfied = isSatisfied,
            AcceptedApprovalId = acceptedApprovalId,
            ApprovalRequirementHash = approvalRequirementHash,
            PolicyRequirementHash = policyRequirementHash,
            Issues = issues
        };

    private static void Add(List<PolicyRequirementSatisfactionIssue> issues, string code, string field, string message) =>
        issues.Add(new PolicyRequirementSatisfactionIssue(code, field, message));
}
