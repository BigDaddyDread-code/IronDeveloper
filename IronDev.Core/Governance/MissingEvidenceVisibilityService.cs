using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public sealed class MissingEvidenceVisibilityService
{
    public MissingEvidenceVisibilityDecision Classify(
        GovernanceRoleCatalog? roleCatalog,
        RoleVisibilityMatrix? visibilityMatrix,
        ForbiddenActionCatalog? forbiddenActionCatalog,
        MissingEvidenceVisibilityRequest? request)
    {
        var requestValidation = MissingEvidenceVisibilityValidator.ValidateRequest(request);
        if (!requestValidation.IsValid)
        {
            return Decision(
                request,
                MissingEvidenceVisibilityClassification.Invalid,
                GovernanceRoleKind.Unknown,
                RoleVisibilityLevel.NotVisible,
                requestValidation.Issues);
        }

        if (string.IsNullOrWhiteSpace(request!.RoleCatalogEvidenceRef))
        {
            return Decision(
                request,
                MissingEvidenceVisibilityClassification.BlockedByMissingRoleCatalogEvidence,
                GovernanceRoleKind.Unknown,
                RoleVisibilityLevel.NotVisible,
                ["Role catalog evidence reference is required."]);
        }

        if (string.IsNullOrWhiteSpace(request.VisibilityMatrixEvidenceRef))
        {
            return Decision(
                request,
                MissingEvidenceVisibilityClassification.BlockedByMissingVisibilityMatrixEvidence,
                GovernanceRoleKind.Unknown,
                RoleVisibilityLevel.NotVisible,
                ["Visibility matrix evidence reference is required."]);
        }

        if (string.IsNullOrWhiteSpace(request.ForbiddenActionCatalogEvidenceRef))
        {
            return Decision(
                request,
                MissingEvidenceVisibilityClassification.BlockedByMissingForbiddenActionCatalogEvidence,
                GovernanceRoleKind.Unknown,
                RoleVisibilityLevel.NotVisible,
                ["Forbidden action catalog evidence reference is required."]);
        }

        if (string.IsNullOrWhiteSpace(request.SourceMissingEvidenceRef))
        {
            return Decision(
                request,
                MissingEvidenceVisibilityClassification.BlockedByMissingSourceMissingEvidenceRef,
                GovernanceRoleKind.Unknown,
                RoleVisibilityLevel.NotVisible,
                ["Source missing-evidence reference is required."]);
        }

        if (!RoleCatalogValidator.ValidateCatalog(roleCatalog).IsValid)
        {
            return Decision(
                request,
                MissingEvidenceVisibilityClassification.BlockedByMissingRoleCatalogEvidence,
                GovernanceRoleKind.Unknown,
                RoleVisibilityLevel.NotVisible,
                ["F01 role catalog evidence is invalid."]);
        }

        if (!RoleVisibilityMatrixValidator.ValidateMatrix(roleCatalog, visibilityMatrix).IsValid)
        {
            return Decision(
                request,
                MissingEvidenceVisibilityClassification.BlockedByMissingVisibilityMatrixEvidence,
                GovernanceRoleKind.Unknown,
                RoleVisibilityLevel.NotVisible,
                ["F02 visibility matrix evidence is invalid."]);
        }

        if (!ForbiddenActionCatalogValidator.ValidateCatalog(roleCatalog, forbiddenActionCatalog).IsValid)
        {
            return Decision(
                request,
                MissingEvidenceVisibilityClassification.BlockedByMissingForbiddenActionCatalogEvidence,
                GovernanceRoleKind.Unknown,
                RoleVisibilityLevel.NotVisible,
                ["F13 forbidden action catalog evidence is invalid."]);
        }

        var role = roleCatalog!.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RoleId, request.RequestedRoleId, StringComparison.OrdinalIgnoreCase));
        if (role is null)
        {
            return Decision(
                request,
                MissingEvidenceVisibilityClassification.BlockedByUnknownRole,
                GovernanceRoleKind.Unknown,
                RoleVisibilityLevel.NotVisible,
                ["Requested role is unknown."]);
        }

        if (!MissingEvidenceVisibilityValidator.IsKnownMissingEvidenceKind(request.RequestedMissingEvidenceKind))
        {
            return Decision(
                request,
                MissingEvidenceVisibilityClassification.BlockedByUnknownMissingEvidenceKind,
                role.RoleKind,
                RoleVisibilityLevel.NotVisible,
                ["Requested missing-evidence kind is unknown."]);
        }

        if (!MissingEvidenceVisibilityValidator.IsKnownMaterialKind(request.RequestedMaterialKind))
        {
            return Decision(
                request,
                MissingEvidenceVisibilityClassification.BlockedByUnknownMaterial,
                role.RoleKind,
                RoleVisibilityLevel.NotVisible,
                ["Requested missing-evidence material kind is unknown."]);
        }

        if (!MissingEvidenceVisibilityValidator.IsKnownIntent(request.RequestedIntent))
        {
            return Decision(
                request,
                MissingEvidenceVisibilityClassification.BlockedByUnknownIntent,
                role.RoleKind,
                RoleVisibilityLevel.NotVisible,
                ["Requested missing-evidence visibility intent is unknown."]);
        }

        if (!MissingEvidenceVisibilityValidator.IsReadOnlyIntent(request.RequestedIntent))
        {
            return Decision(
                request,
                MissingEvidenceVisibilityValidator.ClassifyIntentBlock(request.RequestedIntent),
                role.RoleKind,
                RoleVisibilityLevel.NotVisible,
                ["Missing-evidence visibility cannot satisfy, create, override, waive, approve, satisfy policy, refresh validation, prove source safety, execute, mutate, continue workflow, release, deploy, bypass redaction, or disclose material."]);
        }

        if (!MissingEvidenceVisibilityValidator.IsSafeVisibleMaterial(request.RequestedMaterialKind))
        {
            return Decision(
                request,
                MissingEvidenceVisibilityValidator.ClassifyMaterialBlock(request.RequestedMaterialKind),
                role.RoleKind,
                RoleVisibilityLevel.NotVisible,
                ["Requested missing-evidence material is not visible through this contract."]);
        }

        if (RequiresTenantBoundaryEvidence(role.RoleKind, request.RequestedMissingEvidenceKind) &&
            string.IsNullOrWhiteSpace(request.OptionalTenantBoundaryEvidenceRef))
        {
            return Decision(
                request,
                MissingEvidenceVisibilityClassification.BlockedByMissingTenantBoundaryEvidence,
                role.RoleKind,
                RoleVisibilityLevel.NotVisible,
                ["Tenant or project scoped missing-evidence visibility requires tenant-boundary evidence reference."]);
        }

        if (request.RequestedMaterialKind == MissingEvidenceMaterialKind.RedactedSummary &&
            string.IsNullOrWhiteSpace(request.OptionalRedactionEvidenceRef))
        {
            return Decision(
                request,
                MissingEvidenceVisibilityClassification.BlockedByMissingRedactionEvidence,
                role.RoleKind,
                RoleVisibilityLevel.NotVisible,
                ["Redacted missing-evidence visibility requires redaction evidence reference."]);
        }

        var mappedAction = MapToForbiddenAction(request.RequestedMissingEvidenceKind);
        var f13Decision = new ForbiddenActionCatalogService().Lookup(
            roleCatalog,
            forbiddenActionCatalog,
            new ForbiddenActionLookupRequest
            {
                CorrelationId = request.CorrelationId,
                RequestedRoleId = request.RequestedRoleId,
                RequestedActionKind = mappedAction,
                AuthoritySourceKind = ForbiddenActionAuthoritySourceKind.RoleEvidence,
                RoleCatalogEvidenceRef = request.RoleCatalogEvidenceRef,
                ForbiddenActionCatalogEvidenceRef = request.ForbiddenActionCatalogEvidenceRef,
                OptionalPolicyEvidenceRef = request.OptionalPolicyEvidenceRef,
                OptionalApprovalEvidenceRef = request.OptionalApprovalEvidenceRef,
                OptionalExecutionAuthorityRef = null,
                OptionalMutationAuthorityRef = null,
                OptionalWorkflowAuthorityRef = null,
                OptionalReleaseAuthorityRef = null,
                OptionalRedactionDecisionRef = request.OptionalRedactionEvidenceRef
            });

        if (f13Decision.Classification == ForbiddenActionLookupClassification.Forbidden &&
            MustBlockWhenF13Forbids(request.RequestedMissingEvidenceKind))
        {
            return Decision(
                request,
                MissingEvidenceVisibilityClassification.BlockedByForbiddenActionCatalog,
                role.RoleKind,
                RoleVisibilityLevel.NotVisible,
                ["F13 forbids role-evidence-derived authority for the missing-evidence action category."]);
        }

        return Candidate(request, role.RoleKind);
    }

    public static RoleForbiddenActionKind MapToForbiddenAction(MissingEvidenceKind kind) =>
        kind switch
        {
            MissingEvidenceKind.RoleAssignmentEvidence => RoleForbiddenActionKind.RoleAssignment,
            MissingEvidenceKind.VisibilityDecisionEvidence => RoleForbiddenActionKind.VisibilityGrant,
            MissingEvidenceKind.AccessDecisionEvidence => RoleForbiddenActionKind.AccessGrant,
            MissingEvidenceKind.TenantBoundaryEvidence => RoleForbiddenActionKind.CrossTenantVisibility,
            MissingEvidenceKind.RedactionDecisionEvidence => RoleForbiddenActionKind.RedactionBypass,
            MissingEvidenceKind.ApprovalEvidence => RoleForbiddenActionKind.ApprovalAcceptance,
            MissingEvidenceKind.PolicySatisfactionEvidence => RoleForbiddenActionKind.PolicySatisfaction,
            MissingEvidenceKind.ValidationFreshnessEvidence => RoleForbiddenActionKind.ValidationRefresh,
            MissingEvidenceKind.SourceSafetyEvidence => RoleForbiddenActionKind.SourceSafetyProof,
            MissingEvidenceKind.DiagnosticExecutionAuthority => RoleForbiddenActionKind.DiagnosticExecution,
            MissingEvidenceKind.RetryAuthority => RoleForbiddenActionKind.RetryExecution,
            MissingEvidenceKind.RollbackAuthority => RoleForbiddenActionKind.RollbackExecution,
            MissingEvidenceKind.RecoveryAuthority => RoleForbiddenActionKind.RecoveryExecution,
            MissingEvidenceKind.MutationAuthority => RoleForbiddenActionKind.SourceMutation,
            MissingEvidenceKind.PatchApplyAuthority => RoleForbiddenActionKind.PatchApply,
            MissingEvidenceKind.CommitAuthority => RoleForbiddenActionKind.CommitCreation,
            MissingEvidenceKind.PushAuthority => RoleForbiddenActionKind.PushExecution,
            MissingEvidenceKind.PullRequestAuthority => RoleForbiddenActionKind.PullRequestCreation,
            MissingEvidenceKind.ReadyForReviewAuthority => RoleForbiddenActionKind.ReadyForReview,
            MissingEvidenceKind.WorkflowContinuationEvidence => RoleForbiddenActionKind.WorkflowContinuation,
            MissingEvidenceKind.MergeAuthority => RoleForbiddenActionKind.Merge,
            MissingEvidenceKind.ReleaseAuthority => RoleForbiddenActionKind.Release,
            MissingEvidenceKind.DeploymentAuthority => RoleForbiddenActionKind.Deployment,
            MissingEvidenceKind.ExternalAccessEvidence => RoleForbiddenActionKind.ExternalAccessGrant,
            MissingEvidenceKind.ShareLinkAuthority => RoleForbiddenActionKind.ShareLinkCreation,
            MissingEvidenceKind.RawExportAuthority => RoleForbiddenActionKind.RawExport,
            MissingEvidenceKind.ScreenAccessEvidence => RoleForbiddenActionKind.ScreenAccess,
            MissingEvidenceKind.EndpointAuthorityEvidence => RoleForbiddenActionKind.EndpointInvocation,
            MissingEvidenceKind.RouteAccessEvidence => RoleForbiddenActionKind.RouteAccess,
            MissingEvidenceKind.RouteGuardEvidence => RoleForbiddenActionKind.RouteGuardCreation,
            MissingEvidenceKind.SecretDisclosureAuthority => RoleForbiddenActionKind.SecretDisclosure,
            MissingEvidenceKind.CredentialDisclosureAuthority => RoleForbiddenActionKind.CredentialDisclosure,
            MissingEvidenceKind.RawPayloadDisclosureAuthority => RoleForbiddenActionKind.RawPayloadDisclosure,
            MissingEvidenceKind.RawProviderResponseDisclosureAuthority => RoleForbiddenActionKind.RawProviderResponseDisclosure,
            MissingEvidenceKind.RawSourceDisclosureAuthority => RoleForbiddenActionKind.RawSourceDisclosure,
            MissingEvidenceKind.RawLogDisclosureAuthority => RoleForbiddenActionKind.RawLogDisclosure,
            MissingEvidenceKind.PrivateReasoningDisclosureAuthority => RoleForbiddenActionKind.PrivateReasoningDisclosure,
            _ => RoleForbiddenActionKind.Unknown
        };

    private static bool MustBlockWhenF13Forbids(MissingEvidenceKind kind) =>
        kind is MissingEvidenceKind.ApprovalEvidence or
            MissingEvidenceKind.RoleAssignmentEvidence or
            MissingEvidenceKind.VisibilityDecisionEvidence or
            MissingEvidenceKind.AccessDecisionEvidence or
            MissingEvidenceKind.TenantBoundaryEvidence or
            MissingEvidenceKind.RedactionDecisionEvidence or
            MissingEvidenceKind.PolicySatisfactionEvidence or
            MissingEvidenceKind.ValidationFreshnessEvidence or
            MissingEvidenceKind.SourceSafetyEvidence or
            MissingEvidenceKind.DiagnosticExecutionAuthority or
            MissingEvidenceKind.RetryAuthority or
            MissingEvidenceKind.RollbackAuthority or
            MissingEvidenceKind.RecoveryAuthority or
            MissingEvidenceKind.MutationAuthority or
            MissingEvidenceKind.PatchApplyAuthority or
            MissingEvidenceKind.CommitAuthority or
            MissingEvidenceKind.PushAuthority or
            MissingEvidenceKind.PullRequestAuthority or
            MissingEvidenceKind.ReadyForReviewAuthority or
            MissingEvidenceKind.WorkflowContinuationEvidence or
            MissingEvidenceKind.MergeAuthority or
            MissingEvidenceKind.ReleaseAuthority or
            MissingEvidenceKind.DeploymentAuthority or
            MissingEvidenceKind.ExternalAccessEvidence or
            MissingEvidenceKind.ShareLinkAuthority or
            MissingEvidenceKind.RawExportAuthority or
            MissingEvidenceKind.SecretDisclosureAuthority or
            MissingEvidenceKind.CredentialDisclosureAuthority or
            MissingEvidenceKind.RawPayloadDisclosureAuthority or
            MissingEvidenceKind.RawProviderResponseDisclosureAuthority or
            MissingEvidenceKind.RawSourceDisclosureAuthority or
            MissingEvidenceKind.RawLogDisclosureAuthority or
            MissingEvidenceKind.PrivateReasoningDisclosureAuthority;

    private static MissingEvidenceVisibilityDecision Candidate(
        MissingEvidenceVisibilityRequest request,
        GovernanceRoleKind roleKind)
    {
        var maxLevel = MaxVisibilityFor(roleKind);
        var materialLevel = VisibilityForMaterial(request.RequestedMaterialKind);
        var effectiveLevel = Lower(maxLevel, materialLevel);
        var classification = effectiveLevel switch
        {
            RoleVisibilityLevel.PresenceOnly => MissingEvidenceVisibilityClassification.PresenceOnlyCandidate,
            RoleVisibilityLevel.SummaryOnly => MissingEvidenceVisibilityClassification.CategoryOnlyCandidate,
            RoleVisibilityLevel.RedactedDetails => MissingEvidenceVisibilityClassification.RedactedSummaryCandidate,
            _ => MissingEvidenceVisibilityClassification.Hidden
        };

        return Decision(
            request,
            classification,
            roleKind,
            effectiveLevel,
            ["This role may see bounded missing-evidence visibility only; missing-evidence visibility is not evidence satisfaction."]);
    }

    private static RoleVisibilityLevel MaxVisibilityFor(GovernanceRoleKind roleKind) =>
        roleKind switch
        {
            GovernanceRoleKind.ExternalViewer => RoleVisibilityLevel.PresenceOnly,
            GovernanceRoleKind.Observer or
            GovernanceRoleKind.SystemReadOnly or
            GovernanceRoleKind.AutomationAgent or
            GovernanceRoleKind.TenantAdministrator => RoleVisibilityLevel.SummaryOnly,
            GovernanceRoleKind.Reviewer or
            GovernanceRoleKind.Auditor or
            GovernanceRoleKind.SecurityReviewer or
            GovernanceRoleKind.ReleaseReviewer or
            GovernanceRoleKind.ApproverCandidate or
            GovernanceRoleKind.OperationsReviewer or
            GovernanceRoleKind.ExecutorOperatorCandidate or
            GovernanceRoleKind.RollbackReviewer or
            GovernanceRoleKind.RecoveryReviewer or
            GovernanceRoleKind.SystemAccountabilityOwner => RoleVisibilityLevel.RedactedDetails,
            _ => RoleVisibilityLevel.SummaryOnly
        };

    private static RoleVisibilityLevel VisibilityForMaterial(MissingEvidenceMaterialKind materialKind) =>
        materialKind switch
        {
            MissingEvidenceMaterialKind.PresenceOnly => RoleVisibilityLevel.PresenceOnly,
            MissingEvidenceMaterialKind.CategoryOnly or
            MissingEvidenceMaterialKind.RequiredEvidenceReference => RoleVisibilityLevel.SummaryOnly,
            MissingEvidenceMaterialKind.RedactedSummary => RoleVisibilityLevel.RedactedDetails,
            _ => RoleVisibilityLevel.NotVisible
        };

    private static RoleVisibilityLevel Lower(RoleVisibilityLevel first, RoleVisibilityLevel second) =>
        (RoleVisibilityLevel)Math.Min((int)first, (int)second);

    private static bool RequiresTenantBoundaryEvidence(
        GovernanceRoleKind roleKind,
        MissingEvidenceKind kind) =>
        roleKind == GovernanceRoleKind.TenantAdministrator ||
        kind is MissingEvidenceKind.TenantBoundaryEvidence or
            MissingEvidenceKind.ExternalAccessEvidence or
            MissingEvidenceKind.ScreenAccessEvidence or
            MissingEvidenceKind.EndpointAuthorityEvidence or
            MissingEvidenceKind.RouteAccessEvidence or
            MissingEvidenceKind.RouteGuardEvidence;

    private static MissingEvidenceVisibilityDecision Decision(
        MissingEvidenceVisibilityRequest? request,
        MissingEvidenceVisibilityClassification classification,
        GovernanceRoleKind roleKind,
        RoleVisibilityLevel effectiveCandidateVisibility,
        IReadOnlyList<string> reasons)
    {
        var evidenceRefs = new[]
        {
            Safe(request?.RoleCatalogEvidenceRef),
            Safe(request?.VisibilityMatrixEvidenceRef),
            Safe(request?.ForbiddenActionCatalogEvidenceRef),
            Safe(request?.SourceMissingEvidenceRef),
            Safe(request?.OptionalTenantBoundaryEvidenceRef),
            Safe(request?.OptionalRedactionEvidenceRef),
            Safe(request?.OptionalPolicyEvidenceRef),
            Safe(request?.OptionalApprovalEvidenceRef)
        }
        .Where(static item => !string.IsNullOrWhiteSpace(item))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
        .ToArray();

        return new MissingEvidenceVisibilityDecision
        {
            Classification = classification,
            RoleId = Safe(request?.RequestedRoleId),
            RoleKind = roleKind,
            MissingEvidenceKind = request?.RequestedMissingEvidenceKind ?? MissingEvidenceKind.Unknown,
            EffectiveCandidateVisibility = effectiveCandidateVisibility,
            Reasons = reasons.Select(Safe).ToArray(),
            EvidenceRefs = evidenceRefs,
            RecordFingerprint = Fingerprint(
                request?.CorrelationId,
                request?.RequestedRoleId,
                request?.RequestedMissingEvidenceKind.ToString(),
                request?.RequestedMaterialKind.ToString(),
                request?.RequestedIntent.ToString(),
                classification.ToString(),
                roleKind.ToString(),
                effectiveCandidateVisibility.ToString()),
            IsEvidenceSatisfied = false,
            CreatesEvidence = false,
            OverridesMissingEvidence = false,
            WaivesEvidenceRequirement = false,
            GrantsRoleAssignmentAuthority = false,
            GrantsVisibilityAuthority = false,
            GrantsAccess = false,
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
            DisclosesPrivateReasoning = false,
            RequiresSeparateAuthority = true
        };
    }

    private static string Safe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return MissingEvidenceVisibilityValidator.ContainsUnsafeMissingEvidenceVisibilityText(value)
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
