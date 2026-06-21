using System.Reflection;
using IronDev.Core.Governance;
using IronDev.Core.Governance.RollbackStatus;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBZRollbackStatusMappingTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 6, 22, 3, 0, 0, TimeSpan.Zero);
    private const string Repository = "BigDaddyDread-code/IronDeveloper";
    private const string Branch = "rollback/rollback-status-mapping";
    private const string RunId = "run-bz-001";
    private const string PatchHash = "sha256:abcdef1234567890";
    private const string SourceApplyReceiptRef = "source-apply-receipt:apply-bz-001";

    [TestMethod]
    public void BlockBZ_RollbackUnavailable_MapsToBlocked()
    {
        var missing = RollbackStatusMapper.Map(ValidRequest() with { Availability = null });

        Assert.AreEqual(GovernedOperationState.Blocked, missing.Status.State);
        AssertContains(missing.Status.BlockedReasons, "RollbackAvailabilityEvidenceRequired");
        AssertContains(missing.Status.MissingEvidence, "rollback-availability");
        Assert.IsFalse(missing.IsRollbackExecutionAllowed);
        Assert.IsFalse(missing.IsRollbackExecuted);
        AssertValid(missing);

        var unavailable = RollbackStatusMapper.Map(ValidRequest() with
        {
            Availability = ValidAvailability() with
            {
                IsRollbackAvailable = false,
                AvailabilityReason = "source apply receipt did not record rollback support"
            }
        });

        Assert.AreEqual(GovernedOperationState.Blocked, unavailable.Status.State);
        AssertContains(unavailable.Status.BlockedReasons, "RollbackUnavailable");
        AssertContains(unavailable.Status.MissingEvidence, "available-rollback-support");
        Assert.IsFalse(unavailable.IsRollbackExecutionAllowed);
        Assert.IsFalse(unavailable.IsRollbackExecuted);
        AssertValid(unavailable);
    }

    [TestMethod]
    public void BlockBZ_RollbackPlanWithoutAuthority_MapsToBlocked()
    {
        var result = RollbackStatusMapper.Map(ValidRequest() with { Authority = null });

        Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
        AssertContains(result.Status.BlockedReasons, "RollbackAuthorityRequired");
        AssertContains(result.Status.MissingEvidence, "rollback-operation-authority");
        AssertContains(result.Status.ForbiddenActions, "rollback plan is not rollback execution");
        AssertContains(result.Status.ForbiddenActions, "rollback plan is not rollback authority");
        Assert.IsFalse(result.IsRollbackExecutionAllowed);
        Assert.IsFalse(result.IsRollbackExecuted);
        AssertValid(result);
    }

    [TestMethod]
    public void BlockBZ_AcceptedRollbackRequest_MapsToEligibleOnly()
    {
        var result = RollbackStatusMapper.Map(ValidRequest());

        Assert.AreEqual(GovernedOperationState.Eligible, result.Status.State);
        Assert.AreEqual(0, result.Status.BlockedReasons.Count);
        Assert.AreEqual(0, result.Status.MissingEvidence.Count);
        AssertContains(result.Status.NextSafeActions, "request controlled rollback executor review separately");
        AssertContains(result.Status.ForbiddenActions, "accepted rollback request is not rollback execution");
        AssertContains(result.Status.ForbiddenActions, "eligible rollback status is not rollback execution");
        Assert.IsTrue(result.IsRollbackExecutionAllowed);
        Assert.IsFalse(result.IsRollbackExecuted);
        AssertValid(result);
    }

    [TestMethod]
    public void BlockBZ_WrongApplyReceipt_MapsToBlocked()
    {
        var result = RollbackStatusMapper.Map(ValidRequest() with
        {
            ApplyReceipt = ValidApplyReceipt() with { ReceiptRef = "source-apply-receipt:other" }
        });

        Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
        AssertContains(result.Status.BlockedReasons, "RollbackApplyReceiptMismatch");
        AssertContains(result.Status.MissingEvidence, "matching-source-apply-receipt");
        AssertContains(result.Status.ForbiddenActions, "do not rollback wrong apply receipt");
        Assert.IsFalse(result.IsRollbackExecutionAllowed);
        AssertValid(result);
    }

    [TestMethod]
    public void BlockBZ_PartialRollbackRisk_MapsToBlocked()
    {
        foreach (var request in new[]
        {
            ValidRequest() with { Plan = ValidPlan() with { HasPartialRollbackRisk = true } },
            ValidRequest() with { Plan = ValidPlan() with { RequiresPartialRollback = true } }
        })
        {
            var result = RollbackStatusMapper.Map(request);

            Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State);
            AssertContains(result.Status.BlockedReasons, "PartialRollbackRisk");
            AssertContains(result.Status.NextSafeActions, "request explicit partial rollback authority in future slice");
            AssertContains(result.Status.ForbiddenActions, "do not execute partial rollback from general rollback status");
            Assert.IsFalse(result.IsRollbackExecutionAllowed);
            AssertValid(result);
        }
    }

    [TestMethod]
    public void BlockBZ_DirtyWorktree_MapsToBlocked()
    {
        var cases = new (string Name, RollbackWorktreeStateEvidence Worktree, string ExpectedReason)[]
        {
            ("changed", ValidWorktree() with { ChangedFilePaths = ["IronDev.Core/Governance/Foo.cs"] }, "DirtyWorktree"),
            ("staged", ValidWorktree() with { StagedFilePaths = ["IronDev.Core/Governance/Foo.cs"] }, "DirtyWorktree"),
            ("untracked", ValidWorktree() with { UntrackedFilePaths = ["IronDev.Core/Governance/Foo.cs"] }, "DirtyWorktree"),
            ("changed-null", ValidWorktree() with { ChangedFilePaths = null! }, "WorktreeStateRequired"),
            ("staged-null", ValidWorktree() with { StagedFilePaths = null! }, "WorktreeStateRequired"),
            ("untracked-null", ValidWorktree() with { UntrackedFilePaths = null! }, "WorktreeStateRequired"),
            ("not-immediate", ValidWorktree() with { IsObservedImmediatelyBeforeRollback = false }, "DirtyWorktree")
        };

        foreach (var item in cases)
        {
            var result = RollbackStatusMapper.Map(ValidRequest() with { Worktree = item.Worktree });

            Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State, item.Name);
            AssertContains(result.Status.BlockedReasons, item.ExpectedReason);
            Assert.IsFalse(result.IsRollbackExecutionAllowed, item.Name);
            AssertValid(result);
        }
    }

    [TestMethod]
    public void BlockBZ_PostStateMismatch_MapsToFailed()
    {
        var cases = new (string Name, RollbackPostStateEvidence PostState)[]
        {
            ("not-observed", ValidPostState() with { IsObservedAfterRollback = false }),
            ("mismatch", ValidPostState() with { MatchesExpectedPostRollbackState = false }),
            ("remaining-changed", ValidPostState() with { RemainingChangedFilePaths = ["IronDev.Core/Governance/Foo.cs"] }),
            ("remaining-staged", ValidPostState() with { RemainingStagedFilePaths = ["IronDev.Core/Governance/Foo.cs"] }),
            ("remaining-untracked", ValidPostState() with { RemainingUntrackedFilePaths = ["IronDev.Core/Governance/Foo.cs"] }),
            ("collections-null", ValidPostState() with { RemainingChangedFilePaths = null! })
        };

        foreach (var item in cases)
        {
            var result = RollbackStatusMapper.Map(ValidRequest() with { PostState = item.PostState });

            Assert.AreEqual(GovernedOperationState.Failed, result.Status.State, item.Name);
            Assert.AreEqual(0, result.Status.BlockedReasons.Count, item.Name);
            Assert.AreEqual(0, result.Status.MissingEvidence.Count, item.Name);
            AssertContains(result.Status.ForbiddenActions, "do not continue workflow after rollback mismatch");
            AssertContains(result.Status.ForbiddenActions, "do not treat failed rollback as stable");
            Assert.IsFalse(result.IsRollbackExecutionAllowed, item.Name);
            Assert.IsFalse(result.IsRollbackExecuted, item.Name);
            AssertValid(result);
        }
    }

    [TestMethod]
    public void BlockBZ_MatchingPostState_DoesNotInventRollbackCompletion()
    {
        var result = RollbackStatusMapper.Map(ValidRequest() with { PostState = ValidPostState() });

        Assert.AreEqual(GovernedOperationState.Eligible, result.Status.State);
        Assert.IsTrue(result.IsRollbackExecutionAllowed);
        Assert.IsFalse(result.IsRollbackExecuted);
        Assert.IsFalse(result.Status.ReceiptRefs.Any(value => value.Contains("rollback-execution", StringComparison.OrdinalIgnoreCase)));
        AssertValid(result);
    }

    [TestMethod]
    public void BlockBZ_NonRollbackAuthority_MapsToBlocked()
    {
        foreach (var operationKind in new[]
        {
            RunAuthorityOperationKind.SourceApply,
            RunAuthorityOperationKind.Commit,
            RunAuthorityOperationKind.Push,
            RunAuthorityOperationKind.DraftPullRequest,
            RunAuthorityOperationKind.Merge,
            RunAuthorityOperationKind.Release,
            RunAuthorityOperationKind.Deployment
        })
        {
            var result = RollbackStatusMapper.Map(ValidRequest() with
            {
                Authority = ValidAuthority() with { Decision = EligibleDecision(operationKind) }
            });

            Assert.AreEqual(GovernedOperationState.Blocked, result.Status.State, operationKind.ToString());
            AssertContains(result.Status.BlockedReasons, "RollbackAuthorityOperationMismatch");
            AssertContains(result.Status.MissingEvidence, "rollback-operation-authority");
            Assert.IsFalse(result.IsRollbackExecutionAllowed);
            AssertValid(result);
        }
    }

    [TestMethod]
    public void BlockBZ_StatusDoesNotExecuteRollback()
    {
        var result = RollbackStatusMapper.Map(ValidRequest());

        Assert.AreEqual(GovernedOperationState.Eligible, result.Status.State);
        Assert.IsTrue(result.IsRollbackExecutionAllowed);
        Assert.IsFalse(result.IsRollbackExecuted);

        var root = FindRepositoryRoot();
        var files = Directory.GetFiles(
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackStatus"),
            "*.cs",
            SearchOption.TopDirectoryOnly);
        var text = string.Join(Environment.NewLine, files.Select(File.ReadAllText));

        foreach (var forbidden in new[]
        {
            "Process.Start",
            "File.Write",
            "Directory.CreateDirectory",
            "git",
            "checkout",
            "reset",
            "revert",
            "ExecuteRollback",
            "CommitExecution",
            "PushExecution",
            "PullRequestExecution",
            "Merge execution",
            "Release execution",
            "Deploy execution",
            "WorkflowContinuation",
            "MemoryPromotion",
            "IGovernanceEventStore"
        })
        {
            Assert.IsFalse(ContainsForbiddenSurface(text, forbidden), forbidden);
        }
    }

    [TestMethod]
    public void BlockBZ_StatusBoundaryFlagsRemainFalse()
    {
        foreach (var result in new[]
        {
            RollbackStatusMapper.Map(ValidRequest()),
            RollbackStatusMapper.Map(ValidRequest() with { Authority = null }),
            RollbackStatusMapper.Map(ValidRequest() with { Worktree = ValidWorktree() with { ChangedFilePaths = ["x.txt"] } }),
            RollbackStatusMapper.Map(ValidRequest() with { PostState = ValidPostState() with { MatchesExpectedPostRollbackState = false } })
        })
        {
            Assert.IsFalse(result.StatusValidation.Boundary.CanExecute);
            Assert.IsFalse(result.StatusValidation.Boundary.CanRollback);
            Assert.IsFalse(result.StatusValidation.Boundary.CanSourceApply);
            Assert.IsFalse(result.StatusValidation.Boundary.CanCommit);
            Assert.IsFalse(result.StatusValidation.Boundary.CanPush);
            Assert.IsFalse(result.StatusValidation.Boundary.CanContinueWorkflow);
            Assert.IsFalse(result.StatusValidation.Boundary.CanMerge);
            Assert.IsFalse(result.StatusValidation.Boundary.CanRelease);
            Assert.IsFalse(result.StatusValidation.Boundary.CanDeploy);
            Assert.IsFalse(result.StatusValidation.Boundary.CanMutateSource);
            AssertValid(result);
        }
    }

    [TestMethod]
    public void BlockBZ_Receipt_RecordsRollbackStatusBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "BZ_ROLLBACK_STATUS_MAPPING.md"));

        StringAssert.Contains(doc, "Rollback plan is not rollback execution.");
        StringAssert.Contains(doc, "Rollback request accepted is not rollback execution.");
        StringAssert.Contains(doc, "Rollback availability is not rollback authority.");
        StringAssert.Contains(doc, "Rollback status is not source mutation.");
        StringAssert.Contains(doc, "Wrong apply receipt blocks rollback.");
        StringAssert.Contains(doc, "Partial rollback risk blocks rollback.");
        StringAssert.Contains(doc, "Dirty worktree blocks rollback.");
        StringAssert.Contains(doc, "Post-state mismatch fails rollback status.");
        StringAssert.Contains(doc, "Do not call the system stable until rollback is boring.");
    }

    private static RollbackStatusEvaluationRequest ValidRequest() =>
        new()
        {
            EvaluationId = "rollback-status-bz-001",
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            PatchHash = PatchHash,
            SourceApplyReceiptRef = SourceApplyReceiptRef,
            Availability = ValidAvailability(),
            Plan = ValidPlan(),
            Authority = ValidAuthority(),
            Request = ValidRollbackRequest(),
            ApplyReceipt = ValidApplyReceipt(),
            Worktree = ValidWorktree(),
            PostState = null,
            ObservedAtUtc = ObservedAtUtc,
            EvidenceRefs = ["rollback-status-evaluation:bz-001"],
            ReceiptRefs = []
        };

    private static RollbackAvailabilityEvidence ValidAvailability() =>
        new()
        {
            EvidenceRef = "rollback-availability:bz-001",
            IsRollbackAvailable = true,
            AvailabilityReason = "rollback support was recorded for the source apply receipt"
        };

    private static RollbackPlanEvidence ValidPlan() =>
        new()
        {
            EvidenceRef = "rollback-plan:bz-001",
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            PatchHash = PatchHash,
            SourceApplyReceiptRef = SourceApplyReceiptRef,
            HasRollbackPlan = true,
            IsPlanBoundToApplyReceipt = true,
            RequiresPartialRollback = false,
            HasPartialRollbackRisk = false,
            PlannedRollbackFilePaths = ["IronDev.Core/Governance/Foo.cs"]
        };

    private static RollbackAuthorityEvidence ValidAuthority() =>
        new()
        {
            EvidenceRef = "rollback-operation-authority:bz-001",
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            PatchHash = PatchHash,
            SourceApplyReceiptRef = SourceApplyReceiptRef,
            Decision = EligibleDecision(RunAuthorityOperationKind.Rollback)
        };

    private static RollbackRequestEvidence ValidRollbackRequest() =>
        new()
        {
            EvidenceRef = "accepted-rollback-request:bz-001",
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            PatchHash = PatchHash,
            SourceApplyReceiptRef = SourceApplyReceiptRef,
            IsRollbackRequestAccepted = true,
            AcceptedAtUtc = ObservedAtUtc.AddMinutes(-1)
        };

    private static RollbackApplyReceiptEvidence ValidApplyReceipt() =>
        new()
        {
            ReceiptRef = SourceApplyReceiptRef,
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            PatchHash = PatchHash,
            IsSourceApplyReceipt = true,
            IsApplyReceiptAcceptedForRollback = true
        };

    private static RollbackWorktreeStateEvidence ValidWorktree() =>
        new()
        {
            EvidenceRef = "rollback-worktree:bz-001",
            Repository = Repository,
            Branch = Branch,
            HeadCommitId = "head-bz-001",
            ChangedFilePaths = [],
            StagedFilePaths = [],
            UntrackedFilePaths = [],
            IsObservedImmediatelyBeforeRollback = true
        };

    private static RollbackPostStateEvidence ValidPostState() =>
        new()
        {
            EvidenceRef = "rollback-post-state:bz-001",
            Repository = Repository,
            Branch = Branch,
            RunId = RunId,
            PatchHash = PatchHash,
            SourceApplyReceiptRef = SourceApplyReceiptRef,
            IsObservedAfterRollback = true,
            MatchesExpectedPostRollbackState = true,
            RemainingChangedFilePaths = [],
            RemainingStagedFilePaths = [],
            RemainingUntrackedFilePaths = []
        };

    private static OperationEligibilityDecision EligibleDecision(RunAuthorityOperationKind operationKind) =>
        new()
        {
            IsEligibleUnderProfileAndGrant = true,
            OperationKind = operationKind,
            BlockedReasons = [],
            MissingEvidence = [],
            ForbiddenActions =
            [
                "do not treat eligibility as approval",
                "do not treat eligibility as policy satisfaction",
                "do not treat eligibility as execution authority"
            ],
            RequiredIndependentChecks =
            [
                "operation-specific governance still required",
                "profile and grant eligibility is necessary but not sufficient"
            ]
        };

    private static void AssertValid(RollbackStatusMappingResult result)
    {
        Assert.IsTrue(result.StatusValidation.IsValid, string.Join(", ", result.StatusValidation.Issues.Concat(result.StatusValidation.RedFlags)));
        Assert.AreEqual(0, result.Issues.Count(issue => issue is "StatusImpliesAuthority" or "NextSafeActionImpliesDirectMutation"));
    }

    private static void AssertContains(IEnumerable<string> values, string expected)
    {
        Assert.IsTrue(
            values.Any(value => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)),
            $"Expected '{expected}' in: {string.Join(", ", values)}");
    }

    private static bool ContainsForbiddenSurface(string text, string forbidden)
    {
        if (forbidden is "git" or "checkout" or "reset" or "revert")
        {
            return text.Split(
                    [' ', '\t', '\r', '\n', '"', '\'', '`', '(', ')', '[', ']', '{', '}', ';', ',', '.', ':'],
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(token => string.Equals(token, forbidden, StringComparison.OrdinalIgnoreCase));
        }

        return text.Contains(forbidden, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }
}
