using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockF05ReviewerRoleEvidenceVisibilityRulesTests
{
    [TestMethod]
    public void ReviewerEvidenceMetadataDoesNotGrantReviewerAuthority()
    {
        var decision = Classify(Request(ReviewerRoleEvidenceMaterialKind.ReviewerClaimMetadata));

        Assert.AreEqual(ReviewerRoleEvidenceVisibilityClassification.MetadataOnlyCandidate, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.MetadataOnly, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ReviewerAssignmentClaimSummaryDoesNotGrantRoleAssignmentAuthority()
    {
        var decision = Classify(Request(ReviewerRoleEvidenceMaterialKind.ReviewerAssignmentClaimSummary));

        Assert.AreEqual(ReviewerRoleEvidenceVisibilityClassification.MetadataOnlyCandidate, decision.Classification);
        Assert.IsFalse(decision.GrantsRoleAssignmentAuthority);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ReviewRequestSummaryDoesNotSatisfyApproval()
    {
        var decision = Classify(Request(ReviewerRoleEvidenceMaterialKind.ReviewRequestSummary));

        Assert.AreEqual(ReviewerRoleEvidenceVisibilityClassification.SummaryCandidate, decision.Classification);
        Assert.IsFalse(decision.GrantsApprovalAuthority);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ReviewParticipationSummaryDoesNotSatisfyPolicy()
    {
        var decision = Classify(Request(ReviewerRoleEvidenceMaterialKind.ReviewParticipationSummary));

        Assert.AreEqual(ReviewerRoleEvidenceVisibilityClassification.SummaryCandidate, decision.Classification);
        Assert.IsFalse(decision.SatisfiesPolicy);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ReviewCommentSummaryDoesNotAuthorizeMerge()
    {
        var decision = Classify(Request(ReviewerRoleEvidenceMaterialKind.ReviewCommentSummary));

        Assert.AreEqual(ReviewerRoleEvidenceVisibilityClassification.RedactedSummaryCandidate, decision.Classification);
        CollectionAssert.Contains(decision.Reasons.ToList(), "Review comment summary is redacted summary candidate visibility.");
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ReviewOutcomeSummaryDoesNotAuthorizeWorkflowContinuation()
    {
        var decision = Classify(Request(ReviewerRoleEvidenceMaterialKind.ReviewOutcomeSummary));

        Assert.AreEqual(ReviewerRoleEvidenceVisibilityClassification.RedactedSummaryCandidate, decision.Classification);
        Assert.IsFalse(decision.GrantsWorkflowContinuation);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void RedactedRationaleSummaryRequiresRedactionEvidence()
    {
        var decision = Classify(Request(ReviewerRoleEvidenceMaterialKind.RedactedReviewRationaleSummary) with
        {
            OptionalRedactionEvidenceRef = null
        });

        Assert.AreEqual(ReviewerRoleEvidenceVisibilityClassification.BlockedBySensitiveMaterial, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void RedactedRationaleSummaryCanOnlyBeRedactedSummaryCandidateWithRedactionEvidence()
    {
        var decision = Classify(Request(ReviewerRoleEvidenceMaterialKind.RedactedReviewRationaleSummary));

        Assert.AreEqual(ReviewerRoleEvidenceVisibilityClassification.RedactedSummaryCandidate, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.RedactedDetails, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(ReviewerRoleEvidenceMaterialKind.RawPayload)]
    [DataRow(ReviewerRoleEvidenceMaterialKind.CredentialMaterial)]
    [DataRow(ReviewerRoleEvidenceMaterialKind.PrivateReasoning)]
    public void RawCredentialAndPrivateReasoningMaterialAreAlwaysHidden(ReviewerRoleEvidenceMaterialKind materialKind)
    {
        var decision = Classify(Request(materialKind));

        Assert.AreEqual(ReviewerRoleEvidenceVisibilityClassification.Hidden, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void AuthorityMarkerMaterialIsBlocked()
    {
        var decision = Classify(Request(ReviewerRoleEvidenceMaterialKind.AuthorityMarker));

        Assert.AreEqual(ReviewerRoleEvidenceVisibilityClassification.BlockedByAuthorityMarker, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(ReviewerRoleEvidenceRequestedIntent.ActionAuthority)]
    [DataRow(ReviewerRoleEvidenceRequestedIntent.ApprovalAuthority)]
    [DataRow(ReviewerRoleEvidenceRequestedIntent.PolicySatisfaction)]
    [DataRow(ReviewerRoleEvidenceRequestedIntent.MutationAuthority)]
    [DataRow(ReviewerRoleEvidenceRequestedIntent.WorkflowContinuation)]
    [DataRow(ReviewerRoleEvidenceRequestedIntent.VisibilityGrant)]
    [DataRow(ReviewerRoleEvidenceRequestedIntent.RedactionBypass)]
    [DataRow(ReviewerRoleEvidenceRequestedIntent.PrivateReasoningDisclosure)]
    public void AuthorityIntentsAreBlockedEvenWithReviewerEvidence(ReviewerRoleEvidenceRequestedIntent intent)
    {
        var decision = Classify(Request(ReviewerRoleEvidenceMaterialKind.ReviewerClaimMetadata) with
        {
            RequestedIntent = intent
        });

        Assert.AreEqual(ReviewerRoleEvidenceVisibilityClassification.BlockedByActionIntent, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnknownMaterialFailsClosed()
    {
        var decision = Classify(Request(ReviewerRoleEvidenceMaterialKind.Unknown));

        Assert.AreEqual(ReviewerRoleEvidenceVisibilityClassification.BlockedByUnknownMaterial, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnknownIntentFailsClosed()
    {
        var decision = Classify(Request(ReviewerRoleEvidenceMaterialKind.ReviewerClaimMetadata) with
        {
            RequestedIntent = ReviewerRoleEvidenceRequestedIntent.Unknown
        });

        Assert.AreEqual(ReviewerRoleEvidenceVisibilityClassification.BlockedByUnknownIntent, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void NonReviewerRoleFailsClosed()
    {
        var observer = Catalog().Entries.Single(entry => entry.RoleKind == GovernanceRoleKind.Observer);
        var decision = Classify(Request(ReviewerRoleEvidenceMaterialKind.ReviewerClaimMetadata) with
        {
            RequestedRoleKey = observer.RoleId
        });

        Assert.AreEqual(ReviewerRoleEvidenceVisibilityClassification.BlockedByNonReviewerRole, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void MissingCatalogEvidenceFailsClosed()
    {
        var decision = Classify(Request(ReviewerRoleEvidenceMaterialKind.ReviewerClaimMetadata) with
        {
            RoleCatalogEvidenceRef = string.Empty
        });

        Assert.AreEqual(ReviewerRoleEvidenceVisibilityClassification.BlockedByMissingCatalogEvidence, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void MissingMatrixEvidenceFailsClosed()
    {
        var decision = Classify(Request(ReviewerRoleEvidenceMaterialKind.ReviewerClaimMetadata) with
        {
            VisibilityMatrixEvidenceRef = string.Empty
        });

        Assert.AreEqual(ReviewerRoleEvidenceVisibilityClassification.BlockedByMissingMatrixEvidence, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void MissingReviewerEvidenceFailsClosed()
    {
        var decision = Classify(Request(ReviewerRoleEvidenceMaterialKind.ReviewerClaimMetadata) with
        {
            ReviewerEvidenceRef = string.Empty
        });

        Assert.AreEqual(ReviewerRoleEvidenceVisibilityClassification.BlockedByMissingReviewerEvidence, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ReviewerEvidenceNotAllowedByMatrixIsHidden()
    {
        var decision = Classify(Request(ReviewerRoleEvidenceMaterialKind.ReviewOutcomeSummary) with
        {
            RequestedSurface = RoleVisibilitySurface.DeploymentReadiness
        });

        Assert.AreEqual(ReviewerRoleEvidenceVisibilityClassification.Hidden, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnsafeAuthorityTextIsHiddenAndDoesNotEchoRawMarker()
    {
        var unsafeMarker = "ReviewerGranted = true";
        var decision = Classify(Request(ReviewerRoleEvidenceMaterialKind.ReviewerClaimMetadata) with
        {
            ReviewerEvidenceRef = unsafeMarker
        });

        Assert.AreEqual(ReviewerRoleEvidenceVisibilityClassification.Hidden, decision.Classification);
        CollectionAssert.Contains(decision.EvidenceRefs.ToList(), "[unsafe-rejected]");
        Assert.IsFalse(decision.RecordFingerprint.Contains(unsafeMarker, StringComparison.OrdinalIgnoreCase));
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow("reviewer-evidence:f05")]
    [DataRow("review-request-summary:f05")]
    [DataRow("review-comment-summary:f05")]
    [DataRow("redacted-review-rationale:f05")]
    public void SafeReviewerEvidenceRefsAreNotRejected(string value)
    {
        Assert.IsFalse(ReviewerRoleEvidenceVisibilityValidator.ContainsUnsafeEvidenceText(value));
    }

    [TestMethod]
    public void NullRequestValidationFailsClosed()
    {
        var result = ReviewerRoleEvidenceVisibilityValidator.ValidateRequest(null);

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToList(), "ReviewerRoleEvidenceVisibilityRequestRequired");
    }

    [TestMethod]
    public void EveryDecisionHasAuthorityFlagsFalse()
    {
        var requests = new[]
        {
            Request(ReviewerRoleEvidenceMaterialKind.ReviewerClaimMetadata),
            Request(ReviewerRoleEvidenceMaterialKind.RedactedReviewRationaleSummary) with { OptionalRedactionEvidenceRef = null },
            Request(ReviewerRoleEvidenceMaterialKind.RawPayload),
            Request(ReviewerRoleEvidenceMaterialKind.AuthorityMarker),
            Request(ReviewerRoleEvidenceMaterialKind.ReviewerClaimMetadata) with { RequestedIntent = ReviewerRoleEvidenceRequestedIntent.ApprovalAuthority },
            Request(ReviewerRoleEvidenceMaterialKind.Unknown),
            Request(ReviewerRoleEvidenceMaterialKind.ReviewerClaimMetadata) with { ReviewerEvidenceRef = string.Empty }
        };

        foreach (var request in requests)
        {
            AssertAuthorityFlagsFalse(Classify(request));
        }
    }

    [TestMethod]
    public void DecisionModelCarriesRequiredFalseAuthorityFields()
    {
        var decision = Classify(Request(ReviewerRoleEvidenceMaterialKind.ReviewerClaimMetadata));

        Assert.IsFalse(decision.GrantsReviewerAuthority);
        Assert.IsFalse(decision.GrantsRoleAssignmentAuthority);
        Assert.IsFalse(decision.GrantsVisibilityAuthority);
        Assert.IsFalse(decision.GrantsAccess);
        Assert.IsFalse(decision.GrantsApprovalAuthority);
        Assert.IsFalse(decision.SatisfiesPolicy);
        Assert.IsFalse(decision.GrantsMutationAuthority);
        Assert.IsFalse(decision.GrantsWorkflowContinuation);
        Assert.IsFalse(decision.BypassesRedaction);
        Assert.IsFalse(decision.DisclosesPrivateReasoning);
    }

    [TestMethod]
    public void StaticScanF05CoreAddsNoIdentityPermissionProviderOrMutationSurface()
    {
        var source = F05CoreSourceWithoutStrings();
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
            "GroupMembership",
            "Collaborator",
            "ApprovalRecord",
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
    public void ReceiptExistsAndStatesReviewerRoleEvidenceVisibilityIsNotAuthority()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "F05_REVIEWER_ROLE_EVIDENCE_VISIBILITY_RULES.md"));

        StringAssert.Contains(receipt, "Reviewer role evidence visibility is not reviewer authority.");
        StringAssert.Contains(receipt, "A visible reviewer signal is not a reviewer.");
        StringAssert.Contains(receipt, "does not grant access");
        StringAssert.Contains(receipt, "does not approve work");
        StringAssert.Contains(receipt, "does not satisfy policy");
        StringAssert.Contains(receipt, "does not authorize mutation");
        StringAssert.Contains(receipt, "does not bypass redaction");
    }

    private static ReviewerRoleEvidenceVisibilityDecision Classify(
        ReviewerRoleEvidenceVisibilityRequest request) =>
        new ReviewerRoleEvidenceVisibilityService().Classify(Catalog(), Matrix(), request);

    private static ReviewerRoleEvidenceVisibilityRequest Request(
        ReviewerRoleEvidenceMaterialKind materialKind)
    {
        var reviewer = Catalog().Entries.Single(entry => entry.RoleKind == GovernanceRoleKind.Reviewer);
        return new ReviewerRoleEvidenceVisibilityRequest
        {
            CorrelationId = "correlation-f05",
            RequestedRoleKey = reviewer.RoleId,
            RequestedSurface = SurfaceFor(materialKind),
            RequestedMaterialKind = materialKind,
            RequestedIntent = ReviewerRoleEvidenceRequestedIntent.ReadOnlyInspect,
            ReviewerEvidenceRef = "reviewer-evidence:f05",
            RoleCatalogEvidenceRef = "role-catalog:f05",
            VisibilityMatrixEvidenceRef = "visibility-matrix:f05",
            OptionalPolicyEvidenceRef = "policy-evidence:f05",
            OptionalRedactionEvidenceRef = "redaction-evidence:f05"
        };
    }

    private static RoleVisibilitySurface SurfaceFor(ReviewerRoleEvidenceMaterialKind materialKind) =>
        materialKind switch
        {
            ReviewerRoleEvidenceMaterialKind.ReviewOutcomeSummary => RoleVisibilitySurface.ValidationReview,
            _ => RoleVisibilitySurface.PullRequest
        };

    private static GovernanceRoleCatalog Catalog() => new RoleCatalogService().BuildDefaultCatalog();

    private static RoleVisibilityMatrix Matrix() =>
        new RoleVisibilityMatrixService().BuildDefaultMatrix(Catalog());

    private static void AssertAuthorityFlagsFalse(ReviewerRoleEvidenceVisibilityDecision decision)
    {
        Assert.IsFalse(decision.GrantsReviewerAuthority);
        Assert.IsFalse(decision.GrantsRoleAssignmentAuthority);
        Assert.IsFalse(decision.GrantsVisibilityAuthority);
        Assert.IsFalse(decision.GrantsAccess);
        Assert.IsFalse(decision.GrantsApprovalAuthority);
        Assert.IsFalse(decision.SatisfiesPolicy);
        Assert.IsFalse(decision.GrantsMutationAuthority);
        Assert.IsFalse(decision.GrantsWorkflowContinuation);
        Assert.IsFalse(decision.BypassesRedaction);
        Assert.IsFalse(decision.DisclosesPrivateReasoning);
    }

    private static string F05CoreSourceWithoutStrings()
    {
        var root = RepoRoot();
        var files = Directory.GetFiles(Path.Combine(root, "IronDev.Core", "Governance"), "ReviewerRoleEvidenceVisibility*.cs");
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
