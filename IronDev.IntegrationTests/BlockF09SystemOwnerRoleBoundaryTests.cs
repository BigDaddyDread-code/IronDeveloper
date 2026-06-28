using System.Text.RegularExpressions;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockF09SystemOwnerRoleBoundaryTests
{
    [TestMethod]
    public void SystemAccountabilityOwnerRoleExistsExactlyOnceWithStableId()
    {
        var entries = RoleCatalog().Entries
            .Where(static entry => entry.RoleKind == GovernanceRoleKind.SystemAccountabilityOwner)
            .ToArray();

        Assert.AreEqual(1, entries.Length);
        Assert.AreEqual("role:f01:system-accountability-owner", entries[0].RoleId);
        Assert.AreEqual("System Accountability Owner", entries[0].DisplayName);
        Assert.AreEqual(GovernanceRoleScopeKind.GlobalCatalog, entries[0].ScopeKind);
    }

    [TestMethod]
    public void SystemAccountabilityOwnerRoleIsAccountabilityOnlyCatalogMetadata()
    {
        var entry = SystemOwner();

        StringAssert.Contains(entry.Description, "accountability responsibility marker");
        StringAssert.Contains(entry.ResponsibilitySummary, "does not grant controls");
        StringAssert.Contains(entry.BoundaryStatement, "does not grant authority");
        CollectionAssert.Contains(entry.Surfaces.ToList(), GovernanceRoleSurface.StatusReadModel);
        CollectionAssert.Contains(entry.Surfaces.ToList(), GovernanceRoleSurface.Audit);
        CollectionAssert.Contains(entry.Surfaces.ToList(), GovernanceRoleSurface.FrontendReadOnly);
    }

    [TestMethod]
    public void SystemAccountabilityOwnerRoleTextIsNotControlShaped()
    {
        var entry = SystemOwner();
        var text = string.Join(" ", entry.DisplayName, entry.Description, entry.ResponsibilitySummary, entry.BoundaryStatement);
        var forbidden = new[]
        {
            "root",
            "superuser",
            "platform admin",
            "global admin",
            "break glass",
            "access everything",
            "grant permissions",
            "assign roles",
            "override policy",
            "override approval",
            "override validation",
            "mutate source",
            "continue workflow",
            "release authority",
            "deploy authority",
            "view secrets",
            "view raw payloads",
            "view private reasoning"
        };

        foreach (var marker in forbidden)
        {
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    [DataTestMethod]
    [DataRow(GovernanceRoleKind.TenantAdministrator)]
    [DataRow(GovernanceRoleKind.AutomationAgent)]
    [DataRow(GovernanceRoleKind.ExecutorOperatorCandidate)]
    public void SystemAccountabilityOwnerRoleIsNotConfusedWithOperationalRoles(GovernanceRoleKind otherKind)
    {
        var systemOwner = SystemOwner();
        var other = Role(otherKind);

        Assert.AreNotEqual(other.RoleId, systemOwner.RoleId);
        Assert.AreNotEqual(other.RoleKind, systemOwner.RoleKind);
        Assert.AreNotEqual(other.DisplayName, systemOwner.DisplayName);
    }

    [DataTestMethod]
    [DataRow(RoleForbiddenActionKind.AccessGrant)]
    [DataRow(RoleForbiddenActionKind.RoleAssignment)]
    [DataRow(RoleForbiddenActionKind.RoleGrant)]
    [DataRow(RoleForbiddenActionKind.RoleRevoke)]
    [DataRow(RoleForbiddenActionKind.PermissionManagement)]
    [DataRow(RoleForbiddenActionKind.VisibilityGrant)]
    [DataRow(RoleForbiddenActionKind.PlatformVisibility)]
    [DataRow(RoleForbiddenActionKind.CrossTenantVisibility)]
    [DataRow(RoleForbiddenActionKind.Impersonation)]
    [DataRow(RoleForbiddenActionKind.ApprovalAcceptance)]
    [DataRow(RoleForbiddenActionKind.PolicySatisfaction)]
    [DataRow(RoleForbiddenActionKind.ValidationRefresh)]
    [DataRow(RoleForbiddenActionKind.SourceSafetyProof)]
    [DataRow(RoleForbiddenActionKind.DiagnosticExecution)]
    [DataRow(RoleForbiddenActionKind.RetryExecution)]
    [DataRow(RoleForbiddenActionKind.RollbackExecution)]
    [DataRow(RoleForbiddenActionKind.RecoveryExecution)]
    [DataRow(RoleForbiddenActionKind.SourceMutation)]
    [DataRow(RoleForbiddenActionKind.PatchApply)]
    [DataRow(RoleForbiddenActionKind.CommitCreation)]
    [DataRow(RoleForbiddenActionKind.PushExecution)]
    [DataRow(RoleForbiddenActionKind.PullRequestCreation)]
    [DataRow(RoleForbiddenActionKind.WorkflowContinuation)]
    [DataRow(RoleForbiddenActionKind.Merge)]
    [DataRow(RoleForbiddenActionKind.Release)]
    [DataRow(RoleForbiddenActionKind.Deployment)]
    [DataRow(RoleForbiddenActionKind.RedactionBypass)]
    [DataRow(RoleForbiddenActionKind.SecretDisclosure)]
    [DataRow(RoleForbiddenActionKind.CredentialDisclosure)]
    [DataRow(RoleForbiddenActionKind.RawPayloadDisclosure)]
    [DataRow(RoleForbiddenActionKind.PrivateReasoningDisclosure)]
    public void F13SystemAccountabilityOwnerForbidsRoleEvidenceDerivedAuthority(RoleForbiddenActionKind actionKind)
    {
        var decision = F13Lookup(actionKind);

        Assert.AreEqual(ForbiddenActionLookupClassification.Forbidden, decision.Classification, actionKind.ToString());
        AssertF13DecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void F13OmissionForSystemAccountabilityOwnerDoesNotBecomeAllow()
    {
        var decision = F13Lookup(RoleForbiddenActionKind.RouteGuardCreation);

        Assert.AreEqual(ForbiddenActionLookupClassification.NoCatalogGrantSeparateAuthorityRequired, decision.Classification);
        AssertF13DecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void EverySystemAccountabilityOwnerF13LookupHasNoAllowOrPermissionFlags()
    {
        var actions = new[]
        {
            RoleForbiddenActionKind.AccessGrant,
            RoleForbiddenActionKind.PlatformVisibility,
            RoleForbiddenActionKind.SourceMutation,
            RoleForbiddenActionKind.RouteGuardCreation
        };

        foreach (var action in actions)
        {
            AssertF13DecisionAuthorityFlagsFalse(F13Lookup(action));
        }
    }

    [TestMethod]
    public void SystemAccountabilityOwnerMayReceiveAtMostBoundedRedactedMissingEvidenceCandidate()
    {
        var decision = F14Classify(F14Request(MissingEvidenceKind.RouteGuardEvidence) with
        {
            RequestedMaterialKind = MissingEvidenceMaterialKind.RedactedSummary
        });

        Assert.AreEqual(MissingEvidenceVisibilityClassification.RedactedSummaryCandidate, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.RedactedDetails, decision.EffectiveCandidateVisibility);
        AssertF14DecisionAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(MissingEvidenceKind.RoleAssignmentEvidence)]
    [DataRow(MissingEvidenceKind.AccessDecisionEvidence)]
    [DataRow(MissingEvidenceKind.ApprovalEvidence)]
    [DataRow(MissingEvidenceKind.PolicySatisfactionEvidence)]
    [DataRow(MissingEvidenceKind.ValidationFreshnessEvidence)]
    [DataRow(MissingEvidenceKind.SourceSafetyEvidence)]
    [DataRow(MissingEvidenceKind.DiagnosticExecutionAuthority)]
    [DataRow(MissingEvidenceKind.RetryAuthority)]
    [DataRow(MissingEvidenceKind.RollbackAuthority)]
    [DataRow(MissingEvidenceKind.RecoveryAuthority)]
    [DataRow(MissingEvidenceKind.MutationAuthority)]
    [DataRow(MissingEvidenceKind.WorkflowContinuationEvidence)]
    [DataRow(MissingEvidenceKind.MergeAuthority)]
    [DataRow(MissingEvidenceKind.ReleaseAuthority)]
    [DataRow(MissingEvidenceKind.DeploymentAuthority)]
    public void SystemAccountabilityOwnerMissingEvidenceVisibilityDoesNotGrantAuthority(MissingEvidenceKind kind)
    {
        var decision = F14Classify(F14Request(kind));

        Assert.AreEqual(MissingEvidenceVisibilityClassification.BlockedByForbiddenActionCatalog, decision.Classification, kind.ToString());
        AssertF14DecisionAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(MissingEvidenceMaterialKind.RawPayload, MissingEvidenceVisibilityClassification.BlockedByRawMaterial)]
    [DataRow(MissingEvidenceMaterialKind.RawProviderResponse, MissingEvidenceVisibilityClassification.BlockedByRawMaterial)]
    [DataRow(MissingEvidenceMaterialKind.RawSource, MissingEvidenceVisibilityClassification.BlockedByRawMaterial)]
    [DataRow(MissingEvidenceMaterialKind.RawLog, MissingEvidenceVisibilityClassification.BlockedByRawMaterial)]
    [DataRow(MissingEvidenceMaterialKind.SecretMaterial, MissingEvidenceVisibilityClassification.BlockedBySecretMaterial)]
    [DataRow(MissingEvidenceMaterialKind.CredentialMaterial, MissingEvidenceVisibilityClassification.BlockedByCredentialMaterial)]
    [DataRow(MissingEvidenceMaterialKind.PrivateReasoning, MissingEvidenceVisibilityClassification.BlockedByPrivateReasoningMaterial)]
    public void SystemAccountabilityOwnerRawSecretCredentialAndPrivateMissingEvidenceMaterialsRemainBlocked(
        MissingEvidenceMaterialKind materialKind,
        MissingEvidenceVisibilityClassification expected)
    {
        var decision = F14Classify(F14Request(MissingEvidenceKind.RouteGuardEvidence) with
        {
            RequestedMaterialKind = materialKind
        });

        Assert.AreEqual(expected, decision.Classification);
        AssertF14DecisionAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(MissingEvidenceVisibilityIntent.SatisfyMissingEvidence)]
    [DataRow(MissingEvidenceVisibilityIntent.CreateMissingEvidence)]
    [DataRow(MissingEvidenceVisibilityIntent.OverrideMissingEvidence)]
    [DataRow(MissingEvidenceVisibilityIntent.WaiveEvidenceRequirement)]
    [DataRow(MissingEvidenceVisibilityIntent.AcceptApproval)]
    [DataRow(MissingEvidenceVisibilityIntent.SatisfyPolicy)]
    [DataRow(MissingEvidenceVisibilityIntent.RefreshValidation)]
    [DataRow(MissingEvidenceVisibilityIntent.ProveSourceSafety)]
    [DataRow(MissingEvidenceVisibilityIntent.RunDiagnostic)]
    [DataRow(MissingEvidenceVisibilityIntent.Retry)]
    [DataRow(MissingEvidenceVisibilityIntent.Rollback)]
    [DataRow(MissingEvidenceVisibilityIntent.Recover)]
    [DataRow(MissingEvidenceVisibilityIntent.MutateSource)]
    [DataRow(MissingEvidenceVisibilityIntent.ApplyPatch)]
    [DataRow(MissingEvidenceVisibilityIntent.Commit)]
    [DataRow(MissingEvidenceVisibilityIntent.Push)]
    [DataRow(MissingEvidenceVisibilityIntent.CreatePullRequest)]
    [DataRow(MissingEvidenceVisibilityIntent.ReadyForReview)]
    [DataRow(MissingEvidenceVisibilityIntent.ContinueWorkflow)]
    [DataRow(MissingEvidenceVisibilityIntent.Merge)]
    [DataRow(MissingEvidenceVisibilityIntent.Release)]
    [DataRow(MissingEvidenceVisibilityIntent.Deploy)]
    [DataRow(MissingEvidenceVisibilityIntent.BypassRedaction)]
    [DataRow(MissingEvidenceVisibilityIntent.DiscloseSecret)]
    [DataRow(MissingEvidenceVisibilityIntent.DiscloseCredential)]
    [DataRow(MissingEvidenceVisibilityIntent.DiscloseRawPayload)]
    [DataRow(MissingEvidenceVisibilityIntent.DisclosePrivateReasoning)]
    public void SystemAccountabilityOwnerActionOrDisclosureMissingEvidenceIntentsAreBlocked(
        MissingEvidenceVisibilityIntent intent)
    {
        var decision = F14Classify(F14Request(MissingEvidenceKind.RouteGuardEvidence) with
        {
            RequestedIntent = intent
        });

        Assert.AreNotEqual(MissingEvidenceVisibilityClassification.RedactedSummaryCandidate, decision.Classification);
        AssertF14DecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void SystemAccountabilityOwnerAuditOnlyObservationCreatesAuditOnlyCandidateOnly()
    {
        var decision = F15Create(F15Request() with
        {
            RequestedEventKind = RolePermissionAuditEventKind.AuditOnlyObservation,
            RequestedSubjectKind = RolePermissionAuditSubjectKind.Role,
            RequestedOutcomeKind = RolePermissionAuditOutcomeKind.AuditOnly
        });

        Assert.AreEqual(RolePermissionAuditClassification.AuditOnlyObservationCandidate, decision.Classification);
        Assert.IsTrue(decision.Record.IsAuditOnly);
        Assert.IsTrue(decision.Record.IsImmutableRecord);
        Assert.IsTrue(decision.Record.IsAppendOnlyContract);
        AssertF15DecisionAuthorityFlagsFalse(decision);
        AssertF15RecordAuthorityFlagsFalse(decision.Record);
    }

    [DataTestMethod]
    [DataRow(RolePermissionAuditEventKind.RoleAssignmentRequested)]
    [DataRow(RolePermissionAuditEventKind.PermissionGrantRequested)]
    [DataRow(RolePermissionAuditEventKind.AccessGrantRequested)]
    [DataRow(RolePermissionAuditEventKind.VisibilityGrantRequested)]
    [DataRow(RolePermissionAuditEventKind.TenantBoundaryOverrideRequested)]
    [DataRow(RolePermissionAuditEventKind.PlatformPermissionRequested)]
    public void SystemAccountabilityOwnerAuditRecordsDoNotPerformTheChange(RolePermissionAuditEventKind eventKind)
    {
        var decision = F15Create(F15Request() with
        {
            RequestedEventKind = eventKind,
            RequestedSubjectKind = SubjectFor(eventKind),
            RequestedOutcomeKind = RolePermissionAuditOutcomeKind.Requested
        });

        Assert.AreEqual(RolePermissionAuditClassification.BlockedByForbiddenActionCatalog, decision.Classification, eventKind.ToString());
        AssertF15DecisionAuthorityFlagsFalse(decision);
        AssertF15RecordAuthorityFlagsFalse(decision.Record);
    }

    [TestMethod]
    public void SystemAccountabilityOwnerF15AuditRecordKeepsAllAuthorityActionAndDisclosureFlagsFalse()
    {
        var decisions = new[]
        {
            F15Create(F15Request() with { RequestedEventKind = RolePermissionAuditEventKind.AuditOnlyObservation, RequestedOutcomeKind = RolePermissionAuditOutcomeKind.AuditOnly }),
            F15Create(F15Request() with { RequestedEventKind = RolePermissionAuditEventKind.RoleAssignmentRequested }),
            F15Create(F15Request() with { RequestedEventKind = RolePermissionAuditEventKind.PermissionGrantRequested, RequestedSubjectKind = RolePermissionAuditSubjectKind.Permission })
        };

        foreach (var decision in decisions)
        {
            AssertF15DecisionAuthorityFlagsFalse(decision);
            AssertF15RecordAuthorityFlagsFalse(decision.Record);
        }
    }

    [TestMethod]
    public void StaticScanF09AddsNoProductionRuntimeAuthoritySurface()
    {
        var source = string.Join(
            Environment.NewLine,
            F09Files().Select(File.ReadAllText).Select(StripStringLiterals));

        foreach (var forbiddenToken in new[]
        {
            "ApiController",
            "ControllerBase",
            "MapGet",
            "MapPost",
            "HttpGet",
            "HttpPost",
            "OpenApi",
            "DbContext",
            "SqlConnection",
            "IRepository",
            "HttpClient",
            "ProcessStartInfo",
            "ClaimsPrincipal",
            "UserManager",
            "RoleManager",
            "IAuthorizationHandler",
            "AuthorizationHandler",
            "PermissionResolver",
            "AccessControl",
            "RoleAssignmentService",
            "PermissionService",
            "AccessGrantService",
            "EvidenceWriter",
            "EvidenceStore",
            "AuditStore",
            "AppendAsync",
            "SaveChanges",
            "WorkflowRunner",
            "SourceApplyExecutor",
            "CommitGateway",
            "PushGateway",
            "PullRequestGateway",
            "MergeExecutor",
            "ReleaseExecutor",
            "DeploymentExecutor",
            "RetryExecutor",
            "RollbackExecutor",
            "RecoveryExecutor",
            "AllowAnonymous",
            "AuthorizeAttribute"
        })
        {
            Assert.IsFalse(
                source.Contains(forbiddenToken, StringComparison.Ordinal),
                $"Unexpected runtime/authority surface token found: {forbiddenToken}");
        }
    }

    [TestMethod]
    public void ReceiptExistsAndStatesSystemOwnerEvidenceIsNotSystemAuthority()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "F09_SYSTEM_OWNER_ROLE_BOUNDARY_TESTS.md"));

        StringAssert.Contains(receipt, "F09a created the SystemAccountabilityOwner catalog role.");
        StringAssert.Contains(receipt, "F09 proper adds boundary tests only.");
        StringAssert.Contains(receipt, "System owner evidence is not system authority.");
        StringAssert.Contains(receipt, "Owning accountability is not owning the controls.");
        StringAssert.Contains(receipt, "does not implement role assignment");
        StringAssert.Contains(receipt, "does not implement permission grant");
        StringAssert.Contains(receipt, "does not implement API exposure");
    }

    private static ForbiddenActionLookupDecision F13Lookup(RoleForbiddenActionKind actionKind) =>
        new ForbiddenActionCatalogService().Lookup(
            RoleCatalog(),
            ForbiddenCatalog(),
            new ForbiddenActionLookupRequest
            {
                CorrelationId = "correlation-f09",
                RequestedRoleId = SystemOwner().RoleId,
                RequestedActionKind = actionKind,
                AuthoritySourceKind = ForbiddenActionAuthoritySourceKind.RoleEvidence,
                RoleCatalogEvidenceRef = "role-catalog:f09",
                ForbiddenActionCatalogEvidenceRef = "forbidden-action-catalog:f09",
                OptionalPolicyEvidenceRef = "policy-evidence:f09",
                OptionalApprovalEvidenceRef = "approval-evidence:f09",
                OptionalExecutionAuthorityRef = "execution-authority:f09",
                OptionalMutationAuthorityRef = "mutation-authority:f09",
                OptionalWorkflowAuthorityRef = "workflow-authority:f09",
                OptionalReleaseAuthorityRef = "release-authority:f09",
                OptionalRedactionDecisionRef = "redaction-decision:f09"
            });

    private static MissingEvidenceVisibilityDecision F14Classify(MissingEvidenceVisibilityRequest request) =>
        new MissingEvidenceVisibilityService().Classify(
            RoleCatalog(),
            VisibilityMatrix(),
            ForbiddenCatalog(),
            request);

    private static MissingEvidenceVisibilityRequest F14Request(MissingEvidenceKind kind) =>
        new()
        {
            CorrelationId = "correlation-f09-f14",
            RequestedRoleId = SystemOwner().RoleId,
            RequestedMissingEvidenceKind = kind,
            RequestedMaterialKind = MissingEvidenceMaterialKind.RedactedSummary,
            RequestedIntent = MissingEvidenceVisibilityIntent.InspectMissingEvidence,
            RoleCatalogEvidenceRef = "role-catalog:f09",
            VisibilityMatrixEvidenceRef = "visibility-matrix:f09",
            ForbiddenActionCatalogEvidenceRef = "forbidden-action-catalog:f09",
            SourceMissingEvidenceRef = "missing-evidence:f09",
            OptionalTenantBoundaryEvidenceRef = "tenant-boundary-evidence:f09",
            OptionalRedactionEvidenceRef = "redaction-evidence:f09",
            OptionalPolicyEvidenceRef = "policy-evidence:f09",
            OptionalApprovalEvidenceRef = "approval-evidence:f09"
        };

    private static RolePermissionAuditDecision F15Create(RolePermissionAuditRequest request) =>
        new RolePermissionAuditService().CreateAuditRecordCandidate(RoleCatalog(), ForbiddenCatalog(), request);

    private static RolePermissionAuditRequest F15Request() =>
        new()
        {
            CorrelationId = "correlation-f09-f15",
            RequestedEventKind = RolePermissionAuditEventKind.AuditOnlyObservation,
            RequestedSubjectKind = RolePermissionAuditSubjectKind.Role,
            RequestedOutcomeKind = RolePermissionAuditOutcomeKind.AuditOnly,
            RequestedAuthoritySourceKind = RolePermissionAuditAuthoritySourceKind.RoleEvidence,
            RequestedRoleId = SystemOwner().RoleId,
            RequestedTargetRoleId = Role(GovernanceRoleKind.Reviewer).RoleId,
            RequestedPermissionKey = "permission:f09:system-owner-boundary",
            RequestedActorRef = "actor-ref:f09",
            RequestedTenantRef = "tenant-ref:f09",
            RequestedProjectRef = "project-ref:f09",
            RequestedOperationRef = "operation-ref:f09",
            RoleCatalogEvidenceRef = "role-catalog:f09",
            ForbiddenActionCatalogEvidenceRef = "forbidden-action-catalog:f09",
            MissingEvidenceVisibilityEvidenceRef = "missing-evidence-visibility:f09",
            SourceEvidenceRef = "role-permission-audit-source:f09",
            OptionalPolicyEvidenceRef = "policy-evidence:f09",
            OptionalApprovalEvidenceRef = "approval-evidence:f09",
            OptionalTenantBoundaryEvidenceRef = "tenant-boundary-evidence:f09",
            OptionalRedactionEvidenceRef = "redaction-evidence:f09",
            PreviousAuditRecordFingerprint = "previous-audit-record:f09"
        };

    private static RolePermissionAuditSubjectKind SubjectFor(RolePermissionAuditEventKind eventKind) =>
        eventKind switch
        {
            RolePermissionAuditEventKind.PermissionGrantRequested => RolePermissionAuditSubjectKind.Permission,
            RolePermissionAuditEventKind.AccessGrantRequested => RolePermissionAuditSubjectKind.Access,
            RolePermissionAuditEventKind.VisibilityGrantRequested => RolePermissionAuditSubjectKind.Visibility,
            RolePermissionAuditEventKind.TenantBoundaryOverrideRequested => RolePermissionAuditSubjectKind.TenantBoundary,
            RolePermissionAuditEventKind.PlatformPermissionRequested => RolePermissionAuditSubjectKind.PlatformPermission,
            _ => RolePermissionAuditSubjectKind.Role
        };

    private static GovernanceRoleCatalog RoleCatalog() => new RoleCatalogService().BuildDefaultCatalog();

    private static RoleVisibilityMatrix VisibilityMatrix() =>
        new RoleVisibilityMatrixService().BuildDefaultMatrix(RoleCatalog());

    private static ForbiddenActionCatalog ForbiddenCatalog() =>
        new ForbiddenActionCatalogService().BuildDefaultCatalog(RoleCatalog());

    private static GovernanceRoleCatalogEntry SystemOwner() => Role(GovernanceRoleKind.SystemAccountabilityOwner);

    private static GovernanceRoleCatalogEntry Role(GovernanceRoleKind kind) =>
        RoleCatalog().Entries.Single(entry => entry.RoleKind == kind);

    private static void AssertF13DecisionAuthorityFlagsFalse(ForbiddenActionLookupDecision decision)
    {
        Assert.IsFalse(decision.IsAllowed);
        Assert.IsFalse(decision.GrantsAuthority);
        Assert.IsFalse(decision.GrantsPermission);
        Assert.IsFalse(decision.SatisfiesPolicy);
        Assert.IsFalse(decision.AllowsExecution);
        Assert.IsFalse(decision.AllowsMutation);
        Assert.IsFalse(decision.AllowsWorkflowContinuation);
        Assert.IsFalse(decision.AllowsMerge);
        Assert.IsFalse(decision.AllowsRelease);
        Assert.IsFalse(decision.AllowsDeployment);
        Assert.IsFalse(decision.BypassesRedaction);
        Assert.IsFalse(decision.DisclosesSecrets);
        Assert.IsFalse(decision.DisclosesCredentials);
        Assert.IsFalse(decision.DisclosesRawPayload);
        Assert.IsFalse(decision.DisclosesPrivateReasoning);
        Assert.IsTrue(decision.RequiresSeparateAuthority);
    }

    private static void AssertF14DecisionAuthorityFlagsFalse(MissingEvidenceVisibilityDecision decision)
    {
        Assert.IsFalse(decision.IsEvidenceSatisfied);
        Assert.IsFalse(decision.CreatesEvidence);
        Assert.IsFalse(decision.OverridesMissingEvidence);
        Assert.IsFalse(decision.WaivesEvidenceRequirement);
        Assert.IsFalse(decision.GrantsRoleAssignmentAuthority);
        Assert.IsFalse(decision.GrantsVisibilityAuthority);
        Assert.IsFalse(decision.GrantsAccess);
        Assert.IsFalse(decision.AcceptsApproval);
        Assert.IsFalse(decision.SatisfiesPolicy);
        Assert.IsFalse(decision.RefreshesValidation);
        Assert.IsFalse(decision.ProvesSourceSafety);
        Assert.IsFalse(decision.GrantsDiagnosticExecutionAuthority);
        Assert.IsFalse(decision.GrantsRetryAuthority);
        Assert.IsFalse(decision.GrantsRollbackAuthority);
        Assert.IsFalse(decision.GrantsRecoveryAuthority);
        Assert.IsFalse(decision.GrantsMutationAuthority);
        Assert.IsFalse(decision.GrantsWorkflowContinuation);
        Assert.IsFalse(decision.GrantsMergeAuthority);
        Assert.IsFalse(decision.GrantsReleaseAuthority);
        Assert.IsFalse(decision.GrantsDeploymentAuthority);
        Assert.IsFalse(decision.BypassesRedaction);
        Assert.IsFalse(decision.DisclosesSecrets);
        Assert.IsFalse(decision.DisclosesCredentials);
        Assert.IsFalse(decision.DisclosesRawPayload);
        Assert.IsFalse(decision.DisclosesPrivateReasoning);
        Assert.IsTrue(decision.RequiresSeparateAuthority);
    }

    private static void AssertF15DecisionAuthorityFlagsFalse(RolePermissionAuditDecision decision)
    {
        Assert.IsFalse(decision.IsRecordedAuthority);
        Assert.IsFalse(decision.IsAppliedChange);
        Assert.IsFalse(decision.IsAuthorizationDecision);
        Assert.IsFalse(decision.IsPermissionDecision);
        Assert.IsTrue(decision.RequiresSeparateAuthority);
    }

    private static void AssertF15RecordAuthorityFlagsFalse(RolePermissionAuditRecord record)
    {
        Assert.IsFalse(record.GrantsRoleAssignmentAuthority);
        Assert.IsFalse(record.GrantsPermissionAuthority);
        Assert.IsFalse(record.GrantsAccess);
        Assert.IsFalse(record.GrantsVisibilityAuthority);
        Assert.IsFalse(record.GrantsExternalAccess);
        Assert.IsFalse(record.GrantsTenantBoundaryOverride);
        Assert.IsFalse(record.GrantsPlatformAuthority);
        Assert.IsFalse(record.AcceptsApproval);
        Assert.IsFalse(record.SatisfiesPolicy);
        Assert.IsFalse(record.RefreshesValidation);
        Assert.IsFalse(record.ProvesSourceSafety);
        Assert.IsFalse(record.CreatesEvidence);
        Assert.IsFalse(record.SatisfiesEvidence);
        Assert.IsFalse(record.OverridesMissingEvidence);
        Assert.IsFalse(record.WaivesEvidenceRequirement);
        Assert.IsFalse(record.AllowsExecution);
        Assert.IsFalse(record.AllowsMutation);
        Assert.IsFalse(record.AllowsWorkflowContinuation);
        Assert.IsFalse(record.AllowsMerge);
        Assert.IsFalse(record.AllowsRelease);
        Assert.IsFalse(record.AllowsDeployment);
        Assert.IsFalse(record.BypassesRedaction);
        Assert.IsFalse(record.DisclosesSecrets);
        Assert.IsFalse(record.DisclosesCredentials);
        Assert.IsFalse(record.DisclosesRawPayload);
        Assert.IsFalse(record.DisclosesPrivateReasoning);
        Assert.IsTrue(record.RequiresSeparateAuthority);
    }

    private static IEnumerable<string> F09Files()
    {
        var root = RepoRoot();
        yield return Path.Combine(root, "IronDev.IntegrationTests", "BlockF09SystemOwnerRoleBoundaryTests.cs");
        yield return Path.Combine(root, "Docs", "receipts", "F09_SYSTEM_OWNER_ROLE_BOUNDARY_TESTS.md");
    }

    private static string StripStringLiterals(string source) =>
        Regex.Replace(source, "\"(?:\\\\.|[^\"\\\\])*\"", "\"\"", RegexOptions.CultureInvariant);

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Unable to locate repository root.");
    }
}
