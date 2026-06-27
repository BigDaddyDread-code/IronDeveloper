using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public sealed class ApproverRoleRequestDecisionVisibilityService
{
    public ApproverRoleRequestDecisionVisibilityDecision Classify(
        GovernanceRoleCatalog catalog,
        RoleVisibilityMatrix matrix,
        ApproverRoleRequestDecisionVisibilityRequest request)
    {
        var validation = ApproverRoleRequestDecisionVisibilityValidator.ValidateRequest(request);
        if (!validation.IsValid)
        {
            var unsafePayload = validation.Issues.Any(static issue => issue.EndsWith("Unsafe", StringComparison.Ordinal));
            return Decision(
                request,
                unsafePayload ? ApproverRoleRequestDecisionVisibilityClassification.Hidden : ApproverRoleRequestDecisionVisibilityClassification.Invalid,
                RoleVisibilityLevel.NotVisible,
                validation.Issues);
        }

        if (string.IsNullOrWhiteSpace(request.RoleCatalogEvidenceRef))
        {
            return Decision(
                request,
                ApproverRoleRequestDecisionVisibilityClassification.BlockedByMissingCatalogEvidence,
                RoleVisibilityLevel.NotVisible,
                ["Role catalog evidence reference is required."]);
        }

        if (string.IsNullOrWhiteSpace(request.VisibilityMatrixEvidenceRef))
        {
            return Decision(
                request,
                ApproverRoleRequestDecisionVisibilityClassification.BlockedByMissingMatrixEvidence,
                RoleVisibilityLevel.NotVisible,
                ["Visibility matrix evidence reference is required."]);
        }

        if (string.IsNullOrWhiteSpace(request.ApproverRequestDecisionEvidenceRef))
        {
            return Decision(
                request,
                ApproverRoleRequestDecisionVisibilityClassification.BlockedByMissingRequestDecisionEvidence,
                RoleVisibilityLevel.NotVisible,
                ["Approver request or decision evidence reference is required."]);
        }

        var catalogValidation = RoleCatalogValidator.ValidateCatalog(catalog);
        if (!catalogValidation.IsValid)
        {
            return Decision(
                request,
                ApproverRoleRequestDecisionVisibilityClassification.BlockedByMissingCatalogEvidence,
                RoleVisibilityLevel.NotVisible,
                ["F01 role catalog evidence is invalid."]);
        }

        var matrixValidation = RoleVisibilityMatrixValidator.ValidateMatrix(catalog, matrix);
        if (!matrixValidation.IsValid)
        {
            return Decision(
                request,
                ApproverRoleRequestDecisionVisibilityClassification.BlockedByMissingMatrixEvidence,
                RoleVisibilityLevel.NotVisible,
                ["F02 visibility matrix evidence is invalid."]);
        }

        var role = catalog.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RoleId, request.RequestedRoleKey, StringComparison.OrdinalIgnoreCase));
        if (role is null || role.RoleKind != GovernanceRoleKind.ApproverCandidate)
        {
            return Decision(
                request,
                ApproverRoleRequestDecisionVisibilityClassification.BlockedByNonApproverRole,
                RoleVisibilityLevel.NotVisible,
                ["Requested role key is not the F01 approver candidate role."]);
        }

        if (request.RequestedMaterialKind == ApproverRoleRequestDecisionMaterialKind.Unknown)
        {
            return Decision(
                request,
                ApproverRoleRequestDecisionVisibilityClassification.BlockedByUnknownMaterial,
                RoleVisibilityLevel.NotVisible,
                ["Approver request or decision material is unknown."]);
        }

        if (request.RequestedIntent == ApproverRoleRequestDecisionRequestedIntent.Unknown)
        {
            return Decision(
                request,
                ApproverRoleRequestDecisionVisibilityClassification.BlockedByUnknownIntent,
                RoleVisibilityLevel.NotVisible,
                ["Approver request or decision intent is unknown."]);
        }

        if (ApproverRoleRequestDecisionVisibilityValidator.IsActionIntent(request.RequestedIntent))
        {
            return Decision(
                request,
                ApproverRoleRequestDecisionVisibilityValidator.IsApprovalIntent(request.RequestedIntent)
                    ? ApproverRoleRequestDecisionVisibilityClassification.BlockedByApprovalIntent
                    : ApproverRoleRequestDecisionVisibilityClassification.BlockedByActionIntent,
                RoleVisibilityLevel.NotVisible,
                ["Approver request or decision visibility cannot be used for approval, policy, mutation, workflow, merge, release, deployment, visibility-grant, redaction-bypass, or private-reasoning disclosure intent."]);
        }

        var materialClassification = MaterialBlock(request.RequestedMaterialKind);
        if (materialClassification is not null)
        {
            return Decision(
                request,
                materialClassification.Value,
                RoleVisibilityLevel.NotVisible,
                [$"{request.RequestedMaterialKind} is not visible through approver request or decision visibility."]);
        }

        var mappedMatrixMaterial = MatrixMaterialFor(request.RequestedMaterialKind);
        if (mappedMatrixMaterial is null ||
            !MatrixAllowsApproverEvidence(matrix, request.RequestedRoleKey, request.RequestedSurface, mappedMatrixMaterial.Value))
        {
            return Decision(
                request,
                ApproverRoleRequestDecisionVisibilityClassification.Hidden,
                RoleVisibilityLevel.NotVisible,
                ["F02 visibility matrix does not expose this approver request or decision surface."]);
        }

        if (RequiresRedactionEvidence(request.RequestedMaterialKind) &&
            string.IsNullOrWhiteSpace(request.OptionalRedactionEvidenceRef))
        {
            return Decision(
                request,
                ApproverRoleRequestDecisionVisibilityClassification.BlockedByMissingRedactionEvidence,
                RoleVisibilityLevel.NotVisible,
                ["Redacted approver request or decision rationale summary requires redaction evidence."]);
        }

        return request.RequestedMaterialKind switch
        {
            ApproverRoleRequestDecisionMaterialKind.ApproverRoleRequestMetadata =>
                Candidate(request, ApproverRoleRequestDecisionVisibilityClassification.MetadataOnlyCandidate, RoleVisibilityLevel.MetadataOnly, "Approver role request metadata is metadata-only candidate visibility."),
            ApproverRoleRequestDecisionMaterialKind.ApproverRoleRequestSummary =>
                Candidate(request, ApproverRoleRequestDecisionVisibilityClassification.SummaryCandidate, RoleVisibilityLevel.SummaryOnly, "Approver role request summary is summary candidate visibility."),
            ApproverRoleRequestDecisionMaterialKind.RedactedApproverRoleRequestRationaleSummary =>
                Candidate(request, ApproverRoleRequestDecisionVisibilityClassification.RedactedSummaryCandidate, RoleVisibilityLevel.RedactedDetails, "Redacted approver role request rationale summary is redacted summary candidate visibility."),
            ApproverRoleRequestDecisionMaterialKind.ApproverRoleDecisionMetadata =>
                Candidate(request, ApproverRoleRequestDecisionVisibilityClassification.MetadataOnlyCandidate, RoleVisibilityLevel.MetadataOnly, "Approver role decision metadata is metadata-only candidate visibility."),
            ApproverRoleRequestDecisionMaterialKind.ApproverRoleDecisionSummary =>
                Candidate(request, ApproverRoleRequestDecisionVisibilityClassification.RedactedSummaryCandidate, RoleVisibilityLevel.RedactedDetails, "Approver role decision summary is redacted summary candidate visibility."),
            ApproverRoleRequestDecisionMaterialKind.ApproverRoleDecisionOutcomeSummary =>
                Candidate(request, ApproverRoleRequestDecisionVisibilityClassification.RedactedSummaryCandidate, RoleVisibilityLevel.RedactedDetails, "Approver role decision outcome summary is redacted summary candidate visibility."),
            ApproverRoleRequestDecisionMaterialKind.RedactedApproverRoleDecisionRationaleSummary =>
                Candidate(request, ApproverRoleRequestDecisionVisibilityClassification.RedactedSummaryCandidate, RoleVisibilityLevel.RedactedDetails, "Redacted approver role decision rationale summary is redacted summary candidate visibility."),
            ApproverRoleRequestDecisionMaterialKind.ApprovalPackageReferenceSummary =>
                Candidate(request, ApproverRoleRequestDecisionVisibilityClassification.MetadataOnlyCandidate, RoleVisibilityLevel.MetadataOnly, "Approval package reference summary is metadata-only candidate visibility."),
            _ => Decision(
                request,
                ApproverRoleRequestDecisionVisibilityClassification.BlockedByUnknownMaterial,
                RoleVisibilityLevel.NotVisible,
                ["Approver request or decision material is unknown."])
        };
    }

    private static ApproverRoleRequestDecisionVisibilityDecision Candidate(
        ApproverRoleRequestDecisionVisibilityRequest request,
        ApproverRoleRequestDecisionVisibilityClassification classification,
        RoleVisibilityLevel level,
        string reason) =>
        Decision(request, classification, level, [reason]);

    private static ApproverRoleRequestDecisionVisibilityClassification? MaterialBlock(
        ApproverRoleRequestDecisionMaterialKind materialKind) =>
        materialKind switch
        {
            ApproverRoleRequestDecisionMaterialKind.RawPayload => ApproverRoleRequestDecisionVisibilityClassification.Hidden,
            ApproverRoleRequestDecisionMaterialKind.CredentialMaterial => ApproverRoleRequestDecisionVisibilityClassification.Hidden,
            ApproverRoleRequestDecisionMaterialKind.PrivateReasoning => ApproverRoleRequestDecisionVisibilityClassification.Hidden,
            ApproverRoleRequestDecisionMaterialKind.AuthorityMarker => ApproverRoleRequestDecisionVisibilityClassification.BlockedByAuthorityMarker,
            _ => null
        };

    private static bool RequiresRedactionEvidence(
        ApproverRoleRequestDecisionMaterialKind materialKind) =>
        materialKind is ApproverRoleRequestDecisionMaterialKind.RedactedApproverRoleRequestRationaleSummary or
            ApproverRoleRequestDecisionMaterialKind.RedactedApproverRoleDecisionRationaleSummary;

    private static RoleVisibilityMaterialKind? MatrixMaterialFor(
        ApproverRoleRequestDecisionMaterialKind materialKind) =>
        materialKind switch
        {
            ApproverRoleRequestDecisionMaterialKind.ApproverRoleRequestMetadata => RoleVisibilityMaterialKind.ApprovalPackageSummary,
            ApproverRoleRequestDecisionMaterialKind.ApproverRoleRequestSummary => RoleVisibilityMaterialKind.ApprovalPackageSummary,
            ApproverRoleRequestDecisionMaterialKind.RedactedApproverRoleRequestRationaleSummary => RoleVisibilityMaterialKind.ApprovalPackageSummary,
            ApproverRoleRequestDecisionMaterialKind.ApproverRoleDecisionMetadata => RoleVisibilityMaterialKind.ApprovalPackageSummary,
            ApproverRoleRequestDecisionMaterialKind.ApproverRoleDecisionSummary => RoleVisibilityMaterialKind.ApprovalPackageSummary,
            ApproverRoleRequestDecisionMaterialKind.ApproverRoleDecisionOutcomeSummary => RoleVisibilityMaterialKind.ValidationSummary,
            ApproverRoleRequestDecisionMaterialKind.RedactedApproverRoleDecisionRationaleSummary => RoleVisibilityMaterialKind.ApprovalPackageSummary,
            ApproverRoleRequestDecisionMaterialKind.ApprovalPackageReferenceSummary => RoleVisibilityMaterialKind.ApprovalPackageSummary,
            _ => null
        };

    private static bool MatrixAllowsApproverEvidence(
        RoleVisibilityMatrix matrix,
        string roleKey,
        RoleVisibilitySurface surface,
        RoleVisibilityMaterialKind materialKind) =>
        matrix.Entries.Any(entry =>
            string.Equals(entry.RoleId, roleKey, StringComparison.OrdinalIgnoreCase) &&
            entry.Surface == surface &&
            entry.MaterialKind == materialKind &&
            entry.VisibilityLevel != RoleVisibilityLevel.NotVisible);

    private static ApproverRoleRequestDecisionVisibilityDecision Decision(
        ApproverRoleRequestDecisionVisibilityRequest request,
        ApproverRoleRequestDecisionVisibilityClassification classification,
        RoleVisibilityLevel effectiveCandidateVisibility,
        IReadOnlyList<string> reasons)
    {
        var evidenceRefs = new[]
        {
            Safe(request.RoleCatalogEvidenceRef),
            Safe(request.VisibilityMatrixEvidenceRef),
            Safe(request.ApproverRequestDecisionEvidenceRef),
            Safe(request.OptionalPolicyEvidenceRef),
            Safe(request.OptionalRedactionEvidenceRef)
        }
        .Where(static item => !string.IsNullOrWhiteSpace(item))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
        .ToArray();

        return new ApproverRoleRequestDecisionVisibilityDecision
        {
            Classification = classification,
            EffectiveCandidateVisibility = effectiveCandidateVisibility,
            Reasons = reasons.Select(Safe).ToArray(),
            EvidenceRefs = evidenceRefs,
            GrantsApproverAuthority = false,
            GrantsRoleAssignmentAuthority = false,
            CreatesApproverRequest = false,
            AcceptsApproverRequest = false,
            GrantsVisibilityAuthority = false,
            GrantsAccess = false,
            GrantsApprovalAuthority = false,
            AcceptsApproval = false,
            SatisfiesPolicy = false,
            GrantsMutationAuthority = false,
            GrantsWorkflowContinuation = false,
            GrantsMergeAuthority = false,
            GrantsReleaseAuthority = false,
            GrantsDeploymentAuthority = false,
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

        return ApproverRoleRequestDecisionVisibilityValidator.ContainsUnsafeEvidenceText(value)
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
