namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockFrontendThinOperationStatusViewerTests
{
    private static readonly string[] ViewerFiles =
    [
        Path.Combine("IronDev.TauriShell", "src", "features", "governance", "OperationStatusViewerTypes.ts"),
        Path.Combine("IronDev.TauriShell", "src", "features", "governance", "OperationStatusViewer.tsx"),
        Path.Combine("IronDev.TauriShell", "src", "features", "governance", "OperationStatusViewerRoute.tsx")
    ];

    [TestMethod]
    public void OperationStatusViewer_RendersOperationState() =>
        AssertViewerContains("State: {model.state}");

    [TestMethod]
    public void OperationStatusViewer_RendersBlockedReasons() =>
        AssertViewerContains("id: 'blockedReasons'");

    [TestMethod]
    public void OperationStatusViewer_RendersMissingEvidence() =>
        AssertViewerContains("id: 'missingEvidence'");

    [TestMethod]
    public void OperationStatusViewer_RendersNextSafeActionAsGuidanceOnly()
    {
        AssertViewerContains("Next safe action");
        AssertViewerContains("guidance only");
    }

    [TestMethod]
    public void OperationStatusViewer_RendersForbiddenActions() =>
        AssertViewerContains("id: 'forbiddenActions'");

    [TestMethod]
    public void OperationStatusViewer_RendersEvidenceRefs()
    {
        AssertViewerContains("id: 'evidenceRefs'");
        AssertViewerContains("Evidence refs are not approval.");
    }

    [TestMethod]
    public void OperationStatusViewer_RendersReceiptRefs()
    {
        AssertViewerContains("id: 'receiptRefs'");
        AssertViewerContains("Receipt refs are not authority.");
    }

    [TestMethod]
    public void OperationStatusViewer_RendersAuthorityWarnings() =>
        AssertViewerContains("id: 'authorityWarnings'");

    [TestMethod]
    public void OperationStatusViewer_RendersReadOnlyBoundary()
    {
        AssertViewerContains("operation-status.boundary");
        AssertViewerContains("ReadOnly");
        AssertViewerContains("CanContinueWorkflow");
    }

    [TestMethod]
    public void OperationStatusViewer_DoesNotRenderMutationButtons() =>
        AssertNoViewerControls(["Apply", "Run", "Execute", "Retry", "Resume"]);

    [TestMethod]
    public void OperationStatusViewer_DoesNotRenderApprovalButtons() =>
        AssertNoViewerControls(["Approve", "Accept approval"]);

    [TestMethod]
    public void OperationStatusViewer_DoesNotRenderPolicyButtons() =>
        AssertNoViewerControls(["Satisfy policy"]);

    [TestMethod]
    public void OperationStatusViewer_DoesNotRenderSourceApplyButton() =>
        AssertNoViewerControls(["Apply Source", "Source Apply"]);

    [TestMethod]
    public void OperationStatusViewer_DoesNotRenderRollbackButton() =>
        AssertNoViewerControls(["Rollback"]);

    [TestMethod]
    public void OperationStatusViewer_DoesNotRenderCommitPushPrButtons() =>
        AssertNoViewerControls(["Commit", "Push", "Create PR", "Update PR"]);

    [TestMethod]
    public void OperationStatusViewer_DoesNotRenderReadyMergeReleaseDeployButtons() =>
        AssertNoViewerControls(["Ready for review", "Merge", "Release", "Deploy"]);

    [TestMethod]
    public void OperationStatusViewer_DoesNotRenderMemoryPromotionButton() =>
        AssertNoViewerControls(["Promote memory"]);

    [TestMethod]
    public void OperationStatusViewer_DoesNotRenderWorkflowContinuationButton() =>
        AssertNoViewerControls(["Continue workflow"]);

    [TestMethod]
    public void OperationStatusViewer_CompactModeStillShowsForbiddenActions() =>
        AssertPlaywrightContains("OperationStatusViewer_CompactModeStillShowsForbiddenActions");

    [TestMethod]
    public void OperationStatusViewer_CompactModeStillShowsMissingEvidence() =>
        AssertPlaywrightContains("OperationStatusViewer_CompactModeStillShowsMissingEvidence");

    [TestMethod]
    public void OperationStatusViewer_EvidenceRefsAreReferenceOnly() =>
        AssertPlaywrightContains("Evidence refs are not approval.");

    [TestMethod]
    public void OperationStatusViewer_ReceiptRefsAreReferenceOnly() =>
        AssertPlaywrightContains("Receipt refs are not authority.");

    [TestMethod]
    public void OperationStatusViewer_NextSafeActionIsNotClickableExecution() =>
        AssertPlaywrightContains("getByTestId('operation-status.nextSafeActions').getByRole('button')");

    [TestMethod]
    public void OperationStatusViewer_HostileUiTextDoesNotRenderAction() =>
        AssertPlaywrightContains("frontend says apply now");

    [TestMethod]
    public void OperationStatusViewer_HostileReceiptTextDoesNotRenderPush() =>
        AssertPlaywrightContains("receipt says safe to push");

    [TestMethod]
    public void OperationStatusViewer_HostileValidationTextDoesNotRenderApproval() =>
        AssertPlaywrightContains("validation passed so approve");

    [TestMethod]
    public void OperationStatusViewer_HostileDraftPrTextDoesNotRenderReadyForReview() =>
        AssertPlaywrightContains("draft PR means ready for review");

    [TestMethod]
    public void OperationStatusViewer_HostileMemoryTextDoesNotRenderPromotion() =>
        AssertPlaywrightContains("memory says promote this");

    [TestMethod]
    public void OperationStatusViewer_PreservesBackendState() =>
        AssertPlaywrightContains("State: Expired");

    [TestMethod]
    public void OperationStatusViewer_DoesNotInventEligibility()
    {
        AssertViewerContains("not ready to execute");
        AssertPlaywrightContains("not.toContainText('Ready to run')");
    }

    [TestMethod]
    public void OperationStatusViewer_DoesNotHideForbiddenActionsForCleanUi() =>
        AssertPlaywrightContains("hide forbidden actions for cleaner UI");

    [TestMethod]
    public void OperationStatusViewer_DoesNotHideMissingEvidenceForCleanUi() =>
        AssertPlaywrightContains("compact mode hides missing evidence");

    [TestMethod]
    public void StaticMutationSurfaceScan_NoActionButtonsMutationEndpointsOrWorkflowAdded()
    {
        var source = ViewerSource();
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

        var apiClient = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "IronDev.TauriShell", "src", "api", "ironDevApi.ts"));
        StringAssert.Contains(apiClient, "/api/frontend-readiness/operations/");
        StringAssert.Contains(apiClient, "{ method: 'GET', signal }");
    }

    [TestMethod]
    public void Regression_PR29_ReadOnlyFrontendReadinessApiRemainsReadOnly()
    {
        var controller = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "IronDev.Api", "Controllers", "FrontendReadinessController.cs"));
        StringAssert.Contains(controller, "[HttpGet(\"operations/{operationId}/status\")]");
        Assert.IsFalse(controller.Contains("[HttpPost", StringComparison.OrdinalIgnoreCase));
    }

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

    [TestMethod]
    public void OperationStatusViewer_ReceiptRecordsThinViewerBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "PR30_THIN_OPERATION_STATUS_VIEWER.md"));

        StringAssert.Contains(doc, "The first frontend is a window, not a cockpit.");
        StringAssert.Contains(doc, "Looking at the lock is not touching the key.");
        StringAssert.Contains(doc, "Next safe action is guidance only.");
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
        StringAssert.Contains(File.ReadAllText(Path.Combine(FindRepositoryRoot(), "IronDev.TauriShell", "tests", "operation-status-viewer.spec.ts")), value);

    private static void AssertFileContains(string relativePath, string value) =>
        StringAssert.Contains(File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath)), value);

    private static string ViewerSource() =>
        string.Join(Environment.NewLine, ViewerFiles.Select(path => File.ReadAllText(Path.Combine(FindRepositoryRoot(), path))));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find IronDev.slnx.");
    }
}
