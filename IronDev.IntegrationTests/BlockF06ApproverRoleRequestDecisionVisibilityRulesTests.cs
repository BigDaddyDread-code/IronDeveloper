using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockF06ApproverRoleRequestDecisionVisibilityRulesTests
{
    [TestMethod]
    public void ApproverRequestMetadataDoesNotGrantApproverAuthority()
    {
        var decision = Classify(Request(ApproverRoleRequestDecisionMaterialKind.ApproverRoleRequestMetadata));

        Assert.AreEqual(ApproverRoleRequestDecisionVisibilityClassification.MetadataOnlyCandidate, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.MetadataOnly, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ApproverRequestSummaryDoesNotCreateApproverRequest()
    {
        var decision = Classify(Request(ApproverRoleRequestDecisionMaterialKind.ApproverRoleRequestSummary));

        Assert.AreEqual(ApproverRoleRequestDecisionVisibilityClassification.SummaryCandidate, decision.Classification);
        Assert.IsFalse(decision.CreatesApproverRequest);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ApproverRequestRationaleRequiresRedactionEvidence()
    {
        var decision = Classify(Request(ApproverRoleRequestDecisionMaterialKind.RedactedApproverRoleRequestRationaleSummary) with
        {
            OptionalRedactionEvidenceRef = null
        });

        Assert.AreEqual(ApproverRoleRequestDecisionVisibilityClassification.BlockedByMissingRedactionEvidence, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ApproverRequestRationaleCanOnlyBeRedactedSummaryCandidateWithRedactionEvidence()
    {
        var decision = Classify(Request(ApproverRoleRequestDecisionMaterialKind.RedactedApproverRoleRequestRationaleSummary));

        Assert.AreEqual(ApproverRoleRequestDecisionVisibilityClassification.RedactedSummaryCandidate, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.RedactedDetails, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ApproverDecisionMetadataDoesNotGrantRoleAssignmentAuthority()
    {
        var decision = Classify(Request(ApproverRoleRequestDecisionMaterialKind.ApproverRoleDecisionMetadata));

        Assert.AreEqual(ApproverRoleRequestDecisionVisibilityClassification.MetadataOnlyCandidate, decision.Classification);
        Assert.IsFalse(decision.GrantsRoleAssignmentAuthority);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ApproverDecisionSummaryDoesNotAcceptApproval()
    {
        var decision = Classify(Request(ApproverRoleRequestDecisionMaterialKind.ApproverRoleDecisionSummary));

        Assert.AreEqual(ApproverRoleRequestDecisionVisibilityClassification.RedactedSummaryCandidate, decision.Classification);
        Assert.IsFalse(decision.AcceptsApproval);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ApproverDecisionOutcomeSummaryDoesNotSatisfyPolicy()
    {
        var decision = Classify(Request(ApproverRoleRequestDecisionMaterialKind.ApproverRoleDecisionOutcomeSummary));

        Assert.AreEqual(ApproverRoleRequestDecisionVisibilityClassification.RedactedSummaryCandidate, decision.Classification);
        Assert.IsFalse(decision.SatisfiesPolicy);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ApprovalPackageReferenceSummaryDoesNotAcceptApproval()
    {
        var decision = Classify(Request(ApproverRoleRequestDecisionMaterialKind.ApprovalPackageReferenceSummary));

        Assert.AreEqual(ApproverRoleRequestDecisionVisibilityClassification.MetadataOnlyCandidate, decision.Classification);
        Assert.IsFalse(decision.AcceptsApproval);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ApprovalPackageReferenceSummaryDoesNotSatisfyPolicy()
    {
        var decision = Classify(Request(ApproverRoleRequestDecisionMaterialKind.ApprovalPackageReferenceSummary));

        Assert.AreEqual(ApproverRoleRequestDecisionVisibilityClassification.MetadataOnlyCandidate, decision.Classification);
        Assert.IsFalse(decision.SatisfiesPolicy);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(ApproverRoleRequestDecisionMaterialKind.RawPayload)]
    [DataRow(ApproverRoleRequestDecisionMaterialKind.CredentialMaterial)]
    [DataRow(ApproverRoleRequestDecisionMaterialKind.PrivateReasoning)]
    public void RawCredentialAndPrivateReasoningMaterialAreAlwaysHidden(
        ApproverRoleRequestDecisionMaterialKind materialKind)
    {
        var decision = Classify(Request(materialKind));

        Assert.AreEqual(ApproverRoleRequestDecisionVisibilityClassification.Hidden, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void AuthorityMarkerMaterialIsBlocked()
    {
        var decision = Classify(Request(ApproverRoleRequestDecisionMaterialKind.AuthorityMarker));

        Assert.AreEqual(ApproverRoleRequestDecisionVisibilityClassification.BlockedByAuthorityMarker, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(ApproverRoleRequestDecisionRequestedIntent.CreateApproverRequest, ApproverRoleRequestDecisionVisibilityClassification.BlockedByApprovalIntent)]
    [DataRow(ApproverRoleRequestDecisionRequestedIntent.GrantApproverRole, ApproverRoleRequestDecisionVisibilityClassification.BlockedByApprovalIntent)]
    [DataRow(ApproverRoleRequestDecisionRequestedIntent.AssignApproverRole, ApproverRoleRequestDecisionVisibilityClassification.BlockedByApprovalIntent)]
    [DataRow(ApproverRoleRequestDecisionRequestedIntent.ApprovalAuthority, ApproverRoleRequestDecisionVisibilityClassification.BlockedByApprovalIntent)]
    [DataRow(ApproverRoleRequestDecisionRequestedIntent.ApprovalAcceptance, ApproverRoleRequestDecisionVisibilityClassification.BlockedByApprovalIntent)]
    [DataRow(ApproverRoleRequestDecisionRequestedIntent.PolicySatisfaction, ApproverRoleRequestDecisionVisibilityClassification.BlockedByActionIntent)]
    [DataRow(ApproverRoleRequestDecisionRequestedIntent.MutationAuthority, ApproverRoleRequestDecisionVisibilityClassification.BlockedByActionIntent)]
    [DataRow(ApproverRoleRequestDecisionRequestedIntent.WorkflowContinuation, ApproverRoleRequestDecisionVisibilityClassification.BlockedByActionIntent)]
    [DataRow(ApproverRoleRequestDecisionRequestedIntent.MergeAuthority, ApproverRoleRequestDecisionVisibilityClassification.BlockedByActionIntent)]
    [DataRow(ApproverRoleRequestDecisionRequestedIntent.ReleaseAuthority, ApproverRoleRequestDecisionVisibilityClassification.BlockedByActionIntent)]
    [DataRow(ApproverRoleRequestDecisionRequestedIntent.DeploymentAuthority, ApproverRoleRequestDecisionVisibilityClassification.BlockedByActionIntent)]
    [DataRow(ApproverRoleRequestDecisionRequestedIntent.VisibilityGrant, ApproverRoleRequestDecisionVisibilityClassification.BlockedByActionIntent)]
    [DataRow(ApproverRoleRequestDecisionRequestedIntent.RedactionBypass, ApproverRoleRequestDecisionVisibilityClassification.BlockedByActionIntent)]
    [DataRow(ApproverRoleRequestDecisionRequestedIntent.PrivateReasoningDisclosure, ApproverRoleRequestDecisionVisibilityClassification.BlockedByActionIntent)]
    public void AuthorityIntentsAreBlockedEvenWithApproverEvidence(
        ApproverRoleRequestDecisionRequestedIntent intent,
        ApproverRoleRequestDecisionVisibilityClassification expectedClassification)
    {
        var decision = Classify(Request(ApproverRoleRequestDecisionMaterialKind.ApproverRoleRequestMetadata) with
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
        var decision = Classify(Request(ApproverRoleRequestDecisionMaterialKind.Unknown));

        Assert.AreEqual(ApproverRoleRequestDecisionVisibilityClassification.BlockedByUnknownMaterial, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnknownIntentFailsClosed()
    {
        var decision = Classify(Request(ApproverRoleRequestDecisionMaterialKind.ApproverRoleRequestMetadata) with
        {
            RequestedIntent = ApproverRoleRequestDecisionRequestedIntent.Unknown
        });

        Assert.AreEqual(ApproverRoleRequestDecisionVisibilityClassification.BlockedByUnknownIntent, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void NonApproverRoleFailsClosed()
    {
        var reviewer = Catalog().Entries.Single(entry => entry.RoleKind == GovernanceRoleKind.Reviewer);
        var decision = Classify(Request(ApproverRoleRequestDecisionMaterialKind.ApproverRoleRequestMetadata) with
        {
            RequestedRoleKey = reviewer.RoleId
        });

        Assert.AreEqual(ApproverRoleRequestDecisionVisibilityClassification.BlockedByNonApproverRole, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void MissingCatalogEvidenceFailsClosed()
    {
        var decision = Classify(Request(ApproverRoleRequestDecisionMaterialKind.ApproverRoleRequestMetadata) with
        {
            RoleCatalogEvidenceRef = string.Empty
        });

        Assert.AreEqual(ApproverRoleRequestDecisionVisibilityClassification.BlockedByMissingCatalogEvidence, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void MissingMatrixEvidenceFailsClosed()
    {
        var decision = Classify(Request(ApproverRoleRequestDecisionMaterialKind.ApproverRoleRequestMetadata) with
        {
            VisibilityMatrixEvidenceRef = string.Empty
        });

        Assert.AreEqual(ApproverRoleRequestDecisionVisibilityClassification.BlockedByMissingMatrixEvidence, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void MissingApproverRequestDecisionEvidenceFailsClosed()
    {
        var decision = Classify(Request(ApproverRoleRequestDecisionMaterialKind.ApproverRoleRequestMetadata) with
        {
            ApproverRequestDecisionEvidenceRef = string.Empty
        });

        Assert.AreEqual(ApproverRoleRequestDecisionVisibilityClassification.BlockedByMissingRequestDecisionEvidence, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ApproverEvidenceNotAllowedByMatrixIsHidden()
    {
        var decision = Classify(Request(ApproverRoleRequestDecisionMaterialKind.ApproverRoleRequestMetadata) with
        {
            RequestedSurface = RoleVisibilitySurface.DeploymentReadiness
        });

        Assert.AreEqual(ApproverRoleRequestDecisionVisibilityClassification.Hidden, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnsafeAuthorityTextIsHiddenAndDoesNotEchoRawMarker()
    {
        var unsafeMarker = "ApproverGranted = true";
        var decision = Classify(Request(ApproverRoleRequestDecisionMaterialKind.ApproverRoleRequestMetadata) with
        {
            ApproverRequestDecisionEvidenceRef = unsafeMarker
        });

        Assert.AreEqual(ApproverRoleRequestDecisionVisibilityClassification.Hidden, decision.Classification);
        CollectionAssert.Contains(decision.EvidenceRefs.ToList(), "[unsafe-rejected]");
        Assert.IsFalse(decision.RecordFingerprint.Contains(unsafeMarker, StringComparison.OrdinalIgnoreCase));
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow("approver-request-metadata:f06")]
    [DataRow("approver-decision-summary:f06")]
    [DataRow("approval-package-reference:f06")]
    [DataRow("redacted-approver-rationale:f06")]
    public void SafeApproverEvidenceRefsAreNotRejected(string value)
    {
        Assert.IsFalse(ApproverRoleRequestDecisionVisibilityValidator.ContainsUnsafeEvidenceText(value));
    }

    [TestMethod]
    public void NullRequestValidationFailsClosed()
    {
        var result = ApproverRoleRequestDecisionVisibilityValidator.ValidateRequest(null);

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToList(), "ApproverRoleRequestDecisionVisibilityRequestRequired");
    }

    [TestMethod]
    public void EveryDecisionHasAllAuthorityFlagsFalse()
    {
        var requests = new[]
        {
            Request(ApproverRoleRequestDecisionMaterialKind.ApproverRoleRequestMetadata),
            Request(ApproverRoleRequestDecisionMaterialKind.RedactedApproverRoleRequestRationaleSummary) with { OptionalRedactionEvidenceRef = null },
            Request(ApproverRoleRequestDecisionMaterialKind.RawPayload),
            Request(ApproverRoleRequestDecisionMaterialKind.AuthorityMarker),
            Request(ApproverRoleRequestDecisionMaterialKind.ApproverRoleRequestMetadata) with { RequestedIntent = ApproverRoleRequestDecisionRequestedIntent.ApprovalAuthority },
            Request(ApproverRoleRequestDecisionMaterialKind.Unknown),
            Request(ApproverRoleRequestDecisionMaterialKind.ApproverRoleRequestMetadata) with { ApproverRequestDecisionEvidenceRef = string.Empty }
        };

        foreach (var request in requests)
        {
            AssertAuthorityFlagsFalse(Classify(request));
        }
    }

    [TestMethod]
    public void DecisionModelCarriesRequiredFalseAuthorityFields()
    {
        var decision = Classify(Request(ApproverRoleRequestDecisionMaterialKind.ApproverRoleRequestMetadata));

        Assert.IsFalse(decision.GrantsApproverAuthority);
        Assert.IsFalse(decision.GrantsRoleAssignmentAuthority);
        Assert.IsFalse(decision.CreatesApproverRequest);
        Assert.IsFalse(decision.AcceptsApproverRequest);
        Assert.IsFalse(decision.GrantsVisibilityAuthority);
        Assert.IsFalse(decision.GrantsAccess);
        Assert.IsFalse(decision.GrantsApprovalAuthority);
        Assert.IsFalse(decision.AcceptsApproval);
        Assert.IsFalse(decision.SatisfiesPolicy);
        Assert.IsFalse(decision.GrantsMutationAuthority);
        Assert.IsFalse(decision.GrantsWorkflowContinuation);
        Assert.IsFalse(decision.GrantsMergeAuthority);
        Assert.IsFalse(decision.GrantsReleaseAuthority);
        Assert.IsFalse(decision.GrantsDeploymentAuthority);
        Assert.IsFalse(decision.BypassesRedaction);
        Assert.IsFalse(decision.DisclosesPrivateReasoning);
    }

    [TestMethod]
    public void StaticScanF06CoreAddsNoIdentityPermissionProviderOrMutationSurface()
    {
        var source = F06CoreSourceWithoutStrings();
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
            "ApprovalRecord",
            "PolicySatisfactionService",
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
            "PullRequestAsync",
            "ReadyForReviewAsync",
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
    public void ReceiptExistsAndStatesApproverRequestDecisionVisibilityIsNotAuthority()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "F06_APPROVER_ROLE_REQUEST_DECISION_VISIBILITY_RULES.md"));

        StringAssert.Contains(receipt, "Approver request/decision visibility is not approval authority.");
        StringAssert.Contains(receipt, "Seeing an approval-shaped decision is not approval.");
        StringAssert.Contains(receipt, "does not grant access");
        StringAssert.Contains(receipt, "does not accept approval");
        StringAssert.Contains(receipt, "does not satisfy policy");
        StringAssert.Contains(receipt, "does not authorize mutation");
        StringAssert.Contains(receipt, "does not authorize merge, release, or deployment");
    }

    private static ApproverRoleRequestDecisionVisibilityDecision Classify(
        ApproverRoleRequestDecisionVisibilityRequest request) =>
        new ApproverRoleRequestDecisionVisibilityService().Classify(Catalog(), Matrix(), request);

    private static ApproverRoleRequestDecisionVisibilityRequest Request(
        ApproverRoleRequestDecisionMaterialKind materialKind)
    {
        var approver = Catalog().Entries.Single(entry => entry.RoleKind == GovernanceRoleKind.ApproverCandidate);
        return new ApproverRoleRequestDecisionVisibilityRequest
        {
            CorrelationId = "correlation-f06",
            RequestedRoleKey = approver.RoleId,
            RequestedSurface = SurfaceFor(materialKind),
            RequestedMaterialKind = materialKind,
            RequestedIntent = ApproverRoleRequestDecisionRequestedIntent.ReadOnlyInspect,
            ApproverRequestDecisionEvidenceRef = "approver-request-decision-evidence:f06",
            RoleCatalogEvidenceRef = "role-catalog:f06",
            VisibilityMatrixEvidenceRef = "visibility-matrix:f06",
            OptionalPolicyEvidenceRef = "policy-evidence:f06",
            OptionalRedactionEvidenceRef = "redaction-evidence:f06"
        };
    }

    private static RoleVisibilitySurface SurfaceFor(ApproverRoleRequestDecisionMaterialKind materialKind) =>
        materialKind switch
        {
            ApproverRoleRequestDecisionMaterialKind.ApproverRoleDecisionOutcomeSummary => RoleVisibilitySurface.ValidationReview,
            _ => RoleVisibilitySurface.ApprovalPackage
        };

    private static GovernanceRoleCatalog Catalog() => new RoleCatalogService().BuildDefaultCatalog();

    private static RoleVisibilityMatrix Matrix() =>
        new RoleVisibilityMatrixService().BuildDefaultMatrix(Catalog());

    private static void AssertAuthorityFlagsFalse(ApproverRoleRequestDecisionVisibilityDecision decision)
    {
        Assert.IsFalse(decision.GrantsApproverAuthority);
        Assert.IsFalse(decision.GrantsRoleAssignmentAuthority);
        Assert.IsFalse(decision.CreatesApproverRequest);
        Assert.IsFalse(decision.AcceptsApproverRequest);
        Assert.IsFalse(decision.GrantsVisibilityAuthority);
        Assert.IsFalse(decision.GrantsAccess);
        Assert.IsFalse(decision.GrantsApprovalAuthority);
        Assert.IsFalse(decision.AcceptsApproval);
        Assert.IsFalse(decision.SatisfiesPolicy);
        Assert.IsFalse(decision.GrantsMutationAuthority);
        Assert.IsFalse(decision.GrantsWorkflowContinuation);
        Assert.IsFalse(decision.GrantsMergeAuthority);
        Assert.IsFalse(decision.GrantsReleaseAuthority);
        Assert.IsFalse(decision.GrantsDeploymentAuthority);
        Assert.IsFalse(decision.BypassesRedaction);
        Assert.IsFalse(decision.DisclosesPrivateReasoning);
    }

    private static string F06CoreSourceWithoutStrings()
    {
        var root = RepoRoot();
        var files = Directory.GetFiles(Path.Combine(root, "IronDev.Core", "Governance"), "ApproverRoleRequestDecisionVisibility*.cs");
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
