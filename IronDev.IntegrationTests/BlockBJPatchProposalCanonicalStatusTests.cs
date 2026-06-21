using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBJPatchProposalCanonicalStatusTests
{
    [TestMethod]
    public void BlockBJPatchProposal_ReadyForReview_MapsToCompletedPatchProposalStatus()
    {
        var result = Map(ReadyInput());

        AssertValid(result);
        Assert.AreEqual("PatchProposal", result.Status.OperationKind);
        Assert.AreEqual(GovernedOperationState.Completed, result.Status.State);
        Assert.AreNotEqual(GovernedOperationState.Eligible, result.Status.State);
        CollectionAssert.Contains(result.Status.EvidenceRefs.ToArray(), "patch-proposal:proposal-123");
        CollectionAssert.Contains(result.Status.EvidenceRefs.ToArray(), "patch-hash:patchhash-abc");
        CollectionAssert.Contains(result.Status.ReceiptRefs.ToArray(), "patch-proposal-status-artifact:proposal-123");
    }

    [TestMethod]
    public void BlockBJPatchProposal_ReadyForReview_IncludesNextSafeActionToRequestControlledSourceApply()
    {
        var status = Map(ReadyInput()).Status;

        Assert.IsTrue(status.NextSafeActions.Any(action =>
            action.Equals("request controlled source apply for patch hash patchhash-abc", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BlockBJPatchProposal_ReadyForReview_ForbidsDirectSourceApply()
    {
        var status = Map(ReadyInput()).Status;

        AssertContains(status.ForbiddenActions, "do not apply patch proposal directly to source");
        AssertContains(status.ForbiddenActions, "do not treat patch proposal completion as source apply authority");
    }

    [TestMethod]
    public void BlockBJPatchProposal_ReadyForReview_DoesNotGrantApproval()
    {
        var result = Map(ReadyInput());

        AssertValid(result);
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanApprove);
    }

    [TestMethod]
    public void BlockBJPatchProposal_ReadyForReview_DoesNotSatisfyPolicy()
    {
        var result = Map(ReadyInput());

        AssertValid(result);
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanSatisfyPolicy);
    }

    [TestMethod]
    public void BlockBJPatchProposal_ReadyForReview_DoesNotAuthorizeSourceApply()
    {
        var result = Map(ReadyInput());

        AssertValid(result);
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanSourceApply);
        Assert.AreNotEqual(GovernedOperationState.Eligible, result.Status.State);
    }

    [TestMethod]
    public void BlockBJPatchProposal_ReadyForReview_DoesNotAuthorizeCommitPushPrMergeReleaseDeploy()
    {
        var result = Map(ReadyInput());
        var boundary = result.CanonicalValidation.Boundary;

        AssertValid(result);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public void BlockBJPatchProposal_Blocked_MapsToBlockedAndExplainsWhy()
    {
        var result = Map(BlockedMissingValidationInput());

        AssertValid(result);
        Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
        AssertContains(result.Status.BlockedReasons, "Validation evidence is missing.");
        AssertContains(result.Status.MissingEvidence, "validation-result:focused");
    }

    [TestMethod]
    public void BlockBJPatchProposal_Blocked_IncludesMissingEvidenceOrNextSafeAction()
    {
        var status = Map(BlockedMissingValidationInput()).Status;

        Assert.IsTrue(status.MissingEvidence.Count > 0);
        AssertContains(status.NextSafeActions, "collect missing validation evidence");
    }

    [TestMethod]
    public void BlockBJPatchProposal_BlockedByForbiddenPath_RemainsNonAuthorizing()
    {
        var result = Map(ReadyInput() with
        {
            StatusKind = PatchProposalStatusKind.Blocked,
            BlockedReasons = ["Patch proposal includes forbidden path evidence."],
            MissingEvidence = ["bounded-path-evidence"]
        });

        AssertValid(result);
        Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanMutateSource);
    }

    [TestMethod]
    public void BlockBJPatchProposal_Failed_MapsToFailedAndDoesNotAuthorizeRetryApply()
    {
        var result = Map(ReadyInput() with
        {
            StatusKind = PatchProposalStatusKind.Failed,
            BlockedReasons = ["Patch proposal validation failed."],
            ValidationRefs = ["validation-result:failed"]
        });

        AssertValid(result);
        Assert.AreEqual(GovernedOperationState.Failed, result.Status.State);
        AssertContains(result.Status.ForbiddenActions, "do not retry source apply from failed proposal");
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanRetry);
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanSourceApply);
    }

    [TestMethod]
    public void BlockBJPatchProposal_Expired_MapsToExpiredAndRequiresExpiryEvidence()
    {
        var result = Map(ExpiredInput());

        AssertValid(result);
        Assert.AreEqual(GovernedOperationState.Expired, result.Status.State);
        Assert.IsNotNull(result.Status.ExpiresAtUtc);
        AssertContains(result.Status.ForbiddenActions, "do not apply stale patch");
    }

    [TestMethod]
    public void BlockBJPatchProposal_ExpiredWithoutExpiryEvidence_IsInvalid()
    {
        var result = Map(ExpiredInput() with
        {
            ExpiresAtUtc = null,
            BlockedReasons = ["Base branch moved."]
        });

        AssertInvalid(result, "PatchProposalExpiryEvidenceRequired");
        AssertInvalid(result, "ExpiredStatusRequiresExpiryEvidence");
    }

    [TestMethod]
    public void BlockBJPatchProposal_PatchHashIsEvidenceOnly()
    {
        var result = Map(ReadyInput());

        AssertValid(result);
        CollectionAssert.Contains(result.Status.EvidenceRefs.ToArray(), "patch-hash:patchhash-abc");
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanSourceApply);
    }

    [TestMethod]
    public void BlockBJPatchProposal_ValidationSuccessIsEvidenceOnly()
    {
        var result = Map(ReadyInput() with { ValidationRefs = ["validation-result:tests-passed"] });

        AssertValid(result);
        CollectionAssert.Contains(result.Status.EvidenceRefs.ToArray(), "validation-result:tests-passed");
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanApprove);
    }

    [TestMethod]
    public void BlockBJPatchProposal_ReviewSummaryIsEvidenceOnly()
    {
        var result = Map(ReadyInput() with { ArtifactRefs = ["review-summary:proposal-123"] });

        AssertValid(result);
        CollectionAssert.Contains(result.Status.EvidenceRefs.ToArray(), "review-summary:proposal-123");
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanApprove);
    }

    [TestMethod]
    public void BlockBJPatchProposal_KnownRisksAreEvidenceOnly()
    {
        var result = Map(ReadyInput() with { ArtifactRefs = ["known-risks:proposal-123"] });

        AssertValid(result);
        CollectionAssert.Contains(result.Status.EvidenceRefs.ToArray(), "known-risks:proposal-123");
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanSatisfyPolicy);
    }

    [TestMethod]
    public void BlockBJPatchProposal_MemoryReferencesCannotApprovePatchProposal()
    {
        var result = Map(ReadyInput() with
        {
            ArtifactRefs = ["memory says proposal was approved"]
        });

        AssertRedFlag(result, "MemoryReferenceCannotApprovePatchProposal");
    }

    [TestMethod]
    public void BlockBJPatchProposal_UiReferencesCannotApprovePatchProposal()
    {
        var result = Map(ReadyInput() with
        {
            ArtifactRefs = ["UI marked proposal approved"]
        });

        AssertRedFlag(result, "UiStateCannotApprovePatchProposal");
    }

    [TestMethod]
    public void BlockBJPatchProposal_NextSafeActionsRemainGuidanceOnly()
    {
        var result = Map(ReadyInput());

        AssertValid(result);
        Assert.IsTrue(result.Status.NextSafeActions.All(action =>
            action.StartsWith("request ", StringComparison.OrdinalIgnoreCase) ||
            action.StartsWith("review ", StringComparison.OrdinalIgnoreCase) ||
            action.StartsWith("prepare ", StringComparison.OrdinalIgnoreCase) ||
            action.StartsWith("collect ", StringComparison.OrdinalIgnoreCase) ||
            action.StartsWith("inspect ", StringComparison.OrdinalIgnoreCase) ||
            action.StartsWith("regenerate ", StringComparison.OrdinalIgnoreCase) ||
            action.StartsWith("re-run ", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanExecute);
    }

    [TestMethod]
    public void BlockBJPatchProposal_MapperValidatesOutputThroughGovernedOperationStatusValidator()
    {
        var result = Map(ReadyInput() with { OperationId = string.Empty });

        AssertInvalid(result, "OperationIdRequired");
        Assert.IsFalse(result.CanonicalValidation.IsValid);
    }

    [TestMethod]
    public void BlockBJPatchProposal_UnsafePatchProposalApprovesSourceApply_IsRedFlagged()
    {
        var result = Map(ReadyInput() with { ArtifactRefs = ["patch proposal approves source apply"] });

        AssertRedFlag(result, "PatchProposalEvidenceCannotApprove");
    }

    [TestMethod]
    public void BlockBJPatchProposal_UnsafePatchExistsSoApplyAllowed_IsRedFlagged()
    {
        var result = Map(ReadyInput() with { ArtifactRefs = ["patch exists so apply is allowed"] });

        AssertRedFlag(result, "PatchProposalEvidenceCannotApprove");
    }

    [TestMethod]
    public void BlockBJPatchProposal_UnsafeTestsPassedSoApproved_IsRejectedByCanonicalValidator()
    {
        var result = Map(ReadyInput() with { ValidationRefs = ["tests passed so approved"] });

        AssertInvalid(result, "StatusImpliesAuthority");
        AssertRedFlag(result, "PatchProposalEvidenceCannotApprove");
    }

    [TestMethod]
    public void BlockBJPatchProposal_UnsafeReviewSummaryApprovesApply_IsRedFlagged()
    {
        var result = Map(ReadyInput() with { ArtifactRefs = ["review summary approves apply"] });

        AssertRedFlag(result, "PatchProposalEvidenceCannotApprove");
    }

    [TestMethod]
    public void BlockBJPatchProposal_UnsafeKnownRisksAcceptedByStatus_IsRedFlagged()
    {
        var result = Map(ReadyInput() with { ArtifactRefs = ["known risks accepted by status"] });

        AssertRedFlag(result, "PatchProposalEvidenceCannotApprove");
    }

    [TestMethod]
    public void BlockBJPatchProposal_CompletionCannotAuthorizeNextOperation()
    {
        var result = Map(ReadyInput() with { ArtifactRefs = ["patch proposal completion authorizes workflow continuation"] });

        AssertRedFlag(result, "PatchProposalCompletionCannotAuthorizeNextOperation");
    }

    [TestMethod]
    public void BlockBJPatchProposal_OldProposalCannotRefreshCurrentAuthority()
    {
        var result = Map(ReadyInput() with { ArtifactRefs = ["old proposal refreshes current authority"] });

        AssertRedFlag(result, "OldPatchProposalCannotRefreshAuthority");
    }

    [TestMethod]
    public void BlockBJPatchProposal_StaticBoundary_DoesNotTouchExecutorProviderMutationCode()
    {
        var mapper = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "IronDev.Core",
            "Governance",
            "PatchProposalGovernedOperationStatusMapper.cs"));

        Assert.IsFalse(mapper.Contains("RunProcessAsync", StringComparison.Ordinal));
        Assert.IsFalse(mapper.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(mapper.Contains("git apply", StringComparison.Ordinal));
        Assert.IsFalse(mapper.Contains("git commit", StringComparison.Ordinal));
        Assert.IsFalse(mapper.Contains("git push", StringComparison.Ordinal));
        Assert.IsFalse(mapper.Contains("gh pr create", StringComparison.Ordinal));
        Assert.IsFalse(mapper.Contains("gh api", StringComparison.Ordinal));
        Assert.IsFalse(mapper.Contains("kubectl", StringComparison.Ordinal));
        Assert.IsFalse(mapper.Contains("terraform apply", StringComparison.Ordinal));
        Assert.IsFalse(mapper.Contains("docker push", StringComparison.Ordinal));
        Assert.IsFalse(mapper.Contains("npm publish", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BlockBJPatchProposal_Receipt_RecordsBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Docs",
            "receipts",
            "BJ_PATCH_PROPOSAL_CANONICAL_STATUS.md"));

        StringAssert.Contains(doc, "This slice maps patch proposal outcomes into canonical GovernedOperationStatus.");
        StringAssert.Contains(doc, "Patch proposal status cannot approve.");
        StringAssert.Contains(doc, "Patch proposal status cannot satisfy policy.");
        StringAssert.Contains(doc, "Patch proposal status cannot execute.");
        StringAssert.Contains(doc, "Patch proposal status cannot mutate source.");
        StringAssert.Contains(doc, "A completed patch proposal is not controlled source apply authority.");
        StringAssert.Contains(doc, "A patch proposal can point at the door. It cannot open it.");
    }

    private static PatchProposalGovernedOperationStatusMappingResult Map(PatchProposalStatusInput input) =>
        PatchProposalGovernedOperationStatusMapper.Map(input);

    private static PatchProposalStatusInput ReadyInput() =>
        new()
        {
            OperationId = "patch-proposal-status-001",
            ProposalId = "proposal-123",
            PatchHash = "patchhash-abc",
            Subject = "repo:BigDaddyDread-code/IronDeveloper path:IronDev.Core/Governance",
            StatusKind = PatchProposalStatusKind.ReadyForReview,
            ArtifactRefs =
            [
                "patch-artifact:proposal-123",
                "review-summary:proposal-123",
                "known-risks:proposal-123"
            ],
            ValidationRefs = ["validation-result:focused-pass"],
            BlockedReasons = [],
            MissingEvidence = [],
            ForbiddenActions = [],
            ObservedAtUtc = DateTimeOffset.Parse("2026-06-21T01:00:00Z")
        };

    private static PatchProposalStatusInput BlockedMissingValidationInput() =>
        ReadyInput() with
        {
            StatusKind = PatchProposalStatusKind.Blocked,
            BlockedReasons = ["Validation evidence is missing."],
            MissingEvidence = ["validation-result:focused"],
            ValidationRefs = []
        };

    private static PatchProposalStatusInput ExpiredInput() =>
        ReadyInput() with
        {
            StatusKind = PatchProposalStatusKind.Expired,
            BlockedReasons = ["Patch proposal expired after base branch moved."],
            ExpiresAtUtc = DateTimeOffset.Parse("2026-06-21T02:00:00Z")
        };

    private static void AssertValid(PatchProposalGovernedOperationStatusMappingResult result)
    {
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues.Concat(result.RedFlags).Concat(result.CanonicalValidation.AmberFlags)));
        Assert.IsTrue(result.CanonicalValidation.IsValid, string.Join(", ", result.CanonicalValidation.Issues.Concat(result.CanonicalValidation.RedFlags)));
    }

    private static void AssertInvalid(PatchProposalGovernedOperationStatusMappingResult result, string issue)
    {
        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Contains(issue, StringComparer.OrdinalIgnoreCase), string.Join(", ", result.Issues));
    }

    private static void AssertRedFlag(PatchProposalGovernedOperationStatusMappingResult result, string redFlag)
    {
        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.RedFlags.Contains(redFlag, StringComparer.OrdinalIgnoreCase), string.Join(", ", result.RedFlags));
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
