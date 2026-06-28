using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockF10ExternalViewerRedactionRulesTests
{
    [TestMethod]
    public void ExternalViewerRolePreconditionIsSatisfiedByF10aCatalogRole()
    {
        var role = ExternalViewerRole();

        Assert.AreEqual(GovernanceRoleKind.ExternalViewer, role.RoleKind);
        Assert.AreEqual("role:f01:external-viewer", role.RoleId);
        StringAssert.Contains(role.Description, "external-facing read-only responsibility marker");
    }

    [TestMethod]
    public void ExternalViewerRoleEvidenceDoesNotGrantExternalAccessAssignmentOrVisibilityAuthority()
    {
        var decision = Classify(Request(ExternalViewerRedactionMaterialKind.PublicMetadata));

        Assert.AreEqual(ExternalViewerRedactionClassification.MetadataOnlyCandidate, decision.Classification);
        Assert.IsFalse(decision.GrantsExternalViewerAuthority);
        Assert.IsFalse(decision.GrantsRoleAssignmentAuthority);
        Assert.IsFalse(decision.GrantsVisibilityAuthority);
        Assert.IsFalse(decision.GrantsAccess);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ExternalViewerRoleEvidenceDoesNotCreateShareLinksOrRawExports()
    {
        var decision = Classify(Request(ExternalViewerRedactionMaterialKind.PublicMetadata));

        Assert.IsFalse(decision.CreatesShareLink);
        Assert.IsFalse(decision.ExportsRawData);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ExternalViewerRedactionDoesNotGrantAccessOrBypassRedaction()
    {
        var decision = Classify(Request(ExternalViewerRedactionMaterialKind.RedactedOperationStatusSummary));

        Assert.AreEqual(ExternalViewerRedactionClassification.RedactedSummaryCandidate, decision.Classification);
        Assert.IsFalse(decision.GrantsAccess);
        Assert.IsFalse(decision.BypassesRedaction);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(ExternalViewerRedactionMaterialKind.PublicMetadata)]
    [DataRow(ExternalViewerRedactionMaterialKind.TenantScopedMetadata)]
    [DataRow(ExternalViewerRedactionMaterialKind.ProjectScopedMetadata)]
    [DataRow(ExternalViewerRedactionMaterialKind.OperationStatusMetadata)]
    public void MetadataMaterialsCanBeMetadataOnlyCandidatesWithoutAuthority(
        ExternalViewerRedactionMaterialKind materialKind)
    {
        var decision = Classify(Request(materialKind));

        Assert.AreEqual(ExternalViewerRedactionClassification.MetadataOnlyCandidate, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.MetadataOnly, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(ExternalViewerRedactionMaterialKind.TenantScopedMetadata)]
    [DataRow(ExternalViewerRedactionMaterialKind.ProjectScopedMetadata)]
    public void ScopedMetadataRequiresTenantBoundaryEvidence(
        ExternalViewerRedactionMaterialKind materialKind)
    {
        var decision = Classify(Request(materialKind) with { OptionalTenantBoundaryEvidenceRef = null });

        Assert.AreEqual(ExternalViewerRedactionClassification.BlockedByMissingTenantBoundaryEvidence, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(ExternalViewerRedactionMaterialKind.RedactedOperationStatusSummary)]
    [DataRow(ExternalViewerRedactionMaterialKind.RedactedValidationSummary)]
    [DataRow(ExternalViewerRedactionMaterialKind.RedactedReviewSummary)]
    [DataRow(ExternalViewerRedactionMaterialKind.RedactedApprovalSummary)]
    [DataRow(ExternalViewerRedactionMaterialKind.RedactedDiagnosticSummary)]
    [DataRow(ExternalViewerRedactionMaterialKind.RedactedAuditSummary)]
    [DataRow(ExternalViewerRedactionMaterialKind.RedactedReleaseReadinessSummary)]
    [DataRow(ExternalViewerRedactionMaterialKind.RedactedPolicySummary)]
    [DataRow(ExternalViewerRedactionMaterialKind.RedactedErrorSummary)]
    [DataRow(ExternalViewerRedactionMaterialKind.RedactedLogSummary)]
    [DataRow(ExternalViewerRedactionMaterialKind.RedactedReceiptSummary)]
    public void EveryRedactedSummaryRequiresRedactionEvidence(
        ExternalViewerRedactionMaterialKind materialKind)
    {
        var decision = Classify(Request(materialKind) with { OptionalRedactionEvidenceRef = null });

        Assert.AreEqual(ExternalViewerRedactionClassification.BlockedByMissingRedactionEvidence, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(ExternalViewerRedactionMaterialKind.RedactedOperationStatusSummary)]
    [DataRow(ExternalViewerRedactionMaterialKind.RedactedValidationSummary)]
    [DataRow(ExternalViewerRedactionMaterialKind.RedactedReviewSummary)]
    [DataRow(ExternalViewerRedactionMaterialKind.RedactedApprovalSummary)]
    [DataRow(ExternalViewerRedactionMaterialKind.RedactedDiagnosticSummary)]
    [DataRow(ExternalViewerRedactionMaterialKind.RedactedAuditSummary)]
    [DataRow(ExternalViewerRedactionMaterialKind.RedactedReleaseReadinessSummary)]
    [DataRow(ExternalViewerRedactionMaterialKind.RedactedPolicySummary)]
    [DataRow(ExternalViewerRedactionMaterialKind.RedactedErrorSummary)]
    [DataRow(ExternalViewerRedactionMaterialKind.RedactedLogSummary)]
    [DataRow(ExternalViewerRedactionMaterialKind.RedactedReceiptSummary)]
    public void RedactedSummariesAreOnlyRedactedSummaryCandidates(
        ExternalViewerRedactionMaterialKind materialKind)
    {
        var decision = Classify(Request(materialKind));

        Assert.AreEqual(ExternalViewerRedactionClassification.RedactedSummaryCandidate, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.RedactedDetails, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void OperationStatusSummaryDoesNotContinueWorkflow()
    {
        var decision = Classify(Request(ExternalViewerRedactionMaterialKind.RedactedOperationStatusSummary));

        Assert.IsFalse(decision.GrantsWorkflowContinuation);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ValidationSummaryDoesNotRefreshValidation()
    {
        var decision = Classify(Request(ExternalViewerRedactionMaterialKind.RedactedValidationSummary));

        Assert.IsFalse(decision.RefreshesValidation);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ReviewSummaryDoesNotGrantReviewerAuthority()
    {
        var decision = Classify(Request(ExternalViewerRedactionMaterialKind.RedactedReviewSummary));

        Assert.IsFalse(decision.GrantsVisibilityAuthority);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ApprovalSummaryDoesNotGrantApprovalAuthority()
    {
        var decision = Classify(Request(ExternalViewerRedactionMaterialKind.RedactedApprovalSummary));

        Assert.IsFalse(decision.GrantsApprovalAuthority);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void PolicySummaryDoesNotSatisfyPolicy()
    {
        var decision = Classify(Request(ExternalViewerRedactionMaterialKind.RedactedPolicySummary));

        Assert.IsFalse(decision.SatisfiesPolicy);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void DiagnosticSummaryDoesNotGrantDiagnosticExecution()
    {
        var decision = Classify(Request(ExternalViewerRedactionMaterialKind.RedactedDiagnosticSummary));

        Assert.IsFalse(decision.GrantsDiagnosticExecutionAuthority);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ReleaseReadinessSummaryDoesNotGrantReleaseAuthority()
    {
        var decision = Classify(Request(ExternalViewerRedactionMaterialKind.RedactedReleaseReadinessSummary));

        Assert.IsFalse(decision.GrantsReleaseAuthority);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ReceiptSummaryDoesNotGrantDownstreamAuthority()
    {
        var decision = Classify(Request(ExternalViewerRedactionMaterialKind.RedactedReceiptSummary));

        Assert.IsFalse(decision.GrantsMutationAuthority);
        Assert.IsFalse(decision.GrantsMergeAuthority);
        Assert.IsFalse(decision.GrantsReleaseAuthority);
        Assert.IsFalse(decision.GrantsDeploymentAuthority);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(ExternalViewerRedactionMaterialKind.RawPayload)]
    [DataRow(ExternalViewerRedactionMaterialKind.RawProviderResponse)]
    [DataRow(ExternalViewerRedactionMaterialKind.RawSource)]
    [DataRow(ExternalViewerRedactionMaterialKind.RawDiff)]
    [DataRow(ExternalViewerRedactionMaterialKind.RawPatch)]
    [DataRow(ExternalViewerRedactionMaterialKind.RawLog)]
    public void RawMaterialsAreAlwaysNotVisible(ExternalViewerRedactionMaterialKind materialKind)
    {
        var decision = Classify(Request(materialKind));

        Assert.AreEqual(ExternalViewerRedactionClassification.BlockedByRawMaterial, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(ExternalViewerRedactionMaterialKind.CredentialMaterial, ExternalViewerRedactionClassification.BlockedByCredentialMaterial)]
    [DataRow(ExternalViewerRedactionMaterialKind.SecretMaterial, ExternalViewerRedactionClassification.BlockedBySecretMaterial)]
    [DataRow(ExternalViewerRedactionMaterialKind.PrivateReasoning, ExternalViewerRedactionClassification.BlockedByPrivateReasoningMaterial)]
    public void CredentialSecretAndPrivateReasoningMaterialsAreAlwaysNotVisible(
        ExternalViewerRedactionMaterialKind materialKind,
        ExternalViewerRedactionClassification expected)
    {
        var decision = Classify(Request(materialKind));

        Assert.AreEqual(expected, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void AuthorityMarkerMaterialIsBlocked()
    {
        var decision = Classify(Request(ExternalViewerRedactionMaterialKind.AuthorityMarker));

        Assert.AreEqual(ExternalViewerRedactionClassification.BlockedByAuthorityMarker, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(ExternalViewerRedactionMaterialKind.ApprovalRecord, ExternalViewerRedactionClassification.BlockedByApprovalMaterial)]
    [DataRow(ExternalViewerRedactionMaterialKind.PolicySatisfactionRecord, ExternalViewerRedactionClassification.BlockedByPolicyMaterial)]
    public void ApprovalAndPolicyRecordsAreBlockedUnlessRepresentedAsRedactedSummaries(
        ExternalViewerRedactionMaterialKind materialKind,
        ExternalViewerRedactionClassification expected)
    {
        var decision = Classify(Request(materialKind));

        Assert.AreEqual(expected, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(ExternalViewerRedactionMaterialKind.SourcePatch)]
    [DataRow(ExternalViewerRedactionMaterialKind.CommitPackage)]
    [DataRow(ExternalViewerRedactionMaterialKind.PushReceipt)]
    [DataRow(ExternalViewerRedactionMaterialKind.PullRequestMutationReceipt)]
    public void MutationAdjacentMaterialsAreBlockedUnlessRepresentedAsRedactedReceiptSummary(
        ExternalViewerRedactionMaterialKind materialKind)
    {
        var decision = Classify(Request(materialKind));

        Assert.AreEqual(ExternalViewerRedactionClassification.BlockedByMutationMaterial, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ReleaseDeployReceiptMaterialIsBlockedUnlessRepresentedAsRedactedReceiptSummary()
    {
        var decision = Classify(Request(ExternalViewerRedactionMaterialKind.ReleaseOrDeployReceipt));

        Assert.AreEqual(ExternalViewerRedactionClassification.BlockedByReleaseDeployMaterial, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(ExternalViewerRedactionRequestedIntent.GrantExternalAccess)]
    [DataRow(ExternalViewerRedactionRequestedIntent.CreateShareLink)]
    [DataRow(ExternalViewerRedactionRequestedIntent.ExportRawData)]
    [DataRow(ExternalViewerRedactionRequestedIntent.ViewRawPayload)]
    [DataRow(ExternalViewerRedactionRequestedIntent.ViewSecrets)]
    [DataRow(ExternalViewerRedactionRequestedIntent.ViewCredentials)]
    [DataRow(ExternalViewerRedactionRequestedIntent.ViewPrivateReasoning)]
    [DataRow(ExternalViewerRedactionRequestedIntent.BypassRedaction)]
    [DataRow(ExternalViewerRedactionRequestedIntent.CrossTenantVisibility)]
    [DataRow(ExternalViewerRedactionRequestedIntent.PlatformVisibility)]
    public void ExternalAccessRawDisclosureAndRedactionBypassIntentsAreBlocked(
        ExternalViewerRedactionRequestedIntent intent)
    {
        var decision = Classify(Request(ExternalViewerRedactionMaterialKind.PublicMetadata) with
        {
            RequestedIntent = intent
        });

        Assert.AreEqual(ExternalViewerRedactionClassification.BlockedByAccessIntent, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(ExternalViewerRedactionRequestedIntent.ApprovalAuthority)]
    [DataRow(ExternalViewerRedactionRequestedIntent.PolicySatisfaction)]
    [DataRow(ExternalViewerRedactionRequestedIntent.ValidationRefresh)]
    [DataRow(ExternalViewerRedactionRequestedIntent.SourceSafetyProof)]
    [DataRow(ExternalViewerRedactionRequestedIntent.DiagnosticExecution)]
    [DataRow(ExternalViewerRedactionRequestedIntent.RetryAuthority)]
    [DataRow(ExternalViewerRedactionRequestedIntent.RollbackAuthority)]
    [DataRow(ExternalViewerRedactionRequestedIntent.RecoveryAuthority)]
    [DataRow(ExternalViewerRedactionRequestedIntent.MutationAuthority)]
    [DataRow(ExternalViewerRedactionRequestedIntent.WorkflowContinuation)]
    [DataRow(ExternalViewerRedactionRequestedIntent.MergeAuthority)]
    [DataRow(ExternalViewerRedactionRequestedIntent.ReleaseAuthority)]
    [DataRow(ExternalViewerRedactionRequestedIntent.DeploymentAuthority)]
    public void AuthorityAndActionIntentsAreBlocked(ExternalViewerRedactionRequestedIntent intent)
    {
        var decision = Classify(Request(ExternalViewerRedactionMaterialKind.PublicMetadata) with
        {
            RequestedIntent = intent
        });

        Assert.AreEqual(ExternalViewerRedactionClassification.BlockedByActionIntent, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnknownMaterialFailsClosed()
    {
        var decision = Classify(Request(ExternalViewerRedactionMaterialKind.Unknown));

        Assert.AreEqual(ExternalViewerRedactionClassification.BlockedByUnknownMaterial, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnknownIntentFailsClosed()
    {
        var decision = Classify(Request(ExternalViewerRedactionMaterialKind.PublicMetadata) with
        {
            RequestedIntent = ExternalViewerRedactionRequestedIntent.Unknown
        });

        Assert.AreEqual(ExternalViewerRedactionClassification.BlockedByUnknownIntent, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void NonExternalViewerRoleFailsClosed()
    {
        var observer = Catalog().Entries.Single(entry => entry.RoleKind == GovernanceRoleKind.Observer);
        var decision = Classify(Request(ExternalViewerRedactionMaterialKind.PublicMetadata) with
        {
            RequestedRoleKey = observer.RoleId
        });

        Assert.AreEqual(ExternalViewerRedactionClassification.BlockedByNonExternalViewerRole, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void MissingCatalogEvidenceFailsClosed()
    {
        var decision = Classify(Request(ExternalViewerRedactionMaterialKind.PublicMetadata) with
        {
            RoleCatalogEvidenceRef = string.Empty
        });

        Assert.AreEqual(ExternalViewerRedactionClassification.BlockedByMissingCatalogEvidence, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void MissingMatrixEvidenceFailsClosed()
    {
        var decision = Classify(Request(ExternalViewerRedactionMaterialKind.PublicMetadata) with
        {
            VisibilityMatrixEvidenceRef = string.Empty
        });

        Assert.AreEqual(ExternalViewerRedactionClassification.BlockedByMissingMatrixEvidence, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void MissingSourceEvidenceFailsClosed()
    {
        var decision = Classify(Request(ExternalViewerRedactionMaterialKind.PublicMetadata) with
        {
            SourceEvidenceRef = string.Empty
        });

        Assert.AreEqual(ExternalViewerRedactionClassification.BlockedByMissingSourceEvidence, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void MatrixDenialReturnsHiddenNotVisible()
    {
        var decision = Classify(Request(ExternalViewerRedactionMaterialKind.PublicMetadata) with
        {
            RequestedSurface = RoleVisibilitySurface.DeploymentReadiness
        });

        Assert.AreEqual(ExternalViewerRedactionClassification.Hidden, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void HostileExternalViewerTextIsHiddenAndDoesNotEchoRawMarker()
    {
        var unsafeMarker = "ExternalAccessGranted = true";
        var decision = Classify(Request(ExternalViewerRedactionMaterialKind.PublicMetadata) with
        {
            SourceEvidenceRef = unsafeMarker
        });

        Assert.AreEqual(ExternalViewerRedactionClassification.Hidden, decision.Classification);
        CollectionAssert.Contains(decision.EvidenceRefs.ToList(), "[unsafe-rejected]");
        Assert.IsFalse(decision.RecordFingerprint.Contains(unsafeMarker, StringComparison.OrdinalIgnoreCase));
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow("external-redaction-source:f10")]
    [DataRow("redacted-summary:f10")]
    [DataRow("tenant-boundary-evidence:f10")]
    [DataRow("role-catalog:f10")]
    [DataRow("visibility-matrix:f10")]
    public void SafeExternalViewerEvidenceRefsAreNotRejected(string value)
    {
        Assert.IsFalse(ExternalViewerRedactionValidator.ContainsUnsafeEvidenceText(value));
    }

    [TestMethod]
    public void NullRequestValidationFailsClosed()
    {
        var result = ExternalViewerRedactionValidator.ValidateRequest(null);

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToList(), "ExternalViewerRedactionRequestRequired");
    }

    [TestMethod]
    public void EveryDecisionHasAllAuthorityAndDisclosureFlagsFalse()
    {
        var requests = new[]
        {
            Request(ExternalViewerRedactionMaterialKind.PublicMetadata),
            Request(ExternalViewerRedactionMaterialKind.TenantScopedMetadata) with { OptionalTenantBoundaryEvidenceRef = null },
            Request(ExternalViewerRedactionMaterialKind.RedactedLogSummary) with { OptionalRedactionEvidenceRef = null },
            Request(ExternalViewerRedactionMaterialKind.RawPayload),
            Request(ExternalViewerRedactionMaterialKind.AuthorityMarker),
            Request(ExternalViewerRedactionMaterialKind.PublicMetadata) with { RequestedIntent = ExternalViewerRedactionRequestedIntent.CreateShareLink },
            Request(ExternalViewerRedactionMaterialKind.Unknown),
            Request(ExternalViewerRedactionMaterialKind.PublicMetadata) with { SourceEvidenceRef = string.Empty }
        };

        foreach (var request in requests)
        {
            AssertAuthorityFlagsFalse(Classify(request));
        }
    }

    [TestMethod]
    public void DecisionModelCarriesRequiredFalseAuthorityAndDisclosureFields()
    {
        var decision = Classify(Request(ExternalViewerRedactionMaterialKind.PublicMetadata));

        Assert.IsFalse(decision.GrantsExternalViewerAuthority);
        Assert.IsFalse(decision.GrantsRoleAssignmentAuthority);
        Assert.IsFalse(decision.GrantsVisibilityAuthority);
        Assert.IsFalse(decision.GrantsAccess);
        Assert.IsFalse(decision.CreatesShareLink);
        Assert.IsFalse(decision.ExportsRawData);
        Assert.IsFalse(decision.GrantsCrossTenantVisibility);
        Assert.IsFalse(decision.GrantsPlatformVisibility);
        Assert.IsFalse(decision.GrantsApprovalAuthority);
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
        Assert.IsFalse(decision.DisclosesRawSource);
        Assert.IsFalse(decision.DisclosesRawLogs);
        Assert.IsFalse(decision.DisclosesPrivateReasoning);
    }

    [TestMethod]
    public void StaticScanF10CoreAddsNoIdentityPermissionProviderExportShareOrMutationSurface()
    {
        var source = F10CoreSourceWithoutStrings();
        var forbidden = new[]
        {
            "UserId",
            "PrincipalId",
            "GroupId",
            "AccessToken",
            "ShareToken",
            "ExternalEmail",
            "InviteLink",
            "ClaimsPrincipal",
            "PermissionResolver",
            "IdentityLookup",
            "PrincipalLookup",
            "GroupMembership",
            "Collaborator",
            "ApiController",
            "ControllerBase",
            "DbContext",
            "SqlConnection",
            "IRepository",
            "Repository<",
            "Store<",
            "ProviderClient",
            "ExportAsync",
            "ShareLinkService",
            "DiagnosticGateway",
            "RetryExecutor",
            "RollbackExecutor",
            "RecoveryExecutor",
            "ValidationRunner",
            "RunProcessAsync",
            "ProcessStartInfo",
            "HttpClient",
            "File.Write",
            "Directory.",
            "git ",
            "gh ",
            "ExecuteAsync",
            "ApplyAsync",
            "CommitAsync",
            "PushAsync",
            "MergeAsync",
            "ReleaseAsync",
            "DeployAsync"
        };

        foreach (var marker in forbidden)
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    [TestMethod]
    public void ReceiptExistsAndStatesExternalViewerRedactionIsNotAccessAuthority()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "F10_EXTERNAL_VIEWER_REDACTION_RULES.md"));

        StringAssert.Contains(receipt, "External viewer redaction is not access authority.");
        StringAssert.Contains(receipt, "A redacted view is not a permission slip.");
        StringAssert.Contains(receipt, "does not implement external viewer identity");
        StringAssert.Contains(receipt, "does not implement external access grant");
        StringAssert.Contains(receipt, "does not implement share links");
        StringAssert.Contains(receipt, "does not implement raw exports");
        StringAssert.Contains(receipt, "does not implement redaction bypass");
    }

    private static ExternalViewerRedactionDecision Classify(
        ExternalViewerRedactionRequest request) =>
        new ExternalViewerRedactionService().Classify(Catalog(), Matrix(), request);

    private static ExternalViewerRedactionRequest Request(
        ExternalViewerRedactionMaterialKind materialKind)
    {
        var role = ExternalViewerRole();
        return new ExternalViewerRedactionRequest
        {
            CorrelationId = "correlation-f10",
            RequestedRoleKey = role.RoleId,
            RequestedSurface = SurfaceFor(materialKind),
            RequestedMaterialKind = materialKind,
            RequestedIntent = ExternalViewerRedactionRequestedIntent.ReadOnlyInspect,
            SourceEvidenceRef = "external-redaction-source:f10",
            RoleCatalogEvidenceRef = "role-catalog:f10",
            VisibilityMatrixEvidenceRef = "visibility-matrix:f10",
            OptionalPolicyEvidenceRef = "policy-evidence:f10",
            OptionalRedactionEvidenceRef = "redaction-evidence:f10",
            OptionalTenantBoundaryEvidenceRef = "tenant-boundary-evidence:f10"
        };
    }

    private static RoleVisibilitySurface SurfaceFor(
        ExternalViewerRedactionMaterialKind materialKind) =>
        materialKind switch
        {
            ExternalViewerRedactionMaterialKind.RedactedValidationSummary => RoleVisibilitySurface.ValidationReview,
            ExternalViewerRedactionMaterialKind.RedactedReviewSummary => RoleVisibilitySurface.Proposal,
            ExternalViewerRedactionMaterialKind.RedactedApprovalSummary => RoleVisibilitySurface.ApprovalPackage,
            ExternalViewerRedactionMaterialKind.RedactedAuditSummary => RoleVisibilitySurface.Audit,
            ExternalViewerRedactionMaterialKind.RedactedReleaseReadinessSummary => RoleVisibilitySurface.ReleaseReadiness,
            ExternalViewerRedactionMaterialKind.RedactedPolicySummary => RoleVisibilitySurface.PolicyReview,
            ExternalViewerRedactionMaterialKind.RedactedReceiptSummary => RoleVisibilitySurface.ReceiptReadModel,
            ExternalViewerRedactionMaterialKind.OperationStatusMetadata or
            ExternalViewerRedactionMaterialKind.RedactedOperationStatusSummary or
            ExternalViewerRedactionMaterialKind.RedactedDiagnosticSummary or
            ExternalViewerRedactionMaterialKind.RedactedErrorSummary or
            ExternalViewerRedactionMaterialKind.RedactedLogSummary => RoleVisibilitySurface.OperationStatus,
            _ => RoleVisibilitySurface.FrontendReadOnly
        };

    private static GovernanceRoleCatalog Catalog() => new RoleCatalogService().BuildDefaultCatalog();

    private static RoleVisibilityMatrix Matrix() =>
        new RoleVisibilityMatrixService().BuildDefaultMatrix(Catalog());

    private static GovernanceRoleCatalogEntry ExternalViewerRole() =>
        Catalog().Entries.Single(entry => entry.RoleKind == GovernanceRoleKind.ExternalViewer);

    private static void AssertAuthorityFlagsFalse(ExternalViewerRedactionDecision decision)
    {
        Assert.IsFalse(decision.GrantsExternalViewerAuthority);
        Assert.IsFalse(decision.GrantsRoleAssignmentAuthority);
        Assert.IsFalse(decision.GrantsVisibilityAuthority);
        Assert.IsFalse(decision.GrantsAccess);
        Assert.IsFalse(decision.CreatesShareLink);
        Assert.IsFalse(decision.ExportsRawData);
        Assert.IsFalse(decision.GrantsCrossTenantVisibility);
        Assert.IsFalse(decision.GrantsPlatformVisibility);
        Assert.IsFalse(decision.GrantsApprovalAuthority);
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
        Assert.IsFalse(decision.DisclosesRawSource);
        Assert.IsFalse(decision.DisclosesRawLogs);
        Assert.IsFalse(decision.DisclosesPrivateReasoning);
    }

    private static string F10CoreSourceWithoutStrings()
    {
        var root = RepoRoot();
        var files = Directory.GetFiles(Path.Combine(root, "IronDev.Core", "Governance"), "ExternalViewerRedaction*.cs")
            .Concat([Path.Combine(root, "IronDev.Core", "Governance", "RoleVisibilityMatrixService.cs")]);
        return string.Join(Environment.NewLine, files.Select(path => StripStringLiterals(File.ReadAllText(path))));
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

        return directory?.FullName ?? throw new InvalidOperationException("Unable to locate repository root.");
    }
}
