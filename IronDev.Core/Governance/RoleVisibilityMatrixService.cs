namespace IronDev.Core.Governance;

public sealed class RoleVisibilityMatrixService
{
    public const string DefaultMatrixId = "role-visibility-matrix:f02";

    public RoleVisibilityMatrix BuildDefaultMatrix(GovernanceRoleCatalog catalog)
    {
        var entries = new List<RoleVisibilityMatrixEntry>();

        Add(entries, catalog, GovernanceRoleKind.Requester,
            Hint(RoleVisibilitySurface.Planning, RoleVisibilityMaterialKind.PlanSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.Proposal, RoleVisibilityMaterialKind.ProposalSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.OperationStatus, RoleVisibilityMaterialKind.OperationStatusSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.ReceiptReadModel, RoleVisibilityMaterialKind.ReceiptMetadata, RoleVisibilityLevel.MetadataOnly));

        Add(entries, catalog, GovernanceRoleKind.Planner,
            Hint(RoleVisibilitySurface.Planning, RoleVisibilityMaterialKind.PlanSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.Proposal, RoleVisibilityMaterialKind.ProposalSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.ValidationReview, RoleVisibilityMaterialKind.ValidationSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.OperationStatus, RoleVisibilityMaterialKind.OperationStatusSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.ReceiptReadModel, RoleVisibilityMaterialKind.ReceiptMetadata, RoleVisibilityLevel.MetadataOnly));

        Add(entries, catalog, GovernanceRoleKind.Reviewer,
            Hint(RoleVisibilitySurface.Proposal, RoleVisibilityMaterialKind.ProposalSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.ValidationReview, RoleVisibilityMaterialKind.ValidationSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.ValidationReview, RoleVisibilityMaterialKind.ValidationEvidenceRefs, RoleVisibilityLevel.ReferenceOnly),
            Hint(RoleVisibilitySurface.PullRequest, RoleVisibilityMaterialKind.PullRequestMetadata, RoleVisibilityLevel.MetadataOnly),
            Hint(RoleVisibilitySurface.PullRequest, RoleVisibilityMaterialKind.PullRequestDiffSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.ReceiptReadModel, RoleVisibilityMaterialKind.ReceiptMetadata, RoleVisibilityLevel.MetadataOnly));

        Add(entries, catalog, GovernanceRoleKind.ApproverCandidate,
            Hint(RoleVisibilitySurface.ApprovalPackage, RoleVisibilityMaterialKind.ApprovalPackageSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.PolicyReview, RoleVisibilityMaterialKind.PolicyReviewSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.ValidationReview, RoleVisibilityMaterialKind.ValidationSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.ReceiptReadModel, RoleVisibilityMaterialKind.ReceiptMetadata, RoleVisibilityLevel.MetadataOnly));

        Add(entries, catalog, GovernanceRoleKind.PolicyOwnerCandidate,
            Hint(RoleVisibilitySurface.PolicyReview, RoleVisibilityMaterialKind.PolicyReviewSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.ApprovalPackage, RoleVisibilityMaterialKind.ApprovalPackageSummary, RoleVisibilityLevel.SummaryOnly),
            SensitiveHint(RoleVisibilitySurface.PolicyReview, RoleVisibilityMaterialKind.SensitiveFindingSummary),
            SensitiveHint(RoleVisibilitySurface.PolicyReview, RoleVisibilityMaterialKind.SecretScanSummary));

        Add(entries, catalog, GovernanceRoleKind.SecurityReviewer,
            Hint(RoleVisibilitySurface.ValidationReview, RoleVisibilityMaterialKind.ValidationSummary, RoleVisibilityLevel.SummaryOnly),
            SensitiveHint(RoleVisibilitySurface.ValidationReview, RoleVisibilityMaterialKind.SensitiveFindingSummary),
            SensitiveHint(RoleVisibilitySurface.ValidationReview, RoleVisibilityMaterialKind.SecretScanSummary),
            Hint(RoleVisibilitySurface.SourceApply, RoleVisibilityMaterialKind.PatchMetadata, RoleVisibilityLevel.MetadataOnly),
            Hint(RoleVisibilitySurface.PullRequest, RoleVisibilityMaterialKind.PullRequestDiffSummary, RoleVisibilityLevel.SummaryOnly));

        Add(entries, catalog, GovernanceRoleKind.ReleaseReviewer,
            Hint(RoleVisibilitySurface.MergeReadiness, RoleVisibilityMaterialKind.MergeReadinessSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.ReleaseReadiness, RoleVisibilityMaterialKind.ReleaseReadinessSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.DeploymentReadiness, RoleVisibilityMaterialKind.DeploymentReadinessSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.Audit, RoleVisibilityMaterialKind.AuditTrailSummary, RoleVisibilityLevel.SummaryOnly));

        Add(entries, catalog, GovernanceRoleKind.OperationsReviewer,
            Hint(RoleVisibilitySurface.DeploymentReadiness, RoleVisibilityMaterialKind.DeploymentReadinessSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.OperationStatus, RoleVisibilityMaterialKind.OperationStatusSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.Recovery, RoleVisibilityMaterialKind.RecoverySummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.Rollback, RoleVisibilityMaterialKind.RollbackSummary, RoleVisibilityLevel.SummaryOnly));

        Add(entries, catalog, GovernanceRoleKind.ExecutorOperatorCandidate,
            Hint(RoleVisibilitySurface.SourceApply, RoleVisibilityMaterialKind.SourceApplySummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.Commit, RoleVisibilityMaterialKind.CommitPackageSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.Push, RoleVisibilityMaterialKind.PushReceiptSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.OperationStatus, RoleVisibilityMaterialKind.OperationStatusSummary, RoleVisibilityLevel.SummaryOnly));

        Add(entries, catalog, GovernanceRoleKind.RollbackReviewer,
            Hint(RoleVisibilitySurface.Rollback, RoleVisibilityMaterialKind.RollbackSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.OperationStatus, RoleVisibilityMaterialKind.OperationStatusSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.ReceiptReadModel, RoleVisibilityMaterialKind.ReceiptMetadata, RoleVisibilityLevel.MetadataOnly));

        Add(entries, catalog, GovernanceRoleKind.RecoveryReviewer,
            Hint(RoleVisibilitySurface.Recovery, RoleVisibilityMaterialKind.RecoverySummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.Retry, RoleVisibilityMaterialKind.RetrySummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.OperationStatus, RoleVisibilityMaterialKind.OperationStatusSummary, RoleVisibilityLevel.SummaryOnly));

        Add(entries, catalog, GovernanceRoleKind.Auditor,
            Hint(RoleVisibilitySurface.Audit, RoleVisibilityMaterialKind.AuditTrailSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.ReceiptReadModel, RoleVisibilityMaterialKind.ReceiptMetadata, RoleVisibilityLevel.MetadataOnly),
            Hint(RoleVisibilitySurface.OperationStatus, RoleVisibilityMaterialKind.OperationStatusSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.PolicyReview, RoleVisibilityMaterialKind.PolicyReviewSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.ValidationReview, RoleVisibilityMaterialKind.ValidationSummary, RoleVisibilityLevel.SummaryOnly));

        Add(entries, catalog, GovernanceRoleKind.Observer,
            Hint(RoleVisibilitySurface.OperationStatus, RoleVisibilityMaterialKind.OperationStatusSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.ReceiptReadModel, RoleVisibilityMaterialKind.ReceiptMetadata, RoleVisibilityLevel.MetadataOnly));

        Add(entries, catalog, GovernanceRoleKind.AutomationAgent,
            Hint(RoleVisibilitySurface.OperationStatus, RoleVisibilityMaterialKind.OperationStatusSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.WorkflowContinuation, RoleVisibilityMaterialKind.WorkflowContinuationSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.ReceiptReadModel, RoleVisibilityMaterialKind.ReceiptMetadata, RoleVisibilityLevel.MetadataOnly));

        Add(entries, catalog, GovernanceRoleKind.SystemReadOnly,
            Hint(RoleVisibilitySurface.FrontendReadOnly, RoleVisibilityMaterialKind.OperationStatusSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.FrontendReadOnly, RoleVisibilityMaterialKind.ReceiptMetadata, RoleVisibilityLevel.MetadataOnly));

        Add(entries, catalog, GovernanceRoleKind.ExternalViewer,
            Hint(RoleVisibilitySurface.FrontendReadOnly, RoleVisibilityMaterialKind.OperationStatusSummary, RoleVisibilityLevel.MetadataOnly),
            Hint(RoleVisibilitySurface.OperationStatus, RoleVisibilityMaterialKind.OperationStatusSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.ReceiptReadModel, RoleVisibilityMaterialKind.ReceiptMetadata, RoleVisibilityLevel.MetadataOnly),
            Hint(RoleVisibilitySurface.ValidationReview, RoleVisibilityMaterialKind.ValidationSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.Proposal, RoleVisibilityMaterialKind.ProposalSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.ApprovalPackage, RoleVisibilityMaterialKind.ApprovalPackageSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.PolicyReview, RoleVisibilityMaterialKind.PolicyReviewSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.Audit, RoleVisibilityMaterialKind.AuditTrailSummary, RoleVisibilityLevel.SummaryOnly),
            Hint(RoleVisibilitySurface.ReleaseReadiness, RoleVisibilityMaterialKind.ReleaseReadinessSummary, RoleVisibilityLevel.SummaryOnly));

        foreach (var role in catalog.Entries)
        {
            entries.Add(Hidden(role, RoleVisibilityMaterialKind.RawPayload, RoleVisibilitySensitivityKind.RawPayload));
            entries.Add(Hidden(role, RoleVisibilityMaterialKind.CredentialMaterial, RoleVisibilitySensitivityKind.CredentialLike));
            entries.Add(Hidden(role, RoleVisibilityMaterialKind.PrivateReasoning, RoleVisibilitySensitivityKind.PrivateReasoning));
        }

        return new RoleVisibilityMatrix
        {
            MatrixId = DefaultMatrixId,
            CatalogId = catalog.CatalogId,
            CatalogVersion = catalog.CatalogVersion,
            Entries = entries
                .OrderBy(static entry => entry.RoleId, StringComparer.Ordinal)
                .ThenBy(static entry => entry.Surface)
                .ThenBy(static entry => entry.MaterialKind)
                .ToArray(),
            CreatedReason = "Block F02 role visibility matrix contract.",
            BoundaryStatement = "The role visibility matrix does not grant access and does not grant authority."
        };
    }

    public RoleVisibilityMatrixValidationResult ValidateDefaultMatrix(GovernanceRoleCatalog catalog) =>
        RoleVisibilityMatrixValidator.ValidateMatrix(catalog, BuildDefaultMatrix(catalog));

    public IReadOnlyList<RoleVisibilityMatrixEntry> FindEntriesByRoleId(
        RoleVisibilityMatrix matrix,
        string roleId) =>
        matrix.Entries
            .Where(entry => string.Equals(entry.RoleId, roleId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static entry => entry.Surface)
            .ThenBy(static entry => entry.MaterialKind)
            .ToArray();

    public IReadOnlyList<RoleVisibilityMatrixEntry> ListBySurface(
        RoleVisibilityMatrix matrix,
        RoleVisibilitySurface surface) =>
        matrix.Entries
            .Where(entry => entry.Surface == surface)
            .OrderBy(static entry => entry.RoleId, StringComparer.Ordinal)
            .ThenBy(static entry => entry.MaterialKind)
            .ToArray();

    public IReadOnlyList<RoleVisibilityMatrixEntry> ListByMaterial(
        RoleVisibilityMatrix matrix,
        RoleVisibilityMaterialKind materialKind) =>
        matrix.Entries
            .Where(entry => entry.MaterialKind == materialKind)
            .OrderBy(static entry => entry.RoleId, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Surface)
            .ToArray();

    public IReadOnlyList<RoleVisibilityMatrixEntry> ListByVisibilityLevel(
        RoleVisibilityMatrix matrix,
        RoleVisibilityLevel visibilityLevel) =>
        matrix.Entries
            .Where(entry => entry.VisibilityLevel == visibilityLevel)
            .OrderBy(static entry => entry.RoleId, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Surface)
            .ThenBy(static entry => entry.MaterialKind)
            .ToArray();

    private static void Add(
        ICollection<RoleVisibilityMatrixEntry> entries,
        GovernanceRoleCatalog catalog,
        GovernanceRoleKind roleKind,
        params VisibilityHint[] hints)
    {
        var role = catalog.Entries.First(entry => entry.RoleKind == roleKind);
        foreach (var hint in hints)
        {
            entries.Add(Entry(role, hint));
        }
    }

    private static RoleVisibilityMatrixEntry Entry(
        GovernanceRoleCatalogEntry role,
        VisibilityHint hint)
    {
        var requiresRedaction = RoleVisibilityMatrixValidator.RequiresRedaction(
            hint.MaterialKind,
            hint.SensitivityKind,
            hint.VisibilityLevel);

        return new RoleVisibilityMatrixEntry
        {
            RoleId = role.RoleId,
            CatalogVersion = role.CatalogVersion,
            RoleKind = role.RoleKind,
            RoleScopeKind = role.ScopeKind,
            Surface = hint.Surface,
            MaterialKind = hint.MaterialKind,
            SensitivityKind = hint.SensitivityKind,
            VisibilityLevel = hint.VisibilityLevel,
            RequiresRedaction = requiresRedaction,
            RequiresSeparateRoleAssignment = true,
            RequiresSeparateVisibilityDecision = true,
            RequiresSeparatePolicyDecision = requiresRedaction,
            Reason = "Catalog only read model hint for bounded visibility.",
            BoundaryStatement = "This visibility hint does not grant access and does not grant authority."
        };
    }

    private static RoleVisibilityMatrixEntry Hidden(
        GovernanceRoleCatalogEntry role,
        RoleVisibilityMaterialKind materialKind,
        RoleVisibilitySensitivityKind sensitivityKind) =>
        new()
        {
            RoleId = role.RoleId,
            CatalogVersion = role.CatalogVersion,
            RoleKind = role.RoleKind,
            RoleScopeKind = role.ScopeKind,
            Surface = RoleVisibilitySurface.FrontendReadOnly,
            MaterialKind = materialKind,
            SensitivityKind = sensitivityKind,
            VisibilityLevel = RoleVisibilityLevel.NotVisible,
            RequiresRedaction = true,
            RequiresSeparateRoleAssignment = true,
            RequiresSeparateVisibilityDecision = true,
            RequiresSeparatePolicyDecision = true,
            Reason = "Catalog blocks sensitive material by default.",
            BoundaryStatement = "This visibility hint does not grant access and does not grant authority."
        };

    private static VisibilityHint Hint(
        RoleVisibilitySurface surface,
        RoleVisibilityMaterialKind materialKind,
        RoleVisibilityLevel visibilityLevel) =>
        new(surface, materialKind, RoleVisibilitySensitivityKind.Normal, visibilityLevel);

    private static VisibilityHint SensitiveHint(
        RoleVisibilitySurface surface,
        RoleVisibilityMaterialKind materialKind) =>
        new(surface, materialKind, RoleVisibilitySensitivityKind.SecuritySensitive, RoleVisibilityLevel.RedactedDetails);

    private sealed record VisibilityHint(
        RoleVisibilitySurface Surface,
        RoleVisibilityMaterialKind MaterialKind,
        RoleVisibilitySensitivityKind SensitivityKind,
        RoleVisibilityLevel VisibilityLevel);
}
