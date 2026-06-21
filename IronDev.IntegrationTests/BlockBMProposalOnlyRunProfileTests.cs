using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBMProposalOnlyRunProfileTests
{
    private static readonly string[] ExpectedAllowedOperations =
    [
        ProposalOnlyOperationKinds.RepoInspect,
        ProposalOnlyOperationKinds.TaskInterpretation,
        ProposalOnlyOperationKinds.DisposableWorkspaceCreate,
        ProposalOnlyOperationKinds.DisposableWorkspaceModify,
        ProposalOnlyOperationKinds.DisposableWorkspaceValidate,
        ProposalOnlyOperationKinds.PatchProposal,
        ProposalOnlyOperationKinds.PatchPackageWrite,
        ProposalOnlyOperationKinds.GovernedStatusInspect
    ];

    private static readonly string[] ExpectedBlockedOperations =
    [
        ProposalOnlyOperationKinds.SourceApply,
        ProposalOnlyOperationKinds.Rollback,
        ProposalOnlyOperationKinds.Commit,
        ProposalOnlyOperationKinds.Push,
        ProposalOnlyOperationKinds.DraftPullRequest,
        ProposalOnlyOperationKinds.ReadyForReview,
        ProposalOnlyOperationKinds.Merge,
        ProposalOnlyOperationKinds.Release,
        ProposalOnlyOperationKinds.Deployment,
        ProposalOnlyOperationKinds.MemoryPromotion,
        ProposalOnlyOperationKinds.WorkflowContinuation,
        ProposalOnlyOperationKinds.ApprovalRequestCreate,
        ProposalOnlyOperationKinds.PolicySatisfaction,
        ProposalOnlyOperationKinds.ProviderMutation,
        ProposalOnlyOperationKinds.PackagePublication
    ];

    [TestMethod]
    public void BlockBM_Profile_Exists()
    {
        Assert.AreEqual(1, (int)RunProfileKind.ProposalOnly);
        var boundary = ProposalOnlyRunProfileBoundary.ProposalOnly;

        Assert.IsTrue(boundary.ProfileOnly);
        Assert.IsTrue(boundary.EvidenceOnly);
        Assert.IsTrue(boundary.CanCreateProposalEvidence);
        Assert.IsTrue(boundary.CanWritePatchPackageArtifacts);
        Assert.IsTrue(boundary.CanMutateDisposableWorkspace);
    }

    [TestMethod]
    public void BlockBM_Profile_BoundaryDoesNotGrantDurableAuthority()
    {
        var boundary = ProposalOnlyRunProfileBoundary.ProposalOnly;

        Assert.IsFalse(boundary.CanApprove);
        Assert.IsFalse(boundary.CanSatisfyPolicy);
        Assert.IsFalse(boundary.CanExecuteSourceApply);
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanCreatePullRequest);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanRollback);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanContinueWorkflow);
        Assert.IsFalse(boundary.CanCreateAuthorityRecords);
        Assert.IsFalse(boundary.CanMutateProvider);
        Assert.IsFalse(boundary.CanPublishPackages);
    }

    [TestMethod]
    public void BlockBM_AllowedOperationSet_IsExplicit()
    {
        CollectionAssert.AreEquivalent(ExpectedAllowedOperations, ProposalOnlyRunProfileEvaluator.AllowedOperations.ToArray());
    }

    [TestMethod]
    public void BlockBM_BlockedOperationSet_IsExplicit()
    {
        CollectionAssert.AreEquivalent(ExpectedBlockedOperations, ProposalOnlyRunProfileEvaluator.BlockedOperations.ToArray());
    }

    [TestMethod]
    public void BlockBM_AllowedOperations_ReturnCanonicalEligibleStatus()
    {
        foreach (var operationKind in ExpectedAllowedOperations)
        {
            var result = Evaluate(operationKind);

            Assert.IsTrue(result.IsAllowed, operationKind + ": " + string.Join(", ", result.Issues.Concat(result.RedFlags)));
            Assert.AreEqual(GovernedOperationState.Eligible, result.Status.State, operationKind);
            Assert.AreEqual(operationKind, result.Status.OperationKind);
            AssertValid(result.StatusValidation);
            Assert.AreEqual(0, result.Status.BlockedReasons.Count, operationKind);
            Assert.AreEqual(0, result.Status.MissingEvidence.Count, operationKind);
            Assert.IsTrue(result.Status.NextSafeActions.Count > 0, operationKind);
            Assert.IsTrue(result.Status.ForbiddenActions.Count > 0, operationKind);
            AssertNoStatusAuthority(result.StatusValidation);
        }
    }

    [TestMethod]
    public void BlockBM_BlockedOperations_ReturnCanonicalBlockedStatus()
    {
        foreach (var operationKind in ExpectedBlockedOperations)
        {
            var result = Evaluate(operationKind);

            Assert.IsFalse(result.IsAllowed, operationKind);
            Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State, operationKind);
            Assert.AreEqual(operationKind, result.Status.OperationKind);
            AssertValid(result.StatusValidation);
            AssertContains(result.Status.BlockedReasons, $"ProposalOnly does not allow {operationKind}.");
            AssertContains(result.Status.MissingEvidence, $"explicit-authority:{operationKind}");
            Assert.IsTrue(result.Status.NextSafeActions.Any(action => action.StartsWith("request ", StringComparison.OrdinalIgnoreCase)), operationKind);
            Assert.IsTrue(result.Status.ForbiddenActions.Count > 0, operationKind);
            Assert.IsTrue(result.Issues.Contains($"ProposalOnlyOperationBlocked:{operationKind}", StringComparer.OrdinalIgnoreCase), string.Join(", ", result.Issues));
            AssertNoStatusAuthority(result.StatusValidation);
        }
    }

    [TestMethod]
    public void BlockBM_RepoInspection_IsAllowed()
    {
        var result = Evaluate(ProposalOnlyOperationKinds.RepoInspect);

        AssertAllowed(result);
        AssertContains(result.Status.NextSafeActions, "inspect repository state for proposal context");
    }

    [TestMethod]
    public void BlockBM_TaskInterpretation_IsAllowed()
    {
        var result = Evaluate(ProposalOnlyOperationKinds.TaskInterpretation);

        AssertAllowed(result);
        AssertContains(result.Status.NextSafeActions, "prepare task interpretation evidence");
    }

    [TestMethod]
    public void BlockBM_DisposableWorkspaceCreate_IsAllowed()
    {
        var result = Evaluate(ProposalOnlyOperationKinds.DisposableWorkspaceCreate);

        AssertAllowed(result);
        AssertContains(result.Status.NextSafeActions, "prepare disposable workspace request evidence");
    }

    [TestMethod]
    public void BlockBM_DisposableWorkspaceModify_IsAllowed()
    {
        var result = Evaluate(ProposalOnlyOperationKinds.DisposableWorkspaceModify);

        AssertAllowed(result);
        AssertContains(result.Status.NextSafeActions, "prepare disposable workspace changes only");
    }

    [TestMethod]
    public void BlockBM_DisposableWorkspaceValidate_IsAllowed()
    {
        var result = Evaluate(ProposalOnlyOperationKinds.DisposableWorkspaceValidate);

        AssertAllowed(result);
        AssertContains(result.Status.NextSafeActions, "prepare disposable workspace validation evidence");
    }

    [TestMethod]
    public void BlockBM_PatchProposal_IsAllowedAsEvidenceOnly()
    {
        var result = Evaluate(ProposalOnlyOperationKinds.PatchProposal);

        AssertAllowed(result);
        AssertContains(result.Status.NextSafeActions, "prepare patch proposal evidence in disposable workspace");
        Assert.IsTrue(result.Status.ForbiddenActions.Any(action => action.Contains("proposal evidence as approval", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BlockBM_PatchPackageWrite_IsAllowed()
    {
        var result = Evaluate(ProposalOnlyOperationKinds.PatchPackageWrite);

        AssertAllowed(result);
        AssertContains(result.Status.NextSafeActions, "package patch proposal artifacts for review");
    }

    [TestMethod]
    public void BlockBM_GovernedStatusInspect_IsAllowed()
    {
        var result = Evaluate(ProposalOnlyOperationKinds.GovernedStatusInspect);

        AssertAllowed(result);
        AssertContains(result.Status.NextSafeActions, "inspect governed operation status");
    }

    [TestMethod]
    public void BlockBM_SourceApply_IsBlocked()
    {
        AssertBlocked(Evaluate(ProposalOnlyOperationKinds.SourceApply), ProposalOnlyOperationKinds.SourceApply);
    }

    [TestMethod]
    public void BlockBM_Rollback_IsBlocked()
    {
        AssertBlocked(Evaluate(ProposalOnlyOperationKinds.Rollback), ProposalOnlyOperationKinds.Rollback);
    }

    [TestMethod]
    public void BlockBM_Commit_IsBlocked()
    {
        AssertBlocked(Evaluate(ProposalOnlyOperationKinds.Commit), ProposalOnlyOperationKinds.Commit);
    }

    [TestMethod]
    public void BlockBM_Push_IsBlocked()
    {
        AssertBlocked(Evaluate(ProposalOnlyOperationKinds.Push), ProposalOnlyOperationKinds.Push);
    }

    [TestMethod]
    public void BlockBM_DraftPullRequest_IsBlocked()
    {
        AssertBlocked(Evaluate(ProposalOnlyOperationKinds.DraftPullRequest), ProposalOnlyOperationKinds.DraftPullRequest);
    }

    [TestMethod]
    public void BlockBM_ReadyForReview_IsBlocked()
    {
        AssertBlocked(Evaluate(ProposalOnlyOperationKinds.ReadyForReview), ProposalOnlyOperationKinds.ReadyForReview);
    }

    [TestMethod]
    public void BlockBM_Merge_IsBlocked()
    {
        AssertBlocked(Evaluate(ProposalOnlyOperationKinds.Merge), ProposalOnlyOperationKinds.Merge);
    }

    [TestMethod]
    public void BlockBM_Release_IsBlocked()
    {
        AssertBlocked(Evaluate(ProposalOnlyOperationKinds.Release), ProposalOnlyOperationKinds.Release);
    }

    [TestMethod]
    public void BlockBM_Deployment_IsBlocked()
    {
        AssertBlocked(Evaluate(ProposalOnlyOperationKinds.Deployment), ProposalOnlyOperationKinds.Deployment);
    }

    [TestMethod]
    public void BlockBM_MemoryPromotion_IsBlocked()
    {
        AssertBlocked(Evaluate(ProposalOnlyOperationKinds.MemoryPromotion), ProposalOnlyOperationKinds.MemoryPromotion);
    }

    [TestMethod]
    public void BlockBM_WorkflowContinuation_IsBlocked()
    {
        AssertBlocked(Evaluate(ProposalOnlyOperationKinds.WorkflowContinuation), ProposalOnlyOperationKinds.WorkflowContinuation);
    }

    [TestMethod]
    public void BlockBM_ApprovalRequestCreate_IsBlocked()
    {
        AssertBlocked(Evaluate(ProposalOnlyOperationKinds.ApprovalRequestCreate), ProposalOnlyOperationKinds.ApprovalRequestCreate);
    }

    [TestMethod]
    public void BlockBM_PolicySatisfaction_IsBlocked()
    {
        AssertBlocked(Evaluate(ProposalOnlyOperationKinds.PolicySatisfaction), ProposalOnlyOperationKinds.PolicySatisfaction);
    }

    [TestMethod]
    public void BlockBM_ProviderMutation_IsBlocked()
    {
        AssertBlocked(Evaluate(ProposalOnlyOperationKinds.ProviderMutation), ProposalOnlyOperationKinds.ProviderMutation);
    }

    [TestMethod]
    public void BlockBM_PackagePublication_IsBlocked()
    {
        AssertBlocked(Evaluate(ProposalOnlyOperationKinds.PackagePublication), ProposalOnlyOperationKinds.PackagePublication);
    }

    [TestMethod]
    public void BlockBM_UnknownOperation_IsBlockedAsUnknown()
    {
        var result = Evaluate("TagCreation");

        Assert.IsFalse(result.IsAllowed);
        Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
        AssertContains(result.Issues, "ProposalOnlyOperationKindNotAllowed");
        AssertValid(result.StatusValidation);
    }

    [TestMethod]
    public void BlockBM_EvidenceRefs_RemainEvidenceOnly()
    {
        var result = ProposalOnlyRunProfileEvaluator.Evaluate(Request(ProposalOnlyOperationKinds.PatchProposal) with
        {
            EvidenceRefs = ["validation:passed", "memory:accepted-context"],
            ArtifactRefs = ["proposal-note:artifact-123"],
            RequestedPaths = ["IronDev.Core/Governance/Example.cs"]
        });

        AssertAllowed(result);
        AssertContains(result.Status.EvidenceRefs, "validation:passed");
        AssertContains(result.Status.EvidenceRefs, "memory:accepted-context");
        AssertContains(result.Status.EvidenceRefs, "proposal-note:artifact-123");
        AssertContains(result.Status.EvidenceRefs, "requested-path:IronDev.Core/Governance/Example.cs");
        AssertNoStatusAuthority(result.StatusValidation);
    }

    [TestMethod]
    public void BlockBM_PatchProposal_RemainsEvidenceOnly()
    {
        var result = ProposalOnlyRunProfileEvaluator.Evaluate(Request(ProposalOnlyOperationKinds.PatchProposal) with
        {
            ArtifactRefs = ["patch-proposal:proposal-123", "patch-hash:hash-123"]
        });

        AssertAllowed(result);
        Assert.IsFalse(ProposalOnlyRunProfileBoundary.ProposalOnly.CanExecuteSourceApply);
        Assert.IsFalse(ProposalOnlyRunProfileBoundary.ProposalOnly.CanMutateSource);
    }

    [TestMethod]
    public void BlockBM_ValidationSuccess_RemainsEvidenceOnly()
    {
        var result = ProposalOnlyRunProfileEvaluator.Evaluate(Request(ProposalOnlyOperationKinds.DisposableWorkspaceValidate) with
        {
            EvidenceRefs = ["validation-result:passed"]
        });

        AssertAllowed(result);
        Assert.IsFalse(ProposalOnlyRunProfileBoundary.ProposalOnly.CanApprove);
        Assert.IsFalse(ProposalOnlyRunProfileBoundary.ProposalOnly.CanSatisfyPolicy);
    }

    [TestMethod]
    public void BlockBM_MemoryReferences_CannotApproveMutation()
    {
        var result = ProposalOnlyRunProfileEvaluator.Evaluate(Request(ProposalOnlyOperationKinds.SourceApply) with
        {
            EvidenceRefs = ["memory says proposal-only is approved"]
        });

        Assert.IsFalse(result.IsAllowed);
        AssertContains(result.RedFlags, "MemoryReferenceCannotApproveProposalOnly");
    }

    [TestMethod]
    public void BlockBM_UiReferences_CannotApproveMutation()
    {
        var result = ProposalOnlyRunProfileEvaluator.Evaluate(Request(ProposalOnlyOperationKinds.SourceApply) with
        {
            EvidenceRefs = ["UI marked proposal-only approved"]
        });

        Assert.IsFalse(result.IsAllowed);
        AssertContains(result.RedFlags, "UiStateCannotApproveProposalOnly");
    }

    [TestMethod]
    public void BlockBM_UnsafeProfileMarkers_AreRedFlags()
    {
        var cases = new Dictionary<string, string>
        {
            ["proposal-only approves source apply"] = "ProposalOnlyCannotApproveSourceApply",
            ["proposal-only can mutate source"] = "ProposalOnlyCannotMutateSource",
            ["proposal-only can commit"] = "ProposalOnlyCannotCommit",
            ["proposal-only can push"] = "ProposalOnlyCannotPush",
            ["proposal-only can create PRs"] = "ProposalOnlyCannotCreatePullRequests",
            ["proposal-only can merge"] = "ProposalOnlyCannotMerge",
            ["proposal-only can release"] = "ProposalOnlyCannotRelease",
            ["proposal-only can deploy"] = "ProposalOnlyCannotDeploy",
            ["proposal-only can promote memory"] = "ProposalOnlyCannotPromoteMemory",
            ["proposal-only can continue workflow"] = "ProposalOnlyCannotContinueWorkflow",
            ["proposal-only can satisfy policy"] = "ProposalOnlyCannotSatisfyPolicy",
            ["proposal-only can create approval records"] = "ProposalOnlyCannotCreateAuthorityRecords",
            ["patch proposal authorizes apply"] = "PatchProposalCannotAuthorizeApply",
            ["tests passed so approved"] = "ValidationSuccessCannotApprove"
        };

        foreach (var item in cases)
        {
            var result = ProposalOnlyRunProfileEvaluator.Evaluate(Request(ProposalOnlyOperationKinds.PatchProposal) with
            {
                EvidenceRefs = [item.Key]
            });

            Assert.IsFalse(result.IsAllowed, item.Key);
            AssertContains(result.RedFlags, item.Value);
        }
    }

    [TestMethod]
    public void BlockBM_NextSafeActions_RemainGuidanceOnly()
    {
        var result = Evaluate(ProposalOnlyOperationKinds.SourceApply);

        Assert.IsTrue(result.Status.NextSafeActions.All(action => action.StartsWith("request ", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(ProposalOnlyRunProfileBoundary.ProposalOnly.CanContinueWorkflow);
        Assert.IsFalse(ProposalOnlyRunProfileBoundary.ProposalOnly.CanMutateSource);
    }

    [TestMethod]
    public void BlockBM_Evaluator_ValidatesOutputThroughCanonicalValidator()
    {
        var result = ProposalOnlyRunProfileEvaluator.Evaluate(Request(ProposalOnlyOperationKinds.RepoInspect) with
        {
            OperationId = string.Empty
        });

        Assert.IsFalse(result.IsAllowed);
        AssertContains(result.StatusValidation.Issues, "OperationIdRequired");
        AssertContains(result.Issues, "OperationIdRequired");
    }

    [TestMethod]
    public void BlockBM_StaticBoundary_DoesNotTouchExecutorProviderOrMutationSurface()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "ProposalOnlyRunProfileModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "ProposalOnlyRunProfileEvaluator.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "ProposalOnlyRunProfileStatusMapper.cs")
        };
        var text = string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        var forbidden = new[]
        {
            "RunProcessAsync",
            "ProcessStartInfo",
            "git apply",
            "git commit",
            "git push",
            "gh pr create",
            "gh api",
            "kubectl",
            "terraform apply",
            "docker push",
            "npm publish",
            "source apply execute",
            "rollback execute",
            "commit execute",
            "push execute",
            "merge execute",
            "release execute",
            "deploy execute",
            "promote memory",
            "continue workflow",
            "create approval",
            "satisfy policy"
        };

        foreach (var value in forbidden)
            Assert.IsFalse(text.Contains(value, StringComparison.OrdinalIgnoreCase), value);
    }

    [TestMethod]
    public void BlockBM_Receipt_RecordsProposalOnlyBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "BM_PROPOSAL_ONLY_RUN_PROFILE.md"));

        StringAssert.Contains(doc, "This slice adds the ProposalOnly run profile boundary.");
        StringAssert.Contains(doc, "ProposalOnly allows safe no-approval proposal work:");
        StringAssert.Contains(doc, "ProposalOnly does not approve.");
        StringAssert.Contains(doc, "ProposalOnly does not satisfy policy.");
        StringAssert.Contains(doc, "ProposalOnly does not execute source apply.");
        StringAssert.Contains(doc, "ProposalOnly does not mutate source.");
        StringAssert.Contains(doc, "ProposalOnly does not commit.");
        StringAssert.Contains(doc, "ProposalOnly does not push.");
        StringAssert.Contains(doc, "ProposalOnly does not create PRs.");
        StringAssert.Contains(doc, "ProposalOnly does not mark ready for review.");
        StringAssert.Contains(doc, "ProposalOnly does not merge.");
        StringAssert.Contains(doc, "ProposalOnly does not release.");
        StringAssert.Contains(doc, "ProposalOnly does not deploy.");
        StringAssert.Contains(doc, "ProposalOnly does not execute rollback.");
        StringAssert.Contains(doc, "ProposalOnly does not promote memory.");
        StringAssert.Contains(doc, "ProposalOnly does not continue workflow.");
        StringAssert.Contains(doc, "ProposalOnly does not create authority records.");
        StringAssert.Contains(doc, "ProposalOnly can create evidence.");
        StringAssert.Contains(doc, "ProposalOnly evidence is not authority.");
        StringAssert.Contains(doc, "Patch proposal evidence is not approval.");
        StringAssert.Contains(doc, "Validation success is not approval.");
        StringAssert.Contains(doc, "NextSafeActions are guidance only.");
        StringAssert.Contains(doc, "ProposalOnly can build the case. It cannot carry out the sentence.");
    }

    private static ProposalOnlyRunProfileEvaluationResult Evaluate(string operationKind) =>
        ProposalOnlyRunProfileEvaluator.Evaluate(Request(operationKind));

    private static ProposalOnlyRunProfileEvaluationRequest Request(string operationKind) =>
        new()
        {
            OperationId = $"proposal-only-{operationKind.ToLowerInvariant()}-001",
            OperationKind = operationKind,
            Subject = "repo:BigDaddyDread-code/IronDeveloper branch:main task:proposal-only-boundary",
            RepoId = "BigDaddyDread-code/IronDeveloper",
            Branch = "main",
            ObservedAtUtc = DateTimeOffset.Parse("2026-06-21T05:00:00Z")
        };

    private static void AssertAllowed(ProposalOnlyRunProfileEvaluationResult result)
    {
        Assert.IsTrue(result.IsAllowed, string.Join(", ", result.Issues.Concat(result.RedFlags)));
        Assert.AreEqual(GovernedOperationState.Eligible, result.Status.State);
        AssertValid(result.StatusValidation);
        AssertNoStatusAuthority(result.StatusValidation);
    }

    private static void AssertBlocked(ProposalOnlyRunProfileEvaluationResult result, string operationKind)
    {
        Assert.IsFalse(result.IsAllowed);
        Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
        AssertContains(result.Status.BlockedReasons, $"ProposalOnly does not allow {operationKind}.");
        AssertContains(result.Status.MissingEvidence, $"explicit-authority:{operationKind}");
        AssertContains(result.Issues, $"ProposalOnlyOperationBlocked:{operationKind}");
        AssertValid(result.StatusValidation);
        AssertNoStatusAuthority(result.StatusValidation);
    }

    private static void AssertValid(GovernedOperationStatusValidationResult result) =>
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues.Concat(result.RedFlags).Concat(result.AmberFlags)));

    private static void AssertNoStatusAuthority(GovernedOperationStatusValidationResult result)
    {
        Assert.IsFalse(result.Boundary.CanApprove);
        Assert.IsFalse(result.Boundary.CanSatisfyPolicy);
        Assert.IsFalse(result.Boundary.CanExecute);
        Assert.IsFalse(result.Boundary.CanMutate);
        Assert.IsFalse(result.Boundary.CanMutateSource);
        Assert.IsFalse(result.Boundary.CanPromoteMemory);
        Assert.IsFalse(result.Boundary.CanContinueWorkflow);
    }

    private static void AssertContains(IReadOnlyList<string> values, string expected) =>
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
