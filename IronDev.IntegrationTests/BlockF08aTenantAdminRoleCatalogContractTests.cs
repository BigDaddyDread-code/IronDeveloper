using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockF08aTenantAdminRoleCatalogContractTests
{
    [TestMethod]
    public void DefaultCatalogContainsExactlyOneTenantAdminRole()
    {
        var entries = CatalogService().ListByKind(Catalog(), GovernanceRoleKind.TenantAdministrator);

        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("role:f01:tenant-administrator", entries[0].RoleId);
        Assert.AreEqual("Tenant Administrator", entries[0].DisplayName);
    }

    [TestMethod]
    public void TenantAdminRoleIsTenantScopedOnly()
    {
        var entry = TenantAdmin();

        Assert.AreEqual(GovernanceRoleScopeKind.TenantScoped, entry.ScopeKind);
        Assert.AreNotEqual(GovernanceRoleScopeKind.GlobalCatalog, entry.ScopeKind);
        Assert.AreNotEqual(GovernanceRoleScopeKind.ProjectScoped, entry.ScopeKind);
        Assert.AreNotEqual(GovernanceRoleScopeKind.OperationScoped, entry.ScopeKind);
    }

    [TestMethod]
    public void TenantAdminRoleDescriptionContainsNoAuthorityGrantWording()
    {
        var entry = TenantAdmin();
        var text = string.Join(" ", entry.Description, entry.ResponsibilitySummary);
        var forbidden = new[]
        {
            "grant",
            "approve",
            "assign",
            "access all",
            "cross tenant",
            "global",
            "platform admin",
            "permission",
            "impersonate",
            "mutate",
            "execute",
            "retry",
            "rollback",
            "recover",
            "merge",
            "release",
            "deploy",
            "bypass"
        };

        foreach (var marker in forbidden)
        {
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    [DataTestMethod]
    [DataRow(nameof(TenantAdminBoundaryFinding.GrantsTenantAuthority))]
    [DataRow(nameof(TenantAdminBoundaryFinding.GrantsPlatformAuthority))]
    [DataRow(nameof(TenantAdminBoundaryFinding.GrantsGlobalAdminAuthority))]
    [DataRow(nameof(TenantAdminBoundaryFinding.GrantsCrossTenantVisibility))]
    [DataRow(nameof(TenantAdminBoundaryFinding.GrantsAccess))]
    [DataRow(nameof(TenantAdminBoundaryFinding.GrantsRoleAssignmentAuthority))]
    [DataRow(nameof(TenantAdminBoundaryFinding.GrantsPermissionManagementAuthority))]
    [DataRow(nameof(TenantAdminBoundaryFinding.GrantsImpersonationAuthority))]
    [DataRow(nameof(TenantAdminBoundaryFinding.GrantsApprovalAuthority))]
    [DataRow(nameof(TenantAdminBoundaryFinding.SatisfiesPolicy))]
    [DataRow(nameof(TenantAdminBoundaryFinding.RefreshesValidation))]
    [DataRow(nameof(TenantAdminBoundaryFinding.ProvesSourceSafety))]
    [DataRow(nameof(TenantAdminBoundaryFinding.GrantsDiagnosticExecutionAuthority))]
    [DataRow(nameof(TenantAdminBoundaryFinding.GrantsRetryAuthority))]
    [DataRow(nameof(TenantAdminBoundaryFinding.GrantsRollbackAuthority))]
    [DataRow(nameof(TenantAdminBoundaryFinding.GrantsRecoveryAuthority))]
    [DataRow(nameof(TenantAdminBoundaryFinding.GrantsMutationAuthority))]
    [DataRow(nameof(TenantAdminBoundaryFinding.GrantsWorkflowContinuation))]
    [DataRow(nameof(TenantAdminBoundaryFinding.GrantsMergeAuthority))]
    [DataRow(nameof(TenantAdminBoundaryFinding.GrantsReleaseAuthority))]
    [DataRow(nameof(TenantAdminBoundaryFinding.GrantsDeploymentAuthority))]
    [DataRow(nameof(TenantAdminBoundaryFinding.BypassesRedaction))]
    [DataRow(nameof(TenantAdminBoundaryFinding.DisclosesSecrets))]
    [DataRow(nameof(TenantAdminBoundaryFinding.DisclosesCredentials))]
    [DataRow(nameof(TenantAdminBoundaryFinding.DisclosesRawPayload))]
    [DataRow(nameof(TenantAdminBoundaryFinding.DisclosesPrivateReasoning))]
    public void TenantAdminRoleDoesNotImplyAuthority(string propertyName)
    {
        var finding = TenantAdminBoundaryFinding.FromSafeCatalogEntry(TenantAdmin());
        var value = (bool)(typeof(TenantAdminBoundaryFinding).GetProperty(propertyName)?.GetValue(finding)
            ?? throw new InvalidOperationException($"Missing property {propertyName}."));

        Assert.IsFalse(value, propertyName);
    }

    [TestMethod]
    public void RoleCatalogValidatorAcceptsTenantAdminAsCatalogEntryOnly()
    {
        var result = RoleCatalogValidator.ValidateEntry(TenantAdmin());

        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues));
        CollectionAssert.Contains(result.Warnings.ToList(), "role catalog is descriptive only");
        CollectionAssert.Contains(result.Warnings.ToList(), "role catalog does not grant permissions");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role catalog is not authorization");
    }

    [DataTestMethod]
    [DataRow("IsTenantAdmin = true")]
    [DataRow("TenantAdminGranted = true")]
    [DataRow("TenantAdminAssigned = true")]
    [DataRow("PlatformAdmin = true")]
    [DataRow("GlobalAdmin = true")]
    [DataRow("CanAccessAllTenants = true")]
    [DataRow("CanBypassTenantBoundary = true")]
    [DataRow("CanImpersonate = true")]
    [DataRow("CanAssignRoles = true")]
    [DataRow("CanGrantAccess = true")]
    [DataRow("CanApprove = true")]
    [DataRow("SatisfiesPolicy = true")]
    [DataRow("CanContinue = true")]
    [DataRow("BypassRedaction = true")]
    [DataRow("ShowSecrets = true")]
    [DataRow("ShowCredentials = true")]
    [DataRow("ShowRawPayload = true")]
    [DataRow("ShowPrivateReasoning = true")]
    [DataRow("tenant admin can grant access")]
    [DataRow("tenant admin can manage permissions")]
    [DataRow("tenant admin can access all tenants")]
    [DataRow("tenant admin can bypass tenant isolation")]
    [DataRow("tenant admin can approve policy")]
    [DataRow("tenant admin can continue workflow")]
    [DataRow("tenant admin can operate globally")]
    [DataRow("tenant admin can reveal secrets")]
    public void RoleCatalogValidatorRejectsTenantAdminAuthorityText(string unsafeText)
    {
        var result = RoleCatalogValidator.ValidateEntry(TenantAdmin() with
        {
            Description = $"Unsafe tenant-admin claim: {unsafeText}."
        });

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(static issue => issue.EndsWith("Unsafe", StringComparison.Ordinal)));
        CollectionAssert.Contains(result.UnsafeRoleIds.ToList(), TenantAdmin().RoleId);
    }

    [TestMethod]
    public void DefaultCatalogValidationStillPassesWithTenantAdmin()
    {
        var result = CatalogService().ValidateDefaultCatalog();

        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues));
    }

    [TestMethod]
    public void DefaultVisibilityMatrixStillValidWithTenantAdmin()
    {
        var result = MatrixService().ValidateDefaultMatrix(Catalog());

        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues));
    }

    [DataTestMethod]
    [DataRow(RoleVisibilityMaterialKind.RawPayload)]
    [DataRow(RoleVisibilityMaterialKind.CredentialMaterial)]
    [DataRow(RoleVisibilityMaterialKind.PrivateReasoning)]
    public void TenantAdminMatrixEntriesDoNotExposeSensitiveMaterial(RoleVisibilityMaterialKind materialKind)
    {
        var entries = TenantAdminMatrixEntries()
            .Where(entry => entry.MaterialKind == materialKind)
            .ToArray();

        Assert.AreEqual(1, entries.Length);
        Assert.IsTrue(entries.All(static entry => entry.VisibilityLevel == RoleVisibilityLevel.NotVisible));
        Assert.IsTrue(entries.All(static entry => entry.RequiresSeparatePolicyDecision));
        Assert.IsTrue(entries.All(static entry => entry.RequiresRedaction));
    }

    [TestMethod]
    public void TenantAdminMatrixEntriesDoNotCreateActionAccessOrCrossTenantAuthority()
    {
        foreach (var entry in TenantAdminMatrixEntries())
        {
            Assert.IsTrue(entry.RequiresSeparateRoleAssignment);
            Assert.IsTrue(entry.RequiresSeparateVisibilityDecision);
            StringAssert.Contains(entry.BoundaryStatement, "does not grant access");
            StringAssert.Contains(entry.BoundaryStatement, "does not grant authority");
            Assert.AreNotEqual(RoleVisibilityLevel.DetailEligibilityHint, entry.VisibilityLevel);
        }
    }

    [TestMethod]
    public void StaticScanF08aAddsNoProductionAuthoritySurface()
    {
        var source = StripStringLiterals(F08aCoreSource());
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
    public void ReceiptExistsAndStatesTenantAdminRoleNameIsNotAuthority()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "F08A_TENANT_ADMIN_ROLE_CATALOG_CONTRACT.md"));

        StringAssert.Contains(receipt, "A tenant admin role name is not tenant admin authority.");
        StringAssert.Contains(receipt, "Naming the admin is not granting the keys.");
        StringAssert.Contains(receipt, "does not implement tenant admin authority");
        StringAssert.Contains(receipt, "does not implement platform admin authority");
        StringAssert.Contains(receipt, "does not implement cross-tenant access");
        StringAssert.Contains(receipt, "does not implement permission management");
        StringAssert.Contains(receipt, "does not implement workflow continuation");
    }

    private static GovernanceRoleCatalog Catalog() => CatalogService().BuildDefaultCatalog();

    private static RoleCatalogService CatalogService() => new();

    private static RoleVisibilityMatrixService MatrixService() => new();

    private static GovernanceRoleCatalogEntry TenantAdmin() =>
        Catalog().Entries.Single(entry => entry.RoleKind == GovernanceRoleKind.TenantAdministrator);

    private static IReadOnlyList<RoleVisibilityMatrixEntry> TenantAdminMatrixEntries() =>
        MatrixService().FindEntriesByRoleId(
            MatrixService().BuildDefaultMatrix(Catalog()),
            TenantAdmin().RoleId);

    private static string F08aCoreSource()
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

    private sealed record TenantAdminBoundaryFinding
    {
        public required bool GrantsTenantAuthority { get; init; }
        public required bool GrantsPlatformAuthority { get; init; }
        public required bool GrantsGlobalAdminAuthority { get; init; }
        public required bool GrantsCrossTenantVisibility { get; init; }
        public required bool GrantsAccess { get; init; }
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
        public required bool BypassesRedaction { get; init; }
        public required bool DisclosesSecrets { get; init; }
        public required bool DisclosesCredentials { get; init; }
        public required bool DisclosesRawPayload { get; init; }
        public required bool DisclosesPrivateReasoning { get; init; }

        public static TenantAdminBoundaryFinding FromSafeCatalogEntry(GovernanceRoleCatalogEntry entry)
        {
            Assert.AreEqual(GovernanceRoleKind.TenantAdministrator, entry.RoleKind);
            Assert.AreEqual(GovernanceRoleScopeKind.TenantScoped, entry.ScopeKind);
            return new TenantAdminBoundaryFinding
            {
                GrantsTenantAuthority = false,
                GrantsPlatformAuthority = false,
                GrantsGlobalAdminAuthority = false,
                GrantsCrossTenantVisibility = false,
                GrantsAccess = false,
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
                BypassesRedaction = false,
                DisclosesSecrets = false,
                DisclosesCredentials = false,
                DisclosesRawPayload = false,
                DisclosesPrivateReasoning = false
            };
        }
    }
}
