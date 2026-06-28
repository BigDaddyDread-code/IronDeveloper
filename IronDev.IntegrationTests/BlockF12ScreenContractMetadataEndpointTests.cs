using System.Reflection;
using System.Text.RegularExpressions;
using IronDev.Api.Governance;
using IronDev.Core.Governance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockF12ScreenContractMetadataEndpointTests
{
    [TestMethod]
    public void ScreenContractMetadataCatalogValidates()
    {
        var catalog = Service().BuildDefaultCatalog();

        var validation = ScreenContractMetadataValidator.ValidateCatalog(catalog);

        Assert.IsTrue(validation.IsValid, string.Join("; ", validation.Issues));
        Assert.AreEqual(0, validation.UnsafeRefs.Count);
    }

    [TestMethod]
    public void ScreenContractMetadataEndpointIsGetOnlyAndAuthorized()
    {
        var controllerType = typeof(ScreenContractMetadataEndpointController);
        var getMethod = controllerType.GetMethod(nameof(ScreenContractMetadataEndpointController.Get));

        Assert.IsNotNull(getMethod);
        Assert.IsTrue(controllerType.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any());
        Assert.IsFalse(controllerType.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any());
        Assert.AreEqual(
            "api/governance/screen-contract-metadata",
            controllerType.GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.IsTrue(getMethod!.GetCustomAttributes<HttpGetAttribute>(inherit: true).Any());
        Assert.IsFalse(getMethod.GetCustomAttributes<HttpPostAttribute>(inherit: true).Any());
        Assert.IsFalse(getMethod.GetCustomAttributes<HttpPutAttribute>(inherit: true).Any());
        Assert.IsFalse(getMethod.GetCustomAttributes<HttpPatchAttribute>(inherit: true).Any());
        Assert.IsFalse(getMethod.GetCustomAttributes<HttpDeleteAttribute>(inherit: true).Any());
    }

    [TestMethod]
    public void ScreenContractMetadataEndpointReturnsCatalog()
    {
        var result = new ScreenContractMetadataEndpointController().Get();
        var ok = result.Result as OkObjectResult;

        Assert.IsNotNull(ok);
        var response = ok!.Value as ScreenContractMetadataResponse;
        Assert.IsNotNull(response);
        Assert.AreEqual("screen-contract-metadata:f12", response!.CatalogId);
        Assert.AreEqual("f12", response.CatalogVersion);
        StringAssert.Contains(response.BoundaryStatement, "not UI authority");
        Assert.IsTrue(response.Entries.Count >= 15);
    }

    [TestMethod]
    public void ScreenKeyFilterReturnsOnlyRequestedScreen()
    {
        var response = Service().GetMetadata("screen:f12:status-viewer");

        Assert.AreEqual(1, response.Entries.Count);
        Assert.AreEqual("screen:f12:status-viewer", response.Entries[0].ScreenKey);
    }

    [DataTestMethod]
    [DataRow("screen:f12:missing")]
    [DataRow("screen grants access")]
    [DataRow("https://prod.example/screen")]
    public void UnknownOrUnsafeScreenKeyReturnsEmptyCatalog(string screenKey)
    {
        var response = Service().GetMetadata(screenKey);

        Assert.AreEqual("screen-contract-metadata:f12", response.CatalogId);
        Assert.AreEqual(0, response.Entries.Count);
    }

    [TestMethod]
    public void EveryEntryIsReadOnlyAndCannotMakeClientPermissionDecisions()
    {
        foreach (var entry in Entries())
        {
            Assert.IsTrue(entry.IsReadOnly, entry.ScreenKey);
            Assert.IsFalse(entry.AllowsLocalAuthorityState, entry.ScreenKey);
            Assert.IsFalse(entry.AllowsClientSidePermissionDecision, entry.ScreenKey);
            AssertEntryAuthorityFlagsFalse(entry);
        }
    }

    [TestMethod]
    public void EveryEntryKeepsSeparateAuthorityGates()
    {
        foreach (var entry in Entries())
        {
            Assert.IsTrue(entry.RequiresSeparateRoleAssignment, entry.ScreenKey);
            Assert.IsTrue(entry.RequiresSeparateVisibilityDecision, entry.ScreenKey);
            Assert.IsTrue(entry.RequiresSeparateAccessDecision, entry.ScreenKey);
            Assert.IsTrue(entry.RequiresSeparatePolicyDecision, entry.ScreenKey);
            Assert.IsTrue(entry.RequiresSeparateRedactionDecision, entry.ScreenKey);
            Assert.IsTrue(entry.RequiresTenantBoundaryDecision, entry.ScreenKey);
            Assert.IsTrue(entry.RequiresSeparateActionAuthority, entry.ScreenKey);
            Assert.IsTrue(entry.RequiresSeparateMutationAuthority, entry.ScreenKey);
            Assert.IsTrue(entry.RequiresSeparateWorkflowAuthority, entry.ScreenKey);
        }
    }

    [DataTestMethod]
    [DataRow("screen:f12:status-viewer")]
    [DataRow("screen:f12:receipt-viewer")]
    [DataRow("screen:f12:proposal-viewer")]
    [DataRow("screen:f12:approval-package-viewer")]
    [DataRow("screen:f12:policy-review-viewer")]
    [DataRow("screen:f12:audit-viewer")]
    [DataRow("screen:f12:validation-review-viewer")]
    [DataRow("screen:f12:diagnostic-viewer")]
    [DataRow("screen:f12:release-readiness-viewer")]
    [DataRow("screen:f12:external-redacted-viewer")]
    [DataRow("screen:f12:action-request-viewer")]
    [DataRow("screen:f12:admin-viewer")]
    [DataRow("screen:f12:mutation-viewer")]
    [DataRow("screen:f12:release-deploy-viewer")]
    public void ScreenSpecificMetadataDoesNotGrantAuthority(string screenKey)
    {
        var entry = Single(screenKey);

        AssertEntryAuthorityFlagsFalse(entry);
        StringAssert.Contains(entry.BoundaryStatement, "not UI authority");
    }

    [TestMethod]
    public void StatusScreenDoesNotContinueWorkflow()
    {
        var entry = Single("screen:f12:status-viewer");

        Assert.AreEqual(ScreenContractKind.StatusViewer, entry.ScreenKind);
        Assert.IsFalse(entry.AllowsWorkflowContinuation);
    }

    [TestMethod]
    public void ReceiptScreenDoesNotGrantDownstreamAuthority()
    {
        var entry = Single("screen:f12:receipt-viewer");

        Assert.AreEqual(ScreenContractKind.ReceiptViewer, entry.ScreenKind);
        Assert.IsFalse(entry.AllowsMutation);
        Assert.IsFalse(entry.AllowsWorkflowContinuation);
    }

    [TestMethod]
    public void ProposalScreenDoesNotGrantSourceApplyAuthority()
    {
        var entry = Single("screen:f12:proposal-viewer");

        Assert.AreEqual(RoleVisibilitySurface.Proposal, entry.VisibilitySurface);
        Assert.IsFalse(entry.AllowsMutation);
        Assert.IsTrue(entry.RequiresSeparateMutationAuthority);
    }

    [TestMethod]
    public void ApprovalScreenDoesNotAcceptApproval()
    {
        var entry = Single("screen:f12:approval-package-viewer");

        Assert.AreEqual(ScreenContractKind.ApprovalPackageViewer, entry.ScreenKind);
        Assert.IsFalse(entry.AllowsApproval);
    }

    [TestMethod]
    public void PolicyScreenDoesNotSatisfyPolicy()
    {
        var entry = Single("screen:f12:policy-review-viewer");

        Assert.AreEqual(ScreenContractKind.PolicyReviewViewer, entry.ScreenKind);
        Assert.IsFalse(entry.AllowsPolicySatisfaction);
    }

    [TestMethod]
    public void ValidationScreenDoesNotRefreshValidation()
    {
        var entry = Single("screen:f12:validation-review-viewer");

        Assert.AreEqual(ScreenContractKind.ValidationReviewViewer, entry.ScreenKind);
        AssertEntryAuthorityFlagsFalse(entry);
    }

    [TestMethod]
    public void DiagnosticScreenDoesNotExecuteDiagnostics()
    {
        var entry = Single("screen:f12:diagnostic-viewer");

        Assert.AreEqual(ScreenContractKind.DiagnosticViewer, entry.ScreenKind);
        Assert.IsTrue(entry.RequiredEvidenceRefs.Contains("screen-contract-metadata:f12"));
        AssertEntryAuthorityFlagsFalse(entry);
    }

    [TestMethod]
    public void ActionRequestScreenDoesNotInvokeAction()
    {
        var entry = Single("screen:f12:action-request-viewer");

        Assert.AreEqual(ScreenContractKind.ActionRequestViewer, entry.ScreenKind);
        Assert.IsFalse(entry.IsActionScreen);
        Assert.IsFalse(entry.AllowsActionInvocation);
        Assert.IsTrue(entry.RequiresSeparateActionAuthority);
    }

    [TestMethod]
    public void AdminScreenDoesNotGrantAdminAuthority()
    {
        var entry = Single("screen:f12:admin-viewer");

        Assert.AreEqual(ScreenContractKind.AdminViewer, entry.ScreenKind);
        Assert.IsFalse(entry.IsAdminScreen);
        Assert.IsFalse(entry.AllowsClientSidePermissionDecision);
    }

    [TestMethod]
    public void MutationScreenDoesNotMutate()
    {
        var entry = Single("screen:f12:mutation-viewer");

        Assert.AreEqual(ScreenContractKind.MutationViewer, entry.ScreenKind);
        Assert.IsFalse(entry.IsMutationScreen);
        Assert.IsFalse(entry.AllowsMutation);
        Assert.IsTrue(entry.RequiresSeparateMutationAuthority);
    }

    [TestMethod]
    public void ReleaseDeployScreenDoesNotReleaseOrDeploy()
    {
        var entry = Single("screen:f12:release-deploy-viewer");

        Assert.AreEqual(ScreenContractKind.ReleaseDeployViewer, entry.ScreenKind);
        Assert.IsFalse(entry.IsReleaseDeployScreen);
        AssertEntryAuthorityFlagsFalse(entry);
    }

    [DataTestMethod]
    [DataRow(ScreenContractSensitivityKind.RawPayload)]
    [DataRow(ScreenContractSensitivityKind.CredentialMaterial)]
    [DataRow(ScreenContractSensitivityKind.SecretMaterial)]
    [DataRow(ScreenContractSensitivityKind.PrivateReasoning)]
    public void RawCredentialSecretAndPrivateScreensAreInvalid(ScreenContractSensitivityKind sensitivityKind)
    {
        var validation = ScreenContractMetadataValidator.ValidateEntry(Entry("screen:f12:bad") with
        {
            SensitivityKind = sensitivityKind
        });

        Assert.IsFalse(validation.IsValid);
        CollectionAssert.Contains(validation.Issues.ToList(), "ScreenContractRawSecretOrPrivateMaterialBlocked");
    }

    [TestMethod]
    public void HostileScreenTextIsRejected()
    {
        var hostile = string.Concat("screen grants ", "access");

        var validation = ScreenContractMetadataValidator.ValidateEntry(Entry("screen:f12:hostile") with
        {
            BoundaryStatement = hostile
        });

        Assert.IsFalse(validation.IsValid);
        CollectionAssert.Contains(validation.Issues.ToList(), "BoundaryStatementUnsafe");
        CollectionAssert.Contains(validation.UnsafeRefs.ToList(), hostile);
    }

    [TestMethod]
    public void HostileRoutePatternIsRejected()
    {
        var route = string.Concat("https://prod.example/screen?to", "ken=fake");

        var validation = ScreenContractMetadataValidator.ValidateEntry(Entry("screen:f12:hostile-route") with
        {
            FrontendRoutePattern = route
        });

        Assert.IsFalse(validation.IsValid);
        CollectionAssert.Contains(validation.Issues.ToList(), "FrontendRoutePatternUnsafe");
    }

    [TestMethod]
    public void EndpointSourceDoesNotExposeWriteMethods()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "IronDev.Api", "Governance", "ScreenContractMetadataEndpoint.cs"));

        Assert.IsTrue(source.Contains("[HttpGet]", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("[HttpPost", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("[HttpPut", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("[HttpPatch", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("[HttpDelete", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("AllowAnonymous", StringComparison.Ordinal));
    }

    [TestMethod]
    public void StaticScanAddsNoPersistenceProviderIdentityOrMutationSurface()
    {
        var source = string.Join(
            Environment.NewLine,
            SourceFiles().Select(File.ReadAllText).Select(StripStringLiterals));

        foreach (var forbiddenToken in new[]
        {
            "MapPost",
            "MapPut",
            "MapPatch",
            "MapDelete",
            "HttpPost",
            "HttpPut",
            "HttpPatch",
            "HttpDelete",
            "IHostedService",
            "BackgroundService",
            "DbContext",
            "SqlConnection",
            "HttpClient",
            "Process.Start",
            "RunProcessAsync",
            "File.WriteAllText",
            "IAuthorizationHandler",
            "AuthorizationHandler",
            "ClaimsPrincipal",
            "UserManager",
            "RoleManager",
            "PermissionResolver",
            "AccessControl",
            "SourceApplyExecutor",
            "CommitGateway",
            "PushGateway",
            "ReleaseExecutor",
            "DeploymentExecutor",
            "WorkflowRunner",
            "OpenApi"
        })
        {
            Assert.IsFalse(
                source.Contains(forbiddenToken, StringComparison.Ordinal),
                $"Unexpected runtime/authority surface token found: {forbiddenToken}");
        }
    }

    [TestMethod]
    public void ReceiptExistsAndStatesScreenContractMetadataIsNotUiAuthority()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "F12_SCREEN_CONTRACT_METADATA_ENDPOINT.md"));

        StringAssert.Contains(receipt, "Screen contract metadata is not UI authority.");
        StringAssert.Contains(receipt, "A screen contract is not a screen permission.");
        StringAssert.Contains(receipt, "does not grant screen access");
        StringAssert.Contains(receipt, "does not implement authorization handlers");
        StringAssert.Contains(receipt, "Existing API authentication is preserved");
        StringAssert.Contains(receipt, "F09 boundary tests remain intentionally deferred");
    }

    private static ScreenContractMetadataService Service() => new();

    private static IReadOnlyList<ScreenContractMetadataEntry> Entries() =>
        Service().BuildDefaultCatalog().Entries;

    private static ScreenContractMetadataEntry Single(string screenKey) =>
        Entries().Single(entry => string.Equals(entry.ScreenKey, screenKey, StringComparison.OrdinalIgnoreCase));

    private static ScreenContractMetadataEntry Entry(string screenKey) =>
        new()
        {
            ScreenKey = screenKey,
            DisplayName = "safe screen",
            FrontendRoutePattern = "/safe/{screenRef}",
            OwningSubsystem = "governance",
            ScreenKind = ScreenContractKind.MetadataCatalog,
            VisibilitySurface = RoleVisibilitySurface.FrontendReadOnly,
            VisibilityMaterialKind = RoleVisibilityMaterialKind.OperationStatusSummary,
            SensitivityKind = ScreenContractSensitivityKind.InternalMetadata,
            PrimaryEndpointKey = "endpoint:f12:screen-contract-metadata",
            RelatedEndpointKeys = [],
            RequiredEvidenceRefs = ["role-catalog:f12", "visibility-matrix:f12", "screen-contract-metadata:f12"],
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

    private static void AssertEntryAuthorityFlagsFalse(ScreenContractMetadataEntry entry)
    {
        Assert.IsFalse(entry.IsActionScreen, entry.ScreenKey);
        Assert.IsFalse(entry.IsMutationScreen, entry.ScreenKey);
        Assert.IsFalse(entry.IsAdminScreen, entry.ScreenKey);
        Assert.IsFalse(entry.IsReleaseDeployScreen, entry.ScreenKey);
        Assert.IsFalse(entry.AllowsLocalAuthorityState, entry.ScreenKey);
        Assert.IsFalse(entry.AllowsClientSidePermissionDecision, entry.ScreenKey);
        Assert.IsFalse(entry.AllowsActionInvocation, entry.ScreenKey);
        Assert.IsFalse(entry.AllowsMutation, entry.ScreenKey);
        Assert.IsFalse(entry.AllowsWorkflowContinuation, entry.ScreenKey);
        Assert.IsFalse(entry.AllowsApproval, entry.ScreenKey);
        Assert.IsFalse(entry.AllowsPolicySatisfaction, entry.ScreenKey);
        Assert.IsFalse(entry.AllowsRedactionBypass, entry.ScreenKey);
        Assert.IsFalse(entry.AllowsRawPayloadDisplay, entry.ScreenKey);
        Assert.IsFalse(entry.AllowsSecretDisplay, entry.ScreenKey);
        Assert.IsFalse(entry.AllowsPrivateReasoningDisplay, entry.ScreenKey);
    }

    private static IEnumerable<string> SourceFiles()
    {
        var root = RepoRoot();
        yield return Path.Combine(root, "IronDev.Core", "Governance", "ScreenContractMetadataModels.cs");
        yield return Path.Combine(root, "IronDev.Core", "Governance", "ScreenContractMetadataService.cs");
        yield return Path.Combine(root, "IronDev.Core", "Governance", "ScreenContractMetadataValidator.cs");
        yield return Path.Combine(root, "IronDev.Api", "Governance", "ScreenContractMetadataEndpoint.cs");
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
