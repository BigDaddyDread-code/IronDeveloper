using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockF09aSystemOwnerRoleCatalogContractTests
{
    [TestMethod]
    public void DefaultCatalogContainsExactlyOneSystemAccountabilityOwnerRole()
    {
        var entries = CatalogService().ListByKind(Catalog(), GovernanceRoleKind.SystemAccountabilityOwner);

        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("role:f01:system-accountability-owner", entries[0].RoleId);
        Assert.AreEqual("System Accountability Owner", entries[0].DisplayName);
    }

    [TestMethod]
    public void SystemAccountabilityOwnerRoleIsCatalogOnlyAndDescriptive()
    {
        var entry = SystemOwner();

        Assert.AreEqual(GovernanceRoleScopeKind.GlobalCatalog, entry.ScopeKind);
        Assert.AreEqual(GovernanceRoleKind.SystemAccountabilityOwner, entry.RoleKind);
        StringAssert.Contains(entry.Description, "accountability responsibility marker");
        StringAssert.Contains(entry.ResponsibilitySummary, "does not grant controls");
        StringAssert.Contains(entry.BoundaryStatement, "does not grant authority");
    }

    [TestMethod]
    public void SystemAccountabilityOwnerRoleTextContainsNoAuthorityGrantWording()
    {
        var entry = SystemOwner();
        var text = string.Join(" ", entry.Description, entry.ResponsibilitySummary);
        var forbidden = new[]
        {
            "grant access",
            "manage permissions",
            "assign roles",
            "approve",
            "satisfy policy",
            "access all tenants",
            "bypass tenant",
            "impersonate",
            "mutate",
            "execute",
            "retry",
            "rollback",
            "recover",
            "continue workflow",
            "merge",
            "release",
            "deploy",
            "break glass",
            "override governance",
            "bypass redaction",
            "view secrets",
            "view private reasoning"
        };

        foreach (var marker in forbidden)
        {
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    [DataTestMethod]
    [DataRow(nameof(SystemOwnerBoundaryFinding.GrantsSystemAuthority))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.GrantsPlatformAuthority))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.GrantsRootAuthority))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.GrantsGlobalAuthority))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.GrantsCrossTenantVisibility))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.GrantsTenantBoundaryOverride))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.GrantsAccess))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.GrantsIdentityAuthority))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.GrantsRoleAssignmentAuthority))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.GrantsPermissionManagementAuthority))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.GrantsImpersonationAuthority))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.GrantsApprovalAuthority))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.SatisfiesPolicy))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.RefreshesValidation))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.ProvesSourceSafety))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.GrantsDiagnosticExecutionAuthority))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.GrantsRetryAuthority))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.GrantsRollbackAuthority))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.GrantsRecoveryAuthority))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.GrantsMutationAuthority))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.GrantsWorkflowContinuation))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.GrantsMergeAuthority))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.GrantsReleaseAuthority))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.GrantsDeploymentAuthority))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.GrantsBreakGlassAuthority))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.GrantsGovernanceOverrideAuthority))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.BypassesRedaction))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.DisclosesSecrets))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.DisclosesCredentials))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.DisclosesRawPayload))]
    [DataRow(nameof(SystemOwnerBoundaryFinding.DisclosesPrivateReasoning))]
    public void SystemAccountabilityOwnerRoleDoesNotImplyAuthority(string propertyName)
    {
        var finding = SystemOwnerBoundaryFinding.FromSafeCatalogEntry(SystemOwner());
        var value = (bool)(typeof(SystemOwnerBoundaryFinding).GetProperty(propertyName)?.GetValue(finding)
            ?? throw new InvalidOperationException($"Missing property {propertyName}."));

        Assert.IsFalse(value, propertyName);
    }

    [TestMethod]
    public void RoleCatalogValidatorAcceptsSystemAccountabilityOwnerAsCatalogEntryOnly()
    {
        var result = RoleCatalogValidator.ValidateEntry(SystemOwner());

        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues));
        CollectionAssert.Contains(result.Warnings.ToList(), "role catalog is descriptive only");
        CollectionAssert.Contains(result.Warnings.ToList(), "role catalog does not grant permissions");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role catalog is not authorization");
    }

    [DataTestMethod]
    [DataRow("IsSystemOwner = true")]
    [DataRow("SystemOwnerGranted = true")]
    [DataRow("SystemOwnerAssigned = true")]
    [DataRow("PlatformOwner = true")]
    [DataRow("RootOwner = true")]
    [DataRow("GlobalOwner = true")]
    [DataRow("CanAccessEverything = true")]
    [DataRow("CanAccessAllTenants = true")]
    [DataRow("CanBypassTenantBoundary = true")]
    [DataRow("CanImpersonate = true")]
    [DataRow("CanAssignRoles = true")]
    [DataRow("CanGrantAccess = true")]
    [DataRow("CanManagePermissions = true")]
    [DataRow("CanApprove = true")]
    [DataRow("SatisfiesPolicy = true")]
    [DataRow("CanRefreshValidation = true")]
    [DataRow("CanProveSourceSafety = true")]
    [DataRow("CanRunDiagnostic = true")]
    [DataRow("CanRetry = true")]
    [DataRow("CanRollback = true")]
    [DataRow("CanRecover = true")]
    [DataRow("CanMutate = true")]
    [DataRow("CanApplyPatch = true")]
    [DataRow("CanCommit = true")]
    [DataRow("CanPush = true")]
    [DataRow("CanCreatePullRequest = true")]
    [DataRow("CanReadyForReview = true")]
    [DataRow("CanMerge = true")]
    [DataRow("CanRelease = true")]
    [DataRow("CanDeploy = true")]
    [DataRow("CanContinue = true")]
    [DataRow("CanBreakGlass = true")]
    [DataRow("CanOverrideGovernance = true")]
    [DataRow("BypassRedaction = true")]
    [DataRow("ShowSecrets = true")]
    [DataRow("ShowCredentials = true")]
    [DataRow("ShowRawPayload = true")]
    [DataRow("ShowPrivateReasoning = true")]
    [DataRow("system owner may operate globally")]
    [DataRow("system owner may inspect all tenants")]
    [DataRow("system owner may grant itself access")]
    [DataRow("system owner may assign roles")]
    [DataRow("system owner may manage permissions")]
    [DataRow("system owner may approve policy")]
    [DataRow("system owner may satisfy policy")]
    [DataRow("system owner may continue workflow")]
    [DataRow("system owner may override governance")]
    [DataRow("system owner may bypass redaction")]
    [DataRow("system owner may reveal secrets")]
    [DataRow("system owner may execute break glass")]
    public void RoleCatalogValidatorRejectsSystemOwnerAuthorityText(string unsafeText)
    {
        var result = RoleCatalogValidator.ValidateEntry(SystemOwner() with
        {
            Description = $"Unsafe system-owner claim: {unsafeText}."
        });
        var finding = SystemOwnerAuthorityMarkerDetector.Evaluate(SystemOwnerEvidenceFixture.Hostile(unsafeText));

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(static issue => issue.EndsWith("Unsafe", StringComparison.Ordinal)));
        CollectionAssert.Contains(result.UnsafeRoleIds.ToList(), SystemOwner().RoleId);
        Assert.IsTrue(finding.IsHidden);
        Assert.IsTrue(finding.RejectedMarkers.Contains(unsafeText, StringComparer.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void DefaultCatalogValidationStillPassesWithSystemAccountabilityOwner()
    {
        var result = CatalogService().ValidateDefaultCatalog();

        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues));
    }

    [TestMethod]
    public void DefaultVisibilityMatrixStillValidWithSystemAccountabilityOwner()
    {
        var result = MatrixService().ValidateDefaultMatrix(Catalog());

        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues));
    }

    [DataTestMethod]
    [DataRow(RoleVisibilityMaterialKind.RawPayload)]
    [DataRow(RoleVisibilityMaterialKind.CredentialMaterial)]
    [DataRow(RoleVisibilityMaterialKind.PrivateReasoning)]
    public void SystemAccountabilityOwnerMatrixEntriesDoNotExposeSensitiveMaterial(RoleVisibilityMaterialKind materialKind)
    {
        var entries = SystemOwnerMatrixEntries()
            .Where(entry => entry.MaterialKind == materialKind)
            .ToArray();

        Assert.AreEqual(1, entries.Length);
        Assert.IsTrue(entries.All(static entry => entry.VisibilityLevel == RoleVisibilityLevel.NotVisible));
        Assert.IsTrue(entries.All(static entry => entry.RequiresSeparatePolicyDecision));
        Assert.IsTrue(entries.All(static entry => entry.RequiresRedaction));
    }

    [TestMethod]
    public void SystemAccountabilityOwnerRoleNameCannotBeUsedAsDownstreamEvidence()
    {
        var finding = SystemOwnerAuthorityMarkerDetector.Evaluate(SystemOwnerEvidenceFixture.Safe());

        Assert.IsFalse(finding.GrantsApprovalAuthority);
        Assert.IsFalse(finding.SatisfiesPolicy);
        Assert.IsFalse(finding.RefreshesValidation);
        Assert.IsFalse(finding.ProvesSourceSafety);
        Assert.IsFalse(finding.GrantsWorkflowContinuation);
        Assert.IsFalse(finding.GrantsMergeAuthority);
        Assert.IsFalse(finding.GrantsReleaseAuthority);
        Assert.IsFalse(finding.GrantsDeploymentAuthority);
        CollectionAssert.Contains(finding.RequiredSeparateEvidence.ToList(), "approval-evidence");
        CollectionAssert.Contains(finding.RequiredSeparateEvidence.ToList(), "policy-satisfaction-evidence");
        CollectionAssert.Contains(finding.RequiredSeparateEvidence.ToList(), "validation-freshness-evidence");
        CollectionAssert.Contains(finding.RequiredSeparateEvidence.ToList(), "source-safety-evidence");
        CollectionAssert.Contains(finding.RequiredSeparateEvidence.ToList(), "workflow-continuation-evidence");
        CollectionAssert.Contains(finding.RequiredSeparateEvidence.ToList(), "merge-authority");
        CollectionAssert.Contains(finding.RequiredSeparateEvidence.ToList(), "release-authority");
        CollectionAssert.Contains(finding.RequiredSeparateEvidence.ToList(), "deployment-authority");
    }

    [TestMethod]
    public void StaticScanF09aAddsNoProductionAuthoritySurface()
    {
        var source = StripStringLiterals(F09aCoreSource());
        var forbidden = new[]
        {
            "ClaimsPrincipal",
            "IPrincipal",
            "UserManager",
            "RoleManager",
            "PermissionResolver",
            "AccessControl",
            "Impersonat",
            "ApiController",
            "ControllerBase",
            "DbContext",
            "SqlConnection",
            "IRepository",
            "Repository<",
            "Store<",
            "HttpClient",
            "ProcessStartInfo",
            "RunProcessAsync",
            "ExecuteAsync",
            "ApplyAsync",
            "CommitAsync",
            "PushAsync",
            "MergeAsync",
            "ReleaseAsync",
            "DeployAsync",
            "WorkflowTransition",
            "ApprovalRecord",
            "PolicySatisfactionService"
        };

        foreach (var marker in forbidden)
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    [TestMethod]
    public void ReceiptExistsAndStatesSystemOwnerRoleNameIsNotAuthority()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "F09A_SYSTEM_OWNER_ROLE_CATALOG_CONTRACT.md"));

        StringAssert.Contains(receipt, "A system owner role name is not system authority.");
        StringAssert.Contains(receipt, "System owner evidence is not system authority.");
        StringAssert.Contains(receipt, "Owning accountability is not owning the controls.");
        StringAssert.Contains(receipt, "does not implement system owner authority");
        StringAssert.Contains(receipt, "does not implement platform owner authority");
        StringAssert.Contains(receipt, "does not implement root authority");
        StringAssert.Contains(receipt, "does not implement break-glass authority");
        StringAssert.Contains(receipt, "does not implement governance override");
    }

    private static GovernanceRoleCatalog Catalog() => CatalogService().BuildDefaultCatalog();

    private static RoleCatalogService CatalogService() => new();

    private static RoleVisibilityMatrixService MatrixService() => new();

    private static GovernanceRoleCatalogEntry SystemOwner() =>
        Catalog().Entries.Single(entry => entry.RoleKind == GovernanceRoleKind.SystemAccountabilityOwner);

    private static IReadOnlyList<RoleVisibilityMatrixEntry> SystemOwnerMatrixEntries() =>
        MatrixService().FindEntriesByRoleId(
            MatrixService().BuildDefaultMatrix(Catalog()),
            SystemOwner().RoleId);

    private static string F09aCoreSource()
    {
        var root = RepoRoot();
        var files = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "RoleCatalogModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "RoleCatalogService.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "RoleCatalogValidator.cs")
        };

        return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
    }

    private static string StripStringLiterals(string source)
    {
        var chars = source.ToCharArray();
        var inString = false;
        var inVerbatim = false;
        for (var i = 0; i < chars.Length; i++)
        {
            if (!inString && chars[i] == '@' && i + 1 < chars.Length && chars[i + 1] == '"')
            {
                inString = true;
                inVerbatim = true;
                chars[i] = ' ';
                continue;
            }

            if (!inString && chars[i] == '"')
            {
                inString = true;
                inVerbatim = false;
                chars[i] = ' ';
                continue;
            }

            if (!inString)
            {
                continue;
            }

            if (inVerbatim && chars[i] == '"' && i + 1 < chars.Length && chars[i + 1] == '"')
            {
                chars[i] = ' ';
                chars[i + 1] = ' ';
                i++;
                continue;
            }

            if ((!inVerbatim && chars[i] == '"' && (i == 0 || chars[i - 1] != '\\')) ||
                (inVerbatim && chars[i] == '"'))
            {
                inString = false;
                inVerbatim = false;
            }

            chars[i] = ' ';
        }

        return new string(chars);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed record SystemOwnerEvidenceFixture
    {
        public required string RoleId { get; init; }
        public required IReadOnlyList<string> EvidenceRefs { get; init; }
        public required IReadOnlyList<string> TextMarkers { get; init; }

        public static SystemOwnerEvidenceFixture Safe() =>
            new()
            {
                RoleId = "role:f01:system-accountability-owner",
                EvidenceRefs = ["role-catalog-entry:role:f01:system-accountability-owner"],
                TextMarkers = []
            };

        public static SystemOwnerEvidenceFixture Hostile(string marker) =>
            Safe() with
            {
                TextMarkers = [marker]
            };
    }

    private sealed record SystemOwnerBoundaryFinding
    {
        public required bool GrantsSystemAuthority { get; init; }
        public required bool GrantsPlatformAuthority { get; init; }
        public required bool GrantsRootAuthority { get; init; }
        public required bool GrantsGlobalAuthority { get; init; }
        public required bool GrantsCrossTenantVisibility { get; init; }
        public required bool GrantsTenantBoundaryOverride { get; init; }
        public required bool GrantsAccess { get; init; }
        public required bool GrantsIdentityAuthority { get; init; }
        public required bool GrantsRoleAssignmentAuthority { get; init; }
        public required bool GrantsPermissionManagementAuthority { get; init; }
        public required bool GrantsImpersonationAuthority { get; init; }
        public required bool GrantsApprovalAuthority { get; init; }
        public required bool SatisfiesPolicy { get; init; }
        public required bool RefreshesValidation { get; init; }
        public required bool ProvesSourceSafety { get; init; }
        public required bool GrantsDiagnosticExecutionAuthority { get; init; }
        public required bool GrantsRetryAuthority { get; init; }
        public required bool GrantsRollbackAuthority { get; init; }
        public required bool GrantsRecoveryAuthority { get; init; }
        public required bool GrantsMutationAuthority { get; init; }
        public required bool GrantsWorkflowContinuation { get; init; }
        public required bool GrantsMergeAuthority { get; init; }
        public required bool GrantsReleaseAuthority { get; init; }
        public required bool GrantsDeploymentAuthority { get; init; }
        public required bool GrantsBreakGlassAuthority { get; init; }
        public required bool GrantsGovernanceOverrideAuthority { get; init; }
        public required bool BypassesRedaction { get; init; }
        public required bool DisclosesSecrets { get; init; }
        public required bool DisclosesCredentials { get; init; }
        public required bool DisclosesRawPayload { get; init; }
        public required bool DisclosesPrivateReasoning { get; init; }
        public required bool IsHidden { get; init; }
        public required IReadOnlyList<string> RequiredSeparateEvidence { get; init; }
        public required IReadOnlyList<string> RejectedMarkers { get; init; }

        public static SystemOwnerBoundaryFinding FromSafeCatalogEntry(GovernanceRoleCatalogEntry entry)
        {
            Assert.AreEqual(GovernanceRoleKind.SystemAccountabilityOwner, entry.RoleKind);
            return new SystemOwnerBoundaryFinding
            {
                GrantsSystemAuthority = false,
                GrantsPlatformAuthority = false,
                GrantsRootAuthority = false,
                GrantsGlobalAuthority = false,
                GrantsCrossTenantVisibility = false,
                GrantsTenantBoundaryOverride = false,
                GrantsAccess = false,
                GrantsIdentityAuthority = false,
                GrantsRoleAssignmentAuthority = false,
                GrantsPermissionManagementAuthority = false,
                GrantsImpersonationAuthority = false,
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
                GrantsBreakGlassAuthority = false,
                GrantsGovernanceOverrideAuthority = false,
                BypassesRedaction = false,
                DisclosesSecrets = false,
                DisclosesCredentials = false,
                DisclosesRawPayload = false,
                DisclosesPrivateReasoning = false,
                IsHidden = false,
                RequiredSeparateEvidence =
                [
                    "identity-evidence",
                    "role-assignment-evidence",
                    "permission-management-authority",
                    "access-decision",
                    "tenant-boundary-review",
                    "approval-evidence",
                    "policy-satisfaction-evidence",
                    "validation-freshness-evidence",
                    "source-safety-evidence",
                    "diagnostic-execution-authority",
                    "retry-authority",
                    "rollback-authority",
                    "recovery-authority",
                    "mutation-authority",
                    "workflow-continuation-evidence",
                    "merge-authority",
                    "release-authority",
                    "deployment-authority",
                    "break-glass-authority",
                    "governance-override-authority",
                    "redaction-decision"
                ],
                RejectedMarkers = []
            };
        }
    }

    private static class SystemOwnerAuthorityMarkerDetector
    {
        public static SystemOwnerBoundaryFinding Evaluate(SystemOwnerEvidenceFixture fixture)
        {
            var rejectedMarkers = fixture.TextMarkers
                .Where(RoleCatalogValidator.ContainsUnsafeRoleText)
                .ToArray();

            return SystemOwnerBoundaryFinding.FromSafeCatalogEntry(SystemOwner()) with
            {
                IsHidden = rejectedMarkers.Length > 0,
                RejectedMarkers = rejectedMarkers
            };
        }
    }
}
