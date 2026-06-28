namespace IronDev.Core.Governance;

public sealed class RoleCatalogService
{
    public const string DefaultCatalogId = "role-catalog:f01";
    public const string DefaultCatalogVersion = "f01";

    public GovernanceRoleCatalog BuildDefaultCatalog() =>
        new()
        {
            CatalogId = DefaultCatalogId,
            CatalogVersion = DefaultCatalogVersion,
            CreatedReason = "Block F01 canonical role catalog contract.",
            BoundaryStatement = "The role catalog does not grant authority.",
            Entries =
            [
                Entry(GovernanceRoleKind.Requester, GovernanceRoleScopeKind.OperationScoped, "Requester", "Names the responsibility type that asks for governed work.", "May be referenced by future profile contracts as request context.", GovernanceRoleSurface.Planning, GovernanceRoleSurface.Proposal),
                Entry(GovernanceRoleKind.Planner, GovernanceRoleScopeKind.ProjectScoped, "Planner", "Names the responsibility type that prepares bounded plans.", "May be referenced by future planning profile contracts.", GovernanceRoleSurface.Planning, GovernanceRoleSurface.Proposal),
                Entry(GovernanceRoleKind.Reviewer, GovernanceRoleScopeKind.ProjectScoped, "Reviewer", "Names the responsibility type that reviews evidence or proposals.", "May be referenced by future review profile contracts without granting approval.", GovernanceRoleSurface.Proposal, GovernanceRoleSurface.ValidationReview, GovernanceRoleSurface.PullRequest),
                Entry(GovernanceRoleKind.ApproverCandidate, GovernanceRoleScopeKind.OperationScoped, "Approver Candidate", "Names a candidate responsibility for future approval profile evaluation.", "May be referenced by a future approval profile and does not approve work.", GovernanceRoleSurface.ApprovalProfile),
                Entry(GovernanceRoleKind.PolicyOwnerCandidate, GovernanceRoleScopeKind.ProjectScoped, "Policy Owner Candidate", "Names a candidate responsibility for future policy review.", "May be referenced by future policy review profiles and does not satisfy policy.", GovernanceRoleSurface.PolicyReview),
                Entry(GovernanceRoleKind.SecurityReviewer, GovernanceRoleScopeKind.RepositoryScoped, "Security Reviewer", "Names a responsibility type for security review.", "May be referenced by future security review profiles.", GovernanceRoleSurface.ValidationReview, GovernanceRoleSurface.SourceApply, GovernanceRoleSurface.PullRequest),
                Entry(GovernanceRoleKind.ReleaseReviewer, GovernanceRoleScopeKind.ReleaseScoped, "Release Reviewer", "Names a responsibility type for release readiness review.", "May be referenced by future release readiness profiles.", GovernanceRoleSurface.ReleaseReadiness),
                Entry(GovernanceRoleKind.OperationsReviewer, GovernanceRoleScopeKind.EnvironmentScoped, "Operations Reviewer", "Names a responsibility type for operational review.", "May be referenced by future deployment readiness profiles.", GovernanceRoleSurface.DeploymentReadiness),
                Entry(GovernanceRoleKind.ExecutorOperatorCandidate, GovernanceRoleScopeKind.OperationScoped, "Executor Operator Candidate", "Names a candidate responsibility for future executor operation review.", "May be referenced by future executor profile contracts and does not execute.", GovernanceRoleSurface.SourceApply, GovernanceRoleSurface.Commit, GovernanceRoleSurface.Push),
                Entry(GovernanceRoleKind.RollbackReviewer, GovernanceRoleScopeKind.OperationScoped, "Rollback Reviewer", "Names a responsibility type for rollback review.", "May be referenced by future rollback review profiles.", GovernanceRoleSurface.Rollback),
                Entry(GovernanceRoleKind.RecoveryReviewer, GovernanceRoleScopeKind.OperationScoped, "Recovery Reviewer", "Names a responsibility type for recovery review.", "May be referenced by future recovery review profiles.", GovernanceRoleSurface.Recovery, GovernanceRoleSurface.Retry),
                Entry(GovernanceRoleKind.Auditor, GovernanceRoleScopeKind.TenantScoped, "Auditor", "Names a responsibility type for audit review.", "May be referenced by future audit read profiles.", GovernanceRoleSurface.Audit, GovernanceRoleSurface.StatusReadModel),
                Entry(GovernanceRoleKind.TenantAdministrator, GovernanceRoleScopeKind.TenantScoped, "Tenant Administrator", "Tenant-scoped administrative responsibility marker for future governed visibility and boundary checks.", "May be referenced by future tenant-boundary profile contracts.", GovernanceRoleSurface.StatusReadModel, GovernanceRoleSurface.Audit, GovernanceRoleSurface.FrontendReadOnly),
                Entry(GovernanceRoleKind.Observer, GovernanceRoleScopeKind.ProjectScoped, "Observer", "Names a read-oriented responsibility type.", "May be referenced by future read-only profile contracts.", GovernanceRoleSurface.StatusReadModel, GovernanceRoleSurface.FrontendReadOnly),
                Entry(GovernanceRoleKind.AutomationAgent, GovernanceRoleScopeKind.WorkflowScoped, "Automation Agent", "Names a system participant responsibility type.", "May be referenced by future automation profile contracts and does not create autonomy.", GovernanceRoleSurface.WorkflowContinuation, GovernanceRoleSurface.StatusReadModel),
                Entry(GovernanceRoleKind.SystemReadOnly, GovernanceRoleScopeKind.GlobalCatalog, "System Read Only", "Names a read-only system responsibility type.", "May be referenced by future read-only backend profile contracts.", GovernanceRoleSurface.StatusReadModel, GovernanceRoleSurface.FrontendReadOnly),
                Entry(GovernanceRoleKind.SystemAccountabilityOwner, GovernanceRoleScopeKind.GlobalCatalog, "System Accountability Owner", "Names a system accountability responsibility marker for future governed visibility and boundary checks.", "May be referenced by future system-owner boundary profile contracts and does not grant controls.", GovernanceRoleSurface.StatusReadModel, GovernanceRoleSurface.Audit, GovernanceRoleSurface.FrontendReadOnly),
                Entry(GovernanceRoleKind.ExternalViewer, GovernanceRoleScopeKind.ProjectScoped, "External Viewer", "Names an external-facing read-only responsibility marker for future governed redaction and visibility checks.", "May be referenced by future external-viewer redaction profile contracts and remains evidence only.", GovernanceRoleSurface.StatusReadModel, GovernanceRoleSurface.FrontendReadOnly)
            ]
        };

    public GovernanceRoleCatalogValidationResult ValidateDefaultCatalog() =>
        RoleCatalogValidator.ValidateCatalog(BuildDefaultCatalog());

    public GovernanceRoleCatalogEntry? FindByRoleId(GovernanceRoleCatalog catalog, string roleId) =>
        catalog.Entries.FirstOrDefault(entry => string.Equals(entry.RoleId, roleId, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<GovernanceRoleCatalogEntry> ListBySurface(
        GovernanceRoleCatalog catalog,
        GovernanceRoleSurface surface) =>
        catalog.Entries
            .Where(entry => entry.Surfaces.Contains(surface))
            .OrderBy(static entry => entry.RoleId, StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyList<GovernanceRoleCatalogEntry> ListByKind(
        GovernanceRoleCatalog catalog,
        GovernanceRoleKind kind) =>
        catalog.Entries
            .Where(entry => entry.RoleKind == kind)
            .OrderBy(static entry => entry.RoleId, StringComparer.Ordinal)
            .ToArray();

    private static GovernanceRoleCatalogEntry Entry(
        GovernanceRoleKind kind,
        GovernanceRoleScopeKind scope,
        string displayName,
        string description,
        string responsibility,
        params GovernanceRoleSurface[] surfaces) =>
        new()
        {
            RoleId = $"role:{DefaultCatalogVersion}:{Slug(kind)}",
            CatalogVersion = DefaultCatalogVersion,
            RoleKind = kind,
            ScopeKind = scope,
            DisplayName = displayName,
            Description = description,
            ResponsibilitySummary = responsibility,
            Surfaces = surfaces,
            IsDeprecated = false,
            ReplacementRoleId = null,
            CreatedReason = "Block F01 canonical role vocabulary.",
            BoundaryStatement = "This role does not grant authority."
        };

    private static string Slug(GovernanceRoleKind kind) =>
        kind switch
        {
            GovernanceRoleKind.Requester => "requester",
            GovernanceRoleKind.Planner => "planner",
            GovernanceRoleKind.Reviewer => "reviewer",
            GovernanceRoleKind.ApproverCandidate => "approver-candidate",
            GovernanceRoleKind.PolicyOwnerCandidate => "policy-owner-candidate",
            GovernanceRoleKind.SecurityReviewer => "security-reviewer",
            GovernanceRoleKind.ReleaseReviewer => "release-reviewer",
            GovernanceRoleKind.OperationsReviewer => "operations-reviewer",
            GovernanceRoleKind.ExecutorOperatorCandidate => "executor-operator-candidate",
            GovernanceRoleKind.RollbackReviewer => "rollback-reviewer",
            GovernanceRoleKind.RecoveryReviewer => "recovery-reviewer",
            GovernanceRoleKind.Auditor => "auditor",
            GovernanceRoleKind.TenantAdministrator => "tenant-administrator",
            GovernanceRoleKind.Observer => "observer",
            GovernanceRoleKind.AutomationAgent => "automation-agent",
            GovernanceRoleKind.SystemReadOnly => "system-read-only",
            GovernanceRoleKind.SystemAccountabilityOwner => "system-accountability-owner",
            GovernanceRoleKind.ExternalViewer => "external-viewer",
            _ => "unknown"
        };
}
