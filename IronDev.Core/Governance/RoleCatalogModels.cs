namespace IronDev.Core.Governance;

public enum GovernanceRoleKind
{
    Unknown = 0,
    Requester = 1,
    Planner = 2,
    Reviewer = 3,
    ApproverCandidate = 4,
    PolicyOwnerCandidate = 5,
    SecurityReviewer = 6,
    ReleaseReviewer = 7,
    OperationsReviewer = 8,
    ExecutorOperatorCandidate = 9,
    RollbackReviewer = 10,
    RecoveryReviewer = 11,
    Auditor = 12,
    Observer = 13,
    AutomationAgent = 14,
    SystemReadOnly = 15
}

public enum GovernanceRoleScopeKind
{
    Unknown = 0,
    GlobalCatalog = 1,
    TenantScoped = 2,
    ProjectScoped = 3,
    RepositoryScoped = 4,
    OperationScoped = 5,
    WorkflowScoped = 6,
    ReleaseScoped = 7,
    EnvironmentScoped = 8
}

public enum GovernanceRoleSurface
{
    Unknown = 0,
    Planning = 1,
    Proposal = 2,
    ApprovalProfile = 3,
    PolicyReview = 4,
    ValidationReview = 5,
    SourceApply = 6,
    Commit = 7,
    Push = 8,
    PullRequest = 9,
    ReadyForReview = 10,
    MergeReadiness = 11,
    ReleaseReadiness = 12,
    DeploymentReadiness = 13,
    Rollback = 14,
    Retry = 15,
    Recovery = 16,
    WorkflowContinuation = 17,
    MemoryPromotion = 18,
    Audit = 19,
    StatusReadModel = 20,
    FrontendReadOnly = 21
}

public sealed record GovernanceRoleCatalogEntry
{
    public required string RoleId { get; init; }
    public required string CatalogVersion { get; init; }
    public required GovernanceRoleKind RoleKind { get; init; }
    public required GovernanceRoleScopeKind ScopeKind { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required string ResponsibilitySummary { get; init; }
    public required IReadOnlyList<GovernanceRoleSurface> Surfaces { get; init; }
    public required bool IsDeprecated { get; init; }
    public string? ReplacementRoleId { get; init; }
    public required string CreatedReason { get; init; }
    public required string BoundaryStatement { get; init; }
}

public sealed record GovernanceRoleCatalog
{
    public required string CatalogId { get; init; }
    public required string CatalogVersion { get; init; }
    public required IReadOnlyList<GovernanceRoleCatalogEntry> Entries { get; init; }
    public required string CreatedReason { get; init; }
    public required string BoundaryStatement { get; init; }
}

public sealed record GovernanceRoleCatalogValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
    public required IReadOnlyList<string> InvalidRoleIds { get; init; }
    public required IReadOnlyList<string> DuplicateRoleIds { get; init; }
    public required IReadOnlyList<string> UnsafeRoleIds { get; init; }
}
