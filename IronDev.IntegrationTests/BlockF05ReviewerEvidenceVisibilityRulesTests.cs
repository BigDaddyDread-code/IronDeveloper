using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockF05ReviewerEvidenceVisibilityRulesTests
{
    [TestMethod]
    public void ReviewerProposalSummaryMayProceedOnlyToSeparateEvidenceVisibilityDecision()
    {
        var decision = Service().Evaluate(Catalog(), Matrix(), Request());

        Assert.AreEqual(ReviewerEvidenceVisibilityDecisionKind.MayProceedToSeparateEvidenceVisibilityDecision, decision.Decision);
        Assert.AreEqual(ReviewerEvidenceVisibilityBlockKind.None, decision.BlockKind);
        Assert.IsTrue(decision.RequiresSeparateReviewerAssignment);
        Assert.IsTrue(decision.RequiresSeparateReviewerEvidenceRequest);
        Assert.IsTrue(decision.RequiresSeparateVisibilityDecision);
        Assert.IsTrue(decision.RequiresSeparateApproval);
        Assert.IsTrue(decision.RequiresSeparatePolicySatisfaction);
        Assert.IsTrue(decision.RequiresSeparateActionAuthority);
        Assert.IsTrue(decision.RequiresSeparateMutationAuthority);
        Assert.IsTrue(decision.RequiresSeparateWorkflowAuthority);
        Assert.IsFalse(decision.RequiresHumanReview);
        AssertRequiredWarnings(decision);
        AssertRequiredForbiddenImplications(decision);
    }

    [DataTestMethod]
    [DataRow(GovernanceRoleKind.Reviewer, RoleVisibilitySurface.ValidationReview, RoleVisibilityMaterialKind.ValidationEvidenceRefs, ReviewerEvidenceVisibilityIntentKind.ReadEvidenceReference)]
    [DataRow(GovernanceRoleKind.Reviewer, RoleVisibilitySurface.PullRequest, RoleVisibilityMaterialKind.PullRequestDiffSummary, ReviewerEvidenceVisibilityIntentKind.ReadReviewContext)]
    [DataRow(GovernanceRoleKind.SecurityReviewer, RoleVisibilitySurface.SourceApply, RoleVisibilityMaterialKind.PatchMetadata, ReviewerEvidenceVisibilityIntentKind.ReadEvidenceMetadata)]
    [DataRow(GovernanceRoleKind.ReleaseReviewer, RoleVisibilitySurface.ReleaseReadiness, RoleVisibilityMaterialKind.ReleaseReadinessSummary, ReviewerEvidenceVisibilityIntentKind.ReadEvidenceSummary)]
    [DataRow(GovernanceRoleKind.OperationsReviewer, RoleVisibilitySurface.DeploymentReadiness, RoleVisibilityMaterialKind.DeploymentReadinessSummary, ReviewerEvidenceVisibilityIntentKind.ReadEvidenceSummary)]
    [DataRow(GovernanceRoleKind.RollbackReviewer, RoleVisibilitySurface.Rollback, RoleVisibilityMaterialKind.RollbackSummary, ReviewerEvidenceVisibilityIntentKind.ReadEvidenceSummary)]
    [DataRow(GovernanceRoleKind.RecoveryReviewer, RoleVisibilitySurface.Retry, RoleVisibilityMaterialKind.RetrySummary, ReviewerEvidenceVisibilityIntentKind.ReadEvidenceSummary)]
    public void ReviewerRoleAllowedMatrixEvidenceMayProceedToSeparateVisibilityDecision(
        GovernanceRoleKind roleKind,
        RoleVisibilitySurface surface,
        RoleVisibilityMaterialKind materialKind,
        ReviewerEvidenceVisibilityIntentKind intent)
    {
        var (catalog, matrix, entry) = Fixture(roleKind, surface, materialKind);
        var decision = Service().Evaluate(catalog, matrix, Request(catalog, matrix, entry) with { IntentKind = intent });

        Assert.AreEqual(ReviewerEvidenceVisibilityDecisionKind.MayProceedToSeparateEvidenceVisibilityDecision, decision.Decision);
        Assert.AreEqual(ReviewerEvidenceVisibilityBlockKind.None, decision.BlockKind);
        AssertRequiredWarnings(decision);
        AssertRequiredForbiddenImplications(decision);
    }

    [TestMethod]
    public void SecurityReviewerSensitiveEvidenceRequiresPolicyAndRedaction()
    {
        var (catalog, matrix, entry) = Fixture(
            GovernanceRoleKind.SecurityReviewer,
            RoleVisibilitySurface.ValidationReview,
            RoleVisibilityMaterialKind.SecretScanSummary);

        var decision = Service().Evaluate(catalog, matrix, Request(catalog, matrix, entry) with
        {
            IntentKind = ReviewerEvidenceVisibilityIntentKind.ReadRedactedEvidence,
            PolicyDecisionEvidenceRef = null,
            RedactionEvidenceRef = null
        });

        Assert.AreEqual(ReviewerEvidenceVisibilityDecisionKind.BlockedBySensitiveEvidencePolicyMissing, decision.Decision);
        Assert.AreEqual(ReviewerEvidenceVisibilityBlockKind.SensitiveEvidencePolicyMissing, decision.BlockKind);
        Assert.IsTrue(decision.RequiresSeparatePolicyDecision);
        Assert.IsTrue(decision.RequiresSeparateRedactionEnforcement);
        Assert.IsTrue(decision.RequiresHumanReview);
    }

    [TestMethod]
    public void SecurityReviewerSensitiveEvidenceMayProceedWithSeparatePolicyAndRedaction()
    {
        var (catalog, matrix, entry) = Fixture(
            GovernanceRoleKind.SecurityReviewer,
            RoleVisibilitySurface.ValidationReview,
            RoleVisibilityMaterialKind.SecretScanSummary);

        var decision = Service().Evaluate(catalog, matrix, Request(catalog, matrix, entry) with
        {
            IntentKind = ReviewerEvidenceVisibilityIntentKind.ReadRedactedEvidence
        });

        Assert.AreEqual(ReviewerEvidenceVisibilityDecisionKind.MayProceedToSeparateEvidenceVisibilityDecision, decision.Decision);
        Assert.IsTrue(decision.RequiresSeparatePolicyDecision);
        Assert.IsTrue(decision.RequiresSeparateRedactionEnforcement);
    }

    [TestMethod]
    public void NonReviewerRoleIsBlocked()
    {
        var (catalog, matrix, entry) = Fixture(
            GovernanceRoleKind.Observer,
            RoleVisibilitySurface.OperationStatus,
            RoleVisibilityMaterialKind.OperationStatusSummary);

        var decision = Service().Evaluate(catalog, matrix, Request(catalog, matrix, entry));

        Assert.AreEqual(ReviewerEvidenceVisibilityDecisionKind.BlockedByNonReviewerRole, decision.Decision);
        Assert.AreEqual(ReviewerEvidenceVisibilityBlockKind.NonReviewerRole, decision.BlockKind);
    }

    [TestMethod]
    public void UnknownReviewerRoleIsBlocked()
    {
        var decision = Service().Evaluate(Catalog(), Matrix(), Request() with { ReviewerRoleId = "role:f01:missing" });

        Assert.AreEqual(ReviewerEvidenceVisibilityDecisionKind.BlockedByUnknownReviewerRole, decision.Decision);
        Assert.AreEqual(ReviewerEvidenceVisibilityBlockKind.UnknownReviewerRole, decision.BlockKind);
    }

    [TestMethod]
    public void ReviewerCannotSeeEvidenceNotAllowedByMatrix()
    {
        var entry = Entry(
            GovernanceRoleKind.SecurityReviewer,
            RoleVisibilitySurface.SourceApply,
            RoleVisibilityMaterialKind.PatchMetadata);

        var decision = Service().Evaluate(Catalog(), Matrix(), Request() with
        {
            EvidenceSurface = entry.Surface,
            EvidenceMaterialKind = entry.MaterialKind,
            EvidenceVisibilityLevel = entry.VisibilityLevel,
            EvidenceSensitivityKind = entry.SensitivityKind
        });

        Assert.AreEqual(ReviewerEvidenceVisibilityDecisionKind.BlockedByReviewerEvidenceNotAllowed, decision.Decision);
        Assert.AreEqual(ReviewerEvidenceVisibilityBlockKind.EvidenceNotAllowed, decision.BlockKind);
    }

    [TestMethod]
    public void HiddenRawPayloadEvidenceIsBlocked()
    {
        var (catalog, matrix, entry) = Fixture(
            GovernanceRoleKind.Reviewer,
            RoleVisibilitySurface.FrontendReadOnly,
            RoleVisibilityMaterialKind.RawPayload);

        var decision = Service().Evaluate(catalog, matrix, Request(catalog, matrix, entry));

        Assert.AreEqual(ReviewerEvidenceVisibilityDecisionKind.BlockedByRawOrSecretEvidence, decision.Decision);
        Assert.AreEqual(ReviewerEvidenceVisibilityBlockKind.RawOrSecretEvidence, decision.BlockKind);
        Assert.IsTrue(decision.RequiresHumanReview);
    }

    [TestMethod]
    public void MissingReviewerAssignmentEvidenceBlocks()
    {
        var decision = Service().Evaluate(Catalog(), Matrix(), Request() with { ReviewerAssignmentEvidenceRef = null });

        Assert.AreEqual(ReviewerEvidenceVisibilityDecisionKind.BlockedByMissingEvidence, decision.Decision);
        Assert.AreEqual(ReviewerEvidenceVisibilityBlockKind.MissingEvidence, decision.BlockKind);
        Assert.IsTrue(decision.RequiresSeparateReviewerAssignment);
    }

    [TestMethod]
    public void MissingReviewerEvidenceRequestBlocks()
    {
        var decision = Service().Evaluate(Catalog(), Matrix(), Request() with { ReviewerEvidenceRequestRef = null });

        Assert.AreEqual(ReviewerEvidenceVisibilityDecisionKind.BlockedByMissingEvidence, decision.Decision);
        Assert.AreEqual(ReviewerEvidenceVisibilityBlockKind.MissingEvidence, decision.BlockKind);
        Assert.IsTrue(decision.RequiresSeparateReviewerEvidenceRequest);
    }

    [TestMethod]
    public void MissingVisibilityDecisionEvidenceBlocks()
    {
        var decision = Service().Evaluate(Catalog(), Matrix(), Request() with { VisibilityDecisionEvidenceRef = null });

        Assert.AreEqual(ReviewerEvidenceVisibilityDecisionKind.BlockedByMissingEvidence, decision.Decision);
        Assert.AreEqual(ReviewerEvidenceVisibilityBlockKind.MissingEvidence, decision.BlockKind);
        Assert.IsTrue(decision.RequiresSeparateVisibilityDecision);
    }

    [TestMethod]
    public void CatalogMismatchBlocks()
    {
        var decision = Service().Evaluate(Catalog(), Matrix(), Request() with { RoleCatalogVersion = "f05" });

        Assert.AreEqual(ReviewerEvidenceVisibilityDecisionKind.BlockedByRoleVisibilityMismatch, decision.Decision);
        Assert.AreEqual(ReviewerEvidenceVisibilityBlockKind.RoleVisibilityMismatch, decision.BlockKind);
    }

    [TestMethod]
    public void InvalidCatalogBlocks()
    {
        var decision = Service().Evaluate(Catalog() with { Entries = [] }, Matrix(), Request());

        Assert.AreEqual(ReviewerEvidenceVisibilityDecisionKind.BlockedByInvalidRoleCatalog, decision.Decision);
        Assert.AreEqual(ReviewerEvidenceVisibilityBlockKind.InvalidCatalog, decision.BlockKind);
    }

    [TestMethod]
    public void InvalidMatrixBlocks()
    {
        var decision = Service().Evaluate(Catalog(), Matrix() with { Entries = [] }, Request());

        Assert.AreEqual(ReviewerEvidenceVisibilityDecisionKind.BlockedByInvalidVisibilityMatrix, decision.Decision);
        Assert.AreEqual(ReviewerEvidenceVisibilityBlockKind.InvalidMatrix, decision.BlockKind);
    }

    [DataTestMethod]
    [DataRow(ReviewerEvidenceVisibilityIntentKind.ActionSourceApply, ReviewerEvidenceVisibilityDecisionKind.BlockedByActionAuthorityAttempt, ReviewerEvidenceVisibilityBlockKind.ActionAuthorityAttempt)]
    [DataRow(ReviewerEvidenceVisibilityIntentKind.ActionCommit, ReviewerEvidenceVisibilityDecisionKind.BlockedByActionAuthorityAttempt, ReviewerEvidenceVisibilityBlockKind.ActionAuthorityAttempt)]
    [DataRow(ReviewerEvidenceVisibilityIntentKind.ActionPush, ReviewerEvidenceVisibilityDecisionKind.BlockedByActionAuthorityAttempt, ReviewerEvidenceVisibilityBlockKind.ActionAuthorityAttempt)]
    [DataRow(ReviewerEvidenceVisibilityIntentKind.ActionPullRequest, ReviewerEvidenceVisibilityDecisionKind.BlockedByActionAuthorityAttempt, ReviewerEvidenceVisibilityBlockKind.ActionAuthorityAttempt)]
    [DataRow(ReviewerEvidenceVisibilityIntentKind.ActionReadyForReview, ReviewerEvidenceVisibilityDecisionKind.BlockedByActionAuthorityAttempt, ReviewerEvidenceVisibilityBlockKind.ActionAuthorityAttempt)]
    [DataRow(ReviewerEvidenceVisibilityIntentKind.ActionRequestReviewers, ReviewerEvidenceVisibilityDecisionKind.BlockedByActionAuthorityAttempt, ReviewerEvidenceVisibilityBlockKind.ActionAuthorityAttempt)]
    [DataRow(ReviewerEvidenceVisibilityIntentKind.ActionMerge, ReviewerEvidenceVisibilityDecisionKind.BlockedByActionAuthorityAttempt, ReviewerEvidenceVisibilityBlockKind.ActionAuthorityAttempt)]
    [DataRow(ReviewerEvidenceVisibilityIntentKind.ActionRelease, ReviewerEvidenceVisibilityDecisionKind.BlockedByActionAuthorityAttempt, ReviewerEvidenceVisibilityBlockKind.ActionAuthorityAttempt)]
    [DataRow(ReviewerEvidenceVisibilityIntentKind.ActionDeploy, ReviewerEvidenceVisibilityDecisionKind.BlockedByActionAuthorityAttempt, ReviewerEvidenceVisibilityBlockKind.ActionAuthorityAttempt)]
    [DataRow(ReviewerEvidenceVisibilityIntentKind.ActionRollback, ReviewerEvidenceVisibilityDecisionKind.BlockedByActionAuthorityAttempt, ReviewerEvidenceVisibilityBlockKind.ActionAuthorityAttempt)]
    [DataRow(ReviewerEvidenceVisibilityIntentKind.ActionRetry, ReviewerEvidenceVisibilityDecisionKind.BlockedByActionAuthorityAttempt, ReviewerEvidenceVisibilityBlockKind.ActionAuthorityAttempt)]
    [DataRow(ReviewerEvidenceVisibilityIntentKind.ActionRecover, ReviewerEvidenceVisibilityDecisionKind.BlockedByActionAuthorityAttempt, ReviewerEvidenceVisibilityBlockKind.ActionAuthorityAttempt)]
    [DataRow(ReviewerEvidenceVisibilityIntentKind.ActionApprove, ReviewerEvidenceVisibilityDecisionKind.BlockedByApprovalIntent, ReviewerEvidenceVisibilityBlockKind.ApprovalIntent)]
    [DataRow(ReviewerEvidenceVisibilityIntentKind.ActionSatisfyPolicy, ReviewerEvidenceVisibilityDecisionKind.BlockedByPolicyIntent, ReviewerEvidenceVisibilityBlockKind.PolicyIntent)]
    [DataRow(ReviewerEvidenceVisibilityIntentKind.ActionContinueWorkflow, ReviewerEvidenceVisibilityDecisionKind.BlockedByWorkflowContinuationIntent, ReviewerEvidenceVisibilityBlockKind.WorkflowContinuationIntent)]
    [DataRow(ReviewerEvidenceVisibilityIntentKind.ActionBypassRedaction, ReviewerEvidenceVisibilityDecisionKind.BlockedByRedactionBypassIntent, ReviewerEvidenceVisibilityBlockKind.RedactionBypassIntent)]
    [DataRow(ReviewerEvidenceVisibilityIntentKind.ActionDiscloseRawPayload, ReviewerEvidenceVisibilityDecisionKind.BlockedBySensitiveDisclosureIntent, ReviewerEvidenceVisibilityBlockKind.SensitiveDisclosureIntent)]
    [DataRow(ReviewerEvidenceVisibilityIntentKind.ActionDiscloseCredential, ReviewerEvidenceVisibilityDecisionKind.BlockedBySensitiveDisclosureIntent, ReviewerEvidenceVisibilityBlockKind.SensitiveDisclosureIntent)]
    [DataRow(ReviewerEvidenceVisibilityIntentKind.ActionDisclosePrivateReasoning, ReviewerEvidenceVisibilityDecisionKind.BlockedBySensitiveDisclosureIntent, ReviewerEvidenceVisibilityBlockKind.SensitiveDisclosureIntent)]
    public void ReviewerEvidenceVisibilityBlocksActionIntent(
        ReviewerEvidenceVisibilityIntentKind intent,
        ReviewerEvidenceVisibilityDecisionKind expectedDecision,
        ReviewerEvidenceVisibilityBlockKind expectedBlock)
    {
        var decision = Service().Evaluate(Catalog(), Matrix(), Request() with { IntentKind = intent });

        Assert.AreEqual(expectedDecision, decision.Decision);
        Assert.AreEqual(expectedBlock, decision.BlockKind);
        Assert.IsTrue(decision.RequiresSeparateActionAuthority);
        AssertRequiredWarnings(decision);
        AssertRequiredForbiddenImplications(decision);
    }

    [TestMethod]
    public void UnsafePayloadBlocksAndDoesNotEchoRawDiff()
    {
        var rawDiff = string.Join(" ", "raw", "diff");

        var decision = Service().Evaluate(Catalog(), Matrix(), Request() with { EvidenceRef = rawDiff });

        Assert.AreEqual(ReviewerEvidenceVisibilityDecisionKind.BlockedByUnsafePayload, decision.Decision);
        Assert.AreEqual(ReviewerEvidenceVisibilityBlockKind.UnsafePayload, decision.BlockKind);
        Assert.AreEqual("[unsafe-rejected]", decision.MatchedEvidenceRef);
        Assert.IsFalse(decision.RecordFingerprint.Contains(rawDiff, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void UnsafeCredentialMarkerBlocksAndDoesNotEchoMaterial()
    {
        var marker = string.Concat("to", "ken", "=", "value");

        var decision = Service().Evaluate(Catalog(), Matrix(), Request() with { EvidenceSubjectRef = marker });

        Assert.AreEqual(ReviewerEvidenceVisibilityDecisionKind.BlockedByUnsafePayload, decision.Decision);
        Assert.AreEqual("[unsafe-rejected]", decision.MatchedEvidenceSubjectRef);
        Assert.IsFalse(decision.RecordFingerprint.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    [DataTestMethod]
    [DataRow("proposal-summary:f05")]
    [DataRow("validation-evidence-refs:f05")]
    [DataRow("pull-request-metadata:f05")]
    [DataRow("release-readiness-summary:f05")]
    [DataRow("deployment-readiness-summary:f05")]
    public void SafeReviewerEvidenceRefsAreNotRejected(string value)
    {
        Assert.IsFalse(ReviewerEvidenceVisibilityValidator.ContainsUnsafeEvidenceVisibilityText(value));
    }

    [TestMethod]
    public void NullRequestValidationFailsClosed()
    {
        var result = ReviewerEvidenceVisibilityValidator.ValidateRequest(null);

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToList(), "ReviewerEvidenceVisibilityRequestRequired");
    }

    [TestMethod]
    public void ValidationRequiresUtcObservation()
    {
        var result = ReviewerEvidenceVisibilityValidator.ValidateRequest(
            Request() with { ObservedAtUtc = new DateTimeOffset(2026, 6, 27, 1, 0, 0, TimeSpan.FromHours(1)) });

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToList(), "ObservedAtUtcMustBeUtc");
    }

    [TestMethod]
    public void DecisionModelDoesNotExposeGrantOrAuthorityFlags()
    {
        var forbiddenNames = new[]
        {
            "CanApprove",
            "CanSatisfyPolicy",
            "CanApply",
            "CanCommit",
            "CanPush",
            "CanCreatePullRequest",
            "CanMarkReadyForReview",
            "CanRequestReviewers",
            "CanMerge",
            "CanRelease",
            "CanDeploy",
            "CanRollback",
            "CanRetry",
            "CanRecover",
            "CanContinueWorkflow",
            "GrantsAccess",
            "GrantsPermission",
            "GrantsAuthority",
            "IsAuthorized"
        };

        var propertyNames = typeof(ReviewerEvidenceVisibilityDecision)
            .GetProperties()
            .Select(static property => property.Name)
            .ToArray();

        foreach (var forbiddenName in forbiddenNames)
        {
            CollectionAssert.DoesNotContain(propertyNames, forbiddenName);
        }
    }

    [TestMethod]
    public void StaticScanF05CoreAddsNoExecutorProviderOrMutationSurface()
    {
        var source = F05CoreSourceWithoutStrings();
        var forbidden = new[]
        {
            "RunProcessAsync",
            "ProcessStartInfo",
            "HttpClient",
            "File.Write",
            "File.Read",
            "Directory.",
            "git ",
            "gh ",
            "dotnet ",
            "ExecuteAsync",
            "ApplyAsync",
            "CommitAsync",
            "PushAsync",
            "MergeAsync",
            "ReleaseAsync",
            "DeployAsync",
            "ContinueWorkflowAsync",
            "PromoteMemoryAsync",
            "AuthorizeAsync",
            "AccessGranted",
            "PermissionGranted"
        };

        foreach (var marker in forbidden)
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    [TestMethod]
    public void ReceiptExistsAndStatesReviewerVisibilityCannotApproveOrMutate()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "F05_REVIEWER_EVIDENCE_VISIBILITY_RULES.md"));

        StringAssert.Contains(receipt, "Reviewer evidence visibility is not review approval.");
        StringAssert.Contains(receipt, "Seeing evidence is not judging it.");
        StringAssert.Contains(receipt, "does not approve work");
        StringAssert.Contains(receipt, "does not satisfy policy");
        StringAssert.Contains(receipt, "does not authorize mutation");
        StringAssert.Contains(receipt, "does not bypass redaction");
    }

    private static ReviewerEvidenceVisibilityService Service() => new();

    private static GovernanceRoleCatalog Catalog() => new RoleCatalogService().BuildDefaultCatalog();

    private static RoleVisibilityMatrix Matrix() => Matrix(Catalog());

    private static RoleVisibilityMatrix Matrix(GovernanceRoleCatalog catalog) =>
        new RoleVisibilityMatrixService().BuildDefaultMatrix(catalog);

    private static (GovernanceRoleCatalog Catalog, RoleVisibilityMatrix Matrix, RoleVisibilityMatrixEntry Entry) Fixture(
        GovernanceRoleKind roleKind,
        RoleVisibilitySurface surface,
        RoleVisibilityMaterialKind materialKind)
    {
        var catalog = Catalog();
        var matrix = Matrix(catalog);
        var entry = matrix.Entries.Single(item =>
            item.RoleKind == roleKind &&
            item.Surface == surface &&
            item.MaterialKind == materialKind);
        return (catalog, matrix, entry);
    }

    private static ReviewerEvidenceVisibilityRequest Request() =>
        Request(Catalog(), Matrix(), Entry());

    private static ReviewerEvidenceVisibilityRequest Request(
        GovernanceRoleCatalog catalog,
        RoleVisibilityMatrix matrix,
        RoleVisibilityMatrixEntry entry)
    {
        var role = catalog.Entries.Single(item => string.Equals(item.RoleId, entry.RoleId, StringComparison.Ordinal));
        return new ReviewerEvidenceVisibilityRequest
        {
            TenantId = "tenant-f05",
            ProjectId = "project-f05",
            OperationId = "operation-f05",
            CorrelationId = "correlation-f05",
            ReviewerRoleId = role.RoleId,
            ReviewerRoleKind = role.RoleKind,
            ReviewerRoleScopeKind = role.ScopeKind,
            EvidenceSurface = entry.Surface,
            EvidenceMaterialKind = entry.MaterialKind,
            EvidenceSensitivityKind = entry.SensitivityKind,
            EvidenceVisibilityLevel = entry.VisibilityLevel,
            IntentKind = ReviewerEvidenceVisibilityIntentKind.ReadEvidenceSummary,
            EvidenceRef = "evidence:f05",
            EvidenceSubjectRef = "subject:f05",
            RoleCatalogId = catalog.CatalogId,
            RoleCatalogVersion = catalog.CatalogVersion,
            RoleCatalogEntryRef = "role-entry:f05",
            VisibilityMatrixId = matrix.MatrixId,
            VisibilityMatrixVersion = matrix.CatalogVersion,
            VisibilityMatrixEntryRef = "matrix-entry:f05",
            ReviewerAssignmentEvidenceRef = "reviewer-assignment:f05",
            ReviewerEvidenceRequestRef = "reviewer-evidence-request:f05",
            VisibilityDecisionEvidenceRef = "visibility-decision:f05",
            PolicyDecisionEvidenceRef = "policy-decision:f05",
            RedactionEvidenceRef = "redaction:f05",
            ReasonCode = "reviewer-evidence-visibility",
            Source = "f05-test",
            ObservedAtUtc = new DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero)
        };
    }

    private static RoleVisibilityMatrixEntry Entry() =>
        Entry(
            GovernanceRoleKind.Reviewer,
            RoleVisibilitySurface.Proposal,
            RoleVisibilityMaterialKind.ProposalSummary);

    private static RoleVisibilityMatrixEntry Entry(
        GovernanceRoleKind roleKind,
        RoleVisibilitySurface surface,
        RoleVisibilityMaterialKind materialKind) =>
        Matrix().Entries.Single(item =>
            item.RoleKind == roleKind &&
            item.Surface == surface &&
            item.MaterialKind == materialKind);

    private static void AssertRequiredWarnings(ReviewerEvidenceVisibilityDecision decision)
    {
        foreach (var warning in ReviewerEvidenceVisibilityService.RequiredWarnings)
        {
            CollectionAssert.Contains(decision.Warnings.ToList(), warning);
        }
    }

    private static void AssertRequiredForbiddenImplications(ReviewerEvidenceVisibilityDecision decision)
    {
        foreach (var warning in ReviewerEvidenceVisibilityService.RequiredForbiddenAuthorityImplications)
        {
            CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), warning);
        }
    }

    private static string F05CoreSourceWithoutStrings()
    {
        var root = RepoRoot();
        var files = Directory.GetFiles(Path.Combine(root, "IronDev.Core", "Governance"), "ReviewerEvidenceVisibility*.cs");
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
