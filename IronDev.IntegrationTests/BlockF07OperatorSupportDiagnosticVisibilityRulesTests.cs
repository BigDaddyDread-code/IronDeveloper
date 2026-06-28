using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockF07OperatorSupportDiagnosticVisibilityRulesTests
{
    [TestMethod]
    public void OperationStatusMetadataDoesNotGrantOperatorAuthority()
    {
        var decision = Classify(Request(OperatorSupportDiagnosticMaterialKind.OperationStatusMetadata));

        Assert.AreEqual(OperatorSupportDiagnosticVisibilityClassification.MetadataOnlyCandidate, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.MetadataOnly, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void OperationStatusSummaryDoesNotGrantSupportAuthority()
    {
        var decision = Classify(Request(OperatorSupportDiagnosticMaterialKind.OperationStatusSummary));

        Assert.AreEqual(OperatorSupportDiagnosticVisibilityClassification.SummaryCandidate, decision.Classification);
        Assert.IsFalse(decision.GrantsSupportAuthority);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ValidationSummaryDoesNotRefreshValidation()
    {
        var decision = Classify(Request(OperatorSupportDiagnosticMaterialKind.ValidationSummary));

        Assert.AreEqual(OperatorSupportDiagnosticVisibilityClassification.SummaryCandidate, decision.Classification);
        Assert.IsFalse(decision.RefreshesValidation);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void FailureClassificationSummaryDoesNotAuthorizeRetry()
    {
        var decision = Classify(Request(OperatorSupportDiagnosticMaterialKind.FailureClassificationSummary));

        Assert.AreEqual(OperatorSupportDiagnosticVisibilityClassification.SummaryCandidate, decision.Classification);
        Assert.IsFalse(decision.GrantsRetryAuthority);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void RetryClassificationSummaryDoesNotExecuteRetry()
    {
        var decision = Classify(Request(OperatorSupportDiagnosticMaterialKind.RetryClassificationSummary));

        Assert.AreEqual(OperatorSupportDiagnosticVisibilityClassification.SummaryCandidate, decision.Classification);
        Assert.IsFalse(decision.GrantsRetryAuthority);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void RollbackReadinessSummaryDoesNotExecuteRollback()
    {
        var decision = Classify(Request(OperatorSupportDiagnosticMaterialKind.RollbackReadinessSummary));

        Assert.AreEqual(OperatorSupportDiagnosticVisibilityClassification.SummaryCandidate, decision.Classification);
        Assert.IsFalse(decision.GrantsRollbackAuthority);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void RecoveryRecommendationSummaryDoesNotExecuteRecovery()
    {
        var decision = Classify(Request(OperatorSupportDiagnosticMaterialKind.RecoveryRecommendationSummary));

        Assert.AreEqual(OperatorSupportDiagnosticVisibilityClassification.RedactedSummaryCandidate, decision.Classification);
        Assert.IsFalse(decision.GrantsRecoveryAuthority);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void DependencyHealthSummaryDoesNotGrantAccess()
    {
        var decision = Classify(Request(OperatorSupportDiagnosticMaterialKind.DependencyHealthSummary));

        Assert.AreEqual(OperatorSupportDiagnosticVisibilityClassification.SummaryCandidate, decision.Classification);
        Assert.IsFalse(decision.GrantsAccess);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void EnvironmentReadinessSummaryDoesNotProveSourceSafety()
    {
        var decision = Classify(Request(OperatorSupportDiagnosticMaterialKind.EnvironmentReadinessSummary));

        Assert.AreEqual(OperatorSupportDiagnosticVisibilityClassification.SummaryCandidate, decision.Classification);
        Assert.IsFalse(decision.ProvesSourceSafety);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void QueueOrRunnerStateSummaryDoesNotContinueWorkflow()
    {
        var decision = Classify(Request(OperatorSupportDiagnosticMaterialKind.QueueOrRunnerStateSummary));

        Assert.AreEqual(OperatorSupportDiagnosticVisibilityClassification.SummaryCandidate, decision.Classification);
        Assert.IsFalse(decision.GrantsWorkflowContinuation);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(OperatorSupportDiagnosticMaterialKind.RedactedErrorSummary)]
    [DataRow(OperatorSupportDiagnosticMaterialKind.RedactedLogSummary)]
    [DataRow(OperatorSupportDiagnosticMaterialKind.RedactedDiagnosticRationaleSummary)]
    public void RedactedDiagnosticSummariesRequireRedactionEvidence(
        OperatorSupportDiagnosticMaterialKind materialKind)
    {
        var decision = Classify(Request(materialKind) with { OptionalRedactionEvidenceRef = null });

        Assert.AreEqual(OperatorSupportDiagnosticVisibilityClassification.BlockedByMissingRedactionEvidence, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(OperatorSupportDiagnosticMaterialKind.RedactedErrorSummary)]
    [DataRow(OperatorSupportDiagnosticMaterialKind.RedactedLogSummary)]
    [DataRow(OperatorSupportDiagnosticMaterialKind.RedactedDiagnosticRationaleSummary)]
    public void RedactedDiagnosticSummariesAreOnlyRedactedSummaryCandidatesWithRedactionEvidence(
        OperatorSupportDiagnosticMaterialKind materialKind)
    {
        var decision = Classify(Request(materialKind));

        Assert.AreEqual(OperatorSupportDiagnosticVisibilityClassification.RedactedSummaryCandidate, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.RedactedDetails, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(OperatorSupportDiagnosticMaterialKind.RawLog, OperatorSupportDiagnosticVisibilityClassification.BlockedByRawLogMaterial)]
    [DataRow(OperatorSupportDiagnosticMaterialKind.RawPayload, OperatorSupportDiagnosticVisibilityClassification.BlockedByRawPayloadMaterial)]
    [DataRow(OperatorSupportDiagnosticMaterialKind.RawProviderResponse, OperatorSupportDiagnosticVisibilityClassification.BlockedByRawPayloadMaterial)]
    [DataRow(OperatorSupportDiagnosticMaterialKind.CredentialMaterial, OperatorSupportDiagnosticVisibilityClassification.BlockedByCredentialMaterial)]
    [DataRow(OperatorSupportDiagnosticMaterialKind.SecretMaterial, OperatorSupportDiagnosticVisibilityClassification.BlockedBySecretMaterial)]
    [DataRow(OperatorSupportDiagnosticMaterialKind.PrivateReasoning, OperatorSupportDiagnosticVisibilityClassification.BlockedByPrivateReasoningMaterial)]
    public void SensitiveDiagnosticMaterialIsAlwaysNotVisible(
        OperatorSupportDiagnosticMaterialKind materialKind,
        OperatorSupportDiagnosticVisibilityClassification expectedClassification)
    {
        var decision = Classify(Request(materialKind));

        Assert.AreEqual(expectedClassification, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void AuthorityMarkerMaterialIsBlocked()
    {
        var decision = Classify(Request(OperatorSupportDiagnosticMaterialKind.AuthorityMarker));

        Assert.AreEqual(OperatorSupportDiagnosticVisibilityClassification.BlockedByAuthorityMarker, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(OperatorSupportDiagnosticMaterialKind.SourcePatch)]
    [DataRow(OperatorSupportDiagnosticMaterialKind.CommitPackage)]
    [DataRow(OperatorSupportDiagnosticMaterialKind.PushReceipt)]
    [DataRow(OperatorSupportDiagnosticMaterialKind.PullRequestMutationReceipt)]
    [DataRow(OperatorSupportDiagnosticMaterialKind.ReleaseOrDeployReceipt)]
    public void MutationAdjacentMaterialsAreHiddenUnlessRepresentedAsSafeSummaries(
        OperatorSupportDiagnosticMaterialKind materialKind)
    {
        var decision = Classify(Request(materialKind));

        Assert.AreEqual(OperatorSupportDiagnosticVisibilityClassification.Hidden, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(OperatorSupportDiagnosticRequestedIntent.RunDiagnostic, OperatorSupportDiagnosticVisibilityClassification.BlockedByDiagnosticExecutionIntent)]
    [DataRow(OperatorSupportDiagnosticRequestedIntent.RefreshValidation, OperatorSupportDiagnosticVisibilityClassification.BlockedByDiagnosticExecutionIntent)]
    [DataRow(OperatorSupportDiagnosticRequestedIntent.ProveSourceSafety, OperatorSupportDiagnosticVisibilityClassification.BlockedByDiagnosticExecutionIntent)]
    [DataRow(OperatorSupportDiagnosticRequestedIntent.ExecuteRetry, OperatorSupportDiagnosticVisibilityClassification.BlockedByRetryIntent)]
    [DataRow(OperatorSupportDiagnosticRequestedIntent.ExecuteRollback, OperatorSupportDiagnosticVisibilityClassification.BlockedByRollbackIntent)]
    [DataRow(OperatorSupportDiagnosticRequestedIntent.ExecuteRecovery, OperatorSupportDiagnosticVisibilityClassification.BlockedByRecoveryIntent)]
    [DataRow(OperatorSupportDiagnosticRequestedIntent.MutateSource, OperatorSupportDiagnosticVisibilityClassification.BlockedByMutationIntent)]
    [DataRow(OperatorSupportDiagnosticRequestedIntent.ApplyPatch, OperatorSupportDiagnosticVisibilityClassification.BlockedByMutationIntent)]
    [DataRow(OperatorSupportDiagnosticRequestedIntent.Commit, OperatorSupportDiagnosticVisibilityClassification.BlockedByMutationIntent)]
    [DataRow(OperatorSupportDiagnosticRequestedIntent.Push, OperatorSupportDiagnosticVisibilityClassification.BlockedByMutationIntent)]
    [DataRow(OperatorSupportDiagnosticRequestedIntent.CreatePullRequest, OperatorSupportDiagnosticVisibilityClassification.BlockedByMutationIntent)]
    [DataRow(OperatorSupportDiagnosticRequestedIntent.ReadyForReview, OperatorSupportDiagnosticVisibilityClassification.BlockedByMutationIntent)]
    [DataRow(OperatorSupportDiagnosticRequestedIntent.Merge, OperatorSupportDiagnosticVisibilityClassification.BlockedByMutationIntent)]
    [DataRow(OperatorSupportDiagnosticRequestedIntent.Release, OperatorSupportDiagnosticVisibilityClassification.BlockedByMutationIntent)]
    [DataRow(OperatorSupportDiagnosticRequestedIntent.Deploy, OperatorSupportDiagnosticVisibilityClassification.BlockedByMutationIntent)]
    [DataRow(OperatorSupportDiagnosticRequestedIntent.ApprovalAuthority, OperatorSupportDiagnosticVisibilityClassification.BlockedByActionIntent)]
    [DataRow(OperatorSupportDiagnosticRequestedIntent.PolicySatisfaction, OperatorSupportDiagnosticVisibilityClassification.BlockedByActionIntent)]
    [DataRow(OperatorSupportDiagnosticRequestedIntent.WorkflowContinuation, OperatorSupportDiagnosticVisibilityClassification.BlockedByActionIntent)]
    [DataRow(OperatorSupportDiagnosticRequestedIntent.VisibilityGrant, OperatorSupportDiagnosticVisibilityClassification.BlockedByActionIntent)]
    [DataRow(OperatorSupportDiagnosticRequestedIntent.AccessGrant, OperatorSupportDiagnosticVisibilityClassification.BlockedByActionIntent)]
    [DataRow(OperatorSupportDiagnosticRequestedIntent.RedactionBypass, OperatorSupportDiagnosticVisibilityClassification.BlockedByActionIntent)]
    [DataRow(OperatorSupportDiagnosticRequestedIntent.SecretDisclosure, OperatorSupportDiagnosticVisibilityClassification.BlockedByActionIntent)]
    [DataRow(OperatorSupportDiagnosticRequestedIntent.PrivateReasoningDisclosure, OperatorSupportDiagnosticVisibilityClassification.BlockedByActionIntent)]
    public void ActionIntentsAreBlockedEvenWithDiagnosticEvidence(
        OperatorSupportDiagnosticRequestedIntent intent,
        OperatorSupportDiagnosticVisibilityClassification expectedClassification)
    {
        var decision = Classify(Request(OperatorSupportDiagnosticMaterialKind.OperationStatusMetadata) with
        {
            RequestedIntent = intent
        });

        Assert.AreEqual(expectedClassification, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnknownMaterialFailsClosed()
    {
        var decision = Classify(Request(OperatorSupportDiagnosticMaterialKind.Unknown));

        Assert.AreEqual(OperatorSupportDiagnosticVisibilityClassification.BlockedByUnknownMaterial, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnknownIntentFailsClosed()
    {
        var decision = Classify(Request(OperatorSupportDiagnosticMaterialKind.OperationStatusMetadata) with
        {
            RequestedIntent = OperatorSupportDiagnosticRequestedIntent.Unknown
        });

        Assert.AreEqual(OperatorSupportDiagnosticVisibilityClassification.BlockedByUnknownIntent, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void NonOperatorSupportRoleFailsClosed()
    {
        var reviewer = Catalog().Entries.Single(entry => entry.RoleKind == GovernanceRoleKind.Reviewer);
        var decision = Classify(Request(OperatorSupportDiagnosticMaterialKind.OperationStatusMetadata) with
        {
            RequestedRoleKey = reviewer.RoleId
        });

        Assert.AreEqual(OperatorSupportDiagnosticVisibilityClassification.BlockedByNonOperatorSupportRole, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(GovernanceRoleKind.OperationsReviewer)]
    [DataRow(GovernanceRoleKind.ExecutorOperatorCandidate)]
    [DataRow(GovernanceRoleKind.RollbackReviewer)]
    [DataRow(GovernanceRoleKind.RecoveryReviewer)]
    public void ExistingOperatorSupportRolesCanBeClassifiedWithoutGrantingAuthority(
        GovernanceRoleKind roleKind)
    {
        var role = Catalog().Entries.Single(entry => entry.RoleKind == roleKind);
        var decision = Classify(Request(OperatorSupportDiagnosticMaterialKind.OperationStatusMetadata) with
        {
            RequestedRoleKey = role.RoleId
        });

        Assert.AreEqual(OperatorSupportDiagnosticVisibilityClassification.MetadataOnlyCandidate, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void MissingCatalogEvidenceFailsClosed()
    {
        var decision = Classify(Request(OperatorSupportDiagnosticMaterialKind.OperationStatusMetadata) with
        {
            RoleCatalogEvidenceRef = string.Empty
        });

        Assert.AreEqual(OperatorSupportDiagnosticVisibilityClassification.BlockedByMissingCatalogEvidence, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void MissingMatrixEvidenceFailsClosed()
    {
        var decision = Classify(Request(OperatorSupportDiagnosticMaterialKind.OperationStatusMetadata) with
        {
            VisibilityMatrixEvidenceRef = string.Empty
        });

        Assert.AreEqual(OperatorSupportDiagnosticVisibilityClassification.BlockedByMissingMatrixEvidence, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void MissingDiagnosticEvidenceFailsClosed()
    {
        var decision = Classify(Request(OperatorSupportDiagnosticMaterialKind.OperationStatusMetadata) with
        {
            DiagnosticEvidenceRef = string.Empty
        });

        Assert.AreEqual(OperatorSupportDiagnosticVisibilityClassification.BlockedByMissingDiagnosticEvidence, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void DiagnosticEvidenceNotAllowedByMatrixIsHidden()
    {
        var decision = Classify(Request(OperatorSupportDiagnosticMaterialKind.OperationStatusMetadata) with
        {
            RequestedSurface = RoleVisibilitySurface.ReleaseReadiness
        });

        Assert.AreEqual(OperatorSupportDiagnosticVisibilityClassification.Hidden, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnsafeAuthorityTextIsHiddenAndDoesNotEchoRawMarker()
    {
        var unsafeMarker = "CanRetry = true";
        var decision = Classify(Request(OperatorSupportDiagnosticMaterialKind.OperationStatusMetadata) with
        {
            DiagnosticEvidenceRef = unsafeMarker
        });

        Assert.AreEqual(OperatorSupportDiagnosticVisibilityClassification.Hidden, decision.Classification);
        CollectionAssert.Contains(decision.EvidenceRefs.ToList(), "[unsafe-rejected]");
        Assert.IsFalse(decision.RecordFingerprint.Contains(unsafeMarker, StringComparison.OrdinalIgnoreCase));
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow("operation-status-summary:f07")]
    [DataRow("failure-classification-summary:f07")]
    [DataRow("retry-classification-summary:f07")]
    [DataRow("rollback-readiness-summary:f07")]
    [DataRow("redacted-log-summary:f07")]
    public void SafeDiagnosticEvidenceRefsAreNotRejected(string value)
    {
        Assert.IsFalse(OperatorSupportDiagnosticVisibilityValidator.ContainsUnsafeEvidenceText(value));
    }

    [TestMethod]
    public void NullRequestValidationFailsClosed()
    {
        var result = OperatorSupportDiagnosticVisibilityValidator.ValidateRequest(null);

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToList(), "OperatorSupportDiagnosticVisibilityRequestRequired");
    }

    [TestMethod]
    public void EveryDecisionHasAllAuthorityFlagsFalse()
    {
        var requests = new[]
        {
            Request(OperatorSupportDiagnosticMaterialKind.OperationStatusMetadata),
            Request(OperatorSupportDiagnosticMaterialKind.RedactedLogSummary) with { OptionalRedactionEvidenceRef = null },
            Request(OperatorSupportDiagnosticMaterialKind.RawLog),
            Request(OperatorSupportDiagnosticMaterialKind.AuthorityMarker),
            Request(OperatorSupportDiagnosticMaterialKind.OperationStatusMetadata) with { RequestedIntent = OperatorSupportDiagnosticRequestedIntent.ExecuteRetry },
            Request(OperatorSupportDiagnosticMaterialKind.Unknown),
            Request(OperatorSupportDiagnosticMaterialKind.OperationStatusMetadata) with { DiagnosticEvidenceRef = string.Empty }
        };

        foreach (var request in requests)
        {
            AssertAuthorityFlagsFalse(Classify(request));
        }
    }

    [TestMethod]
    public void DecisionModelCarriesRequiredFalseAuthorityFields()
    {
        var decision = Classify(Request(OperatorSupportDiagnosticMaterialKind.OperationStatusMetadata));

        Assert.IsFalse(decision.GrantsOperatorAuthority);
        Assert.IsFalse(decision.GrantsSupportAuthority);
        Assert.IsFalse(decision.GrantsRoleAssignmentAuthority);
        Assert.IsFalse(decision.GrantsVisibilityAuthority);
        Assert.IsFalse(decision.GrantsAccess);
        Assert.IsFalse(decision.GrantsDiagnosticExecutionAuthority);
        Assert.IsFalse(decision.RefreshesValidation);
        Assert.IsFalse(decision.ProvesSourceSafety);
        Assert.IsFalse(decision.GrantsRetryAuthority);
        Assert.IsFalse(decision.GrantsRollbackAuthority);
        Assert.IsFalse(decision.GrantsRecoveryAuthority);
        Assert.IsFalse(decision.GrantsApprovalAuthority);
        Assert.IsFalse(decision.SatisfiesPolicy);
        Assert.IsFalse(decision.GrantsMutationAuthority);
        Assert.IsFalse(decision.GrantsWorkflowContinuation);
        Assert.IsFalse(decision.GrantsMergeAuthority);
        Assert.IsFalse(decision.GrantsReleaseAuthority);
        Assert.IsFalse(decision.GrantsDeploymentAuthority);
        Assert.IsFalse(decision.BypassesRedaction);
        Assert.IsFalse(decision.DisclosesSecrets);
        Assert.IsFalse(decision.DisclosesPrivateReasoning);
    }

    [TestMethod]
    public void StaticScanF07CoreAddsNoIdentityPermissionProviderOrMutationSurface()
    {
        var source = F07CoreSourceWithoutStrings();
        var forbidden = new[]
        {
            "UserId",
            "PrincipalId",
            "GroupId",
            "AccessToken",
            "ClaimsPrincipal",
            "GitHubActor",
            "PermissionResolver",
            "IdentityLookup",
            "PrincipalLookup",
            "GroupMembership",
            "Collaborator",
            "DiagnosticGateway",
            "RetryExecutor",
            "RollbackExecutor",
            "RecoveryExecutor",
            "ValidationRunner",
            "ProviderClient",
            "ApiController",
            "ControllerBase",
            "DbContext",
            "SqlConnection",
            "WorkflowTransition",
            "RunProcessAsync",
            "ProcessStartInfo",
            "HttpClient",
            "File.Write",
            "File.Read",
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
    public void ReceiptExistsAndStatesOperatorSupportDiagnosticVisibilityIsNotAuthority()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "F07_OPERATOR_SUPPORT_DIAGNOSTIC_VISIBILITY_RULES.md"));

        StringAssert.Contains(receipt, "Operator/support diagnostic visibility is not operational authority.");
        StringAssert.Contains(receipt, "Seeing the failure is not permission to fix it.");
        StringAssert.Contains(receipt, "does not grant access");
        StringAssert.Contains(receipt, "does not refresh validation");
        StringAssert.Contains(receipt, "does not prove source safety");
        StringAssert.Contains(receipt, "does not authorize retry, rollback, or recovery");
        StringAssert.Contains(receipt, "does not authorize merge, release, or deployment");
    }

    private static OperatorSupportDiagnosticVisibilityDecision Classify(
        OperatorSupportDiagnosticVisibilityRequest request) =>
        new OperatorSupportDiagnosticVisibilityService().Classify(Catalog(), Matrix(), request);

    private static OperatorSupportDiagnosticVisibilityRequest Request(
        OperatorSupportDiagnosticMaterialKind materialKind)
    {
        var role = Catalog().Entries.Single(entry => entry.RoleKind == GovernanceRoleKind.OperationsReviewer);
        return new OperatorSupportDiagnosticVisibilityRequest
        {
            CorrelationId = "correlation-f07",
            RequestedRoleKey = role.RoleId,
            RequestedSurface = SurfaceFor(materialKind),
            RequestedMaterialKind = materialKind,
            RequestedIntent = OperatorSupportDiagnosticRequestedIntent.ReadOnlyInspect,
            DiagnosticEvidenceRef = "diagnostic-evidence:f07",
            RoleCatalogEvidenceRef = "role-catalog:f07",
            VisibilityMatrixEvidenceRef = "visibility-matrix:f07",
            OptionalPolicyEvidenceRef = "policy-evidence:f07",
            OptionalRedactionEvidenceRef = "redaction-evidence:f07"
        };
    }

    private static RoleVisibilitySurface SurfaceFor(OperatorSupportDiagnosticMaterialKind materialKind) =>
        materialKind switch
        {
            OperatorSupportDiagnosticMaterialKind.RollbackReadinessSummary => RoleVisibilitySurface.Rollback,
            OperatorSupportDiagnosticMaterialKind.RecoveryRecommendationSummary => RoleVisibilitySurface.Recovery,
            OperatorSupportDiagnosticMaterialKind.DependencyHealthSummary => RoleVisibilitySurface.DeploymentReadiness,
            OperatorSupportDiagnosticMaterialKind.EnvironmentReadinessSummary => RoleVisibilitySurface.DeploymentReadiness,
            _ => RoleVisibilitySurface.OperationStatus
        };

    private static GovernanceRoleCatalog Catalog() => new RoleCatalogService().BuildDefaultCatalog();

    private static RoleVisibilityMatrix Matrix() =>
        new RoleVisibilityMatrixService().BuildDefaultMatrix(Catalog());

    private static void AssertAuthorityFlagsFalse(OperatorSupportDiagnosticVisibilityDecision decision)
    {
        Assert.IsFalse(decision.GrantsOperatorAuthority);
        Assert.IsFalse(decision.GrantsSupportAuthority);
        Assert.IsFalse(decision.GrantsRoleAssignmentAuthority);
        Assert.IsFalse(decision.GrantsVisibilityAuthority);
        Assert.IsFalse(decision.GrantsAccess);
        Assert.IsFalse(decision.GrantsDiagnosticExecutionAuthority);
        Assert.IsFalse(decision.RefreshesValidation);
        Assert.IsFalse(decision.ProvesSourceSafety);
        Assert.IsFalse(decision.GrantsRetryAuthority);
        Assert.IsFalse(decision.GrantsRollbackAuthority);
        Assert.IsFalse(decision.GrantsRecoveryAuthority);
        Assert.IsFalse(decision.GrantsApprovalAuthority);
        Assert.IsFalse(decision.SatisfiesPolicy);
        Assert.IsFalse(decision.GrantsMutationAuthority);
        Assert.IsFalse(decision.GrantsWorkflowContinuation);
        Assert.IsFalse(decision.GrantsMergeAuthority);
        Assert.IsFalse(decision.GrantsReleaseAuthority);
        Assert.IsFalse(decision.GrantsDeploymentAuthority);
        Assert.IsFalse(decision.BypassesRedaction);
        Assert.IsFalse(decision.DisclosesSecrets);
        Assert.IsFalse(decision.DisclosesPrivateReasoning);
    }

    private static string F07CoreSourceWithoutStrings()
    {
        var root = RepoRoot();
        var files = Directory.GetFiles(Path.Combine(root, "IronDev.Core", "Governance"), "OperatorSupportDiagnosticVisibility*.cs");
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
