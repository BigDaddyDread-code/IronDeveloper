using System.Text.RegularExpressions;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockF13ForbiddenActionCatalogByRoleTests
{
    private static readonly RoleForbiddenActionKind[] BaselineActions =
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

    [TestMethod]
    public void DefaultForbiddenActionCatalogValidates()
    {
        var validation = ForbiddenActionCatalogValidator.ValidateCatalog(RoleCatalog(), Catalog());

        Assert.IsTrue(validation.IsValid, string.Join("; ", validation.Issues));
        Assert.AreEqual(0, validation.UnsafeRefs.Count);
    }

    [TestMethod]
    public void DefaultCatalogContainsEntriesForEveryF01Role()
    {
        var roleIds = RoleCatalog().Entries.Select(static role => role.RoleId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var catalogRoleIds = Catalog().Entries.Select(static entry => entry.RoleId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        CollectionAssert.IsSubsetOf(roleIds.ToList(), catalogRoleIds.ToList());
    }

    [TestMethod]
    public void DefaultCatalogReferencesOnlyKnownF01Roles()
    {
        var roleIds = RoleCatalog().Entries.Select(static role => role.RoleId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in Catalog().Entries)
        {
            Assert.IsTrue(roleIds.Contains(entry.RoleId), entry.RoleId);
        }
    }

    [TestMethod]
    public void EveryCatalogEntryIsDenialOnly()
    {
        foreach (var entry in Catalog().Entries)
        {
            Assert.IsTrue(entry.AppliesWhenAuthoritySourceIsRoleEvidence, entry.RoleId);
            Assert.IsTrue(entry.IsForbidden, entry.RoleId);
            AssertEntryAuthorityFlagsFalse(entry);
        }
    }

    [DataTestMethod]
    [DataRow(RoleForbiddenActionKind.AccessGrant)]
    [DataRow(RoleForbiddenActionKind.VisibilityGrant)]
    [DataRow(RoleForbiddenActionKind.ApprovalAcceptance)]
    [DataRow(RoleForbiddenActionKind.PolicySatisfaction)]
    [DataRow(RoleForbiddenActionKind.ValidationRefresh)]
    [DataRow(RoleForbiddenActionKind.SourceSafetyProof)]
    [DataRow(RoleForbiddenActionKind.SourceMutation)]
    [DataRow(RoleForbiddenActionKind.WorkflowContinuation)]
    [DataRow(RoleForbiddenActionKind.Merge)]
    [DataRow(RoleForbiddenActionKind.Release)]
    [DataRow(RoleForbiddenActionKind.Deployment)]
    [DataRow(RoleForbiddenActionKind.RedactionBypass)]
    [DataRow(RoleForbiddenActionKind.SecretDisclosure)]
    [DataRow(RoleForbiddenActionKind.CredentialDisclosure)]
    [DataRow(RoleForbiddenActionKind.RawPayloadDisclosure)]
    [DataRow(RoleForbiddenActionKind.PrivateReasoningDisclosure)]
    public void EveryKnownRoleForbidsBaselineRoleEvidenceAuthority(RoleForbiddenActionKind actionKind)
    {
        foreach (var role in RoleCatalog().Entries)
        {
            AssertForbidden(role.RoleKind, actionKind);
        }
    }

    [TestMethod]
    public void EveryKnownRoleForbidsAllMinimumBaselineActions()
    {
        foreach (var role in RoleCatalog().Entries)
        {
            foreach (var action in BaselineActions)
            {
                AssertForbidden(role.RoleKind, action);
            }
        }
    }

    [DataTestMethod]
    [DataRow(RoleForbiddenActionKind.ExternalAccessGrant)]
    [DataRow(RoleForbiddenActionKind.ShareLinkCreation)]
    [DataRow(RoleForbiddenActionKind.RawExport)]
    [DataRow(RoleForbiddenActionKind.RawProviderResponseDisclosure)]
    [DataRow(RoleForbiddenActionKind.RawSourceDisclosure)]
    [DataRow(RoleForbiddenActionKind.RawLogDisclosure)]
    [DataRow(RoleForbiddenActionKind.CrossTenantVisibility)]
    [DataRow(RoleForbiddenActionKind.PlatformVisibility)]
    [DataRow(RoleForbiddenActionKind.RedactionBypass)]
    public void ExternalViewerForbidsExternalRawCrossTenantAndRedactionActions(RoleForbiddenActionKind actionKind) =>
        AssertForbidden(GovernanceRoleKind.ExternalViewer, actionKind);

    [DataTestMethod]
    [DataRow(RoleForbiddenActionKind.PlatformVisibility)]
    [DataRow(RoleForbiddenActionKind.CrossTenantVisibility)]
    [DataRow(RoleForbiddenActionKind.RoleAssignment)]
    [DataRow(RoleForbiddenActionKind.RoleGrant)]
    [DataRow(RoleForbiddenActionKind.RoleRevoke)]
    [DataRow(RoleForbiddenActionKind.PermissionManagement)]
    [DataRow(RoleForbiddenActionKind.Impersonation)]
    [DataRow(RoleForbiddenActionKind.AccessGrant)]
    [DataRow(RoleForbiddenActionKind.ApprovalAcceptance)]
    [DataRow(RoleForbiddenActionKind.PolicySatisfaction)]
    [DataRow(RoleForbiddenActionKind.SourceMutation)]
    [DataRow(RoleForbiddenActionKind.WorkflowContinuation)]
    [DataRow(RoleForbiddenActionKind.Merge)]
    [DataRow(RoleForbiddenActionKind.Release)]
    [DataRow(RoleForbiddenActionKind.Deployment)]
    [DataRow(RoleForbiddenActionKind.RedactionBypass)]
    [DataRow(RoleForbiddenActionKind.SecretDisclosure)]
    [DataRow(RoleForbiddenActionKind.CredentialDisclosure)]
    [DataRow(RoleForbiddenActionKind.RawPayloadDisclosure)]
    [DataRow(RoleForbiddenActionKind.PrivateReasoningDisclosure)]
    public void TenantAdministratorForbidsAdminShapedAuthority(RoleForbiddenActionKind actionKind) =>
        AssertForbidden(GovernanceRoleKind.TenantAdministrator, actionKind);

    [DataTestMethod]
    [DataRow(RoleForbiddenActionKind.AccessGrant)]
    [DataRow(RoleForbiddenActionKind.RoleAssignment)]
    [DataRow(RoleForbiddenActionKind.RoleGrant)]
    [DataRow(RoleForbiddenActionKind.RoleRevoke)]
    [DataRow(RoleForbiddenActionKind.PermissionManagement)]
    [DataRow(RoleForbiddenActionKind.PlatformVisibility)]
    [DataRow(RoleForbiddenActionKind.CrossTenantVisibility)]
    [DataRow(RoleForbiddenActionKind.Impersonation)]
    [DataRow(RoleForbiddenActionKind.WorkflowContinuation)]
    [DataRow(RoleForbiddenActionKind.Release)]
    [DataRow(RoleForbiddenActionKind.Deployment)]
    public void SystemAccountabilityOwnerForbidsControlDespiteAccountabilityWording(RoleForbiddenActionKind actionKind) =>
        AssertForbidden(GovernanceRoleKind.SystemAccountabilityOwner, actionKind);

    [DataTestMethod]
    [DataRow(RoleForbiddenActionKind.WorkflowContinuation)]
    [DataRow(RoleForbiddenActionKind.SourceMutation)]
    [DataRow(RoleForbiddenActionKind.PatchApply)]
    [DataRow(RoleForbiddenActionKind.CommitCreation)]
    [DataRow(RoleForbiddenActionKind.PushExecution)]
    [DataRow(RoleForbiddenActionKind.PullRequestCreation)]
    [DataRow(RoleForbiddenActionKind.ReadyForReview)]
    [DataRow(RoleForbiddenActionKind.Merge)]
    [DataRow(RoleForbiddenActionKind.Release)]
    [DataRow(RoleForbiddenActionKind.Deployment)]
    [DataRow(RoleForbiddenActionKind.RetryExecution)]
    [DataRow(RoleForbiddenActionKind.RollbackExecution)]
    [DataRow(RoleForbiddenActionKind.RecoveryExecution)]
    [DataRow(RoleForbiddenActionKind.PolicySatisfaction)]
    [DataRow(RoleForbiddenActionKind.ApprovalAcceptance)]
    public void AutomationAgentForbidsAutonomyFromRoleEvidence(RoleForbiddenActionKind actionKind) =>
        AssertForbidden(GovernanceRoleKind.AutomationAgent, actionKind);

    [DataTestMethod]
    [DataRow(RoleForbiddenActionKind.ApprovalAcceptance)]
    [DataRow(RoleForbiddenActionKind.PolicySatisfaction)]
    [DataRow(RoleForbiddenActionKind.SourceMutation)]
    [DataRow(RoleForbiddenActionKind.WorkflowContinuation)]
    [DataRow(RoleForbiddenActionKind.Merge)]
    [DataRow(RoleForbiddenActionKind.Release)]
    [DataRow(RoleForbiddenActionKind.Deployment)]
    public void ApproverCandidateForbidsApprovalAcceptanceFromCandidateRole(RoleForbiddenActionKind actionKind) =>
        AssertForbidden(GovernanceRoleKind.ApproverCandidate, actionKind);

    [DataTestMethod]
    [DataRow(GovernanceRoleKind.OperationsReviewer)]
    [DataRow(GovernanceRoleKind.ExecutorOperatorCandidate)]
    [DataRow(GovernanceRoleKind.RollbackReviewer)]
    [DataRow(GovernanceRoleKind.RecoveryReviewer)]
    public void SupportLikeRolesForbidDiagnosticRetryRollbackAndRecoveryExecution(GovernanceRoleKind roleKind)
    {
        foreach (var action in new[]
        {
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
        })
        {
            AssertForbidden(roleKind, action);
        }
    }

    [TestMethod]
    public void ExplicitForbiddenLookupReturnsForbidden()
    {
        var decision = Lookup(RoleId(GovernanceRoleKind.TenantAdministrator), RoleForbiddenActionKind.PermissionManagement);

        Assert.AreEqual(ForbiddenActionLookupClassification.Forbidden, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnlistedActionReturnsSeparateAuthorityRequiredNotAllowed()
    {
        var decision = Lookup(RoleId(GovernanceRoleKind.Requester), RoleForbiddenActionKind.RouteGuardCreation);

        Assert.AreEqual(ForbiddenActionLookupClassification.NoCatalogGrantSeparateAuthorityRequired, decision.Classification);
        Assert.IsTrue(decision.RequiresSeparateAuthority);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnknownRoleFailsClosed()
    {
        var decision = Lookup("role:f13:missing", RoleForbiddenActionKind.AccessGrant);

        Assert.AreEqual(ForbiddenActionLookupClassification.BlockedByUnknownRole, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnknownActionFailsClosed()
    {
        var decision = Service().Lookup(
            RoleCatalog(),
            Catalog(),
            Request(RoleId(GovernanceRoleKind.Requester), RoleForbiddenActionKind.Unknown));

        Assert.AreEqual(ForbiddenActionLookupClassification.BlockedByUnknownAction, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnknownAuthoritySourceFailsClosed()
    {
        var decision = Service().Lookup(
            RoleCatalog(),
            Catalog(),
            Request(RoleId(GovernanceRoleKind.Requester), RoleForbiddenActionKind.AccessGrant) with
            {
                AuthoritySourceKind = ForbiddenActionAuthoritySourceKind.Unknown
            });

        Assert.AreEqual(ForbiddenActionLookupClassification.BlockedByUnknownAuthoritySource, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void MissingRoleCatalogEvidenceFailsClosed()
    {
        var decision = Service().Lookup(
            RoleCatalog(),
            Catalog(),
            Request(RoleId(GovernanceRoleKind.Requester), RoleForbiddenActionKind.AccessGrant) with
            {
                RoleCatalogEvidenceRef = string.Empty
            });

        Assert.AreEqual(ForbiddenActionLookupClassification.BlockedByMissingRoleCatalogEvidence, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void MissingForbiddenActionCatalogEvidenceFailsClosed()
    {
        var decision = Service().Lookup(
            RoleCatalog(),
            Catalog(),
            Request(RoleId(GovernanceRoleKind.Requester), RoleForbiddenActionKind.AccessGrant) with
            {
                ForbiddenActionCatalogEvidenceRef = string.Empty
            });

        Assert.AreEqual(ForbiddenActionLookupClassification.BlockedByMissingForbiddenCatalogEvidence, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void HostileForbiddenActionTextIsRejected()
    {
        var hostile = string.Concat("role grants ", "action");
        var validation = ForbiddenActionCatalogValidator.ValidateEntry(Entry(Role(GovernanceRoleKind.Requester), RoleForbiddenActionKind.AccessGrant) with
        {
            BoundaryStatement = hostile
        });

        Assert.IsFalse(validation.IsValid);
        CollectionAssert.Contains(validation.Issues.ToList(), "BoundaryStatementUnsafe");
        CollectionAssert.Contains(validation.UnsafeRefs.ToList(), hostile);
    }

    [TestMethod]
    public void UnsafeAllowMarkersAreRejected()
    {
        var marker = string.Concat("Is", "Allowed", " = true");
        var validation = ForbiddenActionCatalogValidator.ValidateEntry(Entry(Role(GovernanceRoleKind.Requester), RoleForbiddenActionKind.AccessGrant) with
        {
            BoundaryStatement = marker
        });

        Assert.IsFalse(validation.IsValid);
        CollectionAssert.Contains(validation.Issues.ToList(), "BoundaryStatementUnsafe");
    }

    [TestMethod]
    public void UnsafeLookupEvidenceIsInvalidAndNotEchoed()
    {
        var hostile = string.Concat("not forbidden means ", "allowed");
        var decision = Service().Lookup(
            RoleCatalog(),
            Catalog(),
            Request(RoleId(GovernanceRoleKind.Requester), RoleForbiddenActionKind.AccessGrant) with
            {
                ForbiddenActionCatalogEvidenceRef = hostile
            });

        Assert.AreEqual(ForbiddenActionLookupClassification.Invalid, decision.Classification);
        CollectionAssert.DoesNotContain(decision.EvidenceRefs.ToList(), hostile);
        CollectionAssert.Contains(decision.EvidenceRefs.ToList(), "[unsafe-rejected]");
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void EveryLookupDecisionHasFalseAuthorityFlagsAndRequiresSeparateAuthority()
    {
        foreach (var role in RoleCatalog().Entries.Take(4))
        {
            foreach (var action in new[] { RoleForbiddenActionKind.AccessGrant, RoleForbiddenActionKind.RouteGuardCreation })
            {
                var decision = Lookup(role.RoleId, action);
                Assert.IsTrue(decision.RequiresSeparateAuthority);
                AssertDecisionAuthorityFlagsFalse(decision);
            }
        }
    }

    [TestMethod]
    public void ClassificationVocabularyDoesNotContainAllowedPermittedAuthorizedOrGranted()
    {
        var names = Enum.GetNames<ForbiddenActionLookupClassification>();
        foreach (var forbidden in new[] { "Allowed", "Permitted", "Authorized", "Granted", "CanExecute" })
        {
            Assert.IsFalse(names.Any(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)), forbidden);
        }
    }

    [TestMethod]
    public void StaticScanAddsNoRuntimeAuthorityOrSurface()
    {
        var source = string.Join(
            Environment.NewLine,
            SourceFiles().Select(File.ReadAllText).Select(StripStringLiterals));

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
            "AllowAnonymous",
            "AuthorizeAttribute",
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
            "RecoveryExecutor"
        })
        {
            Assert.IsFalse(
                source.Contains(forbiddenToken, StringComparison.Ordinal),
                $"Unexpected runtime/authority surface token found: {forbiddenToken}");
        }
    }

    [TestMethod]
    public void ReceiptExistsAndStatesForbiddenActionMetadataIsNotAuthorization()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "F13_FORBIDDEN_ACTION_CATALOG_BY_ROLE.md"));

        StringAssert.Contains(receipt, "Forbidden action metadata is not authorization.");
        StringAssert.Contains(receipt, "A blacklist is not a permission system.");
        StringAssert.Contains(receipt, "Absence from the forbidden catalog is not permission.");
        StringAssert.Contains(receipt, "F09 boundary tests remain intentionally deferred.");
        StringAssert.Contains(receipt, "does not implement authorization");
        StringAssert.Contains(receipt, "does not implement runtime enforcement");
    }

    private static void AssertForbidden(GovernanceRoleKind roleKind, RoleForbiddenActionKind actionKind)
    {
        var role = Role(roleKind);
        var entry = Catalog().Entries.SingleOrDefault(candidate =>
            string.Equals(candidate.RoleId, role.RoleId, StringComparison.OrdinalIgnoreCase) &&
            candidate.RoleForbiddenActionKind == actionKind);

        Assert.IsNotNull(entry, $"{roleKind} missing {actionKind}");
        Assert.IsTrue(entry!.IsForbidden);
        AssertEntryAuthorityFlagsFalse(entry);
    }

    private static ForbiddenActionLookupDecision Lookup(string roleId, RoleForbiddenActionKind actionKind) =>
        Service().Lookup(RoleCatalog(), Catalog(), Request(roleId, actionKind));

    private static ForbiddenActionLookupRequest Request(string roleId, RoleForbiddenActionKind actionKind) =>
        new()
        {
            CorrelationId = "correlation-f13",
            RequestedRoleId = roleId,
            RequestedActionKind = actionKind,
            AuthoritySourceKind = ForbiddenActionAuthoritySourceKind.RoleEvidence,
            RoleCatalogEvidenceRef = "role-catalog:f13",
            ForbiddenActionCatalogEvidenceRef = "forbidden-action-catalog:f13",
            OptionalPolicyEvidenceRef = "policy-evidence:f13",
            OptionalApprovalEvidenceRef = "approval-evidence:f13",
            OptionalExecutionAuthorityRef = "execution-authority:f13",
            OptionalMutationAuthorityRef = "mutation-authority:f13",
            OptionalWorkflowAuthorityRef = "workflow-authority:f13",
            OptionalReleaseAuthorityRef = "release-authority:f13",
            OptionalRedactionDecisionRef = "redaction-decision:f13"
        };

    private static ForbiddenActionCatalogEntry Entry(
        GovernanceRoleCatalogEntry role,
        RoleForbiddenActionKind actionKind) =>
        new()
        {
            RoleId = role.RoleId,
            RoleKind = role.RoleKind,
            RoleDisplayName = role.DisplayName,
            RoleForbiddenActionKind = actionKind,
            ReasonKind = ForbiddenActionReasonKind.RoleEvidenceCannotGrantAuthority,
            BoundaryStatement = "Forbidden action metadata is not authorization and not a permission grant.",
            RequiredSeparateEvidenceRefs = ["separate-authority-evidence:f13"],
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

    private static ForbiddenActionCatalogService Service() => new();

    private static ForbiddenActionCatalog Catalog() => Service().BuildDefaultCatalog(RoleCatalog());

    private static GovernanceRoleCatalog RoleCatalog() => new RoleCatalogService().BuildDefaultCatalog();

    private static GovernanceRoleCatalogEntry Role(GovernanceRoleKind kind) =>
        RoleCatalog().Entries.Single(entry => entry.RoleKind == kind);

    private static string RoleId(GovernanceRoleKind kind) => Role(kind).RoleId;

    private static void AssertEntryAuthorityFlagsFalse(ForbiddenActionCatalogEntry entry)
    {
        Assert.IsFalse(entry.IsAllowed);
        Assert.IsFalse(entry.GrantsAuthority);
        Assert.IsFalse(entry.GrantsPermission);
        Assert.IsFalse(entry.SatisfiesPolicy);
        Assert.IsFalse(entry.AllowsExecution);
        Assert.IsFalse(entry.AllowsMutation);
        Assert.IsFalse(entry.AllowsWorkflowContinuation);
        Assert.IsFalse(entry.AllowsRelease);
        Assert.IsFalse(entry.AllowsDeployment);
        Assert.IsFalse(entry.BypassesRedaction);
        Assert.IsFalse(entry.DisclosesSecrets);
        Assert.IsFalse(entry.DisclosesCredentials);
        Assert.IsFalse(entry.DisclosesRawPayload);
        Assert.IsFalse(entry.DisclosesPrivateReasoning);
    }

    private static void AssertDecisionAuthorityFlagsFalse(ForbiddenActionLookupDecision decision)
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

    private static IEnumerable<string> SourceFiles()
    {
        var root = RepoRoot();
        yield return Path.Combine(root, "IronDev.Core", "Governance", "ForbiddenActionCatalogModels.cs");
        yield return Path.Combine(root, "IronDev.Core", "Governance", "ForbiddenActionCatalogService.cs");
        yield return Path.Combine(root, "IronDev.Core", "Governance", "ForbiddenActionCatalogValidator.cs");
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
