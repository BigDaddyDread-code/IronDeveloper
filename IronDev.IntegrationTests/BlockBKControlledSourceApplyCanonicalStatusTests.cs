using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBKControlledSourceApplyCanonicalStatusTests
{
    [TestMethod]
    public void BlockBKSourceApply_Blocked_MapsToBlockedSourceApplyStatus()
    {
        var result = Map(BlockedMissingRequestInput());

        AssertValid(result);
        Assert.AreEqual("SourceApply", result.Status.OperationKind);
        Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
        AssertContains(result.Status.BlockedReasons, "Accepted source-apply request is missing.");
        AssertContains(result.Status.MissingEvidence, "accepted-source-apply-request:source-apply-123");
    }

    [TestMethod]
    public void BlockBKSourceApply_PatchProposalWithoutAcceptedApplyRequest_MapsToBlocked()
    {
        var result = Map(BlockedMissingRequestInput() with
        {
            EvidenceRefs = ["patch-proposal:proposal-123", "patch-hash:patchhash-abc"]
        });

        AssertValid(result);
        Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanSourceApply);
    }

    [TestMethod]
    public void BlockBKSourceApply_DryRunOnly_MapsToBlocked()
    {
        var result = Map(Blocked("Dry-run exists but accepted source-apply request is missing.", "accepted-source-apply-request:source-apply-123") with
        {
            EvidenceRefs = ["dry-run:dryrun-123"]
        });

        AssertValid(result);
        Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
    }

    [TestMethod]
    public void BlockBKSourceApply_WrongPatchHash_MapsToBlocked()
    {
        var result = Map(Blocked("Apply request patch hash does not match current patch.", "patch-hash:patchhash-abc"));

        AssertValid(result);
        Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
    }

    [TestMethod]
    public void BlockBKSourceApply_WrongRepo_MapsToBlocked()
    {
        var result = Map(Blocked("Apply request repo does not match current repo.", "repo:BigDaddyDread-code/IronDeveloper"));

        AssertValid(result);
        Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
    }

    [TestMethod]
    public void BlockBKSourceApply_WrongBranch_MapsToBlocked()
    {
        var result = Map(Blocked("Apply request branch does not match current branch.", "branch:main"));

        AssertValid(result);
        Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
    }

    [TestMethod]
    public void BlockBKSourceApply_DirtyWorktree_MapsToBlocked()
    {
        var result = Map(Blocked("Worktree is dirty.", "worktree-state:clean"));

        AssertValid(result);
        Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
        AssertContains(result.Status.NextSafeActions, "inspect dirty worktree state");
    }

    [TestMethod]
    public void BlockBKSourceApply_ForbiddenPath_MapsToBlocked()
    {
        var result = Map(Blocked("Patch touches forbidden files.", "path-boundary:evidence"));

        AssertValid(result);
        Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
    }

    [TestMethod]
    public void BlockBKSourceApply_MissingPolicySatisfaction_MapsToBlocked()
    {
        var result = Map(Blocked("Policy satisfaction is missing.", "policy-satisfaction:source-apply"));

        AssertValid(result);
        Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
        AssertContains(result.Status.NextSafeActions, "request policy satisfaction for source apply");
    }

    [TestMethod]
    public void BlockBKSourceApply_MissingRollbackSupport_MapsToBlockedWhereRequired()
    {
        var result = Map(Blocked("Rollback support is missing.", "rollback-plan:source-apply-123"));

        AssertValid(result);
        Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
        AssertContains(result.Status.NextSafeActions, "prepare rollback support");
    }

    [TestMethod]
    public void BlockBKSourceApply_ExpiredApplyRequest_MapsToExpired()
    {
        var result = Map(ExpiredInput());

        AssertValid(result);
        Assert.AreEqual(GovernedOperationState.Expired, result.Status.State);
        Assert.IsNotNull(result.Status.ExpiresAtUtc);
        AssertContains(result.Status.ForbiddenActions, "do not reuse old apply request");
    }

    [TestMethod]
    public void BlockBKSourceApply_ExpiredByStaleAuthority_MapsToExpired()
    {
        var result = Map(ExpiredInput() with
        {
            ExpiresAtUtc = null,
            BlockedReasons = ["Source state moved and validation became stale."]
        });

        AssertValid(result);
        Assert.AreEqual(GovernedOperationState.Expired, result.Status.State);
        Assert.IsTrue(result.Status.BlockedReasons.Any(reason => reason.Contains("expired", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BlockBKSourceApply_ExpiredWithoutExpiryOrStaleEvidence_IsInvalid()
    {
        var result = Map(ExpiredInput() with
        {
            ExpiresAtUtc = null,
            BlockedReasons = ["Base branch moved."]
        });

        AssertInvalid(result, "SourceApplyExpiryEvidenceRequired");
        AssertInvalid(result, "ExpiredStatusRequiresExpiryEvidence");
    }

    [TestMethod]
    public void BlockBKSourceApply_Eligible_MapsToEligibleSourceApplyStatus()
    {
        var result = Map(EligibleInput());

        AssertValid(result);
        Assert.AreEqual("SourceApply", result.Status.OperationKind);
        Assert.AreEqual(GovernedOperationState.Eligible, result.Status.State);
        CollectionAssert.Contains(result.Status.EvidenceRefs.ToArray(), "accepted-source-apply-request:source-apply-123");
        CollectionAssert.Contains(result.Status.EvidenceRefs.ToArray(), "patch-hash:patchhash-abc");
    }

    [TestMethod]
    public void BlockBKSourceApply_Eligible_DoesNotGrantExecutionAuthority()
    {
        var result = Map(EligibleInput());

        AssertValid(result);
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanExecute);
    }

    [TestMethod]
    public void BlockBKSourceApply_Eligible_DoesNotMutateSource()
    {
        var result = Map(EligibleInput());

        AssertValid(result);
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanMutateSource);
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanMutate);
    }

    [TestMethod]
    public void BlockBKSourceApply_Eligible_BoundaryCanSourceApplyFalse()
    {
        var result = Map(EligibleInput());

        AssertValid(result);
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanSourceApply);
    }

    [TestMethod]
    public void BlockBKSourceApply_Eligible_IncludesNextSafeActionToRequestControlledExecution()
    {
        var status = Map(EligibleInput()).Status;

        AssertContains(status.NextSafeActions, "request controlled source apply execution for patch hash patchhash-abc");
    }

    [TestMethod]
    public void BlockBKSourceApply_Eligible_CannotCarryBlockedReasons()
    {
        var result = Map(EligibleInput() with { BlockedReasons = ["Policy satisfaction is missing."] });

        AssertInvalid(result, "EligibleSourceApplyCannotCarryBlockedReasons");
    }

    [TestMethod]
    public void BlockBKSourceApply_Eligible_CannotCarryMissingEvidence()
    {
        var result = Map(EligibleInput() with { MissingEvidence = ["policy-satisfaction:source-apply"] });

        AssertInvalid(result, "EligibleSourceApplyCannotCarryMissingEvidence");
    }

    [TestMethod]
    public void BlockBKSourceApply_Eligible_RequiresAcceptedSourceApplyRequestRef()
    {
        var result = Map(EligibleInput() with
        {
            EvidenceRefs = WithoutEvidencePrefix(EligibleInput(), "accepted-source-apply-request")
        });

        AssertInvalid(result, "EligibleSourceApplyAcceptedRequestRequired");
    }

    [TestMethod]
    public void BlockBKSourceApply_Eligible_RequiresPolicySatisfactionRef()
    {
        var result = Map(EligibleInput() with
        {
            EvidenceRefs = WithoutEvidencePrefix(EligibleInput(), "policy-satisfaction")
        });

        AssertInvalid(result, "EligibleSourceApplyPolicySatisfactionRequired");
    }

    [TestMethod]
    public void BlockBKSourceApply_Eligible_RequiresDryRunRef()
    {
        var result = Map(EligibleInput() with
        {
            EvidenceRefs = WithoutEvidencePrefix(EligibleInput(), "dry-run")
        });

        AssertInvalid(result, "EligibleSourceApplyDryRunRequired");
    }

    [TestMethod]
    public void BlockBKSourceApply_Eligible_RequiresPatchArtifactRef()
    {
        var result = Map(EligibleInput() with
        {
            EvidenceRefs = WithoutEvidencePrefix(EligibleInput(), "patch-artifact")
        });

        AssertInvalid(result, "EligibleSourceApplyPatchArtifactRequired");
    }

    [TestMethod]
    public void BlockBKSourceApply_Eligible_RequiresRollbackSupportRef()
    {
        var result = Map(EligibleInput() with
        {
            EvidenceRefs = WithoutEvidencePrefix(EligibleInput(), "rollback-plan")
        });

        AssertInvalid(result, "EligibleSourceApplyRollbackSupportRequired");
    }

    [TestMethod]
    public void BlockBKSourceApply_Eligible_RequiresWorktreeStateRef()
    {
        var result = Map(EligibleInput() with
        {
            EvidenceRefs = WithoutEvidencePrefix(EligibleInput(), "worktree-state")
        });

        AssertInvalid(result, "EligibleSourceApplyWorktreeStateRequired");
    }

    [TestMethod]
    public void BlockBKSourceApply_Eligible_WithOnlyIdentityRefsIsInvalid()
    {
        var result = Map(EligibleInput() with { EvidenceRefs = [] });

        AssertInvalid(result, "EligibleSourceApplyAcceptedRequestRequired");
        AssertInvalid(result, "EligibleSourceApplyPolicySatisfactionRequired");
        AssertInvalid(result, "EligibleSourceApplyDryRunRequired");
        AssertInvalid(result, "EligibleSourceApplyPatchArtifactRequired");
        AssertInvalid(result, "EligibleSourceApplyRollbackSupportRequired");
        AssertInvalid(result, "EligibleSourceApplyWorktreeStateRequired");
    }

    [TestMethod]
    public void BlockBKSourceApply_Running_MapsToRunningAndDoesNotImplySuccess()
    {
        var result = Map(RunningInput());

        AssertValid(result);
        Assert.AreEqual(GovernedOperationState.Running, result.Status.State);
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanCommit);
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanContinueWorkflow);
        AssertContains(result.Status.ForbiddenActions, "do not continue workflow from running status");
    }

    [TestMethod]
    public void BlockBKSourceApply_Running_CannotCarryBlockedReasons()
    {
        var result = Map(RunningInput() with { BlockedReasons = ["Patch artifact is missing."] });

        AssertInvalid(result, "RunningSourceApplyCannotCarryBlockedReasons");
    }

    [TestMethod]
    public void BlockBKSourceApply_Completed_MapsToCompletedAndIncludesSourceApplyReceiptRef()
    {
        var result = Map(CompletedInput());

        AssertValid(result);
        Assert.AreEqual(GovernedOperationState.Completed, result.Status.State);
        CollectionAssert.Contains(result.Status.ReceiptRefs.ToArray(), "source-apply-receipt:receipt-123");
    }

    [TestMethod]
    public void BlockBKSourceApply_Completed_RequiresReceiptRef()
    {
        var result = Map(CompletedInput() with { ReceiptRefs = [] });

        AssertInvalid(result, "SourceApplyCompletedReceiptRequired");
        AssertInvalid(result, "CompletedStatusRequiresReceiptReference");
        AssertInvalid(result, "SourceApplyCompletedReceiptRefRequired");
    }

    [TestMethod]
    public void BlockBKSourceApply_Completed_RequiresSourceApplyReceiptRef()
    {
        var result = Map(CompletedInput() with { ReceiptRefs = ["source-apply-failure-receipt:failure-123"] });

        AssertInvalid(result, "SourceApplyCompletedReceiptRefRequired");
    }

    [TestMethod]
    public void BlockBKSourceApply_Completed_CannotCarryBlockedReasons()
    {
        var result = Map(CompletedInput() with { BlockedReasons = ["Post-state mismatch."] });

        AssertInvalid(result, "CompletedSourceApplyCannotCarryBlockedReasons");
    }

    [TestMethod]
    public void BlockBKSourceApply_Completed_DoesNotAuthorizeCommit()
    {
        var result = Map(CompletedInput());

        AssertValid(result);
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanCommit);
        AssertContains(result.Status.ForbiddenActions, "do not treat source apply completion as commit authority");
    }

    [TestMethod]
    public void BlockBKSourceApply_Completed_DoesNotAuthorizePush()
    {
        var result = Map(CompletedInput());

        AssertValid(result);
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanPush);
        AssertContains(result.Status.ForbiddenActions, "do not treat source apply completion as push authority");
    }

    [TestMethod]
    public void BlockBKSourceApply_Completed_DoesNotAuthorizePrCreation()
    {
        var result = Map(CompletedInput());

        AssertValid(result);
        AssertContains(result.Status.ForbiddenActions, "do not treat source apply completion as PR authority");
    }

    [TestMethod]
    public void BlockBKSourceApply_Completed_DoesNotAuthorizeRollbackExecution()
    {
        var result = Map(CompletedInput());

        AssertValid(result);
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanRollback);
        AssertContains(result.Status.ForbiddenActions, "do not treat source apply receipt as rollback execution authority");
    }

    [TestMethod]
    public void BlockBKSourceApply_Completed_DoesNotAuthorizeWorkflowContinuation()
    {
        var result = Map(CompletedInput());

        AssertValid(result);
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanContinueWorkflow);
        AssertContains(result.Status.ForbiddenActions, "do not treat source apply completion as workflow continuation authority");
    }

    [TestMethod]
    public void BlockBKSourceApply_Failed_MapsToFailedAndDoesNotAuthorizeRetry()
    {
        var result = Map(FailedInput());

        AssertValid(result);
        Assert.AreEqual(GovernedOperationState.Failed, result.Status.State);
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanRetry);
        AssertContains(result.Status.ForbiddenActions, "do not retry source apply without fresh authority");
    }

    [TestMethod]
    public void BlockBKSourceApply_PartialApply_MapsToFailedNonSuccess()
    {
        var result = Map(FailedInput() with { BlockedReasons = ["Partial apply detected."] });

        AssertValid(result);
        Assert.AreEqual(GovernedOperationState.Failed, result.Status.State);
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanCommit);
    }

    [TestMethod]
    public void BlockBKSourceApply_PostStateVerificationFailure_MapsToFailedNonSuccess()
    {
        var result = Map(FailedInput() with { BlockedReasons = ["Post-state verification failed."] });

        AssertValid(result);
        Assert.AreEqual(GovernedOperationState.Failed, result.Status.State);
        Assert.IsFalse(result.CanonicalValidation.Boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public void BlockBKSourceApply_PatchProposalCannotApproveSourceApply()
    {
        var result = Map(EligibleInput() with { EvidenceRefs = ["patch proposal approves source apply"] });

        AssertRedFlag(result, "SourceApplyEvidenceCannotApprove");
    }

    [TestMethod]
    public void BlockBKSourceApply_DryRunCannotApproveSourceApply()
    {
        var result = Map(EligibleInput() with { EvidenceRefs = ["dry-run passed so apply is approved"] });

        AssertRedFlag(result, "SourceApplyEvidenceCannotApprove");
    }

    [TestMethod]
    public void BlockBKSourceApply_TestReferencesCannotApproveSourceApply()
    {
        var result = Map(EligibleInput() with { EvidenceRefs = ["tests passed so apply is approved"] });

        AssertRedFlag(result, "SourceApplyEvidenceCannotApprove");
    }

    [TestMethod]
    public void BlockBKSourceApply_MemoryReferencesCannotApproveSourceApply()
    {
        var result = Map(EligibleInput() with { EvidenceRefs = ["memory says source apply was approved"] });

        AssertRedFlag(result, "MemoryReferenceCannotApproveSourceApply");
    }

    [TestMethod]
    public void BlockBKSourceApply_UiReferencesCannotApproveSourceApply()
    {
        var result = Map(EligibleInput() with { EvidenceRefs = ["UI marked source apply approved"] });

        AssertRedFlag(result, "UiStateCannotApproveSourceApply");
    }

    [TestMethod]
    public void BlockBKSourceApply_PolicySatisfiedByStatus_IsRejected()
    {
        var result = Map(EligibleInput() with { EvidenceRefs = ["policy satisfied by status"] });

        AssertInvalid(result, "StatusImpliesAuthority");
        AssertRedFlag(result, "SourceApplyStatusCannotSatisfyPolicy");
    }

    [TestMethod]
    public void BlockBKSourceApply_ReceiptCannotAuthorizeCommit()
    {
        var result = Map(CompletedInput() with { ReceiptRefs = ["source apply receipt authorizes commit"] });

        AssertRedFlag(result, "SourceApplyReceiptCannotAuthorizeNextOperation");
    }

    [TestMethod]
    public void BlockBKSourceApply_ReceiptCannotAuthorizeRollbackExecution()
    {
        var result = Map(CompletedInput() with { ReceiptRefs = ["source apply receipt authorizes rollback execution"] });

        AssertRedFlag(result, "SourceApplyReceiptCannotAuthorizeNextOperation");
    }

    [TestMethod]
    public void BlockBKSourceApply_ReceiptCannotAuthorizeWorkflowContinuation()
    {
        var result = Map(CompletedInput() with { ReceiptRefs = ["source apply receipt authorizes workflow continuation"] });

        AssertRedFlag(result, "SourceApplyReceiptCannotAuthorizeNextOperation");
    }

    [TestMethod]
    public void BlockBKSourceApply_OldApplyRequestCannotRefreshCurrentAuthority()
    {
        var result = Map(ExpiredInput() with { EvidenceRefs = ["old apply request refreshes current authority"] });

        AssertRedFlag(result, "OldSourceApplyRequestCannotRefreshAuthority");
    }

    [TestMethod]
    public void BlockBKSourceApply_NextSafeActionsRemainGuidanceOnly()
    {
        foreach (var input in new[]
        {
            BlockedMissingRequestInput(),
            EligibleInput(),
            RunningInput(),
            CompletedInput(),
            FailedInput(),
            ExpiredInput()
        })
        {
            var result = Map(input);

            AssertValid(result);
            Assert.IsTrue(result.Status.NextSafeActions.All(IsGuidance), string.Join(", ", result.Status.NextSafeActions));
            Assert.IsFalse(result.CanonicalValidation.Boundary.CanExecute);
        }
    }

    [TestMethod]
    public void BlockBKSourceApply_MapperValidatesOutputThroughGovernedOperationStatusValidator()
    {
        var result = Map(EligibleInput() with { OperationId = string.Empty });

        AssertInvalid(result, "OperationIdRequired");
        Assert.IsFalse(result.CanonicalValidation.IsValid);
    }

    [TestMethod]
    public void BlockBKSourceApply_StaticBoundary_DoesNotTouchExecutorProviderMutationCode()
    {
        var mapper = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "IronDev.Core",
            "Governance",
            "ControlledSourceApplyGovernedOperationStatusMapper.cs"));

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
    public void BlockBKSourceApply_Receipt_RecordsBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Docs",
            "receipts",
            "BK_CONTROLLED_SOURCE_APPLY_CANONICAL_STATUS.md"));

        StringAssert.Contains(doc, "This slice maps controlled source apply outcomes into canonical GovernedOperationStatus.");
        StringAssert.Contains(doc, "Source apply status cannot approve.");
        StringAssert.Contains(doc, "Source apply status cannot satisfy policy.");
        StringAssert.Contains(doc, "Source apply status cannot execute.");
        StringAssert.Contains(doc, "Source apply status cannot mutate source.");
        StringAssert.Contains(doc, "Eligible status is explanation, not execution authority.");
        StringAssert.Contains(doc, "Eligible status requires refs that explain eligibility.");
        StringAssert.Contains(doc, "Eligible status requires a policy-satisfaction ref as explanatory evidence.");
        StringAssert.Contains(doc, "Completed source apply is not commit authority.");
        StringAssert.Contains(doc, "Completed source apply is not push authority.");
        StringAssert.Contains(doc, "Completed source apply is not PR authority.");
        StringAssert.Contains(doc, "Completed status requires a source-apply-receipt reference.");
        StringAssert.Contains(doc, "A source apply receipt is not rollback execution authority.");
        StringAssert.Contains(doc, "Source apply status can show the loaded gate. It cannot pull the lever.");
    }

    private static ControlledSourceApplyGovernedOperationStatusMappingResult Map(ControlledSourceApplyStatusInput input) =>
        ControlledSourceApplyGovernedOperationStatusMapper.Map(input);

    private static ControlledSourceApplyStatusInput BaseInput(ControlledSourceApplyStatusKind statusKind) =>
        new()
        {
            OperationId = "source-apply-status-001",
            SourceApplyId = "source-apply-123",
            Subject = "repo:BigDaddyDread-code/IronDeveloper path:IronDev.Core/Governance",
            RepoId = "BigDaddyDread-code/IronDeveloper",
            Branch = "main",
            PatchHash = "patchhash-abc",
            StatusKind = statusKind,
            EvidenceRefs = [],
            ReceiptRefs = [],
            BlockedReasons = [],
            MissingEvidence = [],
            ForbiddenActions = [],
            ObservedAtUtc = DateTimeOffset.Parse("2026-06-21T02:00:00Z")
        };

    private static ControlledSourceApplyStatusInput BlockedMissingRequestInput() =>
        Blocked("Accepted source-apply request is missing.", "accepted-source-apply-request:source-apply-123");

    private static ControlledSourceApplyStatusInput Blocked(string reason, string missingEvidence) =>
        BaseInput(ControlledSourceApplyStatusKind.Blocked) with
        {
            EvidenceRefs = ["patch-proposal:proposal-123", "patch-hash:patchhash-abc"],
            BlockedReasons = [reason],
            MissingEvidence = [missingEvidence]
        };

    private static ControlledSourceApplyStatusInput EligibleInput() =>
        BaseInput(ControlledSourceApplyStatusKind.Eligible) with
        {
            EvidenceRefs =
            [
                "accepted-source-apply-request:source-apply-123",
                "policy-satisfaction:policy-123",
                "dry-run:dryrun-123",
                "patch-artifact:artifact-123",
                "rollback-plan:rollback-123",
                "worktree-state:clean"
            ]
        };

    private static ControlledSourceApplyStatusInput RunningInput() =>
        BaseInput(ControlledSourceApplyStatusKind.Running) with
        {
            EvidenceRefs =
            [
                "accepted-source-apply-request:source-apply-123",
                "executor-run:run-123"
            ]
        };

    private static ControlledSourceApplyStatusInput CompletedInput() =>
        BaseInput(ControlledSourceApplyStatusKind.Completed) with
        {
            EvidenceRefs =
            [
                "accepted-source-apply-request:source-apply-123",
                "post-state:post-123"
            ],
            ReceiptRefs = ["source-apply-receipt:receipt-123"]
        };

    private static ControlledSourceApplyStatusInput FailedInput() =>
        BaseInput(ControlledSourceApplyStatusKind.Failed) with
        {
            BlockedReasons = ["Source apply failed."],
            ReceiptRefs = ["source-apply-failure-receipt:failure-123"]
        };

    private static ControlledSourceApplyStatusInput ExpiredInput() =>
        BaseInput(ControlledSourceApplyStatusKind.Expired) with
        {
            BlockedReasons = ["Accepted source apply request expired."],
            ExpiresAtUtc = DateTimeOffset.Parse("2026-06-21T03:00:00Z")
        };

    private static IReadOnlyList<string> WithoutEvidencePrefix(ControlledSourceApplyStatusInput input, string prefix) =>
        input.EvidenceRefs
            .Where(value => !value.StartsWith($"{prefix}:", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static void AssertValid(ControlledSourceApplyGovernedOperationStatusMappingResult result)
    {
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues.Concat(result.RedFlags).Concat(result.CanonicalValidation.AmberFlags)));
        Assert.IsTrue(result.CanonicalValidation.IsValid, string.Join(", ", result.CanonicalValidation.Issues.Concat(result.CanonicalValidation.RedFlags)));
    }

    private static void AssertInvalid(ControlledSourceApplyGovernedOperationStatusMappingResult result, string issue)
    {
        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Contains(issue, StringComparer.OrdinalIgnoreCase), string.Join(", ", result.Issues));
    }

    private static void AssertRedFlag(ControlledSourceApplyGovernedOperationStatusMappingResult result, string redFlag)
    {
        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.RedFlags.Contains(redFlag, StringComparer.OrdinalIgnoreCase), string.Join(", ", result.RedFlags));
    }

    private static void AssertContains(IReadOnlyList<string> values, string expected) =>
        Assert.IsTrue(values.Contains(expected, StringComparer.OrdinalIgnoreCase), string.Join(", ", values));

    private static bool IsGuidance(string action)
    {
        var normalized = action.Trim();
        return normalized.StartsWith("ask ", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("collect ", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("create ", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("inspect ", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("observe ", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("open ", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("package ", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("prepare ", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("request ", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("review ", StringComparison.OrdinalIgnoreCase);
    }

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
