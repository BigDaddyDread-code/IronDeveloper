using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public sealed class BackendEndpointCapabilityMetadataService
{
    public BackendEndpointCapabilityDecision Classify(
        GovernanceRoleCatalog? roleCatalog,
        RoleVisibilityMatrix? visibilityMatrix,
        BackendEndpointCapabilityMetadataCatalog? endpointCatalog,
        BackendEndpointCapabilityMetadataRequest? request)
    {
        var requestValidation = BackendEndpointCapabilityMetadataValidator.ValidateRequest(request);
        if (!requestValidation.IsValid)
        {
            return Decision(
                request,
                BackendEndpointCapabilityClassification.Invalid,
                RoleVisibilityLevel.NotVisible,
                requestValidation.Issues);
        }

        var catalogValidation = BackendEndpointCapabilityMetadataValidator.ValidateCatalog(endpointCatalog);
        if (!catalogValidation.IsValid)
        {
            return Decision(
                request,
                BackendEndpointCapabilityClassification.BlockedByMissingEndpointMetadataEvidence,
                RoleVisibilityLevel.NotVisible,
                ["Endpoint capability metadata catalog evidence is invalid."]);
        }

        if (string.IsNullOrWhiteSpace(request!.EndpointMetadataEvidenceRef))
        {
            return Decision(
                request,
                BackendEndpointCapabilityClassification.BlockedByMissingEndpointMetadataEvidence,
                RoleVisibilityLevel.NotVisible,
                ["Endpoint metadata evidence reference is required."]);
        }

        if (string.IsNullOrWhiteSpace(request.RoleCatalogEvidenceRef))
        {
            return Decision(
                request,
                BackendEndpointCapabilityClassification.BlockedByMissingCatalogEvidence,
                RoleVisibilityLevel.NotVisible,
                ["Role catalog evidence reference is required."]);
        }

        if (string.IsNullOrWhiteSpace(request.VisibilityMatrixEvidenceRef))
        {
            return Decision(
                request,
                BackendEndpointCapabilityClassification.BlockedByMissingMatrixEvidence,
                RoleVisibilityLevel.NotVisible,
                ["Visibility matrix evidence reference is required."]);
        }

        if (!RoleCatalogValidator.ValidateCatalog(roleCatalog).IsValid)
        {
            return Decision(
                request,
                BackendEndpointCapabilityClassification.BlockedByMissingCatalogEvidence,
                RoleVisibilityLevel.NotVisible,
                ["F01 role catalog evidence is invalid."]);
        }

        if (!RoleVisibilityMatrixValidator.ValidateMatrix(roleCatalog, visibilityMatrix).IsValid)
        {
            return Decision(
                request,
                BackendEndpointCapabilityClassification.BlockedByMissingMatrixEvidence,
                RoleVisibilityLevel.NotVisible,
                ["F02 visibility matrix evidence is invalid."]);
        }

        var entry = endpointCatalog!.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.EndpointKey, request.RequestedEndpointKey, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return Decision(
                request,
                BackendEndpointCapabilityClassification.BlockedByUnknownEndpoint,
                RoleVisibilityLevel.NotVisible,
                ["Requested endpoint metadata entry is unknown."]);
        }

        if (request.RequestedIntent == BackendEndpointCapabilityIntent.Unknown)
        {
            return Decision(
                request,
                BackendEndpointCapabilityClassification.BlockedByUnknownIntent,
                RoleVisibilityLevel.NotVisible,
                ["Endpoint metadata intent is unknown."]);
        }

        if (!BackendEndpointCapabilityMetadataValidator.IsMetadataIntent(request.RequestedIntent))
        {
            return Decision(
                request,
                BackendEndpointCapabilityMetadataValidator.ClassifyIntentBlock(request.RequestedIntent),
                RoleVisibilityLevel.NotVisible,
                ["Endpoint capability metadata cannot be used for route access, invocation, route guard, approval, policy, diagnostic, retry, rollback, recovery, mutation, workflow, merge, release, deploy, redaction bypass, or disclosure intent."]);
        }

        var sensitiveBlock = SensitiveBlock(entry);
        if (sensitiveBlock is not null)
        {
            return Decision(
                request,
                sensitiveBlock.Value,
                RoleVisibilityLevel.NotVisible,
                [$"{entry.SensitivityKind} endpoint capability metadata is blocked."]);
        }

        if (BackendEndpointCapabilityMetadataValidator.RequiresPolicyEvidence(entry) &&
            string.IsNullOrWhiteSpace(request.OptionalPolicyEvidenceRef))
        {
            return Decision(
                request,
                BackendEndpointCapabilityClassification.BlockedByMissingPolicyEvidence,
                RoleVisibilityLevel.NotVisible,
                ["Sensitive endpoint capability metadata requires policy evidence reference."]);
        }

        if (BackendEndpointCapabilityMetadataValidator.RequiresRedactionEvidence(entry) &&
            string.IsNullOrWhiteSpace(request.OptionalRedactionEvidenceRef))
        {
            return Decision(
                request,
                BackendEndpointCapabilityClassification.BlockedByMissingRedactionEvidence,
                RoleVisibilityLevel.NotVisible,
                ["Redacted endpoint capability metadata requires redaction evidence reference."]);
        }

        if (BackendEndpointCapabilityMetadataValidator.RequiresTenantBoundaryEvidence(entry) &&
            string.IsNullOrWhiteSpace(request.OptionalTenantBoundaryEvidenceRef))
        {
            return Decision(
                request,
                BackendEndpointCapabilityClassification.BlockedByMissingTenantBoundaryEvidence,
                RoleVisibilityLevel.NotVisible,
                ["Tenant-scoped or project-scoped endpoint metadata requires tenant-boundary evidence reference."]);
        }

        return Candidate(request, entry);
    }

    private static BackendEndpointCapabilityClassification? SensitiveBlock(
        BackendEndpointCapabilityMetadataEntry entry) =>
        entry.SensitivityKind switch
        {
            BackendEndpointSensitivityKind.RawPayload => BackendEndpointCapabilityClassification.BlockedByRawCapability,
            BackendEndpointSensitivityKind.CredentialMaterial => BackendEndpointCapabilityClassification.BlockedBySecretCapability,
            BackendEndpointSensitivityKind.SecretMaterial => BackendEndpointCapabilityClassification.BlockedBySecretCapability,
            BackendEndpointSensitivityKind.PrivateReasoning => BackendEndpointCapabilityClassification.BlockedByPrivateReasoningCapability,
            BackendEndpointSensitivityKind.MutationMaterial => BackendEndpointCapabilityClassification.BlockedBySensitiveCapability,
            BackendEndpointSensitivityKind.ReleaseDeployMaterial => BackendEndpointCapabilityClassification.BlockedBySensitiveCapability,
            _ => null
        };

    private static BackendEndpointCapabilityDecision Candidate(
        BackendEndpointCapabilityMetadataRequest request,
        BackendEndpointCapabilityMetadataEntry entry) =>
        entry.CapabilityKind switch
        {
            BackendEndpointCapabilityKind.ReadOnlyMetadata or
            BackendEndpointCapabilityKind.ReceiptReadModel or
            BackendEndpointCapabilityKind.MutationEndpoint or
            BackendEndpointCapabilityKind.ExecutionEndpoint or
            BackendEndpointCapabilityKind.AdminEndpoint or
            BackendEndpointCapabilityKind.RawExportEndpoint or
            BackendEndpointCapabilityKind.ExternalShareEndpoint =>
                Decision(
                    request,
                    BackendEndpointCapabilityClassification.MetadataOnlyCandidate,
                    RoleVisibilityLevel.MetadataOnly,
                    ["Endpoint capability metadata is metadata-only candidate visibility and not endpoint authority."]),
            BackendEndpointCapabilityKind.ReadOnlySummary or
            BackendEndpointCapabilityKind.StatusReadModel or
            BackendEndpointCapabilityKind.ProposalReadModel or
            BackendEndpointCapabilityKind.AuditReadModel =>
                Decision(
                    request,
                    BackendEndpointCapabilityClassification.SummaryCandidate,
                    RoleVisibilityLevel.SummaryOnly,
                    ["Endpoint capability metadata is summary candidate visibility and not endpoint authority."]),
            BackendEndpointCapabilityKind.RedactedSummary or
            BackendEndpointCapabilityKind.ApprovalPackageReadModel or
            BackendEndpointCapabilityKind.PolicyReviewReadModel or
            BackendEndpointCapabilityKind.ValidationReviewReadModel or
            BackendEndpointCapabilityKind.OperationDiagnosticReadModel or
            BackendEndpointCapabilityKind.ReleaseReadinessReadModel =>
                Decision(
                    request,
                    BackendEndpointCapabilityClassification.RedactedSummaryCandidate,
                    RoleVisibilityLevel.RedactedDetails,
                    ["Endpoint capability metadata is redacted-summary candidate visibility and not endpoint authority."]),
            _ => Decision(
                request,
                BackendEndpointCapabilityClassification.Invalid,
                RoleVisibilityLevel.NotVisible,
                ["Endpoint capability kind is invalid."])
        };

    private static BackendEndpointCapabilityDecision Decision(
        BackendEndpointCapabilityMetadataRequest? request,
        BackendEndpointCapabilityClassification classification,
        RoleVisibilityLevel effectiveCandidateVisibility,
        IReadOnlyList<string> reasons)
    {
        var evidenceRefs = new[]
        {
            Safe(request?.EndpointMetadataEvidenceRef),
            Safe(request?.RoleCatalogEvidenceRef),
            Safe(request?.VisibilityMatrixEvidenceRef),
            Safe(request?.OptionalPolicyEvidenceRef),
            Safe(request?.OptionalRedactionEvidenceRef),
            Safe(request?.OptionalTenantBoundaryEvidenceRef)
        }
        .Where(static item => !string.IsNullOrWhiteSpace(item))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
        .ToArray();

        return new BackendEndpointCapabilityDecision
        {
            Classification = classification,
            EffectiveCandidateVisibility = effectiveCandidateVisibility,
            EndpointKey = Safe(request?.RequestedEndpointKey),
            Reasons = reasons.Select(Safe).ToArray(),
            EvidenceRefs = evidenceRefs,
            RecordFingerprint = Fingerprint(
                request?.CorrelationId,
                request?.RequestedEndpointKey,
                request?.RequestedIntent.ToString(),
                classification.ToString(),
                effectiveCandidateVisibility.ToString()),
            GrantsEndpointAuthority = false,
            GrantsRouteAccess = false,
            AllowsInvocation = false,
            CreatesRouteGuard = false,
            GrantsRoleAssignmentAuthority = false,
            GrantsVisibilityAuthority = false,
            GrantsAccess = false,
            GrantsExternalAccess = false,
            AcceptsApproval = false,
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
            DisclosesPrivateReasoning = false
        };
    }

    private static string Safe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return BackendEndpointCapabilityMetadataValidator.ContainsUnsafeEndpointMetadataText(value)
            ? "[unsafe-rejected]"
            : value;
    }

    private static string Fingerprint(params string?[] parts)
    {
        var canonical = string.Join("|", parts.Select(Safe));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
