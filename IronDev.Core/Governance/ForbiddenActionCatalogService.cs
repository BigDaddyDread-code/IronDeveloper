using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public sealed class ForbiddenActionCatalogService
{
    public ForbiddenActionCatalog BuildDefaultCatalog(GovernanceRoleCatalog roleCatalog)
    {
        var entries = new List<ForbiddenActionCatalogEntry>();
        foreach (var role in roleCatalog.Entries
            .Where(static entry => entry.RoleKind != GovernanceRoleKind.Unknown)
            .OrderBy(static entry => entry.RoleId, StringComparer.OrdinalIgnoreCase))
        {
            var actions = new HashSet<RoleForbiddenActionKind>(BaselineActions());
            foreach (var action in RoleSpecificActions(role.RoleKind))
            {
                actions.Add(action);
            }

            entries.AddRange(actions
                .OrderBy(static action => action)
                .Select(action => Entry(role, action)));
        }

        return new ForbiddenActionCatalog
        {
            CatalogId = "forbidden-action-catalog:f13",
            CatalogVersion = "f13",
            BoundaryStatement = "Forbidden action metadata is not authorization. A forbidden action catalog is not a permission system and not an allow list.",
            Entries = entries
        };
    }

    public ForbiddenActionLookupDecision Lookup(
        GovernanceRoleCatalog? roleCatalog,
        ForbiddenActionCatalog? catalog,
        ForbiddenActionLookupRequest? request)
    {
        var requestValidation = ForbiddenActionCatalogValidator.ValidateRequest(request);
        if (!requestValidation.IsValid)
        {
            return Decision(
                request,
                ForbiddenActionLookupClassification.Invalid,
                GovernanceRoleKind.Unknown,
                requestValidation.Issues);
        }

        if (requestValidation.UnsafeRefs.Count > 0)
        {
            return Decision(
                request,
                ForbiddenActionLookupClassification.BlockedByUnsafeEvidence,
                GovernanceRoleKind.Unknown,
                ["Forbidden action lookup evidence contains unsafe text."]);
        }

        if (string.IsNullOrWhiteSpace(request!.RoleCatalogEvidenceRef))
        {
            return Decision(
                request,
                ForbiddenActionLookupClassification.BlockedByMissingRoleCatalogEvidence,
                GovernanceRoleKind.Unknown,
                ["Role catalog evidence reference is required."]);
        }

        if (string.IsNullOrWhiteSpace(request.ForbiddenActionCatalogEvidenceRef))
        {
            return Decision(
                request,
                ForbiddenActionLookupClassification.BlockedByMissingForbiddenCatalogEvidence,
                GovernanceRoleKind.Unknown,
                ["Forbidden action catalog evidence reference is required."]);
        }

        if (!ForbiddenActionCatalogValidator.IsKnownAction(request.RequestedActionKind))
        {
            return Decision(
                request,
                ForbiddenActionLookupClassification.BlockedByUnknownAction,
                GovernanceRoleKind.Unknown,
                ["Requested action kind is unknown."]);
        }

        if (!ForbiddenActionCatalogValidator.IsKnownAuthoritySource(request.AuthoritySourceKind))
        {
            return Decision(
                request,
                ForbiddenActionLookupClassification.BlockedByUnknownAuthoritySource,
                GovernanceRoleKind.Unknown,
                ["Authority source kind is unknown."]);
        }

        if (!RoleCatalogValidator.ValidateCatalog(roleCatalog).IsValid)
        {
            return Decision(
                request,
                ForbiddenActionLookupClassification.BlockedByMissingRoleCatalogEvidence,
                GovernanceRoleKind.Unknown,
                ["F01 role catalog evidence is invalid."]);
        }

        var catalogValidation = ForbiddenActionCatalogValidator.ValidateCatalog(roleCatalog, catalog);
        if (!catalogValidation.IsValid)
        {
            return Decision(
                request,
                ForbiddenActionLookupClassification.BlockedByMissingForbiddenCatalogEvidence,
                GovernanceRoleKind.Unknown,
                ["Forbidden action catalog evidence is invalid."]);
        }

        var role = roleCatalog!.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RoleId, request.RequestedRoleId, StringComparison.OrdinalIgnoreCase));
        if (role is null)
        {
            return Decision(
                request,
                ForbiddenActionLookupClassification.BlockedByUnknownRole,
                GovernanceRoleKind.Unknown,
                ["Requested role is unknown."]);
        }

        var entry = catalog!.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.RoleId, request.RequestedRoleId, StringComparison.OrdinalIgnoreCase) &&
            candidate.RoleForbiddenActionKind == request.RequestedActionKind);

        if (entry is not null)
        {
            return Decision(
                request,
                ForbiddenActionLookupClassification.Forbidden,
                role.RoleKind,
                ["This role evidence is forbidden as authority for this action."]);
        }

        return Decision(
            request,
            ForbiddenActionLookupClassification.NoCatalogGrantSeparateAuthorityRequired,
            role.RoleKind,
            ["This catalog does not record a specific forbidden entry, but no authority is granted and separate authority is required."]);
    }

    private static ForbiddenActionCatalogEntry Entry(
        GovernanceRoleCatalogEntry role,
        RoleForbiddenActionKind actionKind) =>
        new()
        {
            RoleId = role.RoleId,
            RoleKind = role.RoleKind,
            RoleDisplayName = role.DisplayName,
            RoleForbiddenActionKind = actionKind,
            ReasonKind = ReasonFor(actionKind),
            BoundaryStatement = "Forbidden action metadata is not authorization and not a permission grant.",
            RequiredSeparateEvidenceRefs = EvidenceRefsFor(actionKind),
            AppliesWhenAuthoritySourceIsRoleEvidence = true,
            IsForbidden = true,
            IsAllowed = false,
            GrantsAuthority = false,
            GrantsPermission = false,
            SatisfiesPolicy = false,
            AllowsExecution = false,
            AllowsMutation = false,
            AllowsWorkflowContinuation = false,
            AllowsRelease = false,
            AllowsDeployment = false,
            BypassesRedaction = false,
            DisclosesSecrets = false,
            DisclosesCredentials = false,
            DisclosesRawPayload = false,
            DisclosesPrivateReasoning = false
        };

    private static IReadOnlyList<RoleForbiddenActionKind> BaselineActions() =>
    [
        RoleForbiddenActionKind.RoleAssignment,
        RoleForbiddenActionKind.PermissionManagement,
        RoleForbiddenActionKind.AccessGrant,
        RoleForbiddenActionKind.VisibilityGrant,
        RoleForbiddenActionKind.ApprovalAcceptance,
        RoleForbiddenActionKind.PolicySatisfaction,
        RoleForbiddenActionKind.ValidationRefresh,
        RoleForbiddenActionKind.SourceSafetyProof,
        RoleForbiddenActionKind.SourceMutation,
        RoleForbiddenActionKind.PatchApply,
        RoleForbiddenActionKind.CommitCreation,
        RoleForbiddenActionKind.PushExecution,
        RoleForbiddenActionKind.PullRequestCreation,
        RoleForbiddenActionKind.WorkflowContinuation,
        RoleForbiddenActionKind.Merge,
        RoleForbiddenActionKind.Release,
        RoleForbiddenActionKind.Deployment,
        RoleForbiddenActionKind.RedactionBypass,
        RoleForbiddenActionKind.SecretDisclosure,
        RoleForbiddenActionKind.CredentialDisclosure,
        RoleForbiddenActionKind.RawPayloadDisclosure,
        RoleForbiddenActionKind.PrivateReasoningDisclosure,
        RoleForbiddenActionKind.EndpointInvocation,
        RoleForbiddenActionKind.RouteAccess,
        RoleForbiddenActionKind.ScreenAccess,
        RoleForbiddenActionKind.UiAuthority,
        RoleForbiddenActionKind.ClientSidePermissionDecision,
        RoleForbiddenActionKind.LocalAuthorityState
    ];

    private static IReadOnlyList<RoleForbiddenActionKind> RoleSpecificActions(GovernanceRoleKind roleKind) =>
        roleKind switch
        {
            GovernanceRoleKind.Observer or
            GovernanceRoleKind.SystemReadOnly or
            GovernanceRoleKind.Auditor or
            GovernanceRoleKind.Reviewer or
            GovernanceRoleKind.SecurityReviewer or
            GovernanceRoleKind.ReleaseReviewer =>
            [
                RoleForbiddenActionKind.ExternalAccessGrant,
                RoleForbiddenActionKind.ShareLinkCreation,
                RoleForbiddenActionKind.RawExport,
                RoleForbiddenActionKind.CrossTenantVisibility,
                RoleForbiddenActionKind.PlatformVisibility,
                RoleForbiddenActionKind.DiagnosticExecution,
                RoleForbiddenActionKind.RetryExecution,
                RoleForbiddenActionKind.RollbackExecution,
                RoleForbiddenActionKind.RecoveryExecution
            ],
            GovernanceRoleKind.ExternalViewer =>
            [
                RoleForbiddenActionKind.ExternalAccessGrant,
                RoleForbiddenActionKind.ShareLinkCreation,
                RoleForbiddenActionKind.RawExport,
                RoleForbiddenActionKind.RawProviderResponseDisclosure,
                RoleForbiddenActionKind.RawSourceDisclosure,
                RoleForbiddenActionKind.RawLogDisclosure,
                RoleForbiddenActionKind.CrossTenantVisibility,
                RoleForbiddenActionKind.PlatformVisibility,
                RoleForbiddenActionKind.RedactionBypass
            ],
            GovernanceRoleKind.TenantAdministrator =>
            [
                RoleForbiddenActionKind.PlatformVisibility,
                RoleForbiddenActionKind.CrossTenantVisibility,
                RoleForbiddenActionKind.RoleAssignment,
                RoleForbiddenActionKind.RoleGrant,
                RoleForbiddenActionKind.RoleRevoke,
                RoleForbiddenActionKind.PermissionManagement,
                RoleForbiddenActionKind.Impersonation,
                RoleForbiddenActionKind.AccessGrant,
                RoleForbiddenActionKind.ApprovalAcceptance,
                RoleForbiddenActionKind.PolicySatisfaction,
                RoleForbiddenActionKind.DiagnosticExecution,
                RoleForbiddenActionKind.RetryExecution,
                RoleForbiddenActionKind.RollbackExecution,
                RoleForbiddenActionKind.RecoveryExecution,
                RoleForbiddenActionKind.SourceMutation,
                RoleForbiddenActionKind.WorkflowContinuation,
                RoleForbiddenActionKind.Merge,
                RoleForbiddenActionKind.Release,
                RoleForbiddenActionKind.Deployment,
                RoleForbiddenActionKind.RedactionBypass,
                RoleForbiddenActionKind.SecretDisclosure,
                RoleForbiddenActionKind.CredentialDisclosure,
                RoleForbiddenActionKind.RawPayloadDisclosure,
                RoleForbiddenActionKind.PrivateReasoningDisclosure
            ],
            GovernanceRoleKind.SystemAccountabilityOwner =>
            [
                RoleForbiddenActionKind.AccessGrant,
                RoleForbiddenActionKind.RoleAssignment,
                RoleForbiddenActionKind.RoleGrant,
                RoleForbiddenActionKind.RoleRevoke,
                RoleForbiddenActionKind.PermissionManagement,
                RoleForbiddenActionKind.PlatformVisibility,
                RoleForbiddenActionKind.CrossTenantVisibility,
                RoleForbiddenActionKind.Impersonation,
                RoleForbiddenActionKind.ApprovalAcceptance,
                RoleForbiddenActionKind.PolicySatisfaction,
                RoleForbiddenActionKind.DiagnosticExecution,
                RoleForbiddenActionKind.RetryExecution,
                RoleForbiddenActionKind.RollbackExecution,
                RoleForbiddenActionKind.RecoveryExecution,
                RoleForbiddenActionKind.SourceMutation,
                RoleForbiddenActionKind.WorkflowContinuation,
                RoleForbiddenActionKind.Merge,
                RoleForbiddenActionKind.Release,
                RoleForbiddenActionKind.Deployment,
                RoleForbiddenActionKind.RedactionBypass,
                RoleForbiddenActionKind.SecretDisclosure,
                RoleForbiddenActionKind.CredentialDisclosure,
                RoleForbiddenActionKind.RawPayloadDisclosure,
                RoleForbiddenActionKind.PrivateReasoningDisclosure
            ],
            GovernanceRoleKind.AutomationAgent =>
            [
                RoleForbiddenActionKind.WorkflowContinuation,
                RoleForbiddenActionKind.SourceMutation,
                RoleForbiddenActionKind.PatchApply,
                RoleForbiddenActionKind.CommitCreation,
                RoleForbiddenActionKind.PushExecution,
                RoleForbiddenActionKind.PullRequestCreation,
                RoleForbiddenActionKind.ReadyForReview,
                RoleForbiddenActionKind.Merge,
                RoleForbiddenActionKind.Release,
                RoleForbiddenActionKind.Deployment,
                RoleForbiddenActionKind.RetryExecution,
                RoleForbiddenActionKind.RollbackExecution,
                RoleForbiddenActionKind.RecoveryExecution,
                RoleForbiddenActionKind.PolicySatisfaction,
                RoleForbiddenActionKind.ApprovalAcceptance
            ],
            GovernanceRoleKind.ApproverCandidate =>
            [
                RoleForbiddenActionKind.ApprovalAcceptance,
                RoleForbiddenActionKind.PolicySatisfaction,
                RoleForbiddenActionKind.SourceMutation,
                RoleForbiddenActionKind.WorkflowContinuation,
                RoleForbiddenActionKind.Merge,
                RoleForbiddenActionKind.Release,
                RoleForbiddenActionKind.Deployment
            ],
            GovernanceRoleKind.OperationsReviewer or
            GovernanceRoleKind.ExecutorOperatorCandidate or
            GovernanceRoleKind.RollbackReviewer or
            GovernanceRoleKind.RecoveryReviewer =>
            [
                RoleForbiddenActionKind.DiagnosticExecution,
                RoleForbiddenActionKind.RetryExecution,
                RoleForbiddenActionKind.RollbackExecution,
                RoleForbiddenActionKind.RecoveryExecution,
                RoleForbiddenActionKind.SourceMutation,
                RoleForbiddenActionKind.WorkflowContinuation,
                RoleForbiddenActionKind.Merge,
                RoleForbiddenActionKind.Release,
                RoleForbiddenActionKind.Deployment,
                RoleForbiddenActionKind.ApprovalAcceptance,
                RoleForbiddenActionKind.PolicySatisfaction
            ],
            _ => []
        };

    private static ForbiddenActionReasonKind ReasonFor(RoleForbiddenActionKind actionKind) =>
        actionKind switch
        {
            RoleForbiddenActionKind.RoleAssignment or
            RoleForbiddenActionKind.RoleGrant or
            RoleForbiddenActionKind.RoleRevoke => ForbiddenActionReasonKind.RequiresSeparateRoleAssignment,
            RoleForbiddenActionKind.PermissionManagement or
            RoleForbiddenActionKind.AccessGrant or
            RoleForbiddenActionKind.ExternalAccessGrant or
            RoleForbiddenActionKind.ScreenAccess or
            RoleForbiddenActionKind.RouteAccess or
            RoleForbiddenActionKind.EndpointInvocation or
            RoleForbiddenActionKind.RouteGuardCreation => ForbiddenActionReasonKind.RequiresSeparateAccessDecision,
            RoleForbiddenActionKind.VisibilityGrant => ForbiddenActionReasonKind.RequiresSeparateVisibilityDecision,
            RoleForbiddenActionKind.ApprovalAcceptance => ForbiddenActionReasonKind.RequiresSeparateApprovalDecision,
            RoleForbiddenActionKind.PolicySatisfaction => ForbiddenActionReasonKind.RequiresSeparatePolicyDecision,
            RoleForbiddenActionKind.ValidationRefresh => ForbiddenActionReasonKind.RequiresSeparateValidationEvidence,
            RoleForbiddenActionKind.SourceSafetyProof => ForbiddenActionReasonKind.RequiresSeparateSourceSafetyEvidence,
            RoleForbiddenActionKind.SourceMutation or
            RoleForbiddenActionKind.PatchApply or
            RoleForbiddenActionKind.CommitCreation or
            RoleForbiddenActionKind.PushExecution or
            RoleForbiddenActionKind.PullRequestCreation or
            RoleForbiddenActionKind.ReadyForReview => ForbiddenActionReasonKind.RequiresSeparateMutationAuthority,
            RoleForbiddenActionKind.WorkflowContinuation => ForbiddenActionReasonKind.RequiresSeparateWorkflowAuthority,
            RoleForbiddenActionKind.Merge or
            RoleForbiddenActionKind.Release => ForbiddenActionReasonKind.RequiresSeparateReleaseAuthority,
            RoleForbiddenActionKind.Deployment => ForbiddenActionReasonKind.RequiresSeparateDeploymentAuthority,
            RoleForbiddenActionKind.RedactionBypass => ForbiddenActionReasonKind.RequiresSeparateRedactionDecision,
            RoleForbiddenActionKind.CrossTenantVisibility or
            RoleForbiddenActionKind.PlatformVisibility or
            RoleForbiddenActionKind.Impersonation => ForbiddenActionReasonKind.RequiresSeparateTenantBoundaryDecision,
            RoleForbiddenActionKind.SecretDisclosure or
            RoleForbiddenActionKind.CredentialDisclosure => ForbiddenActionReasonKind.SensitiveMaterialNeverFromRoleEvidence,
            RoleForbiddenActionKind.RawExport or
            RoleForbiddenActionKind.RawPayloadDisclosure or
            RoleForbiddenActionKind.RawProviderResponseDisclosure or
            RoleForbiddenActionKind.RawSourceDisclosure or
            RoleForbiddenActionKind.RawLogDisclosure => ForbiddenActionReasonKind.RawMaterialNeverFromRoleEvidence,
            RoleForbiddenActionKind.PrivateReasoningDisclosure => ForbiddenActionReasonKind.PrivateReasoningNeverFromRoleEvidence,
            RoleForbiddenActionKind.DiagnosticExecution or
            RoleForbiddenActionKind.RetryExecution or
            RoleForbiddenActionKind.RollbackExecution or
            RoleForbiddenActionKind.RecoveryExecution => ForbiddenActionReasonKind.RequiresSeparateExecutionAuthority,
            _ => ForbiddenActionReasonKind.RoleEvidenceCannotGrantAuthority
        };

    private static IReadOnlyList<string> EvidenceRefsFor(RoleForbiddenActionKind actionKind) =>
        ReasonFor(actionKind) switch
        {
            ForbiddenActionReasonKind.RequiresSeparateRoleAssignment => ["role-assignment-evidence:f13"],
            ForbiddenActionReasonKind.RequiresSeparateVisibilityDecision => ["visibility-decision-evidence:f13"],
            ForbiddenActionReasonKind.RequiresSeparateAccessDecision => ["access-decision-evidence:f13"],
            ForbiddenActionReasonKind.RequiresSeparateApprovalDecision => ["approval-evidence:f13"],
            ForbiddenActionReasonKind.RequiresSeparatePolicyDecision => ["policy-evidence:f13"],
            ForbiddenActionReasonKind.RequiresSeparateValidationEvidence => ["validation-evidence:f13"],
            ForbiddenActionReasonKind.RequiresSeparateSourceSafetyEvidence => ["source-safety-evidence:f13"],
            ForbiddenActionReasonKind.RequiresSeparateMutationAuthority => ["mutation-authority-evidence:f13"],
            ForbiddenActionReasonKind.RequiresSeparateWorkflowAuthority => ["workflow-authority-evidence:f13"],
            ForbiddenActionReasonKind.RequiresSeparateReleaseAuthority => ["release-authority-evidence:f13"],
            ForbiddenActionReasonKind.RequiresSeparateDeploymentAuthority => ["deployment-authority-evidence:f13"],
            ForbiddenActionReasonKind.RequiresSeparateRedactionDecision => ["redaction-decision-evidence:f13"],
            ForbiddenActionReasonKind.RequiresSeparateTenantBoundaryDecision => ["tenant-boundary-evidence:f13"],
            ForbiddenActionReasonKind.SensitiveMaterialNeverFromRoleEvidence => ["redaction-decision-evidence:f13", "tenant-boundary-evidence:f13"],
            ForbiddenActionReasonKind.RawMaterialNeverFromRoleEvidence => ["raw-material-never-from-role-evidence:f13"],
            ForbiddenActionReasonKind.PrivateReasoningNeverFromRoleEvidence => ["private-reasoning-never-from-role-evidence:f13"],
            _ => ["separate-authority-evidence:f13"]
        };

    private static ForbiddenActionLookupDecision Decision(
        ForbiddenActionLookupRequest? request,
        ForbiddenActionLookupClassification classification,
        GovernanceRoleKind roleKind,
        IReadOnlyList<string> reasons)
    {
        var evidenceRefs = new[]
        {
            Safe(request?.RoleCatalogEvidenceRef),
            Safe(request?.ForbiddenActionCatalogEvidenceRef),
            Safe(request?.OptionalPolicyEvidenceRef),
            Safe(request?.OptionalApprovalEvidenceRef),
            Safe(request?.OptionalExecutionAuthorityRef),
            Safe(request?.OptionalMutationAuthorityRef),
            Safe(request?.OptionalWorkflowAuthorityRef),
            Safe(request?.OptionalReleaseAuthorityRef),
            Safe(request?.OptionalRedactionDecisionRef)
        }
        .Where(static item => !string.IsNullOrWhiteSpace(item))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
        .ToArray();

        return new ForbiddenActionLookupDecision
        {
            Classification = classification,
            RoleId = Safe(request?.RequestedRoleId),
            RoleKind = roleKind,
            ActionKind = request?.RequestedActionKind ?? RoleForbiddenActionKind.Unknown,
            Reasons = reasons.Select(Safe).ToArray(),
            EvidenceRefs = evidenceRefs,
            RecordFingerprint = Fingerprint(
                request?.CorrelationId,
                request?.RequestedRoleId,
                request?.RequestedActionKind.ToString(),
                request?.AuthoritySourceKind.ToString(),
                classification.ToString(),
                roleKind.ToString()),
            IsAllowed = false,
            GrantsAuthority = false,
            GrantsPermission = false,
            SatisfiesPolicy = false,
            AllowsExecution = false,
            AllowsMutation = false,
            AllowsWorkflowContinuation = false,
            AllowsMerge = false,
            AllowsRelease = false,
            AllowsDeployment = false,
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

        return ForbiddenActionCatalogValidator.ContainsUnsafeForbiddenActionText(value)
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
