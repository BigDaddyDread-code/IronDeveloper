using System.Text.RegularExpressions;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockF11BackendEndpointCapabilityMetadataTests
{
    [TestMethod]
    public void EndpointMetadataCatalogValidatesSafeEntries()
    {
        var validation = BackendEndpointCapabilityMetadataValidator.ValidateCatalog(Catalog());

        Assert.IsTrue(validation.IsValid, string.Join("; ", validation.Issues));
        Assert.AreEqual(0, validation.UnsafeRefs.Count);
    }

    [TestMethod]
    public void EndpointMetadataIsMetadataOnlyAndDescriptive()
    {
        var decision = Classify(Request("endpoint:f11:metadata"));

        Assert.AreEqual(BackendEndpointCapabilityClassification.MetadataOnlyCandidate, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.MetadataOnly, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void EndpointKeyDoesNotGrantEndpointAuthority()
    {
        var decision = Classify(Request("endpoint:f11:metadata"));

        Assert.AreEqual("endpoint:f11:metadata", decision.EndpointKey);
        Assert.IsFalse(decision.GrantsEndpointAuthority);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void RouteTemplateDoesNotGrantRouteAccess()
    {
        var entry = Entry(
            "endpoint:f11:route-template",
            BackendEndpointCapabilityKind.ReadOnlyMetadata,
            BackendEndpointSensitivityKind.PublicMetadata) with
        {
            RouteTemplate = "/api/governance/endpoint-capabilities/{endpointKey}"
        };

        var decision = Classify(Request(entry.EndpointKey), Catalog(entry));

        Assert.AreEqual(BackendEndpointCapabilityClassification.MetadataOnlyCandidate, decision.Classification);
        Assert.IsFalse(decision.GrantsRouteAccess);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(BackendEndpointHttpMethodKind.Get)]
    [DataRow(BackendEndpointHttpMethodKind.Head)]
    [DataRow(BackendEndpointHttpMethodKind.Options)]
    public void ReadMethodMetadataDoesNotGrantInvocationAuthority(BackendEndpointHttpMethodKind method)
    {
        var entry = Entry(
            $"endpoint:f11:{method.ToString().ToLowerInvariant()}",
            BackendEndpointCapabilityKind.ReadOnlyMetadata,
            BackendEndpointSensitivityKind.PublicMetadata) with
        {
            HttpMethod = method
        };

        var decision = Classify(Request(entry.EndpointKey), Catalog(entry));

        Assert.AreEqual(BackendEndpointCapabilityClassification.MetadataOnlyCandidate, decision.Classification);
        Assert.IsFalse(decision.AllowsInvocation);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(BackendEndpointHttpMethodKind.Post)]
    [DataRow(BackendEndpointHttpMethodKind.Put)]
    [DataRow(BackendEndpointHttpMethodKind.Patch)]
    [DataRow(BackendEndpointHttpMethodKind.Delete)]
    public void WriteMethodMetadataDoesNotGrantMutationAuthority(BackendEndpointHttpMethodKind method)
    {
        var entry = Entry(
            $"endpoint:f11:{method.ToString().ToLowerInvariant()}",
            BackendEndpointCapabilityKind.MutationEndpoint,
            BackendEndpointSensitivityKind.InternalMetadata) with
        {
            HttpMethod = method
        };

        var decision = Classify(Request(entry.EndpointKey), Catalog(entry));

        Assert.AreEqual(BackendEndpointCapabilityClassification.MetadataOnlyCandidate, decision.Classification);
        Assert.IsFalse(decision.GrantsMutationAuthority);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void CapabilityKindDoesNotGrantPermission()
    {
        var decision = Classify(Request("endpoint:f11:status"));

        Assert.AreEqual(BackendEndpointCapabilityClassification.SummaryCandidate, decision.Classification);
        Assert.IsFalse(decision.GrantsAccess);
        Assert.IsFalse(decision.GrantsEndpointAuthority);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void VisibilitySurfaceDoesNotGrantVisibilityAuthority()
    {
        var decision = Classify(Request("endpoint:f11:status"));

        Assert.IsFalse(decision.GrantsVisibilityAuthority);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void VisibilityMaterialKindDoesNotGrantAccess()
    {
        var decision = Classify(Request("endpoint:f11:receipt"));

        Assert.AreEqual(BackendEndpointCapabilityClassification.MetadataOnlyCandidate, decision.Classification);
        Assert.IsFalse(decision.GrantsAccess);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void RequiredEvidenceRefsDoNotSatisfyEvidence()
    {
        var decision = Classify(Request("endpoint:f11:approval"));

        CollectionAssert.Contains(decision.EvidenceRefs.ToList(), "endpoint-metadata:f11");
        Assert.IsFalse(decision.AcceptsApproval);
        Assert.IsFalse(decision.SatisfiesPolicy);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow("endpoint:f11:metadata", BackendEndpointCapabilityClassification.MetadataOnlyCandidate, RoleVisibilityLevel.MetadataOnly)]
    [DataRow("endpoint:f11:summary", BackendEndpointCapabilityClassification.SummaryCandidate, RoleVisibilityLevel.SummaryOnly)]
    [DataRow("endpoint:f11:redacted", BackendEndpointCapabilityClassification.RedactedSummaryCandidate, RoleVisibilityLevel.RedactedDetails)]
    [DataRow("endpoint:f11:status", BackendEndpointCapabilityClassification.SummaryCandidate, RoleVisibilityLevel.SummaryOnly)]
    [DataRow("endpoint:f11:receipt", BackendEndpointCapabilityClassification.MetadataOnlyCandidate, RoleVisibilityLevel.MetadataOnly)]
    [DataRow("endpoint:f11:proposal", BackendEndpointCapabilityClassification.SummaryCandidate, RoleVisibilityLevel.SummaryOnly)]
    [DataRow("endpoint:f11:approval", BackendEndpointCapabilityClassification.RedactedSummaryCandidate, RoleVisibilityLevel.RedactedDetails)]
    [DataRow("endpoint:f11:policy", BackendEndpointCapabilityClassification.RedactedSummaryCandidate, RoleVisibilityLevel.RedactedDetails)]
    [DataRow("endpoint:f11:audit", BackendEndpointCapabilityClassification.SummaryCandidate, RoleVisibilityLevel.SummaryOnly)]
    [DataRow("endpoint:f11:validation", BackendEndpointCapabilityClassification.RedactedSummaryCandidate, RoleVisibilityLevel.RedactedDetails)]
    [DataRow("endpoint:f11:diagnostic", BackendEndpointCapabilityClassification.RedactedSummaryCandidate, RoleVisibilityLevel.RedactedDetails)]
    [DataRow("endpoint:f11:release-readiness", BackendEndpointCapabilityClassification.RedactedSummaryCandidate, RoleVisibilityLevel.RedactedDetails)]
    public void AllowedCapabilityKindsReturnOnlyCandidateClassifications(
        string endpointKey,
        BackendEndpointCapabilityClassification expectedClassification,
        RoleVisibilityLevel expectedLevel)
    {
        var decision = Classify(Request(endpointKey));

        Assert.AreEqual(expectedClassification, decision.Classification);
        Assert.AreEqual(expectedLevel, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(BackendEndpointCapabilityKind.RedactedSummary)]
    [DataRow(BackendEndpointCapabilityKind.ApprovalPackageReadModel)]
    [DataRow(BackendEndpointCapabilityKind.PolicyReviewReadModel)]
    [DataRow(BackendEndpointCapabilityKind.ValidationReviewReadModel)]
    [DataRow(BackendEndpointCapabilityKind.OperationDiagnosticReadModel)]
    [DataRow(BackendEndpointCapabilityKind.ReleaseReadinessReadModel)]
    public void RedactedSummaryCapabilitiesRequireRedactionEvidence(BackendEndpointCapabilityKind capabilityKind)
    {
        var entry = Entry("endpoint:f11:redaction-required", capabilityKind, BackendEndpointSensitivityKind.RedactedSummary);

        var decision = Classify(
            Request(entry.EndpointKey) with { OptionalRedactionEvidenceRef = null },
            Catalog(entry));

        Assert.AreEqual(BackendEndpointCapabilityClassification.BlockedByMissingRedactionEvidence, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(BackendEndpointSensitivityKind.TenantScopedMetadata)]
    [DataRow(BackendEndpointSensitivityKind.ProjectScopedMetadata)]
    public void ScopedEndpointMetadataRequiresTenantBoundaryEvidence(BackendEndpointSensitivityKind sensitivityKind)
    {
        var entry = Entry("endpoint:f11:scoped", BackendEndpointCapabilityKind.ReadOnlyMetadata, sensitivityKind);

        var decision = Classify(
            Request(entry.EndpointKey) with { OptionalTenantBoundaryEvidenceRef = null },
            Catalog(entry));

        Assert.AreEqual(BackendEndpointCapabilityClassification.BlockedByMissingTenantBoundaryEvidence, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void SensitiveSummaryRequiresPolicyEvidence()
    {
        var entry = Entry("endpoint:f11:sensitive", BackendEndpointCapabilityKind.ReadOnlySummary, BackendEndpointSensitivityKind.SensitiveSummary);

        var decision = Classify(
            Request(entry.EndpointKey) with { OptionalPolicyEvidenceRef = null },
            Catalog(entry));

        Assert.AreEqual(BackendEndpointCapabilityClassification.BlockedByMissingPolicyEvidence, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void StatusReadModelMetadataDoesNotContinueWorkflow()
    {
        var decision = Classify(Request("endpoint:f11:status"));

        Assert.IsFalse(decision.GrantsWorkflowContinuation);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ReceiptReadModelMetadataDoesNotGrantDownstreamAuthority()
    {
        var decision = Classify(Request("endpoint:f11:receipt"));

        Assert.IsFalse(decision.GrantsMutationAuthority);
        Assert.IsFalse(decision.GrantsMergeAuthority);
        Assert.IsFalse(decision.GrantsReleaseAuthority);
        Assert.IsFalse(decision.GrantsDeploymentAuthority);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ApprovalPackageEndpointMetadataDoesNotAcceptApproval()
    {
        var decision = Classify(Request("endpoint:f11:approval"));

        Assert.IsFalse(decision.AcceptsApproval);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void PolicyReviewEndpointMetadataDoesNotSatisfyPolicy()
    {
        var decision = Classify(Request("endpoint:f11:policy"));

        Assert.IsFalse(decision.SatisfiesPolicy);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ValidationEndpointMetadataDoesNotRefreshValidation()
    {
        var decision = Classify(Request("endpoint:f11:validation"));

        Assert.IsFalse(decision.RefreshesValidation);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void DiagnosticEndpointMetadataDoesNotExecuteDiagnostics()
    {
        var decision = Classify(Request("endpoint:f11:diagnostic"));

        Assert.IsFalse(decision.GrantsDiagnosticExecutionAuthority);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void MutationEndpointMetadataDoesNotAuthorizeMutation()
    {
        var decision = Classify(Request("endpoint:f11:mutation"));

        Assert.AreEqual(BackendEndpointCapabilityClassification.MetadataOnlyCandidate, decision.Classification);
        Assert.IsFalse(decision.GrantsMutationAuthority);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void WorkflowEndpointMetadataDoesNotContinueWorkflow()
    {
        var decision = Classify(Request("endpoint:f11:workflow"));

        Assert.AreEqual(BackendEndpointCapabilityClassification.MetadataOnlyCandidate, decision.Classification);
        Assert.IsFalse(decision.GrantsWorkflowContinuation);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void ReleaseReadinessEndpointMetadataDoesNotAuthorizeRelease()
    {
        var decision = Classify(Request("endpoint:f11:release-readiness"));

        Assert.IsFalse(decision.GrantsReleaseAuthority);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void DeployEndpointMetadataDoesNotAuthorizeDeployment()
    {
        var decision = Classify(Request("endpoint:f11:deploy"));

        Assert.AreEqual(BackendEndpointCapabilityClassification.MetadataOnlyCandidate, decision.Classification);
        Assert.IsFalse(decision.GrantsDeploymentAuthority);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(BackendEndpointSensitivityKind.RawPayload, BackendEndpointCapabilityClassification.BlockedByRawCapability)]
    [DataRow(BackendEndpointSensitivityKind.CredentialMaterial, BackendEndpointCapabilityClassification.BlockedBySecretCapability)]
    [DataRow(BackendEndpointSensitivityKind.SecretMaterial, BackendEndpointCapabilityClassification.BlockedBySecretCapability)]
    [DataRow(BackendEndpointSensitivityKind.PrivateReasoning, BackendEndpointCapabilityClassification.BlockedByPrivateReasoningCapability)]
    public void RawCredentialSecretAndPrivateReasoningCapabilitiesAreBlocked(
        BackendEndpointSensitivityKind sensitivityKind,
        BackendEndpointCapabilityClassification expected)
    {
        var entry = Entry("endpoint:f11:sensitive-block", BackendEndpointCapabilityKind.ReadOnlyMetadata, sensitivityKind);

        var decision = Classify(Request(entry.EndpointKey), Catalog(entry));

        Assert.AreEqual(expected, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(BackendEndpointCapabilityIntent.InvokeEndpoint, BackendEndpointCapabilityClassification.BlockedByInvocationIntent)]
    [DataRow(BackendEndpointCapabilityIntent.AuthorizeRouteAccess, BackendEndpointCapabilityClassification.BlockedByAccessIntent)]
    [DataRow(BackendEndpointCapabilityIntent.CreateRouteGuard, BackendEndpointCapabilityClassification.BlockedByInvocationIntent)]
    [DataRow(BackendEndpointCapabilityIntent.GrantRoleAccess, BackendEndpointCapabilityClassification.BlockedByAccessIntent)]
    [DataRow(BackendEndpointCapabilityIntent.GrantExternalAccess, BackendEndpointCapabilityClassification.BlockedByAccessIntent)]
    [DataRow(BackendEndpointCapabilityIntent.AcceptApproval, BackendEndpointCapabilityClassification.BlockedByApprovalIntent)]
    [DataRow(BackendEndpointCapabilityIntent.SatisfyPolicy, BackendEndpointCapabilityClassification.BlockedByPolicyIntent)]
    [DataRow(BackendEndpointCapabilityIntent.RefreshValidation, BackendEndpointCapabilityClassification.BlockedByPolicyIntent)]
    [DataRow(BackendEndpointCapabilityIntent.ProveSourceSafety, BackendEndpointCapabilityClassification.BlockedByPolicyIntent)]
    [DataRow(BackendEndpointCapabilityIntent.ExecuteDiagnostic, BackendEndpointCapabilityClassification.BlockedByActionIntent)]
    [DataRow(BackendEndpointCapabilityIntent.ExecuteRetry, BackendEndpointCapabilityClassification.BlockedByActionIntent)]
    [DataRow(BackendEndpointCapabilityIntent.ExecuteRollback, BackendEndpointCapabilityClassification.BlockedByActionIntent)]
    [DataRow(BackendEndpointCapabilityIntent.ExecuteRecovery, BackendEndpointCapabilityClassification.BlockedByActionIntent)]
    [DataRow(BackendEndpointCapabilityIntent.MutateSource, BackendEndpointCapabilityClassification.BlockedByMutationIntent)]
    [DataRow(BackendEndpointCapabilityIntent.ContinueWorkflow, BackendEndpointCapabilityClassification.BlockedByWorkflowIntent)]
    [DataRow(BackendEndpointCapabilityIntent.Merge, BackendEndpointCapabilityClassification.BlockedByReleaseDeployIntent)]
    [DataRow(BackendEndpointCapabilityIntent.Release, BackendEndpointCapabilityClassification.BlockedByReleaseDeployIntent)]
    [DataRow(BackendEndpointCapabilityIntent.Deploy, BackendEndpointCapabilityClassification.BlockedByReleaseDeployIntent)]
    [DataRow(BackendEndpointCapabilityIntent.BypassRedaction, BackendEndpointCapabilityClassification.BlockedByRedactionBypassIntent)]
    [DataRow(BackendEndpointCapabilityIntent.DiscloseSecrets, BackendEndpointCapabilityClassification.BlockedBySecretDisclosureIntent)]
    [DataRow(BackendEndpointCapabilityIntent.DiscloseRawPayload, BackendEndpointCapabilityClassification.BlockedByRawDisclosureIntent)]
    [DataRow(BackendEndpointCapabilityIntent.DisclosePrivateReasoning, BackendEndpointCapabilityClassification.BlockedByPrivateReasoningDisclosureIntent)]
    public void NonMetadataIntentsAreBlocked(
        BackendEndpointCapabilityIntent intent,
        BackendEndpointCapabilityClassification expected)
    {
        var decision = Classify(Request("endpoint:f11:metadata") with { RequestedIntent = intent });

        Assert.AreEqual(expected, decision.Classification);
        Assert.AreEqual(RoleVisibilityLevel.NotVisible, decision.EffectiveCandidateVisibility);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnknownEndpointFailsClosed()
    {
        var decision = Classify(Request("endpoint:f11:missing"));

        Assert.AreEqual(BackendEndpointCapabilityClassification.BlockedByUnknownEndpoint, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void UnknownIntentFailsClosed()
    {
        var decision = Classify(Request("endpoint:f11:metadata") with { RequestedIntent = BackendEndpointCapabilityIntent.Unknown });

        Assert.AreEqual(BackendEndpointCapabilityClassification.BlockedByUnknownIntent, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [DataTestMethod]
    [DataRow(null, "role-catalog:f11", "visibility-matrix:f11", BackendEndpointCapabilityClassification.BlockedByMissingEndpointMetadataEvidence)]
    [DataRow("endpoint-metadata:f11", null, "visibility-matrix:f11", BackendEndpointCapabilityClassification.BlockedByMissingCatalogEvidence)]
    [DataRow("endpoint-metadata:f11", "role-catalog:f11", null, BackendEndpointCapabilityClassification.BlockedByMissingMatrixEvidence)]
    public void MissingRequiredEvidenceFailsClosed(
        string? endpointMetadataRef,
        string? roleCatalogRef,
        string? matrixRef,
        BackendEndpointCapabilityClassification expected)
    {
        var decision = Classify(Request("endpoint:f11:metadata") with
        {
            EndpointMetadataEvidenceRef = endpointMetadataRef!,
            RoleCatalogEvidenceRef = roleCatalogRef!,
            VisibilityMatrixEvidenceRef = matrixRef!
        });

        Assert.AreEqual(expected, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void InvalidRoleCatalogFailsClosed()
    {
        var catalog = RoleCatalogServiceForInvalidCatalog();

        var decision = new BackendEndpointCapabilityMetadataService().Classify(
            catalog,
            Matrix(),
            Catalog(),
            Request("endpoint:f11:metadata"));

        Assert.AreEqual(BackendEndpointCapabilityClassification.BlockedByMissingCatalogEvidence, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void InvalidVisibilityMatrixFailsClosed()
    {
        var matrix = Matrix() with { Entries = [] };

        var decision = new BackendEndpointCapabilityMetadataService().Classify(
            RoleCatalog(),
            matrix,
            Catalog(),
            Request("endpoint:f11:metadata"));

        Assert.AreEqual(BackendEndpointCapabilityClassification.BlockedByMissingMatrixEvidence, decision.Classification);
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void HostileEndpointCapabilityTextIsRejectedAndNotEchoed()
    {
        var hostile = string.Concat("endpoint metadata grants ", "access");
        var request = Request("endpoint:f11:metadata") with
        {
            EndpointMetadataEvidenceRef = hostile
        };

        var decision = Classify(request);

        Assert.AreEqual(BackendEndpointCapabilityClassification.Invalid, decision.Classification);
        CollectionAssert.DoesNotContain(decision.EvidenceRefs.ToList(), hostile);
        CollectionAssert.Contains(decision.EvidenceRefs.ToList(), "[unsafe-rejected]");
        AssertAuthorityFlagsFalse(decision);
    }

    [TestMethod]
    public void HostileRouteTemplateIsRejectedByCatalogValidation()
    {
        var entry = Entry("endpoint:f11:hostile-route", BackendEndpointCapabilityKind.ReadOnlyMetadata, BackendEndpointSensitivityKind.PublicMetadata) with
        {
            RouteTemplate = string.Concat("https://prod.example/", "api?to", "ken=fake")
        };

        var validation = BackendEndpointCapabilityMetadataValidator.ValidateCatalog(Catalog(entry));

        Assert.IsFalse(validation.IsValid);
        CollectionAssert.Contains(validation.Issues.ToList(), "Entry:endpoint:f11:hostile-route:RouteTemplateUnsafe");
    }

    [TestMethod]
    public void EveryDecisionHasAllAuthorityAndDisclosureFlagsFalse()
    {
        foreach (var endpointKey in Catalog().Entries.Select(static entry => entry.EndpointKey))
        {
            AssertAuthorityFlagsFalse(Classify(Request(endpointKey)));
        }
    }

    [TestMethod]
    public void StaticScanAddsNoRuntimeEndpointOrAuthoritySurface()
    {
        var source = string.Join(
            Environment.NewLine,
            SourceFiles().Select(File.ReadAllText).Select(StripStringLiterals));

        var forbidden = new[]
        {
            "ControllerBase",
            "MapGet",
            "MapPost",
            "MapPut",
            "MapPatch",
            "MapDelete",
            "OpenApi",
            "IAuthorizationHandler",
            "AuthorizationHandler",
            "ClaimsPrincipal",
            "DbContext",
            "SqlConnection",
            "HttpClient",
            "Process.Start",
            "RunProcessAsync",
            "File.WriteAllText",
            "WorkflowRunner",
            "SourceApplyExecutor",
            "CommitGateway",
            "PushGateway",
            "ReleaseExecutor",
            "DeploymentExecutor",
            "RouteGuardService",
            "IRouteGuard",
            "UseRouteGuard",
            "PermissionResolver",
            "AccessControl"
        };

        foreach (var forbiddenToken in forbidden)
        {
            Assert.IsFalse(
                source.Contains(forbiddenToken, StringComparison.Ordinal),
                $"Unexpected runtime/authority surface token found: {forbiddenToken}");
        }
    }

    [TestMethod]
    public void ReceiptExistsAndStatesEndpointCapabilityMetadataIsNotEndpointAuthority()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "F11_BACKEND_ENDPOINT_CAPABILITY_METADATA.md"));

        StringAssert.Contains(receipt, "Endpoint capability metadata is not endpoint authority.");
        StringAssert.Contains(receipt, "Knowing what a door is for is not permission to open it.");
        StringAssert.Contains(receipt, "does not implement runtime endpoint registration");
        StringAssert.Contains(receipt, "does not implement route guards");
        StringAssert.Contains(receipt, "does not implement authorization handlers");
        StringAssert.Contains(receipt, "does not implement permission resolution");
        StringAssert.Contains(receipt, "does not implement access control");
    }

    private static BackendEndpointCapabilityDecision Classify(
        BackendEndpointCapabilityMetadataRequest request,
        BackendEndpointCapabilityMetadataCatalog? endpointCatalog = null) =>
        new BackendEndpointCapabilityMetadataService().Classify(
            RoleCatalog(),
            Matrix(),
            endpointCatalog ?? Catalog(),
            request);

    private static BackendEndpointCapabilityMetadataRequest Request(string endpointKey) =>
        new()
        {
            CorrelationId = "correlation-f11",
            RequestedEndpointKey = endpointKey,
            RequestedIntent = BackendEndpointCapabilityIntent.InspectMetadata,
            EndpointMetadataEvidenceRef = "endpoint-metadata:f11",
            RoleCatalogEvidenceRef = "role-catalog:f11",
            VisibilityMatrixEvidenceRef = "visibility-matrix:f11",
            OptionalPolicyEvidenceRef = "policy-evidence:f11",
            OptionalRedactionEvidenceRef = "redaction-evidence:f11",
            OptionalTenantBoundaryEvidenceRef = "tenant-boundary-evidence:f11"
        };

    private static BackendEndpointCapabilityMetadataCatalog Catalog(params BackendEndpointCapabilityMetadataEntry[] extraEntries)
    {
        var entries = new List<BackendEndpointCapabilityMetadataEntry>
        {
            Entry("endpoint:f11:metadata", BackendEndpointCapabilityKind.ReadOnlyMetadata, BackendEndpointSensitivityKind.PublicMetadata),
            Entry("endpoint:f11:summary", BackendEndpointCapabilityKind.ReadOnlySummary, BackendEndpointSensitivityKind.InternalMetadata),
            Entry("endpoint:f11:redacted", BackendEndpointCapabilityKind.RedactedSummary, BackendEndpointSensitivityKind.RedactedSummary),
            Entry("endpoint:f11:status", BackendEndpointCapabilityKind.StatusReadModel, BackendEndpointSensitivityKind.OperationScopedMetadata),
            Entry("endpoint:f11:receipt", BackendEndpointCapabilityKind.ReceiptReadModel, BackendEndpointSensitivityKind.OperationScopedMetadata),
            Entry("endpoint:f11:proposal", BackendEndpointCapabilityKind.ProposalReadModel, BackendEndpointSensitivityKind.ProjectScopedMetadata),
            Entry("endpoint:f11:approval", BackendEndpointCapabilityKind.ApprovalPackageReadModel, BackendEndpointSensitivityKind.SensitiveSummary),
            Entry("endpoint:f11:policy", BackendEndpointCapabilityKind.PolicyReviewReadModel, BackendEndpointSensitivityKind.SensitiveSummary),
            Entry("endpoint:f11:audit", BackendEndpointCapabilityKind.AuditReadModel, BackendEndpointSensitivityKind.TenantScopedMetadata),
            Entry("endpoint:f11:validation", BackendEndpointCapabilityKind.ValidationReviewReadModel, BackendEndpointSensitivityKind.RedactedSummary),
            Entry("endpoint:f11:diagnostic", BackendEndpointCapabilityKind.OperationDiagnosticReadModel, BackendEndpointSensitivityKind.RedactedSummary),
            Entry("endpoint:f11:release-readiness", BackendEndpointCapabilityKind.ReleaseReadinessReadModel, BackendEndpointSensitivityKind.RedactedSummary),
            Entry("endpoint:f11:mutation", BackendEndpointCapabilityKind.MutationEndpoint, BackendEndpointSensitivityKind.InternalMetadata),
            Entry("endpoint:f11:workflow", BackendEndpointCapabilityKind.ExecutionEndpoint, BackendEndpointSensitivityKind.InternalMetadata),
            Entry("endpoint:f11:deploy", BackendEndpointCapabilityKind.AdminEndpoint, BackendEndpointSensitivityKind.InternalMetadata)
        };

        entries.AddRange(extraEntries);

        return new BackendEndpointCapabilityMetadataCatalog
        {
            CatalogId = "backend-endpoint-capability:f11",
            CatalogVersion = "f11",
            Entries = entries,
            BoundaryStatement = "Endpoint capability metadata is not endpoint authority and not route access."
        };
    }

    private static BackendEndpointCapabilityMetadataEntry Entry(
        string endpointKey,
        BackendEndpointCapabilityKind capabilityKind,
        BackendEndpointSensitivityKind sensitivityKind) =>
        new()
        {
            EndpointKey = endpointKey,
            DisplayName = endpointKey.Replace("endpoint:f11:", string.Empty, StringComparison.Ordinal).Replace('-', ' '),
            RouteTemplate = "/api/governance/endpoint-capabilities/{endpointKey}",
            HttpMethod = BackendEndpointHttpMethodKind.Get,
            CapabilityKind = capabilityKind,
            VisibilitySurface = SurfaceFor(capabilityKind),
            VisibilityMaterialKind = MaterialFor(capabilityKind),
            SensitivityKind = sensitivityKind,
            OwningSubsystem = "governance",
            BoundaryStatement = "Endpoint capability metadata is not endpoint authority and not route access.",
            RequiredEvidenceRefs = ["role-catalog:f11", "visibility-matrix:f11", "endpoint-metadata:f11"],
            RequiresSeparateRoleAssignment = true,
            RequiresSeparateVisibilityDecision = true,
            RequiresSeparateAccessDecision = true,
            RequiresSeparatePolicyDecision = true,
            RequiresSeparateRedactionDecision = true,
            RequiresTenantBoundaryDecision = true,
            RequiresSeparateApprovalDecision = true,
            RequiresSeparateExecutionAuthority = true,
            RequiresSeparateMutationAuthority = true,
            RequiresSeparateWorkflowAuthority = true
        };

    private static RoleVisibilitySurface SurfaceFor(BackendEndpointCapabilityKind capabilityKind) =>
        capabilityKind switch
        {
            BackendEndpointCapabilityKind.StatusReadModel => RoleVisibilitySurface.OperationStatus,
            BackendEndpointCapabilityKind.ReceiptReadModel => RoleVisibilitySurface.ReceiptReadModel,
            BackendEndpointCapabilityKind.ProposalReadModel => RoleVisibilitySurface.Proposal,
            BackendEndpointCapabilityKind.ApprovalPackageReadModel => RoleVisibilitySurface.ApprovalPackage,
            BackendEndpointCapabilityKind.PolicyReviewReadModel => RoleVisibilitySurface.PolicyReview,
            BackendEndpointCapabilityKind.AuditReadModel => RoleVisibilitySurface.Audit,
            BackendEndpointCapabilityKind.ValidationReviewReadModel => RoleVisibilitySurface.ValidationReview,
            BackendEndpointCapabilityKind.ReleaseReadinessReadModel => RoleVisibilitySurface.ReleaseReadiness,
            BackendEndpointCapabilityKind.MutationEndpoint => RoleVisibilitySurface.SourceApply,
            BackendEndpointCapabilityKind.ExecutionEndpoint => RoleVisibilitySurface.WorkflowContinuation,
            BackendEndpointCapabilityKind.AdminEndpoint => RoleVisibilitySurface.DeploymentReadiness,
            _ => RoleVisibilitySurface.FrontendReadOnly
        };

    private static RoleVisibilityMaterialKind MaterialFor(BackendEndpointCapabilityKind capabilityKind) =>
        capabilityKind switch
        {
            BackendEndpointCapabilityKind.StatusReadModel => RoleVisibilityMaterialKind.OperationStatusSummary,
            BackendEndpointCapabilityKind.ReceiptReadModel => RoleVisibilityMaterialKind.ReceiptMetadata,
            BackendEndpointCapabilityKind.ProposalReadModel => RoleVisibilityMaterialKind.ProposalSummary,
            BackendEndpointCapabilityKind.ApprovalPackageReadModel => RoleVisibilityMaterialKind.ApprovalPackageSummary,
            BackendEndpointCapabilityKind.PolicyReviewReadModel => RoleVisibilityMaterialKind.PolicyReviewSummary,
            BackendEndpointCapabilityKind.AuditReadModel => RoleVisibilityMaterialKind.AuditTrailSummary,
            BackendEndpointCapabilityKind.ValidationReviewReadModel => RoleVisibilityMaterialKind.ValidationSummary,
            BackendEndpointCapabilityKind.ReleaseReadinessReadModel => RoleVisibilityMaterialKind.ReleaseReadinessSummary,
            BackendEndpointCapabilityKind.MutationEndpoint => RoleVisibilityMaterialKind.SourceApplySummary,
            BackendEndpointCapabilityKind.ExecutionEndpoint => RoleVisibilityMaterialKind.WorkflowContinuationSummary,
            BackendEndpointCapabilityKind.AdminEndpoint => RoleVisibilityMaterialKind.DeploymentReadinessSummary,
            _ => RoleVisibilityMaterialKind.OperationStatusSummary
        };

    private static GovernanceRoleCatalog RoleCatalog() => new RoleCatalogService().BuildDefaultCatalog();

    private static GovernanceRoleCatalog RoleCatalogServiceForInvalidCatalog() =>
        RoleCatalog() with { Entries = [] };

    private static RoleVisibilityMatrix Matrix() =>
        new RoleVisibilityMatrixService().BuildDefaultMatrix(RoleCatalog());

    private static void AssertAuthorityFlagsFalse(BackendEndpointCapabilityDecision decision)
    {
        Assert.IsFalse(decision.GrantsEndpointAuthority);
        Assert.IsFalse(decision.GrantsRouteAccess);
        Assert.IsFalse(decision.AllowsInvocation);
        Assert.IsFalse(decision.CreatesRouteGuard);
        Assert.IsFalse(decision.GrantsRoleAssignmentAuthority);
        Assert.IsFalse(decision.GrantsVisibilityAuthority);
        Assert.IsFalse(decision.GrantsAccess);
        Assert.IsFalse(decision.GrantsExternalAccess);
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
    }

    private static IEnumerable<string> SourceFiles()
    {
        var root = RepoRoot();
        yield return Path.Combine(root, "IronDev.Core", "Governance", "BackendEndpointCapabilityMetadataModels.cs");
        yield return Path.Combine(root, "IronDev.Core", "Governance", "BackendEndpointCapabilityMetadataService.cs");
        yield return Path.Combine(root, "IronDev.Core", "Governance", "BackendEndpointCapabilityMetadataValidator.cs");
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
