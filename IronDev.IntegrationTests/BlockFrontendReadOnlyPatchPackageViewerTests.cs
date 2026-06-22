using IronDev.Api.Controllers;
using IronDev.Core.Governance;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockFrontendReadOnlyPatchPackageViewerTests
{
    private const string PackageId = "patch-package-pr31";
    private static readonly string[] ViewerFiles =
    [
        Path.Combine("IronDev.TauriShell", "src", "features", "governance", "PatchPackageViewerTypes.ts"),
        Path.Combine("IronDev.TauriShell", "src", "features", "governance", "PatchPackageViewer.tsx"),
        Path.Combine("IronDev.TauriShell", "src", "features", "governance", "PatchPackageViewerRoute.tsx")
    ];

    [TestMethod]
    public void PatchPackageViewer_BackendArtifactsEndpointReturnsPatchDiff()
    {
        var model = Api().GetPatchPackageArtifacts(PackageId)!;

        Assert.AreEqual(PackageId, model.PackageId);
        StringAssert.Contains(model.PatchDiffText, "diff --git");
    }

    [TestMethod]
    public void PatchPackageViewer_BackendArtifactsEndpointReturnsReviewSummary()
    {
        var model = Api().GetPatchPackageArtifacts(PackageId)!;

        StringAssert.Contains(model.ReviewSummaryText, "Manual review should inspect the proposed file path.");
    }

    [TestMethod]
    public void PatchPackageViewer_BackendArtifactsEndpointReturnsKnownRisks()
    {
        var model = Api().GetPatchPackageArtifacts(PackageId)!;

        StringAssert.Contains(model.KnownRisksText, "Source apply has not been performed.");
    }

    [TestMethod]
    public void PatchPackageViewer_BackendArtifactsEndpointReturnsValidationSummary()
    {
        var model = Api().GetPatchPackageArtifacts(PackageId)!;

        StringAssert.Contains(model.ValidationSummaryText, "Focused PR31: passed");
        Assert.AreEqual("Passed", model.ValidationOutcome);
    }

    [TestMethod]
    public void PatchPackageViewer_BackendArtifactsEndpointReturnsValidationLanes()
    {
        var model = Api().GetPatchPackageArtifacts(PackageId)!;

        AssertContains(model.WhatRan, "Focused PR31");
        AssertContains(model.WhatPassed, "Frontend PR31");
    }

    [TestMethod]
    public void PatchPackageViewer_BackendArtifactsEndpointReturnsProposedFiles()
    {
        var model = Api().GetPatchPackageArtifacts(PackageId)!;

        AssertContains(model.ProposedFilePaths, "IronDev.Core/Governance/Example.cs");
    }

    [TestMethod]
    public void PatchPackageViewer_BackendArtifactsEndpointReturnsEvidenceRefs()
    {
        var model = Api().GetPatchPackageArtifacts(PackageId)!;

        AssertContains(model.EvidenceRefs, "patch-package:patch-package-pr31");
    }

    [TestMethod]
    public void PatchPackageViewer_BackendArtifactsEndpointReturnsReceiptRefs()
    {
        var model = Api().GetPatchPackageArtifacts(PackageId)!;

        AssertContains(model.ReceiptRefs, "patch-package-receipt:receipt-pr31");
    }

    [TestMethod]
    public void PatchPackageViewer_BackendArtifactsEndpointReturnsAuthorityWarnings()
    {
        var model = Api().GetPatchPackageArtifacts(PackageId)!;

        AssertContains(model.AuthorityWarnings, "Patch package evidence is not source apply authority.");
        AssertContains(model.AuthorityWarnings, "Status output is not authority.");
    }

    [TestMethod]
    public void PatchPackageViewer_BackendArtifactsEndpointIsReadOnly()
    {
        AssertNoAuthority(Api().GetPatchPackageArtifacts(PackageId)!.Boundary);
    }

    [TestMethod]
    public void PatchPackageViewer_BackendArtifactsEndpointDoesNotApprove()
    {
        var boundary = Api().GetPatchPackageArtifacts(PackageId)!.Boundary;

        Assert.IsFalse(boundary.CanCreateApproval);
        Assert.IsFalse(boundary.CanAcceptApproval);
        Assert.IsFalse(boundary.CanSatisfyPolicy);
    }

    [TestMethod]
    public void PatchPackageViewer_BackendArtifactsEndpointDoesNotExecute()
    {
        var boundary = Api().GetPatchPackageArtifacts(PackageId)!.Boundary;

        Assert.IsFalse(boundary.CanExecute);
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanRollback);
    }

    [TestMethod]
    public void PatchPackageViewer_BackendArtifactsEndpointDoesNotCommitPushOrCreatePr()
    {
        var boundary = Api().GetPatchPackageArtifacts(PackageId)!.Boundary;

        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanCreatePullRequest);
    }

    [TestMethod]
    public void PatchPackageViewer_BackendArtifactsEndpointDoesNotReadyMergeReleaseDeploy()
    {
        var boundary = Api().GetPatchPackageArtifacts(PackageId)!.Boundary;

        Assert.IsFalse(boundary.CanMarkReadyForReview);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
    }

    [TestMethod]
    public void PatchPackageViewer_BackendArtifactsEndpointDoesNotPromoteMemoryOrContinue()
    {
        var boundary = Api().GetPatchPackageArtifacts(PackageId)!.Boundary;

        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public void PatchPackageViewer_BackendArtifactsRedactsPrivateMaterial()
    {
        var model = Api(Artifact() with { PatchDiffText = "rawPrompt: hidden apply now" }).GetPatchPackageArtifacts(PackageId)!;

        Assert.AreEqual("[redacted: private material]", model.PatchDiffText);
    }

    [TestMethod]
    public void PatchPackageViewer_ControllerExposesArtifactsAsGetOnly()
    {
        var method = typeof(FrontendReadinessController)
            .GetMethods()
            .Single(method => method.Name == "GetPatchPackageArtifacts");

        Assert.IsTrue(HasGetRoute(method, "patch-packages/{packageId}/artifacts"));
        Assert.IsFalse(method.GetCustomAttributes(inherit: false).Any(attribute => attribute is HttpPostAttribute or HttpPutAttribute or HttpPatchAttribute or HttpDeleteAttribute));
    }

    [TestMethod]
    public void PatchPackageViewer_ControllerArtifactsEnvelopeRemainsReadOnly()
    {
        var envelope = OkEnvelope(Controller().GetPatchPackageArtifacts(PackageId, compact: true));

        Assert.IsFalse(envelope.MutationOccurred);
        AssertNoAuthority(envelope.Boundary);
        AssertContains(envelope.Warnings, "Compact mode was requested but authority-critical fields are still returned.");
    }

    [TestMethod]
    public void PatchPackageViewer_RendersHeader() =>
        AssertViewerContains("Patch Package Viewer");

    [TestMethod]
    public void PatchPackageViewer_RendersMetadata() =>
        AssertViewerContains("patch-package.header");

    [TestMethod]
    public void PatchPackageViewer_RendersPatchDiff() =>
        AssertViewerContains("patch-package.patchDiff");

    [TestMethod]
    public void PatchPackageViewer_RendersReviewSummary() =>
        AssertViewerContains("patch-package.reviewSummary");

    [TestMethod]
    public void PatchPackageViewer_RendersValidationSummary() =>
        AssertViewerContains("patch-package.validationSummary");

    [TestMethod]
    public void PatchPackageViewer_RendersKnownRisks() =>
        AssertViewerContains("patch-package.knownRisks");

    [TestMethod]
    public void PatchPackageViewer_RendersProposedFiles() =>
        AssertViewerContains("patch-package.proposedFiles");

    [TestMethod]
    public void PatchPackageViewer_RendersEvidenceRefs()
    {
        AssertViewerContains("patch-package.evidenceRefs");
        AssertViewerContains("Evidence refs are not approval.");
    }

    [TestMethod]
    public void PatchPackageViewer_RendersReceiptRefs()
    {
        AssertViewerContains("patch-package.receiptRefs");
        AssertViewerContains("Receipt refs are not authority.");
    }

    [TestMethod]
    public void PatchPackageViewer_RendersAuthorityWarnings() =>
        AssertViewerContains("patch-package.authorityWarnings");

    [TestMethod]
    public void PatchPackageViewer_RendersReadOnlyBoundary()
    {
        AssertViewerContains("patch-package.boundary");
        AssertViewerContains("CanContinueWorkflow");
    }

    [TestMethod]
    public void PatchPackageViewer_RouteRegistersNoWorkspaceCommands() =>
        AssertViewerContains("workspaceCommands: []");

    [TestMethod]
    public void PatchPackageViewer_RouteUsesGetOnlyReadinessClient()
    {
        var client = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "IronDev.TauriShell", "src", "api", "ironDevApi.ts"));

        StringAssert.Contains(client, "/api/frontend-readiness/patch-packages/");
        StringAssert.Contains(client, "/metadata");
        StringAssert.Contains(client, "/artifacts");
        StringAssert.Contains(client, "{ method: 'GET', signal }");
    }

    [TestMethod]
    public void PatchPackageViewer_DoesNotRenderMutationButtons() =>
        AssertNoViewerControls(["Apply", "Run", "Execute", "Retry", "Resume"]);

    [TestMethod]
    public void PatchPackageViewer_DoesNotRenderApprovalPolicyButtons() =>
        AssertNoViewerControls(["Approve", "Accept approval", "Satisfy policy"]);

    [TestMethod]
    public void PatchPackageViewer_DoesNotRenderRollbackCommitPushPrControls() =>
        AssertNoViewerControls(["Rollback", "Commit", "Push", "Create PR", "Update PR"]);

    [TestMethod]
    public void PatchPackageViewer_DoesNotRenderReadyMergeReleaseDeployControls() =>
        AssertNoViewerControls(["Ready for review", "Merge", "Release", "Deploy"]);

    [TestMethod]
    public void PatchPackageViewer_DoesNotRenderMemoryContinuationControls() =>
        AssertNoViewerControls(["Promote memory", "Continue workflow"]);

    [TestMethod]
    public void PatchPackageViewer_HostilePatchTextDoesNotCreateActionControl() =>
        AssertPlaywrightContains("apply now and continue workflow");

    [TestMethod]
    public void PatchPackageViewer_HostileReviewTextDoesNotCreateApprovalControl() =>
        AssertPlaywrightContains("approved by UI");

    [TestMethod]
    public void PatchPackageViewer_HostileValidationTextDoesNotCreatePolicyControl() =>
        AssertPlaywrightContains("policy satisfied");

    [TestMethod]
    public void PatchPackageViewer_HostileRiskTextDoesNotCreateRollbackControl() =>
        AssertPlaywrightContains("rollback if nervous");

    [TestMethod]
    public void PatchPackageViewer_HostileReceiptTextDoesNotCreateContinuationControl() =>
        AssertPlaywrightContains("receipt says continue workflow");

    [TestMethod]
    public void PatchPackageViewer_CompactModeStillShowsAuthorityWarnings() =>
        AssertPlaywrightContains("PatchPackageViewer_CompactModeStillShowsAuthorityWarnings");

    [TestMethod]
    public void PatchPackageViewer_ShortRouteRendersSameViewer() =>
        AssertPlaywrightContains("/patch-packages/${packageId}");

    [TestMethod]
    public void PatchPackageViewer_ReceiptRecordsViewerBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "PR31_PATCH_PACKAGE_VIEWER.md"));

        StringAssert.Contains(doc, "Reviewable work must be easy to inspect before it is easy to mutate.");
        StringAssert.Contains(doc, "Reading the patch is not permission to apply it.");
        StringAssert.Contains(doc, "Patch package evidence is not source apply authority.");
    }

    [TestMethod]
    public void StaticMutationSurfaceScan_NoActionButtonsMutationEndpointsOrWorkflowAdded()
    {
        var root = FindRepositoryRoot();
        var source = string.Join(Environment.NewLine, ViewerFiles.Select(path => File.ReadAllText(Path.Combine(root, path))));
        var apiSource = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Controllers", "FrontendReadinessController.cs"));
        var clientSource = File.ReadAllText(Path.Combine(root, "IronDev.TauriShell", "src", "api", "ironDevApi.ts"));
        var forbidden = new[]
        {
            "<button",
            "onClick",
            "href=",
            "[HttpPost",
            "[HttpPut",
            "[HttpPatch",
            "[HttpDelete",
            "RunProcessAsync",
            "ProcessStartInfo",
            "git commit",
            "git push",
            "gh pr",
            "SourceApplyExecutor",
            "ControlledRollbackExecutor",
            "ControlledCommitExecutor",
            "ControlledPushExecutor",
            "ControlledDraftPullRequestExecutor",
            "MergeExecutor",
            "ReleaseExecutor",
            "DeploymentExecutor",
            "MemoryPromotionExecutor",
            "WorkflowContinuationExecutor"
        };

        foreach (var marker in forbidden)
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);

        Assert.IsFalse(apiSource.Contains("[HttpPost", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(clientSource, "getFrontendPatchPackageArtifacts");
        StringAssert.Contains(clientSource, "/artifacts");
        StringAssert.Contains(clientSource, "{ method: 'GET', signal }");
    }

    [TestMethod]
    public void Regression_PR30_OperationStatusViewerRemainsThinAndReadOnly() =>
        AssertFileContains(Path.Combine("IronDev.IntegrationTests", "BlockFrontendThinOperationStatusViewerTests.cs"), "StaticMutationSurfaceScan_NoActionButtonsMutationEndpointsOrWorkflowAdded");

    [TestMethod]
    public void Regression_PR29_ReadinessApiRemainsReadOnly() =>
        AssertFileContains(Path.Combine("IronDev.IntegrationTests", "BlockFrontendReadOnlyReadinessApiTests.cs"), "FrontendReadiness_ControllerExposesOnlyGetEndpoints");

    [TestMethod]
    public void Regression_PR28_PortableMemoryGuardrailsRemainReadOnlyAndBlockProjectTruthAuthorityTransfer() =>
        AssertFileContains(Path.Combine("IronDev.IntegrationTests", "BlockPortableEngineeringMemoryGuardrailsTests.cs"), "PortableMemoryAuthorityTransferRejected");

    [TestMethod]
    public void Regression_PR27_MemoryPromotionPackageStatusDoesNotPromoteMemory() =>
        AssertFileContains(Path.Combine("IronDev.IntegrationTests", "BlockMemoryPromotionPackageStatusTests.cs"), "CanPromoteMemory");

    [TestMethod]
    public void Regression_PR26_MemoryContextRemainsAdvisoryOnly() =>
        AssertFileContains(Path.Combine("IronDev.IntegrationTests", "BlockMemoryReadOnlyPatchContextTests.cs"), "CanPromoteMemory");

    [TestMethod]
    public void Regression_PR25_FormatterRemainsPresentationOnly() =>
        AssertFileContains(Path.Combine("IronDev.Core", "Governance", "GovernedStatusUserMessageFormatter.cs"), "CanExecute = validation.Boundary.CanExecute");

    [TestMethod]
    public void Regression_PR24_BoundedAuthorityLaneStillStopsBeforeDownstreamAuthority() =>
        AssertFileContains(Path.Combine("IronDev.IntegrationTests", "BlockDogfoodBoundedAuthorityDraftPrLaneTests.cs"), "CanContinueWorkflow");

    [TestMethod]
    public void Regression_PR23_AskBeforeLaneStillBlocksSourceApplyWithoutAuthority() =>
        AssertFileContains(Path.Combine("IronDev.IntegrationTests", "BlockDogfoodAskBeforeMutationBoundaryLaneTests.cs"), "CanMutateSource");

    [TestMethod]
    public void Regression_PR22_NoApprovalLaneStillProducesEvidenceOnly() =>
        AssertFileContains(Path.Combine("IronDev.IntegrationTests", "BlockDogfoodNoApprovalProposalOnlyLaneTests.cs"), "AssertNoStatusAuthority");

    [TestMethod]
    public void Regression_PR21_FreshnessGuardRemainsExplanationOnly() =>
        AssertFileContains(Path.Combine("IronDev.IntegrationTests", "BlockRepoStateFreshnessGuardTests.cs"), "CanApplySource");

    [TestMethod]
    public void Regression_PR20_RecoveryRemainsReadOnly() =>
        AssertFileContains(Path.Combine("IronDev.IntegrationTests", "BlockInterruptedRunRecoveryTests.cs"), "CanContinueWorkflow");

    [TestMethod]
    public void Regression_CA_RollbackExecutorStillRequiresSeparateRollbackAuthority() =>
        AssertFileContains(Path.Combine("IronDev.IntegrationTests", "BlockCAControlledRollbackExecutorTests.cs"), "RollbackAuthority");

    [TestMethod]
    public void Regression_ProposalOnlyStillForbidsMutation() =>
        AssertFileContains(Path.Combine("IronDev.Core", "Governance", "ProposalOnlyRunProfileEvaluator.cs"), "SourceApply");

    private static bool HasGetRoute(System.Reflection.MethodInfo method, string route) =>
        method.GetCustomAttributes(typeof(HttpGetAttribute), inherit: false)
            .OfType<HttpGetAttribute>()
            .Any(attribute => string.Equals(attribute.Template, route, StringComparison.OrdinalIgnoreCase));

    private static FrontendReadinessController Controller(FrontendPatchPackageArtifactsReadModel? artifact = null) =>
        new(Api(artifact));

    private static IFrontendReadinessReadApi Api(FrontendPatchPackageArtifactsReadModel? artifact = null) =>
        new FrontendReadinessReadApi(Snapshot(artifact ?? Artifact()));

    private static FrontendReadinessReadSnapshot Snapshot(FrontendPatchPackageArtifactsReadModel artifact)
    {
        return new FrontendReadinessReadSnapshot
        {
            PatchPackages = new Dictionary<string, FrontendPatchPackageMetadataReadModel>(StringComparer.OrdinalIgnoreCase)
            {
                [PackageId] = Metadata()
            },
            PatchPackageArtifacts = new Dictionary<string, FrontendPatchPackageArtifactsReadModel>(StringComparer.OrdinalIgnoreCase)
            {
                [PackageId] = artifact
            }
        };
    }

    private static FrontendPatchPackageMetadataReadModel Metadata() =>
        new()
        {
            PackageId = PackageId,
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "dogfood/bounded-authority-draft-pr-lane",
            RunId = "run-pr31",
            PatchHash = "sha256:patch-pr31",
            ProposedFilePaths = ["IronDev.Core/Governance/Example.cs"],
            ArtifactRefs = ["patch-package:patch-package-pr31", "patch-artifact:patch-package-pr31"],
            EvidenceRefs = ["patch-package:patch-package-pr31", "validation-result:validation-pr31"],
            ReceiptRefs = ["patch-package-receipt:receipt-pr31"],
            ReviewSummaryRef = "review-summary:patch-package-pr31",
            KnownRisksRef = "known-risks:patch-package-pr31",
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };

    private static FrontendPatchPackageArtifactsReadModel Artifact() =>
        new()
        {
            PackageId = PackageId,
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "dogfood/bounded-authority-draft-pr-lane",
            RunId = "run-pr31",
            PatchHash = "sha256:patch-pr31",
            PatchDiffText = "diff --git a/IronDev.Core/Governance/Example.cs b/IronDev.Core/Governance/Example.cs",
            ReviewSummaryText = "Manual review should inspect the proposed file path.",
            KnownRisksText = "Source apply has not been performed.",
            ValidationSummaryText = "Focused PR31: passed",
            ValidationOutcome = "Passed",
            WhatRan = ["Focused PR31", "Frontend PR31"],
            WhatPassed = ["Focused PR31", "Frontend PR31"],
            WhatFailed = [],
            WhatWasSkipped = [],
            ValidationIsStale = false,
            ProposedFilePaths = ["IronDev.Core/Governance/Example.cs"],
            ArtifactRefs = ["patch-artifact:patch-package-pr31"],
            EvidenceRefs = ["patch-package:patch-package-pr31", "validation-result:validation-pr31"],
            ReceiptRefs = ["patch-package-receipt:receipt-pr31"],
            AuthorityWarnings = ["Patch package evidence is not source apply authority.", "Validation evidence is not approval."],
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };

    private static FrontendReadinessApiEnvelope<T> OkEnvelope<T>(ActionResult<FrontendReadinessApiEnvelope<T>> result)
    {
        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        return (FrontendReadinessApiEnvelope<T>)ok.Value!;
    }

    private static void AssertNoViewerControls(IEnumerable<string> labels)
    {
        var source = ViewerSource();
        Assert.IsFalse(source.Contains("<button", StringComparison.OrdinalIgnoreCase));
        foreach (var label in labels)
            Assert.IsFalse(source.Contains($">{label}<", StringComparison.OrdinalIgnoreCase), label);
    }

    private static void AssertViewerContains(string value) =>
        StringAssert.Contains(ViewerSource(), value);

    private static void AssertPlaywrightContains(string value) =>
        StringAssert.Contains(File.ReadAllText(Path.Combine(FindRepositoryRoot(), "IronDev.TauriShell", "tests", "patch-package-viewer.spec.ts")), value);

    private static void AssertFileContains(string relativePath, string value) =>
        StringAssert.Contains(File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath)), value);

    private static string ViewerSource() =>
        string.Join(Environment.NewLine, ViewerFiles.Select(path => File.ReadAllText(Path.Combine(FindRepositoryRoot(), path))));

    private static void AssertNoAuthority(FrontendReadBoundary boundary)
    {
        Assert.IsTrue(boundary.ReadOnly);
        Assert.IsTrue(boundary.StatusOnly);
        Assert.IsFalse(boundary.CanCreateApproval);
        Assert.IsFalse(boundary.CanAcceptApproval);
        Assert.IsFalse(boundary.CanSatisfyPolicy);
        Assert.IsFalse(boundary.CanExecute);
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanRollback);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanCreatePullRequest);
        Assert.IsFalse(boundary.CanMarkReadyForReview);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanContinueWorkflow);
    }

    private static void AssertContains(IEnumerable<string> values, string expected) =>
        Assert.IsTrue(values.Contains(expected, StringComparer.OrdinalIgnoreCase), string.Join(", ", values));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
