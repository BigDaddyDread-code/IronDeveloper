using IronDev.Core.Governance;
using IronDev.Core.Governance.InterruptedRunRecovery;
using IronDev.Core.Governance.RepoStateFreshness;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockDogfoodBypassPressureCleanupTests
{
    private const string RepoId = "BigDaddyDread-code/IronDeveloper";
    private const string Branch = "dogfood/ask-before-mutation-boundary-lane";
    private const string RunId = "run-pr23";
    private const string PatchHash = "sha256:pr25bypasspressure";
    private const string FileScope = "Docs/receipts/PR23_ASK_BEFORE_MUTATION_DOGFOOD_LANE.md";
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-22T14:00:00Z");

    [TestMethod]
    public void BypassCleanup_BlockedSourceApplyHasPlainReason()
    {
        var message = FormatSourceApply();

        AssertContains(message.PlainReasons, "Source apply is blocked because no accepted source-apply request or bounded SourceApply authority exists");
        AssertContains(message.PlainReasons, RepoId);
        AssertContains(message.PlainReasons, Branch);
    }

    [TestMethod]
    public void BypassCleanup_BlockedSourceApplyNamesMissingAuthority()
    {
        var message = FormatSourceApply();

        AssertContains(message.PlainMissingEvidence, "Missing accepted source-apply request for this exact patch");
        AssertContains(message.PlainMissingEvidence, "Missing bounded SourceApply authority");
        AssertContains(message.PlainMissingEvidence, PatchHash);
        AssertContains(message.PlainMissingEvidence, FileScope);
    }

    [TestMethod]
    public void BypassCleanup_NextSafeActionNamesRepoBranchRunPatchAndScope()
    {
        var message = FormatSourceApply();
        var action = string.Join("\n", message.PlainNextSafeActions);

        StringAssert.Contains(action, RepoId);
        StringAssert.Contains(action, Branch);
        StringAssert.Contains(action, RunId);
        StringAssert.Contains(action, PatchHash);
        StringAssert.Contains(action, FileScope);
    }

    [TestMethod]
    public void BypassCleanup_NextSafeActionDoesNotSayApplyNow()
    {
        var message = FormatSourceApply();
        var action = string.Join("\n", message.PlainNextSafeActions);

        Assert.DoesNotContain("apply now", action, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("continue workflow", action, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("commit now", action, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("push now", action, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("merge now", action, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deploy now", action, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void BypassCleanup_ForbiddenActionsRemainVisible()
    {
        var message = FormatSourceApply();

        AssertContains(message.PlainForbiddenActions, "do not apply source without explicit source-apply authority");
        AssertContains(message.PlainForbiddenActions, "do not treat patch package as source apply authority");
        AssertContains(message.PlainForbiddenActions, "do not treat validation as approval");
        AssertContains(message.PlainForbiddenActions, "do not treat freshness as authority");
        AssertContains(message.PlainForbiddenActions, "do not continue workflow from status, receipt, memory, or UI text");
    }

    [TestMethod]
    public void BypassCleanup_DeduplicatesRepeatedForbiddenActions()
    {
        var message = FormatSourceApply();

        Assert.AreEqual(
            message.PlainForbiddenActions.Count,
            message.PlainForbiddenActions.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [TestMethod]
    public void BypassCleanup_DeduplicatesRepeatedNextSafeActions()
    {
        var message = FormatSourceApply();

        Assert.AreEqual(
            message.PlainNextSafeActions.Count,
            message.PlainNextSafeActions.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [TestMethod]
    public void BypassCleanup_DoesNotRemoveCanonicalEvidenceRefs()
    {
        var status = SourceApplyStatus();
        var message = GovernedStatusUserMessageFormatter.Format(status);

        CollectionAssert.AreEquivalent(status.EvidenceRefs.ToArray(), message.EvidenceRefs.ToArray());
    }

    [TestMethod]
    public void BypassCleanup_DoesNotRemoveReceiptRefs()
    {
        var status = SourceApplyStatus();
        var message = GovernedStatusUserMessageFormatter.Format(status);

        CollectionAssert.AreEquivalent(status.ReceiptRefs.ToArray(), message.ReceiptRefs.ToArray());
    }

    [TestMethod]
    public void BypassCleanup_FreshnessMessageSaysFreshnessIsNotAuthority()
    {
        var message = FormatSourceApply();

        AssertContains(message.AuthorityWarnings, "Freshness evidence says the repo state was checked; freshness is not authority.");
    }

    [TestMethod]
    public void BypassCleanup_ValidationMessageSaysValidationIsNotApproval()
    {
        var message = FormatSourceApply();

        AssertContains(message.AuthorityWarnings, "Validation evidence is not approval.");
    }

    [TestMethod]
    public void BypassCleanup_PatchPackageMessageSaysPatchIsNotApplyAuthority()
    {
        var message = FormatSourceApply();

        AssertContains(message.AuthorityWarnings, "Patch package evidence is not source apply authority.");
    }

    [TestMethod]
    public void BypassCleanup_DraftPrMessageSaysDraftIsNotReadyForReview()
    {
        var message = GovernedStatusUserMessageFormatter.Format(SourceApplyStatus() with
        {
            Subject = Subject() + " draft-pr:true",
            EvidenceRefs = [.. SourceApplyStatus().EvidenceRefs, "draft-pull-request:pr24"]
        });

        AssertContains(message.AuthorityWarnings, "Draft PR evidence is not ready-for-review authority.");
        AssertContains(message.PlainForbiddenActions, "do not treat draft PR as ready-for-review authority");
    }

    [TestMethod]
    public void BypassCleanup_PrUrlMessageSaysPrUrlIsNotReleaseCandidate()
    {
        var message = GovernedStatusUserMessageFormatter.Format(SourceApplyStatus() with
        {
            EvidenceRefs = [.. SourceApplyStatus().EvidenceRefs, "pull-request-url:https://example.invalid/pr/24"]
        });

        AssertContains(message.AuthorityWarnings, "A PR URL is not a release candidate reference.");
        AssertContains(message.PlainForbiddenActions, "do not treat PR URL as release candidate ref");
    }

    [TestMethod]
    public void BypassCleanup_RecoveryMessageSaysDiagnosisDoesNotResume()
    {
        var message = GovernedStatusUserMessageFormatter.Format(SourceApplyStatus() with
        {
            Subject = Subject() + " recovery:true",
            EvidenceRefs = [.. SourceApplyStatus().EvidenceRefs, "interrupted-run:pr20"]
        });

        AssertContains(message.AuthorityWarnings, "Recovery diagnosis is read-only and does not resume a run.");
    }

    [TestMethod]
    public void BypassCleanup_RollbackMessageSaysRollbackPlanIsNotExecution()
    {
        var message = GovernedStatusUserMessageFormatter.Format(SourceApplyStatus() with
        {
            EvidenceRefs = [.. SourceApplyStatus().EvidenceRefs, "rollback-plan:pr25"]
        });

        AssertContains(message.AuthorityWarnings, "Rollback plan evidence is not rollback execution.");
    }

    [TestMethod]
    public void BypassCleanup_CommandDefaultsRemainReadOnly()
    {
        var message = FormatSourceApply();

        Assert.IsFalse(message.CanApprove);
        Assert.IsFalse(message.CanSatisfyPolicy);
        Assert.IsFalse(message.CanExecute);
        Assert.IsFalse(message.CanMutateSource);
        Assert.IsFalse(message.CanContinueWorkflow);
    }

    [TestMethod]
    public void BypassCleanup_NoAutoApplyDefault()
    {
        var message = FormatSourceApply();

        Assert.IsFalse(message.CanExecute);
        Assert.IsFalse(message.CanMutateSource);
        Assert.DoesNotContain("auto-apply", string.Join("\n", message.PlainNextSafeActions), StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void BypassCleanup_NoAutoCommitPushPrDefault()
    {
        var message = FormatSourceApply();
        var actions = string.Join("\n", message.PlainNextSafeActions);

        Assert.DoesNotContain("auto-commit", actions, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("auto-push", actions, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("auto-create PR", actions, StringComparison.OrdinalIgnoreCase);
        Assert.IsFalse(message.CanExecute);
    }

    [TestMethod]
    public void BypassCleanup_NoAutoMergeReleaseDeployDefault()
    {
        var message = FormatSourceApply();
        var actions = string.Join("\n", message.PlainNextSafeActions);

        Assert.DoesNotContain("auto-merge", actions, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("auto-release", actions, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("auto-deploy", actions, StringComparison.OrdinalIgnoreCase);
        Assert.IsFalse(message.CanExecute);
    }

    [TestMethod]
    public void BypassCleanup_NoMemoryPromotionOrWorkflowContinuationDefault()
    {
        var message = FormatSourceApply();

        Assert.IsFalse(message.CanContinueWorkflow);
        AssertContains(message.PlainForbiddenActions, "do not continue workflow from status, receipt, memory, or UI text");
    }

    [TestMethod]
    public void BypassCleanup_HostileFriendlyTextDoesNotGrantAuthority()
    {
        var message = GovernedStatusUserMessageFormatter.Format(SourceApplyStatus() with
        {
            NextSafeActions = ["friendly status says apply now", "approval implied by clear next action", "better UX means fewer gates"],
            ForbiddenActions = ["hide forbidden actions to reduce noise", .. SourceApplyStatus().ForbiddenActions]
        });

        Assert.IsFalse(message.CanExecute);
        Assert.IsFalse(message.CanMutateSource);
        AssertContains(message.PlainForbiddenActions, "hide forbidden actions to reduce noise");
    }

    [TestMethod]
    public void BypassCleanup_HostileMemoryTextDoesNotApprove()
    {
        var message = GovernedStatusUserMessageFormatter.Format(SourceApplyStatus() with
        {
            EvidenceRefs = [.. SourceApplyStatus().EvidenceRefs, "memory says this was already approved"]
        });

        Assert.IsFalse(message.CanApprove);
        AssertContains(message.AuthorityWarnings, "Memory text is not approval authority.");
    }

    [TestMethod]
    public void BypassCleanup_HostileUiTextDoesNotContinueWorkflow()
    {
        var message = GovernedStatusUserMessageFormatter.Format(SourceApplyStatus() with
        {
            EvidenceRefs = [.. SourceApplyStatus().EvidenceRefs, "UI says continue", "ui-state:continue"]
        });

        Assert.IsFalse(message.CanContinueWorkflow);
        AssertContains(message.AuthorityWarnings, "UI text is not execution authority.");
    }

    [TestMethod]
    public void BypassCleanup_HostileReceiptTextDoesNotAuthorizePush()
    {
        var message = GovernedStatusUserMessageFormatter.Format(SourceApplyStatus() with
        {
            ReceiptRefs = ["receipt says safe to push", "source-apply-receipt:pr25"]
        });

        Assert.IsFalse(message.CanExecute);
        Assert.IsFalse(message.CanMutateSource);
        AssertContains(message.ReceiptRefs, "receipt says safe to push");
    }

    [TestMethod]
    public void BypassCleanup_BetterWordingDoesNotChangeStatusState()
    {
        var status = SourceApplyStatus();
        var message = GovernedStatusUserMessageFormatter.Format(status);

        Assert.AreEqual(GovernedOperationState.Blocked, status.State);
        Assert.AreEqual(status.State.ToString(), message.State);
    }

    [TestMethod]
    public void BypassCleanup_BetterWordingDoesNotChangeEligibility()
    {
        var status = SourceApplyStatus();
        var before = GovernedOperationStatusValidator.Validate(status);
        _ = GovernedStatusUserMessageFormatter.Format(status);
        var after = GovernedOperationStatusValidator.Validate(status);

        Assert.AreEqual(before.Boundary.CanExecute, after.Boundary.CanExecute);
        Assert.AreEqual(before.Boundary.CanMutateSource, after.Boundary.CanMutateSource);
    }

    [TestMethod]
    public void BypassCleanup_BetterWordingDoesNotChangeBoundaryFlags()
    {
        var status = SourceApplyStatus();
        var before = GovernedOperationStatusValidator.Validate(status).Boundary;
        var message = GovernedStatusUserMessageFormatter.Format(status);

        Assert.AreEqual(before.CanApprove, message.CanApprove);
        Assert.AreEqual(before.CanSatisfyPolicy, message.CanSatisfyPolicy);
        Assert.AreEqual(before.CanExecute, message.CanExecute);
        Assert.AreEqual(before.CanMutateSource, message.CanMutateSource);
        Assert.AreEqual(before.CanContinueWorkflow, message.CanContinueWorkflow);
    }

    [TestMethod]
    public void BypassCleanup_DogfoodFindingsAreRecorded()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "dogfood", "PR25_BYPASS_PRESSURE_FINDINGS.md"));

        StringAssert.Contains(doc, "no-approval lane");
        StringAssert.Contains(doc, "ask-before-mutation");
        StringAssert.Contains(doc, "freshness is not authority");
        StringAssert.Contains(doc, "Draft PR evidence is not ready-for-review authority.");
    }

    [TestMethod]
    public void StaticMutationSurfaceScan_NoExecutorProviderGitUiMemoryReleaseDeployAdded()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "GovernedStatusUserMessageFormatter.cs"));

        foreach (var forbidden in new[]
        {
            "Process.Start",
            "ProcessStartInfo",
            "Run" + "ProcessAsync",
            "git ",
            "\"git\"",
            "gh",
            "HttpClient",
            "IControlled",
            "Executor.Execute",
            "Gateway",
            "Frontend",
            "UI.",
            "MemoryPromotion",
            "ReleaseExecutor",
            "DeploymentExecutor"
        })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [TestMethod]
    public void BypassCleanup_PR24BoundedAuthorityLaneStillStopsBeforeDownstreamAuthority()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "PR24_BOUNDED_AUTHORITY_DOGFOOD_LANE.md"));

        StringAssert.Contains(doc, "ready-for-review");
        StringAssert.Contains(doc, "merge");
        StringAssert.Contains(doc, "release");
        StringAssert.Contains(doc, "deployment");
        StringAssert.Contains(doc, "memory promotion");
        StringAssert.Contains(doc, "workflow continuation");
    }

    [TestMethod]
    public void BypassCleanup_PR23AskBeforeLaneStillBlocksSourceApplyWithoutAuthority()
    {
        var status = ControlledSourceApplyGovernedOperationStatusMapper.Map(new ControlledSourceApplyStatusInput
        {
            OperationId = "source-apply-status-pr25-regression",
            SourceApplyId = "source-apply-pr25",
            Subject = Subject(),
            RepoId = RepoId,
            Branch = Branch,
            PatchHash = PatchHash,
            StatusKind = ControlledSourceApplyStatusKind.Blocked,
            EvidenceRefs = ["patch-package:pr23"],
            ReceiptRefs = [],
            BlockedReasons = ["MissingExplicitSourceApplyAuthority"],
            MissingEvidence = ["bounded-authority-grant:SourceApply"],
            ForbiddenActions = ["do not apply source without explicit source-apply authority"],
            ObservedAtUtc = ObservedAtUtc
        });

        Assert.AreEqual(GovernedOperationState.Blocked, status.Status.State);
        Assert.IsFalse(status.CanonicalValidation.Boundary.CanSourceApply);
    }

    [TestMethod]
    public void BypassCleanup_PR22NoApprovalLaneStillProducesEvidenceOnly()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "PR22_NO_APPROVAL_DOGFOOD_LANE.md"));

        StringAssert.Contains(doc, "Useful evidence is not mutation permission.");
        StringAssert.Contains(doc, "Patch package is not source apply.");
        StringAssert.Contains(doc, "Status is not authority.");
    }

    [TestMethod]
    public void BypassCleanup_PR21FreshnessGuardRemainsExplanationOnly()
    {
        var result = RepoStateFreshnessGuard.Evaluate(new RepoStateFreshnessRequest
        {
            CheckId = "freshness-pr25",
            Repository = RepoId,
            RunId = RunId,
            OperationKind = RunAuthorityOperationKind.SourceApply,
            Expected = new()
            {
                BaseBranch = "main",
                BaseSha = "base-pr25",
                HeadBranch = Branch,
                HeadSha = "head-pr25",
                PatchHash = PatchHash,
                CommitHeadSha = null,
                RemoteBranch = null,
                RemoteSha = null,
                ValidationBaseSha = "base-pr25",
                ValidationHeadSha = "head-pr25",
                ValidationPatchHash = PatchHash,
                ValidationObservedAtUtc = ObservedAtUtc.AddMinutes(-5),
                ValidationExpiresAtUtc = ObservedAtUtc.AddMinutes(10)
            },
            Observed = new()
            {
                BaseBranch = "main",
                BaseSha = "base-pr25",
                HeadBranch = Branch,
                HeadSha = "head-pr25",
                WorktreeState = RepoWorktreeState.Clean,
                PatchApplicability = PatchApplicability.Applies,
                CommitHeadSha = null,
                RemoteBranch = null,
                RemoteSha = null,
                ObservedAtUtc = ObservedAtUtc
            },
            EvidenceRefs = ["patch-package:pr25"],
            ReceiptRefs = [],
            ObservedAtUtc = ObservedAtUtc
        });

        Assert.IsTrue(result.Boundary.CanExplainFreshness);
        Assert.IsFalse(result.Boundary.CanApplySource);
        Assert.IsFalse(result.Boundary.CanCommit);
        Assert.IsFalse(result.Boundary.CanPush);
    }

    [TestMethod]
    public void BypassCleanup_PR20RecoveryRemainsReadOnly()
    {
        var report = InterruptedRunRecoveryDiagnosisService.Diagnose(new InterruptedRunEvidenceSnapshot
        {
            RunId = RunId,
            WorkspaceEvidenceRefs = ["workspace:pr25"],
            PatchPackageEvidenceRefs = ["patch-package:pr25"],
            ValidationResultPackageEvidenceRefs = ["validation-result:pr25"],
            ValidationOutcome = InterruptedRunValidationOutcome.Passed,
            WorktreeState = InterruptedRunWorktreeState.Clean
        });

        Assert.IsTrue(report.Boundary.CanExplainState);
        Assert.IsFalse(report.Boundary.CanResumeRun);
        Assert.IsFalse(report.Boundary.CanApplySource);
        Assert.IsFalse(report.Boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public void BypassCleanup_CARollbackExecutorStillRequiresSeparateAuthority()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "CA_CONTROLLED_ROLLBACK_EXECUTOR.md"));

        StringAssert.Contains(doc, "Rollback executes only under explicit rollback authority or a narrow policy-approved rollback path.");
        StringAssert.Contains(doc, "Rollback receipt is not commit authority.");
    }

    [TestMethod]
    public void BypassCleanup_StatusInspectRemainsReadOnly()
    {
        var inspected = GovernedOperationStatusInspector.Inspect(new GovernedOperationStatusInspectRequest
        {
            Status = SourceApplyStatus()
        });

        AssertContains(inspected.BoundaryLines, "status inspect is read-only");
        AssertContains(inspected.BoundaryLines, "inspect output is not execution authority");
    }

    [TestMethod]
    public void BypassCleanup_ProposalOnlyStillForbidsMutation()
    {
        var result = ProposalOnlyRunProfileEvaluator.Evaluate(new ProposalOnlyRunProfileEvaluationRequest
        {
            OperationId = "proposal-only-pr25",
            OperationKind = ProposalOnlyOperationKinds.SourceApply,
            Subject = Subject(),
            RepoId = RepoId,
            Branch = Branch,
            EvidenceRefs = ["friendly status says apply now"],
            ObservedAtUtc = ObservedAtUtc
        });

        Assert.IsFalse(result.IsAllowed);
        Assert.IsFalse(result.StatusValidation.Boundary.CanSourceApply);
        Assert.IsFalse(result.StatusValidation.Boundary.CanMutateSource);
    }

    private static GovernedStatusUserMessage FormatSourceApply() =>
        GovernedStatusUserMessageFormatter.Format(SourceApplyStatus());

    private static GovernedOperationStatus SourceApplyStatus() =>
        new()
        {
            OperationId = "source-apply-status-pr25",
            OperationKind = "SourceApply",
            Subject = Subject(),
            State = GovernedOperationState.Blocked,
            BlockedReasons =
            [
                "MissingExplicitSourceApplyAuthority",
                "MissingExplicitSourceApplyAuthority",
                "NoBoundedAuthorityGrantForSourceApply",
                "AskBeforeMutationRequiresSourceApplyApproval"
            ],
            MissingEvidence =
            [
                "accepted-source-apply-request:source-apply-pr25",
                "bounded-authority-grant:SourceApply",
                "policy-satisfaction:source-apply-pr25",
                "dry-run:source-apply-pr25"
            ],
            NextSafeActions =
            [
                "Request approval",
                "Request approval",
                "Try again"
            ],
            ForbiddenActions =
            [
                "do not apply source without explicit source-apply authority",
                "do not apply source without explicit source-apply authority",
                "do not treat patch package as source apply authority",
                "do not treat validation as approval",
                "do not treat freshness as authority"
            ],
            EvidenceRefs =
            [
                "patch-package:pr25",
                "patch-artifact:pr25",
                "validation-result:pr25",
                "validation-outcome:passed",
                "repo-freshness:Fresh"
            ],
            ReceiptRefs =
            [
                "patch-package-receipt:pr25"
            ],
            ObservedAtUtc = ObservedAtUtc
        };

    private static string Subject() =>
        $"repo:{RepoId} branch:{Branch} run:{RunId} patch:{PatchHash} scope:{FileScope}";

    private static void AssertContains(IEnumerable<string> values, string expected) =>
        Assert.IsTrue(
            values.Any(value => value.Contains(expected, StringComparison.OrdinalIgnoreCase)),
            $"Expected '{expected}' in: {string.Join(" | ", values)}");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }
}
