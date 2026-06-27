using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public sealed class ReviewerEvidenceVisibilityService
{
    public static readonly IReadOnlyList<string> RequiredWarnings =
    [
        "reviewer evidence visibility is descriptive only",
        "reviewer evidence visibility does not grant access",
        "reviewer evidence visibility does not grant permissions",
        "reviewer evidence visibility does not approve work",
        "reviewer evidence visibility does not satisfy policy",
        "reviewer evidence visibility does not refresh validation",
        "reviewer evidence visibility does not prove source safety",
        "reviewer evidence visibility does not authorize execution",
        "reviewer evidence visibility does not authorize mutation",
        "reviewer evidence visibility does not continue workflow",
        "reviewer evidence visibility does not bypass redaction",
        "reviewer evidence visibility does not disclose secrets",
        "reviewer evidence visibility does not disclose private reasoning",
        "reviewer evidence visibility requires separate reviewer assignment evidence",
        "reviewer evidence visibility requires separate visibility decision evidence",
        "sensitive reviewer evidence requires separate policy and redaction enforcement"
    ];

    public static readonly IReadOnlyList<string> RequiredForbiddenAuthorityImplications =
    [
        "reviewer visibility is not identity",
        "reviewer visibility is not authorization",
        "reviewer visibility is not access control",
        "reviewer visibility is not access grant",
        "reviewer visibility is not permission grant",
        "reviewer visibility is not approval",
        "reviewer visibility is not policy satisfaction",
        "reviewer visibility is not validation freshness",
        "reviewer visibility is not source safety",
        "reviewer visibility is not execution authority",
        "reviewer visibility is not mutation authority",
        "reviewer visibility is not pull request authority",
        "reviewer visibility is not ready-for-review authority",
        "reviewer visibility is not reviewer-request authority",
        "reviewer visibility is not merge authority",
        "reviewer visibility is not release authority",
        "reviewer visibility is not deployment authority",
        "reviewer visibility is not rollback authority",
        "reviewer visibility is not retry authority",
        "reviewer visibility is not recovery authority",
        "reviewer visibility is not workflow continuation authority",
        "reviewer visibility is not redaction bypass",
        "reviewer visibility is not secret disclosure authority",
        "reviewer visibility is not private reasoning disclosure authority"
    ];

    public ReviewerEvidenceVisibilityDecision Evaluate(
        GovernanceRoleCatalog catalog,
        RoleVisibilityMatrix matrix,
        ReviewerEvidenceVisibilityRequest request)
    {
        var requestValidation = ReviewerEvidenceVisibilityValidator.ValidateRequest(request);
        if (!requestValidation.IsValid)
        {
            var unsafePayload = requestValidation.Issues.Any(static issue => issue.EndsWith("Unsafe", StringComparison.Ordinal));
            return Decision(
                request,
                unsafePayload ? ReviewerEvidenceVisibilityDecisionKind.BlockedByUnsafePayload : ReviewerEvidenceVisibilityDecisionKind.Invalid,
                unsafePayload ? ReviewerEvidenceVisibilityBlockKind.UnsafePayload : ReviewerEvidenceVisibilityBlockKind.InvalidRequest,
                string.Join(",", requestValidation.Issues),
                requiresHumanReview: true);
        }

        var catalogValidation = RoleCatalogValidator.ValidateCatalog(catalog);
        if (!catalogValidation.IsValid)
        {
            return Decision(
                request,
                ReviewerEvidenceVisibilityDecisionKind.BlockedByInvalidRoleCatalog,
                ReviewerEvidenceVisibilityBlockKind.InvalidCatalog,
                "F01 role catalog is invalid.",
                requiresHumanReview: true);
        }

        var matrixValidation = RoleVisibilityMatrixValidator.ValidateMatrix(catalog, matrix);
        if (!matrixValidation.IsValid)
        {
            return Decision(
                request,
                ReviewerEvidenceVisibilityDecisionKind.BlockedByInvalidVisibilityMatrix,
                ReviewerEvidenceVisibilityBlockKind.InvalidMatrix,
                "F02 role visibility matrix is invalid.",
                requiresHumanReview: true);
        }

        if (!string.Equals(request.RoleCatalogId, catalog.CatalogId, StringComparison.Ordinal) ||
            !string.Equals(request.RoleCatalogVersion, catalog.CatalogVersion, StringComparison.Ordinal) ||
            !string.Equals(request.VisibilityMatrixId, matrix.MatrixId, StringComparison.Ordinal) ||
            !string.Equals(request.VisibilityMatrixVersion, matrix.CatalogVersion, StringComparison.Ordinal))
        {
            return Decision(
                request,
                ReviewerEvidenceVisibilityDecisionKind.BlockedByRoleVisibilityMismatch,
                ReviewerEvidenceVisibilityBlockKind.RoleVisibilityMismatch,
                "Catalog or matrix reference does not match supplied evidence.",
                requiresHumanReview: true);
        }

        var role = catalog.Entries.FirstOrDefault(roleEntry =>
            string.Equals(roleEntry.RoleId, request.ReviewerRoleId, StringComparison.OrdinalIgnoreCase));

        if (role is null)
        {
            return Decision(
                request,
                ReviewerEvidenceVisibilityDecisionKind.BlockedByUnknownReviewerRole,
                ReviewerEvidenceVisibilityBlockKind.UnknownReviewerRole,
                "Reviewer role id is not present in the F01 role catalog.",
                requiresHumanReview: true);
        }

        if (role.RoleKind != request.ReviewerRoleKind || role.ScopeKind != request.ReviewerRoleScopeKind)
        {
            return Decision(
                request,
                ReviewerEvidenceVisibilityDecisionKind.BlockedByRoleVisibilityMismatch,
                ReviewerEvidenceVisibilityBlockKind.RoleVisibilityMismatch,
                "Reviewer role kind or scope does not match the F01 role catalog.",
                requiresHumanReview: true);
        }

        if (!ReviewerEvidenceVisibilityValidator.IsReviewerRole(request.ReviewerRoleKind))
        {
            return Decision(
                request,
                ReviewerEvidenceVisibilityDecisionKind.BlockedByNonReviewerRole,
                ReviewerEvidenceVisibilityBlockKind.NonReviewerRole,
                "Role is not a reviewer evidence visibility role.",
                requiresHumanReview: true);
        }

        if (request.IntentKind == ReviewerEvidenceVisibilityIntentKind.Unknown)
        {
            return Decision(
                request,
                ReviewerEvidenceVisibilityDecisionKind.Invalid,
                ReviewerEvidenceVisibilityBlockKind.InvalidRequest,
                "Reviewer evidence visibility intent is unknown.",
                requiresHumanReview: true);
        }

        if (ReviewerEvidenceVisibilityValidator.IsActionIntent(request.IntentKind))
        {
            var (decision, blockKind) = ActionBlock(request.IntentKind);
            return Decision(
                request,
                decision,
                blockKind,
                "Reviewer evidence visibility cannot be used for action intent.",
                requiresHumanReview: true);
        }

        if (!ReviewerEvidenceVisibilityValidator.IsReadIntent(request.IntentKind))
        {
            return Decision(
                request,
                ReviewerEvidenceVisibilityDecisionKind.Invalid,
                ReviewerEvidenceVisibilityBlockKind.InvalidRequest,
                "Reviewer evidence visibility intent is not recognized as read-only.",
                requiresHumanReview: true);
        }

        var entry = matrix.Entries.FirstOrDefault(matrixEntry =>
            string.Equals(matrixEntry.RoleId, request.ReviewerRoleId, StringComparison.OrdinalIgnoreCase) &&
            matrixEntry.Surface == request.EvidenceSurface &&
            matrixEntry.MaterialKind == request.EvidenceMaterialKind);

        if (entry is null)
        {
            return Decision(
                request,
                ReviewerEvidenceVisibilityDecisionKind.BlockedByReviewerEvidenceNotAllowed,
                ReviewerEvidenceVisibilityBlockKind.EvidenceNotAllowed,
                "F02 matrix does not allow this reviewer role to see this evidence kind.",
                requiresHumanReview: true);
        }

        if (entry.RoleKind != request.ReviewerRoleKind ||
            entry.RoleScopeKind != request.ReviewerRoleScopeKind ||
            entry.VisibilityLevel != request.EvidenceVisibilityLevel ||
            entry.SensitivityKind != request.EvidenceSensitivityKind)
        {
            return Decision(
                request,
                ReviewerEvidenceVisibilityDecisionKind.BlockedByRoleVisibilityMismatch,
                ReviewerEvidenceVisibilityBlockKind.RoleVisibilityMismatch,
                "F02 matrix entry does not match request evidence.",
                requiresHumanReview: true);
        }

        if (RoleVisibilityMatrixValidator.IsSecretOrRawMaterial(
                request.EvidenceMaterialKind,
                request.EvidenceSensitivityKind))
        {
            return Decision(
                request,
                ReviewerEvidenceVisibilityDecisionKind.BlockedByRawOrSecretEvidence,
                ReviewerEvidenceVisibilityBlockKind.RawOrSecretEvidence,
                "Reviewer evidence visibility cannot expose raw, credential, secret, or private reasoning material.",
                requiresHumanReview: true);
        }

        if (entry.VisibilityLevel == RoleVisibilityLevel.NotVisible)
        {
            return Decision(
                request,
                ReviewerEvidenceVisibilityDecisionKind.BlockedByReviewerEvidenceNotAllowed,
                ReviewerEvidenceVisibilityBlockKind.EvidenceNotAllowed,
                "F02 matrix marks this evidence kind as not visible.",
                requiresHumanReview: true);
        }

        if (string.IsNullOrWhiteSpace(request.ReviewerAssignmentEvidenceRef) ||
            string.IsNullOrWhiteSpace(request.ReviewerEvidenceRequestRef) ||
            string.IsNullOrWhiteSpace(request.VisibilityDecisionEvidenceRef))
        {
            return Decision(
                request,
                ReviewerEvidenceVisibilityDecisionKind.BlockedByMissingEvidence,
                ReviewerEvidenceVisibilityBlockKind.MissingEvidence,
                "Reviewer evidence visibility requires reviewer assignment, evidence request, and visibility decision evidence.",
                requiresHumanReview: true);
        }

        var requiresPolicyAndRedaction =
            request.IntentKind == ReviewerEvidenceVisibilityIntentKind.ReadRedactedEvidence ||
            ReviewerEvidenceVisibilityValidator.RequiresPolicyAndRedaction(
                request.EvidenceMaterialKind,
                request.EvidenceSensitivityKind,
                request.EvidenceVisibilityLevel);

        if (requiresPolicyAndRedaction &&
            (string.IsNullOrWhiteSpace(request.PolicyDecisionEvidenceRef) ||
             string.IsNullOrWhiteSpace(request.RedactionEvidenceRef)))
        {
            return Decision(
                request,
                ReviewerEvidenceVisibilityDecisionKind.BlockedBySensitiveEvidencePolicyMissing,
                ReviewerEvidenceVisibilityBlockKind.SensitiveEvidencePolicyMissing,
                "Sensitive reviewer evidence requires separate policy and redaction evidence.",
                requiresHumanReview: true);
        }

        return Decision(
            request,
            ReviewerEvidenceVisibilityDecisionKind.MayProceedToSeparateEvidenceVisibilityDecision,
            ReviewerEvidenceVisibilityBlockKind.None,
            "Reviewer evidence visibility did not block this read-only evidence intent.",
            requiresHumanReview: false);
    }

    private static (ReviewerEvidenceVisibilityDecisionKind Decision, ReviewerEvidenceVisibilityBlockKind BlockKind) ActionBlock(
        ReviewerEvidenceVisibilityIntentKind intent) =>
        intent switch
        {
            ReviewerEvidenceVisibilityIntentKind.ActionApprove => (ReviewerEvidenceVisibilityDecisionKind.BlockedByApprovalIntent, ReviewerEvidenceVisibilityBlockKind.ApprovalIntent),
            ReviewerEvidenceVisibilityIntentKind.ActionSatisfyPolicy => (ReviewerEvidenceVisibilityDecisionKind.BlockedByPolicyIntent, ReviewerEvidenceVisibilityBlockKind.PolicyIntent),
            ReviewerEvidenceVisibilityIntentKind.ActionContinueWorkflow => (ReviewerEvidenceVisibilityDecisionKind.BlockedByWorkflowContinuationIntent, ReviewerEvidenceVisibilityBlockKind.WorkflowContinuationIntent),
            ReviewerEvidenceVisibilityIntentKind.ActionBypassRedaction => (ReviewerEvidenceVisibilityDecisionKind.BlockedByRedactionBypassIntent, ReviewerEvidenceVisibilityBlockKind.RedactionBypassIntent),
            ReviewerEvidenceVisibilityIntentKind.ActionDiscloseRawPayload or
            ReviewerEvidenceVisibilityIntentKind.ActionDiscloseCredential or
            ReviewerEvidenceVisibilityIntentKind.ActionDisclosePrivateReasoning => (ReviewerEvidenceVisibilityDecisionKind.BlockedBySensitiveDisclosureIntent, ReviewerEvidenceVisibilityBlockKind.SensitiveDisclosureIntent),
            _ => (ReviewerEvidenceVisibilityDecisionKind.BlockedByActionAuthorityAttempt, ReviewerEvidenceVisibilityBlockKind.ActionAuthorityAttempt)
        };

    private static ReviewerEvidenceVisibilityDecision Decision(
        ReviewerEvidenceVisibilityRequest request,
        ReviewerEvidenceVisibilityDecisionKind decision,
        ReviewerEvidenceVisibilityBlockKind blockKind,
        string reason,
        bool requiresHumanReview)
    {
        var requiresPolicyAndRedaction = ReviewerEvidenceVisibilityValidator.RequiresPolicyAndRedaction(
            request.EvidenceMaterialKind,
            request.EvidenceSensitivityKind,
            request.EvidenceVisibilityLevel);

        var fingerprint = Fingerprint(
            request.TenantId,
            request.ProjectId,
            request.OperationId,
            request.CorrelationId,
            request.ReviewerRoleId,
            request.IntentKind.ToString(),
            request.EvidenceRef,
            decision.ToString(),
            blockKind.ToString());

        return new ReviewerEvidenceVisibilityDecision
        {
            Decision = decision,
            BlockKind = blockKind,
            Reason = Safe(reason),
            TenantId = Safe(request.TenantId),
            ProjectId = Safe(request.ProjectId),
            OperationId = Safe(request.OperationId),
            CorrelationId = Safe(request.CorrelationId),
            ReviewerRoleId = Safe(request.ReviewerRoleId),
            ReviewerRoleKind = request.ReviewerRoleKind,
            ReviewerRoleScopeKind = request.ReviewerRoleScopeKind,
            EvidenceSurface = request.EvidenceSurface,
            EvidenceMaterialKind = request.EvidenceMaterialKind,
            EvidenceSensitivityKind = request.EvidenceSensitivityKind,
            EvidenceVisibilityLevel = request.EvidenceVisibilityLevel,
            IntentKind = request.IntentKind,
            MatchedEvidenceRef = Safe(request.EvidenceRef),
            MatchedEvidenceSubjectRef = Safe(request.EvidenceSubjectRef),
            MatchedRoleCatalogId = Safe(request.RoleCatalogId),
            MatchedRoleCatalogVersion = Safe(request.RoleCatalogVersion),
            MatchedRoleCatalogEntryRef = Safe(request.RoleCatalogEntryRef),
            MatchedVisibilityMatrixId = Safe(request.VisibilityMatrixId),
            MatchedVisibilityMatrixVersion = Safe(request.VisibilityMatrixVersion),
            MatchedVisibilityMatrixEntryRef = Safe(request.VisibilityMatrixEntryRef),
            MatchedReviewerAssignmentEvidenceRef = Safe(request.ReviewerAssignmentEvidenceRef),
            MatchedReviewerEvidenceRequestRef = Safe(request.ReviewerEvidenceRequestRef),
            MatchedVisibilityDecisionEvidenceRef = Safe(request.VisibilityDecisionEvidenceRef),
            MatchedPolicyDecisionEvidenceRef = Safe(request.PolicyDecisionEvidenceRef),
            MatchedRedactionEvidenceRef = Safe(request.RedactionEvidenceRef),
            RequiresSeparateReviewerAssignment = true,
            RequiresSeparateReviewerEvidenceRequest = true,
            RequiresSeparateVisibilityDecision = true,
            RequiresSeparatePolicyDecision = requiresPolicyAndRedaction,
            RequiresSeparateRedactionEnforcement = requiresPolicyAndRedaction,
            RequiresSeparateApproval = true,
            RequiresSeparatePolicySatisfaction = true,
            RequiresSeparateActionAuthority = true,
            RequiresSeparateMutationAuthority = true,
            RequiresSeparateWorkflowAuthority = true,
            RequiresHumanReview = requiresHumanReview,
            Warnings = RequiredWarnings,
            ForbiddenAuthorityImplications = RequiredForbiddenAuthorityImplications,
            RecordFingerprint = fingerprint
        };
    }

    private static string Safe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return ReviewerEvidenceVisibilityValidator.ContainsUnsafeEvidenceVisibilityText(value)
            ? "[unsafe-rejected]"
            : value;
    }

    private static string Fingerprint(params string?[] parts)
    {
        var safeParts = parts.Select(Safe);
        var canonical = string.Join("|", safeParts);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
