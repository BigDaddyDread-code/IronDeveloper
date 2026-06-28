using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public sealed class ReviewerRoleEvidenceVisibilityService
{
    public ReviewerRoleEvidenceVisibilityDecision Classify(
        GovernanceRoleCatalog catalog,
        RoleVisibilityMatrix matrix,
        ReviewerRoleEvidenceVisibilityRequest request)
    {
        var validation = ReviewerRoleEvidenceVisibilityValidator.ValidateRequest(request);
        if (!validation.IsValid)
        {
            var unsafePayload = validation.Issues.Any(static issue => issue.EndsWith("Unsafe", StringComparison.Ordinal));
            return Decision(
                request,
                unsafePayload ? ReviewerRoleEvidenceVisibilityClassification.Hidden : ReviewerRoleEvidenceVisibilityClassification.Invalid,
                RoleVisibilityLevel.NotVisible,
                validation.Issues);
        }

        if (string.IsNullOrWhiteSpace(request.RoleCatalogEvidenceRef))
        {
            return Decision(
                request,
                ReviewerRoleEvidenceVisibilityClassification.BlockedByMissingCatalogEvidence,
                RoleVisibilityLevel.NotVisible,
                ["Role catalog evidence reference is required."]);
        }

        if (string.IsNullOrWhiteSpace(request.VisibilityMatrixEvidenceRef))
        {
            return Decision(
                request,
                ReviewerRoleEvidenceVisibilityClassification.BlockedByMissingMatrixEvidence,
                RoleVisibilityLevel.NotVisible,
                ["Visibility matrix evidence reference is required."]);
        }

        if (string.IsNullOrWhiteSpace(request.ReviewerEvidenceRef))
        {
            return Decision(
                request,
                ReviewerRoleEvidenceVisibilityClassification.BlockedByMissingReviewerEvidence,
                RoleVisibilityLevel.NotVisible,
                ["Reviewer evidence reference is required."]);
        }

        var catalogValidation = RoleCatalogValidator.ValidateCatalog(catalog);
        if (!catalogValidation.IsValid)
        {
            return Decision(
                request,
                ReviewerRoleEvidenceVisibilityClassification.BlockedByMissingCatalogEvidence,
                RoleVisibilityLevel.NotVisible,
                ["F01 role catalog evidence is invalid."]);
        }

        var matrixValidation = RoleVisibilityMatrixValidator.ValidateMatrix(catalog, matrix);
        if (!matrixValidation.IsValid)
        {
            return Decision(
                request,
                ReviewerRoleEvidenceVisibilityClassification.BlockedByMissingMatrixEvidence,
                RoleVisibilityLevel.NotVisible,
                ["F02 visibility matrix evidence is invalid."]);
        }

        var role = catalog.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RoleId, request.RequestedRoleKey, StringComparison.OrdinalIgnoreCase));
        if (role is null || role.RoleKind != GovernanceRoleKind.Reviewer)
        {
            return Decision(
                request,
                ReviewerRoleEvidenceVisibilityClassification.BlockedByNonReviewerRole,
                RoleVisibilityLevel.NotVisible,
                ["Requested role key is not the F01 reviewer role."]);
        }

        if (request.RequestedMaterialKind == ReviewerRoleEvidenceMaterialKind.Unknown)
        {
            return Decision(
                request,
                ReviewerRoleEvidenceVisibilityClassification.BlockedByUnknownMaterial,
                RoleVisibilityLevel.NotVisible,
                ["Reviewer role evidence material is unknown."]);
        }

        if (request.RequestedIntent == ReviewerRoleEvidenceRequestedIntent.Unknown)
        {
            return Decision(
                request,
                ReviewerRoleEvidenceVisibilityClassification.BlockedByUnknownIntent,
                RoleVisibilityLevel.NotVisible,
                ["Reviewer role evidence intent is unknown."]);
        }

        if (ReviewerRoleEvidenceVisibilityValidator.IsActionIntent(request.RequestedIntent))
        {
            return Decision(
                request,
                ReviewerRoleEvidenceVisibilityClassification.BlockedByActionIntent,
                RoleVisibilityLevel.NotVisible,
                ["Reviewer role evidence visibility cannot be used for action, approval, policy, mutation, workflow, visibility-grant, redaction-bypass, or private-reasoning disclosure intent."]);
        }

        var materialClassification = MaterialBlock(request.RequestedMaterialKind);
        if (materialClassification is not null)
        {
            return Decision(
                request,
                materialClassification.Value,
                RoleVisibilityLevel.NotVisible,
                [$"{request.RequestedMaterialKind} is not visible through reviewer role evidence visibility."]);
        }

        var mappedMatrixMaterial = MatrixMaterialFor(request.RequestedMaterialKind);
        if (mappedMatrixMaterial is null ||
            !MatrixAllowsReviewerEvidence(matrix, request.RequestedRoleKey, request.RequestedSurface, mappedMatrixMaterial.Value))
        {
            return Decision(
                request,
                ReviewerRoleEvidenceVisibilityClassification.Hidden,
                RoleVisibilityLevel.NotVisible,
                ["F02 visibility matrix does not expose this reviewer role evidence surface."]);
        }

        if (request.RequestedMaterialKind == ReviewerRoleEvidenceMaterialKind.RedactedReviewRationaleSummary &&
            string.IsNullOrWhiteSpace(request.OptionalRedactionEvidenceRef))
        {
            return Decision(
                request,
                ReviewerRoleEvidenceVisibilityClassification.BlockedBySensitiveMaterial,
                RoleVisibilityLevel.NotVisible,
                ["Redacted review rationale summary requires redaction evidence."]);
        }

        return request.RequestedMaterialKind switch
        {
            ReviewerRoleEvidenceMaterialKind.ReviewerClaimMetadata =>
                Candidate(request, ReviewerRoleEvidenceVisibilityClassification.MetadataOnlyCandidate, RoleVisibilityLevel.MetadataOnly, "Reviewer claim metadata is metadata-only candidate visibility."),
            ReviewerRoleEvidenceMaterialKind.ReviewerAssignmentClaimSummary =>
                Candidate(request, ReviewerRoleEvidenceVisibilityClassification.MetadataOnlyCandidate, RoleVisibilityLevel.MetadataOnly, "Reviewer assignment claim summary is metadata-only candidate visibility."),
            ReviewerRoleEvidenceMaterialKind.ReviewRequestSummary =>
                Candidate(request, ReviewerRoleEvidenceVisibilityClassification.SummaryCandidate, RoleVisibilityLevel.SummaryOnly, "Review request summary is summary candidate visibility."),
            ReviewerRoleEvidenceMaterialKind.ReviewParticipationSummary =>
                Candidate(request, ReviewerRoleEvidenceVisibilityClassification.SummaryCandidate, RoleVisibilityLevel.SummaryOnly, "Review participation summary is summary candidate visibility."),
            ReviewerRoleEvidenceMaterialKind.ReviewCommentSummary =>
                Candidate(request, ReviewerRoleEvidenceVisibilityClassification.RedactedSummaryCandidate, RoleVisibilityLevel.RedactedDetails, "Review comment summary is redacted summary candidate visibility."),
            ReviewerRoleEvidenceMaterialKind.ReviewOutcomeSummary =>
                Candidate(request, ReviewerRoleEvidenceVisibilityClassification.RedactedSummaryCandidate, RoleVisibilityLevel.RedactedDetails, "Review outcome summary is redacted summary candidate visibility."),
            ReviewerRoleEvidenceMaterialKind.RedactedReviewRationaleSummary =>
                Candidate(request, ReviewerRoleEvidenceVisibilityClassification.RedactedSummaryCandidate, RoleVisibilityLevel.RedactedDetails, "Redacted review rationale summary is redacted summary candidate visibility."),
            _ => Decision(
                request,
                ReviewerRoleEvidenceVisibilityClassification.BlockedByUnknownMaterial,
                RoleVisibilityLevel.NotVisible,
                ["Reviewer role evidence material is unknown."])
        };
    }

    private static ReviewerRoleEvidenceVisibilityDecision Candidate(
        ReviewerRoleEvidenceVisibilityRequest request,
        ReviewerRoleEvidenceVisibilityClassification classification,
        RoleVisibilityLevel level,
        string reason) =>
        Decision(request, classification, level, [reason]);

    private static ReviewerRoleEvidenceVisibilityClassification? MaterialBlock(
        ReviewerRoleEvidenceMaterialKind materialKind) =>
        materialKind switch
        {
            ReviewerRoleEvidenceMaterialKind.RawPayload => ReviewerRoleEvidenceVisibilityClassification.Hidden,
            ReviewerRoleEvidenceMaterialKind.CredentialMaterial => ReviewerRoleEvidenceVisibilityClassification.Hidden,
            ReviewerRoleEvidenceMaterialKind.PrivateReasoning => ReviewerRoleEvidenceVisibilityClassification.Hidden,
            ReviewerRoleEvidenceMaterialKind.AuthorityMarker => ReviewerRoleEvidenceVisibilityClassification.BlockedByAuthorityMarker,
            _ => null
        };

    private static RoleVisibilityMaterialKind? MatrixMaterialFor(
        ReviewerRoleEvidenceMaterialKind materialKind) =>
        materialKind switch
        {
            ReviewerRoleEvidenceMaterialKind.ReviewerClaimMetadata => RoleVisibilityMaterialKind.PullRequestMetadata,
            ReviewerRoleEvidenceMaterialKind.ReviewerAssignmentClaimSummary => RoleVisibilityMaterialKind.PullRequestMetadata,
            ReviewerRoleEvidenceMaterialKind.ReviewRequestSummary => RoleVisibilityMaterialKind.PullRequestMetadata,
            ReviewerRoleEvidenceMaterialKind.ReviewParticipationSummary => RoleVisibilityMaterialKind.PullRequestMetadata,
            ReviewerRoleEvidenceMaterialKind.ReviewCommentSummary => RoleVisibilityMaterialKind.PullRequestDiffSummary,
            ReviewerRoleEvidenceMaterialKind.ReviewOutcomeSummary => RoleVisibilityMaterialKind.ValidationSummary,
            ReviewerRoleEvidenceMaterialKind.RedactedReviewRationaleSummary => RoleVisibilityMaterialKind.PullRequestDiffSummary,
            _ => null
        };

    private static bool MatrixAllowsReviewerEvidence(
        RoleVisibilityMatrix matrix,
        string roleKey,
        RoleVisibilitySurface surface,
        RoleVisibilityMaterialKind materialKind) =>
        matrix.Entries.Any(entry =>
            string.Equals(entry.RoleId, roleKey, StringComparison.OrdinalIgnoreCase) &&
            entry.Surface == surface &&
            entry.MaterialKind == materialKind &&
            entry.VisibilityLevel != RoleVisibilityLevel.NotVisible);

    private static ReviewerRoleEvidenceVisibilityDecision Decision(
        ReviewerRoleEvidenceVisibilityRequest request,
        ReviewerRoleEvidenceVisibilityClassification classification,
        RoleVisibilityLevel effectiveCandidateVisibility,
        IReadOnlyList<string> reasons)
    {
        var evidenceRefs = new[]
        {
            Safe(request.RoleCatalogEvidenceRef),
            Safe(request.VisibilityMatrixEvidenceRef),
            Safe(request.ReviewerEvidenceRef),
            Safe(request.OptionalPolicyEvidenceRef),
            Safe(request.OptionalRedactionEvidenceRef)
        }
        .Where(static item => !string.IsNullOrWhiteSpace(item))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
        .ToArray();

        return new ReviewerRoleEvidenceVisibilityDecision
        {
            Classification = classification,
            EffectiveCandidateVisibility = effectiveCandidateVisibility,
            Reasons = reasons.Select(Safe).ToArray(),
            EvidenceRefs = evidenceRefs,
            GrantsReviewerAuthority = false,
            GrantsRoleAssignmentAuthority = false,
            GrantsVisibilityAuthority = false,
            GrantsAccess = false,
            GrantsApprovalAuthority = false,
            SatisfiesPolicy = false,
            GrantsMutationAuthority = false,
            GrantsWorkflowContinuation = false,
            BypassesRedaction = false,
            DisclosesPrivateReasoning = false,
            RecordFingerprint = Fingerprint(
                request.CorrelationId,
                request.RequestedRoleKey,
                request.RequestedSurface.ToString(),
                request.RequestedMaterialKind.ToString(),
                request.RequestedIntent.ToString(),
                classification.ToString(),
                effectiveCandidateVisibility.ToString())
        };
    }

    private static string Safe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return ReviewerRoleEvidenceVisibilityValidator.ContainsUnsafeEvidenceText(value)
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
