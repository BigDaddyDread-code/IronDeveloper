using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public sealed class ExternalViewerRedactionService
{
    public ExternalViewerRedactionDecision Classify(
        GovernanceRoleCatalog catalog,
        RoleVisibilityMatrix matrix,
        ExternalViewerRedactionRequest request)
    {
        var validation = ExternalViewerRedactionValidator.ValidateRequest(request);
        if (!validation.IsValid)
        {
            var unsafePayload = validation.Issues.Any(static issue => issue.EndsWith("Unsafe", StringComparison.Ordinal));
            return Decision(
                request,
                unsafePayload ? ExternalViewerRedactionClassification.Hidden : ExternalViewerRedactionClassification.Invalid,
                RoleVisibilityLevel.NotVisible,
                validation.Issues);
        }

        if (string.IsNullOrWhiteSpace(request.RoleCatalogEvidenceRef))
        {
            return Decision(
                request,
                ExternalViewerRedactionClassification.BlockedByMissingCatalogEvidence,
                RoleVisibilityLevel.NotVisible,
                ["Role catalog evidence reference is required."]);
        }

        if (string.IsNullOrWhiteSpace(request.VisibilityMatrixEvidenceRef))
        {
            return Decision(
                request,
                ExternalViewerRedactionClassification.BlockedByMissingMatrixEvidence,
                RoleVisibilityLevel.NotVisible,
                ["Visibility matrix evidence reference is required."]);
        }

        if (string.IsNullOrWhiteSpace(request.SourceEvidenceRef))
        {
            return Decision(
                request,
                ExternalViewerRedactionClassification.BlockedByMissingSourceEvidence,
                RoleVisibilityLevel.NotVisible,
                ["Source evidence reference is required."]);
        }

        var catalogValidation = RoleCatalogValidator.ValidateCatalog(catalog);
        if (!catalogValidation.IsValid)
        {
            return Decision(
                request,
                ExternalViewerRedactionClassification.BlockedByMissingCatalogEvidence,
                RoleVisibilityLevel.NotVisible,
                ["F01 role catalog evidence is invalid."]);
        }

        var matrixValidation = RoleVisibilityMatrixValidator.ValidateMatrix(catalog, matrix);
        if (!matrixValidation.IsValid)
        {
            return Decision(
                request,
                ExternalViewerRedactionClassification.BlockedByMissingMatrixEvidence,
                RoleVisibilityLevel.NotVisible,
                ["F02 visibility matrix evidence is invalid."]);
        }

        var role = catalog.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RoleId, request.RequestedRoleKey, StringComparison.OrdinalIgnoreCase));
        if (role is null || role.RoleKind != GovernanceRoleKind.ExternalViewer)
        {
            return Decision(
                request,
                ExternalViewerRedactionClassification.BlockedByNonExternalViewerRole,
                RoleVisibilityLevel.NotVisible,
                ["Requested role key is not the F01 external viewer role."]);
        }

        if (request.RequestedMaterialKind == ExternalViewerRedactionMaterialKind.Unknown)
        {
            return Decision(
                request,
                ExternalViewerRedactionClassification.BlockedByUnknownMaterial,
                RoleVisibilityLevel.NotVisible,
                ["External viewer material is unknown."]);
        }

        if (request.RequestedIntent == ExternalViewerRedactionRequestedIntent.Unknown)
        {
            return Decision(
                request,
                ExternalViewerRedactionClassification.BlockedByUnknownIntent,
                RoleVisibilityLevel.NotVisible,
                ["External viewer intent is unknown."]);
        }

        if (!ExternalViewerRedactionValidator.IsReadOnlyIntent(request.RequestedIntent))
        {
            return Decision(
                request,
                ExternalViewerRedactionValidator.ClassifyIntentBlock(request.RequestedIntent),
                RoleVisibilityLevel.NotVisible,
                ["External viewer redaction cannot be used for access, sharing, raw export, disclosure, approval, policy, diagnostic, retry, rollback, recovery, mutation, workflow, merge, release, or deployment intent."]);
        }

        var materialClassification = MaterialBlock(request.RequestedMaterialKind);
        if (materialClassification is not null)
        {
            return Decision(
                request,
                materialClassification.Value,
                RoleVisibilityLevel.NotVisible,
                [$"{request.RequestedMaterialKind} is not visible through external viewer redaction."]);
        }

        if (!MatrixAllowsCandidate(matrix, request))
        {
            return Decision(
                request,
                ExternalViewerRedactionClassification.Hidden,
                RoleVisibilityLevel.NotVisible,
                ["F02 visibility matrix does not expose this external viewer surface."]);
        }

        if (ExternalViewerRedactionValidator.RequiresTenantBoundaryEvidence(request.RequestedMaterialKind) &&
            string.IsNullOrWhiteSpace(request.OptionalTenantBoundaryEvidenceRef))
        {
            return Decision(
                request,
                ExternalViewerRedactionClassification.BlockedByMissingTenantBoundaryEvidence,
                RoleVisibilityLevel.NotVisible,
                ["Tenant-scoped or project-scoped external viewer material requires tenant-boundary evidence."]);
        }

        if (ExternalViewerRedactionValidator.RequiresRedactionEvidence(request.RequestedMaterialKind) &&
            string.IsNullOrWhiteSpace(request.OptionalRedactionEvidenceRef))
        {
            return Decision(
                request,
                ExternalViewerRedactionClassification.BlockedByMissingRedactionEvidence,
                RoleVisibilityLevel.NotVisible,
                ["Redacted external viewer summary requires redaction evidence."]);
        }

        return request.RequestedMaterialKind switch
        {
            ExternalViewerRedactionMaterialKind.PublicMetadata or
            ExternalViewerRedactionMaterialKind.TenantScopedMetadata or
            ExternalViewerRedactionMaterialKind.ProjectScopedMetadata or
            ExternalViewerRedactionMaterialKind.OperationStatusMetadata =>
                Candidate(request, ExternalViewerRedactionClassification.MetadataOnlyCandidate, RoleVisibilityLevel.MetadataOnly, "External viewer material is metadata-only candidate visibility."),
            ExternalViewerRedactionMaterialKind.RedactedOperationStatusSummary or
            ExternalViewerRedactionMaterialKind.RedactedValidationSummary or
            ExternalViewerRedactionMaterialKind.RedactedReviewSummary or
            ExternalViewerRedactionMaterialKind.RedactedApprovalSummary or
            ExternalViewerRedactionMaterialKind.RedactedDiagnosticSummary or
            ExternalViewerRedactionMaterialKind.RedactedAuditSummary or
            ExternalViewerRedactionMaterialKind.RedactedReleaseReadinessSummary or
            ExternalViewerRedactionMaterialKind.RedactedPolicySummary or
            ExternalViewerRedactionMaterialKind.RedactedErrorSummary or
            ExternalViewerRedactionMaterialKind.RedactedLogSummary or
            ExternalViewerRedactionMaterialKind.RedactedReceiptSummary =>
                Candidate(request, ExternalViewerRedactionClassification.RedactedSummaryCandidate, RoleVisibilityLevel.RedactedDetails, "External viewer material is redacted-summary candidate visibility."),
            _ => Decision(
                request,
                ExternalViewerRedactionClassification.BlockedByUnknownMaterial,
                RoleVisibilityLevel.NotVisible,
                ["External viewer material is unknown."])
        };
    }

    private static ExternalViewerRedactionDecision Candidate(
        ExternalViewerRedactionRequest request,
        ExternalViewerRedactionClassification classification,
        RoleVisibilityLevel level,
        string reason) =>
        Decision(request, classification, level, [reason]);

    private static ExternalViewerRedactionClassification? MaterialBlock(
        ExternalViewerRedactionMaterialKind materialKind) =>
        materialKind switch
        {
            ExternalViewerRedactionMaterialKind.RawPayload or
            ExternalViewerRedactionMaterialKind.RawProviderResponse or
            ExternalViewerRedactionMaterialKind.RawSource or
            ExternalViewerRedactionMaterialKind.RawDiff or
            ExternalViewerRedactionMaterialKind.RawPatch or
            ExternalViewerRedactionMaterialKind.RawLog => ExternalViewerRedactionClassification.BlockedByRawMaterial,
            ExternalViewerRedactionMaterialKind.CredentialMaterial => ExternalViewerRedactionClassification.BlockedByCredentialMaterial,
            ExternalViewerRedactionMaterialKind.SecretMaterial => ExternalViewerRedactionClassification.BlockedBySecretMaterial,
            ExternalViewerRedactionMaterialKind.PrivateReasoning => ExternalViewerRedactionClassification.BlockedByPrivateReasoningMaterial,
            ExternalViewerRedactionMaterialKind.AuthorityMarker => ExternalViewerRedactionClassification.BlockedByAuthorityMarker,
            ExternalViewerRedactionMaterialKind.ApprovalRecord => ExternalViewerRedactionClassification.BlockedByApprovalMaterial,
            ExternalViewerRedactionMaterialKind.PolicySatisfactionRecord => ExternalViewerRedactionClassification.BlockedByPolicyMaterial,
            ExternalViewerRedactionMaterialKind.SourcePatch or
            ExternalViewerRedactionMaterialKind.CommitPackage or
            ExternalViewerRedactionMaterialKind.PushReceipt or
            ExternalViewerRedactionMaterialKind.PullRequestMutationReceipt => ExternalViewerRedactionClassification.BlockedByMutationMaterial,
            ExternalViewerRedactionMaterialKind.ReleaseOrDeployReceipt => ExternalViewerRedactionClassification.BlockedByReleaseDeployMaterial,
            _ => null
        };

    private static bool MatrixAllowsCandidate(
        RoleVisibilityMatrix matrix,
        ExternalViewerRedactionRequest request)
    {
        var matrixMaterial = MatrixMaterialFor(request.RequestedMaterialKind);
        if (matrixMaterial is null)
        {
            return false;
        }

        return matrix.Entries.Any(entry =>
            string.Equals(entry.RoleId, request.RequestedRoleKey, StringComparison.OrdinalIgnoreCase) &&
            entry.Surface == request.RequestedSurface &&
            entry.MaterialKind == matrixMaterial.Value &&
            entry.VisibilityLevel != RoleVisibilityLevel.NotVisible);
    }

    private static RoleVisibilityMaterialKind? MatrixMaterialFor(
        ExternalViewerRedactionMaterialKind materialKind) =>
        materialKind switch
        {
            ExternalViewerRedactionMaterialKind.PublicMetadata or
            ExternalViewerRedactionMaterialKind.TenantScopedMetadata or
            ExternalViewerRedactionMaterialKind.ProjectScopedMetadata or
            ExternalViewerRedactionMaterialKind.OperationStatusMetadata or
            ExternalViewerRedactionMaterialKind.RedactedOperationStatusSummary or
            ExternalViewerRedactionMaterialKind.RedactedDiagnosticSummary or
            ExternalViewerRedactionMaterialKind.RedactedErrorSummary or
            ExternalViewerRedactionMaterialKind.RedactedLogSummary => RoleVisibilityMaterialKind.OperationStatusSummary,
            ExternalViewerRedactionMaterialKind.RedactedValidationSummary => RoleVisibilityMaterialKind.ValidationSummary,
            ExternalViewerRedactionMaterialKind.RedactedReviewSummary => RoleVisibilityMaterialKind.ProposalSummary,
            ExternalViewerRedactionMaterialKind.RedactedApprovalSummary => RoleVisibilityMaterialKind.ApprovalPackageSummary,
            ExternalViewerRedactionMaterialKind.RedactedAuditSummary => RoleVisibilityMaterialKind.AuditTrailSummary,
            ExternalViewerRedactionMaterialKind.RedactedReleaseReadinessSummary => RoleVisibilityMaterialKind.ReleaseReadinessSummary,
            ExternalViewerRedactionMaterialKind.RedactedPolicySummary => RoleVisibilityMaterialKind.PolicyReviewSummary,
            ExternalViewerRedactionMaterialKind.RedactedReceiptSummary => RoleVisibilityMaterialKind.ReceiptMetadata,
            _ => null
        };

    private static ExternalViewerRedactionDecision Decision(
        ExternalViewerRedactionRequest request,
        ExternalViewerRedactionClassification classification,
        RoleVisibilityLevel effectiveCandidateVisibility,
        IReadOnlyList<string> reasons)
    {
        var evidenceRefs = new[]
        {
            Safe(request.RoleCatalogEvidenceRef),
            Safe(request.VisibilityMatrixEvidenceRef),
            Safe(request.SourceEvidenceRef),
            Safe(request.OptionalPolicyEvidenceRef),
            Safe(request.OptionalRedactionEvidenceRef),
            Safe(request.OptionalTenantBoundaryEvidenceRef)
        }
        .Where(static item => !string.IsNullOrWhiteSpace(item))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
        .ToArray();

        return new ExternalViewerRedactionDecision
        {
            Classification = classification,
            EffectiveCandidateVisibility = effectiveCandidateVisibility,
            Reasons = reasons.Select(Safe).ToArray(),
            EvidenceRefs = evidenceRefs,
            RecordFingerprint = Fingerprint(
                request.CorrelationId,
                request.RequestedRoleKey,
                request.RequestedSurface.ToString(),
                request.RequestedMaterialKind.ToString(),
                request.RequestedIntent.ToString(),
                classification.ToString(),
                effectiveCandidateVisibility.ToString()),
            GrantsExternalViewerAuthority = false,
            GrantsRoleAssignmentAuthority = false,
            GrantsVisibilityAuthority = false,
            GrantsAccess = false,
            CreatesShareLink = false,
            ExportsRawData = false,
            GrantsCrossTenantVisibility = false,
            GrantsPlatformVisibility = false,
            GrantsApprovalAuthority = false,
            SatisfiesPolicy = false,
            RefreshesValidation = false,
            ProvesSourceSafety = false,
            GrantsDiagnosticExecutionAuthority = false,
            GrantsRetryAuthority = false,
            GrantsRollbackAuthority = false,
            GrantsRecoveryAuthority = false,
            GrantsMutationAuthority = false,
            GrantsWorkflowContinuation = false,
            GrantsMergeAuthority = false,
            GrantsReleaseAuthority = false,
            GrantsDeploymentAuthority = false,
            BypassesRedaction = false,
            DisclosesSecrets = false,
            DisclosesCredentials = false,
            DisclosesRawPayload = false,
            DisclosesRawSource = false,
            DisclosesRawLogs = false,
            DisclosesPrivateReasoning = false
        };
    }

    private static string Safe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return ExternalViewerRedactionValidator.ContainsUnsafeEvidenceText(value)
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
