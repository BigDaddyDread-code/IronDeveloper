using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public sealed class OperatorSupportDiagnosticVisibilityService
{
    public OperatorSupportDiagnosticVisibilityDecision Classify(
        GovernanceRoleCatalog catalog,
        RoleVisibilityMatrix matrix,
        OperatorSupportDiagnosticVisibilityRequest request)
    {
        var validation = OperatorSupportDiagnosticVisibilityValidator.ValidateRequest(request);
        if (!validation.IsValid)
        {
            var unsafePayload = validation.Issues.Any(static issue => issue.EndsWith("Unsafe", StringComparison.Ordinal));
            return Decision(
                request,
                unsafePayload ? OperatorSupportDiagnosticVisibilityClassification.Hidden : OperatorSupportDiagnosticVisibilityClassification.Invalid,
                RoleVisibilityLevel.NotVisible,
                validation.Issues);
        }

        if (string.IsNullOrWhiteSpace(request.RoleCatalogEvidenceRef))
        {
            return Decision(
                request,
                OperatorSupportDiagnosticVisibilityClassification.BlockedByMissingCatalogEvidence,
                RoleVisibilityLevel.NotVisible,
                ["Role catalog evidence reference is required."]);
        }

        if (string.IsNullOrWhiteSpace(request.VisibilityMatrixEvidenceRef))
        {
            return Decision(
                request,
                OperatorSupportDiagnosticVisibilityClassification.BlockedByMissingMatrixEvidence,
                RoleVisibilityLevel.NotVisible,
                ["Visibility matrix evidence reference is required."]);
        }

        if (string.IsNullOrWhiteSpace(request.DiagnosticEvidenceRef))
        {
            return Decision(
                request,
                OperatorSupportDiagnosticVisibilityClassification.BlockedByMissingDiagnosticEvidence,
                RoleVisibilityLevel.NotVisible,
                ["Diagnostic evidence reference is required."]);
        }

        var catalogValidation = RoleCatalogValidator.ValidateCatalog(catalog);
        if (!catalogValidation.IsValid)
        {
            return Decision(
                request,
                OperatorSupportDiagnosticVisibilityClassification.BlockedByMissingCatalogEvidence,
                RoleVisibilityLevel.NotVisible,
                ["F01 role catalog evidence is invalid."]);
        }

        var matrixValidation = RoleVisibilityMatrixValidator.ValidateMatrix(catalog, matrix);
        if (!matrixValidation.IsValid)
        {
            return Decision(
                request,
                OperatorSupportDiagnosticVisibilityClassification.BlockedByMissingMatrixEvidence,
                RoleVisibilityLevel.NotVisible,
                ["F02 visibility matrix evidence is invalid."]);
        }

        var role = catalog.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RoleId, request.RequestedRoleKey, StringComparison.OrdinalIgnoreCase));
        if (role is null || !IsOperatorSupportRole(role.RoleKind))
        {
            return Decision(
                request,
                OperatorSupportDiagnosticVisibilityClassification.BlockedByNonOperatorSupportRole,
                RoleVisibilityLevel.NotVisible,
                ["Requested role key is not an F01 operator or support role."]);
        }

        if (request.RequestedMaterialKind == OperatorSupportDiagnosticMaterialKind.Unknown)
        {
            return Decision(
                request,
                OperatorSupportDiagnosticVisibilityClassification.BlockedByUnknownMaterial,
                RoleVisibilityLevel.NotVisible,
                ["Operator/support diagnostic material is unknown."]);
        }

        if (request.RequestedIntent == OperatorSupportDiagnosticRequestedIntent.Unknown)
        {
            return Decision(
                request,
                OperatorSupportDiagnosticVisibilityClassification.BlockedByUnknownIntent,
                RoleVisibilityLevel.NotVisible,
                ["Operator/support diagnostic intent is unknown."]);
        }

        if (OperatorSupportDiagnosticVisibilityValidator.IsActionIntent(request.RequestedIntent))
        {
            return Decision(
                request,
                OperatorSupportDiagnosticVisibilityValidator.ClassifyIntentBlock(request.RequestedIntent),
                RoleVisibilityLevel.NotVisible,
                ["Operator/support diagnostic visibility cannot be used for diagnostic execution, validation refresh, source-safety proof, retry, rollback, recovery, mutation, approval, policy, workflow, merge, release, deployment, access-grant, redaction-bypass, secret disclosure, or private-reasoning disclosure intent."]);
        }

        var materialClassification = MaterialBlock(request.RequestedMaterialKind);
        if (materialClassification is not null)
        {
            return Decision(
                request,
                materialClassification.Value,
                RoleVisibilityLevel.NotVisible,
                [$"{request.RequestedMaterialKind} is not visible through operator/support diagnostic visibility."]);
        }

        var mappedMatrixMaterial = MatrixMaterialFor(request.RequestedMaterialKind);
        if (mappedMatrixMaterial is null ||
            !MatrixAllowsDiagnosticEvidence(matrix, request.RequestedRoleKey, request.RequestedSurface, mappedMatrixMaterial.Value))
        {
            return Decision(
                request,
                OperatorSupportDiagnosticVisibilityClassification.Hidden,
                RoleVisibilityLevel.NotVisible,
                ["F02 visibility matrix does not expose this operator/support diagnostic surface."]);
        }

        if (RequiresRedactionEvidence(request.RequestedMaterialKind) &&
            string.IsNullOrWhiteSpace(request.OptionalRedactionEvidenceRef))
        {
            return Decision(
                request,
                OperatorSupportDiagnosticVisibilityClassification.BlockedByMissingRedactionEvidence,
                RoleVisibilityLevel.NotVisible,
                ["Redacted operator/support diagnostic summary requires redaction evidence."]);
        }

        return request.RequestedMaterialKind switch
        {
            OperatorSupportDiagnosticMaterialKind.OperationStatusMetadata =>
                Candidate(request, OperatorSupportDiagnosticVisibilityClassification.MetadataOnlyCandidate, RoleVisibilityLevel.MetadataOnly, "Operation status metadata is metadata-only candidate visibility."),
            OperatorSupportDiagnosticMaterialKind.OperationStatusSummary =>
                Candidate(request, OperatorSupportDiagnosticVisibilityClassification.SummaryCandidate, RoleVisibilityLevel.SummaryOnly, "Operation status summary is summary candidate visibility."),
            OperatorSupportDiagnosticMaterialKind.ValidationSummary =>
                Candidate(request, OperatorSupportDiagnosticVisibilityClassification.SummaryCandidate, RoleVisibilityLevel.SummaryOnly, "Validation summary is summary candidate visibility."),
            OperatorSupportDiagnosticMaterialKind.FailureClassificationSummary =>
                Candidate(request, OperatorSupportDiagnosticVisibilityClassification.SummaryCandidate, RoleVisibilityLevel.SummaryOnly, "Failure classification summary is summary candidate visibility."),
            OperatorSupportDiagnosticMaterialKind.RetryClassificationSummary =>
                Candidate(request, OperatorSupportDiagnosticVisibilityClassification.SummaryCandidate, RoleVisibilityLevel.SummaryOnly, "Retry classification summary is summary candidate visibility."),
            OperatorSupportDiagnosticMaterialKind.RollbackReadinessSummary =>
                Candidate(request, OperatorSupportDiagnosticVisibilityClassification.SummaryCandidate, RoleVisibilityLevel.SummaryOnly, "Rollback readiness summary is summary candidate visibility."),
            OperatorSupportDiagnosticMaterialKind.RecoveryRecommendationSummary =>
                Candidate(request, OperatorSupportDiagnosticVisibilityClassification.RedactedSummaryCandidate, RoleVisibilityLevel.RedactedDetails, "Recovery recommendation summary is redacted summary candidate visibility."),
            OperatorSupportDiagnosticMaterialKind.DependencyHealthSummary =>
                Candidate(request, OperatorSupportDiagnosticVisibilityClassification.SummaryCandidate, RoleVisibilityLevel.SummaryOnly, "Dependency health summary is summary candidate visibility."),
            OperatorSupportDiagnosticMaterialKind.EnvironmentReadinessSummary =>
                Candidate(request, OperatorSupportDiagnosticVisibilityClassification.SummaryCandidate, RoleVisibilityLevel.SummaryOnly, "Environment readiness summary is summary candidate visibility."),
            OperatorSupportDiagnosticMaterialKind.QueueOrRunnerStateSummary =>
                Candidate(request, OperatorSupportDiagnosticVisibilityClassification.SummaryCandidate, RoleVisibilityLevel.SummaryOnly, "Queue or runner state summary is summary candidate visibility."),
            OperatorSupportDiagnosticMaterialKind.RedactedErrorSummary =>
                Candidate(request, OperatorSupportDiagnosticVisibilityClassification.RedactedSummaryCandidate, RoleVisibilityLevel.RedactedDetails, "Redacted error summary is redacted summary candidate visibility."),
            OperatorSupportDiagnosticMaterialKind.RedactedLogSummary =>
                Candidate(request, OperatorSupportDiagnosticVisibilityClassification.RedactedSummaryCandidate, RoleVisibilityLevel.RedactedDetails, "Redacted log summary is redacted summary candidate visibility."),
            OperatorSupportDiagnosticMaterialKind.RedactedDiagnosticRationaleSummary =>
                Candidate(request, OperatorSupportDiagnosticVisibilityClassification.RedactedSummaryCandidate, RoleVisibilityLevel.RedactedDetails, "Redacted diagnostic rationale summary is redacted summary candidate visibility."),
            _ => Decision(
                request,
                OperatorSupportDiagnosticVisibilityClassification.BlockedByUnknownMaterial,
                RoleVisibilityLevel.NotVisible,
                ["Operator/support diagnostic material is unknown."])
        };
    }

    private static OperatorSupportDiagnosticVisibilityDecision Candidate(
        OperatorSupportDiagnosticVisibilityRequest request,
        OperatorSupportDiagnosticVisibilityClassification classification,
        RoleVisibilityLevel level,
        string reason) =>
        Decision(request, classification, level, [reason]);

    private static bool IsOperatorSupportRole(GovernanceRoleKind roleKind) =>
        roleKind is GovernanceRoleKind.OperationsReviewer or
            GovernanceRoleKind.ExecutorOperatorCandidate or
            GovernanceRoleKind.RollbackReviewer or
            GovernanceRoleKind.RecoveryReviewer;

    private static OperatorSupportDiagnosticVisibilityClassification? MaterialBlock(
        OperatorSupportDiagnosticMaterialKind materialKind) =>
        materialKind switch
        {
            OperatorSupportDiagnosticMaterialKind.RawLog => OperatorSupportDiagnosticVisibilityClassification.BlockedByRawLogMaterial,
            OperatorSupportDiagnosticMaterialKind.RawPayload => OperatorSupportDiagnosticVisibilityClassification.BlockedByRawPayloadMaterial,
            OperatorSupportDiagnosticMaterialKind.RawProviderResponse => OperatorSupportDiagnosticVisibilityClassification.BlockedByRawPayloadMaterial,
            OperatorSupportDiagnosticMaterialKind.CredentialMaterial => OperatorSupportDiagnosticVisibilityClassification.BlockedByCredentialMaterial,
            OperatorSupportDiagnosticMaterialKind.SecretMaterial => OperatorSupportDiagnosticVisibilityClassification.BlockedBySecretMaterial,
            OperatorSupportDiagnosticMaterialKind.PrivateReasoning => OperatorSupportDiagnosticVisibilityClassification.BlockedByPrivateReasoningMaterial,
            OperatorSupportDiagnosticMaterialKind.AuthorityMarker => OperatorSupportDiagnosticVisibilityClassification.BlockedByAuthorityMarker,
            OperatorSupportDiagnosticMaterialKind.SourcePatch => OperatorSupportDiagnosticVisibilityClassification.Hidden,
            OperatorSupportDiagnosticMaterialKind.CommitPackage => OperatorSupportDiagnosticVisibilityClassification.Hidden,
            OperatorSupportDiagnosticMaterialKind.PushReceipt => OperatorSupportDiagnosticVisibilityClassification.Hidden,
            OperatorSupportDiagnosticMaterialKind.PullRequestMutationReceipt => OperatorSupportDiagnosticVisibilityClassification.Hidden,
            OperatorSupportDiagnosticMaterialKind.ReleaseOrDeployReceipt => OperatorSupportDiagnosticVisibilityClassification.Hidden,
            _ => null
        };

    private static bool RequiresRedactionEvidence(
        OperatorSupportDiagnosticMaterialKind materialKind) =>
        materialKind is OperatorSupportDiagnosticMaterialKind.RedactedErrorSummary or
            OperatorSupportDiagnosticMaterialKind.RedactedLogSummary or
            OperatorSupportDiagnosticMaterialKind.RedactedDiagnosticRationaleSummary;

    private static RoleVisibilityMaterialKind? MatrixMaterialFor(
        OperatorSupportDiagnosticMaterialKind materialKind) =>
        materialKind switch
        {
            OperatorSupportDiagnosticMaterialKind.OperationStatusMetadata => RoleVisibilityMaterialKind.OperationStatusSummary,
            OperatorSupportDiagnosticMaterialKind.OperationStatusSummary => RoleVisibilityMaterialKind.OperationStatusSummary,
            OperatorSupportDiagnosticMaterialKind.ValidationSummary => RoleVisibilityMaterialKind.OperationStatusSummary,
            OperatorSupportDiagnosticMaterialKind.FailureClassificationSummary => RoleVisibilityMaterialKind.OperationStatusSummary,
            OperatorSupportDiagnosticMaterialKind.RetryClassificationSummary => RoleVisibilityMaterialKind.OperationStatusSummary,
            OperatorSupportDiagnosticMaterialKind.RollbackReadinessSummary => RoleVisibilityMaterialKind.RollbackSummary,
            OperatorSupportDiagnosticMaterialKind.RecoveryRecommendationSummary => RoleVisibilityMaterialKind.RecoverySummary,
            OperatorSupportDiagnosticMaterialKind.DependencyHealthSummary => RoleVisibilityMaterialKind.DeploymentReadinessSummary,
            OperatorSupportDiagnosticMaterialKind.EnvironmentReadinessSummary => RoleVisibilityMaterialKind.DeploymentReadinessSummary,
            OperatorSupportDiagnosticMaterialKind.QueueOrRunnerStateSummary => RoleVisibilityMaterialKind.OperationStatusSummary,
            OperatorSupportDiagnosticMaterialKind.RedactedErrorSummary => RoleVisibilityMaterialKind.OperationStatusSummary,
            OperatorSupportDiagnosticMaterialKind.RedactedLogSummary => RoleVisibilityMaterialKind.OperationStatusSummary,
            OperatorSupportDiagnosticMaterialKind.RedactedDiagnosticRationaleSummary => RoleVisibilityMaterialKind.OperationStatusSummary,
            _ => null
        };

    private static bool MatrixAllowsDiagnosticEvidence(
        RoleVisibilityMatrix matrix,
        string roleKey,
        RoleVisibilitySurface surface,
        RoleVisibilityMaterialKind materialKind) =>
        matrix.Entries.Any(entry =>
            string.Equals(entry.RoleId, roleKey, StringComparison.OrdinalIgnoreCase) &&
            entry.Surface == surface &&
            entry.MaterialKind == materialKind &&
            entry.VisibilityLevel != RoleVisibilityLevel.NotVisible);

    private static OperatorSupportDiagnosticVisibilityDecision Decision(
        OperatorSupportDiagnosticVisibilityRequest request,
        OperatorSupportDiagnosticVisibilityClassification classification,
        RoleVisibilityLevel effectiveCandidateVisibility,
        IReadOnlyList<string> reasons)
    {
        var evidenceRefs = new[]
        {
            Safe(request.RoleCatalogEvidenceRef),
            Safe(request.VisibilityMatrixEvidenceRef),
            Safe(request.DiagnosticEvidenceRef),
            Safe(request.OptionalPolicyEvidenceRef),
            Safe(request.OptionalRedactionEvidenceRef)
        }
        .Where(static item => !string.IsNullOrWhiteSpace(item))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
        .ToArray();

        return new OperatorSupportDiagnosticVisibilityDecision
        {
            Classification = classification,
            EffectiveCandidateVisibility = effectiveCandidateVisibility,
            Reasons = reasons.Select(Safe).ToArray(),
            EvidenceRefs = evidenceRefs,
            GrantsOperatorAuthority = false,
            GrantsSupportAuthority = false,
            GrantsRoleAssignmentAuthority = false,
            GrantsVisibilityAuthority = false,
            GrantsAccess = false,
            GrantsDiagnosticExecutionAuthority = false,
            RefreshesValidation = false,
            ProvesSourceSafety = false,
            GrantsRetryAuthority = false,
            GrantsRollbackAuthority = false,
            GrantsRecoveryAuthority = false,
            GrantsApprovalAuthority = false,
            SatisfiesPolicy = false,
            GrantsMutationAuthority = false,
            GrantsWorkflowContinuation = false,
            GrantsMergeAuthority = false,
            GrantsReleaseAuthority = false,
            GrantsDeploymentAuthority = false,
            BypassesRedaction = false,
            DisclosesSecrets = false,
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

        return OperatorSupportDiagnosticVisibilityValidator.ContainsUnsafeEvidenceText(value)
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
