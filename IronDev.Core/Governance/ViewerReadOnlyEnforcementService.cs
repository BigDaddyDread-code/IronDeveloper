using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public sealed class ViewerReadOnlyEnforcementService
{
    public static readonly IReadOnlyList<string> RequiredWarnings =
    [
        "viewer read-only enforcement is restrictive only",
        "viewer read-only enforcement does not grant access",
        "viewer read-only enforcement does not grant permissions",
        "viewer read-only enforcement does not approve work",
        "viewer read-only enforcement does not satisfy policy",
        "viewer read-only enforcement does not authorize execution",
        "viewer read-only enforcement does not authorize mutation",
        "viewer read-only enforcement does not continue workflow",
        "viewer read-only enforcement does not bypass redaction",
        "viewer read-only enforcement does not disclose secrets",
        "viewer read-only enforcement does not disclose private reasoning",
        "read-only evidence requires separate role assignment evidence",
        "read-only evidence requires separate visibility decision evidence",
        "sensitive read-only evidence requires separate policy and redaction enforcement"
    ];

    public static readonly IReadOnlyList<string> RequiredForbiddenAuthorityImplications =
    [
        "viewer role is not identity",
        "viewer role is not authorization",
        "viewer role is not access control",
        "viewer role is not access grant",
        "viewer role is not permission grant",
        "viewer role is not approval",
        "viewer role is not policy satisfaction",
        "viewer role is not validation freshness",
        "viewer role is not source safety",
        "viewer role is not execution authority",
        "viewer role is not mutation authority",
        "viewer role is not merge authority",
        "viewer role is not release authority",
        "viewer role is not deployment authority",
        "viewer role is not workflow continuation authority",
        "viewer role is not redaction bypass",
        "viewer role is not secret disclosure authority",
        "viewer role is not private reasoning disclosure authority"
    ];

    public ViewerReadOnlyEnforcementDecision Evaluate(
        GovernanceRoleCatalog catalog,
        RoleVisibilityMatrix matrix,
        ViewerReadOnlyEnforcementRequest request)
    {
        var requestValidation = ViewerReadOnlyEnforcementValidator.ValidateRequest(request);
        if (!requestValidation.IsValid)
        {
            var unsafePayload = requestValidation.Issues.Any(static issue => issue.EndsWith("Unsafe", StringComparison.Ordinal));
            return Decision(
                request,
                unsafePayload ? ViewerReadOnlyDecisionKind.BlockedByUnsafePayload : ViewerReadOnlyDecisionKind.Invalid,
                unsafePayload ? ViewerReadOnlyBlockKind.UnsafePayload : ViewerReadOnlyBlockKind.InvalidRequest,
                string.Join(",", requestValidation.Issues),
                requiresHumanReview: true);
        }

        var catalogValidation = RoleCatalogValidator.ValidateCatalog(catalog);
        if (!catalogValidation.IsValid)
        {
            return Decision(
                request,
                ViewerReadOnlyDecisionKind.BlockedByInvalidRoleCatalog,
                ViewerReadOnlyBlockKind.InvalidCatalog,
                "F01 role catalog is invalid.",
                requiresHumanReview: true);
        }

        var matrixValidation = RoleVisibilityMatrixValidator.ValidateMatrix(catalog, matrix);
        if (!matrixValidation.IsValid)
        {
            return Decision(
                request,
                ViewerReadOnlyDecisionKind.BlockedByInvalidVisibilityMatrix,
                ViewerReadOnlyBlockKind.InvalidMatrix,
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
                ViewerReadOnlyDecisionKind.BlockedByRoleVisibilityMismatch,
                ViewerReadOnlyBlockKind.RoleVisibilityMismatch,
                "Catalog or matrix reference does not match supplied evidence.",
                requiresHumanReview: true);
        }

        var role = catalog.Entries.FirstOrDefault(roleEntry =>
            string.Equals(roleEntry.RoleId, request.RoleId, StringComparison.OrdinalIgnoreCase));

        if (role is null)
        {
            return Decision(
                request,
                ViewerReadOnlyDecisionKind.BlockedByUnknownRole,
                ViewerReadOnlyBlockKind.UnknownRole,
                "Role id is not present in the F01 role catalog.",
                requiresHumanReview: true);
        }

        if (role.RoleKind != request.RoleKind || role.ScopeKind != request.RoleScopeKind)
        {
            return Decision(
                request,
                ViewerReadOnlyDecisionKind.BlockedByRoleVisibilityMismatch,
                ViewerReadOnlyBlockKind.RoleVisibilityMismatch,
                "Role kind or scope does not match the F01 role catalog.",
                requiresHumanReview: true);
        }

        var entry = matrix.Entries.FirstOrDefault(matrixEntry =>
            string.Equals(matrixEntry.RoleId, request.RoleId, StringComparison.OrdinalIgnoreCase) &&
            matrixEntry.Surface == request.VisibilitySurface &&
            matrixEntry.MaterialKind == request.VisibilityMaterialKind);

        if (entry is null)
        {
            return Decision(
                request,
                ViewerReadOnlyDecisionKind.BlockedByRoleVisibilityMismatch,
                ViewerReadOnlyBlockKind.RoleVisibilityMismatch,
                "Role visibility matrix entry is missing.",
                requiresHumanReview: true);
        }

        if (entry.RoleKind != request.RoleKind ||
            entry.RoleScopeKind != request.RoleScopeKind ||
            entry.VisibilityLevel != request.VisibilityLevel ||
            entry.SensitivityKind != request.SensitivityKind)
        {
            return Decision(
                request,
                ViewerReadOnlyDecisionKind.BlockedByRoleVisibilityMismatch,
                ViewerReadOnlyBlockKind.RoleVisibilityMismatch,
                "Role visibility matrix entry does not match request evidence.",
                requiresHumanReview: true);
        }

        if (request.IntentKind == ViewerReadOnlyIntentKind.Unknown)
        {
            return Decision(
                request,
                ViewerReadOnlyDecisionKind.BlockedByUnknownIntent,
                ViewerReadOnlyBlockKind.UnknownIntent,
                "Viewer read-only intent is unknown.",
                requiresHumanReview: true);
        }

        if (ViewerReadOnlyEnforcementValidator.IsActionIntent(request.IntentKind))
        {
            var (decision, blockKind) = ActionBlock(request.IntentKind);
            return Decision(
                request,
                decision,
                blockKind,
                "Viewer read-only evidence cannot be used for action intent.",
                requiresHumanReview: true);
        }

        if (!ViewerReadOnlyEnforcementValidator.IsReadIntent(request.IntentKind))
        {
            return Decision(
                request,
                ViewerReadOnlyDecisionKind.BlockedByUnknownIntent,
                ViewerReadOnlyBlockKind.UnknownIntent,
                "Viewer read-only intent is not recognized as read-only.",
                requiresHumanReview: true);
        }

        if (entry.VisibilityLevel == RoleVisibilityLevel.NotVisible)
        {
            return Decision(
                request,
                ViewerReadOnlyDecisionKind.BlockedByRoleVisibilityMismatch,
                ViewerReadOnlyBlockKind.RoleVisibilityMismatch,
                "Visibility matrix entry is not visible for read intent.",
                requiresHumanReview: true);
        }

        if (string.IsNullOrWhiteSpace(request.RoleAssignmentEvidenceRef) ||
            string.IsNullOrWhiteSpace(request.VisibilityDecisionEvidenceRef))
        {
            return Decision(
                request,
                ViewerReadOnlyDecisionKind.BlockedByMissingEvidence,
                ViewerReadOnlyBlockKind.MissingEvidence,
                "Read-only path requires separate role assignment and visibility decision evidence.",
                requiresHumanReview: true);
        }

        var requiresPolicyAndRedaction =
            request.IntentKind == ViewerReadOnlyIntentKind.ReadRedactedDetails ||
            ViewerReadOnlyEnforcementValidator.RequiresPolicyAndRedaction(
                request.VisibilityMaterialKind,
                request.SensitivityKind,
                request.VisibilityLevel);

        if (requiresPolicyAndRedaction &&
            (string.IsNullOrWhiteSpace(request.PolicyDecisionEvidenceRef) ||
             string.IsNullOrWhiteSpace(request.RedactionEvidenceRef)))
        {
            return Decision(
                request,
                ViewerReadOnlyDecisionKind.BlockedByMissingEvidence,
                ViewerReadOnlyBlockKind.MissingEvidence,
                "Sensitive read-only path requires separate policy and redaction evidence.",
                requiresHumanReview: true);
        }

        return Decision(
            request,
            ViewerReadOnlyDecisionKind.MayProceedToSeparateVisibilityDecision,
            ViewerReadOnlyBlockKind.None,
            "Viewer read-only enforcement did not block this read-only intent.",
            requiresHumanReview: false);
    }

    private static (ViewerReadOnlyDecisionKind Decision, ViewerReadOnlyBlockKind BlockKind) ActionBlock(
        ViewerReadOnlyIntentKind intent) =>
        intent switch
        {
            ViewerReadOnlyIntentKind.ActionSourceApply or
            ViewerReadOnlyIntentKind.ActionCommit or
            ViewerReadOnlyIntentKind.ActionPush or
            ViewerReadOnlyIntentKind.ActionPullRequest or
            ViewerReadOnlyIntentKind.ActionReadyForReview or
            ViewerReadOnlyIntentKind.ActionReviewRequest or
            ViewerReadOnlyIntentKind.ActionMerge or
            ViewerReadOnlyIntentKind.ActionRelease or
            ViewerReadOnlyIntentKind.ActionDeploy or
            ViewerReadOnlyIntentKind.ActionRollback or
            ViewerReadOnlyIntentKind.ActionRetry or
            ViewerReadOnlyIntentKind.ActionRecover => (ViewerReadOnlyDecisionKind.BlockedByMutationIntent, ViewerReadOnlyBlockKind.MutationIntent),
            ViewerReadOnlyIntentKind.ActionApprove => (ViewerReadOnlyDecisionKind.BlockedByApprovalIntent, ViewerReadOnlyBlockKind.ApprovalIntent),
            ViewerReadOnlyIntentKind.ActionSatisfyPolicy => (ViewerReadOnlyDecisionKind.BlockedByPolicyIntent, ViewerReadOnlyBlockKind.PolicyIntent),
            ViewerReadOnlyIntentKind.ActionContinueWorkflow => (ViewerReadOnlyDecisionKind.BlockedByWorkflowContinuationIntent, ViewerReadOnlyBlockKind.WorkflowContinuationIntent),
            ViewerReadOnlyIntentKind.ActionPromoteMemory => (ViewerReadOnlyDecisionKind.BlockedByMemoryPromotionIntent, ViewerReadOnlyBlockKind.MemoryPromotionIntent),
            ViewerReadOnlyIntentKind.ActionBypassRedaction => (ViewerReadOnlyDecisionKind.BlockedByRedactionBypassIntent, ViewerReadOnlyBlockKind.RedactionBypassIntent),
            ViewerReadOnlyIntentKind.ActionDiscloseSecret or
            ViewerReadOnlyIntentKind.ActionDiscloseCredential or
            ViewerReadOnlyIntentKind.ActionDisclosePrivateReasoning or
            ViewerReadOnlyIntentKind.ActionDiscloseRawPayload => (ViewerReadOnlyDecisionKind.BlockedBySensitiveDisclosureIntent, ViewerReadOnlyBlockKind.SensitiveDisclosureIntent),
            _ => (ViewerReadOnlyDecisionKind.BlockedByActionIntent, ViewerReadOnlyBlockKind.ActionIntent)
        };

    private static ViewerReadOnlyEnforcementDecision Decision(
        ViewerReadOnlyEnforcementRequest request,
        ViewerReadOnlyDecisionKind decision,
        ViewerReadOnlyBlockKind blockKind,
        string reason,
        bool requiresHumanReview)
    {
        var requiresPolicyAndRedaction = ViewerReadOnlyEnforcementValidator.RequiresPolicyAndRedaction(
            request.VisibilityMaterialKind,
            request.SensitivityKind,
            request.VisibilityLevel);

        var fingerprint = Fingerprint(
            request.TenantId,
            request.ProjectId,
            request.OperationId,
            request.CorrelationId,
            request.RoleId,
            request.IntentKind.ToString(),
            request.RequestedEvidenceRef,
            decision.ToString(),
            blockKind.ToString());

        return new ViewerReadOnlyEnforcementDecision
        {
            Decision = decision,
            BlockKind = blockKind,
            Reason = Safe(reason),
            TenantId = Safe(request.TenantId),
            ProjectId = Safe(request.ProjectId),
            OperationId = Safe(request.OperationId),
            CorrelationId = Safe(request.CorrelationId),
            RoleId = Safe(request.RoleId),
            RoleKind = request.RoleKind,
            RoleScopeKind = request.RoleScopeKind,
            ViewerRoleKind = request.ViewerRoleKind,
            VisibilitySurface = request.VisibilitySurface,
            VisibilityMaterialKind = request.VisibilityMaterialKind,
            VisibilityLevel = request.VisibilityLevel,
            SensitivityKind = request.SensitivityKind,
            IntentKind = request.IntentKind,
            MatchedRoleCatalogId = Safe(request.RoleCatalogId),
            MatchedRoleCatalogVersion = Safe(request.RoleCatalogVersion),
            MatchedRoleCatalogEntryRef = Safe(request.RoleCatalogEntryRef),
            MatchedVisibilityMatrixId = Safe(request.VisibilityMatrixId),
            MatchedVisibilityMatrixVersion = Safe(request.VisibilityMatrixVersion),
            MatchedVisibilityMatrixEntryRef = Safe(request.VisibilityMatrixEntryRef),
            MatchedRoleAssignmentEvidenceRef = Safe(request.RoleAssignmentEvidenceRef),
            MatchedVisibilityDecisionEvidenceRef = Safe(request.VisibilityDecisionEvidenceRef),
            MatchedPolicyDecisionEvidenceRef = Safe(request.PolicyDecisionEvidenceRef),
            MatchedRedactionEvidenceRef = Safe(request.RedactionEvidenceRef),
            RequiresSeparateRoleAssignment = true,
            RequiresSeparateVisibilityDecision = true,
            RequiresSeparatePolicyDecision = requiresPolicyAndRedaction,
            RequiresSeparateRedactionEnforcement = requiresPolicyAndRedaction,
            RequiresSeparateActionAuthority = true,
            RequiresSeparateApproval = true,
            RequiresSeparatePolicySatisfaction = true,
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

        return ViewerReadOnlyEnforcementValidator.ContainsUnsafeViewerText(value)
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
