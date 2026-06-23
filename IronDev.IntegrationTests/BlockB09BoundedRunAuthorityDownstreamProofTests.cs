using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockB09BoundedRunAuthorityDownstreamProofTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 6, 23, 0, 0, 0, TimeSpan.Zero);
    private const string PatchHash = "sha256:b09abcdef1234567890";

    [TestMethod]
    public void BlockB09_BoundedRunAuthority_ForbiddenOperationsAlwaysMapBlocked()
    {
        foreach (var operation in RunAuthorityProfileValidator.BoundedRunAuthorityForbiddenOperations)
        {
            var status = Map(HostileRequest(operation) with
            {
                EligibilityDecision = EligibleDecision(operation)
            });

            AssertBoundedProfileBlocked(status, operation);
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB09_BoundedRunAuthority_EligibilityDecisionCannotBeReusedForDownstreamOperation()
    {
        var allowedCases = new[]
        {
            (RequestOperation: RunAuthorityOperationKind.Commit, DecisionOperation: RunAuthorityOperationKind.SourceApply),
            (RequestOperation: RunAuthorityOperationKind.Push, DecisionOperation: RunAuthorityOperationKind.Commit),
            (RequestOperation: RunAuthorityOperationKind.DraftPullRequest, DecisionOperation: RunAuthorityOperationKind.Push)
        };

        foreach (var item in allowedCases)
        {
            var status = Map(Request(item.RequestOperation) with
            {
                EvidenceRefs = BoundedEvidenceRefs(),
                ReceiptRefs = HostileReceiptRefs(),
                EligibilityDecision = EligibleDecision(item.DecisionOperation)
            });

            AssertOperationMismatch(status, item.RequestOperation);
        }

        var forbidden = Map(Request(RunAuthorityOperationKind.ReadyForReview) with
        {
            EvidenceRefs = BoundedEvidenceRefs(),
            ReceiptRefs = HostileReceiptRefs(),
            EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.DraftPullRequest)
        });

        AssertBoundedProfileBlocked(forbidden, RunAuthorityOperationKind.ReadyForReview);
        AssertValid(forbidden);
    }

    [TestMethod]
    public void BlockB09_SourceApplyAuthorityCannotAuthorizeCommitPushPrOrLater()
    {
        foreach (var operation in new[]
        {
            RunAuthorityOperationKind.Commit,
            RunAuthorityOperationKind.Push,
            RunAuthorityOperationKind.DraftPullRequest,
            RunAuthorityOperationKind.ReadyForReview,
            RunAuthorityOperationKind.Merge,
            RunAuthorityOperationKind.Release,
            RunAuthorityOperationKind.Deployment,
            RunAuthorityOperationKind.WorkflowContinuation
        })
        {
            var status = Map(Request(operation) with
            {
                EvidenceRefs = [.. BoundedEvidenceRefs(), .. SourceApplyEvidenceRefs()],
                EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.SourceApply)
            });

            AssertNotEligible(status, operation);
            AssertDownstreamBlockedByMismatchOrBoundary(status, operation);
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB09_CommitAuthorityCannotAuthorizePushPrOrLater()
    {
        foreach (var operation in new[]
        {
            RunAuthorityOperationKind.Push,
            RunAuthorityOperationKind.DraftPullRequest,
            RunAuthorityOperationKind.ReadyForReview,
            RunAuthorityOperationKind.Merge,
            RunAuthorityOperationKind.Release,
            RunAuthorityOperationKind.Deployment,
            RunAuthorityOperationKind.WorkflowContinuation
        })
        {
            var status = Map(Request(operation) with
            {
                EvidenceRefs = [.. BoundedEvidenceRefs(), .. CommitEvidenceRefs()],
                EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.Commit)
            });

            AssertNotEligible(status, operation);
            AssertDownstreamBlockedByMismatchOrBoundary(status, operation);
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB09_PushAuthorityCannotAuthorizePrReviewOrLater()
    {
        foreach (var operation in new[]
        {
            RunAuthorityOperationKind.DraftPullRequest,
            RunAuthorityOperationKind.ReadyForReview,
            RunAuthorityOperationKind.Merge,
            RunAuthorityOperationKind.Release,
            RunAuthorityOperationKind.Deployment,
            RunAuthorityOperationKind.WorkflowContinuation
        })
        {
            var status = Map(Request(operation) with
            {
                EvidenceRefs = [.. BoundedEvidenceRefs(), .. PushEvidenceRefs()],
                EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.Push)
            });

            AssertNotEligible(status, operation);
            AssertDownstreamBlockedByMismatchOrBoundary(status, operation);
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB09_DraftPrAuthorityCannotAuthorizeReadyReviewMergeReleaseOrWorkflow()
    {
        foreach (var operation in new[]
        {
            RunAuthorityOperationKind.ReadyForReview,
            RunAuthorityOperationKind.Merge,
            RunAuthorityOperationKind.Release,
            RunAuthorityOperationKind.Deployment,
            RunAuthorityOperationKind.MemoryPromotion,
            RunAuthorityOperationKind.WorkflowContinuation
        })
        {
            var status = Map(Request(operation) with
            {
                EvidenceRefs = [.. BoundedEvidenceRefs(), .. DraftPullRequestEvidenceRefs()],
                EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.DraftPullRequest)
            });

            Assert.AreEqual(GovernedOperationState.Blocked, status.State, operation.ToString());
            AssertContains(status.BlockedReasons, $"BoundedRunAuthorityOperationBlocked:{operation}");
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB09_MatchingBoundedLaneEligibilityStillNotExecution()
    {
        foreach (var operation in BoundedMutationLanes())
        {
            var status = Map(Request(operation) with
            {
                EvidenceRefs = BoundedEvidenceRefs(),
                EligibilityDecision = EligibleDecision(operation),
                GrantExpiresAtUtc = ObservedAtUtc.AddHours(1)
            });

            Assert.AreEqual(GovernedOperationState.Eligible, status.State, operation.ToString());
            AssertContains(status.ForbiddenActions, "do not execute from status alone");
            AssertContains(status.ForbiddenActions, "do not treat Eligible status as approval");
            AssertContains(status.ForbiddenActions, "do not treat Eligible status as policy satisfaction");
            AssertContains(status.ForbiddenActions, "do not apply source from status alone");
            AssertContains(status.ForbiddenActions, "executor must independently re-check profile/grant/scope/patch hash/validation/mutation budget/worktree state");
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB09_BoundedLaneStillRequiresVisibleBoundedEvidenceRefs()
    {
        foreach (var operation in new[]
        {
            RunAuthorityOperationKind.Commit,
            RunAuthorityOperationKind.Push,
            RunAuthorityOperationKind.DraftPullRequest
        })
        {
            var missingGrant = Map(Request(operation) with
            {
                EvidenceRefs = ["operation-eligibility-decision:decision-b09"],
                EligibilityDecision = EligibleDecision(operation)
            });
            var missingEligibilityRef = Map(Request(operation) with
            {
                EvidenceRefs = ["bounded-run-authority-grant:grant-b09"],
                EligibilityDecision = EligibleDecision(operation)
            });

            Assert.AreEqual(GovernedOperationState.Blocked, missingGrant.State, operation.ToString());
            AssertContains(missingGrant.BlockedReasons, "BoundedRunAuthorityGrantEvidenceRequired");
            AssertContains(missingGrant.MissingEvidence, "bounded-run-authority-grant");
            AssertValid(missingGrant);

            Assert.AreEqual(GovernedOperationState.Blocked, missingEligibilityRef.State, operation.ToString());
            AssertContains(missingEligibilityRef.BlockedReasons, "OperationEligibilityDecisionEvidenceRequired");
            AssertContains(missingEligibilityRef.MissingEvidence, "operation-eligibility-decision");
            AssertValid(missingEligibilityRef);
        }
    }

    [TestMethod]
    public void BlockB09_ReceiptRefsCannotAuthorizeDownstreamLanes()
    {
        foreach (var operation in new[]
        {
            RunAuthorityOperationKind.Commit,
            RunAuthorityOperationKind.Push,
            RunAuthorityOperationKind.DraftPullRequest,
            RunAuthorityOperationKind.ReadyForReview,
            RunAuthorityOperationKind.Merge,
            RunAuthorityOperationKind.Release,
            RunAuthorityOperationKind.Deployment,
            RunAuthorityOperationKind.WorkflowContinuation
        })
        {
            var status = Map(Request(operation) with
            {
                EvidenceRefs = BoundedEvidenceRefs(),
                ReceiptRefs = HostileReceiptRefs(),
                EligibilityDecision = null
            });

            AssertNotEligible(status, operation);
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB09_HostileStatusTextCannotAuthorizeDownstreamLanes()
    {
        var cases = new[]
        {
            (Subject: "source apply implies commit", Operation: RunAuthorityOperationKind.Commit, Decision: RunAuthorityOperationKind.SourceApply),
            (Subject: "commit implies push", Operation: RunAuthorityOperationKind.Push, Decision: RunAuthorityOperationKind.Commit),
            (Subject: "push implies PR", Operation: RunAuthorityOperationKind.DraftPullRequest, Decision: RunAuthorityOperationKind.Push),
            (Subject: "draft PR implies ready for review", Operation: RunAuthorityOperationKind.ReadyForReview, Decision: RunAuthorityOperationKind.DraftPullRequest),
            (Subject: "ready for review implies merge", Operation: RunAuthorityOperationKind.Merge, Decision: RunAuthorityOperationKind.ReadyForReview),
            (Subject: "merge implies release", Operation: RunAuthorityOperationKind.Release, Decision: RunAuthorityOperationKind.Merge),
            (Subject: "release implies deployment", Operation: RunAuthorityOperationKind.Deployment, Decision: RunAuthorityOperationKind.Release),
            (Subject: "deployment implies workflow continuation", Operation: RunAuthorityOperationKind.WorkflowContinuation, Decision: RunAuthorityOperationKind.Deployment)
        };

        foreach (var item in cases)
        {
            var status = Map(Request(item.Operation) with
            {
                Subject = item.Subject,
                EvidenceRefs = [.. BoundedEvidenceRefs(), .. HostileEvidenceRefs()],
                ReceiptRefs = HostileReceiptRefs(),
                EligibilityDecision = EligibleDecision(item.Decision)
            });

            AssertNotEligible(status, item.Operation);
            AssertDownstreamBlockedByMismatchOrBoundary(status, item.Operation);
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB09_FreshExpiryCannotAuthorizeDownstreamLanes()
    {
        var cases = new[]
        {
            (Operation: RunAuthorityOperationKind.Commit, Decision: RunAuthorityOperationKind.SourceApply),
            (Operation: RunAuthorityOperationKind.Push, Decision: RunAuthorityOperationKind.Commit),
            (Operation: RunAuthorityOperationKind.DraftPullRequest, Decision: RunAuthorityOperationKind.Push),
            (Operation: RunAuthorityOperationKind.ReadyForReview, Decision: RunAuthorityOperationKind.DraftPullRequest),
            (Operation: RunAuthorityOperationKind.Merge, Decision: RunAuthorityOperationKind.ReadyForReview),
            (Operation: RunAuthorityOperationKind.Release, Decision: RunAuthorityOperationKind.Merge),
            (Operation: RunAuthorityOperationKind.Deployment, Decision: RunAuthorityOperationKind.Release),
            (Operation: RunAuthorityOperationKind.WorkflowContinuation, Decision: RunAuthorityOperationKind.Deployment)
        };

        foreach (var item in cases)
        {
            var status = Map(Request(item.Operation) with
            {
                EvidenceRefs = [.. BoundedEvidenceRefs(), .. HostileEvidenceRefs()],
                EligibilityDecision = EligibleDecision(item.Decision),
                GrantExpiresAtUtc = ObservedAtUtc.AddDays(1)
            });

            AssertNotEligible(status, item.Operation);
            Assert.DoesNotContain(status.BlockedReasons, "BoundedRunGrantExpired");
            AssertDownstreamBlockedByMismatchOrBoundary(status, item.Operation);
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB09_ExpiredStatusCannotBeRefreshedByDownstreamEvidence()
    {
        foreach (var operation in new[]
        {
            RunAuthorityOperationKind.Commit,
            RunAuthorityOperationKind.Push,
            RunAuthorityOperationKind.DraftPullRequest,
            RunAuthorityOperationKind.ReadyForReview
        })
        {
            var status = Map(Request(operation) with
            {
                EvidenceRefs = HostileEvidenceRefs(),
                EligibilityDecision = EligibleDecision(operation),
                GrantExpiresAtUtc = ObservedAtUtc
            });

            Assert.AreEqual(GovernedOperationState.Expired, status.State, operation.ToString());
            AssertContains(status.BlockedReasons, "BoundedRunGrantExpired");
            AssertContains(status.ForbiddenActions, "do not use expired grant");
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB09_StatusMapper_BoundedBoundaryRunsBeforeEligibility()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "IronDev.Core",
            "Governance",
            "AuthorityProfiles",
            "AuthorityProfileStatusMapper.cs"));

        var boundaryIndex = source.IndexOf("boundary.ForbiddenOperations.Contains(request.OperationKind)", StringComparison.Ordinal);
        var eligibilityIndex = source.IndexOf("request.EligibilityDecision is null", StringComparison.Ordinal);

        Assert.IsTrue(boundaryIndex >= 0, "canonical forbidden-boundary check not found");
        Assert.IsTrue(eligibilityIndex >= 0, "eligibility null check not found");
        Assert.IsTrue(boundaryIndex < eligibilityIndex, "BoundedRunAuthority profile boundary must run before eligibility.");
        StringAssert.Contains(source, "RunAuthorityProfileValidator.BoundedRunAuthorityForbiddenOperations");
        StringAssert.Contains(source, "BoundedRunAuthorityOperationBlocked");
        StringAssert.Contains(source, "do not treat bounded profile allowance as later-stage authority");
    }

    [TestMethod]
    public void BlockB09_Receipt_RecordsBoundedAuthorityDownstreamProof()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "B09_BOUNDED_AUTHORITY_DOWNSTREAM_PROOF.md"));

        StringAssert.Contains(doc, "BoundedRunAuthority does not imply downstream authority.");
        StringAssert.Contains(doc, "A source apply decision is not commit authority.");
        StringAssert.Contains(doc, "A commit decision is not push authority.");
        StringAssert.Contains(doc, "A push decision is not draft PR authority.");
        StringAssert.Contains(doc, "A draft PR decision is not ready-for-review authority.");
        StringAssert.Contains(doc, "Ready-for-review remains outside BoundedRunAuthority.");
        StringAssert.Contains(doc, "Merge, release, deployment, memory promotion, and workflow continuation remain outside BoundedRunAuthority.");
        StringAssert.Contains(doc, "Eligibility operation must match the requested operation.");
        StringAssert.Contains(doc, "Evidence refs are not downstream authority.");
        StringAssert.Contains(doc, "Receipt refs are not downstream authority.");
        StringAssert.Contains(doc, "Fresh expiry is not downstream authority.");
        StringAssert.Contains(doc, "Eligible status is not execution.");
        StringAssert.Contains(doc, "Executor re-check remains separate.");
        StringAssert.Contains(doc, "No executor, mutation, approval, policy, UI, API, CLI, SQL, durable store, or generated client path was added.");
        StringAssert.Contains(doc, "A bounded lane ends where the next authority boundary begins.");
    }

    private static AuthorityProfileStatusRequest HostileRequest(RunAuthorityOperationKind operation) =>
        Request(operation) with
        {
            Subject = "hostile BoundedRunAuthority downstream proof",
            EvidenceRefs = HostileEvidenceRefs(),
            ReceiptRefs = HostileReceiptRefs(),
            EligibilityDecision = EligibleDecision(operation),
            GrantExpiresAtUtc = ObservedAtUtc.AddHours(1)
        };

    private static AuthorityProfileStatusRequest Request(RunAuthorityOperationKind operation) =>
        new()
        {
            OperationId = $"operation-b09-{operation}",
            OperationKind = operation,
            Subject = "BoundedRunAuthority downstream proof",
            ProfileKind = AuthorityProfileKind.BoundedRunAuthority,
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "governance/bounded-authority-downstream-proof",
            RunId = "run-b09-001",
            PatchHash = PatchHash,
            ObservedAtUtc = ObservedAtUtc,
            EligibilityDecision = EligibleDecision(operation),
            GrantExpiresAtUtc = ObservedAtUtc.AddHours(1),
            EvidenceRefs = [],
            ReceiptRefs = []
        };

    private static OperationEligibilityDecision EligibleDecision(RunAuthorityOperationKind operation) =>
        new()
        {
            IsEligibleUnderProfileAndGrant = true,
            OperationKind = operation,
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

    private static RunAuthorityOperationKind[] BoundedMutationLanes() =>
    [
        RunAuthorityOperationKind.SourceApply,
        RunAuthorityOperationKind.DurableSourceMutation,
        RunAuthorityOperationKind.Rollback,
        RunAuthorityOperationKind.Commit,
        RunAuthorityOperationKind.Push,
        RunAuthorityOperationKind.DraftPullRequest
    ];

    private static string[] BoundedEvidenceRefs() =>
    [
        "bounded-run-authority-grant:grant-b09",
        "operation-eligibility-decision:decision-b09"
    ];

    private static string[] SourceApplyEvidenceRefs() =>
    [
        "source-apply-authority:authority-b09",
        "source-apply-receipt:receipt-b09",
        "accepted-source-apply-request:request-b09",
        "accepted-apply-approval:approval-b09"
    ];

    private static string[] CommitEvidenceRefs() =>
    [
        "commit-operation-authority:commit-b09",
        "commit-package:package-b09",
        "commit-created:commit-b09"
    ];

    private static string[] PushEvidenceRefs() =>
    [
        "push-authority:push-b09",
        "push-receipt:push-b09",
        "remote-branch-updated:push-b09"
    ];

    private static string[] DraftPullRequestEvidenceRefs() =>
    [
        "draft-pr-authority:pr-b09",
        "draft-pr-created:pr-b09",
        "pull-request:url-b09"
    ];

    private static string[] HostileEvidenceRefs() =>
    [
        "bounded-run-authority-grant:grant-b09",
        "operation-eligibility-decision:decision-b09",
        "accepted-apply-approval:approval-b09",
        "accepted-source-apply-request:request-b09",
        "source-apply-authority:authority-b09",
        "source-apply-receipt:receipt-b09",
        "rollback-authority:rollback-b09",
        "rollback-receipt:rollback-b09",
        "commit-operation-authority:commit-b09",
        "commit-package:package-b09",
        "commit-created:commit-b09",
        "push-authority:push-b09",
        "push-receipt:push-b09",
        "remote-branch-updated:push-b09",
        "draft-pr-authority:pr-b09",
        "draft-pr-created:pr-b09",
        "pull-request:url-b09",
        "ready-for-review-authority:ready-b09",
        "ready-for-review:complete-b09",
        "merge-authority:merge-b09",
        "merge-receipt:merge-b09",
        "release-authority:release-b09",
        "release-candidate:rc-b09",
        "deployment-authority:deploy-b09",
        "memory-promotion:memory-b09",
        "workflow-continuation:workflow-b09",
        "approval-request:created-b09",
        "policy-satisfaction:satisfied-b09",
        "provider-mutation:allowed-b09",
        "package-publication:ready-b09",
        "validation-result:passed-b09",
        "patch-package:package-b09",
        "worktree-diff:clean-b09"
    ];

    private static string[] HostileReceiptRefs() =>
    [
        "receipt:bounded-run-authority-says-all-lanes",
        "receipt:source-apply-complete",
        "receipt:rollback-ready",
        "receipt:commit-ready",
        "receipt:push-ready",
        "receipt:pr-ready",
        "receipt:ready-for-review-approved",
        "receipt:merge-ready",
        "receipt:release-ready",
        "receipt:deployment-ready",
        "receipt:memory-promotion-approved",
        "receipt:workflow-continuation-approved"
    ];

    private static void AssertBoundedProfileBlocked(GovernedOperationStatus status, RunAuthorityOperationKind operation)
    {
        Assert.AreEqual(GovernedOperationState.Blocked, status.State, operation.ToString());
        AssertContains(status.BlockedReasons, $"BoundedRunAuthorityOperationBlocked:{operation}");
        AssertContains(status.ForbiddenActions, $"do not perform {operation} under BoundedRunAuthority");
        AssertContains(status.ForbiddenActions, "do not treat bounded profile allowance as later-stage authority");
    }

    private static void AssertOperationMismatch(GovernedOperationStatus status, RunAuthorityOperationKind operation)
    {
        Assert.AreEqual(GovernedOperationState.Blocked, status.State, operation.ToString());
        AssertContains(status.BlockedReasons, "OperationEligibilityDecisionOperationMismatch");
        AssertContains(status.MissingEvidence, "matching operation eligibility decision");
        AssertContains(status.ForbiddenActions, "do not reuse eligibility decision from another operation");
        AssertValid(status);
    }

    private static void AssertDownstreamBlockedByMismatchOrBoundary(GovernedOperationStatus status, RunAuthorityOperationKind operation)
    {
        if (RunAuthorityProfileValidator.BoundedRunAuthorityForbiddenOperations.Contains(operation))
        {
            AssertBoundedProfileBlocked(status, operation);
            return;
        }

        AssertOperationMismatch(status, operation);
    }

    private static void AssertNotEligible(GovernedOperationStatus status, RunAuthorityOperationKind operation)
    {
        Assert.AreNotEqual(GovernedOperationState.Eligible, status.State, operation.ToString());
    }

    private static GovernedOperationStatus Map(AuthorityProfileStatusRequest request) =>
        AuthorityProfileStatusMapper.Map(request);

    private static void AssertValid(GovernedOperationStatus status)
    {
        var validation = GovernedOperationStatusValidator.Validate(status);
        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues.Concat(validation.RedFlags)));
    }

    private static void AssertContains(IEnumerable<string> values, string expected)
    {
        Assert.IsTrue(
            values.Any(value => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)),
            $"Expected '{expected}' in: {string.Join(", ", values)}");
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
