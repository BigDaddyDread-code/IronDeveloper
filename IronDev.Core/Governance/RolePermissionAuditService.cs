using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public sealed class RolePermissionAuditService
{
    public RolePermissionAuditDecision CreateAuditRecordCandidate(
        GovernanceRoleCatalog? roleCatalog,
        ForbiddenActionCatalog? forbiddenActionCatalog,
        RolePermissionAuditRequest? request)
    {
        var requestValidation = RolePermissionAuditValidator.ValidateRequest(request);
        if (!requestValidation.IsValid)
        {
            return Decision(
                request,
                ClassifyValidationIssues(requestValidation.Issues),
                requestValidation.Issues);
        }

        if (string.IsNullOrWhiteSpace(request!.RoleCatalogEvidenceRef))
        {
            return Decision(
                request,
                RolePermissionAuditClassification.BlockedByMissingRoleCatalogEvidence,
                ["Role catalog evidence reference is required."]);
        }

        if (string.IsNullOrWhiteSpace(request.ForbiddenActionCatalogEvidenceRef))
        {
            return Decision(
                request,
                RolePermissionAuditClassification.BlockedByMissingForbiddenActionCatalogEvidence,
                ["Forbidden action catalog evidence reference is required."]);
        }

        if (string.IsNullOrWhiteSpace(request.SourceEvidenceRef))
        {
            return Decision(
                request,
                RolePermissionAuditClassification.BlockedByMissingSourceEvidence,
                ["Source audit evidence reference is required."]);
        }

        if (!RoleCatalogValidator.ValidateCatalog(roleCatalog).IsValid)
        {
            return Decision(
                request,
                RolePermissionAuditClassification.BlockedByMissingRoleCatalogEvidence,
                ["F01 role catalog evidence is invalid."]);
        }

        if (!ForbiddenActionCatalogValidator.ValidateCatalog(roleCatalog, forbiddenActionCatalog).IsValid)
        {
            return Decision(
                request,
                RolePermissionAuditClassification.BlockedByMissingForbiddenActionCatalogEvidence,
                ["F13 forbidden action catalog evidence is invalid."]);
        }

        if (!RolePermissionAuditValidator.IsKnownEvent(request.RequestedEventKind))
        {
            return Decision(
                request,
                RolePermissionAuditClassification.BlockedByUnknownEvent,
                ["Requested audit event kind is unknown."]);
        }

        if (!RolePermissionAuditValidator.IsKnownSubject(request.RequestedSubjectKind))
        {
            return Decision(
                request,
                RolePermissionAuditClassification.BlockedByUnknownSubject,
                ["Requested audit subject kind is unknown."]);
        }

        if (!RolePermissionAuditValidator.IsKnownOutcome(request.RequestedOutcomeKind))
        {
            return Decision(
                request,
                RolePermissionAuditClassification.BlockedByUnknownOutcome,
                ["Requested audit outcome kind is unknown."]);
        }

        if (!RolePermissionAuditValidator.IsKnownAuthoritySource(request.RequestedAuthoritySourceKind))
        {
            return Decision(
                request,
                RolePermissionAuditClassification.BlockedByUnknownAuthoritySource,
                ["Requested audit authority source kind is unknown."]);
        }

        if (!KnownRole(roleCatalog!, request.RequestedRoleId))
        {
            return Decision(
                request,
                RolePermissionAuditClassification.BlockedByUnknownSubject,
                ["Requested role id is not present in the F01 role catalog."]);
        }

        if (!string.IsNullOrWhiteSpace(request.RequestedTargetRoleId) &&
            !KnownRole(roleCatalog!, request.RequestedTargetRoleId))
        {
            return Decision(
                request,
                RolePermissionAuditClassification.BlockedByUnknownSubject,
                ["Requested target role id is not present in the F01 role catalog."]);
        }

        var mappedAction = MapEventToForbiddenAction(request.RequestedEventKind);
        if (mappedAction != RoleForbiddenActionKind.Unknown)
        {
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

            if (f13Decision.Classification == ForbiddenActionLookupClassification.Forbidden)
            {
                return Decision(
                    request,
                    RolePermissionAuditClassification.BlockedByForbiddenActionCatalog,
                    ["F13 forbids role-evidence-derived authority for this audit event category."]);
            }
        }

        return Decision(
            request,
            CandidateClassification(request),
            ["Role/permission audit record candidate is evidence only and requires separate authority."]);
    }

    public static RoleForbiddenActionKind MapEventToForbiddenAction(RolePermissionAuditEventKind eventKind) =>
        eventKind switch
        {
            RolePermissionAuditEventKind.RoleAssignmentRequested or
            RolePermissionAuditEventKind.RoleAssignmentProposed or
            RolePermissionAuditEventKind.RoleAssignmentAttemptBlocked or
            RolePermissionAuditEventKind.RoleAssignmentRejected => RoleForbiddenActionKind.RoleAssignment,
            RolePermissionAuditEventKind.RoleGrantRequested or
            RolePermissionAuditEventKind.RoleGrantProposed or
            RolePermissionAuditEventKind.RoleGrantAttemptBlocked or
            RolePermissionAuditEventKind.RoleGrantRejected => RoleForbiddenActionKind.RoleGrant,
            RolePermissionAuditEventKind.RoleRevokeRequested or
            RolePermissionAuditEventKind.RoleRevokeProposed or
            RolePermissionAuditEventKind.RoleRevokeAttemptBlocked or
            RolePermissionAuditEventKind.RoleRevokeRejected => RoleForbiddenActionKind.RoleRevoke,
            RolePermissionAuditEventKind.PermissionGrantRequested or
            RolePermissionAuditEventKind.PermissionGrantProposed or
            RolePermissionAuditEventKind.PermissionGrantAttemptBlocked or
            RolePermissionAuditEventKind.PermissionGrantRejected or
            RolePermissionAuditEventKind.PermissionRevokeRequested or
            RolePermissionAuditEventKind.PermissionRevokeProposed or
            RolePermissionAuditEventKind.PermissionRevokeAttemptBlocked or
            RolePermissionAuditEventKind.PermissionRevokeRejected => RoleForbiddenActionKind.PermissionManagement,
            RolePermissionAuditEventKind.AccessGrantRequested or
            RolePermissionAuditEventKind.AccessGrantProposed or
            RolePermissionAuditEventKind.AccessGrantAttemptBlocked or
            RolePermissionAuditEventKind.AccessGrantRejected => RoleForbiddenActionKind.AccessGrant,
            RolePermissionAuditEventKind.VisibilityGrantRequested or
            RolePermissionAuditEventKind.VisibilityGrantProposed or
            RolePermissionAuditEventKind.VisibilityGrantAttemptBlocked or
            RolePermissionAuditEventKind.VisibilityGrantRejected => RoleForbiddenActionKind.VisibilityGrant,
            RolePermissionAuditEventKind.ExternalAccessRequested or
            RolePermissionAuditEventKind.ExternalAccessAttemptBlocked or
            RolePermissionAuditEventKind.ExternalAccessRejected => RoleForbiddenActionKind.ExternalAccessGrant,
            RolePermissionAuditEventKind.TenantBoundaryOverrideRequested or
            RolePermissionAuditEventKind.TenantBoundaryOverrideAttemptBlocked or
            RolePermissionAuditEventKind.TenantBoundaryOverrideRejected => RoleForbiddenActionKind.CrossTenantVisibility,
            RolePermissionAuditEventKind.PlatformPermissionRequested or
            RolePermissionAuditEventKind.PlatformPermissionAttemptBlocked or
            RolePermissionAuditEventKind.PlatformPermissionRejected => RoleForbiddenActionKind.PlatformVisibility,
            _ => RoleForbiddenActionKind.Unknown
        };

    private static RolePermissionAuditClassification CandidateClassification(
        RolePermissionAuditRequest request)
    {
        if (request.RequestedEventKind == RolePermissionAuditEventKind.AuditOnlyObservation ||
            request.RequestedOutcomeKind == RolePermissionAuditOutcomeKind.AuditOnly)
        {
            return RolePermissionAuditClassification.AuditOnlyObservationCandidate;
        }

        if (request.RequestedOutcomeKind == RolePermissionAuditOutcomeKind.Blocked ||
            request.RequestedEventKind.ToString().Contains("AttemptBlocked", StringComparison.Ordinal))
        {
            return RolePermissionAuditClassification.BlockedAuditRecordCandidate;
        }

        if (request.RequestedOutcomeKind == RolePermissionAuditOutcomeKind.Rejected ||
            request.RequestedEventKind.ToString().EndsWith("Rejected", StringComparison.Ordinal))
        {
            return RolePermissionAuditClassification.RejectedAuditRecordCandidate;
        }

        return RolePermissionAuditClassification.AuditRecordCandidate;
    }

    private static RolePermissionAuditClassification ClassifyValidationIssues(
        IReadOnlyList<string> issues)
    {
        if (issues.Any(static issue => issue.Contains("AppliedChange", StringComparison.Ordinal)))
        {
            return RolePermissionAuditClassification.BlockedByPerformedChangeLanguage;
        }

        if (issues.Any(static issue => issue.Contains("AuthorityGrant", StringComparison.Ordinal)))
        {
            return RolePermissionAuditClassification.BlockedByAuthorityGrantLanguage;
        }

        if (issues.Any(static issue => issue.Contains("RawMaterial", StringComparison.Ordinal)))
        {
            return RolePermissionAuditClassification.BlockedByRawMaterial;
        }

        if (issues.Any(static issue => issue.Contains("SecretMaterial", StringComparison.Ordinal)))
        {
            return RolePermissionAuditClassification.BlockedBySecretMaterial;
        }

        if (issues.Any(static issue => issue.Contains("CredentialMaterial", StringComparison.Ordinal)))
        {
            return RolePermissionAuditClassification.BlockedByCredentialMaterial;
        }

        return issues.Any(static issue => issue.Contains("PrivateReasoning", StringComparison.Ordinal))
            ? RolePermissionAuditClassification.BlockedByPrivateReasoningMaterial
            : RolePermissionAuditClassification.Invalid;
    }

    private static bool KnownRole(GovernanceRoleCatalog roleCatalog, string roleId) =>
        roleCatalog.Entries.Any(entry => string.Equals(entry.RoleId, roleId, StringComparison.OrdinalIgnoreCase));

    private static RolePermissionAuditDecision Decision(
        RolePermissionAuditRequest? request,
        RolePermissionAuditClassification classification,
        IReadOnlyList<string> reasons)
    {
        var evidenceRefs = new[]
        {
            Safe(request?.RoleCatalogEvidenceRef),
            Safe(request?.ForbiddenActionCatalogEvidenceRef),
            Safe(request?.MissingEvidenceVisibilityEvidenceRef),
            Safe(request?.SourceEvidenceRef),
            Safe(request?.OptionalPolicyEvidenceRef),
            Safe(request?.OptionalApprovalEvidenceRef),
            Safe(request?.OptionalTenantBoundaryEvidenceRef),
            Safe(request?.OptionalRedactionEvidenceRef)
        }
        .Where(static item => !string.IsNullOrWhiteSpace(item))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
        .ToArray();

        var fingerprint = Fingerprint(
            request?.CorrelationId,
            request?.RequestedEventKind.ToString(),
            request?.RequestedSubjectKind.ToString(),
            request?.RequestedOutcomeKind.ToString(),
            request?.RequestedAuthoritySourceKind.ToString(),
            request?.RequestedRoleId,
            request?.RequestedTargetRoleId,
            request?.RequestedPermissionKey,
            classification.ToString(),
            request?.PreviousAuditRecordFingerprint);

        var record = new RolePermissionAuditRecord
        {
            AuditRecordId = string.IsNullOrWhiteSpace(fingerprint) ? string.Empty : $"role-permission-audit:{fingerprint[..16]}",
            CorrelationId = Safe(request?.CorrelationId),
            EventKind = request?.RequestedEventKind ?? RolePermissionAuditEventKind.Unknown,
            SubjectKind = request?.RequestedSubjectKind ?? RolePermissionAuditSubjectKind.Unknown,
            OutcomeKind = request?.RequestedOutcomeKind ?? RolePermissionAuditOutcomeKind.Unknown,
            AuthoritySourceKind = request?.RequestedAuthoritySourceKind ?? RolePermissionAuditAuthoritySourceKind.Unknown,
            RoleId = Safe(request?.RequestedRoleId),
            TargetRoleId = Safe(request?.RequestedTargetRoleId),
            PermissionKey = Safe(request?.RequestedPermissionKey),
            ActorRef = Safe(request?.RequestedActorRef),
            TenantRef = Safe(request?.RequestedTenantRef),
            ProjectRef = Safe(request?.RequestedProjectRef),
            OperationRef = Safe(request?.RequestedOperationRef),
            EvidenceRefs = evidenceRefs,
            PreviousAuditRecordFingerprint = Safe(request?.PreviousAuditRecordFingerprint),
            RecordFingerprint = fingerprint,
            BoundaryStatement = "Role/permission audit records are not role/permission authority.",
            IsAuditOnly = true,
            IsImmutableRecord = true,
            IsAppendOnlyContract = true,
            GrantsRoleAssignmentAuthority = false,
            GrantsPermissionAuthority = false,
            GrantsAccess = false,
            GrantsVisibilityAuthority = false,
            GrantsExternalAccess = false,
            GrantsTenantBoundaryOverride = false,
            GrantsPlatformAuthority = false,
            AcceptsApproval = false,
            SatisfiesPolicy = false,
            RefreshesValidation = false,
            ProvesSourceSafety = false,
            CreatesEvidence = false,
            SatisfiesEvidence = false,
            OverridesMissingEvidence = false,
            WaivesEvidenceRequirement = false,
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

        return new RolePermissionAuditDecision
        {
            Classification = classification,
            Record = record,
            Reasons = reasons.Select(Safe).ToArray(),
            EvidenceRefs = evidenceRefs,
            RecordFingerprint = fingerprint,
            IsRecordedAuthority = false,
            IsAppliedChange = false,
            IsAuthorizationDecision = false,
            IsPermissionDecision = false,
            RequiresSeparateAuthority = true
        };
    }

    private static string Safe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return RolePermissionAuditValidator.ContainsUnsafeAuditText(value)
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
