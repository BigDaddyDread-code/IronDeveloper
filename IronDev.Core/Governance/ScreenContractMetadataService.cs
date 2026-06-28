namespace IronDev.Core.Governance;

public sealed class ScreenContractMetadataService
{
    public ScreenContractMetadataResponse GetMetadata(string? screenKey = null)
    {
        var catalog = BuildDefaultCatalog();
        var entries = string.IsNullOrWhiteSpace(screenKey)
            ? catalog.Entries
            : ScreenContractMetadataValidator.IsSafeScreenKey(screenKey)
                ? catalog.Entries
                    .Where(entry => string.Equals(entry.ScreenKey, screenKey, StringComparison.OrdinalIgnoreCase))
                    .ToArray()
                : [];

        return new ScreenContractMetadataResponse
        {
            CatalogId = catalog.CatalogId,
            CatalogVersion = catalog.CatalogVersion,
            BoundaryStatement = catalog.BoundaryStatement,
            Entries = entries
        };
    }

    public ScreenContractMetadataCatalog BuildDefaultCatalog() =>
        new()
        {
            CatalogId = "screen-contract-metadata:f12",
            CatalogVersion = "f12",
            BoundaryStatement = "Screen contract metadata is not UI authority and not screen permission.",
            Entries =
            [
                Entry(
                    "screen:f12:metadata-catalog",
                    "Screen contract metadata catalog",
                    "/governance/screen-contracts",
                    "governance",
                    ScreenContractKind.MetadataCatalog,
                    RoleVisibilitySurface.FrontendReadOnly,
                    RoleVisibilityMaterialKind.OperationStatusSummary,
                    ScreenContractSensitivityKind.InternalMetadata,
                    "endpoint:f12:screen-contract-metadata",
                    []),
                Entry(
                    "screen:f12:status-viewer",
                    "Operation status viewer",
                    "/operations/{operationId}/status",
                    "frontend-readiness",
                    ScreenContractKind.StatusViewer,
                    RoleVisibilitySurface.OperationStatus,
                    RoleVisibilityMaterialKind.OperationStatusSummary,
                    ScreenContractSensitivityKind.OperationScopedMetadata,
                    "endpoint:f11:status",
                    ["endpoint:f12:screen-contract-metadata"]),
                Entry(
                    "screen:f12:receipt-viewer",
                    "Receipt metadata viewer",
                    "/operations/{operationId}/receipts/{receiptRef}",
                    "frontend-readiness",
                    ScreenContractKind.ReceiptViewer,
                    RoleVisibilitySurface.ReceiptReadModel,
                    RoleVisibilityMaterialKind.ReceiptMetadata,
                    ScreenContractSensitivityKind.OperationScopedMetadata,
                    "endpoint:f11:receipt",
                    ["endpoint:f12:screen-contract-metadata"]),
                Entry(
                    "screen:f12:proposal-viewer",
                    "Proposal metadata viewer",
                    "/proposals/{proposalId}",
                    "frontend-readiness",
                    ScreenContractKind.ProposalViewer,
                    RoleVisibilitySurface.Proposal,
                    RoleVisibilityMaterialKind.ProposalSummary,
                    ScreenContractSensitivityKind.ProjectScopedMetadata,
                    "endpoint:f11:proposal",
                    ["endpoint:f12:screen-contract-metadata"]),
                Entry(
                    "screen:f12:approval-package-viewer",
                    "Approval package viewer",
                    "/approvals/{approvalPackageId}",
                    "governance",
                    ScreenContractKind.ApprovalPackageViewer,
                    RoleVisibilitySurface.ApprovalPackage,
                    RoleVisibilityMaterialKind.ApprovalPackageSummary,
                    ScreenContractSensitivityKind.RedactedSummary,
                    "endpoint:f11:approval",
                    ["endpoint:f12:screen-contract-metadata"]),
                Entry(
                    "screen:f12:policy-review-viewer",
                    "Policy review viewer",
                    "/policy-reviews/{policyReviewId}",
                    "governance",
                    ScreenContractKind.PolicyReviewViewer,
                    RoleVisibilitySurface.PolicyReview,
                    RoleVisibilityMaterialKind.PolicyReviewSummary,
                    ScreenContractSensitivityKind.SensitiveSummary,
                    "endpoint:f11:policy",
                    ["endpoint:f12:screen-contract-metadata"]),
                Entry(
                    "screen:f12:audit-viewer",
                    "Audit metadata viewer",
                    "/audit/{auditRef}",
                    "governance",
                    ScreenContractKind.AuditViewer,
                    RoleVisibilitySurface.Audit,
                    RoleVisibilityMaterialKind.AuditTrailSummary,
                    ScreenContractSensitivityKind.TenantScopedMetadata,
                    "endpoint:f11:audit",
                    ["endpoint:f12:screen-contract-metadata"]),
                Entry(
                    "screen:f12:validation-review-viewer",
                    "Validation review viewer",
                    "/validation-results/{validationResultId}",
                    "frontend-readiness",
                    ScreenContractKind.ValidationReviewViewer,
                    RoleVisibilitySurface.ValidationReview,
                    RoleVisibilityMaterialKind.ValidationSummary,
                    ScreenContractSensitivityKind.RedactedSummary,
                    "endpoint:f11:validation",
                    ["endpoint:f12:screen-contract-metadata"]),
                Entry(
                    "screen:f12:diagnostic-viewer",
                    "Diagnostic metadata viewer",
                    "/diagnostics/{diagnosticId}",
                    "support",
                    ScreenContractKind.DiagnosticViewer,
                    RoleVisibilitySurface.Recovery,
                    RoleVisibilityMaterialKind.RecoverySummary,
                    ScreenContractSensitivityKind.RedactedSummary,
                    "endpoint:f11:diagnostic",
                    ["endpoint:f12:screen-contract-metadata"]),
                Entry(
                    "screen:f12:release-readiness-viewer",
                    "Release readiness viewer",
                    "/release-readiness/{releaseReadinessId}",
                    "release",
                    ScreenContractKind.ReleaseReadinessViewer,
                    RoleVisibilitySurface.ReleaseReadiness,
                    RoleVisibilityMaterialKind.ReleaseReadinessSummary,
                    ScreenContractSensitivityKind.RedactedSummary,
                    "endpoint:f11:release-readiness",
                    ["endpoint:f12:screen-contract-metadata"]),
                Entry(
                    "screen:f12:external-redacted-viewer",
                    "External redacted viewer",
                    "/external/{shareId}/summary",
                    "governance",
                    ScreenContractKind.ExternalRedactedViewer,
                    RoleVisibilitySurface.FrontendReadOnly,
                    RoleVisibilityMaterialKind.OperationStatusSummary,
                    ScreenContractSensitivityKind.RedactedSummary,
                    "endpoint:f11:redacted",
                    ["endpoint:f12:screen-contract-metadata"]),
                Entry(
                    "screen:f12:action-request-viewer",
                    "Controlled action request viewer",
                    "/action-requests/{actionRequestId}",
                    "frontend-readiness",
                    ScreenContractKind.ActionRequestViewer,
                    RoleVisibilitySurface.FrontendReadOnly,
                    RoleVisibilityMaterialKind.OperationStatusSummary,
                    ScreenContractSensitivityKind.InternalMetadata,
                    "endpoint:f11:metadata",
                    ["endpoint:f12:screen-contract-metadata"]),
                Entry(
                    "screen:f12:admin-viewer",
                    "Admin metadata viewer",
                    "/admin/metadata/{adminRef}",
                    "admin",
                    ScreenContractKind.AdminViewer,
                    RoleVisibilitySurface.FrontendReadOnly,
                    RoleVisibilityMaterialKind.AuditTrailSummary,
                    ScreenContractSensitivityKind.InternalMetadata,
                    "endpoint:f11:metadata",
                    ["endpoint:f12:screen-contract-metadata"]),
                Entry(
                    "screen:f12:mutation-viewer",
                    "Mutation metadata viewer",
                    "/mutations/{mutationRef}",
                    "governance",
                    ScreenContractKind.MutationViewer,
                    RoleVisibilitySurface.SourceApply,
                    RoleVisibilityMaterialKind.SourceApplySummary,
                    ScreenContractSensitivityKind.InternalMetadata,
                    "endpoint:f11:mutation",
                    ["endpoint:f12:screen-contract-metadata"]),
                Entry(
                    "screen:f12:release-deploy-viewer",
                    "Release deployment metadata viewer",
                    "/release-deploy/{releaseDeployRef}",
                    "release",
                    ScreenContractKind.ReleaseDeployViewer,
                    RoleVisibilitySurface.DeploymentReadiness,
                    RoleVisibilityMaterialKind.DeploymentReadinessSummary,
                    ScreenContractSensitivityKind.InternalMetadata,
                    "endpoint:f11:deploy",
                    ["endpoint:f12:screen-contract-metadata"])
            ]
        };

    private static ScreenContractMetadataEntry Entry(
        string screenKey,
        string displayName,
        string frontendRoutePattern,
        string owningSubsystem,
        ScreenContractKind screenKind,
        RoleVisibilitySurface visibilitySurface,
        RoleVisibilityMaterialKind visibilityMaterialKind,
        ScreenContractSensitivityKind sensitivityKind,
        string primaryEndpointKey,
        IReadOnlyList<string> relatedEndpointKeys) =>
        new()
        {
            ScreenKey = screenKey,
            DisplayName = displayName,
            FrontendRoutePattern = frontendRoutePattern,
            OwningSubsystem = owningSubsystem,
            ScreenKind = screenKind,
            VisibilitySurface = visibilitySurface,
            VisibilityMaterialKind = visibilityMaterialKind,
            SensitivityKind = sensitivityKind,
            PrimaryEndpointKey = primaryEndpointKey,
            RelatedEndpointKeys = relatedEndpointKeys,
            RequiredEvidenceRefs =
            [
                "role-catalog:f12",
                "visibility-matrix:f12",
                "endpoint-capability-metadata:f12",
                "screen-contract-metadata:f12"
            ],
            BoundaryStatement = "Screen contract metadata is not UI authority and not screen permission.",
            IsReadOnly = true,
            IsActionScreen = false,
            IsMutationScreen = false,
            IsAdminScreen = false,
            IsReleaseDeployScreen = false,
            AllowsLocalAuthorityState = false,
            AllowsClientSidePermissionDecision = false,
            AllowsActionInvocation = false,
            AllowsMutation = false,
            AllowsWorkflowContinuation = false,
            AllowsApproval = false,
            AllowsPolicySatisfaction = false,
            AllowsRedactionBypass = false,
            AllowsRawPayloadDisplay = false,
            AllowsSecretDisplay = false,
            AllowsPrivateReasoningDisplay = false,
            RequiresSeparateRoleAssignment = true,
            RequiresSeparateVisibilityDecision = true,
            RequiresSeparateAccessDecision = true,
            RequiresSeparatePolicyDecision = true,
            RequiresSeparateRedactionDecision = true,
            RequiresTenantBoundaryDecision = true,
            RequiresSeparateActionAuthority = true,
            RequiresSeparateMutationAuthority = true,
            RequiresSeparateWorkflowAuthority = true
        };
}
