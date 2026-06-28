using System.Text.RegularExpressions;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockF14MissingEvidenceVisibilityByRoleTests
{
    [TestMethod]
    public void MissingEvidenceVisibilityRequestValidatesSafeInput()
    {
        var validation = MissingEvidenceVisibilityValidator.ValidateRequest(Request(GovernanceRoleKind.Reviewer));

        Assert.IsTrue(validation.IsValid, string.Join("; ", validation.Issues));
        Assert.AreEqual(0, validation.UnsafeRefs.Count);
    }

    [TestMethod]
    public void MissingEvidenceVisibilityRejectsUnsafeText()
    {
        var hostile = string.Concat("missing evidence visibility ", "satisfies evidence");
        var validation = MissingEvidenceVisibilityValidator.ValidateRequest(Request(GovernanceRoleKind.Reviewer) with
        {
            SourceMissingEvidenceRef = hostile
        });

        Assert.IsFalse(validation.IsValid);
        CollectionAssert.Contains(validation.Issues.ToList(), "SourceMissingEvidenceRefUnsafe");
        CollectionAssert.Contains(validation.UnsafeRefs.ToList(), hostile);
    }

    [TestMethod]
    public void UnsafeRequestDecisionDoesNotEchoHostileEvidence()
    {
        var hostile = string.Concat("missing evidence visibility ", "grants mutation");
        var decision = Classify(Request(GovernanceRoleKind.Reviewer) with
        {
            SourceMissingEvidenceRef = hostile
        });

        Assert.AreEqual(MissingEvidenceVisibilityClassification.Invalid, decision.Classification);
        CollectionAssert.DoesNotContain(decision.EvidenceRefs.ToList(), hostile);
        CollectionAssert.Contains(decision.EvidenceRefs.ToList(), "[unsafe-rejected]");
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnknownRoleFailsClosed()
    {
        var decision = Classify(Request(GovernanceRoleKind.Reviewer) with
        {
            RequestedRoleId = "role:f14:not-present"
        });

        Assert.AreEqual(MissingEvidenceVisibilityClassification.BlockedByUnknownRole, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnknownMissingEvidenceKindFailsClosed()
    {
        var decision = Classify(Request(GovernanceRoleKind.Reviewer) with
        {
            RequestedMissingEvidenceKind = MissingEvidenceKind.Unknown
        });

        Assert.AreEqual(MissingEvidenceVisibilityClassification.BlockedByUnknownMissingEvidenceKind, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnknownMaterialFailsClosed()
    {
        var decision = Classify(Request(GovernanceRoleKind.Reviewer) with
        {
            RequestedMaterialKind = MissingEvidenceMaterialKind.Unknown
        });

        Assert.AreEqual(MissingEvidenceVisibilityClassification.BlockedByUnknownMaterial, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnknownIntentFailsClosed()
    {
        var decision = Classify(Request(GovernanceRoleKind.Reviewer) with
        {
            RequestedIntent = MissingEvidenceVisibilityIntent.Unknown
        });

        Assert.AreEqual(MissingEvidenceVisibilityClassification.BlockedByUnknownIntent, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow("RoleCatalogEvidenceRef", MissingEvidenceVisibilityClassification.BlockedByMissingRoleCatalogEvidence)]
    [DataRow("VisibilityMatrixEvidenceRef", MissingEvidenceVisibilityClassification.BlockedByMissingVisibilityMatrixEvidence)]
    [DataRow("ForbiddenActionCatalogEvidenceRef", MissingEvidenceVisibilityClassification.BlockedByMissingForbiddenActionCatalogEvidence)]
    [DataRow("SourceMissingEvidenceRef", MissingEvidenceVisibilityClassification.BlockedByMissingSourceMissingEvidenceRef)]
    public void MissingRequiredEvidenceFailsClosed(
        string missingRefName,
        MissingEvidenceVisibilityClassification expected)
    {
        var request = missingRefName switch
        {
            "RoleCatalogEvidenceRef" => Request(GovernanceRoleKind.Reviewer) with { RoleCatalogEvidenceRef = string.Empty },
            "VisibilityMatrixEvidenceRef" => Request(GovernanceRoleKind.Reviewer) with { VisibilityMatrixEvidenceRef = string.Empty },
            "ForbiddenActionCatalogEvidenceRef" => Request(GovernanceRoleKind.Reviewer) with { ForbiddenActionCatalogEvidenceRef = string.Empty },
            "SourceMissingEvidenceRef" => Request(GovernanceRoleKind.Reviewer) with { SourceMissingEvidenceRef = string.Empty },
            _ => Request(GovernanceRoleKind.Reviewer)
        };

        var decision = Classify(request);

        Assert.AreEqual(expected, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(MissingEvidenceMaterialKind.RawPayload)]
    [DataRow(MissingEvidenceMaterialKind.RawProviderResponse)]
    [DataRow(MissingEvidenceMaterialKind.RawSource)]
    [DataRow(MissingEvidenceMaterialKind.RawDiff)]
    [DataRow(MissingEvidenceMaterialKind.RawPatch)]
    [DataRow(MissingEvidenceMaterialKind.RawLog)]
    public void RawMaterialsAreBlocked(MissingEvidenceMaterialKind materialKind)
    {
        var decision = Classify(Request(GovernanceRoleKind.Reviewer) with
        {
            RequestedMaterialKind = materialKind
        });

        Assert.AreEqual(MissingEvidenceVisibilityClassification.BlockedByRawMaterial, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(MissingEvidenceMaterialKind.CredentialMaterial, MissingEvidenceVisibilityClassification.BlockedByCredentialMaterial)]
    [DataRow(MissingEvidenceMaterialKind.SecretMaterial, MissingEvidenceVisibilityClassification.BlockedBySecretMaterial)]
    [DataRow(MissingEvidenceMaterialKind.PrivateReasoning, MissingEvidenceVisibilityClassification.BlockedByPrivateReasoningMaterial)]
    public void CredentialSecretAndPrivateReasoningMaterialsAreBlocked(
        MissingEvidenceMaterialKind materialKind,
        MissingEvidenceVisibilityClassification expected)
    {
        var decision = Classify(Request(GovernanceRoleKind.Reviewer) with
        {
            RequestedMaterialKind = materialKind
        });

        Assert.AreEqual(expected, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(MissingEvidenceVisibilityIntent.SatisfyMissingEvidence, MissingEvidenceVisibilityClassification.BlockedByEvidenceSatisfactionIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.CreateMissingEvidence, MissingEvidenceVisibilityClassification.BlockedByEvidenceSatisfactionIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.OverrideMissingEvidence, MissingEvidenceVisibilityClassification.BlockedByEvidenceSatisfactionIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.WaiveEvidenceRequirement, MissingEvidenceVisibilityClassification.BlockedByEvidenceSatisfactionIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.AcceptApproval, MissingEvidenceVisibilityClassification.BlockedByApprovalIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.SatisfyPolicy, MissingEvidenceVisibilityClassification.BlockedByPolicyIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.RefreshValidation, MissingEvidenceVisibilityClassification.BlockedByValidationIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.ProveSourceSafety, MissingEvidenceVisibilityClassification.BlockedBySourceSafetyIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.RunDiagnostic, MissingEvidenceVisibilityClassification.BlockedByExecutionIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.Retry, MissingEvidenceVisibilityClassification.BlockedByExecutionIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.Rollback, MissingEvidenceVisibilityClassification.BlockedByExecutionIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.Recover, MissingEvidenceVisibilityClassification.BlockedByExecutionIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.MutateSource, MissingEvidenceVisibilityClassification.BlockedByMutationIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.ApplyPatch, MissingEvidenceVisibilityClassification.BlockedByMutationIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.Commit, MissingEvidenceVisibilityClassification.BlockedByMutationIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.Push, MissingEvidenceVisibilityClassification.BlockedByMutationIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.CreatePullRequest, MissingEvidenceVisibilityClassification.BlockedByMutationIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.ReadyForReview, MissingEvidenceVisibilityClassification.BlockedByMutationIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.ContinueWorkflow, MissingEvidenceVisibilityClassification.BlockedByWorkflowIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.Merge, MissingEvidenceVisibilityClassification.BlockedByReleaseDeployIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.Release, MissingEvidenceVisibilityClassification.BlockedByReleaseDeployIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.Deploy, MissingEvidenceVisibilityClassification.BlockedByReleaseDeployIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.BypassRedaction, MissingEvidenceVisibilityClassification.BlockedByRedactionBypassIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.DiscloseSecret, MissingEvidenceVisibilityClassification.BlockedByDisclosureIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.DiscloseCredential, MissingEvidenceVisibilityClassification.BlockedByDisclosureIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.DiscloseRawPayload, MissingEvidenceVisibilityClassification.BlockedByDisclosureIntent)]
    [DataRow(MissingEvidenceVisibilityIntent.DisclosePrivateReasoning, MissingEvidenceVisibilityClassification.BlockedByDisclosureIntent)]
    public void NonReadOnlyIntentsAreBlocked(
        MissingEvidenceVisibilityIntent intent,
        MissingEvidenceVisibilityClassification expected)
    {
        var decision = Classify(Request(GovernanceRoleKind.Reviewer) with
        {
            RequestedIntent = intent
        });

        Assert.AreEqual(expected, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void EveryMissingEvidenceKindMapsDefensivelyToF13ActionWhereApplicable()
    {
        foreach (var kind in Enum.GetValues<MissingEvidenceKind>().Where(static kind => kind != MissingEvidenceKind.Unknown))
        {
            Assert.AreNotEqual(RoleForbiddenActionKind.Unknown, MissingEvidenceVisibilityService.MapToForbiddenAction(kind), kind.ToString());
        }
    }

    [TestMethod]
    public void F13ForbiddenActionLookupBlocksRoleEvidenceDerivedAuthority()
    {
        var decision = Classify(Request(GovernanceRoleKind.ApproverCandidate) with
        {
            RequestedMissingEvidenceKind = MissingEvidenceKind.ApprovalEvidence
        });

        Assert.AreEqual(MissingEvidenceVisibilityClassification.BlockedByForbiddenActionCatalog, decision.Classification);
        Assert.IsFalse(decision.AcceptsApproval);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ExplicitF13ForbiddenActionReturnsBlockedVisibility()
    {
        var decision = Classify(Request(GovernanceRoleKind.TenantAdministrator) with
        {
            RequestedMissingEvidenceKind = MissingEvidenceKind.AccessDecisionEvidence,
            RequestedMaterialKind = MissingEvidenceMaterialKind.CategoryOnly
        });

        Assert.AreEqual(MissingEvidenceVisibilityClassification.BlockedByForbiddenActionCatalog, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnlistedF13ActionDoesNotBecomeAllowed()
    {
        var decision = Classify(Request(GovernanceRoleKind.Requester) with
        {
            RequestedMissingEvidenceKind = MissingEvidenceKind.RouteGuardEvidence,
            RequestedMaterialKind = MissingEvidenceMaterialKind.CategoryOnly
        });

        Assert.AreEqual(MissingEvidenceVisibilityClassification.CategoryOnlyCandidate, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.SummaryOnly, decision.EffectiveCandidateVisibility);
        Assert.IsTrue(decision.RequiresSeparateAuthority);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ExternalViewerReceivesAtMostPresenceOnlyCandidateVisibility()
    {
        var decision = Classify(Request(GovernanceRoleKind.ExternalViewer) with
        {
            RequestedMissingEvidenceKind = MissingEvidenceKind.RouteGuardEvidence,
            RequestedMaterialKind = MissingEvidenceMaterialKind.RedactedSummary
        });

        Assert.AreEqual(MissingEvidenceVisibilityClassification.PresenceOnlyCandidate, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.PresenceOnly, decision.EffectiveCandidateVisibility);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(MissingEvidenceKind.ExternalAccessEvidence)]
    [DataRow(MissingEvidenceKind.ShareLinkAuthority)]
    [DataRow(MissingEvidenceKind.RawExportAuthority)]
    [DataRow(MissingEvidenceKind.RawProviderResponseDisclosureAuthority)]
    [DataRow(MissingEvidenceKind.RawSourceDisclosureAuthority)]
    [DataRow(MissingEvidenceKind.RawLogDisclosureAuthority)]
    public void ExternalViewerCannotSeeExternalRawOrPlatformShapedMissingEvidence(
        MissingEvidenceKind kind)
    {
        var decision = Classify(Request(GovernanceRoleKind.ExternalViewer) with
        {
            RequestedMissingEvidenceKind = kind
        });

        Assert.AreEqual(MissingEvidenceVisibilityClassification.BlockedByForbiddenActionCatalog, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ViewerReadOnlyRolesReceiveAtMostCategoryCandidateVisibility()
    {
        var decision = Classify(Request(GovernanceRoleKind.Observer) with
        {
            RequestedMissingEvidenceKind = MissingEvidenceKind.RouteGuardEvidence,
            RequestedMaterialKind = MissingEvidenceMaterialKind.RedactedSummary
        });

        Assert.AreEqual(MissingEvidenceVisibilityClassification.CategoryOnlyCandidate, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.SummaryOnly, decision.EffectiveCandidateVisibility);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(GovernanceRoleKind.Reviewer)]
    [DataRow(GovernanceRoleKind.Auditor)]
    [DataRow(GovernanceRoleKind.SecurityReviewer)]
    [DataRow(GovernanceRoleKind.ReleaseReviewer)]
    public void ReviewerAndAuditorRolesReceiveOnlyRedactedSummaryCandidates(
        GovernanceRoleKind roleKind)
    {
        var decision = Classify(Request(roleKind) with
        {
            RequestedMissingEvidenceKind = MissingEvidenceKind.RouteGuardEvidence,
            RequestedMaterialKind = MissingEvidenceMaterialKind.RedactedSummary
        });

        Assert.AreEqual(MissingEvidenceVisibilityClassification.RedactedSummaryCandidate, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.RedactedDetails, decision.EffectiveCandidateVisibility);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ApproverCandidateVisibilityDoesNotAcceptApproval()
    {
        var decision = Classify(Request(GovernanceRoleKind.ApproverCandidate) with
        {
            RequestedMissingEvidenceKind = MissingEvidenceKind.ApprovalEvidence
        });

        Assert.IsFalse(decision.AcceptsApproval);
        Assert.IsFalse(decision.IsEvidenceSatisfied);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(GovernanceRoleKind.OperationsReviewer)]
    [DataRow(GovernanceRoleKind.ExecutorOperatorCandidate)]
    [DataRow(GovernanceRoleKind.RollbackReviewer)]
    [DataRow(GovernanceRoleKind.RecoveryReviewer)]
    public void OperationsSupportVisibilityDoesNotExecuteDiagnosticsRetryRollbackOrRecovery(
        GovernanceRoleKind roleKind)
    {
        var decision = Classify(Request(roleKind) with
        {
            RequestedMissingEvidenceKind = MissingEvidenceKind.RouteGuardEvidence,
            RequestedMaterialKind = MissingEvidenceMaterialKind.RedactedSummary
        });

        Assert.AreEqual(MissingEvidenceVisibilityClassification.RedactedSummaryCandidate, decision.Classification);
        Assert.IsFalse(decision.GrantsDiagnosticExecutionAuthority);
        Assert.IsFalse(decision.GrantsRetryAuthority);
        Assert.IsFalse(decision.GrantsRollbackAuthority);
        Assert.IsFalse(decision.GrantsRecoveryAuthority);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void TenantAdministratorVisibilityRequiresTenantBoundaryEvidence()
    {
        var decision = Classify(Request(GovernanceRoleKind.TenantAdministrator) with
        {
            RequestedMissingEvidenceKind = MissingEvidenceKind.RouteGuardEvidence,
            OptionalTenantBoundaryEvidenceRef = null
        });

        Assert.AreEqual(MissingEvidenceVisibilityClassification.BlockedByMissingTenantBoundaryEvidence, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void TenantAdministratorVisibilityDoesNotBecomePlatformOrAdminAuthority()
    {
        var decision = Classify(Request(GovernanceRoleKind.TenantAdministrator) with
        {
            RequestedMissingEvidenceKind = MissingEvidenceKind.RouteGuardEvidence,
            RequestedMaterialKind = MissingEvidenceMaterialKind.CategoryOnly
        });

        Assert.AreEqual(MissingEvidenceVisibilityClassification.CategoryOnlyCandidate, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.SummaryOnly, decision.EffectiveCandidateVisibility);
        Assert.IsFalse(decision.GrantsRoleAssignmentAuthority);
        Assert.IsFalse(decision.GrantsAccess);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void SystemAccountabilityOwnerVisibilityDoesNotBecomeRootControl()
    {
        var decision = Classify(Request(GovernanceRoleKind.SystemAccountabilityOwner) with
        {
            RequestedMissingEvidenceKind = MissingEvidenceKind.RouteGuardEvidence,
            RequestedMaterialKind = MissingEvidenceMaterialKind.RedactedSummary
        });

        Assert.AreEqual(MissingEvidenceVisibilityClassification.RedactedSummaryCandidate, decision.Classification);
        Assert.IsFalse(decision.GrantsAccess);
        Assert.IsFalse(decision.GrantsMutationAuthority);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void AutomationAgentVisibilityDoesNotBecomeAutonomyOrWorkflowContinuation()
    {
        var decision = Classify(Request(GovernanceRoleKind.AutomationAgent) with
        {
            RequestedMissingEvidenceKind = MissingEvidenceKind.RouteGuardEvidence,
            RequestedMaterialKind = MissingEvidenceMaterialKind.RedactedSummary
        });

        Assert.AreEqual(MissingEvidenceVisibilityClassification.CategoryOnlyCandidate, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.SummaryOnly, decision.EffectiveCandidateVisibility);
        Assert.IsFalse(decision.CreatesEvidence);
        Assert.IsFalse(decision.GrantsWorkflowContinuation);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void RedactedSummaryRequiresRedactionEvidence()
    {
        var decision = Classify(Request(GovernanceRoleKind.Reviewer) with
        {
            RequestedMissingEvidenceKind = MissingEvidenceKind.RouteGuardEvidence,
            RequestedMaterialKind = MissingEvidenceMaterialKind.RedactedSummary,
            OptionalRedactionEvidenceRef = null
        });

        Assert.AreEqual(MissingEvidenceVisibilityClassification.BlockedByMissingRedactionEvidence, decision.Classification);
        AssertDecisionAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void EveryDecisionHasFalseAuthorityFlagsAndRequiresSeparateAuthority()
    {
        var requests = new[]
        {
            Request(GovernanceRoleKind.ExternalViewer),
            Request(GovernanceRoleKind.Observer) with { RequestedMissingEvidenceKind = MissingEvidenceKind.RouteGuardEvidence },
            Request(GovernanceRoleKind.Reviewer) with { RequestedMaterialKind = MissingEvidenceMaterialKind.RawPatch },
            Request(GovernanceRoleKind.ApproverCandidate) with { RequestedMissingEvidenceKind = MissingEvidenceKind.ApprovalEvidence },
            Request(GovernanceRoleKind.AutomationAgent) with { RequestedIntent = MissingEvidenceVisibilityIntent.ContinueWorkflow },
            Request(GovernanceRoleKind.TenantAdministrator) with { OptionalTenantBoundaryEvidenceRef = null },
            Request(GovernanceRoleKind.SystemAccountabilityOwner) with { RequestedMaterialKind = MissingEvidenceMaterialKind.RedactedSummary }
        };

        foreach (var request in requests)
        {
            AssertDecisionAuthorityFlagsFalse(Classify(request));
        }
    }

    [TestMethod]
    public void ClassificationVocabularyDoesNotContainAuthorityOrSatisfactionLanguage()
    {
        var names = Enum.GetNames<MissingEvidenceVisibilityClassification>();
        foreach (var forbidden in new[] { "Satisfied", "Allowed", "Authorized", "Permitted", "EvidenceCreated", "EvidenceAccepted", "CanProceed" })
        {
            Assert.IsFalse(names.Any(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)), forbidden);
        }
    }

    [TestMethod]
    public void StaticScanF14AddsNoRuntimeAuthorityOrSurface()
    {
        var source = string.Join(
            Environment.NewLine,
            SourceFiles().Select(File.ReadAllText).Select(StripStringLiterals));

        foreach (var forbiddenToken in new[]
        {
            "ApiController",
            "ControllerBase",
            "MapGet",
            "MapPost",
            "HttpGet",
            "HttpPost",
            "OpenApi",
            "DbContext",
            "SqlConnection",
            "IRepository",
            "HttpClient",
            "ProcessStartInfo",
            "ClaimsPrincipal",
            "UserManager",
            "RoleManager",
            "IAuthorizationHandler",
            "AuthorizationHandler",
            "PermissionResolver",
            "AccessControl",
            "EvidenceWriter",
            "EvidenceStore",
            "SatisfyEvidence",
            "CreateEvidence",
            "WorkflowRunner",
            "SourceApplyExecutor",
            "CommitGateway",
            "PushGateway",
            "PullRequestGateway",
            "MergeExecutor",
            "ReleaseExecutor",
            "DeploymentExecutor",
            "RetryExecutor",
            "RollbackExecutor",
            "RecoveryExecutor",
            "AllowAnonymous",
            "AuthorizeAttribute"
        })
        {
            Assert.IsFalse(
                source.Contains(forbiddenToken, StringComparison.Ordinal),
                $"Unexpected runtime/authority surface token found: {forbiddenToken}");
        }
    }

    [TestMethod]
    public void ReceiptExistsAndStatesMissingEvidenceVisibilityIsNotEvidenceSatisfaction()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "F14_MISSING_EVIDENCE_VISIBILITY_BY_ROLE.md"));

        StringAssert.Contains(receipt, "Missing-evidence visibility is not evidence satisfaction.");
        StringAssert.Contains(receipt, "Seeing the gap is not filling it.");
        StringAssert.Contains(receipt, "does not implement evidence creation");
        StringAssert.Contains(receipt, "does not implement evidence satisfaction");
        StringAssert.Contains(receipt, "does not implement workflow continuation");
        StringAssert.Contains(receipt, "F09 boundary tests remain intentionally deferred.");
    }

    private static MissingEvidenceVisibilityDecision Classify(MissingEvidenceVisibilityRequest request) =>
        new MissingEvidenceVisibilityService().Classify(RoleCatalog(), Matrix(), ForbiddenCatalog(), request);

    private static MissingEvidenceVisibilityRequest Request(GovernanceRoleKind roleKind) =>
        new()
        {
            CorrelationId = "correlation-f14",
            RequestedRoleId = RoleId(roleKind),
            RequestedMissingEvidenceKind = MissingEvidenceKind.RouteGuardEvidence,
            RequestedMaterialKind = MissingEvidenceMaterialKind.CategoryOnly,
            RequestedIntent = MissingEvidenceVisibilityIntent.InspectMissingEvidence,
            RoleCatalogEvidenceRef = "role-catalog:f14",
            VisibilityMatrixEvidenceRef = "visibility-matrix:f14",
            ForbiddenActionCatalogEvidenceRef = "forbidden-action-catalog:f14",
            SourceMissingEvidenceRef = "missing-evidence:f14",
            OptionalTenantBoundaryEvidenceRef = "tenant-boundary-evidence:f14",
            OptionalRedactionEvidenceRef = "redaction-evidence:f14",
            OptionalPolicyEvidenceRef = "policy-evidence:f14",
            OptionalApprovalEvidenceRef = "approval-evidence:f14"
        };

    private static GovernanceRoleCatalog RoleCatalog() => new RoleCatalogService().BuildDefaultCatalog();

    private static RoleVisibilityMatrix Matrix() =>
        new RoleVisibilityMatrixService().BuildDefaultMatrix(RoleCatalog());

    private static ForbiddenActionCatalog ForbiddenCatalog() =>
        new ForbiddenActionCatalogService().BuildDefaultCatalog(RoleCatalog());

    private static string RoleId(GovernanceRoleKind kind) =>
        RoleCatalog().Entries.Single(entry => entry.RoleKind == kind).RoleId;

    private static void AssertDecisionAuthorityFlagsFalse(MissingEvidenceVisibilityDecision decision)
    {
        Assert.IsFalse(decision.IsEvidenceSatisfied);
        Assert.IsFalse(decision.CreatesEvidence);
        Assert.IsFalse(decision.OverridesMissingEvidence);
        Assert.IsFalse(decision.WaivesEvidenceRequirement);
        Assert.IsFalse(decision.GrantsRoleAssignmentAuthority);
        Assert.IsFalse(decision.GrantsVisibilityAuthority);
        Assert.IsFalse(decision.GrantsAccess);
        Assert.IsFalse(decision.AcceptsApproval);
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
        Assert.IsFalse(decision.DisclosesPrivateReasoning);
        Assert.IsTrue(decision.RequiresSeparateAuthority);
    }

    private static IEnumerable<string> SourceFiles()
    {
        var root = RepoRoot();
        yield return Path.Combine(root, "IronDev.Core", "Governance", "MissingEvidenceVisibilityModels.cs");
        yield return Path.Combine(root, "IronDev.Core", "Governance", "MissingEvidenceVisibilityService.cs");
        yield return Path.Combine(root, "IronDev.Core", "Governance", "MissingEvidenceVisibilityValidator.cs");
    }

    private static string StripStringLiterals(string source) =>
        Regex.Replace(source, "\"(?:\\\\.|[^\"\\\\])*\"", "\"\"", RegexOptions.CultureInvariant);

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
