namespace IronDev.UnitTests.Governance;

internal static class GovernanceValidatorTestFixtures
{
    internal static GovernanceRoleCatalog RoleCatalog() => new RoleCatalogService().BuildDefaultCatalog();

    internal static GovernanceRoleCatalogEntry Role(GovernanceRoleKind kind) =>
        RoleCatalog().Entries.Single(entry => entry.RoleKind == kind);

    internal static GovernanceRoleCatalogEntry ReviewerRole() =>
        new()
        {
            RoleId = "role:f01:reviewer",
            CatalogVersion = "f01",
            RoleKind = GovernanceRoleKind.Reviewer,
            ScopeKind = GovernanceRoleScopeKind.ProjectScoped,
            DisplayName = "Reviewer",
            Description = "Names a responsibility type for proposal review.",
            ResponsibilitySummary = "May be referenced by future review profile contracts.",
            Surfaces = [GovernanceRoleSurface.Proposal],
            IsDeprecated = false,
            ReplacementRoleId = null,
            CreatedReason = "Block F01 canonical role vocabulary.",
            BoundaryStatement = "This role does not grant authority."
        };

    internal static GovernanceRoleCatalog Catalog(params GovernanceRoleCatalogEntry[] entries) =>
        new()
        {
            CatalogId = RoleCatalogService.DefaultCatalogId,
            CatalogVersion = RoleCatalogService.DefaultCatalogVersion,
            Entries = entries,
            CreatedReason = "Block F01 canonical role catalog contract.",
            BoundaryStatement = "The role catalog does not grant authority."
        };

    internal static RoleVisibilityMatrix VisibilityMatrix() =>
        new RoleVisibilityMatrixService().BuildDefaultMatrix(RoleCatalog());

    internal static RoleVisibilityMatrixEntry VisibilityEntry() =>
        new()
        {
            RoleId = "role:f01:reviewer",
            CatalogVersion = "f01",
            RoleKind = GovernanceRoleKind.Reviewer,
            RoleScopeKind = GovernanceRoleScopeKind.ProjectScoped,
            Surface = RoleVisibilitySurface.Proposal,
            MaterialKind = RoleVisibilityMaterialKind.ProposalSummary,
            SensitivityKind = RoleVisibilitySensitivityKind.Normal,
            VisibilityLevel = RoleVisibilityLevel.SummaryOnly,
            RequiresRedaction = false,
            RequiresSeparateRoleAssignment = true,
            RequiresSeparateVisibilityDecision = true,
            RequiresSeparatePolicyDecision = false,
            Reason = "Catalog only visibility hint for bounded summary.",
            BoundaryStatement = "This visibility hint does not grant access and does not grant authority."
        };

    internal static RoleVisibilityMatrix Matrix(params RoleVisibilityMatrixEntry[] entries) =>
        new()
        {
            MatrixId = RoleVisibilityMatrixService.DefaultMatrixId,
            CatalogId = RoleCatalogService.DefaultCatalogId,
            CatalogVersion = RoleCatalogService.DefaultCatalogVersion,
            Entries = entries,
            CreatedReason = "Block F02 role visibility matrix contract.",
            BoundaryStatement = "The role visibility matrix does not grant access and does not grant authority."
        };

    internal static ForbiddenActionCatalog ForbiddenActionCatalog() =>
        new ForbiddenActionCatalogService().BuildDefaultCatalog(RoleCatalog());

    internal static ForbiddenActionCatalogEntry ForbiddenActionEntry(
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

    internal static ForbiddenActionCatalog ForbiddenActionCatalog(params ForbiddenActionCatalogEntry[] entries) =>
        new()
        {
            CatalogId = "forbidden-action-catalog:f13",
            CatalogVersion = "f13",
            BoundaryStatement = "Forbidden action metadata is not authorization and not a permission grant.",
            Entries = entries
        };

    internal static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
