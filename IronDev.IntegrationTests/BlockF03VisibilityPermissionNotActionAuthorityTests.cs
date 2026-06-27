using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockF03VisibilityPermissionNotActionAuthorityTests
{
    [TestMethod]
    public void DefaultF02MatrixStillValid()
    {
        var result = MatrixService().ValidateDefaultMatrix(Catalog());

        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues));
        CollectionAssert.Contains(result.Warnings.ToList(), "role visibility matrix does not grant access");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not execution authority");
    }

    [TestMethod]
    public void VisibilityPermissionFixtureIsEvidenceOnly()
    {
        var fixture = Fixture(RoleVisibilitySurface.OperationStatus, RoleVisibilityMaterialKind.OperationStatusSummary);

        Assert.IsTrue(fixture.Boundary.EvidenceOnly);
        AssertNoActionAuthority(fixture);
    }

    [TestMethod]
    public void VisibilityPermissionFixtureRequiresSeparateRoleAssignment()
    {
        var fixture = Fixture(RoleVisibilitySurface.OperationStatus, RoleVisibilityMaterialKind.OperationStatusSummary);

        Assert.IsTrue(fixture.RequiresSeparateRoleAssignment);
        CollectionAssert.Contains(fixture.Boundary.RequiredSeparateEvidence.ToList(), "SeparateRoleAssignmentRequired");
    }

    [TestMethod]
    public void VisibilityPermissionFixtureRequiresSeparateVisibilityDecision()
    {
        var fixture = Fixture(RoleVisibilitySurface.OperationStatus, RoleVisibilityMaterialKind.OperationStatusSummary);

        Assert.IsTrue(fixture.RequiresSeparateVisibilityDecision);
        CollectionAssert.Contains(fixture.Boundary.RequiredSeparateEvidence.ToList(), "SeparateVisibilityDecisionRequired");
    }

    [TestMethod]
    public void VisibilityPermissionFixtureRequiresSeparatePolicyDecisionForSensitiveMaterial()
    {
        var fixture = Fixture(
            RoleVisibilitySurface.PolicyReview,
            RoleVisibilityMaterialKind.SensitiveFindingSummary,
            RoleVisibilityLevel.RedactedDetails,
            RoleVisibilitySensitivityKind.SecuritySensitive);

        Assert.IsTrue(fixture.RequiresSeparatePolicyDecision);
        CollectionAssert.Contains(fixture.Boundary.RequiredSeparateEvidence.ToList(), "SeparatePolicyDecisionRequired");
    }

    [TestMethod]
    public void VisibilityPermissionFixtureRequiresSeparateRedactionForRedactedMaterial()
    {
        var fixture = Fixture(
            RoleVisibilitySurface.PolicyReview,
            RoleVisibilityMaterialKind.SecretScanSummary,
            RoleVisibilityLevel.RedactedDetails,
            RoleVisibilitySensitivityKind.SecuritySensitive);

        Assert.IsTrue(fixture.RequiresSeparateRedaction);
        CollectionAssert.Contains(fixture.Boundary.RequiredSeparateEvidence.ToList(), "SeparateRedactionEnforcementRequired");
    }

    [TestMethod]
    public void SourceApplyVisibilityDoesNotAuthorizeSourceApply()
    {
        var fixture = Fixture(RoleVisibilitySurface.SourceApply, RoleVisibilityMaterialKind.SourceApplySummary)
            .WithBoundary("SeparateMutationAuthorityRequired");

        AssertNoActionAuthority(fixture);
        Assert.IsFalse(fixture.Boundary.SourceApplyAuthority);
        CollectionAssert.Contains(fixture.Boundary.RequiredSeparateEvidence.ToList(), "SeparateMutationAuthorityRequired");
    }

    [TestMethod]
    public void PatchMetadataVisibilityDoesNotAuthorizeApplyCommitOrPush()
    {
        var fixture = Fixture(RoleVisibilitySurface.SourceApply, RoleVisibilityMaterialKind.PatchMetadata)
            .WithBoundary("SeparateMutationAuthorityRequired");

        Assert.IsFalse(fixture.Boundary.SourceApplyAuthority);
        Assert.IsFalse(fixture.Boundary.CommitAuthority);
        Assert.IsFalse(fixture.Boundary.PushAuthority);
        AssertNoActionAuthority(fixture);
    }

    [TestMethod]
    public void CommitPackageVisibilityDoesNotAuthorizeCommit()
    {
        var fixture = Fixture(RoleVisibilitySurface.Commit, RoleVisibilityMaterialKind.CommitPackageSummary)
            .WithBoundary("SeparateMutationAuthorityRequired");

        Assert.IsFalse(fixture.Boundary.CommitAuthority);
        AssertNoActionAuthority(fixture);
    }

    [TestMethod]
    public void PushReceiptVisibilityDoesNotAuthorizePush()
    {
        var fixture = Fixture(RoleVisibilitySurface.Push, RoleVisibilityMaterialKind.PushReceiptSummary)
            .WithBoundary("SeparateMutationAuthorityRequired");

        Assert.IsFalse(fixture.Boundary.PushAuthority);
        AssertNoActionAuthority(fixture);
    }

    [TestMethod]
    public void PullRequestMetadataVisibilityDoesNotAuthorizePullRequestAction()
    {
        var fixture = Fixture(RoleVisibilitySurface.PullRequest, RoleVisibilityMaterialKind.PullRequestMetadata)
            .WithBoundary("PR visibility is review evidence only. It is not PR action authority.");

        Assert.IsFalse(fixture.Boundary.PullRequestAuthority);
        Assert.IsFalse(fixture.Boundary.ReadyForReviewAuthority);
        Assert.IsFalse(fixture.Boundary.MergeAuthority);
        StringAssert.Contains(fixture.Boundary.BoundaryStatement, "not PR action authority");
    }

    [TestMethod]
    public void PullRequestDiffSummaryVisibilityDoesNotAuthorizeReadyForReviewOrMerge()
    {
        var fixture = Fixture(RoleVisibilitySurface.PullRequest, RoleVisibilityMaterialKind.PullRequestDiffSummary)
            .WithBoundary("PR visibility is review evidence only. It is not PR action authority.");

        Assert.IsFalse(fixture.Boundary.ReadyForReviewAuthority);
        Assert.IsFalse(fixture.Boundary.MergeAuthority);
        AssertNoActionAuthority(fixture);
    }

    [TestMethod]
    public void ReadyForReviewVisibilityDoesNotAuthorizeReadyForReview()
    {
        var fixture = Fixture(RoleVisibilitySurface.ReadyForReview, RoleVisibilityMaterialKind.ReadyForReviewSummary)
            .WithBoundary("PR visibility is review evidence only. It is not PR action authority.");

        Assert.IsFalse(fixture.Boundary.ReadyForReviewAuthority);
        AssertNoActionAuthority(fixture);
    }

    [TestMethod]
    public void MergeReadinessVisibilityDoesNotAuthorizeMerge()
    {
        var fixture = Fixture(RoleVisibilitySurface.MergeReadiness, RoleVisibilityMaterialKind.MergeReadinessSummary)
            .WithBoundary("Readiness visibility is not readiness decision or execution authority.");

        Assert.IsFalse(fixture.Boundary.MergeAuthority);
        AssertNoActionAuthority(fixture);
    }

    [TestMethod]
    public void ReleaseReadinessVisibilityDoesNotAuthorizeRelease()
    {
        var fixture = Fixture(RoleVisibilitySurface.ReleaseReadiness, RoleVisibilityMaterialKind.ReleaseReadinessSummary)
            .WithBoundary("Readiness visibility is not readiness decision or execution authority.");

        Assert.IsFalse(fixture.Boundary.ReleaseAuthority);
        AssertNoActionAuthority(fixture);
    }

    [TestMethod]
    public void DeploymentReadinessVisibilityDoesNotAuthorizeDeployment()
    {
        var fixture = Fixture(RoleVisibilitySurface.DeploymentReadiness, RoleVisibilityMaterialKind.DeploymentReadinessSummary)
            .WithBoundary("Readiness visibility is not readiness decision or execution authority.");

        Assert.IsFalse(fixture.Boundary.DeploymentAuthority);
        AssertNoActionAuthority(fixture);
    }

    [TestMethod]
    public void RollbackVisibilityDoesNotAuthorizeRollback()
    {
        var fixture = Fixture(RoleVisibilitySurface.Rollback, RoleVisibilityMaterialKind.RollbackSummary)
            .WithBoundary("Seeing recovery evidence is not permission to recover.");

        Assert.IsFalse(fixture.Boundary.RollbackAuthority);
        AssertNoActionAuthority(fixture);
    }

    [TestMethod]
    public void RetryVisibilityDoesNotAuthorizeRetry()
    {
        var fixture = Fixture(RoleVisibilitySurface.Retry, RoleVisibilityMaterialKind.RetrySummary)
            .WithBoundary("Seeing recovery evidence is not permission to recover.");

        Assert.IsFalse(fixture.Boundary.RetryAuthority);
        AssertNoActionAuthority(fixture);
    }

    [TestMethod]
    public void RecoveryVisibilityDoesNotAuthorizeRecovery()
    {
        var fixture = Fixture(RoleVisibilitySurface.Recovery, RoleVisibilityMaterialKind.RecoverySummary)
            .WithBoundary("Seeing recovery evidence is not permission to recover.");

        Assert.IsFalse(fixture.Boundary.RecoveryAuthority);
        AssertNoActionAuthority(fixture);
    }

    [TestMethod]
    public void WorkflowContinuationVisibilityDoesNotAuthorizeWorkflowContinuation()
    {
        var fixture = Fixture(RoleVisibilitySurface.WorkflowContinuation, RoleVisibilityMaterialKind.WorkflowContinuationSummary)
            .WithBoundary("Continuation visibility is not continuation authority.");

        Assert.IsFalse(fixture.Boundary.WorkflowContinuationAuthority);
        Assert.IsFalse(fixture.Boundary.AutomationAutonomy);
        AssertNoActionAuthority(fixture);
    }

    [TestMethod]
    public void OperationStatusVisibilityDoesNotAuthorizeMutation()
    {
        var fixture = Fixture(RoleVisibilitySurface.OperationStatus, RoleVisibilityMaterialKind.OperationStatusSummary)
            .WithBoundary("Status and receipt visibility are witnesses, not permission.");

        Assert.IsFalse(fixture.Boundary.SourceApplyAuthority);
        Assert.IsFalse(fixture.Boundary.CommitAuthority);
        Assert.IsFalse(fixture.Boundary.PushAuthority);
        AssertNoActionAuthority(fixture);
    }

    [TestMethod]
    public void ReceiptVisibilityDoesNotAuthorizeDownstreamAction()
    {
        var fixture = Fixture(RoleVisibilitySurface.ReceiptReadModel, RoleVisibilityMaterialKind.ReceiptMetadata)
            .WithBoundary("Status and receipt visibility are witnesses, not permission.");

        AssertNoActionAuthority(fixture);
    }

    [TestMethod]
    public void AuditVisibilityDoesNotAuthorizeAction()
    {
        var fixture = Fixture(RoleVisibilitySurface.Audit, RoleVisibilityMaterialKind.AuditTrailSummary)
            .WithBoundary("Status and receipt visibility are witnesses, not permission.");

        AssertNoActionAuthority(fixture);
    }

    [TestMethod]
    public void ApprovalPackageVisibilityDoesNotApprove()
    {
        var fixture = Fixture(RoleVisibilitySurface.ApprovalPackage, RoleVisibilityMaterialKind.ApprovalPackageSummary)
            .WithBoundary("Seeing an approval package is not approving it.");

        Assert.IsFalse(fixture.Boundary.ApprovalAuthority);
        StringAssert.Contains(fixture.Boundary.BoundaryStatement, "not approving it");
    }

    [TestMethod]
    public void ApprovalPackageVisibilityDoesNotSatisfyApprovalProfile()
    {
        var fixture = Fixture(RoleVisibilitySurface.ApprovalPackage, RoleVisibilityMaterialKind.ApprovalPackageSummary);

        Assert.IsFalse(fixture.Boundary.ApprovalProfileSatisfaction);
        AssertNoActionAuthority(fixture);
    }

    [TestMethod]
    public void PolicyReviewVisibilityDoesNotSatisfyPolicy()
    {
        var fixture = Fixture(RoleVisibilitySurface.PolicyReview, RoleVisibilityMaterialKind.PolicyReviewSummary)
            .WithBoundary("Policy visibility is not policy satisfaction.");

        Assert.IsFalse(fixture.Boundary.PolicySatisfaction);
        StringAssert.Contains(fixture.Boundary.BoundaryStatement, "not policy satisfaction");
    }

    [TestMethod]
    public void SensitiveFindingVisibilityDoesNotSatisfySecurityPolicy()
    {
        var fixture = Fixture(RoleVisibilitySurface.PolicyReview, RoleVisibilityMaterialKind.SensitiveFindingSummary, RoleVisibilityLevel.RedactedDetails, RoleVisibilitySensitivityKind.SecuritySensitive);

        Assert.IsFalse(fixture.Boundary.PolicySatisfaction);
        Assert.IsFalse(fixture.Boundary.SecurityApprovalSatisfaction);
    }

    [TestMethod]
    public void SecretScanVisibilityDoesNotSatisfySecurityPolicy()
    {
        var fixture = Fixture(RoleVisibilitySurface.PolicyReview, RoleVisibilityMaterialKind.SecretScanSummary, RoleVisibilityLevel.RedactedDetails, RoleVisibilitySensitivityKind.SecuritySensitive);

        Assert.IsFalse(fixture.Boundary.PolicySatisfaction);
        Assert.IsFalse(fixture.Boundary.SecurityApprovalSatisfaction);
    }

    [TestMethod]
    public void ValidationSummaryVisibilityDoesNotRefreshValidation()
    {
        var fixture = Fixture(RoleVisibilitySurface.ValidationReview, RoleVisibilityMaterialKind.ValidationSummary)
            .WithBoundary("Seeing validation evidence is not fresh validation.");

        Assert.IsFalse(fixture.Boundary.ValidationFresh);
        StringAssert.Contains(fixture.Boundary.BoundaryStatement, "not fresh validation");
    }

    [TestMethod]
    public void ValidationEvidenceRefVisibilityDoesNotRefreshValidation()
    {
        var fixture = Fixture(RoleVisibilitySurface.ValidationReview, RoleVisibilityMaterialKind.ValidationEvidenceRefs)
            .WithBoundary("Seeing validation evidence is not fresh validation.");

        Assert.IsFalse(fixture.Boundary.ValidationFresh);
    }

    [TestMethod]
    public void ValidationVisibilityDoesNotProveSourceSafety()
    {
        var fixture = Fixture(RoleVisibilitySurface.ValidationReview, RoleVisibilityMaterialKind.ValidationSummary);

        Assert.IsFalse(fixture.Boundary.SourceSafe);
        AssertNoActionAuthority(fixture);
    }

    [TestMethod]
    public void RedactedDetailsVisibilityDoesNotBypassRedaction()
    {
        var fixture = Fixture(RoleVisibilitySurface.PolicyReview, RoleVisibilityMaterialKind.SensitiveFindingSummary, RoleVisibilityLevel.RedactedDetails, RoleVisibilitySensitivityKind.SecuritySensitive)
            .WithBoundary("Redacted detail visibility is not raw material visibility.");

        Assert.IsFalse(fixture.Boundary.RedactionBypass);
        Assert.IsFalse(fixture.Boundary.RawPayloadDisclosed);
        Assert.IsFalse(fixture.Boundary.SecretDisclosed);
        Assert.IsFalse(fixture.Boundary.PrivateReasoningDisclosed);
    }

    [TestMethod]
    public void DetailEligibilityHintDoesNotDiscloseDetails()
    {
        var fixture = Fixture(RoleVisibilitySurface.PolicyReview, RoleVisibilityMaterialKind.SensitiveFindingSummary, RoleVisibilityLevel.DetailEligibilityHint, RoleVisibilitySensitivityKind.SecuritySensitive)
            .WithBoundary("Detail eligibility is a hint, not disclosure.");

        Assert.IsFalse(fixture.Boundary.DetailAccess);
        Assert.IsFalse(fixture.Boundary.RawPayloadDisclosed);
        Assert.IsTrue(fixture.RequiresSeparateVisibilityDecision);
        Assert.IsTrue(fixture.RequiresSeparatePolicyDecision);
    }

    [DataTestMethod]
    [DataRow(RoleVisibilityLevel.SummaryOnly)]
    [DataRow(RoleVisibilityLevel.MetadataOnly)]
    [DataRow(RoleVisibilityLevel.ReferenceOnly)]
    [DataRow(RoleVisibilityLevel.PresenceOnly)]
    public void BoundedVisibilityLevelsDoNotExposeRawPayload(RoleVisibilityLevel level)
    {
        var fixture = Fixture(RoleVisibilitySurface.OperationStatus, RoleVisibilityMaterialKind.OperationStatusSummary, level);

        Assert.IsFalse(fixture.Boundary.RawPayloadDisclosed);
        Assert.IsFalse(fixture.Boundary.CredentialDisclosed);
        Assert.IsFalse(fixture.Boundary.PrivateReasoningDisclosed);
    }

    [DataTestMethod]
    [DataRow(RoleVisibilityMaterialKind.CredentialMaterial, RoleVisibilitySensitivityKind.CredentialLike)]
    [DataRow(RoleVisibilityMaterialKind.PrivateReasoning, RoleVisibilitySensitivityKind.PrivateReasoning)]
    [DataRow(RoleVisibilityMaterialKind.RawPayload, RoleVisibilitySensitivityKind.RawPayload)]
    public void HiddenMaterialRemainsHiddenEvenWithVisibilityPermission(
        RoleVisibilityMaterialKind material,
        RoleVisibilitySensitivityKind sensitivity)
    {
        var fixture = Fixture(RoleVisibilitySurface.FrontendReadOnly, material, RoleVisibilityLevel.NotVisible, sensitivity);

        Assert.AreEqual(RoleVisibilityLevel.NotVisible, fixture.VisibilityLevel);
        Assert.IsFalse(fixture.Boundary.RawPayloadDisclosed);
        Assert.IsFalse(fixture.Boundary.CredentialDisclosed);
        Assert.IsFalse(fixture.Boundary.PrivateReasoningDisclosed);
        Assert.IsFalse(fixture.Boundary.SecretDisclosed);
    }

    [TestMethod]
    public void SecretLikeMaterialRemainsHiddenEvenWithVisibilityPermission()
    {
        var fixture = Fixture(RoleVisibilitySurface.FrontendReadOnly, RoleVisibilityMaterialKind.SecretScanSummary, RoleVisibilityLevel.NotVisible, RoleVisibilitySensitivityKind.SecretLike);

        Assert.AreEqual(RoleVisibilityLevel.NotVisible, fixture.VisibilityLevel);
        Assert.IsFalse(fixture.Boundary.SecretDisclosed);
        Assert.IsFalse(fixture.Boundary.RedactionBypass);
    }

    [DataTestMethod]
    [DataRow(GovernanceRoleKind.ApproverCandidate, "ApprovalAuthority")]
    [DataRow(GovernanceRoleKind.PolicyOwnerCandidate, "PolicySatisfaction")]
    [DataRow(GovernanceRoleKind.ExecutorOperatorCandidate, "ExecutionAuthority")]
    [DataRow(GovernanceRoleKind.ReleaseReviewer, "ReleaseAuthority")]
    [DataRow(GovernanceRoleKind.OperationsReviewer, "DeploymentAuthority")]
    [DataRow(GovernanceRoleKind.RollbackReviewer, "RollbackAuthority")]
    [DataRow(GovernanceRoleKind.RecoveryReviewer, "RecoveryAuthority")]
    [DataRow(GovernanceRoleKind.AutomationAgent, "AutomationAutonomy")]
    [DataRow(GovernanceRoleKind.SystemReadOnly, "BackendAuthority")]
    [DataRow(GovernanceRoleKind.Auditor, "ActionAuthority")]
    [DataRow(GovernanceRoleKind.Observer, "ActionAuthority")]
    public void RoleSpecificVisibilityDoesNotCreateActionAuthority(GovernanceRoleKind roleKind, string deniedState)
    {
        var fixture = FixtureFromRole(roleKind);

        AssertNoActionAuthority(fixture);
        CollectionAssert.Contains(fixture.Boundary.ForbiddenImplications.ToList(), deniedState);
    }

    [TestMethod]
    public void FindEntriesByRoleIdDoesNotGrantAccessOrActionAuthority()
    {
        var roleId = Catalog().Entries.Single(entry => entry.RoleKind == GovernanceRoleKind.Reviewer).RoleId;
        var entries = MatrixService().FindEntriesByRoleId(Matrix(), roleId);

        Assert.IsTrue(entries.Count > 0);
        AssertLookupNonAuthority(entries);
    }

    [TestMethod]
    public void ListBySurfaceDoesNotGrantAccessOrActionAuthority()
    {
        var entries = MatrixService().ListBySurface(Matrix(), RoleVisibilitySurface.PullRequest);

        Assert.IsTrue(entries.Count > 0);
        AssertLookupNonAuthority(entries);
    }

    [TestMethod]
    public void ListByMaterialDoesNotGrantAccessOrActionAuthority()
    {
        var entries = MatrixService().ListByMaterial(Matrix(), RoleVisibilityMaterialKind.ReceiptMetadata);

        Assert.IsTrue(entries.Count > 0);
        AssertLookupNonAuthority(entries);
    }

    [TestMethod]
    public void ListByVisibilityLevelDoesNotGrantAccessOrActionAuthority()
    {
        var entries = MatrixService().ListByVisibilityLevel(Matrix(), RoleVisibilityLevel.SummaryOnly);

        Assert.IsTrue(entries.Count > 0);
        AssertLookupNonAuthority(entries);
    }

    [TestMethod]
    public void VisibilityMatrixValidationDoesNotGrantAccessOrActionAuthority()
    {
        var result = RoleVisibilityMatrixValidator.ValidateMatrix(Catalog(), Matrix());

        Assert.IsTrue(result.IsValid);
        AssertAuthorityWarnings(result);
    }

    [TestMethod]
    public void VisibilityMatrixWarningsDenyActionAuthority()
    {
        var result = RoleVisibilityMatrixValidator.ValidateMatrix(Catalog(), Matrix());

        CollectionAssert.Contains(result.Warnings.ToList(), "role visibility matrix does not grant access");
        CollectionAssert.Contains(result.Warnings.ToList(), "role visibility matrix does not authorize mutation");
    }

    [TestMethod]
    public void VisibilityMatrixForbiddenImplicationsDenyActionAuthority()
    {
        var result = RoleVisibilityMatrixValidator.ValidateMatrix(Catalog(), Matrix());

        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not access control");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not workflow continuation authority");
    }

    [DataTestMethod]
    [DataRow("can apply", VisibilityPermissionFindingKind.ApplyAuthorityAttempt)]
    [DataRow("can commit", VisibilityPermissionFindingKind.CommitAuthorityAttempt)]
    [DataRow("can push", VisibilityPermissionFindingKind.PushAuthorityAttempt)]
    [DataRow("can open pull request", VisibilityPermissionFindingKind.PullRequestAuthorityAttempt)]
    [DataRow("can mark ready for review", VisibilityPermissionFindingKind.ReadyForReviewAuthorityAttempt)]
    [DataRow("can merge", VisibilityPermissionFindingKind.MergeAuthorityAttempt)]
    [DataRow("can release", VisibilityPermissionFindingKind.ReleaseAuthorityAttempt)]
    [DataRow("can deploy", VisibilityPermissionFindingKind.DeploymentAuthorityAttempt)]
    [DataRow("can rollback", VisibilityPermissionFindingKind.RollbackAuthorityAttempt)]
    [DataRow("can retry", VisibilityPermissionFindingKind.RetryAuthorityAttempt)]
    [DataRow("can recover", VisibilityPermissionFindingKind.RecoveryAuthorityAttempt)]
    [DataRow("can continue workflow", VisibilityPermissionFindingKind.WorkflowContinuationAuthorityAttempt)]
    [DataRow("can approve", VisibilityPermissionFindingKind.ApprovalAuthorityAttempt)]
    [DataRow("can satisfy policy", VisibilityPermissionFindingKind.PolicySatisfactionAttempt)]
    [DataRow("can bypass redaction", VisibilityPermissionFindingKind.RedactionBypassAttempt)]
    [DataRow("can view secrets", VisibilityPermissionFindingKind.SecretDisclosureAttempt)]
    [DataRow("can view private reasoning", VisibilityPermissionFindingKind.PrivateReasoningDisclosureAttempt)]
    [DataRow("action authorized", VisibilityPermissionFindingKind.ActionAuthorityAttempt)]
    [DataRow("mutation authorized", VisibilityPermissionFindingKind.SourceSafetyAttempt)]
    [DataRow("approval granted", VisibilityPermissionFindingKind.ApprovalAuthorityAttempt)]
    [DataRow("policy satisfied", VisibilityPermissionFindingKind.PolicySatisfactionAttempt)]
    [DataRow("release authorized", VisibilityPermissionFindingKind.ReleaseAuthorityAttempt)]
    [DataRow("deployment authorized", VisibilityPermissionFindingKind.DeploymentAuthorityAttempt)]
    [DataRow("workflow continuation authorized", VisibilityPermissionFindingKind.WorkflowContinuationAuthorityAttempt)]
    [DataRow("redaction bypassed", VisibilityPermissionFindingKind.RedactionBypassAttempt)]
    public void DetectorFlagsAuthorityAttemptText(string text, VisibilityPermissionFindingKind expectedKind)
    {
        var fixture = Fixture(RoleVisibilitySurface.OperationStatus, RoleVisibilityMaterialKind.OperationStatusSummary) with
        {
            ClaimedStates = [text]
        };

        var findings = VisibilityPermissionAuthorityAttemptTestDetector.Detect(fixture);

        Assert.IsTrue(findings.Any(finding => finding.Kind == expectedKind), expectedKind.ToString());
        AssertNoActionAuthority(fixture);
    }

    [DataTestMethod]
    [DataRow("source apply summary")]
    [DataRow("commit package summary")]
    [DataRow("push receipt summary")]
    [DataRow("pull request metadata")]
    [DataRow("pull request diff summary")]
    [DataRow("ready for review summary")]
    [DataRow("merge readiness summary")]
    [DataRow("release readiness summary")]
    [DataRow("deployment readiness summary")]
    [DataRow("rollback summary")]
    [DataRow("retry summary")]
    [DataRow("recovery summary")]
    [DataRow("workflow continuation summary")]
    [DataRow("operation status summary")]
    [DataRow("receipt metadata")]
    [DataRow("audit trail summary")]
    public void ValidVisibilitySummaryTermsAreNotFlaggedAsActionAuthority(string text) =>
        AssertNoDetectorFinding(text);

    [DataTestMethod]
    [DataRow("read model hint")]
    [DataRow("frontend read only")]
    [DataRow("visibility evidence only")]
    [DataRow("read scope only")]
    public void ValidReadModelTermsAreNotFlaggedAsAccessGrant(string text) =>
        AssertNoDetectorFinding(text);

    [DataTestMethod]
    [DataRow("redacted details")]
    [DataRow("separate redaction enforcement required")]
    [DataRow("redaction required")]
    public void ValidRedactionTermsAreNotFlaggedAsBypass(string text) =>
        AssertNoDetectorFinding(text);

    [DataTestMethod]
    [DataRow("metadata only")]
    [DataRow("summary only")]
    [DataRow("reference only")]
    [DataRow("presence only")]
    [DataRow("detail eligibility hint")]
    public void ValidReceiptAndStatusTermsAreNotFlaggedAsActionAuthority(string text) =>
        AssertNoDetectorFinding(text);

    [TestMethod]
    public void F03AddsNoApiCliUiWorkerOrOpenApiSurface()
    {
        var changedNames = F03AllowedFileNames();

        Assert.IsTrue(changedNames.All(static path =>
            path.StartsWith("IronDev.IntegrationTests/", StringComparison.Ordinal) ||
            path.StartsWith("Docs/receipts/", StringComparison.Ordinal)));
        Assert.IsFalse(changedNames.Any(static path =>
            path.StartsWith("IronDev.Api/", StringComparison.Ordinal) ||
            path.StartsWith("IronDev.Cli/", StringComparison.Ordinal) ||
            path.StartsWith("IronDev.Infrastructure/", StringComparison.Ordinal) ||
            path.StartsWith("OpenApi/", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void F03AddsNoPersistenceSqlIdentityPrincipalPermissionAccessControlRedactionOrExecutorSurface()
    {
        var source = StripStringsAndTestMethodNames(F03ChangedSource());

        AssertDoesNotContain(source, "SqlConnection");
        AssertDoesNotContain(source, "DbContext");
        AssertDoesNotContain(source, "Repository");
        AssertDoesNotContain(source, "Store");
        AssertDoesNotContain(source, "ClaimsPrincipal");
        AssertDoesNotContain(source, "IPrincipal");
        AssertDoesNotContain(source, "UserManager");
        AssertDoesNotContain(source, "RoleManager");
        AssertDoesNotContain(source, "Authorize");
        AssertDoesNotContain(source, "PermissionService");
        AssertDoesNotContain(source, "AccessControl");
        AssertDoesNotContain(source, "RedactionEngine");
        AssertDoesNotContain(source, "ExecuteAsync");
        AssertDoesNotContain(source, "ProcessStartInfo");
        AssertDoesNotContain(source, "HttpClient");
        AssertDoesNotContain(source, "GitHub");
        AssertDoesNotContain(source, "Octokit");
    }

    [TestMethod]
    public void F03DoesNotIntroduceActionAuthorityFields()
    {
        var propertyNames = typeof(VisibilityPermissionBoundaryFixture)
            .GetProperties()
            .Select(static property => property.Name)
            .Concat(typeof(VisibilityPermissionEvidenceFixture).GetProperties().Select(static property => property.Name));

        AssertNoForbiddenActionNames(propertyNames);
    }

    [TestMethod]
    public void F03DoesNotIntroduceAccessGrantDecisionNames()
    {
        var source = StripStringsAndTestMethodNames(F03ChangedSource());

        AssertDoesNotContain(source, "AccessGranted");
        AssertDoesNotContain(source, "ActionAuthorized");
        AssertDoesNotContain(source, "MutationAuthorized");
        AssertDoesNotContain(source, "ReleaseAuthorized");
        AssertDoesNotContain(source, "DeploymentAuthorized");
    }

    [DataTestMethod]
    [DataRow("Visibility permission is not action authority.")]
    [DataRow("Being allowed to see a thing is not being allowed to do the thing.")]
    [DataRow("regression-only hard-stop")]
    [DataRow("access grant as action authority")]
    [DataRow("permission grant as action authority")]
    [DataRow("approval")]
    [DataRow("approval profile satisfaction")]
    [DataRow("policy satisfaction")]
    [DataRow("validation freshness")]
    [DataRow("source safety")]
    [DataRow("source apply authority")]
    [DataRow("commit authority")]
    [DataRow("push authority")]
    [DataRow("pull request authority")]
    [DataRow("ready-for-review authority")]
    [DataRow("merge authority")]
    [DataRow("release authority")]
    [DataRow("deployment authority")]
    [DataRow("rollback authority")]
    [DataRow("retry authority")]
    [DataRow("recovery authority")]
    [DataRow("workflow continuation")]
    [DataRow("memory promotion authority")]
    [DataRow("redaction bypass")]
    [DataRow("secret disclosure authority")]
    [DataRow("credential disclosure authority")]
    [DataRow("private reasoning disclosure authority")]
    [DataRow("raw payload disclosure authority")]
    [DataRow("automation autonomy")]
    public void ReceiptContainsBoundaryLines(string phrase)
    {
        var receipt = File.ReadAllText(Path.Combine(RootPath(), "Docs", "receipts", "F03_VISIBILITY_PERMISSION_NOT_ACTION_AUTHORITY.md"));

        StringAssert.Contains(receipt, phrase);
    }

    private static GovernanceRoleCatalog Catalog() => CatalogService().BuildDefaultCatalog();

    private static RoleVisibilityMatrix Matrix() => MatrixService().BuildDefaultMatrix(Catalog());

    private static RoleCatalogService CatalogService() => new();

    private static RoleVisibilityMatrixService MatrixService() => new();

    private static VisibilityPermissionEvidenceFixture FixtureFromRole(GovernanceRoleKind roleKind)
    {
        var role = Catalog().Entries.Single(entry => entry.RoleKind == roleKind);
        var entry = Matrix().Entries.First(matrixEntry =>
            matrixEntry.RoleId == role.RoleId &&
            matrixEntry.MaterialKind is not RoleVisibilityMaterialKind.RawPayload and
                not RoleVisibilityMaterialKind.CredentialMaterial and
                not RoleVisibilityMaterialKind.PrivateReasoning);

        return Fixture(entry.Surface, entry.MaterialKind, entry.VisibilityLevel, entry.SensitivityKind) with
        {
            RoleId = entry.RoleId,
            RoleKind = entry.RoleKind
        };
    }

    private static VisibilityPermissionEvidenceFixture Fixture(
        RoleVisibilitySurface surface,
        RoleVisibilityMaterialKind material,
        RoleVisibilityLevel level = RoleVisibilityLevel.SummaryOnly,
        RoleVisibilitySensitivityKind sensitivity = RoleVisibilitySensitivityKind.Normal) =>
        new()
        {
            RoleId = "role:f01:reviewer",
            RoleKind = GovernanceRoleKind.Reviewer,
            VisibilitySurface = surface,
            MaterialKind = material,
            SensitivityKind = sensitivity,
            VisibilityLevel = level,
            PermissionState = "VisibilityEvidenceOnly",
            RequiresSeparateRoleAssignment = true,
            RequiresSeparateVisibilityDecision = true,
            RequiresSeparatePolicyDecision = RoleVisibilityMatrixValidator.RequiresRedaction(material, sensitivity, level),
            RequiresSeparateRedaction = RoleVisibilityMatrixValidator.RequiresRedaction(material, sensitivity, level),
            EvidenceRefs = ["visibility-evidence:f03"],
            ClaimedStates = [],
            NextActions = ["request separate action authority if mutation is needed"],
            Boundary = VisibilityPermissionBoundaryFixture.Default,
            BoundaryStatement = "Visibility permission is not action authority."
        };

    private static void AssertLookupNonAuthority(IEnumerable<RoleVisibilityMatrixEntry> entries)
    {
        foreach (var fixture in entries.Select(entry => Fixture(entry.Surface, entry.MaterialKind, entry.VisibilityLevel, entry.SensitivityKind)))
        {
            AssertNoActionAuthority(fixture);
            Assert.IsTrue(fixture.RequiresSeparateRoleAssignment);
            Assert.IsTrue(fixture.RequiresSeparateVisibilityDecision);
        }
    }

    private static void AssertNoActionAuthority(VisibilityPermissionEvidenceFixture fixture)
    {
        Assert.IsTrue(fixture.Boundary.EvidenceOnly);
        Assert.IsFalse(fixture.Boundary.AccessGrant);
        Assert.IsFalse(fixture.Boundary.ActionAuthority);
        Assert.IsFalse(fixture.Boundary.ApprovalAuthority);
        Assert.IsFalse(fixture.Boundary.ApprovalProfileSatisfaction);
        Assert.IsFalse(fixture.Boundary.PolicySatisfaction);
        Assert.IsFalse(fixture.Boundary.ValidationFresh);
        Assert.IsFalse(fixture.Boundary.SourceSafe);
        Assert.IsFalse(fixture.Boundary.SourceApplyAuthority);
        Assert.IsFalse(fixture.Boundary.CommitAuthority);
        Assert.IsFalse(fixture.Boundary.PushAuthority);
        Assert.IsFalse(fixture.Boundary.PullRequestAuthority);
        Assert.IsFalse(fixture.Boundary.ReadyForReviewAuthority);
        Assert.IsFalse(fixture.Boundary.MergeAuthority);
        Assert.IsFalse(fixture.Boundary.ReleaseAuthority);
        Assert.IsFalse(fixture.Boundary.DeploymentAuthority);
        Assert.IsFalse(fixture.Boundary.RollbackAuthority);
        Assert.IsFalse(fixture.Boundary.RetryAuthority);
        Assert.IsFalse(fixture.Boundary.RecoveryAuthority);
        Assert.IsFalse(fixture.Boundary.WorkflowContinuationAuthority);
        Assert.IsFalse(fixture.Boundary.MemoryPromotionAuthority);
        Assert.IsFalse(fixture.Boundary.RedactionBypass);
        Assert.IsFalse(fixture.Boundary.SecretDisclosed);
        Assert.IsFalse(fixture.Boundary.CredentialDisclosed);
        Assert.IsFalse(fixture.Boundary.PrivateReasoningDisclosed);
        Assert.IsFalse(fixture.Boundary.RawPayloadDisclosed);
        Assert.IsFalse(fixture.Boundary.AutomationAutonomy);
    }

    private static void AssertAuthorityWarnings(RoleVisibilityMatrixValidationResult result)
    {
        CollectionAssert.Contains(result.Warnings.ToList(), "role visibility matrix does not grant access");
        CollectionAssert.Contains(result.Warnings.ToList(), "role visibility matrix does not authorize execution");
        CollectionAssert.Contains(result.Warnings.ToList(), "role visibility matrix does not authorize mutation");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not access control");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not execution authority");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not workflow continuation authority");
    }

    private static void AssertNoDetectorFinding(string text)
    {
        var fixture = Fixture(RoleVisibilitySurface.OperationStatus, RoleVisibilityMaterialKind.OperationStatusSummary) with
        {
            ClaimedStates = [text]
        };

        var findings = VisibilityPermissionAuthorityAttemptTestDetector.Detect(fixture);

        Assert.AreEqual(0, findings.Count, string.Join("; ", findings.Select(static finding => finding.Kind)));
    }

    private static void AssertNoForbiddenActionNames(IEnumerable<string> names)
    {
        var forbidden = new[]
        {
            "CanAct",
            "CanApply",
            "CanCommit",
            "CanPush",
            "CanOpenPullRequest",
            "CanMarkReadyForReview",
            "CanMerge",
            "CanRelease",
            "CanDeploy",
            "CanRollback",
            "CanRetry",
            "CanRecover",
            "CanContinueWorkflow",
            "CanApprove",
            "CanSatisfyPolicy",
            "CanBypassRedaction",
            "CanViewSecrets",
            "CanViewPrivateReasoning",
            "ActionAuthorized",
            "MutationAuthorized",
            "ApprovalGranted",
            "PolicySatisfied",
            "ReleaseAuthorized",
            "DeploymentAuthorized",
            "WorkflowContinuationAuthorized",
            "RedactionBypassed"
        };

        foreach (var name in names)
        {
            CollectionAssert.DoesNotContain(forbidden, name);
        }
    }

    private static string F03ChangedSource() =>
        string.Join(Environment.NewLine, F03AllowedFileNames()
            .Where(static path => path.EndsWith(".cs", StringComparison.Ordinal))
            .Select(path => Path.Combine(RootPath(), path.Replace('/', Path.DirectorySeparatorChar)))
            .Where(File.Exists)
            .Select(File.ReadAllText));

    private static IReadOnlyList<string> F03AllowedFileNames() =>
    [
        "IronDev.IntegrationTests/BlockF03VisibilityPermissionNotActionAuthorityTests.cs",
        "Docs/receipts/F03_VISIBILITY_PERMISSION_NOT_ACTION_AUTHORITY.md"
    ];

    private static string StripStringsAndTestMethodNames(string source)
    {
        var withoutStrings = System.Text.RegularExpressions.Regex.Replace(source, "\"(?:\\\\.|[^\"])*\"", "\"\"");
        return System.Text.RegularExpressions.Regex.Replace(
            withoutStrings,
            "\\bpublic\\s+void\\s+[A-Za-z0-9_]+\\s*\\(",
            "public void Test(");
    }

    private static string RootPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Solution root not found.");
    }

    private static void AssertDoesNotContain(string source, string forbidden) =>
        Assert.IsFalse(
            source.Contains(forbidden, StringComparison.Ordinal),
            $"Unexpected forbidden marker found in F03 source: {forbidden}");

    private sealed record VisibilityPermissionEvidenceFixture
    {
        public required string RoleId { get; init; }
        public required GovernanceRoleKind RoleKind { get; init; }
        public required RoleVisibilitySurface VisibilitySurface { get; init; }
        public required RoleVisibilityMaterialKind MaterialKind { get; init; }
        public required RoleVisibilitySensitivityKind SensitivityKind { get; init; }
        public required RoleVisibilityLevel VisibilityLevel { get; init; }
        public required string PermissionState { get; init; }
        public required bool RequiresSeparateRoleAssignment { get; init; }
        public required bool RequiresSeparateVisibilityDecision { get; init; }
        public required bool RequiresSeparatePolicyDecision { get; init; }
        public required bool RequiresSeparateRedaction { get; init; }
        public required IReadOnlyList<string> EvidenceRefs { get; init; }
        public required IReadOnlyList<string> ClaimedStates { get; init; }
        public required IReadOnlyList<string> NextActions { get; init; }
        public required VisibilityPermissionBoundaryFixture Boundary { get; init; }
        public required string BoundaryStatement { get; init; }

        public VisibilityPermissionEvidenceFixture WithBoundary(string boundary) =>
            this with
            {
                BoundaryStatement = boundary,
                Boundary = Boundary with
                {
                    BoundaryStatement = boundary,
                    RequiredSeparateEvidence = Boundary.RequiredSeparateEvidence
                        .Concat([boundary])
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()
                }
            };
    }

    private sealed record VisibilityPermissionBoundaryFixture
    {
        public static VisibilityPermissionBoundaryFixture Default { get; } = new()
        {
            EvidenceOnly = true,
            AccessGrant = false,
            ActionAuthority = false,
            ApprovalAuthority = false,
            ApprovalProfileSatisfaction = false,
            PolicySatisfaction = false,
            SecurityApprovalSatisfaction = false,
            ValidationFresh = false,
            SourceSafe = false,
            SourceApplyAuthority = false,
            CommitAuthority = false,
            PushAuthority = false,
            PullRequestAuthority = false,
            ReadyForReviewAuthority = false,
            MergeAuthority = false,
            ReleaseAuthority = false,
            DeploymentAuthority = false,
            RollbackAuthority = false,
            RetryAuthority = false,
            RecoveryAuthority = false,
            WorkflowContinuationAuthority = false,
            MemoryPromotionAuthority = false,
            RedactionBypass = false,
            DetailAccess = false,
            SecretDisclosed = false,
            CredentialDisclosed = false,
            PrivateReasoningDisclosed = false,
            RawPayloadDisclosed = false,
            AutomationAutonomy = false,
            BackendAuthority = false,
            ExecutionAuthority = false,
            BoundaryStatement = "Visibility permission is not action authority.",
            RequiredSeparateEvidence =
            [
                "SeparateRoleAssignmentRequired",
                "SeparateVisibilityDecisionRequired",
                "SeparateActionAuthorityRequired",
                "SeparateApprovalRequired",
                "SeparatePolicyDecisionRequired",
                "SeparateMutationAuthorityRequired",
                "SeparateWorkflowAuthorityRequired",
                "SeparateRedactionEnforcementRequired"
            ],
            ForbiddenImplications =
            [
                "ActionAuthority",
                "ApprovalAuthority",
                "PolicySatisfaction",
                "ExecutionAuthority",
                "ReleaseAuthority",
                "DeploymentAuthority",
                "RollbackAuthority",
                "RecoveryAuthority",
                "AutomationAutonomy",
                "BackendAuthority"
            ]
        };

        public required bool EvidenceOnly { get; init; }
        public required bool AccessGrant { get; init; }
        public required bool ActionAuthority { get; init; }
        public required bool ApprovalAuthority { get; init; }
        public required bool ApprovalProfileSatisfaction { get; init; }
        public required bool PolicySatisfaction { get; init; }
        public required bool SecurityApprovalSatisfaction { get; init; }
        public required bool ValidationFresh { get; init; }
        public required bool SourceSafe { get; init; }
        public required bool SourceApplyAuthority { get; init; }
        public required bool CommitAuthority { get; init; }
        public required bool PushAuthority { get; init; }
        public required bool PullRequestAuthority { get; init; }
        public required bool ReadyForReviewAuthority { get; init; }
        public required bool MergeAuthority { get; init; }
        public required bool ReleaseAuthority { get; init; }
        public required bool DeploymentAuthority { get; init; }
        public required bool RollbackAuthority { get; init; }
        public required bool RetryAuthority { get; init; }
        public required bool RecoveryAuthority { get; init; }
        public required bool WorkflowContinuationAuthority { get; init; }
        public required bool MemoryPromotionAuthority { get; init; }
        public required bool RedactionBypass { get; init; }
        public required bool DetailAccess { get; init; }
        public required bool SecretDisclosed { get; init; }
        public required bool CredentialDisclosed { get; init; }
        public required bool PrivateReasoningDisclosed { get; init; }
        public required bool RawPayloadDisclosed { get; init; }
        public required bool AutomationAutonomy { get; init; }
        public required bool BackendAuthority { get; init; }
        public required bool ExecutionAuthority { get; init; }
        public required string BoundaryStatement { get; init; }
        public required IReadOnlyList<string> RequiredSeparateEvidence { get; init; }
        public required IReadOnlyList<string> ForbiddenImplications { get; init; }
    }

    private sealed record VisibilityPermissionFinding(VisibilityPermissionFindingKind Kind, string Source);

    public enum VisibilityPermissionFindingKind
    {
        ApplyAuthorityAttempt,
        CommitAuthorityAttempt,
        PushAuthorityAttempt,
        PullRequestAuthorityAttempt,
        ReadyForReviewAuthorityAttempt,
        MergeAuthorityAttempt,
        ReleaseAuthorityAttempt,
        DeploymentAuthorityAttempt,
        RollbackAuthorityAttempt,
        RetryAuthorityAttempt,
        RecoveryAuthorityAttempt,
        WorkflowContinuationAuthorityAttempt,
        ApprovalAuthorityAttempt,
        PolicySatisfactionAttempt,
        ValidationFreshnessAttempt,
        SourceSafetyAttempt,
        RedactionBypassAttempt,
        SecretDisclosureAttempt,
        PrivateReasoningDisclosureAttempt,
        AccessGrantAttempt,
        PermissionGrantAttempt,
        ActionAuthorityAttempt
    }

    private static class VisibilityPermissionAuthorityAttemptTestDetector
    {
        private static readonly IReadOnlyDictionary<string, VisibilityPermissionFindingKind> Markers =
            new Dictionary<string, VisibilityPermissionFindingKind>(StringComparer.OrdinalIgnoreCase)
            {
                ["can apply"] = VisibilityPermissionFindingKind.ApplyAuthorityAttempt,
                ["can commit"] = VisibilityPermissionFindingKind.CommitAuthorityAttempt,
                ["can push"] = VisibilityPermissionFindingKind.PushAuthorityAttempt,
                ["can open pull request"] = VisibilityPermissionFindingKind.PullRequestAuthorityAttempt,
                ["can mark ready for review"] = VisibilityPermissionFindingKind.ReadyForReviewAuthorityAttempt,
                ["can merge"] = VisibilityPermissionFindingKind.MergeAuthorityAttempt,
                ["can release"] = VisibilityPermissionFindingKind.ReleaseAuthorityAttempt,
                ["can deploy"] = VisibilityPermissionFindingKind.DeploymentAuthorityAttempt,
                ["can rollback"] = VisibilityPermissionFindingKind.RollbackAuthorityAttempt,
                ["can retry"] = VisibilityPermissionFindingKind.RetryAuthorityAttempt,
                ["can recover"] = VisibilityPermissionFindingKind.RecoveryAuthorityAttempt,
                ["can continue workflow"] = VisibilityPermissionFindingKind.WorkflowContinuationAuthorityAttempt,
                ["can approve"] = VisibilityPermissionFindingKind.ApprovalAuthorityAttempt,
                ["can satisfy policy"] = VisibilityPermissionFindingKind.PolicySatisfactionAttempt,
                ["can bypass redaction"] = VisibilityPermissionFindingKind.RedactionBypassAttempt,
                ["can view secrets"] = VisibilityPermissionFindingKind.SecretDisclosureAttempt,
                ["can view private reasoning"] = VisibilityPermissionFindingKind.PrivateReasoningDisclosureAttempt,
                ["action authorized"] = VisibilityPermissionFindingKind.ActionAuthorityAttempt,
                ["mutation authorized"] = VisibilityPermissionFindingKind.SourceSafetyAttempt,
                ["approval granted"] = VisibilityPermissionFindingKind.ApprovalAuthorityAttempt,
                ["policy satisfied"] = VisibilityPermissionFindingKind.PolicySatisfactionAttempt,
                ["release authorized"] = VisibilityPermissionFindingKind.ReleaseAuthorityAttempt,
                ["deployment authorized"] = VisibilityPermissionFindingKind.DeploymentAuthorityAttempt,
                ["workflow continuation authorized"] = VisibilityPermissionFindingKind.WorkflowContinuationAuthorityAttempt,
                ["redaction bypassed"] = VisibilityPermissionFindingKind.RedactionBypassAttempt
            };

        public static IReadOnlyList<VisibilityPermissionFinding> Detect(VisibilityPermissionEvidenceFixture fixture)
        {
            var values = fixture.ClaimedStates
                .Concat(fixture.NextActions)
                .Concat(fixture.EvidenceRefs)
                .Append(fixture.BoundaryStatement)
                .Append(fixture.Boundary.BoundaryStatement);

            return values
                .SelectMany(FindingsFor)
                .Distinct()
                .ToArray();
        }

        private static IEnumerable<VisibilityPermissionFinding> FindingsFor(string value)
        {
            foreach (var marker in Markers)
            {
                if (value.Contains(marker.Key, StringComparison.OrdinalIgnoreCase))
                {
                    yield return new VisibilityPermissionFinding(marker.Value, marker.Key);
                }
            }
        }
    }
}
