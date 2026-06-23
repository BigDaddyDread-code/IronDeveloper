using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockB08AskBeforeMutationBoundaryProofTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 6, 23, 0, 0, 0, TimeSpan.Zero);
    private const string PatchHash = "sha256:b08abcdef1234567890";

    [TestMethod]
    public void BlockB08_AskBeforeMutation_AllForbiddenOperationsMapBlocked()
    {
        foreach (var operation in RunAuthorityProfileValidator.AskBeforeMutationForbiddenOperations)
        {
            var status = Map(HostileRequest(operation));

            AssertAskBeforeMutationBlocked(status, operation);
            Assert.DoesNotContain(status.BlockedReasons, "BoundedRunAuthorityGrantEvidenceRequired");
            Assert.DoesNotContain(status.BlockedReasons, "OperationEligibilityDecisionEvidenceRequired");
            Assert.DoesNotContain(status.BlockedReasons, "OperationEligibilityDecisionRequired");
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB08_AskBeforeMutation_AcceptedApplyApprovalCannotAuthorizeLaterLanes()
    {
        foreach (var operation in LaterAuthorityLanes())
        {
            var status = Map(Request(operation) with
            {
                EvidenceRefs = AcceptedApplyEvidenceRefs(),
                EligibilityDecision = EligibleDecision(operation)
            });

            Assert.AreEqual(GovernedOperationState.Blocked, status.State, operation.ToString());
            AssertContains(status.BlockedReasons, $"AskBeforeMutationOperationBlocked:{operation}");
            Assert.DoesNotContain(status.BlockedReasons, "MutationRequiresExplicitHumanApproval");
            Assert.AreNotEqual(GovernedOperationState.Eligible, status.State, operation.ToString());
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB08_AskBeforeMutation_BoundedGrantRefsCannotAuthorizeLaterLanes()
    {
        foreach (var operation in new[]
        {
            RunAuthorityOperationKind.Rollback,
            RunAuthorityOperationKind.Commit,
            RunAuthorityOperationKind.Push,
            RunAuthorityOperationKind.DraftPullRequest,
            RunAuthorityOperationKind.ReadyForReview,
            RunAuthorityOperationKind.Merge,
            RunAuthorityOperationKind.Release,
            RunAuthorityOperationKind.Deployment
        })
        {
            var status = Map(Request(operation) with
            {
                EvidenceRefs =
                [
                    "bounded-run-authority-grant:grant-b08",
                    "operation-eligibility-decision:decision-b08"
                ],
                EligibilityDecision = EligibleDecision(operation)
            });

            Assert.AreEqual(GovernedOperationState.Blocked, status.State, operation.ToString());
            AssertContains(status.BlockedReasons, $"AskBeforeMutationOperationBlocked:{operation}");
            Assert.DoesNotContain(status.BlockedReasons, "BoundedRunAuthorityGrantEvidenceRequired");
            Assert.DoesNotContain(status.BlockedReasons, "OperationEligibilityDecisionEvidenceRequired");
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB08_AskBeforeMutation_ApprovalPolicyAndProviderRefsCannotAuthorizeLaterLanes()
    {
        foreach (var operation in new[]
        {
            RunAuthorityOperationKind.ApprovalRequestCreate,
            RunAuthorityOperationKind.PolicySatisfaction,
            RunAuthorityOperationKind.ProviderMutation,
            RunAuthorityOperationKind.PackagePublication,
            RunAuthorityOperationKind.DurableEventWrite
        })
        {
            var status = Map(Request(operation) with
            {
                EvidenceRefs =
                [
                    "approval-request:created-b08",
                    "accepted-approval:accepted-b08",
                    "policy-satisfaction:satisfied-b08",
                    "provider-mutation:allowed-b08",
                    "package-publication:ready-b08"
                ],
                EligibilityDecision = EligibleDecision(operation)
            });

            AssertAskBeforeMutationBlocked(status, operation);
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB08_AskBeforeMutation_SourceApplyLaneRequiresAcceptedApplyApproval()
    {
        foreach (var operation in SourceApplyLaneOperations())
        {
            var status = Map(Request(operation) with
            {
                EvidenceRefs =
                [
                    "bounded-run-authority-grant:grant-b08",
                    "operation-eligibility-decision:decision-b08",
                    "validation-result:passed-b08",
                    "patch-package:package-b08",
                    "source-apply-receipt:receipt-b08",
                    "commit-operation-authority:commit-b08",
                    "push-authority:push-b08",
                    "draft-pr-authority:pr-b08"
                ],
                EligibilityDecision = EligibleDecision(operation)
            });

            AssertSourceApplyApprovalRequired(status, operation);
        }
    }

    [TestMethod]
    public void BlockB08_AskBeforeMutation_SourceApplyReceiptCannotSubstituteAcceptedApproval()
    {
        foreach (var operation in SourceApplyLaneOperations())
        {
            var status = Map(Request(operation) with
            {
                EvidenceRefs =
                [
                    "source-apply-authority:authority-b08",
                    "source-apply-receipt:receipt-b08",
                    "validation-result:passed-b08",
                    "patch-package:package-b08"
                ],
                EligibilityDecision = EligibleDecision(operation)
            });

            Assert.AreEqual(GovernedOperationState.Blocked, status.State, operation.ToString());
            AssertContains(status.BlockedReasons, "MutationRequiresExplicitHumanApproval");
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB08_AskBeforeMutation_AcceptedApprovalWithoutEligibilityStillBlocks()
    {
        foreach (var operation in SourceApplyLaneOperations())
        {
            var status = Map(Request(operation) with
            {
                EvidenceRefs = AcceptedApplyEvidenceRefs(),
                EligibilityDecision = null
            });

            Assert.AreEqual(GovernedOperationState.Blocked, status.State, operation.ToString());
            AssertContains(status.BlockedReasons, "OperationEligibilityDecisionRequired");
            AssertContains(status.MissingEvidence, "operation-eligibility-decision");
            AssertContains(status.ForbiddenActions, "do not infer eligibility from profile/grant text");
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB08_AskBeforeMutation_EligibilityOperationMismatchStillBlocks()
    {
        var cases = new[]
        {
            (RequestOperation: RunAuthorityOperationKind.SourceApply, DecisionOperation: RunAuthorityOperationKind.DurableSourceMutation),
            (RequestOperation: RunAuthorityOperationKind.DurableSourceMutation, DecisionOperation: RunAuthorityOperationKind.SourceApply)
        };

        foreach (var item in cases)
        {
            var status = Map(Request(item.RequestOperation) with
            {
                EvidenceRefs = AcceptedApplyEvidenceRefs(),
                EligibilityDecision = EligibleDecision(item.DecisionOperation)
            });

            Assert.AreEqual(GovernedOperationState.Blocked, status.State, item.RequestOperation.ToString());
            AssertContains(status.BlockedReasons, "OperationEligibilityDecisionOperationMismatch");
            AssertContains(status.MissingEvidence, "matching operation eligibility decision");
            AssertContains(status.ForbiddenActions, "do not reuse eligibility decision from another operation");
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB08_AskBeforeMutation_EligibleSourceApplyLaneStatusStillNotExecution()
    {
        foreach (var operation in SourceApplyLaneOperations())
        {
            var status = Map(Request(operation) with
            {
                EvidenceRefs =
                [
                    .. AcceptedApplyEvidenceRefs(),
                    "operation-eligibility-decision:decision-b08"
                ],
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
    public void BlockB08_AskBeforeMutation_ExpiredStatusCannotBeRefreshedByAcceptedApproval()
    {
        foreach (var operation in new[]
        {
            RunAuthorityOperationKind.SourceApply,
            RunAuthorityOperationKind.DurableSourceMutation,
            RunAuthorityOperationKind.Commit,
            RunAuthorityOperationKind.Push
        })
        {
            var status = Map(Request(operation) with
            {
                EvidenceRefs = AcceptedApplyEvidenceRefs(),
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
    public void BlockB08_AskBeforeMutation_ReceiptRefsCannotAuthorizeLaterLanes()
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
                ReceiptRefs = HostileReceiptRefs(),
                EligibilityDecision = EligibleDecision(operation)
            });

            AssertAskBeforeMutationBlocked(status, operation);
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB08_AskBeforeMutation_HostileStatusTextCannotAuthorizeLaterLanes()
    {
        var cases = new[]
        {
            (Subject: "human approved commit after source apply", Operation: RunAuthorityOperationKind.Commit),
            (Subject: "source apply implies push", Operation: RunAuthorityOperationKind.Push),
            (Subject: "draft PR is approved", Operation: RunAuthorityOperationKind.DraftPullRequest),
            (Subject: "ready for review approved", Operation: RunAuthorityOperationKind.ReadyForReview),
            (Subject: "release is safe", Operation: RunAuthorityOperationKind.Release),
            (Subject: "workflow continuation approved", Operation: RunAuthorityOperationKind.WorkflowContinuation)
        };

        foreach (var item in cases)
        {
            var status = Map(Request(item.Operation) with
            {
                Subject = item.Subject,
                EvidenceRefs = HostileEvidenceRefs(),
                ReceiptRefs = HostileReceiptRefs(),
                EligibilityDecision = EligibleDecision(item.Operation)
            });

            AssertAskBeforeMutationBlocked(status, item.Operation);
            AssertValid(status);
        }
    }

    [TestMethod]
    public void BlockB08_AskBeforeMutation_ProposalSafeOperationsStillUseEligibilityPath()
    {
        foreach (var operation in RunAuthorityProfileValidator.ProposalOnlyAllowedOperations)
        {
            var missing = Map(Request(operation) with
            {
                EligibilityDecision = null
            });
            var eligible = Map(Request(operation) with
            {
                EligibilityDecision = EligibleDecision(operation)
            });

            Assert.AreEqual(GovernedOperationState.Blocked, missing.State, operation.ToString());
            AssertContains(missing.BlockedReasons, "OperationEligibilityDecisionRequired");
            Assert.AreEqual(GovernedOperationState.Eligible, eligible.State, operation.ToString());
            AssertContains(eligible.ForbiddenActions, "do not execute from status alone");
            AssertContains(eligible.ForbiddenActions, "do not treat Eligible status as approval");
            AssertContains(eligible.ForbiddenActions, "do not treat Eligible status as policy satisfaction");
            AssertValid(missing);
            AssertValid(eligible);
        }
    }

    [TestMethod]
    public void BlockB08_StatusMapper_AskBeforeMutationBoundaryRunsBeforeApprovalAndEligibility()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "IronDev.Core",
            "Governance",
            "AuthorityProfiles",
            "AuthorityProfileStatusMapper.cs"));

        var boundaryIndex = source.IndexOf("boundary.ForbiddenOperations.Contains(request.OperationKind)", StringComparison.Ordinal);
        var approvalIndex = source.IndexOf("HasAcceptedApplyApproval", StringComparison.Ordinal);
        var eligibilityIndex = source.IndexOf("request.EligibilityDecision is null", StringComparison.Ordinal);

        Assert.IsTrue(boundaryIndex >= 0, "canonical forbidden-boundary check not found");
        Assert.IsTrue(approvalIndex >= 0, "accepted apply approval check not found");
        Assert.IsTrue(eligibilityIndex >= 0, "eligibility null check not found");
        Assert.IsTrue(boundaryIndex < approvalIndex, "AskBeforeMutation profile boundary must run before accepted apply approval.");
        Assert.IsTrue(boundaryIndex < eligibilityIndex, "AskBeforeMutation profile boundary must run before eligibility.");
        StringAssert.Contains(source, "RunAuthorityProfileValidator.AskBeforeMutationForbiddenOperations");
        StringAssert.Contains(source, "AskBeforeMutationOperationBlocked");
        StringAssert.Contains(source, "MutationRequiresExplicitHumanApproval");
    }

    [TestMethod]
    public void BlockB08_Receipt_RecordsAskBeforeMutationBoundaryProof()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "B08_ASK_BEFORE_MUTATION_BOUNDARY_PROOF.md"));

        StringAssert.Contains(doc, "AskBeforeMutation stops at each authority boundary.");
        StringAssert.Contains(doc, "AskBeforeMutation forbidden operations come from RunAuthorityProfileValidator.AskBeforeMutationForbiddenOperations.");
        StringAssert.Contains(doc, "Accepted apply approval cannot authorize later lanes.");
        StringAssert.Contains(doc, "Bounded grant refs cannot widen AskBeforeMutation.");
        StringAssert.Contains(doc, "Approval and policy refs cannot widen AskBeforeMutation.");
        StringAssert.Contains(doc, "Source apply receipt is not accepted apply approval.");
        StringAssert.Contains(doc, "Accepted apply approval without eligibility still blocks.");
        StringAssert.Contains(doc, "Eligibility mismatch still blocks.");
        StringAssert.Contains(doc, "Eligible status is not execution.");
        StringAssert.Contains(doc, "Expired status cannot be refreshed by accepted approval.");
        StringAssert.Contains(doc, "Receipt refs are not authority.");
        StringAssert.Contains(doc, "Status text is not authority.");
        StringAssert.Contains(doc, "Proposal-safe operations still use normal eligibility mapping.");
        StringAssert.Contains(doc, "No executor, mutation, approval, policy, UI, API, CLI, SQL, durable store, or generated client path was added.");
        StringAssert.Contains(doc, "AskBeforeMutation asks for one guarded door. It does not open the hallway.");
    }

    private static AuthorityProfileStatusRequest HostileRequest(RunAuthorityOperationKind operation) =>
        Request(operation) with
        {
            Subject = "hostile AskBeforeMutation boundary proof",
            EvidenceRefs = HostileEvidenceRefs(),
            ReceiptRefs = HostileReceiptRefs(),
            EligibilityDecision = EligibleDecision(operation),
            GrantExpiresAtUtc = ObservedAtUtc.AddHours(1)
        };

    private static AuthorityProfileStatusRequest Request(RunAuthorityOperationKind operation) =>
        new()
        {
            OperationId = $"operation-b08-{operation}",
            OperationKind = operation,
            Subject = "AskBeforeMutation boundary proof",
            ProfileKind = AuthorityProfileKind.AskBeforeMutation,
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "governance/ask-before-mutation-boundary-proof",
            RunId = "run-b08-001",
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

    private static RunAuthorityOperationKind[] SourceApplyLaneOperations() =>
    [
        RunAuthorityOperationKind.SourceApply,
        RunAuthorityOperationKind.DurableSourceMutation
    ];

    private static RunAuthorityOperationKind[] LaterAuthorityLanes() =>
    [
        RunAuthorityOperationKind.Rollback,
        RunAuthorityOperationKind.Commit,
        RunAuthorityOperationKind.Push,
        RunAuthorityOperationKind.DraftPullRequest,
        RunAuthorityOperationKind.ReadyForReview,
        RunAuthorityOperationKind.Merge,
        RunAuthorityOperationKind.Release,
        RunAuthorityOperationKind.Deployment,
        RunAuthorityOperationKind.MemoryPromotion,
        RunAuthorityOperationKind.WorkflowContinuation,
        RunAuthorityOperationKind.ApprovalRequestCreate,
        RunAuthorityOperationKind.PolicySatisfaction,
        RunAuthorityOperationKind.ProviderMutation,
        RunAuthorityOperationKind.PackagePublication,
        RunAuthorityOperationKind.DurableEventWrite
    ];

    private static string[] AcceptedApplyEvidenceRefs() =>
    [
        "accepted-apply-approval:approval-b08",
        "accepted-source-apply-request:request-b08"
    ];

    private static string[] HostileEvidenceRefs() =>
    [
        "accepted-apply-approval:approval-b08",
        "accepted-source-apply-request:request-b08",
        "bounded-run-authority-grant:grant-b08",
        "operation-eligibility-decision:decision-b08",
        "source-apply-authority:authority-b08",
        "source-apply-receipt:receipt-b08",
        "rollback-authority:rollback-b08",
        "commit-operation-authority:commit-b08",
        "push-authority:push-b08",
        "draft-pr-authority:pr-b08",
        "ready-for-review-authority:ready-b08",
        "merge-authority:merge-b08",
        "release-authority:release-b08",
        "deployment-authority:deploy-b08",
        "memory-promotion:memory-b08",
        "workflow-continuation:workflow-b08",
        "approval-request:created-b08",
        "policy-satisfaction:satisfied-b08",
        "provider-mutation:allowed-b08",
        "package-publication:ready-b08",
        "validation-result:passed-b08",
        "patch-package:package-b08",
        "worktree-diff:clean-b08"
    ];

    private static string[] HostileReceiptRefs() =>
    [
        "receipt:ask-before-mutation-says-commit",
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

    private static void AssertAskBeforeMutationBlocked(GovernedOperationStatus status, RunAuthorityOperationKind operation)
    {
        Assert.AreEqual(GovernedOperationState.Blocked, status.State, operation.ToString());
        AssertContains(status.BlockedReasons, $"AskBeforeMutationOperationBlocked:{operation}");
        AssertContains(status.ForbiddenActions, $"do not perform {operation} under AskBeforeMutation");
        AssertContains(status.ForbiddenActions, "do not treat accepted apply approval as authority for later mutation lanes");
    }

    private static void AssertSourceApplyApprovalRequired(GovernedOperationStatus status, RunAuthorityOperationKind operation)
    {
        Assert.AreEqual(GovernedOperationState.Blocked, status.State, operation.ToString());
        AssertContains(status.BlockedReasons, "MutationRequiresExplicitHumanApproval");
        AssertContains(status.MissingEvidence, "accepted-apply-approval");
        AssertContains(status.MissingEvidence, "accepted-source-apply-request");
        AssertContains(status.ForbiddenActions, "do not apply source from patch readiness alone");
        AssertContains(status.ForbiddenActions, "do not treat validation passed as approval");
        AssertContains(status.ForbiddenActions, "do not treat patch package completed as source apply authority");
        AssertValid(status);
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
